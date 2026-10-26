using System.Numerics;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Import.Gltf;
using SharpGLTF.Schema2;
using static CSharpGameEngine.Unreal.SceneImporter.SceneSourceFormatting;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>
/// UE5.8 ThirdPersonテンプレートのmanifest(version 3)から、テンプレート原本プロジェクトのC#を生成する。
/// 生成物はLvl_ThirdPerson(レベル配置とコリジョン)、ThirdPersonAssets(資産カタログ)、ThirdPersonCharacter
/// (キャラクター)。入力(Gameplay/PlayerActions)は手書きで、生成しない(ADR-0077)。アニメーションglbは
/// ジョイント階層とアニメーションだけを残す形へ縮約し、UEが同梱した重複メッシュ・テクスチャを除く。
/// SceneクラスのC#は中間形式で、run_export_thirdperson.cmdが続けて<c>CSharpGameEngine.SceneJson.Cli
/// convert</c>でシーンJSONへ変換する(ADR-0070)。
/// </summary>
internal static class ThirdPersonTemplateImporter {
    internal const string Namespace="CgeThirdPerson.Scenes";
    internal static int Run(string[]Arguments){
        if(Arguments.Length!=2){
            Console.Error.WriteLine("Usage: CSharpGameEngine.Unreal.SceneImporter thirdperson <manifest.json> <template-project-directory>");
            return 2;
        }
        var ManifestPath=Path.GetFullPath(Arguments[0]);
        var TemplateDirectory=Path.GetFullPath(Arguments[1]);
        var Manifest=JsonSerializer.Deserialize<ThirdPersonManifest>(File.ReadAllText(ManifestPath),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
            ??throw new InvalidOperationException($"Could not deserialize '{ManifestPath}'.");
        if(Manifest.Version!=3){
            throw new InvalidOperationException($"Unsupported ThirdPerson manifest version {Manifest.Version}.");
        }
        var Result=GltfImporter.ImportScene(Manifest.StagingGlb);
        if(!Result.Success||Result.Scene is not{ }Scene){
            throw new InvalidOperationException(string.Join(Environment.NewLine,Result.Diagnostics));
        }
        var Lighting=Manifest.Lighting??throw new InvalidOperationException("The ThirdPerson manifest has no lighting section; re-run the Unreal export.");
        var SkyDomeActors=Manifest.Actors.Where(Actor=>Actor.Materials.Any(Material=>Material.StartsWith(Lighting.SkyDomeMaterial,StringComparison.Ordinal))).Select(Actor=>Actor.Label).ToHashSet(StringComparer.Ordinal);
        var MeshNodeByName=Scene.Nodes.Where(Node=>Node.MeshIndex.HasValue).GroupBy(Node=>Node.Name,StringComparer.Ordinal).ToDictionary(Group=>Group.Key,Group=>Group.First(),StringComparer.Ordinal);
        if(Manifest.Actors.Count!=55){
            throw new InvalidOperationException($"Expected 55 StaticMeshActors, found {Manifest.Actors.Count}.");
        }
        foreach(var Actor in Manifest.Actors){
            if(!MeshNodeByName.ContainsKey(Actor.Label)){
                throw new InvalidOperationException($"The staging GLB has no mesh node named '{Actor.Label}'.");
            }
        }
        ImportedSceneNode[]DirectionalNodes=[.. Scene.Nodes.Where(Node=>Node.LightIndex is int Index&&Scene.Lights[Index] is ImportedDirectionalLight)];
        if(DirectionalNodes.Length!=1){
            throw new InvalidOperationException($"Expected one directional light node, found {DirectionalNodes.Length}.");
        }
        foreach(var Clip in Manifest.Character.Animations){
            var ClipPath=Path.Combine(TemplateDirectory,Clip.AssetPath);
            var Joints=AnimationGlbCompactor.CompactInPlace(ClipPath);
            Console.WriteLine($"Animation {Clip.Name}: {Joints} animated joints, {new FileInfo(ClipPath).Length} bytes.");
        }
        var ScenesDirectory=Path.Combine(TemplateDirectory,"Scenes");
        Directory.CreateDirectory(ScenesDirectory);
        var Utf8=new UTF8Encoding(encoderShouldEmitUTF8Identifier:false);
        // Unreal は IsSky マテリアルでスカイドームを描画し、SkyAtmosphere をサンプリングする。glTF ベイクでは表現できないため、
        // SkySphere コンポーネント (M0110) でそれらのアクターを表し、レンダラーがその上に大気を描画する (ADR-0069)。
        var SkyDomeAssets=Manifest.Actors.Where(Actor=>SkyDomeActors.Contains(Actor.Label)).Select(Actor=>Actor.AssetPath).ToHashSet(StringComparer.Ordinal);
        foreach(var Asset in SkyDomeAssets.Where(Asset=>Manifest.Actors.All(Actor=>SkyDomeActors.Contains(Actor.Label)||Actor.AssetPath!=Asset))){
            File.Delete(Path.Combine(TemplateDirectory,Asset));
            File.Delete(Path.Combine(TemplateDirectory,Asset+".meta"));
        }
        File.WriteAllText(Path.Combine(ScenesDirectory,"ThirdPersonAssets.cs"),BuildAssetsSource(Manifest,SkyDomeAssets),Utf8);
        File.WriteAllText(Path.Combine(ScenesDirectory,"Lvl_ThirdPerson.cs"),BuildLevelSource(Manifest,Scene,MeshNodeByName,DirectionalNodes[0],SkyDomeActors),Utf8);
        File.WriteAllText(Path.Combine(ScenesDirectory,"ThirdPersonCharacter.cs"),ThirdPersonGameplayEmitter.BuildCharacterSource(Manifest.Character,Manifest.Environment.PlayerStart),Utf8);
        Console.WriteLine($"Generated {Manifest.Actors.Count-SkyDomeActors.Count} StaticMesh nodes from {Manifest.RenderVariants.Count-SkyDomeAssets.Count} render variants; {SkyDomeActors.Count} sky dome actor(s) became SkySphere nodes.");
        return 0;
    }
    private static string BuildAssetsSource(ThirdPersonManifest Manifest,IReadOnlySet<string>SkippedAssets){
        var Builder=new StringBuilder();
        Builder.AppendLine("using CSharpGameEngine.Authoring;");
        Builder.AppendLine("namespace "+Namespace+";");
        Builder.AppendLine("/// <summary>UE5.8 ThirdPersonテンプレートから取り込んだ描画・キャラクター資産のカタログ。</summary>");
        Builder.AppendLine("[SceneAssets]");
        Builder.AppendLine("internal static class ThirdPersonAssets {");
        foreach(var Variant in Manifest.RenderVariants.Where(Item=>!SkippedAssets.Contains(Item.AssetPath)).OrderBy(Item=>Item.AssetPath,StringComparer.Ordinal)){
            AppendAsset(Builder,AssetMemberName(Variant.AssetPath),Variant.AssetPath);
        }
        AppendAsset(Builder,AssetMemberName(Manifest.Character.Mesh.AssetPath),Manifest.Character.Mesh.AssetPath);
        foreach(var Clip in Manifest.Character.Animations.OrderBy(Item=>Item.AssetPath,StringComparer.Ordinal)){
            AppendAsset(Builder,AssetMemberName(Clip.AssetPath),Clip.AssetPath);
        }
        Builder.AppendLine("}");
        return Builder.ToString();
    }
    /// <summary>Unreal の環境アクターを、作成時の単位のまま ADR-0069 のコンポーネントとして出力する。</summary>
    private static void AppendEnvironment(StringBuilder Builder,ThirdPersonLightingManifest Lighting){
        var Atmosphere=Lighting.SkyAtmosphere;
        Builder.AppendLine("            new SkyAtmosphere {");
        Builder.Append("                BottomRadiusKm=").Append(F(Atmosphere.BottomRadiusKm)).Append(",AtmosphereHeightKm=").Append(F(Atmosphere.AtmosphereHeightKm)).AppendLine(",");
        AppendRgb(Builder,"GroundAlbedo",Atmosphere.GroundAlbedoLinear);
        AppendRgb(Builder,"RayleighScattering",Atmosphere.RayleighScattering);
        Builder.Append("                RayleighScatteringScale=").Append(F(Atmosphere.RayleighScatteringScale)).Append(",RayleighExponentialDistributionKm=").Append(F(Atmosphere.RayleighExponentialDistributionKm)).AppendLine(",");
        AppendRgb(Builder,"MieScattering",Atmosphere.MieScattering);
        Builder.Append("                MieScatteringScale=").Append(F(Atmosphere.MieScatteringScale)).AppendLine(",");
        AppendRgb(Builder,"MieAbsorption",Atmosphere.MieAbsorption);
        Builder.Append("                MieAbsorptionScale=").Append(F(Atmosphere.MieAbsorptionScale)).Append(",MieAnisotropy=").Append(F(Atmosphere.MieAnisotropy)).Append(",MieExponentialDistributionKm=").Append(F(Atmosphere.MieExponentialDistributionKm)).AppendLine(",");
        AppendRgb(Builder,"OtherAbsorption",Atmosphere.OtherAbsorption);
        Builder.Append("                OtherAbsorptionScale=").Append(F(Atmosphere.OtherAbsorptionScale)).Append(",OtherTentTipAltitudeKm=").Append(F(Atmosphere.OtherTentTipAltitudeKm)).Append(",OtherTentWidthKm=").Append(F(Atmosphere.OtherTentWidthKm)).AppendLine(",");
        Builder.Append("                MultiScatteringFactor=").Append(F(Atmosphere.MultiScatteringFactor)).Append(",SunDiskAngleDegrees=").Append(F(Lighting.Sun.SourceAngleDegrees)).AppendLine(",");
        Builder.AppendLine("            },");
        var Sky=Lighting.SkyLight;
        Builder.Append("            new SkyLight { Intensity=").Append(F(Sky.Intensity));
        var Lower=Sky.LowerHemisphereIsBlack?new float[3]:Sky.LowerHemisphereColor;
        Builder.Append(",LowerHemisphereR=").Append(F(Lower[0])).Append(",LowerHemisphereG=").Append(F(Lower[1])).Append(",LowerHemisphereB=").Append(F(Lower[2])).AppendLine(" },");
        var Fog=Lighting.HeightFog;
        Builder.Append("            new ExponentialHeightFog { FogDensity=").Append(F(Fog.FogDensity)).Append(",FogHeightFalloff=").Append(F(Fog.FogHeightFalloff))
            .Append(",FogHeight=").Append(F(Fog.FogHeightMeters)).Append(",FogMaxOpacity=").Append(F(Fog.FogMaxOpacity))
            .Append(",InscatteringLuminanceR=").Append(F(Fog.InscatteringLuminance[0])).Append(",InscatteringLuminanceG=").Append(F(Fog.InscatteringLuminance[1])).Append(",InscatteringLuminanceB=").Append(F(Fog.InscatteringLuminance[2]))
            .Append(",SkyAtmosphereAmbientContributionScale=").Append(F(Fog.SkyAtmosphereAmbientContributionScale)).AppendLine(" },");
        var Post=Lighting.PostProcess;
        // Unreal のヒストグラム自動露出で最小と最大の明るさを等しくすると、露出は固定される。最小/最大に幅がある場合は、
        // AutoHistogram 方式が必要だが、インポーターは未対応のため拒否する。
        var Method=Post.AutoExposureMethod switch {
            "AEM_HISTOGRAM"=>"AutoHistogram",
            "AEM_MANUAL"=>"Manual",
            _=>throw new NotSupportedException($"Unreal auto exposure method '{Post.AutoExposureMethod}' is not supported; only AEM_HISTOGRAM and AEM_MANUAL are."),
        };
        Builder.Append("            new PostProcessVolume { ExposureMethod=ExposureMethod.").Append(Method).Append(",MinEv100=").Append(F(Post.MinEv100)).Append(",MaxEv100=").Append(F(Post.MaxEv100))
            .Append(",AutoExposureSpeedUp=").Append(F(Post.AutoExposureSpeedUp)).Append(",AutoExposureSpeedDown=").Append(F(Post.AutoExposureSpeedDown))
            .Append(",ExposureCompensation=").Append(F(Post.ExposureCompensation)).Append(",ToneMapper=ToneMapperKind.UnrealFilmic,BloomIntensity=").Append(F(Post.BloomIntensity))
            .Append(",AmbientOcclusionIntensity=").Append(F(Post.AmbientOcclusionIntensity)).Append(",AmbientOcclusionRadiusMeters=").Append(F(Post.AmbientOcclusionRadiusCm*0.01f)).AppendLine(" },");
        if(Lighting.VolumetricCloud){
            // M0110：テンプレートの VolumetricCloud は、既定の雲マテリアルを使ってコンポーネントの既定値を維持する。この箇所で設定する値も同じ既定値。
            Builder.AppendLine("            new VolumetricCloud(),");
        }
    }
    /// <summary>
    /// M0110：各スカイドームアクターは、その位置に <c>SkySphere</c> を持つノードになる。半径は配置済みメッシュの最遠頂点で決める（Unreal の SM_SkySphere は 40.96 m で、400 倍に拡大されるため 16.384 km）。
    /// </summary>
    internal static void AppendSkySpheres(StringBuilder Builder,ThirdPersonManifest Manifest,ImportedScene Scene,IReadOnlyDictionary<string,ImportedSceneNode>MeshNodeByName,IReadOnlySet<string>SkyDomeActors){
        foreach(var Actor in Manifest.Actors.Where(Item=>SkyDomeActors.Contains(Item.Label)).OrderBy(Item=>Item.Label,StringComparer.Ordinal)){
            var Node=MeshNodeByName[Actor.Label];
            var Mesh=Scene.Meshes[Node.MeshIndex!.Value];
            var Center=Node.WorldTransform.Translation;
            var Radius=Mesh.Positions.Max(Position=>Vector3.Distance(Vector3.Transform(Position,Node.WorldTransform),Center));
            Builder.AppendLine("            new Node {");
            Builder.Append("                Name=\"").Append(Escape(Actor.Label)).AppendLine("\",");
            Builder.AppendLine("                Components=[");
            AppendTransform(Builder,Center,Quaternion.Identity,Vector3.One,"                    ");
            Builder.Append("                    new SkySphere { RadiusMeters=").Append(F(Radius)).AppendLine(" },");
            Builder.AppendLine("                ],");
            Builder.AppendLine("            },");
        }
    }
    private static void AppendRgb(StringBuilder Builder,string Name,float[]Value)=>
        Builder.Append("                ").Append(Name).Append("R=").Append(F(Value[0])).Append(',').Append(Name).Append("G=").Append(F(Value[1])).Append(',').Append(Name).Append("B=").Append(F(Value[2])).AppendLine(",");
    private static void AppendAsset(StringBuilder Builder,string Member,string AssetPath)=>
        Builder.Append("    public const string ").Append(Member).Append("=\"").Append(Escape(AssetPath)).AppendLine("\";");
    private static string BuildLevelSource(ThirdPersonManifest Manifest,ImportedScene Scene,IReadOnlyDictionary<string,ImportedSceneNode>MeshNodeByName,ImportedSceneNode DirectionalNode,IReadOnlySet<string>SkyDomeActors)=>
        BuildLevelSource(Manifest,Scene,MeshNodeByName,DirectionalNode,SkyDomeActors,LevelNames.ThirdPerson);
    internal static string BuildLevelSource(ThirdPersonManifest Manifest,ImportedScene Scene,IReadOnlyDictionary<string,ImportedSceneNode>MeshNodeByName,ImportedSceneNode DirectionalNode,IReadOnlySet<string>SkyDomeActors,LevelNames Names){
        var Light=(ImportedDirectionalLight)Scene.Lights[DirectionalNode.LightIndex!.Value];
        var TravelDirection=ImportedLightDirection.WorldForward(DirectionalNode.WorldTransform);
        var Builder=new StringBuilder();
        Builder.AppendLine("using CSharpGameEngine.Entities;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Lighting;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Physics;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Windows;");
        Builder.AppendLine("namespace "+Names.Namespace+";");
        Builder.AppendLine("/// <summary>");
        Builder.AppendLine("/// UE5.8 "+Names.TemplateName+"テンプレートの"+Manifest.LevelAssetPath+"から取り込んだ宣言型レベル。");
        Builder.AppendLine("/// 各メッシュは描画三角形をそのまま静的コリジョンにする(UEの簡易コリジョン形状は移植しない)。");
        Builder.AppendLine("/// このC#を正本として編集する。再取り込みは"+Names.RunScript+" --forceを明示した場合だけ行う。");
        Builder.AppendLine("/// </summary>");
        Builder.AppendLine("[StartupScene(Order=1)]");
        Builder.AppendLine("internal partial class "+Names.LevelClass+":IScene {");
        Builder.AppendLine("    public Node Root=>new(){");
        Builder.AppendLine("        Name=\""+Names.LevelClass+"\",");
        Builder.AppendLine("        Components=[");
        AppendEnvironment(Builder,Manifest.Lighting!);
        Builder.AppendLine("        ],");
        Builder.AppendLine("        Children=[");
        foreach(var Actor in Manifest.Actors.Where(Item=>!SkyDomeActors.Contains(Item.Label)).OrderBy(Item=>Item.Label,StringComparer.Ordinal)){
            if(!Matrix4x4.Decompose(MeshNodeByName[Actor.Label].WorldTransform,out var Scale,out var Rotation,out var Translation)){
                throw new InvalidOperationException($"Could not decompose transform for '{Actor.Label}'.");
            }
            Builder.AppendLine("            new Node {");
            Builder.Append("                Name=\"").Append(Escape(Actor.Label)).AppendLine("\",");
            Builder.AppendLine("                Components=[");
            AppendTransform(Builder,Translation,Rotation,Scale,"                    ");
            Builder.Append("                    new StaticMesh { AssetPath="+Names.AssetsClass+".").Append(AssetMemberName(Actor.AssetPath)).Append(",Mobility=MeshMobility.").Append(Actor.Mobility).AppendLine(" },");
            Builder.AppendLine("                    new StaticMeshCollider(),");
            Builder.AppendLine("                ],");
            Builder.AppendLine("            },");
        }
        Builder.AppendLine("            new Node {");
        Builder.AppendLine("                Name=\"DirectionalLight\",");
        Builder.AppendLine("                Components=[");
        UnrealLightSceneEmitter.AppendDirectionalLightComponentsFromTravelDirection(Builder,TravelDirection,new Vector3(Light.Color.R,Light.Color.G,Light.Color.B),Light.Illuminance.Lux,"                    ");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        var Volume=GlobalIlluminationVolumeFitter.Fit(Manifest.Actors.Where(Item=>!SkyDomeActors.Contains(Item.Label)).Select(Item=>{
            var Node=MeshNodeByName[Item.Label];
            var Mesh=Scene.Meshes[Node.MeshIndex!.Value];
            return (Node.WorldTransform,Mesh.Positions,Mesh.Indices);
        }));
        Builder.AppendLine("            new Node {");
        Builder.AppendLine("                Name=\"GlobalIlluminationVolume\",");
        Builder.AppendLine("                Components=[");
        AppendTransform(Builder,Volume.Center,Quaternion.Identity,Vector3.One,"                    ");
        Builder.Append("                    new GlobalIlluminationVolume { HalfExtentX=").Append(F(Volume.HalfExtent.X)).Append(",HalfExtentY=").Append(F(Volume.HalfExtent.Y))
            .Append(",HalfExtentZ=").Append(F(Volume.HalfExtent.Z)).Append(",ProbeSpacingMeters=").Append(F(Volume.SpacingMeters)).AppendLine(" },");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        AppendSkySpheres(Builder,Manifest,Scene,MeshNodeByName,SkyDomeActors);
        var Player=Manifest.Environment.PlayerStart;
        Builder.AppendLine("            new Node {");
        Builder.Append("                Name=\"").Append(Escape(Player.Name)).AppendLine("\",");
        Builder.AppendLine("                Components=[");
        AppendTransform(Builder,new Vector3(Player.Translation[0],Player.Translation[1],Player.Translation[2]),Quaternion.CreateFromAxisAngle(Vector3.UnitY,ThirdPersonGameplayEmitter.FacingYawRadians(Player)),Vector3.One,"                    ");
        Builder.AppendLine("                    new PlayerStart(),");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        Builder.AppendLine("        ],");
        Builder.AppendLine("    };");
        Builder.AppendLine("}");
        return Builder.ToString();
    }
}
/// <summary>UEのAnimSequence書き出し(メッシュ・マテリアル同梱)を、ジョイント階層とアニメーションだけのglbへ縮約する。</summary>
internal static class AnimationGlbCompactor {
    internal static int CompactInPlace(string Path){
        var Source=ModelRoot.Load(Path);
        if(Source.LogicalAnimations.Count!=1){
            throw new InvalidOperationException($"'{Path}' must contain exactly one animation, found {Source.LogicalAnimations.Count}.");
        }
        if(Source.LogicalMeshes.Count==0&&Source.LogicalImages.Count==0){
            return Source.LogicalAnimations[0].Channels.Select(Channel=>Channel.TargetNode).Distinct().Count();
        }
        var Animation=Source.LogicalAnimations[0];
        var Joints=Source.LogicalSkins.Count==1?Source.LogicalSkins[0].Joints.ToHashSet():Animation.Channels.Select(Channel=>Channel.TargetNode).ToHashSet();
        var Target=ModelRoot.CreateModel();
        var TargetScene=Target.UseScene(0);
        var Copies=new Dictionary<SharpGLTF.Schema2.Node,SharpGLTF.Schema2.Node>();
        foreach(var Root in Joints.Where(Joint=>Joint.VisualParent is null||!Joints.Contains(Joint.VisualParent)).OrderBy(Joint=>Joint.LogicalIndex)){
            CopyJoint(Root,TargetScene.CreateNode(Root.Name),Joints,Copies);
        }
        var Copy=Target.CreateAnimation(Animation.Name);
        foreach(var Joint in Joints.OrderBy(Joint=>Joint.LogicalIndex)){
            var Node=Copies[Joint];
            if(Animation.FindTranslationChannel(Joint)?.GetTranslationSampler() is { }Translation){
                Copy.CreateTranslationChannel(Node,Keys(Translation.GetLinearKeys()),Translation.InterpolationMode!=AnimationInterpolationMode.STEP);
            }
            if(Animation.FindRotationChannel(Joint)?.GetRotationSampler() is { }Rotation){
                Copy.CreateRotationChannel(Node,Keys(Rotation.GetLinearKeys()),Rotation.InterpolationMode!=AnimationInterpolationMode.STEP);
            }
            if(Animation.FindScaleChannel(Joint)?.GetScaleSampler() is { }Scale){
                Copy.CreateScaleChannel(Node,Keys(Scale.GetLinearKeys()),Scale.InterpolationMode!=AnimationInterpolationMode.STEP);
            }
        }
        var Temporary=Path+".tmp";
        Target.SaveGLB(Temporary);
        File.Move(Temporary,Path,true);
        return Joints.Count;
    }
    private static void CopyJoint(SharpGLTF.Schema2.Node Source,SharpGLTF.Schema2.Node Target,HashSet<SharpGLTF.Schema2.Node>Joints,Dictionary<SharpGLTF.Schema2.Node,SharpGLTF.Schema2.Node>Copies){
        Target.LocalTransform=Source.LocalTransform;
        Copies[Source]=Target;
        foreach(var Child in Source.VisualChildren.Where(Joints.Contains).OrderBy(Child=>Child.LogicalIndex)){
            CopyJoint(Child,Target.CreateNode(Child.Name),Joints,Copies);
        }
    }
    private static Dictionary<float,T>Keys<T>(IEnumerable<(float Time,T Value)>Keys){
        var Result=new Dictionary<float,T>();
        foreach(var(Time,Value)in Keys){
            Result[Time]=Value;
        }
        return Result;
    }
}
internal sealed class ThirdPersonManifest {
    public int Version { get; set; }
    public string LevelAssetPath { get; set; }=string.Empty;
    public string StagingGlb { get; set; }=string.Empty;
    public List<ImportActor>Actors { get; set; }=[];
    public List<ImportVariant>RenderVariants { get; set; }=[];
    public ImportEnvironment Environment { get; set; }=new();
    public ThirdPersonCharacterManifest Character { get; set; }=new();
    public ThirdPersonLightingManifest?Lighting { get; set; }
    public JsonElement Input { get; set; }
}
internal sealed class ThirdPersonMeshManifest {
    public string AssetPath { get; set; }=string.Empty;
    public float[]RelativeLocationCm { get; set; }=[0f,0f,0f];
    public ThirdPersonRotatorManifest RelativeRotation { get; set; }=new();
}
internal sealed class ThirdPersonAnimationManifest {
    public string Name { get; set; }=string.Empty;
    public string AssetPath { get; set; }=string.Empty;
    public float LengthSeconds { get; set; }
}
internal sealed class ThirdPersonLightingManifest {
    public ThirdPersonSunManifest Sun { get; set; }=new();
    public ThirdPersonSkyLightManifest SkyLight { get; set; }=new();
    public ThirdPersonSkyAtmosphereManifest SkyAtmosphere { get; set; }=new();
    public ThirdPersonHeightFogManifest HeightFog { get; set; }=new();
    public ThirdPersonPostProcessManifest PostProcess { get; set; }=new();
    public bool VolumetricCloud { get; set; }
    public string SkyDomeMaterial { get; set; }=string.Empty;
}
internal sealed class ThirdPersonSunManifest {
    public float IntensityLux { get; set; }
    public float SourceAngleDegrees { get; set; }
}
internal sealed class ThirdPersonSkyLightManifest {
    public float Intensity { get; set; }
    public bool LowerHemisphereIsBlack { get; set; }
    public float[]LowerHemisphereColor { get; set; }=[0f,0f,0f];
}
internal sealed class ThirdPersonSkyAtmosphereManifest {
    public float BottomRadiusKm { get; set; }
    public float AtmosphereHeightKm { get; set; }
    public float[]GroundAlbedoLinear { get; set; }=[];
    public float[]RayleighScattering { get; set; }=[];
    public float RayleighScatteringScale { get; set; }
    public float RayleighExponentialDistributionKm { get; set; }
    public float[]MieScattering { get; set; }=[];
    public float MieScatteringScale { get; set; }
    public float[]MieAbsorption { get; set; }=[];
    public float MieAbsorptionScale { get; set; }
    public float MieAnisotropy { get; set; }
    public float MieExponentialDistributionKm { get; set; }
    public float[]OtherAbsorption { get; set; }=[];
    public float OtherAbsorptionScale { get; set; }
    public float OtherTentTipAltitudeKm { get; set; }
    public float OtherTentWidthKm { get; set; }
    public float MultiScatteringFactor { get; set; }
}
internal sealed class ThirdPersonHeightFogManifest {
    public float FogDensity { get; set; }
    public float FogHeightFalloff { get; set; }
    public float FogHeightMeters { get; set; }
    public float FogMaxOpacity { get; set; }
    public float[]InscatteringLuminance { get; set; }=[0f,0f,0f];
    public float SkyAtmosphereAmbientContributionScale { get; set; }
}
internal sealed class ThirdPersonPostProcessManifest {
    public string AutoExposureMethod { get; set; }="AEM_HISTOGRAM";
    public float MinEv100 { get; set; }
    /// <summary>マニフェストに適応速度のエクスポートがない場合に使う Unreal の既定値。</summary>
    public float AutoExposureSpeedUp { get; set; }=3f;
    public float AutoExposureSpeedDown { get; set; }=1f;
    public float MaxEv100 { get; set; }
    public float ExposureCompensation { get; set; }
    public float BloomIntensity { get; set; }
    public float AmbientOcclusionIntensity { get; set; }
    public float AmbientOcclusionRadiusCm { get; set; }
}
