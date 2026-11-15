using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using CSharpGameEngine.SceneAnalysis.Json;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// ADR-0077 stage 7: <c>Assets/</c> の下のファイルから、型付きのアセット参照 <c>Assets.Sounds.Jump</c> を生成する。
/// ファイルの一覧(AdditionalFiles)だけを使い、中身は読まない。<c>.wav</c> は <c>SoundAsset</c>、<c>.glb</c>/<c>.gltf</c>/<c>.fbx</c> は
/// <c>ModelAsset</c>、<c>.png</c>/<c>.jpg</c> は <c>TextureAsset</c>、<c>.cgefont</c> は <c>FontAsset</c>。シーン JSON が <c>"Assets/…"</c> で書いたパスに対応するファイルが
/// 無ければ SCENE208(エラー)にする。
/// </summary>
internal static partial class AssetCatalogEmitter {
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

    /// <summary><c>Plugins/&lt;名前&gt;/Assets/…</c>：プロジェクトプラグインのアセット。</summary>
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

    /// <summary>データアセット (ADR-0079)：シーンコンポーネントと同様にコンパイルされる、<c>[DataAsset]</c> 型の値 1 つ。</summary>
    internal const string DataAssetExtension=".cgeasset";
    private const string DataAssetKind="DataAsset";

    /// <summary>コンパイル内容に照らして読み取ったデータアセットファイル。その値を C# として表すか、カタログから除外する原因となる問題を表す。</summary>
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

    internal static void Emit(SourceProductionContext Context,ImmutableArray<string>AssetPaths,ImmutableArray<DataAssetModel>DataAssets=default,ImmutableArray<(string Path,string Json)>ImageMetadata=default){
        if(AssetPaths.IsEmpty){ return; }
        Dictionary<string,DataAssetModel>DataByPath=new(StringComparer.Ordinal);
        foreach(var Data in DataAssets.IsDefault?[]:DataAssets){
            foreach(var Problem in Data.Diagnostics){ Context.ReportDiagnostic(Problem); }
            DataByPath[Data.RelativePath]=Data;
        }
        var Images=ReadImages(Context,ImageMetadata);
        var Root=new Folder("Assets");
        var Names=AssetCatalogLayout.Map(AssetPaths);
        foreach(var Path in AssetPaths.Distinct(StringComparer.Ordinal).OrderBy(static P=>P,StringComparer.Ordinal)){
            var Segments=Path.Split('/');
            var Current=Root;
            // Assets/Sounds/Jump.wav → Assets.Sounds.Jump。Plugins/Vehicles/Assets/Car.glb → Assets.Plugins.Vehicles.Car (ADR-0079)。
            IEnumerable<string>Folders=IsProjectPluginAsset(Path)
                ?[Segments[0],Segments[1],.. Segments.Skip(3).Take(Segments.Length-4)]
                :Segments.Skip(1).Take(Segments.Length-2);
            foreach(var Folder in Folders){
                Current=Current.Child(Folder);
            }

            var FileName=Segments[Segments.Length-1];
            Current.Add(Names[Path].Split('.').Last(),System.IO.Path.GetExtension(FileName),Path);
        }

        List<string>Lines=[
            "// <auto-generated/>",
            "#nullable enable",
            "/// <summary>The project's asset files as typed references (ADR-0077). A missing file or a wrong name is a compile error.</summary>",
        ];
        Write(Root,0,Lines,DataByPath,Images);
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

    private static void Write(Folder Node,int Depth,List<string>Lines,Dictionary<string,DataAssetModel>DataByPath,Dictionary<string,CSharpGameEngine.CodeAnalysis.ImageContract.ImageImportSettings>Images){
        var Indent=new string(' ',Depth*4);
        Lines.Add($"{Indent}internal static class {Node.Name} {{");
        foreach(var Entry in Node.Entries){
            if(Entry.Kind=="TextureAsset"&&Images.TryGetValue(Entry.Path,out var Image)){
                WriteImage(Entry.Name,Entry.Path,Image,Indent+"    ",Lines);
            } else if(Entry.Kind!=DataAssetKind){
                Lines.Add($"{Indent}    public static readonly global::CSharpGameEngine.Authoring.{Entry.Kind} {Entry.Name} = new(\"{Entry.Path}\");");
            } else if(DataByPath.TryGetValue(Entry.Path,out var Data)&&Data.TypeFullyQualifiedName is { }Type&&Data.Expression is { }Expression){
                // データアセットはその値自体を指す。問題のあるドキュメントは除外する（問題は報告する）。
                Lines.Add($"{Indent}    public static readonly {Type} {Entry.Name} = {Expression};");
            }
        }

        foreach(var Child in Node.Children){ Write(Child,Depth+1,Lines,DataByPath,Images); }
        Lines.Add($"{Indent}}}");
    }

    private sealed class Folder(string RawName) {
        internal readonly string Name=AssetCatalogLayout.Identifier(RawName);
        internal readonly List<(string Name,string Kind,string Path)>Entries=[];
        internal readonly List<Folder>Children=[];

        internal Folder Child(string Raw){
            var Wanted=AssetCatalogLayout.Identifier(Raw);
            var Existing=this.Children.FirstOrDefault(Child=>Child.Name==Wanted);
            if(Existing is not null){ return Existing; }
            var Created=new Folder(Raw);
            this.Children.Add(Created);
            return Created;
        }

        internal void Add(string Name,string Extension,string Path){
            this.Entries.Add((Name,KindOf(Path)!,Path));
        }
    }

}
