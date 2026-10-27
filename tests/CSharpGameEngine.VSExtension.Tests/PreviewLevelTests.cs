using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>プレビューレベルテンプレート（M0127）：標準テンプレート、JSON、検証、ワイヤ変換。</summary>
public sealed class PreviewLevelTemplateTests {
    [Fact]
    public void BuiltIns_AreTheFourDocumentedTemplatesAndAreValid(){
        var All=PreviewLevelBuiltIns.All();
        Assert.Equal(new[]{ "default","studio","outdoor","neutral" },All.Select(static Template=>Template.Id));
        Assert.All(All,static Template=>{
            Assert.True(Template.BuiltIn);
            Assert.False(string.IsNullOrWhiteSpace(Template.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(Template.Description));
            Assert.Null(Template.Validate());
        });
        Assert.Equal(PreviewLevelBuiltIns.ReservedIds,All.Select(static Template=>Template.Id));
    }
    [Fact]
    public void Default_KeepsTheCurrentPreviewLook(){
        var Template=PreviewLevelBuiltIns.Default();
        Assert.Equal(PreviewBackgroundMode.RendererDefault,Template.Background.Mode);
        Assert.False(Template.Floor.Visible);
        Assert.True(Template.Environment.IsLegacy);
        Assert.Equal(0.4f,Template.Ambient.Intensity);
        var Sun=Assert.Single(Template.Lights);
        Assert.Equal(PreviewLightKind.Directional,Sun.Kind);
        Assert.Equal(2f,Sun.Intensity);
        Assert.True(Sun.CastsShadows);
        // ホストが従来固定していた向き（+Y）。
        var Forward=PreviewLevelWire.Forward(Sun.YawDegrees,Sun.PitchDegrees);
        Assert.Equal(0f,Forward.X,4);
        Assert.Equal(1f,Forward.Y,4);
        Assert.Equal(0f,Forward.Z,4);
        Assert.Equal(PreviewProjection.Perspective,Template.Camera.Projection);
        Assert.Equal(45f,Template.Camera.FovDegrees);
    }
    [Fact]
    public void Studio_HasKeyFillRimLightsAndAShadowReceivingFloor(){
        var Template=PreviewLevelBuiltIns.Studio();
        Assert.Equal(new[]{ "Key","Fill","Rim" },Template.Lights.Select(static Light=>Light.Name));
        Assert.All(Template.Lights,static Light=>Assert.Equal(PreviewLightKind.Directional,Light.Kind));
        Assert.True(Template.Lights[0].CastsShadows);
        Assert.True(Template.Floor.Visible);
        Assert.Equal(PreviewBackgroundMode.Solid,Template.Background.Mode);
        // キーが最も強く、フィルは弱い。
        Assert.True(Template.Lights[0].Intensity>Template.Lights[1].Intensity);
    }
    [Fact]
    public void Outdoor_UsesAPhysicalSkyWithTheThirdPersonLevelExposure(){
        var Template=PreviewLevelBuiltIns.Outdoor();
        Assert.True(Template.Environment.Atmosphere);
        Assert.True(Template.Environment.PhysicalExposure);
        Assert.Equal(0f,Template.Environment.ExposureEv100);
        Assert.Equal(1f,Template.Environment.ExposureCompensation);
        Assert.Equal(3f,Assert.Single(Template.Lights).Intensity);
        Assert.True(Template.Floor.Visible);
        Assert.False(Template.Environment.IsLegacy);
    }
    [Fact]
    public void Neutral_MinimizesLightingAndIsNotTheUnlitViewMode(){
        var Template=PreviewLevelBuiltIns.Neutral();
        Assert.False(Template.Floor.Visible);
        Assert.True(Template.Environment.IsLegacy);
        Assert.All(Template.Lights,static Light=>Assert.False(Light.CastsShadows));
        Assert.True(Template.Lights.Sum(static Light=>Light.Intensity)<1f);
        Assert.True(Template.Ambient.Intensity>0.5f);
    }
    [Fact]
    public void Reserved_IdsAreRecognizedCaseInsensitively(){
        Assert.True(PreviewLevelBuiltIns.IsReserved("Studio"));
        Assert.True(PreviewLevelBuiltIns.IsReserved("DEFAULT"));
        Assert.False(PreviewLevelBuiltIns.IsReserved("studio-copy"));
        Assert.False(PreviewLevelBuiltIns.IsReserved(null));
        Assert.False(PreviewLevelBuiltIns.IsReserved(""));
    }
    [Fact]
    public void Json_RoundTripsEveryBuiltInWithStringEnums(){
        foreach(var Template in PreviewLevelBuiltIns.All()){
            var Json=Template.ToJson();
            Assert.Contains("\"Kind\": \"Directional\"",Json);
            Assert.DoesNotContain("\"Id\"",Json);
            Assert.DoesNotContain("BuiltIn",Json);
            var Parsed=PreviewLevelTemplate.TryParse(Json,out var Error);
            Assert.NotNull(Parsed);
            Assert.Null(Error);
            Assert.Equal(Json,Parsed!.ToJson());
        }
    }
    [Fact]
    public void Json_ToleratesCommentsTrailingCommasAndUnknownMembers(){
        const string Json="""
            {
              // a hand-edited file
              "SchemaVersion": 1,
              "DisplayName": "Hand made",
              "FutureThing": { "x": 1 },
              "Lights": [ { "Kind": "Point", "Intensity": 500, "PositionRadii": [1,2,3], "RangeRadii": 6, }, ],
            }
            """;
        var Template=PreviewLevelTemplate.TryParse(Json,out var Error);
        Assert.True(Template is not null,Error);
        Assert.Equal("Hand made",Template!.DisplayName);
        Assert.Equal(PreviewLightKind.Point,Assert.Single(Template.Lights).Kind);
        Assert.Equal(PreviewBackgroundMode.RendererDefault,Template.Background.Mode);
    }
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{ \"SchemaVersion\": 1 }")]
    [InlineData("{ \"SchemaVersion\": 2, \"DisplayName\": \"x\" }")]
    [InlineData("{ \"SchemaVersion\": 0, \"DisplayName\": \"x\" }")]
    [InlineData("{ \"SchemaVersion\": 1, \"DisplayName\": \"x\", \"Lights\": [ { \"Kind\": \"Banana\" } ] }")]
    [InlineData("{ \"SchemaVersion\": 1, \"DisplayName\": \"x\", \"Background\": { \"Color\": [1,2] } }")]
    [InlineData("{ \"SchemaVersion\": 1, \"DisplayName\": \"x\", \"Background\": { \"Mode\": 99 } }")]
    public void Json_RejectsBrokenFilesWithAReasonInsteadOfThrowing(string Json){
        var Template=PreviewLevelTemplate.TryParse(Json,out var Error);
        Assert.Null(Template);
        Assert.False(string.IsNullOrWhiteSpace(Error));
    }
    private static readonly (Action<PreviewLevelTemplate> Change,string Fragment)[]InvalidCases=[
        ((Action<PreviewLevelTemplate>)(static T=>T.DisplayName=" "),"DisplayName"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Ambient.Intensity=-1f),"Ambient"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Ambient.Color=[1f,1f]),"Ambient"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Background.Color=[2f,0f,0f]),"Background"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights[0].Intensity=float.NaN),"Light 0"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights[0].PitchDegrees=120f),"Light 0"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights=Enumerable.Range(0,5).Select(static _=>new PreviewLight()).ToList()),"directional"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights=Enumerable.Range(0,9).Select(static _=>new PreviewLight{ Kind=PreviewLightKind.Point }).ToList()),"point"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights=Enumerable.Range(0,9).Select(static _=>new PreviewLight{ Kind=PreviewLightKind.Spot }).ToList()),"spot"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights=[new PreviewLight{ Kind=PreviewLightKind.Spot,InnerConeDegrees=50f,OuterConeDegrees=40f }]),"cone"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights=[new PreviewLight{ Kind=PreviewLightKind.Point,RangeRadii=0f }]),"range"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights=[new PreviewLight{ Kind=PreviewLightKind.Point,PositionRadii=[1f] }]),"position"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Lights=[null!]),"Light 0"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Environment.ExposureEv100=100f),"Environment"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Environment.BloomIntensity=-1f),"Environment"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Floor.SizeRadii=0f),"Floor"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Floor.Color=[0f,0f,2f]),"Floor"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Floor.GridSizeMeters=-1f),"Floor"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Camera.FovDegrees=1f),"Camera"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Camera.ElevationDegrees=85f),"Camera"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Camera.Projection=(PreviewProjection)9),"Camera"),
        ((Action<PreviewLevelTemplate>)(static T=>T.Background=null!),"missing"),
    ];
    public static IEnumerable<object[]>InvalidCaseIndices(){
        for(var Index=0;Index<InvalidCases.Length;Index++){ yield return [Index]; }
    }
    [Theory]
    [MemberData(nameof(InvalidCaseIndices))]
    public void Validation_RejectsOutOfRangeValues(int CaseIndex){
        var (Change,Fragment)=InvalidCases[CaseIndex];
        var Template=PreviewLevelBuiltIns.Studio();
        Change(Template);
        Assert.Contains(Fragment,Assert.IsType<string>(Template.Validate()),StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Clone_IsIndependentAndKeepsIdentity(){
        var Original=PreviewLevelBuiltIns.Studio();
        var Copy=Original.Clone();
        Copy.Lights[0].Intensity=99f;
        Copy.DisplayName="Changed";
        Assert.NotEqual(99f,Original.Lights[0].Intensity);
        Assert.Equal(PreviewLevelBuiltIns.StudioId,Copy.Id);
        Assert.True(Copy.BuiltIn);
    }
}
public sealed class PreviewLevelWireTests {
    private static PreviewBounds Bounds(float Radius,Vector3?Center=null)=>new(new Vector3(-Radius,-Radius,-Radius)+(Center??Vector3.Zero),new Vector3(Radius,Radius,Radius)+(Center??Vector3.Zero),Center??Vector3.Zero,Radius);
    [Theory]
    [InlineData(0f,-90f,0f,-1f,0f)]
    [InlineData(0f,90f,0f,1f,0f)]
    [InlineData(90f,0f,1f,0f,0f)]
    [InlineData(0f,0f,0f,0f,1f)]
    [InlineData(180f,0f,0f,0f,-1f)]
    public void Forward_FollowsTheSceneCameraConvention(float Yaw,float Pitch,float X,float Y,float Z){
        var Forward=PreviewLevelWire.Forward(Yaw,Pitch);
        Assert.Equal(X,Forward.X,4);
        Assert.Equal(Y,Forward.Y,4);
        Assert.Equal(Z,Forward.Z,4);
        Assert.Equal(1f,Forward.Length(),4);
        var Scene=SceneViewCamera.GetForward(Yaw*FloatMath.Pi/180f,Pitch*FloatMath.Pi/180f);
        Assert.Equal(Scene.X,Forward.X,3);
        Assert.Equal(Scene.Y,Forward.Y,3);
        Assert.Equal(Scene.Z,Forward.Z,3);
    }
    [Fact]
    public void DefaultTemplate_MapsToTheLegacyHostLighting(){
        var Lighting=PreviewLevelWire.CreateLighting(PreviewLevelBuiltIns.Default(),Bounds(1f),7);
        Assert.Equal(7,Lighting.EnvironmentRevision);
        Assert.Equal(new[]{ 0.4f,0.4f,0.4f },Lighting.Ambient);
        Assert.Null(Lighting.Background);
        Assert.Null(Lighting.Environment);
        var Sun=Assert.Single(Lighting.Directionals);
        Assert.Equal(2f,Sun.IlluminanceLux);
        Assert.True(Sun.CastsShadows);
        Assert.Equal(0.0015f,Sun.ShadowBiasMeters,6);
        Assert.Equal(0f,Sun.Forward[0],4);
        Assert.Equal(1f,Sun.Forward[1],4);
        Assert.Equal(0f,Sun.Forward[2],4);
        Assert.Empty(Lighting.Points);
        Assert.Empty(Lighting.Spots);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public void DirectionalLightDoesNotRequireALocalPosition(string Position){
        var Json="{ \"DisplayName\": \"Sun\", \"Lights\": [{ \"Kind\": \"Directional\", \"PositionRadii\": "+Position+" }] }";
        var Template=PreviewLevelTemplate.TryParse(Json,out var Error);
        Assert.True(Template is not null,Error);
        var Lighting=PreviewLevelWire.CreateLighting(Template!,Bounds(1f),1);
        Assert.Single(Lighting.Directionals);
        Assert.Null(RenderPreviewLightingValidation.Validate(Lighting));
    }
    [Fact]
    public void SolidBackgroundAndEnvironmentAreCopiedNotShared(){
        var Template=PreviewLevelBuiltIns.Outdoor();
        Template.Background=new(){ Mode=PreviewBackgroundMode.Solid,Color=[0.1f,0.2f,0.3f] };
        var Lighting=PreviewLevelWire.CreateLighting(Template,Bounds(1f),1);
        Assert.Equal(new[]{ 0.1f,0.2f,0.3f },Lighting.Background);
        Assert.NotSame(Template.Background.Color,Lighting.Background);
        var Environment=Assert.IsType<RenderPreviewEnvironment>(Lighting.Environment);
        Assert.True(Environment.Atmosphere);
        Assert.True(Environment.PhysicalExposure);
        Assert.Equal(1f,Environment.ExposureCompensation);
        Assert.True(Environment.FilmicToneMapper);
        Assert.Equal(0.675f,Environment.BloomIntensity,4);
        Assert.True(Environment.AmbientOcclusion);
        Assert.Equal(0f,Lighting.Ambient[0]);
    }
    [Fact]
    public void LocalLightsScaleWithTheAssetRadius(){
        var Template=new PreviewLevelTemplate { DisplayName="Local" };
        Template.Lights=[
            new PreviewLight { Kind=PreviewLightKind.Point,Intensity=100f,PositionRadii=[1f,2f,3f],RangeRadii=6f,CastsShadows=true },
            new PreviewLight { Kind=PreviewLightKind.Spot,Intensity=200f,PositionRadii=[0f,4f,0f],YawDegrees=0f,PitchDegrees=-90f,RangeRadii=10f,InnerConeDegrees=20f,OuterConeDegrees=50f },
        ];
        var Small=PreviewLevelWire.CreateLighting(Template,Bounds(1f,new Vector3(10f,0f,0f)),1);
        var Big=PreviewLevelWire.CreateLighting(Template,Bounds(3f,new Vector3(10f,0f,0f)),1);
        var SmallPoint=Assert.Single(Small.Points);
        var BigPoint=Assert.Single(Big.Points);
        Assert.Equal(new[]{ 11f,2f,3f },SmallPoint.Position);
        Assert.Equal(new[]{ 13f,6f,9f },BigPoint.Position);
        Assert.Equal(100f,SmallPoint.FluxLumens,3);
        Assert.Equal(900f,BigPoint.FluxLumens,3);
        Assert.Equal(6f,SmallPoint.RangeMeters,3);
        Assert.Equal(18f,BigPoint.RangeMeters,3);
        Assert.True(BigPoint.ShadowBiasMeters>SmallPoint.ShadowBiasMeters);
        var Spot=Assert.Single(Big.Spots);
        Assert.Equal(-1f,Spot.Forward[1],4);
        Assert.Equal(20f,Spot.InnerConeDegrees);
        Assert.Equal(50f,Spot.OuterConeDegrees);
        Assert.Equal(1800f,Spot.FluxLumens,3);
    }
    [Theory]
    [InlineData(0.001f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(40f)]
    [InlineData(1000f)]
    public void EveryBuiltInProducesLightingTheHostAccepts(float Radius){
        foreach(var Template in PreviewLevelBuiltIns.All()){
            var Lighting=PreviewLevelWire.CreateLighting(Template,Bounds(Radius),1);
            Assert.Null(RenderPreviewLightingValidation.Validate(Lighting));
        }
    }
    [Fact]
    public void ALightingFromAMaximalTemplateStillPassesTheProtocolLimits(){
        var Template=new PreviewLevelTemplate { DisplayName="Max" };
        Template.Lights=[
            ..Enumerable.Range(0,PreviewLevelTemplate.MaximumDirectionalLights).Select(static _=>new PreviewLight{ Kind=PreviewLightKind.Directional }),
            ..Enumerable.Range(0,PreviewLevelTemplate.MaximumPointLights).Select(static _=>new PreviewLight{ Kind=PreviewLightKind.Point }),
            ..Enumerable.Range(0,PreviewLevelTemplate.MaximumSpotLights).Select(static _=>new PreviewLight{ Kind=PreviewLightKind.Spot }),
        ];
        Assert.Null(Template.Validate());
        Assert.Null(RenderPreviewLightingValidation.Validate(PreviewLevelWire.CreateLighting(Template,Bounds(1f),1)));
        Assert.Equal(PreviewLevelTemplate.MaximumDirectionalLights,RenderPreviewLightingValidation.MaximumDirectionalLights);
        Assert.Equal(PreviewLevelTemplate.MaximumPointLights,RenderPreviewLightingValidation.MaximumPointLights);
        Assert.Equal(PreviewLevelTemplate.MaximumSpotLights,RenderPreviewLightingValidation.MaximumSpotLights);
    }
    [Theory]
    [InlineData(0.87f,0.1f)]
    [InlineData(1f,1f)]
    [InlineData(9.99f,1f)]
    [InlineData(10f,10f)]
    [InlineData(250f,100f)]
    [InlineData(0.01f,0.01f)]
    [InlineData(0f,1f)]
    [InlineData(-5f,1f)]
    public void AutoGridSize_IsAPowerOfTenNearTheAssetSize(float Radius,float Expected)=>
        Assert.Equal(Expected,PreviewLevelWire.AutoGridSizeMeters(Radius),5);
}
public sealed class PreviewSceneComposerTests {
    private static SimpleMesh Box(float Half=0.5f,float Lift=0f){
        Vector3[]Positions=[new(-Half,-Half+Lift,-Half),new(Half,-Half+Lift,-Half),new(Half,Half+Lift,-Half),new(-Half,Half+Lift,Half)];
        return new SimpleMesh {
            Positions=Positions,Normals=[Vector3.UnitY,Vector3.UnitY,Vector3.UnitY,Vector3.UnitY],Indices=[0u,1u,2u,0u,2u,3u],
            BaseColorFactor=new Vector4(0.6f,0.6f,0.6f,1f),BoundsCenter=new Vector3(0f,Lift,0f),BoundsRadius=Half*1.7f,
        };
    }
    private static PreviewSceneComposition Compose(PreviewLevelTemplate Template,SimpleMesh?Mesh=null){
        var Subject=Mesh??Box();
        return PreviewSceneComposer.Compose(Subject,PreviewBounds.From(Subject,AssetMeshStatistics.From(Subject)),Template,128,96);
    }
    private static RenderPreviewHostRequest AsOpen(RenderPreviewScene Scene)=>new(){
        RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,SurfaceHandle=1,Width=128,Height=96,ShaderArtifactPath="Stage.cgeshaderartifact",Scene=Scene,
    };
    [Fact]
    public void StaticBoundsUseAllExtentsAndRejectInvalidHostData(){
        var Source=new RenderPreviewAssetBounds { Min=[-4f,0f,0f],Max=[5f,4f,0f],VertexCount=6,TriangleCount=2 };
        Assert.True(PreviewBounds.TryFrom(Source,out var Bounds));
        Assert.Equal(new Vector3(0.5f,2f,0f),Bounds.Center);
        Assert.True(Bounds.Radius>4.5f);
        var Statistics=AssetMeshStatistics.From(Source,Bounds);
        Assert.Equal(6,Statistics.VertexCount);
        Assert.Equal(2,Statistics.TriangleCount);
        Assert.False(PreviewBounds.TryFrom(null,out _));
        Assert.False(PreviewBounds.TryFrom(new RenderPreviewAssetBounds { Min=[0f,0f],Max=[1f,1f,1f] },out _));
        Assert.False(PreviewBounds.TryFrom(new RenderPreviewAssetBounds { Min=[2f,0f,0f],Max=[1f,1f,1f] },out _));
        Assert.False(PreviewBounds.TryFrom(new RenderPreviewAssetBounds { Min=[float.NaN,0f,0f],Max=[1f,1f,1f] },out _));
        Assert.False(PreviewBounds.TryFrom(new RenderPreviewAssetBounds { Min=[0f,0f,0f],Max=[1f,1f,1f],VertexCount=-1 },out _));
    }
    [Fact]
    public void StaticHostAssetKeepsOnlyTheFloorInTheExplicitRoster(){
        var Subject=Box();
        var Bounds=PreviewBounds.From(Subject,AssetMeshStatistics.From(Subject));
        var Path=@"C:\Game\Assets\Box.glb";
        var Composition=PreviewSceneComposer.Compose(null,Bounds,PreviewLevelBuiltIns.Studio(),128,96,StaticAssetPath:Path);
        Assert.Equal(Path,Composition.Scene.StaticAssetPath);
        Assert.Null(Composition.Scene.Asset);
        Assert.Equal(PreviewSceneComposer.FloorObjectId,Assert.Single(Composition.Scene.Objects).ObjectId);
        Assert.Equal(PreviewSceneComposer.FloorObjectId,Assert.Single(Composition.Objects).ObjectId);
        Assert.Null(RenderPreviewHostProtocol.Validate(AsOpen(Composition.Scene)));
    }
    [Fact]
    public void WithoutAFloor_OnlyTheAssetIsInTheScene(){
        var Composition=Compose(PreviewLevelBuiltIns.Default());
        Assert.False(Composition.HasFloor);
        Assert.Equal(string.Empty,Composition.FloorSignature);
        Assert.Single(Composition.Scene.Objects);
        Assert.Single(Composition.Scene.Meshes);
        Assert.Equal(PreviewSceneComposer.AssetObjectId,Assert.Single(Composition.Objects).ObjectId);
        Assert.Null(RenderPreviewHostProtocol.Validate(AsOpen(Composition.Scene)));
    }
    [Fact]
    public void WithAFloor_TheSceneHasTheAssetThenTheFloorAndThePerFrameRosterMatches(){
        var Composition=Compose(PreviewLevelBuiltIns.Studio());
        Assert.True(Composition.HasFloor);
        Assert.Equal(2,Composition.Scene.Objects.Length);
        Assert.Equal(2,Composition.Scene.Meshes.Length);
        Assert.Equal(2,Composition.Scene.Materials.Length);
        Assert.Equal(Composition.Scene.Objects.Select(static Object=>Object.ObjectId),Composition.Objects.Select(static Object=>Object.ObjectId));
        Assert.Equal(new[]{ PreviewSceneComposer.AssetObjectId,PreviewSceneComposer.FloorObjectId },Composition.Objects.Select(static Object=>Object.ObjectId));
        Assert.Null(RenderPreviewHostProtocol.Validate(AsOpen(Composition.Scene)));
        var Grid=Composition.Scene.Materials[1].WorldGrid;
        Assert.NotNull(Grid);
        Assert.True(Grid!.GridSizeMeters>0f);
    }
    [Fact]
    public void TheFloorSitsJustBelowTheAssetAndFacesUp(){
        var Subject=Box(0.5f,Lift:2f);
        var Composition=Compose(PreviewLevelBuiltIns.Studio(),Subject);
        var Floor=Composition.Scene.Objects[1];
        var Y=Floor.World.Values[13];
        Assert.True(Y<1.5f,$"floor y={Y}");
        Assert.True(Y>1.5f-0.05f,$"floor y={Y}");
        var Mesh=Composition.Scene.Meshes[Floor.MeshIndex];
        Assert.Equal(new[]{ 0f,1f,0f,0f,1f,0f,0f,1f,0f,0f,1f,0f },Mesh.Normals);
        Assert.All(Enumerable.Range(0,4),Index=>Assert.Equal(0f,Mesh.Positions[Index*3+1]));
        Assert.Equal(6,Mesh.Indices.Length);
    }
    [Fact]
    public void TheFloorMatchesTheEnginePlaneVertexOrder(){
        var Mesh=PreviewSceneComposer.FloorMesh(1f,2f,3f);
        Assert.Equal(new[]{ -2f,0f,-1f, 4f,0f,-1f, 4f,0f,5f, -2f,0f,5f },Mesh.Positions);
        Assert.Equal(new uint[]{ 0,1,2,0,2,3 },Mesh.Indices);
    }
    [Fact]
    public void ChangingOnlyLightsBackgroundOrAmbientNeverRebuildsTheScene(){
        var Base=PreviewLevelBuiltIns.Studio();
        var Signature=Compose(Base).FloorSignature;
        var Changed=Base.Clone();
        Changed.Lights[0].Intensity=9f;
        Changed.Lights.Add(new PreviewLight { Kind=PreviewLightKind.Point });
        Changed.Background=new(){ Mode=PreviewBackgroundMode.Solid,Color=[0.9f,0.1f,0.1f] };
        Changed.Ambient.Intensity=0.9f;
        Changed.Environment.Atmosphere=true;
        Changed.Camera.FovDegrees=70f;
        Assert.Equal(Signature,Compose(Changed).FloorSignature);
        Assert.Equal(Signature,Compose(Base.Clone()).FloorSignature);
    }
    [Theory]
    [InlineData("color")]
    [InlineData("size")]
    [InlineData("grid")]
    [InlineData("gridcolor")]
    [InlineData("roughness")]
    [InlineData("gridsize")]
    public void ChangingTheFloorChangesTheSignature(string What){
        var Base=PreviewLevelBuiltIns.Studio();
        var Changed=Base.Clone();
        switch(What){
            case "color": Changed.Floor.Color=[0.9f,0.1f,0.1f]; break;
            case "size": Changed.Floor.SizeRadii=5f; break;
            case "grid": Changed.Floor.Grid=false; break;
            case "gridcolor": Changed.Floor.GridColor=[1f,0f,0f]; break;
            case "roughness": Changed.Floor.Roughness=0.2f; break;
            case "gridsize": Changed.Floor.GridSizeMeters=0.25f; break;
        }
        Assert.NotEqual(Compose(Base).FloorSignature,Compose(Changed).FloorSignature);
    }
    [Fact]
    public void ToggleTheFloorOffAndOnAgainGivesTheSameSignature(){
        var Template=PreviewLevelBuiltIns.Studio();
        var Original=Compose(Template).FloorSignature;
        Template.Floor.Visible=false;
        Assert.Equal(string.Empty,Compose(Template).FloorSignature);
        Template.Floor.Visible=true;
        Assert.Equal(Original,Compose(Template).FloorSignature);
    }
    [Fact]
    public void ADifferentAssetSizeChangesTheFloor(){
        var Template=PreviewLevelBuiltIns.Studio();
        Assert.NotEqual(Compose(Template,Box(0.5f)).FloorSignature,Compose(Template,Box(5f)).FloorSignature);
    }
    [Fact]
    public void TheAssetObjectIsNotModifiedByTheTemplate(){
        var Subject=Box();
        var Before=Subject.Positions.ToArray();
        _=Compose(PreviewLevelBuiltIns.Studio(),Subject);
        Assert.Equal(Before,Subject.Positions);
        var Composition=Compose(PreviewLevelBuiltIns.Studio(),Subject);
        Assert.Equal(4,Composition.Scene.Meshes[0].Positions.Length/3);
    }
}
public sealed class PreviewCameraDefaultsTests {
    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void LargeFiniteYawFramesLikeTheEquivalentAngle(float Yaw){
        var Template=PreviewLevelBuiltIns.Default();
        Template.Camera.YawDegrees=Yaw;
        Assert.Null(Template.Validate());
        var Actual=AssetViewportCamera.ViewDirection(Template.Camera);
        var Expected=AssetViewportCamera.ViewDirection(new PreviewCameraDefaults { YawDegrees=Yaw%360f });
        Assert.Equal(Expected.X,Actual.X,4);
        Assert.Equal(Expected.Y,Actual.Y,4);
        Assert.Equal(Expected.Z,Actual.Z,4);
        var Camera=new AssetViewportCamera();
        Camera.Frame(Vector3.Zero,1f,1.5f,Template.Camera);
        Assert.False(float.IsNaN(Camera.Eye.X)||float.IsInfinity(Camera.Eye.X));
    }
    [Fact]
    public void TheDefaultViewDirectionMatchesTheClassicPreview(){
        var Direction=AssetViewportCamera.ViewDirection(new PreviewCameraDefaults());
        var Classic=AssetViewportCamera.DefaultViewDirection;
        Assert.Equal(Classic.X,Direction.X,2);
        Assert.Equal(Classic.Y,Direction.Y,2);
        Assert.Equal(Classic.Z,Direction.Z,2);
    }
    [Fact]
    public void FramingWithDefaultsAppliesTheViewFovAndProjection(){
        var Camera=new AssetViewportCamera();
        Camera.Frame(Vector3.Zero,1f,1.5f,new PreviewCameraDefaults { YawDegrees=90f,ElevationDegrees=0f,FovDegrees=30f,Projection=PreviewProjection.Orthographic });
        Assert.True(Camera.IsOrthographic);
        Assert.Equal(30f*FloatMath.Pi/180f,Camera.FovYRadians,4);
        // 視点は +X 側にあり、原点を見ている。
        Assert.True(Camera.Eye.X>1f);
        Assert.Equal(0f,Camera.Eye.Y,3);
        Assert.Equal(0f,Camera.Projection(1.5f).M34);
        Camera.Frame(Vector3.Zero,1f,1.5f,new PreviewCameraDefaults());
        Assert.False(Camera.IsOrthographic);
        Assert.Equal(-1f,Camera.Projection(1.5f).M34);
    }
    [Fact]
    public void ExplicitResetReturnsToTheTemplateViewAfterTheUserOrbited(){
        var Defaults=new PreviewCameraDefaults();
        var Camera=new AssetViewportCamera();
        Camera.Frame(Vector3.Zero,1f,1.5f,Defaults);
        var Yaw=Camera.Yaw;
        var Distance=Camera.FocusDistance;
        Camera.Orbit(120f,-40f);
        Camera.Dolly(5);
        Assert.NotEqual(Yaw,Camera.Yaw);
        Camera.Frame(Vector3.Zero,1f,1.5f,Defaults);
        Assert.Equal(Yaw,Camera.Yaw,4);
        Assert.Equal(Distance,Camera.FocusDistance,3);
    }
    [Fact]
    public void OrthographicWheelZoomsTheViewVolumeInsteadOfDollying(){
        var Camera=new AssetViewportCamera();
        Camera.Frame(Vector3.Zero,2f,1f,new PreviewCameraDefaults { Projection=PreviewProjection.Orthographic });
        var Distance=Camera.FocusDistance;
        var Before=Camera.Projection(1f).M11;
        Camera.Dolly(3);
        Assert.Equal(Distance,Camera.FocusDistance,4);
        Assert.NotEqual(Before,Camera.Projection(1f).M11);
        var AfterWheel=Camera.Projection(1f).M11;
        Camera.DollyByPixels(100f);
        Assert.NotEqual(AfterWheel,Camera.Projection(1f).M11);
        Assert.Equal(Distance,Camera.FocusDistance,4);
    }
}
