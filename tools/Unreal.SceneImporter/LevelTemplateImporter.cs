using System.Text;
using System.Text.Json;
using CSharpGameEngine.Import.Gltf;
using static CSharpGameEngine.Unreal.SceneImporter.SceneSourceFormatting;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>テンプレートが生成するレベルシーンとアセットカタログで使う名前（名前空間、クラス名、生成コメントのテキスト）。</summary>
internal sealed class LevelNames(string Namespace,string LevelClass,string AssetsClass,string TemplateName,string RunScript) {
    internal string Namespace { get; }=Namespace;
    internal string LevelClass { get; }=LevelClass;
    internal string AssetsClass { get; }=AssetsClass;
    internal string TemplateName { get; }=TemplateName;
    internal string RunScript { get; }=RunScript;
    internal static LevelNames ThirdPerson { get; }=new("CgeThirdPerson.Scenes","Lvl_ThirdPerson","ThirdPersonAssets","ThirdPerson","tools/UnrealExport/run_export_thirdperson.cmd");
    internal static LevelNames For(string Template,string Level)=>new($"Cge{Template}.Scenes",Level,$"{Template}Assets",Template,"tools/UnrealExport/run_export_level.cmd "+Template);
}
/// <summary>
/// UE5.8のレベル(manifest version 4。キャラクターなし)から、テンプレート原本プロジェクトのレベルとアセットカタログのC#を生成する
/// (FirstPerson、TopDown)。生成したC#はrun_export_level.cmdが続けて<c>SceneJson.Cli convert</c>でシーンJSONへ変換する(ADR-0070)。
/// キャラクターは手書きで、UEのBlueprintのダンプ(dump_blueprint.py)を参照して作る。
/// </summary>
internal static class LevelTemplateImporter {
    internal static int Run(string[]Arguments){
        if(Arguments.Length!=4){
            Console.Error.WriteLine("Usage: CSharpGameEngine.Unreal.SceneImporter level <manifest.json> <template-project-directory> <TemplateName> <LevelName>");
            return 2;
        }
        var ManifestPath=Path.GetFullPath(Arguments[0]);
        var TemplateDirectory=Path.GetFullPath(Arguments[1]);
        var Names=LevelNames.For(Arguments[2],Arguments[3]);
        var Manifest=JsonSerializer.Deserialize<ThirdPersonManifest>(File.ReadAllText(ManifestPath),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
            ??throw new InvalidOperationException($"Could not deserialize '{ManifestPath}'.");
        if(Manifest.Version!=4){
            throw new InvalidOperationException($"Unsupported level manifest version {Manifest.Version}; run tools/UnrealExport/run_export_level.cmd.");
        }
        var Result=GltfImporter.ImportScene(Manifest.StagingGlb);
        if(!Result.Success||Result.Scene is not{ }Scene){
            throw new InvalidOperationException(string.Join(Environment.NewLine,Result.Diagnostics));
        }
        var Lighting=Manifest.Lighting??throw new InvalidOperationException("The manifest has no lighting section; re-run the Unreal export.");
        var SkyDomeActors=Manifest.Actors.Where(Actor=>Actor.Materials.Any(Material=>Material.StartsWith(Lighting.SkyDomeMaterial,StringComparison.Ordinal))).Select(Actor=>Actor.Label).ToHashSet(StringComparer.Ordinal);
        var MeshNodeByName=Scene.Nodes.Where(Node=>Node.MeshIndex.HasValue).GroupBy(Node=>Node.Name,StringComparer.Ordinal).ToDictionary(Group=>Group.Key,Group=>Group.First(),StringComparer.Ordinal);
        foreach(var Actor in Manifest.Actors){
            if(!MeshNodeByName.ContainsKey(Actor.Label)){
                throw new InvalidOperationException($"The staging GLB has no mesh node named '{Actor.Label}'.");
            }
        }
        ImportedSceneNode[]DirectionalNodes=[.. Scene.Nodes.Where(Node=>Node.LightIndex is int Index&&Scene.Lights[Index] is ImportedDirectionalLight)];
        if(DirectionalNodes.Length!=1){
            throw new InvalidOperationException($"Expected one directional light node, found {DirectionalNodes.Length}.");
        }
        // スカイドームは glTF ベイクでは表現できない IsSky マテリアルを使うため、SkySphere ノードで表す (ADR-0069、M0110)。
        var SkyDomeAssets=Manifest.Actors.Where(Actor=>SkyDomeActors.Contains(Actor.Label)).Select(Actor=>Actor.AssetPath).ToHashSet(StringComparer.Ordinal);
        foreach(var Asset in SkyDomeAssets.Where(Asset=>Manifest.Actors.All(Actor=>SkyDomeActors.Contains(Actor.Label)||Actor.AssetPath!=Asset))){
            File.Delete(Path.Combine(TemplateDirectory,Asset));
            File.Delete(Path.Combine(TemplateDirectory,Asset+".meta"));
        }
        var ScenesDirectory=Path.Combine(TemplateDirectory,"Scenes");
        Directory.CreateDirectory(ScenesDirectory);
        var Utf8=new UTF8Encoding(encoderShouldEmitUTF8Identifier:false);
        File.WriteAllText(Path.Combine(ScenesDirectory,Names.AssetsClass+".cs"),BuildAssetsSource(Manifest,SkyDomeAssets,Names),Utf8);
        File.WriteAllText(Path.Combine(ScenesDirectory,Names.LevelClass+".cs"),ThirdPersonTemplateImporter.BuildLevelSource(Manifest,Scene,MeshNodeByName,DirectionalNodes[0],SkyDomeActors,Names),Utf8);
        Console.WriteLine($"Generated {Manifest.Actors.Count-SkyDomeActors.Count} StaticMesh nodes from {Manifest.RenderVariants.Count-SkyDomeAssets.Count} render variants; {SkyDomeActors.Count} sky dome actor(s) became SkySphere nodes.");
        return 0;
    }
    private static string BuildAssetsSource(ThirdPersonManifest Manifest,IReadOnlySet<string>SkippedAssets,LevelNames Names){
        var Builder=new StringBuilder();
        Builder.AppendLine("using CSharpGameEngine.Authoring;");
        Builder.AppendLine("namespace "+Names.Namespace+";");
        Builder.AppendLine("/// <summary>UE5.8 "+Names.TemplateName+"テンプレートから取り込んだ描画資産のカタログ(シーンの生成にだけ使う中間形式)。</summary>");
        Builder.AppendLine("[SceneAssets]");
        Builder.AppendLine("internal static class "+Names.AssetsClass+" {");
        foreach(var Variant in Manifest.RenderVariants.Where(Item=>!SkippedAssets.Contains(Item.AssetPath)).OrderBy(Item=>Item.AssetPath,StringComparer.Ordinal)){
            Builder.Append("    public const string ").Append(AssetMemberName(Variant.AssetPath)).Append("=\"").Append(Escape(Variant.AssetPath)).AppendLine("\";");
        }
        Builder.AppendLine("}");
        return Builder.ToString();
    }
}
