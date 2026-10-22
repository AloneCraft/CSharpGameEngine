using System.Numerics;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Unreal.SceneImporter;
using CSharpGameEngine.Import.Gltf;
using static CSharpGameEngine.Unreal.SceneImporter.SceneSourceFormatting;

if(args.Length>0&&args[0]=="thirdperson"){
    return ThirdPersonTemplateImporter.Run(args[1..]);
}

if(args.Length>0&&args[0]=="level"){
    return LevelTemplateImporter.Run(args[1..]);
}

if(args.Length>0&&args[0]=="worldgrid"){
    return WorldGridGlbConverter.Run(args[1..]);
}

if(args.Length==2&&args[0]=="compact-animations"){
    // tools/UnrealExport/export_mannequin_animations.py writes the preview mesh into every clip; this keeps the joints and the clip only.
    foreach(var ClipPath in Directory.EnumerateFiles(Path.GetFullPath(args[1]),"*.glb").OrderBy(Item=>Item,StringComparer.Ordinal)){
        var Joints=AnimationGlbCompactor.CompactInPlace(ClipPath);
        Console.WriteLine($"{Path.GetFileName(ClipPath)}: {Joints} animated joints, {new FileInfo(ClipPath).Length} bytes.");
    }
    return 0;
}

if(args.Length>0&&args[0]=="logic"){
    return BlueprintLogicWriter.Run(args[1..]);
}

if(args.Length!=3){
    Console.Error.WriteLine("Usage: CSharpGameEngine.Unreal.SceneImporter <manifest.json> <scene.cs> <assets.cs>");
    Console.Error.WriteLine("       CSharpGameEngine.Unreal.SceneImporter level <manifest.json> <template-project-directory> <TemplateName> <LevelName>");
    Console.Error.WriteLine("       CSharpGameEngine.Unreal.SceneImporter thirdperson <manifest.json> <template-project-directory>");
    Console.Error.WriteLine("       CSharpGameEngine.Unreal.SceneImporter worldgrid <manifest.json> <project-directory>");
    Console.Error.WriteLine("       CSharpGameEngine.Unreal.SceneImporter logic <directory with .t3d files>");
    Console.Error.WriteLine("       CSharpGameEngine.Unreal.SceneImporter compact-animations <directory with animation .glb files>");
    return 2;
}

var ManifestPath=Path.GetFullPath(args[0]);
var ScenePath=Path.GetFullPath(args[1]);
var AssetsPath=Path.GetFullPath(args[2]);
var Manifest=JsonSerializer.Deserialize<ImportManifest>(File.ReadAllText(ManifestPath),JsonOptions())
           ??throw new InvalidOperationException($"Could not deserialize '{ManifestPath}'.");

if(Manifest.Version!=2){
    throw new InvalidOperationException($"Unsupported manifest version {Manifest.Version}.");
}

var Result=GltfImporter.ImportScene(Manifest.StagingGlb);
if(!Result.Success||Result.Scene is not{ }Scene){
    throw new InvalidOperationException(string.Join(Environment.NewLine,Result.Diagnostics));
}

var MeshNodeByName=Scene.Nodes
                       .Where(Node=>Node.MeshIndex.HasValue)
                       .GroupBy(Node=>Node.Name,StringComparer.Ordinal)
                       .ToDictionary(Group=>Group.Key,Group=>Group.First(),StringComparer.Ordinal);

if(Manifest.Actors.Count!=55){
    throw new InvalidOperationException($"Expected 55 StaticMeshActors, found {Manifest.Actors.Count}.");
}

foreach(ImportActor Actor in Manifest.Actors){
    if(!MeshNodeByName.ContainsKey(Actor.Label)){
        throw new InvalidOperationException($"The staging GLB has no mesh node named '{Actor.Label}'.");
    }
}

ImportedSceneNode[]DirectionalNodes=[.. Scene.Nodes.Where(Node=>Node.LightIndex is int Index&&Scene.Lights[Index] is ImportedDirectionalLight)];
if(DirectionalNodes.Length!=1){
    throw new InvalidOperationException($"Expected one directional light node, found {DirectionalNodes.Length}.");
}

Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
Directory.CreateDirectory(Path.GetDirectoryName(AssetsPath)!);
File.WriteAllText(AssetsPath,BuildAssetsSource(Manifest),new UTF8Encoding(encoderShouldEmitUTF8Identifier:false));
File.WriteAllText(ScenePath,BuildSceneSource(Manifest,Scene,MeshNodeByName,DirectionalNodes[0]),new UTF8Encoding(encoderShouldEmitUTF8Identifier:false));

Console.WriteLine($"Generated {Manifest.Actors.Count} StaticMesh nodes from {Manifest.RenderVariants.Count} render variants.");
return 0;

static JsonSerializerOptions JsonOptions()=>new(){ PropertyNameCaseInsensitive=true };

static string BuildAssetsSource(ImportManifest Manifest){
    var Builder=new StringBuilder();
    Builder.AppendLine("using CSharpGameEngine.Authoring;");
    Builder.AppendLine();
    Builder.AppendLine("namespace UserEcsProject.Scenes;");
    Builder.AppendLine();
    Builder.AppendLine("/// <summary>UE FirstPersonレベルから一度だけ取り込んだ描画資産カタログ。</summary>");
    Builder.AppendLine("[SceneAssets]");
    Builder.AppendLine("internal static class FirstPersonStageAssets {");
    foreach(ImportVariant Variant in Manifest.RenderVariants.OrderBy(Item=>Item.AssetPath,StringComparer.Ordinal)){
        Builder.Append("    public const string ");
        Builder.Append(AssetMemberName(Variant.AssetPath));
        Builder.Append("=\"");
        Builder.Append(Escape(Variant.AssetPath));
        Builder.AppendLine("\";");
    }
    Builder.AppendLine("}");
    return Builder.ToString();
}

static string BuildSceneSource(
    ImportManifest Manifest,
    ImportedScene Scene,
    IReadOnlyDictionary<string,ImportedSceneNode>MeshNodeByName,
    ImportedSceneNode DirectionalNode){
    var Light=(ImportedDirectionalLight)Scene.Lights[DirectionalNode.LightIndex!.Value];
    var TravelDirection=ImportedLightDirection.WorldForward(DirectionalNode.WorldTransform);
    ImportEnvironment Environment=Manifest.Environment;

    var Builder=new StringBuilder();
    Builder.AppendLine("using CSharpGameEngine.Entities;");
    Builder.AppendLine("using CSharpGameEngine.Runtime.Windows;");
    Builder.AppendLine();
    Builder.AppendLine("namespace UserEcsProject.Scenes;");
    Builder.AppendLine();
    Builder.AppendLine("/// <summary>");
    Builder.AppendLine("/// /Game/FirstPerson/Lvl_FirstPersonから一度だけ取り込んだ宣言型ステージ。");
    Builder.AppendLine("/// このC#を正本として編集する。再取り込みはrun_export.cmd --forceを明示した場合だけ行う。");
    Builder.AppendLine("/// </summary>");
    Builder.AppendLine("[StartupScene(Order=1)]");
    Builder.AppendLine("internal partial class FirstPersonStageScene:IScene {");
    Builder.AppendLine("    public Node Root=>new(){");
    Builder.AppendLine("        Name=\"Lvl_FirstPerson\",");
    Builder.AppendLine("        Components=[");
    AppendAmbient(Builder,Environment.Ambient);
    AppendFog(Builder,Environment.Fog);
    Builder.AppendLine("        ],");
    Builder.AppendLine("        Children=[");

    foreach(ImportActor Actor in Manifest.Actors.OrderBy(Item=>Item.Label,StringComparer.Ordinal)){
        var World=MeshNodeByName[Actor.Label].WorldTransform;
        if(!Matrix4x4.Decompose(World,out var Scale,out var Rotation,out var Translation)){
            throw new InvalidOperationException($"Could not decompose transform for '{Actor.Label}'.");
        }
        Builder.AppendLine("            new Node {");
        Builder.Append("                Name=\"").Append(Escape(Actor.Label)).AppendLine("\",");
        Builder.AppendLine("                Components=[");
        AppendTransform(Builder,Translation,Rotation,Scale,"                    ");
        Builder.Append("                    new StaticMesh { AssetPath=FirstPersonStageAssets.")
            .Append(AssetMemberName(Actor.AssetPath)).Append(",Mobility=MeshMobility.")
            .Append(Actor.Mobility).AppendLine(" },");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
    }

    Builder.AppendLine("            new Node {");
    Builder.AppendLine("                Name=\"DirectionalLight\",");
    Builder.AppendLine("                Components=[");
    UnrealLightSceneEmitter.AppendDirectionalLightComponentsFromTravelDirection(
        Builder,TravelDirection,new Vector3(Light.Color.R,Light.Color.G,Light.Color.B),Light.Illuminance.Lux,"                    ");
    Builder.AppendLine("                ],");
    Builder.AppendLine("            },");

    ImportPlayerStart Player=Environment.PlayerStart;
    Builder.AppendLine("            new Node {");
    Builder.Append("                Name=\"").Append(Escape(Player.Name)).AppendLine("\",");
    Builder.AppendLine("                Components=[");
    AppendTransform(Builder,new Vector3(Player.Translation[0],Player.Translation[1],Player.Translation[2]),Quaternion.Identity,Vector3.One,"                    ");
    Builder.Append("                    new PlayerStart { YawRadians=").Append(F(Player.YawRadians))
        .Append(",PitchRadians=").Append(F(Player.PitchRadians)).AppendLine(" },");
    Builder.AppendLine("                ],");
    Builder.AppendLine("            },");

    Builder.AppendLine("        ],");
    Builder.AppendLine("    };");
    Builder.AppendLine("}");
    return Builder.ToString();
}

internal sealed class ImportManifest {
    public int Version { get; set;}
    public string StagingGlb { get; set;}=string.Empty;
    public List<ImportActor>Actors { get; set;}=[];
    public List<ImportVariant>RenderVariants { get; set;}=[];
    public ImportEnvironment Environment { get; set;}=new();
}

internal sealed class ImportActor {
    public string Label { get; set;}=string.Empty;
    public List<string>Materials { get; set;}=[];
    public string AssetPath { get; set;}=string.Empty;
    public string Mobility { get; set;}=string.Empty;
}

internal sealed class ImportVariant {
    public string AssetPath { get; set;}=string.Empty;
}

internal sealed class ImportEnvironment {
    public ImportAmbient Ambient { get; set;}=new();
    public ImportFog Fog { get; set;}=new();
    public ImportPlayerStart PlayerStart { get; set;}=new();
}

internal sealed class ImportAmbient {
    public string Name { get; set;}=string.Empty;
    public float[]ColorRgb { get; set;}=[];
    public float Intensity { get; set;}
}

internal sealed class ImportFog {
    public string Name { get; set;}=string.Empty;
    public float Density { get; set;}
    public float HeightFalloff { get; set;}
    public float[]ColorRgb { get; set;}=[];
}

internal sealed class ImportPlayerStart {
    public string Name { get; set;}=string.Empty;
    public float[]Translation { get; set;}=[];
    public float YawRadians { get; set;}
    public float PitchRadians { get; set;}
}
