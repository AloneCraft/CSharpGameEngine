using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
using Xunit.Abstractions;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// ADR-0078/M0110 WARP oracles for the volumetric clouds (the panorama traced through the cloud layer and drawn over the sky) and the sky
/// sphere (geometry beyond it is covered by the sky). Images of the cloudy sky are written next to the test assembly (m0110\*.png).
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VolumetricCloudGpuIntegrationTests(ITestOutputHelper Output) {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=320;
    private const int Height=180;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Clouds_Warp_WithZeroDensityRenderTheCloudlessFrameWithinOneLevel(){
        var Cloudless=Render(null,null,Frames:3);
        var Empty=Render(VolumetricCloudSettings.Unreal with { DensityPerMeter=0f },null,Frames:3);
        // The empty panorama keeps the whole sky (alpha 1, no light); the smooth read of it may differ from the bare sky only by rounding.
        var Worst=0;
        for(var Index=0;Index<Cloudless.Frame.Pixels.Length;Index++){
            Worst=Math.Max(Worst,Math.Abs(Cloudless.Frame.Pixels[Index]-Empty.Frame.Pixels[Index]));
        }
        Assert.True(Worst<=1,$"largest channel difference {Worst}");
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Clouds_Warp_CoverPartOfTheSkyAndAreDeterministic(){
        var Cloudless=Render(null,null,Frames:3);
        var First=Render(VolumetricCloudSettings.Unreal,null,Frames:3);
        var Second=Render(VolumetricCloudSettings.Unreal,null,Frames:3);
        Save(Cloudless.Frame,"sky-without-clouds.png");
        Save(First.Frame,"sky-with-clouds.png");
        Assert.Equal(Hash(First.Frame),Hash(Second.Frame));
        Assert.Equal(First.Panorama,Second.Panorama);
        // Sky pixels are the upper part of the image; the floor fills the lower part.
        var Changed=0;
        var Whiter=0;
        var Sky=0;
        for(var Y=0;Y<Height/3;Y++){
            for(var X=0;X<Width;X++){
                var Before=ReadPixel(Cloudless.Frame,X,Y);
                var After=ReadPixel(First.Frame,X,Y);
                Sky++;
                if(Math.Abs(Before.R-After.R)+Math.Abs(Before.G-After.G)+Math.Abs(Before.B-After.B)>12){
                    Changed++;
                    // A cloud is greyer than the blue sky behind it: its red comes closer to its blue.
                    if(After.B-After.R<Before.B-Before.R){ Whiter++; }
                }
            }
        }
        var Covered=First.Panorama.Count(Texel=>Texel.W<0.5f)/(double)First.Panorama.Length;
        Output.WriteLine($"sky pixels changed {Changed}/{Sky}, greyer {Whiter}; panorama texels under half sky {Covered:P1}; last frame {First.Milliseconds:F0} ms with clouds, {Cloudless.Milliseconds:F0} ms without; first frame {First.FirstMilliseconds:F0} ms with, {Cloudless.FirstMilliseconds:F0} ms without");
        Assert.All(First.Panorama,Texel=>{
            Assert.True(float.IsFinite(Texel.X)&&float.IsFinite(Texel.Y)&&float.IsFinite(Texel.Z)&&float.IsFinite(Texel.W));
            Assert.True(Texel.X>=0f&&Texel.Y>=0f&&Texel.Z>=0f);
            Assert.InRange(Texel.W,0f,1.0001f);
        });
        Assert.InRange(Changed,Sky/20,Sky*19/20);
        Assert.True(Whiter>Changed/2,$"greyer {Whiter} of {Changed}");
        Assert.InRange(Covered,0.03,0.9);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Clouds_Warp_MoveWithTheWindAndRefreshOneTexelOfEachBlockPerFrame(){
        var Calm=VolumetricCloudSettings.Unreal;
        var Blown=Calm with { WindOffsetKm=new Vector2(3f,-1f) };
        var A=Render(Calm,null,Frames:1);
        var B=Render(Blown,null,Frames:1);
        Assert.NotEqual(A.Panorama,B.Panorama);
        Assert.Equal(B.Panorama,Render(Blown,null,Frames:1).Panorama);
        // Only the wind changes between the frames: the first frame traces every texel, the second only the first Bayer phase (offset 0, 0 of
        // each 4 x 4 block), so every texel that changed is one of those.
        using var Scene=CloudScene.Create();
        Scene.RenderFrame(Calm,null);
        var Before=Scene.Renderer.ReadCloudPanoramaForTesting();
        Scene.RenderFrame(Blown,null);
        var After=Scene.Renderer.ReadCloudPanoramaForTesting();
        var ChangedOutside=0;
        var ChangedInside=0;
        for(var Index=0;Index<Before.Texels.Length;Index++){
            if(Before.Texels[Index]==After.Texels[Index]){ continue; }
            var X=Index%Before.Width;
            var Y=Index/Before.Width;
            if(X%4==0&&Y%4==0){ ChangedInside++; } else { ChangedOutside++; }
        }
        Assert.Equal(0,ChangedOutside);
        Assert.True(ChangedInside>0);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Clouds_InvalidSettings_AreRejected(){
        Assert.False((VolumetricCloudSettings.Unreal with { DensityPerMeter=float.NaN }).IsValid);
        Assert.False((VolumetricCloudSettings.Unreal with { LayerHeightKm=0f }).IsValid);
        Assert.False((VolumetricCloudSettings.Unreal with { PhaseG=1f }).IsValid);
        Assert.False((VolumetricCloudSettings.Unreal with { WindOffsetKm=new Vector2(float.PositiveInfinity,0f) }).IsValid);
        Assert.False(new SkySphereSettings(Vector3.Zero,0f).IsValid);
        Assert.False(new SkySphereSettings(new Vector3(float.NaN),10f).IsValid);
        using var Scene=CloudScene.Create();
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetEnvironmentLighting(Environment(VolumetricCloudSettings.Unreal with { Coverage=float.NaN },null)));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void SkySphere_Warp_CoversGeometryBeyondItsRadius(){
        // A wall 30 km ahead, 20 km wide: with UE's 16.384 km sphere it is behind the sky; with a 50 km sphere it is in front of it.
        var EmptySky=Render(null,null,Frames:2,Wall:false);
        var Visible=Render(null,null,Frames:2,Wall:true);
        var Covered=Render(null,new SkySphereSettings(Vector3.Zero,16384f),Frames:2,Wall:true);
        var Inside=Render(null,new SkySphereSettings(Vector3.Zero,50000f),Frames:2,Wall:true);
        var Probe=(X:Width/2,Y:Height/2);
        Assert.NotEqual(ReadPixel(EmptySky.Frame,Probe.X,Probe.Y),ReadPixel(Visible.Frame,Probe.X,Probe.Y));
        Assert.Equal(ReadPixel(EmptySky.Frame,Probe.X,Probe.Y),ReadPixel(Covered.Frame,Probe.X,Probe.Y));
        Assert.Equal(ReadPixel(Visible.Frame,Probe.X,Probe.Y),ReadPixel(Inside.Frame,Probe.X,Probe.Y));
        // The floor near the camera is far inside the sphere and stays.
        Assert.Equal(ReadPixel(Visible.Frame,Width/2,Height-4),ReadPixel(Covered.Frame,Width/2,Height-4));
    }
    private static EnvironmentLightingSettings Environment(VolumetricCloudSettings?Clouds,SkySphereSettings?Sphere)=>new(
        SkyAtmosphereSettings.Earth,new SkyLightSettings(1f,Vector3.Zero),null,
        new ExposureSettings(ExposureMode.PhysicalCamera,14f,0f),ToneMapper.UnrealFilmicSrgb,0.675f,0.0047f,null,null,Clouds,Sphere);
    private static (PixelReadbackResult Frame,Vector4[]Panorama,double Milliseconds,double FirstMilliseconds) Render(VolumetricCloudSettings?Clouds,SkySphereSettings?Sphere,int Frames,
        bool Wall=false){
        using var Scene=CloudScene.Create(Wall);
        var Clock=Stopwatch.StartNew();
        double Last=0;
        double First=0;
        for(var Frame=0;Frame<Frames;Frame++){
            var Start=Clock.Elapsed.TotalMilliseconds;
            Scene.RenderFrame(Clouds,Sphere,Capture:Frame==Frames-1);
            Last=Clock.Elapsed.TotalMilliseconds-Start;
            if(Frame==0){ First=Last; }
        }
        Scene.AssertNoDebugLayerErrors();
        var Panorama=Clouds is null?[]:Scene.Renderer.ReadCloudPanoramaForTesting().Texels;
        return (Scene.Renderer.LastCapturedFrame!,Panorama,Last,First);
    }
    private sealed class CloudScene:IDisposable {
        private readonly Win32Window Window;
        internal StageRenderer Renderer { get; }
        private readonly bool HasWall;
        private CloudScene(Win32Window Window,StageRenderer Renderer,bool HasWall){
            this.Window=Window;
            this.Renderer=Renderer;
            this.HasWall=HasWall;
        }
        internal static CloudScene Create(bool Wall=false){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Volumetric Clouds",Width=Width,Height=Height,Visible=false });
            StageMeshCreateInfo[]Meshes=[
                ToMesh(ProceduralMeshFactory.CreatePlane(Width:400f,Depth:400f)),
                ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(20000f,20000f,10f))),
            ];
            StageMaterialCreateInfo[]Materials=[
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.3f,0.3f,0.3f,1f),RoughnessFactor=1f,MetallicFactor=0f },
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.1f,0.1f,1f),RoughnessFactor=1f,MetallicFactor=0f },
            ];
            StageObjectCreateInfo[]Objects=Wall
                ?[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 }]
                :[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }];
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
                ShaderArtifactPath,Meshes,Materials,Objects);
            return new CloudScene(Window,Renderer,Wall);
        }
        internal void RenderFrame(VolumetricCloudSettings?Clouds,SkySphereSettings?Sphere,bool Capture=false){
            var View=Camera.CreateFirstPerson(new Vector3(0f,1.7f,0f));
            View.Pitch=0.18f;
            View.FarZ=200000f;
            this.Renderer.SetCamera(View);
            this.Renderer.SetEnvironmentLighting(Environment(Clouds,Sphere));
            this.Renderer.SetCameraAltitude(1.7f);
            this.Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.3f,0.6f,0.75f)),Vector3.One,100000f,CastsShadows:true,ShadowBiasMeters:0.005f);
            this.Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            if(this.HasWall){
                this.Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,0f,30000f));
            }
            this.Renderer.RenderFrame(CaptureForVerification:Capture);
        }
        internal void AssertNoDebugLayerErrors()=>Assert.Equal(0,this.Renderer.ErrorOrWorseMessageCount);
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
    private static StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
        var Tangents=new Vector4[Data.Positions.Length];
        for(var Index=0;Index<Tangents.Length;Index++){
            Tangents[Index]=StageMeshGeometry.ArbitraryTangentFor(Data.Normals[Index]);
        }
        return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
    }
    private static string Hash(PixelReadbackResult Frame)=>Convert.ToHexString(SHA256.HashData(Frame.Pixels));
    private static void Save(PixelReadbackResult Frame,string Name){
        var Folder=Path.Combine(AppContext.BaseDirectory,"m0110");
        Directory.CreateDirectory(Folder);
        RuntimeAutomation.WritePng(Frame,Path.Combine(Folder,Name));
    }
    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        var Offset=((long)Y*Readback.RowPitch)+(X*4);
        return new PixelColor(Readback.Pixels[Offset],Readback.Pixels[Offset+1],Readback.Pixels[Offset+2],Readback.Pixels[Offset+3]);
    }
}
