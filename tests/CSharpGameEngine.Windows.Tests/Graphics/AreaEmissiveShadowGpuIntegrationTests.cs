using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// WARP regressions for the Area/Emissive center-projection shadow path. These tests keep the
/// source on its -Y center axis so an off-axis projection error cannot be mistaken for a PCSS
/// penumbra failure.
/// </summary>
public sealed class AreaEmissiveShadowGpuIntegrationTests {
    private static readonly string ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.hlsl");
    private static readonly Vector3 AreaSourcePosition=new(0f,8f,0f);
    private static readonly Vector3 AreaForward=-Vector3.UnitY;
    private static readonly Vector3 BlockerPosition=new(1.5f,4f,0f);
    private static readonly Vector3 ShadowReceiver=new(3f,0f,0f);
    private static readonly Vector3[]PenumbraReceivers=[
        new(2.5f,0f,0f),
        new(3f,0f,0f),
        new(3.5f,0f,0f),
        new(4f,0f,0f),
        new(4.5f,0f,0f),
        new(5f,0f,0f),
        new(5.5f,0f,0f),
    ];
    private static readonly Vector3 FarAway=new(500f,500f,500f);

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_AreaLight_BlockerCastsShadowAndLargerSourceSoftensPenumbra(){
        RunWithTimeout(RunAreaShadowAndPenumbraScenario,TimeSpan.FromSeconds(120));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_EmissivePatch_ProducesDirectLightAndCenterProjectionShadow(){
        RunWithTimeout(RunEmissivePatchShadowScenario,TimeSpan.FromSeconds(120));
    }

    private static void RunAreaShadowAndPenumbraScenario(){
        using ShadowScene Scene=CreateShadowScene();
        Camera Camera=CreateTopDownCamera(new Vector3(4f,0f,0f));

        PixelReadbackResult UnoccludedMedium=Render(
            Scene,Camera,FarAway,
            static Renderer=>SubmitArea(Renderer,LuminanceNits:20f,WidthMeters:2f,HeightMeters:2f));
        PixelReadbackResult ShadowedMedium=Render(
            Scene,Camera,BlockerPosition,
            static Renderer=>SubmitArea(Renderer,LuminanceNits:20f,WidthMeters:2f,HeightMeters:2f));

        PixelColor UnoccludedCenter=ReadWorldPixel(UnoccludedMedium,Camera,ShadowReceiver);
        PixelColor ShadowedCenter=ReadWorldPixel(ShadowedMedium,Camera,ShadowReceiver);
        int UnoccludedCenterSum=PixelSum(UnoccludedCenter);
        int ShadowedCenterSum=PixelSum(ShadowedCenter);
        Assert.True(UnoccludedCenterSum>30,
            $"Expected the unoccluded Area receiver to be visibly lit: {UnoccludedCenter} (sum={UnoccludedCenterSum}).");
        Assert.True(ShadowedCenterSum<UnoccludedCenterSum-15,
            $"Expected the Area center projection to darken the blocked receiver: unoccluded={UnoccludedCenter} (sum={UnoccludedCenterSum}), shadowed={ShadowedCenter} (sum={ShadowedCenterSum}).");

        // Keep L * area exactly equal (80 nit-m²) so a change in the normalized receiver
        // visibility can only come from the source extent used by PCSS, not a brighter direct
        // source. The small source should retain a hard umbra near the projected blocker edge;
        // the large source must admit partial visibility across a wider penumbra.
        PixelReadbackResult SmallUnoccluded=Render(
            Scene,Camera,FarAway,
            static Renderer=>SubmitArea(Renderer,LuminanceNits:320f,WidthMeters:0.5f,HeightMeters:0.5f));
        PixelReadbackResult SmallShadowed=Render(
            Scene,Camera,BlockerPosition,
            static Renderer=>SubmitArea(Renderer,LuminanceNits:320f,WidthMeters:0.5f,HeightMeters:0.5f));
        PixelReadbackResult LargeUnoccluded=Render(
            Scene,Camera,FarAway,
            static Renderer=>SubmitArea(Renderer,LuminanceNits:5f,WidthMeters:4f,HeightMeters:4f));
        PixelReadbackResult LargeShadowed=Render(
            Scene,Camera,BlockerPosition,
            static Renderer=>SubmitArea(Renderer,LuminanceNits:5f,WidthMeters:4f,HeightMeters:4f));

        float MaximumVisibilityIncrease=float.NegativeInfinity;
        List<string>Profile=[];
        foreach(Vector3 Receiver in PenumbraReceivers){
            PixelColor SmallUnoccludedPixel=ReadWorldPixel(SmallUnoccluded,Camera,Receiver);
            PixelColor SmallShadowedPixel=ReadWorldPixel(SmallShadowed,Camera,Receiver);
            PixelColor LargeUnoccludedPixel=ReadWorldPixel(LargeUnoccluded,Camera,Receiver);
            PixelColor LargeShadowedPixel=ReadWorldPixel(LargeShadowed,Camera,Receiver);
            float SmallVisibility=CalculateVisibility(SmallShadowedPixel,SmallUnoccludedPixel,Receiver,"small");
            float LargeVisibility=CalculateVisibility(LargeShadowedPixel,LargeUnoccludedPixel,Receiver,"large");
            MaximumVisibilityIncrease=MathF.Max(MaximumVisibilityIncrease,LargeVisibility-SmallVisibility);
            Profile.Add($"x={Receiver.X:0.0}: small={SmallVisibility:0.000}, large={LargeVisibility:0.000}");
        }

        Assert.True(MaximumVisibilityIncrease>0.05f,
            "Expected the 4m rectangle to create a measurably wider soft shadow than the 0.5m "
            +$"rectangle at equal L*area. Maximum normalized visibility increase was {MaximumVisibilityIncrease:0.000}. "
            +string.Join("; ",Profile));
        Assert.Equal(0,Scene.Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunEmissivePatchShadowScenario(){
        using ShadowScene Scene=CreateShadowScene();
        Camera Camera=CreateTopDownCamera(new Vector3(3f,0f,0f));
        Matrix4x4 EmitterObjectWorld=Matrix4x4.CreateTranslation(AreaSourcePosition);
        const float AuthoredEmissiveNits=40f;
        const float EmissiveTextureLuminanceScale=0.5f;
        Assert.NotEqual(Matrix4x4.Identity,EmitterObjectWorld);
        IReadOnlyList<EmissivePatch>Patches=BuildDownwardEmissivePatch(
            EmitterObjectWorld,AreaForward,WidthMeters:2f,HeightMeters:2f,
            AuthoredEmissiveNits,EmissiveTextureLuminanceScale);
        EmissivePatch Patch=Assert.Single(Patches);
        Assert.True(Vector3.Distance(Patch.Center,AreaSourcePosition)<1e-4f,
            $"Expected ObjectWorld to move the local emissive patch to {AreaSourcePosition}, got {Patch.Center}.");
        Assert.True(Vector3.Dot(Patch.Forward,AreaForward)>0.999f,
            $"Expected the transformed emissive forward to remain {AreaForward}, got {Patch.Forward}.");
        Assert.True(Vector3.Dot(Patch.Right,-Vector3.UnitZ)>0.999f
            &&Vector3.Dot(Patch.Up,Vector3.UnitX)>0.999f
            &&Vector3.Dot(Vector3.Cross(Patch.Right,Patch.Up),Patch.Forward)>0.999f,
            $"Expected the transformed emissive rectangle basis to remain right-handed; right={Patch.Right}, up={Patch.Up}, forward={Patch.Forward}.");
        Assert.Equal(20f,Patch.LuminanceNits,precision:4);

        PixelReadbackResult Unlit=Render(Scene,Camera,FarAway,static _=>{ });
        PixelReadbackResult Unoccluded=Render(
            Scene,Camera,FarAway,
            Renderer=>SubmitEmissivePatches(Renderer,Patches));
        PixelReadbackResult Shadowed=Render(
            Scene,Camera,BlockerPosition,
            Renderer=>SubmitEmissivePatches(Renderer,Patches));

        PixelColor UnlitReceiver=ReadWorldPixel(Unlit,Camera,ShadowReceiver);
        PixelColor UnoccludedReceiver=ReadWorldPixel(Unoccluded,Camera,ShadowReceiver);
        PixelColor ShadowedReceiver=ReadWorldPixel(Shadowed,Camera,ShadowReceiver);
        int UnlitSum=PixelSum(UnlitReceiver);
        int UnoccludedSum=PixelSum(UnoccludedReceiver);
        int ShadowedSum=PixelSum(ShadowedReceiver);
        Assert.True(UnoccludedSum>UnlitSum+20,
            $"Expected an emissive patch to submit direct light: unlit={UnlitReceiver} (sum={UnlitSum}), unoccluded={UnoccludedReceiver} (sum={UnoccludedSum}).");
        Assert.True(ShadowedSum<UnoccludedSum-15,
            $"Expected the emissive patch's selected center projection to shadow its receiver: unoccluded={UnoccludedReceiver} (sum={UnoccludedSum}), shadowed={ShadowedReceiver} (sum={ShadowedSum}).");
        Assert.Equal(0,Scene.Renderer.ErrorOrWorseMessageCount);
    }

    private static ShadowScene CreateShadowScene(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);
        ProceduralMeshData Blocker=ProceduralMeshFactory.CreateBox(new Vector3(1.5f,1.5f,1.5f));
        Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Area Emissive Shadow GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });
        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };
        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=Vector4.One,
            RoughnessFactor=1f,
            MetallicFactor=0f,
        };
        StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [CreateStageMesh(Ground),CreateStageMesh(Blocker)],[Material],
            [
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },
            ]);
        return new ShadowScene(Window,Renderer);
    }

    private static StageMeshCreateInfo CreateStageMesh(in ProceduralMeshData Mesh){
        Vector2[]Uvs=new Vector2[Mesh.Positions.Length];
        Vector4[]Tangents=new Vector4[Mesh.Positions.Length];
        for(int Index=0;Index<Tangents.Length;Index++){
            Tangents[Index]=new Vector4(1f,0f,0f,1f);
        }
        return new StageMeshCreateInfo {
            Positions=Mesh.Positions,
            Normals=Mesh.Normals,
            Tangents=Tangents,
            Uvs=Uvs,
            Indices=Mesh.Indices,
        };
    }

    private static Camera CreateTopDownCamera(Vector3 Target){
        Camera Camera=Camera.CreateDefault(Target);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;
        return Camera;
    }

    private static PixelReadbackResult Render(
        ShadowScene Scene,in Camera Camera,Vector3 CurrentBlockerPosition,Action<StageRenderer> SubmitLights){
        Assert.True(Scene.Renderer.ShaderCompilationSucceeded,
            string.Join(Environment.NewLine,Scene.Renderer.ShaderDiagnostics));
        for(int Frame=0;Frame<3;Frame++){
            Scene.Renderer.SetCamera(Camera);
            Scene.Renderer.SetAmbientLight(Vector3.Zero);
            Scene.Renderer.SetDirectionalLight(
                Vector3.UnitY,Vector3.Zero,0f,CastsShadows:false,ShadowBiasMeters:0f);
            Scene.Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Scene.Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(CurrentBlockerPosition));
            SubmitLights(Scene.Renderer);
            Scene.Renderer.RenderFrame(CaptureForVerification:Frame==2);
        }
        return Assert.IsType<PixelReadbackResult>(Scene.Renderer.LastCapturedFrame);
    }

    private static void SubmitArea(
        StageRenderer Renderer,float LuminanceNits,float WidthMeters,float HeightMeters){
        Renderer.AddAreaLight(
            Position:AreaSourcePosition,LightForward:AreaForward,LinearRgb:Vector3.One,
            LuminanceNits:LuminanceNits,WidthMeters:WidthMeters,HeightMeters:HeightMeters,
            RangeMeters:20f,CastsShadows:true,ShadowPriority:10,ShadowBiasMeters:0.001f);
    }

    private static IReadOnlyList<EmissivePatch> BuildDownwardEmissivePatch(
        Matrix4x4 ObjectWorld,Vector3 Forward,float WidthMeters,float HeightMeters,
        float AuthoredEmissiveNits,float EmissiveTextureLuminanceScale){
        Vector3 Reference=MathF.Abs(Vector3.Dot(Forward,Vector3.UnitY))>0.99f
            ?Vector3.UnitX:Vector3.UnitY;
        Vector3 Right=Vector3.Normalize(Vector3.Cross(Reference,Forward));
        Vector3 Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        float HalfWidth=WidthMeters*0.5f;
        float HalfHeight=HeightMeters*0.5f;
        // The geometry is deliberately authored around the local origin. ObjectWorld must place
        // it at the live emitter transform; this prevents a world-space pre-bake from masking a
        // transform-following regression.
        Vector3 A=-(Right*HalfWidth)-(Up*HalfHeight);
        Vector3 B=(Right*HalfWidth)-(Up*HalfHeight);
        Vector3 C=-(Right*HalfWidth)+(Up*HalfHeight);
        Vector3 D=(Right*HalfWidth)+(Up*HalfHeight);
        return EmissivePatchBuilder.Build(new EmissivePatchBuildInput {
            Positions=[A,B,C,D],
            // Both triangles intentionally wind toward Forward so this patch emits down onto
            // the ground rather than away from the tested receiver.
            Indices=[0,1,2,1,3,2],
            ObjectWorld=ObjectWorld,
            EmissiveNits=new Vector3(AuthoredEmissiveNits),
            EmissiveTextureLuminanceScale=EmissiveTextureLuminanceScale,
            MaxPatches=1,
        });
    }

    private static void SubmitEmissivePatches(
        StageRenderer Renderer,IReadOnlyList<EmissivePatch>Patches){
        Renderer.SubmitDirectLighting(
            Patches,RangeMeters:20f,CastsShadows:true,ShadowPriority:10,ShadowBiasMeters:0.001f);
    }

    private static float CalculateVisibility(
        PixelColor Shadowed,PixelColor Unoccluded,Vector3 Receiver,string SourceLabel){
        int UnoccludedSum=PixelSum(Unoccluded);
        Assert.True(UnoccludedSum>30,
            $"Expected the {SourceLabel} source to visibly illuminate {Receiver}: {Unoccluded} (sum={UnoccludedSum}).");
        return PixelSum(Shadowed)/(float)UnoccludedSum;
    }

    private static PixelColor ReadWorldPixel(PixelReadbackResult Readback,in Camera Camera,Vector3 WorldPosition){
        Matrix4x4 ViewProjection=Camera.GetView()*Camera.GetProjection((float)Readback.Width/Readback.Height);
        Vector4 Clip=Vector4.Transform(new Vector4(WorldPosition,1f),ViewProjection);
        Assert.True(Clip.W>1e-4f,$"Expected {WorldPosition} to be in front of the camera.");
        float NdcX=Clip.X/Clip.W;
        float NdcY=Clip.Y/Clip.W;
        int X=(int)MathF.Round((NdcX*0.5f+0.5f)*(Readback.Width-1));
        int Y=(int)MathF.Round((-NdcY*0.5f+0.5f)*(Readback.Height-1));
        Assert.InRange(X,0,Readback.Width-1);
        Assert.InRange(Y,0,Readback.Height-1);
        return ReadPixel(Readback,X,Y);
    }

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        long Offset=((long)Y*Readback.RowPitch)+(X*BytesPerPixel);
        return new PixelColor(
            Readback.Pixels[Offset],Readback.Pixels[Offset+1],Readback.Pixels[Offset+2],Readback.Pixels[Offset+3]);
    }

    private static int PixelSum(PixelColor Color)=>Color.R+Color.G+Color.B;

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private sealed class ShadowScene:IDisposable {
        public ShadowScene(Win32Window Window,StageRenderer Renderer){
            this.Window=Window;
            this.Renderer=Renderer;
        }

        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }

        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
