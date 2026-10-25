using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>WARP oracles for the ADR-0069 histogram auto exposure, metered and adapted on the GPU.</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class AutoExposureGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly Vector3 SunTowardLight=Vector3.Normalize(new Vector3(0.3f,0.7f,-0.6f));
    private const float FrameSeconds=1f/30f;
    private static ExposureSettings Auto(float MinEv100,float MaxEv100,float Compensation=1f)=>new(ExposureMode.AutoHistogram,MinEv100,Compensation,MaxEv100,3f,1f);
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AutoExposure_Warp_MetersAKnownLuminanceWithTheReflectedLightMeter(){
        // A 100 cd/m2 emitter fills the view: EV100 = log2(100 * 100 / 12.5) = 9.64. The first frames
        // are exposed at EV100 -10 and clip; metering must recover instead of settling on them, for
        // every compensation (the clipping depends on it).
        foreach(var Compensation in new[]{0f,1f,2f}){
            using var Scene=ExposureScene.Create(EmitterNits:100f);
            Scene.Render(Auto(-10f,20f,Compensation),Atmosphere:false,SunLux:0f,Frames:16);
            var State=Scene.Renderer.ReadAutoExposureStateForTesting();
            Assert.True(MathF.Abs(State.X-(MathF.Log2(100f)+3f))<=0.35f,$"compensation {Compensation}: {State}");
            Assert.Equal(MathF.Pow(2f,Compensation)/(1.2f*MathF.Pow(2f,State.X)),State.Y,4);
            Assert.Equal(1f,State.Z);
            Scene.AssertNoDebugLayerErrors();
        }
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AutoExposure_Warp_SixteenTimesBrighterSceneMetersFourStopsHigher(){
        using var Scene=ExposureScene.Create(EmitterNits:0f);
        Scene.Render(Auto(-10f,20f),Atmosphere:true,SunLux:3f,Frames:120);
        var Dim=Scene.Renderer.ReadAutoExposureStateForTesting().X;
        Scene.Render(Auto(-10f,20f),Atmosphere:true,SunLux:48f,Frames:120);
        var Bright=Scene.Renderer.ReadAutoExposureStateForTesting().X;
        Assert.True(MathF.Abs(Bright-Dim-4f)<=0.35f,$"dim={Dim} bright={Bright}");
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AutoExposure_Warp_AdaptsWithTheDirectionDependentSpeed(){
        using var Scene=ExposureScene.Create(EmitterNits:0f);
        Scene.Render(Auto(-10f,20f),Atmosphere:true,SunLux:3f,Frames:120);
        var Dim=Scene.Renderer.ReadAutoExposureStateForTesting().X;
        Scene.Render(Auto(-10f,20f),Atmosphere:true,SunLux:48f,Frames:10);
        var Rising=Scene.Renderer.ReadAutoExposureStateForTesting().X;
        Scene.Render(Auto(-10f,20f),Atmosphere:true,SunLux:48f,Frames:150);
        var Bright=Scene.Renderer.ReadAutoExposureStateForTesting().X;
        Scene.Render(Auto(-10f,20f),Atmosphere:true,SunLux:3f,Frames:10);
        var Falling=Scene.Renderer.ReadAutoExposureStateForTesting().X;
        // Ten frames of 1/30 s: SpeedUp 3 leaves exp(-1) of the step, SpeedDown 1 leaves exp(-1/3).
        var Diagnostic=$"dim={Dim} rising={Rising} bright={Bright} falling={Falling}";
        Assert.True(MathF.Abs((Bright-Rising)/(Bright-Dim)-MathF.Exp(-1f))<=0.05f,Diagnostic);
        Assert.True(MathF.Abs((Falling-Dim)/(Bright-Dim)-MathF.Exp(-1f/3f))<=0.05f,Diagnostic);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AutoExposure_Warp_DegenerateRangeIsIdenticalToTheFixedPhysicalCamera(){
        // Unreal's histogram mode with MinEv100 == MaxEv100 is a fixed exposure; the import relies on it.
        using var Scene=ExposureScene.Create(EmitterNits:0f);
        Scene.Render(new ExposureSettings(ExposureMode.PhysicalCamera,5f,1f),Atmosphere:true,SunLux:3f,Frames:4,Capture:true);
        var Fixed=Scene.Renderer.LastCapturedFrame!.Pixels.ToArray();
        Scene.Render(Auto(5f,5f),Atmosphere:true,SunLux:3f,Frames:4,Capture:true);
        Assert.Equal(5f,Scene.Renderer.ReadAutoExposureStateForTesting().X);
        Assert.Equal(Fixed,Scene.Renderer.LastCapturedFrame!.Pixels);
        Scene.AssertNoDebugLayerErrors();
    }
    private sealed class ExposureScene:IDisposable {
        private ExposureScene(Win32Window Window,StageRenderer Renderer){
            this.Window=Window;
            this.Renderer=Renderer;
        }
        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }
        public static ExposureScene Create(float EmitterNits){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Auto Exposure GPU Test",Width=160,Height=90,Visible=false });
            var Plane=ProceduralMeshFactory.CreatePlane(Width:200f,Depth:200f);
            var Tangents=new Vector4[Plane.Positions.Length];
            Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
            StageMeshCreateInfo Mesh=new(){ Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Tangents,Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices };
            var Material=EmitterNits>0f
                ?new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0f,0f,0f,1f),RoughnessFactor=1f,MetallicFactor=0f,EmissiveFactor=Vector3.One,EmissiveNits=new Vector3(EmitterNits) }
                :new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.5f,0.5f,0.5f,1f),RoughnessFactor=1f,MetallicFactor=0f };
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
                [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
            Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
            Renderer.AutoExposureDeltaSecondsForTesting=FrameSeconds;
            return new ExposureScene(Window,Renderer);
        }
        /// <summary>The emitter scene turns the plane toward the camera so it fills the view; the lit scene is a floor under the sky.</summary>
        public void Render(ExposureSettings Exposure,bool Atmosphere,float SunLux,int Frames,bool Capture=false){
            for(var Frame=0;Frame<Frames;Frame++){
                this.Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(
                    Atmosphere?SkyAtmosphereSettings.Earth:null,Atmosphere?new SkyLightSettings(1f,Vector3.Zero):null,null,
                    Exposure,ToneMapper.UnrealFilmicSrgb,0f,0.0064f));
                this.Renderer.SetAmbientLight(Vector3.Zero);
                this.Renderer.SetFog(FogParams.None);
                this.Renderer.SetCamera(Camera.CreateFirstPerson(new Vector3(0f,1.5f,-4f)));
                this.Renderer.SetCameraAltitude(1.5f);
                if(SunLux>0f){
                    this.Renderer.SetDirectionalLight(SunTowardLight,Vector3.One,SunLux);
                }
                this.Renderer.SetObjectTransform(0,Atmosphere
                    ?Matrix4x4.Identity
                    :Matrix4x4.CreateRotationX(-MathF.PI/2f)*Matrix4x4.CreateTranslation(0f,1.5f,5f));
                this.Renderer.RenderFrame(CaptureForVerification:Capture&&Frame==Frames-1);
            }
        }
        public void AssertNoDebugLayerErrors()=>
            Assert.DoesNotContain(this.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
