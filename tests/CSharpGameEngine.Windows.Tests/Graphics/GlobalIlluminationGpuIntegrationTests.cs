using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>WARP oracles for the ADR-0069 dynamic GI probe volume.</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class GlobalIlluminationGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly Vector3 SunTowardLight=Vector3.Normalize(new Vector3(0.3f,0.7f,-0.6f));
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void GlobalIllumination_Warp_ProbesInOpenSkyReproduceTheSkyLightIrradiance(){
        // Only a far-away plane beyond the capture far plane: every probe sees nothing but sky, so
        // the convolved irradiance must equal the sky light's SH irradiance in every texel direction.
        using var Scene=GiScene.Create(WithWall:false,FloorHeight:-2000f);
        var Settings=new GlobalIlluminationSettings(new Vector3(0f,10f,0f),new Vector3(2f,2f,2f),4f,8,0f,1f);
        Scene.Render(Settings,Frames:2);
        var Readback=Scene.Renderer.ReadGlobalIlluminationForTesting();
        var Sh=Scene.Renderer.ReadAtmosphereForTesting().IrradianceSh;
        var Largest=Enumerable.Range(0,64).Max(Texel=>EvaluateSh(Sh,StageGlobalIllumination.TexelDirection(Texel%8,Texel/8,8)).Length());
        for(var Probe=0;Probe<Settings.ProbeCount;Probe++){
            for(var Y=0;Y<StageGlobalIllumination.IrradianceTexels;Y++){
                for(var X=0;X<StageGlobalIllumination.IrradianceTexels;X++){
                    var Direction=StageGlobalIllumination.TexelDirection(X,Y,StageGlobalIllumination.IrradianceTexels);
                    var Expected=EvaluateSh(Sh,Direction);
                    var Actual=Readback.ProbeIrradianceTexel(Probe,X,Y);
                    Assert.Equal(1f,Actual.W);
                    Assert.True(Vector3.Distance(new Vector3(Actual.X,Actual.Y,Actual.Z),Expected)<=0.03f*Largest,
                        $"probe {Probe} texel ({X},{Y}) direction {Direction}: expected {Expected}, actual {Actual}");
                }
            }
        }
        var SkyDistance=Readback.ProbeDistance(0,Vector3.UnitX);
        Assert.InRange(SkyDistance.X,15.9f,16.1f);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void GlobalIllumination_Warp_ProbeRecordsTheLitWallDistanceAndColour(){
        using var Scene=GiScene.Create(WithWall:true,FloorHeight:0f);
        var Settings=new GlobalIlluminationSettings(new Vector3(0f,1.6f,-0.5f),new Vector3(4f,1.4f,3.5f),2f,64,0f,1f);
        Scene.Render(Settings,Frames:3);
        var Readback=Scene.Renderer.ReadGlobalIlluminationForTesting();
        var Counts=Settings.ProbeCounts;
        // Probe (x=2, y=1, z=2) sits at (0, 3.0, 0.667); the wall's near face is at z=1.9.
        var Probe=2+Counts.X*(1+Counts.Y*2);
        var Position=Settings.ProbePosition(Probe);
        Assert.Equal(0f,Position.X,3);
        var Toward=Readback.ProbeDistance(Probe,Vector3.UnitZ);
        Assert.InRange(Toward.X,1.9f-Position.Z-0.15f,1.9f-Position.Z+0.15f);
        var WallIrradiance=Readback.ProbeIrradiance(Probe,Vector3.UnitZ);
        Assert.True(WallIrradiance.X>1.5f*WallIrradiance.Y&&WallIrradiance.X>1.5f*WallIrradiance.Z,$"The lit red wall must dominate +Z irradiance: {WallIrradiance}");
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void GlobalIllumination_Warp_LitRedWallBleedsOntoTheFloor(){
        using var Scene=GiScene.Create(WithWall:true,FloorHeight:0f);
        var FloorPoint=new Vector3(0f,0f,1.3f);
        Scene.Render(null,Frames:2);
        var Without=Scene.SamplePixel(FloorPoint);
        var Settings=new GlobalIlluminationSettings(new Vector3(0f,1.6f,-0.5f),new Vector3(4f,1.4f,3.5f),2f,64,0.5f,1f);
        Scene.Render(Settings,Frames:4);
        var With=Scene.SamplePixel(FloorPoint);
        var RatioWithout=(Without.R+1f)/(Without.G+1f);
        var RatioWith=(With.R+1f)/(With.G+1f);
        Assert.True(RatioWith>RatioWithout*1.1f,$"GI must tint the floor next to the lit red wall: without={Without} with={With}");
        Scene.AssertNoDebugLayerErrors();
    }
    private static Vector3 EvaluateSh(Vector3[]Sh,Vector3 N)=>
        Sh[0]*0.282095f
        +Sh[1]*(0.488603f*N.Y)+Sh[2]*(0.488603f*N.Z)+Sh[3]*(0.488603f*N.X)
        +Sh[4]*(1.092548f*N.X*N.Y)+Sh[5]*(1.092548f*N.Y*N.Z)
        +Sh[6]*(0.315392f*(3f*N.Z*N.Z-1f))+Sh[7]*(1.092548f*N.X*N.Z)
        +Sh[8]*(0.546274f*(N.X*N.X-N.Y*N.Y));
    private sealed class GiScene:IDisposable {
        private readonly bool WithWall;
        private readonly float FloorHeight;
        private GiScene(Win32Window Window,StageRenderer Renderer,bool WithWall,float FloorHeight){
            this.Window=Window;
            this.Renderer=Renderer;
            this.WithWall=WithWall;
            this.FloorHeight=FloorHeight;
        }
        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }
        private static Camera View=>Camera.CreateFirstPerson(new Vector3(0f,1.5f,-4f));
        public static GiScene Create(bool WithWall,float FloorHeight){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine GI GPU Test",Width=160,Height=90,Visible=false });
            StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
                var Tangents=new Vector4[Data.Positions.Length];
                Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
                return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
            }
            StageMeshCreateInfo[]Meshes=[ToMesh(ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f)),ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(8f,4f,0.2f)))];
            StageMaterialCreateInfo[]Materials=[
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.8f,0.8f,1f),RoughnessFactor=1f,MetallicFactor=0f },
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.9f,0.05f,0.05f,1f),RoughnessFactor=1f,MetallicFactor=0f },
            ];
            StageObjectCreateInfo[]Objects=WithWall
                ?[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 }]
                :[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }];
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Meshes,Materials,Objects);
            Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
            return new GiScene(Window,Renderer,WithWall,FloorHeight);
        }
        public void Render(GlobalIlluminationSettings?Settings,int Frames){
            for(var Frame=0;Frame<Frames;Frame++){
                this.Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(
                    SkyAtmosphereSettings.Earth,new SkyLightSettings(1f,Vector3.Zero),null,
                    new ExposureSettings(ExposureMode.PhysicalCamera,2f,0f),ToneMapper.UnrealFilmicSrgb,0.675f,0.0064f,Settings));
                this.Renderer.SetCamera(View);
                this.Renderer.SetCameraAltitude(1.5f);
                this.Renderer.SetDirectionalLight(SunTowardLight,Vector3.One,3f);
                this.Renderer.SetObjectTransform(0,Matrix4x4.CreateTranslation(0f,this.FloorHeight,0f));
                if(this.WithWall){
                    this.Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,2f,2f));
                }
                this.Renderer.RenderFrame(CaptureForVerification:Frame==Frames-1);
            }
        }
        public PixelColor SamplePixel(Vector3 World){
            var Frame=this.Renderer.LastCapturedFrame!;
            var Clip=Vector4.Transform(new Vector4(World,1f),View.GetView()*View.GetProjection((float)Frame.Width/Frame.Height));
            var X=(int)((Clip.X/Clip.W*0.5f+0.5f)*Frame.Width);
            var Y=(int)((0.5f-Clip.Y/Clip.W*0.5f)*Frame.Height);
            Assert.InRange(X,0,Frame.Width-1);
            Assert.InRange(Y,0,Frame.Height-1);
            var Offset=(int)(Y*Frame.RowPitch)+X*4;
            return new PixelColor(Frame.Pixels[Offset],Frame.Pixels[Offset+1],Frame.Pixels[Offset+2],Frame.Pixels[Offset+3]);
        }
        public void AssertNoDebugLayerErrors()=>
            Assert.DoesNotContain(this.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
