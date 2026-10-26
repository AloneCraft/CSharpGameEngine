using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>空大気とGPU LUTの結果をCPU参照値と比較する、ADR-0069用WARP検証オラクル。</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class AtmosphereGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Atmosphere_Warp_LutsAreFiniteAndTransmittanceMatchesCpuReference(){
        using var Scene=AtmosphereScene.Create();
        var Readback=Scene.Renderer.ReadAtmosphereForTesting();
        Assert.All(Readback.Transmittance,Value=>Assert.True(float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z),$"Transmittance {Value}"));
        Assert.All(Readback.SkyView,Value=>Assert.True(float.IsFinite(Value.X)&&Value.X>=0f,$"SkyView {Value}"));
        Assert.All(Readback.IrradianceSh,Value=>Assert.True(float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z),$"SH {Value}"));
        // 地面から天頂を見る。uv.y=0は地面の高さ、uv.xはd=dMinに対応し、真上を向く。
        var ZenithTransmittance=Readback.Transmittance[0];
        var Cpu=AtmosphereTransmittance.Evaluate(SkyAtmosphereSettings.Earth,0f,Vector3.UnitY);
        Assert.InRange(ZenithTransmittance.X,Cpu.X-0.02f,Cpu.X+0.02f);
        Assert.InRange(ZenithTransmittance.Z,Cpu.Z-0.02f,Cpu.Z+0.02f);
        Assert.True(Readback.SkyView.Max(Value=>Value.Z)>0f,"The sky view LUT must contain scattered light.");
        Assert.True(Readback.IrradianceSh[0].Z>0f,"The sky light's DC term must be positive for a lit sky.");
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Atmosphere_Warp_SkyAndSkyLitGroundAreVisible(){
        using var Scene=AtmosphereScene.Create();
        Scene.Renderer.SetCamera(Camera.CreateFirstPerson(new Vector3(0f,2f,-6f)));
        Scene.Renderer.SetCameraAltitude(2f);
        Scene.Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.3f,0.8f,0.2f)),Vector3.One,3f);
        Scene.Renderer.RenderFrame(CaptureForVerification:true);
        var Frame=Scene.Renderer.LastCapturedFrame!;
        PixelColor Pixel(int X,int Y){
            var Offset=(int)(Y*Frame.RowPitch)+X*4;
            return new PixelColor(Frame.Pixels[Offset],Frame.Pixels[Offset+1],Frame.Pixels[Offset+2],Frame.Pixels[Offset+3]);
        }
        var Sky=Pixel(Frame.Width/2,2);
        var Ground=Pixel(Frame.Width/2,Frame.Height-3);
        var Sh=Scene.Renderer.ReadAtmosphereForTesting().IrradianceSh;
        var Diagnostic=$"sky={Sky} ground={Ground} sh0={Sh[0]} sh1={Sh[1]} sh2={Sh[2]}";
        Assert.True(Sky.B>Sky.R&&Sky.B>40,Diagnostic);
        Assert.True(Ground.R>20&&Ground.G>20,Diagnostic);
    }
    private sealed class AtmosphereScene:IDisposable {
        private AtmosphereScene(Win32Window Window,StageRenderer Renderer){
            this.Window=Window;
            this.Renderer=Renderer;
        }
        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }
        public static AtmosphereScene Create(){
            var Ground=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Atmosphere GPU Test",Width=128,Height=72,Visible=false });
            var Tangents=new Vector4[Ground.Positions.Length];
            Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
            StageMeshCreateInfo Mesh=new(){ Positions=Ground.Positions,Normals=Ground.Normals,Tangents=Tangents,Uvs=new Vector2[Ground.Positions.Length],Indices=Ground.Indices };
            StageMaterialCreateInfo Material=new(){ BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f };
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
                [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
            Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
            Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(
                SkyAtmosphereSettings.Earth,new SkyLightSettings(1f,Vector3.Zero),null,
                new ExposureSettings(ExposureMode.PhysicalCamera,0f,1f),ToneMapper.UnrealFilmicSrgb,0.675f,0.0064f));
            var View=Camera.CreateFirstPerson(new Vector3(0f,2f,-6f));
            for(var Frame=0;Frame<2;Frame++){
                Renderer.SetCamera(View);
                Renderer.SetCameraAltitude(2f);
                Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.3f,0.8f,0.2f)),Vector3.One,3f);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                Renderer.RenderFrame();
            }
            return new AtmosphereScene(Window,Renderer);
        }
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
