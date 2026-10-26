using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>GPUで測光し適応する、ADR-0069のヒストグラム自動露出に対するWARP検証オラクル。</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class AutoExposureGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly Vector3 SunTowardLight=Vector3.Normalize(new Vector3(0.3f,0.7f,-0.6f));
    private const float FrameSeconds=1f/30f;
    private static ExposureSettings Auto(float MinEv100,float MaxEv100,float Compensation=1f)=>new(ExposureMode.AutoHistogram,MinEv100,Compensation,MaxEv100,3f,1f);
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AutoExposure_Warp_MetersAKnownLuminanceWithTheReflectedLightMeter(){
        // 100 cd/m2の発光体で視界を満たす。EV100 = log2(100 * 100 / 12.5) = 9.64。最初のフレームは
        // EV100 -10で露出オーバーになるが、測光ではその結果に収束せず回復しなければならない。
        // これはどの補正値でも必要（クリッピングは補正値に依存する）。
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
        // 1/30秒を10フレーム実行すると、SpeedUp 3ではステップのexp(-1)、SpeedDown 1ではexp(-1/3)が残る。
        var Diagnostic=$"dim={Dim} rising={Rising} bright={Bright} falling={Falling}";
        Assert.True(MathF.Abs((Bright-Rising)/(Bright-Dim)-MathF.Exp(-1f))<=0.05f,Diagnostic);
        Assert.True(MathF.Abs((Falling-Dim)/(Bright-Dim)-MathF.Exp(-1f/3f))<=0.05f,Diagnostic);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AutoExposure_Warp_DegenerateRangeIsIdenticalToTheFixedPhysicalCamera(){
        // UnrealのヒストグラムモードでMinEv100 == MaxEv100にすると固定露出となる。インポートではこの仕様を使う。
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
        /// <summary>発光体シーンでは平面をカメラへ向けて視界を満たす。照明シーンでは空の下に床を置く。</summary>
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
