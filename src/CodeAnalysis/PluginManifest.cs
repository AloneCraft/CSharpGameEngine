using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// プラグインのマニフェスト<c>&lt;Name&gt;.cgeplugin</c>（ADR-0079）。名前、バージョン、対応するエンジンのバージョン、および依存するプラグインを記述する。
/// シーンJSONリーダーで解析する（厳格なRFC 8259準拠。コメントと重複キーは不可）。
/// </summary>
internal sealed class PluginManifest {
    internal const string Extension=".cgeplugin";

    private PluginManifest(string Name,PluginVersion Version,string?EngineVersion,ImmutableArray<string>Dependencies,
        int NameStart,int NameEnd,int EngineVersionStart,int EngineVersionEnd,ImmutableArray<(string Name,int Start,int End)>DependencySpans){
        this.Name=Name;
        this.Version=Version;
        this.EngineVersion=EngineVersion;
        this.Dependencies=Dependencies;
        this.NameStart=NameStart;
        this.NameEnd=NameEnd;
        this.EngineVersionStart=EngineVersionStart;
        this.EngineVersionEnd=EngineVersionEnd;
        this.DependencySpans=DependencySpans;
    }

    internal string Name { get; }
    internal PluginVersion Version { get; }
    /// <summary>エンジンのバージョン範囲（例：<c>&gt;=0.1.16 &lt;0.2.0</c>）。プラグインに記載がない場合はnull。</summary>
    internal string?EngineVersion { get; }
    internal ImmutableArray<string>Dependencies { get; }
    internal int NameStart { get; }
    internal int NameEnd { get; }
    internal int EngineVersionStart { get; }
    internal int EngineVersionEnd { get; }
    internal ImmutableArray<(string Name,int Start,int End)>DependencySpans { get; }

    /// <summary>マニフェストを読み込む。失敗時は問題の内容と文字位置の範囲を添えてnullを返す。</summary>
    internal static PluginManifest?Parse(string Text,out string?Problem,out int ProblemStart,out int ProblemEnd){
        Problem=null;
        ProblemStart=0;
        ProblemEnd=0;
        SceneJsonValue Document;
        try {
            Document=SceneJsonReader.Parse(Text);
        } catch(SceneJsonException Exception){
            Problem=Exception.Message;
            ProblemStart=Exception.Position;
            ProblemEnd=Exception.Position;
            return null;
        }
        if(Document.Kind!=SceneJsonKind.Object){
            Problem="The manifest must be a JSON object.";
            ProblemEnd=Text.Length;
            return null;
        }
        var NameValue=Document.Get("Name");
        if(NameValue is not { Kind:SceneJsonKind.String,Text: { Length:>0 }Name }||!IsIdentifier(Name)){
            Problem="\"Name\" must be a string that is a C# identifier (it names the plugin's folder and Assets.Plugins.<Name>).";
            (ProblemStart,ProblemEnd)=NameValue is null?(Document.Start,Document.End):(NameValue.Start,NameValue.End);
            return null;
        }
        var VersionValue=Document.Get("Version");
        if(VersionValue is not { Kind:SceneJsonKind.String }||!PluginVersion.TryParse(VersionValue.Text,out var Version)){
            Problem="\"Version\" must be a string such as \"1.0.0\".";
            (ProblemStart,ProblemEnd)=VersionValue is null?(Document.Start,Document.End):(VersionValue.Start,VersionValue.End);
            return null;
        }
        string?EngineVersion=null;
        int EngineVersionStart=0,EngineVersionEnd=0;
        if(Document.Get("EngineVersion") is { }EngineValue){
            if(EngineValue.Kind!=SceneJsonKind.String||!PluginVersionRange.TryParse(EngineValue.Text,out _)){
                Problem="\"EngineVersion\" must be a string of constraints such as \">=0.1.16 <0.2.0\".";
                ProblemStart=EngineValue.Start;
                ProblemEnd=EngineValue.End;
                return null;
            }
            EngineVersion=EngineValue.Text;
            EngineVersionStart=EngineValue.Start;
            EngineVersionEnd=EngineValue.End;
        }
        var Dependencies=ImmutableArray.CreateBuilder<(string Name,int Start,int End)>();
        if(Document.Get("Dependencies") is { }DependencyList){
            if(DependencyList.Kind!=SceneJsonKind.Array){
                Problem="\"Dependencies\" must be an array of plugin names.";
                ProblemStart=DependencyList.Start;
                ProblemEnd=DependencyList.End;
                return null;
            }
            foreach(var Item in DependencyList.Items){
                if(Item is not { Kind:SceneJsonKind.String,Text: { Length:>0 }Dependency }){
                    Problem="Each dependency must be a plugin name.";
                    ProblemStart=Item.Start;
                    ProblemEnd=Item.End;
                    return null;
                }
                Dependencies.Add((Dependency,Item.Start,Item.End));
            }
        }
        return new PluginManifest(Name,Version,EngineVersion,[.. Dependencies.Select(static D=>D.Name)],NameValue.Start,NameValue.End,
            EngineVersionStart,EngineVersionEnd,Dependencies.ToImmutable());
    }

    private static bool IsIdentifier(string Value)=>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(Value);
}

/// <summary>
/// 最大4つの数値で表すバージョン（<c>1</c>、<c>1.2</c>、<c>1.2.3</c>、<c>1.2.3.4</c>。省略部分は0）と、任意のプレリリース（<c>0.1.0-dev.50</c>）。
/// 比較は SemVer と同じ：数値が同じなら、プレリリースは正式版より前。プレリリース同士は点で区切った識別子を順に比べ、数字だけの識別子は数として比べる
/// （ADR-0098 §5 規則 4：最初の公開まで、エンジンの版は <c>0.1.0-dev.N</c>）。
/// </summary>
internal readonly struct PluginVersion:IComparable<PluginVersion> {
    private readonly int Major;
    private readonly int Minor;
    private readonly int Patch;
    private readonly int Revision;
    private readonly string?Prerelease;

    private PluginVersion(int Major,int Minor,int Patch,int Revision,string?Prerelease){
        this.Major=Major;
        this.Minor=Minor;
        this.Patch=Patch;
        this.Revision=Revision;
        this.Prerelease=Prerelease;
    }

    internal static bool TryParse(string?Text,out PluginVersion Version){
        Version=default;
        if(string.IsNullOrEmpty(Text)){ return false; }
        var Separator=Text!.IndexOf('-');
        var Core=Separator<0?Text:Text.Substring(0,Separator);
        string?Prerelease=null;
        if(Separator>=0){
            Prerelease=Text.Substring(Separator+1);
            if(Prerelease.Split('.').Any(static Identifier=>Identifier.Length==0||!Identifier.All(static Character=>Character is >='0' and <='9' or >='A' and <='Z' or >='a' and <='z' or '-'))){ return false; }
        }
        var Parts=Core.Split('.');
        if(Parts.Length>4){ return false; }
        var Numbers=new int[4];
        for(var Index=0;Index<Parts.Length;Index++){
            if(Parts[Index].Length==0||!Parts[Index].All(char.IsDigit)||!int.TryParse(Parts[Index],out Numbers[Index])){ return false; }
        }
        Version=new PluginVersion(Numbers[0],Numbers[1],Numbers[2],Numbers[3],Prerelease);
        return true;
    }

    public int CompareTo(PluginVersion Other){
        var Result=this.Major.CompareTo(Other.Major);
        if(Result==0){ Result=this.Minor.CompareTo(Other.Minor); }
        if(Result==0){ Result=this.Patch.CompareTo(Other.Patch); }
        if(Result==0){ Result=this.Revision.CompareTo(Other.Revision); }
        return Result!=0?Result:ComparePrerelease(this.Prerelease,Other.Prerelease);
    }

    private static int ComparePrerelease(string?Left,string?Right){
        if(Left is null||Right is null){ return Left is null?(Right is null?0:1):-1; }
        var LeftParts=Left.Split('.');
        var RightParts=Right.Split('.');
        for(var Index=0;Index<Math.Min(LeftParts.Length,RightParts.Length);Index++){
            var Result=CompareIdentifier(LeftParts[Index],RightParts[Index]);
            if(Result!=0){ return Result; }
        }
        return LeftParts.Length.CompareTo(RightParts.Length);
    }

    /// <summary>数字だけの識別子は数として比べ、英数字の識別子より前に並べる。英数字同士は序数で比べる。</summary>
    private static int CompareIdentifier(string Left,string Right){
        var LeftNumeric=Left.All(char.IsDigit);
        var RightNumeric=Right.All(char.IsDigit);
        if(LeftNumeric!=RightNumeric){ return LeftNumeric?-1:1; }
        if(!LeftNumeric){ return string.CompareOrdinal(Left,Right); }
        var LeftDigits=Left.TrimStart('0');
        var RightDigits=Right.TrimStart('0');
        var Length=LeftDigits.Length.CompareTo(RightDigits.Length);
        return Length!=0?Length:string.CompareOrdinal(LeftDigits,RightDigits);
    }

    public override string ToString()=>(this.Revision!=0?$"{this.Major}.{this.Minor}.{this.Patch}.{this.Revision}":$"{this.Major}.{this.Minor}.{this.Patch}")
        +(this.Prerelease is null?string.Empty:"-"+this.Prerelease);
}

/// <summary>
/// すべて満たす必要がある、スペース区切りの制約。<c>&gt;=</c>、<c>&gt;</c>、<c>&lt;=</c>、<c>&lt;</c>、<c>=</c>、または演算子なしのバージョン（完全一致）。
/// </summary>
internal sealed class PluginVersionRange {
    private readonly ImmutableArray<(string Operator,PluginVersion Version)>Constraints;

    private PluginVersionRange(ImmutableArray<(string Operator,PluginVersion Version)>Constraints){
        this.Constraints=Constraints;
    }

    internal static bool TryParse(string?Text,out PluginVersionRange Range){
        Range=new PluginVersionRange([]);
        if(string.IsNullOrWhiteSpace(Text)){ return false; }
        var Constraints=ImmutableArray.CreateBuilder<(string,PluginVersion)>();
        foreach(var Part in Text!.Split([' '],StringSplitOptions.RemoveEmptyEntries)){
            var Operator=Part.StartsWith(">=",StringComparison.Ordinal)||Part.StartsWith("<=",StringComparison.Ordinal)?Part.Substring(0,2)
                :Part.StartsWith(">",StringComparison.Ordinal)||Part.StartsWith("<",StringComparison.Ordinal)||Part.StartsWith("=",StringComparison.Ordinal)?Part.Substring(0,1)
                :string.Empty;
            if(!PluginVersion.TryParse(Part.Substring(Operator.Length),out var Version)){ return false; }
            Constraints.Add((Operator.Length==0?"=":Operator,Version));
        }
        Range=new PluginVersionRange(Constraints.ToImmutable());
        return true;
    }

    internal bool Contains(PluginVersion Version){
        foreach(var (Operator,Bound) in this.Constraints){
            var Compare=Version.CompareTo(Bound);
            var Holds=Operator switch{
                ">="=>Compare>=0,
                ">"=>Compare>0,
                "<="=>Compare<=0,
                "<"=>Compare<0,
                _=>Compare==0,
            };
            if(!Holds){ return false; }
        }
        return true;
    }
}
