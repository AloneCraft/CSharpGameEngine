using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>WARP oracles for the ADR-0069 screen-space ambient occlusion of indirect light.</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class AmbientOcclusionGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly AmbientOcclusionSettings FullOcclusion=new(1f,1f);
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AmbientOcclusion_Warp_DarkensTheCreaseButNotOpenFloor(){
        using var Scene=AoScene.Create();
        // The box's front face 0.2 m above the floor: within the occlusion radius of the floor.
        var Crease=new Vector3(0f,0.2f,1f);
        var Open=new Vector3(-2.8f,0f,0.9f);
        Scene.Render(SkyLight:true,Occlusion:null);
        var CreaseWithout=Scene.SamplePixel(Crease);
        var OpenWithout=Scene.SamplePixel(Open);
        Scene.Render(SkyLight:true,Occlusion:FullOcclusion);
        var CreaseWith=Scene.SamplePixel(Crease);
        var OpenWith=Scene.SamplePixel(Open);
        var Diagnostic=$"crease {CreaseWithout}->{CreaseWith}, open {OpenWithout}->{OpenWith}";
        Assert.True(CreaseWith.R+CreaseWith.G+CreaseWith.B+3<CreaseWithout.R+CreaseWithout.G+CreaseWithout.B,Diagnostic);
        Assert.InRange(OpenWith.G-OpenWithout.G,-1,1);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AmbientOcclusion_Warp_OccludesCreasesAndLeavesOpenSurfacesAndSkyUnoccluded(){
        using var Scene=AoScene.Create();
        Scene.Render(SkyLight:true,Occlusion:FullOcclusion);
        var (Values,Width,Height)=Scene.Renderer.ReadAmbientOcclusionForTesting();
        float At(Vector3 World){
            var Clip=Vector4.Transform(new Vector4(World,1f),Scene.ViewProjection);
            var X=Math.Clamp((int)((Clip.X/Clip.W*0.5f+0.5f)*Width),0,Width-1);
            var Y=Math.Clamp((int)((0.5f-Clip.Y/Clip.W*0.5f)*Height),0,Height-1);
            return Values[Y*Width+X];
        }
        // Radius 1 m: the lower face and the floor in front of it occlude each other; surfaces
        // farther than the radius from any other surface stay unoccluded.
        var FaceNearFloor=At(new Vector3(0f,0.2f,1f));
        var FloorNearFace=At(new Vector3(0f,0f,0.7f));
        var FaceHigh=At(new Vector3(0f,1.7f,1f));
        var OpenFloor=At(new Vector3(-2.8f,0f,0.9f));
        var Sky=Values[Width/2];
        var Diagnostic=$"faceNearFloor={FaceNearFloor} floorNearFace={FloorNearFace} faceHigh={FaceHigh} open={OpenFloor} sky={Sky}";
        Assert.All(Values,Value=>Assert.InRange(Value,0f,1f));
        Assert.True(FaceNearFloor<0.8f,Diagnostic);
        Assert.True(FloorNearFace<0.95f,Diagnostic);
        Assert.True(FaceHigh>0.97f,Diagnostic);
        Assert.True(OpenFloor>0.97f,Diagnostic);
        Assert.Equal(1f,Sky);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AmbientOcclusion_Warp_LeavesDirectLightUntouched(){
        // Without sky light or flat ambient there is no indirect light, so occlusion must not change a single pixel.
        using var Scene=AoScene.Create();
        Scene.Render(SkyLight:false,Occlusion:null);
        var Without=Scene.Renderer.LastCapturedFrame!.Pixels.ToArray();
        Scene.Render(SkyLight:false,Occlusion:FullOcclusion);
        var With=Scene.Renderer.LastCapturedFrame!.Pixels;
        Assert.Equal(Without,With);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyTheory]
    [Trait("Category","GPU")]
    [InlineData(1f,300f,0.8f)]
    [InlineData(10000f,1e18f,1f)]
    public void AmbientOcclusion_PreservesPhysicalCreasesBeyondTheAuxiliaryProjection(float Scale,float Far,float Maximum){
        using var Scene=AoScene.Create(Scale,Far);
        Scene.Render(SkyLight:true,Occlusion:new AmbientOcclusionSettings(1f,Scale));
        var (Values,Width,Height)=Scene.Renderer.ReadAmbientOcclusionForTesting();
        var Clip=Vector4.Transform(new Vector4(new Vector3(0f,0.2f,1f)*Scale,1f),Scene.ViewProjection);
        var X=Math.Clamp((int)((Clip.X/Clip.W*0.5f+0.5f)*Width),0,Width-1);
        var Y=Math.Clamp((int)((0.5f-Clip.Y/Clip.W*0.5f)*Height),0,Height-1);
        Assert.All(Values,Value=>Assert.InRange(Value,0f,1f));
        Assert.True(Values[Y*Width+X]<Maximum,$"Physical crease at scale {Scale}: {Values[Y*Width+X]}");
        Scene.AssertNoDebugLayerErrors();
    }
    private sealed class AoScene:IDisposable {
        private AoScene(Win32Window Window,StageRenderer Renderer,float Scale,float Far){
            this.Scale=Scale;
            this.Far=Far;
            this.Window=Window;
            this.Renderer=Renderer;
        }
        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }
        private readonly float Scale;
        private readonly float Far;
        private Camera View {
            get {
                var Result=Camera.CreateFirstPerson(new Vector3(0f,1.5f,-4f)*this.Scale);
                Result.FarZ=this.Far;
                return Result;
            }
        }
        public Matrix4x4 ViewProjection=>View.GetView()*View.GetProjection((float)this.Window.ClientWidth/this.Window.ClientHeight);
        public static AoScene Create(float Scale=1f,float Far=300f){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine AO GPU Test",Width=160,Height=90,Visible=false });
            StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
                var Tangents=new Vector4[Data.Positions.Length];
                Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
                return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
            }
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
                [ToMesh(ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f)),ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(2f,2f,2f)))],
                [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.8f,0.8f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
                [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
            Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
            return new AoScene(Window,Renderer,Scale,Far);
        }
        public void Render(bool SkyLight,AmbientOcclusionSettings?Occlusion){
            for(var Frame=0;Frame<2;Frame++){
                this.Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(
                    SkyAtmosphereSettings.Earth,SkyLight?new SkyLightSettings(1f,Vector3.Zero):null,null,
                    new ExposureSettings(ExposureMode.PhysicalCamera,2f,0f),ToneMapper.UnrealFilmicSrgb,0f,0.0064f,null,Occlusion));
                this.Renderer.SetAmbientLight(Vector3.Zero);
                this.Renderer.SetCamera(this.View);
                this.Renderer.SetCameraAltitude(1.5f);
                this.Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.3f,0.7f,-0.6f)),Vector3.One,3f);
                this.Renderer.SetObjectTransform(0,Matrix4x4.CreateScale(this.Scale));
                this.Renderer.SetObjectTransform(1,Matrix4x4.CreateScale(this.Scale)*Matrix4x4.CreateTranslation(new Vector3(0f,1f,2f)*this.Scale));
                this.Renderer.RenderFrame(CaptureForVerification:Frame==1);
            }
        }
        public PixelColor SamplePixel(Vector3 World){
            var Frame=this.Renderer.LastCapturedFrame!;
            var Clip=Vector4.Transform(new Vector4(World,1f),this.View.GetView()*this.View.GetProjection((float)Frame.Width/Frame.Height));
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
