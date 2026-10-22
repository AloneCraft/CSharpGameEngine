using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// ADR-0077 stage 7: <c>Assets/</c> の下のファイルから、型付きのアセット参照 <c>Assets.Sounds.Jump</c> を生成する。
/// ファイルの一覧(AdditionalFiles)だけを使い、中身は読まない。<c>.wav</c> は <c>SoundAsset</c>、<c>.glb</c>/<c>.gltf</c>/<c>.fbx</c> は
/// <c>ModelAsset</c>、<c>.png</c>/<c>.jpg</c> は <c>TextureAsset</c>、<c>.cgefont</c> は <c>FontAsset</c>。シーン JSON が <c>"Assets/…"</c> で書いたパスに対応するファイルが
/// 無ければ SCENE208(エラー)にする。
/// </summary>
internal static class AssetCatalogEmitter {
    internal const string HintName="GameSystem.Assets.g.cs";
    private static readonly Regex AssetPathPattern=new("\"((?:Plugins/[^\"/\\\\]+/)?Assets/[^\"\\\\]+)\"",RegexOptions.Compiled);

    internal static readonly DiagnosticDescriptor MissingAsset=new(
        id:"SCENE208",
        title:"A scene refers to an asset file that does not exist",
        messageFormat:"The asset '{0}' is not a file of the project (Assets must hold a .wav, .glb, .gltf, .fbx, .png or .jpg with this path)",
        category:"CSharpGameEngine.Entities.CodeGen",
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// 出力フォルダ相対の `/` 区切りのパス。ゲームのアセットは<c>Assets/…</c>、プラグインのアセットは<c>Plugins/&lt;名前&gt;/Assets/…</c>
    /// (ADR-0079。プロジェクトのプラグインはプロジェクト相対のパスがそのままこの形で、エンジンとパッケージのプラグインはビルドがこの場所へコピーする)。
    /// どの<c>Assets/</c>の外でもない、または知らない拡張子なら null。
    /// </summary>
    internal static string?RelativeAssetPath(string FullPath,string?ProjectDir,IReadOnlyList<PluginRoot>?PluginRoots=null){
        var Path=FullPath.Replace('\\','/');
        if(PluginRoots is not null){
            foreach(var Plugin in PluginRoots){
                var PluginRootPath=Plugin.Directory.Replace('\\','/');
                if(!PluginRootPath.EndsWith("/",StringComparison.Ordinal)){ PluginRootPath+="/"; }
                if(!Path.StartsWith(PluginRootPath+"Assets/",StringComparison.OrdinalIgnoreCase)){ continue; }
                var PluginRelative=SceneFiles.PluginsFolderName+"/"+Plugin.Name+"/"+Path.Substring(PluginRootPath.Length);
                return KindOf(PluginRelative) is not null?PluginRelative:null;
            }
        }
        string Relative;
        if(!string.IsNullOrEmpty(ProjectDir)){
            var Root=ProjectDir!.Replace('\\','/');
            if(!Root.EndsWith("/",StringComparison.Ordinal)){ Root+="/"; }
            if(!Path.StartsWith(Root,StringComparison.OrdinalIgnoreCase)){ return null; }
            Relative=Path.Substring(Root.Length);
        } else {
            var At=Path.LastIndexOf("/Assets/",StringComparison.OrdinalIgnoreCase);
            if(At<0){ return null; }
            Relative=Path.Substring(At+1);
        }

        return (Relative.StartsWith("Assets/",StringComparison.Ordinal)||IsProjectPluginAsset(Relative))&&KindOf(Relative) is not null?Relative:null;
    }

    /// <summary><c>Plugins/&lt;名前&gt;/Assets/…</c>: a project plugin's asset.</summary>
    private static bool IsProjectPluginAsset(string Relative){
        var Segments=Relative.Split('/');
        return Segments.Length>3&&Segments[0]==SceneFiles.PluginsFolderName&&Segments[2]=="Assets";
    }

    private static string?KindOf(string Path){
        var Extension=System.IO.Path.GetExtension(Path).ToLowerInvariant();
        return Extension switch {
            ".wav"=>"SoundAsset",
            ".glb" or ".gltf" or ".fbx"=>"ModelAsset",
            ".png" or ".jpg" or ".jpeg"=>"TextureAsset",
            ".cgefont"=>"FontAsset",
            DataAssetExtension=>DataAssetKind,
            _=>null,
        };
    }

    /// <summary>A data asset (ADR-0079): one value of a <c>[DataAsset]</c> type, compiled like a scene component.</summary>
    internal const string DataAssetExtension=".cgeasset";
    private const string DataAssetKind="DataAsset";

    /// <summary>A data asset file read against the compilation: its value as C#, or the problems that keep it out of the catalog.</summary>
    internal readonly struct DataAssetModel(string RelativePath,string?TypeFullyQualifiedName,string?Expression,ImmutableArray<Diagnostic>Diagnostics) {
        internal readonly string RelativePath=RelativePath;
        internal readonly string?TypeFullyQualifiedName=TypeFullyQualifiedName;
        internal readonly string?Expression=Expression;
        internal readonly ImmutableArray<Diagnostic>Diagnostics=Diagnostics;
    }

    internal static DataAssetModel?AnalyzeDataAsset(AdditionalText File,string?RelativePath,Compilation Compilation,CancellationToken CancellationToken){
        if(RelativePath is null||!RelativePath.EndsWith(DataAssetExtension,StringComparison.OrdinalIgnoreCase)||File.GetText(CancellationToken) is not { }Text){
            return null;
        }
        var Value=CSharpGameEngine.SceneAnalysis.Json.SceneJsonSceneAnalyzer.AnalyzeDataAsset(Text.ToString(),Compilation,out var Problems);
        return new DataAssetModel(RelativePath,Value?.TypeFullyQualifiedName,Value?.EmitExpressionText,
            [.. Problems.Select(Problem=>Diagnostic.Create(Problem.Descriptor,SceneJsonDslAnalyzer.Locate(File.Path,Text,Problem.Start,Problem.End),Problem.Arguments))]);
    }

    internal static void Emit(SourceProductionContext Context,ImmutableArray<string>AssetPaths,ImmutableArray<DataAssetModel>DataAssets=default){
        if(AssetPaths.IsEmpty){ return; }
        Dictionary<string,DataAssetModel>DataByPath=new(StringComparer.Ordinal);
        foreach(var Data in DataAssets.IsDefault?[]:DataAssets){
            foreach(var Problem in Data.Diagnostics){ Context.ReportDiagnostic(Problem); }
            DataByPath[Data.RelativePath]=Data;
        }
        var Root=new Folder("Assets");
        foreach(var Path in AssetPaths.Distinct(StringComparer.Ordinal).OrderBy(static P=>P,StringComparer.Ordinal)){
            var Segments=Path.Split('/');
            var Current=Root;
            // Assets/Sounds/Jump.wav → Assets.Sounds.Jump; Plugins/Vehicles/Assets/Car.glb → Assets.Plugins.Vehicles.Car (ADR-0079).
            IEnumerable<string>Folders=IsProjectPluginAsset(Path)
                ?[Segments[0],Segments[1],.. Segments.Skip(3).Take(Segments.Length-4)]
                :Segments.Skip(1).Take(Segments.Length-2);
            foreach(var Folder in Folders){
                Current=Current.Child(Folder);
            }

            var FileName=Segments[Segments.Length-1];
            Current.Add(System.IO.Path.GetFileNameWithoutExtension(FileName),System.IO.Path.GetExtension(FileName),Path);
        }

        List<string>Lines=[
            "// <auto-generated/>",
            "#nullable enable",
            "/// <summary>The project's asset files as typed references (ADR-0077). A missing file or a wrong name is a compile error.</summary>",
        ];
        Write(Root,0,Lines,DataByPath);
        Context.AddSource(HintName,SourceText.From(string.Join("\n",Lines),Encoding.UTF8));
    }

    internal static void ReportMissing(SourceProductionContext Context,ImmutableArray<string>AssetPaths,ImmutableArray<(string Path,SourceText Text)>Scenes){
        var Known=new HashSet<string>(AssetPaths,StringComparer.OrdinalIgnoreCase);
        foreach(var (Path,Text) in Scenes.OrderBy(static S=>S.Path,StringComparer.Ordinal)){
            var Source=Text.ToString();
            foreach(Match Found in AssetPathPattern.Matches(Source)){
                var Asset=Found.Groups[1].Value;
                if(Known.Contains(Asset)){ continue; }
                var Span=new TextSpan(Found.Groups[1].Index,Found.Groups[1].Length);
                Context.ReportDiagnostic(Diagnostic.Create(MissingAsset,Location.Create(Path,Span,Text.Lines.GetLinePositionSpan(Span)),Asset));
            }
        }
    }

    private static void Write(Folder Node,int Depth,List<string>Lines,Dictionary<string,DataAssetModel>DataByPath){
        var Indent=new string(' ',Depth*4);
        Lines.Add($"{Indent}internal static class {Node.Name} {{");
        foreach(var Entry in Node.Entries){
            if(Entry.Kind!=DataAssetKind){
                Lines.Add($"{Indent}    public static readonly global::CSharpGameEngine.Authoring.{Entry.Kind} {Entry.Name} = new(\"{Entry.Path}\");");
            } else if(DataByPath.TryGetValue(Entry.Path,out var Data)&&Data.TypeFullyQualifiedName is { }Type&&Data.Expression is { }Expression){
                // A data asset is its value itself; a document with problems is left out (they are reported).
                Lines.Add($"{Indent}    public static readonly {Type} {Entry.Name} = {Expression};");
            }
        }

        foreach(var Child in Node.Children){ Write(Child,Depth+1,Lines,DataByPath); }
        Lines.Add($"{Indent}}}");
    }

    private sealed class Folder(string RawName) {
        internal readonly string Name=Identifier(RawName);
        internal readonly List<(string Name,string Kind,string Path)>Entries=[];
        internal readonly List<Folder>Children=[];

        internal Folder Child(string Raw){
            var Wanted=Identifier(Raw);
            var Existing=this.Children.FirstOrDefault(Child=>Child.Name==Wanted);
            if(Existing is not null){ return Existing; }
            var Created=new Folder(Raw);
            this.Children.Add(Created);
            return Created;
        }

        internal void Add(string Stem,string Extension,string Path){
            var Name=Identifier(Stem);
            if(this.Taken(Name)){ Name=Identifier(Stem+"_"+Extension.TrimStart('.')); }
            var Candidate=Name;
            var Suffix=2;
            while(this.Taken(Candidate)){ Candidate=Name+"_"+Suffix++; }
            this.Entries.Add((Candidate,KindOf(Path)!,Path));
        }

        private bool Taken(string Candidate)=>this.Entries.Any(Entry=>Entry.Name==Candidate)||this.Children.Any(Child=>Child.Name==Candidate);
    }

    private static string Identifier(string Raw){
        var Builder=new StringBuilder();
        foreach(var Character in Raw){ Builder.Append(char.IsLetterOrDigit(Character)||Character=='_'?Character:'_'); }
        if(Builder.Length==0||char.IsDigit(Builder[0])){ Builder.Insert(0,'_'); }
        var Text=Builder.ToString();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(Text)!=Microsoft.CodeAnalysis.CSharp.SyntaxKind.None?"@"+Text:Text;
    }
}
