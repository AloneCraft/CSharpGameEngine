using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 主パスの 2 パスの遮蔽（M0131 単位 2、ADR-0100 決定 3）。床と壁（普通のメッシュ）、壁の後ろに隠れた球 A と見える球 B（仮想ジオメトリ）を、
/// 遮蔽を使う描画器と使わない描画器で同じカメラで描き、毎フレームの絵が 1 画素も違わないことを確かめる。
/// 球 A は壁の横の床に影を落とすので、影が変わらないことも同じ比較で確かめる。隠れた球 A の塊の大半は主パスで描かれず、見える球 B は 1 パス目で描かれる。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualGeometryOcclusionGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=320;
    private const int Height=180;
    private const int HiddenSphere=0;
    private const int VisibleSphere=1;
    private static readonly Vector3 HiddenPosition=new(0f,1.2f,4f);
    private static readonly Vector3 VisiblePosition=new(-2.5f,1f,-2f);
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_OcclusionDrawsTheSameImageAndSkipsTheHiddenSphere(){
        using var With=new Scene(Occlusion:true);
        using var Without=new Scene(Occlusion:false);
        var View=Camera.CreateFirstPerson(new Vector3(0f,1.5f,-6f));
        byte[]Last=[];
        for(var Frame=0;Frame<4;Frame++){
            Last=With.Render(View);
            var Reference=Without.Render(View);
            AssertSameImage(Reference,Last,$"frame {Frame}");
            if(Frame==3){ WriteCapture("still",Reference,Last); }
        }
        var Counts=With.Renderer.ReadVirtualGeometryPassCountsForTesting();
        var Selected=With.Renderer.ReadVirtualGeometryClusterCountsForTesting();
        Assert.True(Selected[HiddenSphere]>0,"the hidden sphere selected no cluster (the shadow needs it)");
        // 球で判定するので、粗い段の大きな塊は、画面で覆う範囲を Hi-Z の画素にそろえたときに壁の縁の外まで含み、見えるとされることがある（描きすぎる側）。
        // 約束は「隠れた塊の大半を描かない」。
        var HiddenDrawn=Counts[HiddenSphere].First+Counts[HiddenSphere].Second;
        WriteResult("still",$"hidden sphere: selected {Selected[HiddenSphere]}, first pass {Counts[HiddenSphere].First}, second pass {Counts[HiddenSphere].Second}; visible sphere: selected {Selected[VisibleSphere]}, first pass {Counts[VisibleSphere].First}, second pass {Counts[VisibleSphere].Second}");
        Assert.True(HiddenDrawn*10<=Selected[HiddenSphere],$"the hidden sphere drew {HiddenDrawn} of its {Selected[HiddenSphere]} selected clusters in the main pass");
        Assert.True(Counts[VisibleSphere].First>0,$"the visible sphere drew {Counts[VisibleSphere].First} clusters in the first pass");
        Assert.Equal(0,Counts[VisibleSphere].Second);
        // 隠れた球の影は、壁の横の床に落ちる（遮蔽で影が消えていない）。
        var Shadow=Luminance(Last,View,new Vector3(3.2f,0f,4f));
        var Open=Luminance(Last,View,new Vector3(3.2f,0f,6.5f));
        Assert.True(Shadow+20<Open,$"the floor in the hidden sphere's shadow ({Shadow}) is not darker than the open floor ({Open})");
        Assert.DoesNotContain(With.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    /// <summary>
    /// カメラを左右に回して、画面の外にいた球 B が入ってくる。前のフレームで写っていない塊は 2 パス目が描くので、入ってきたフレームでも欠けず、
    /// どのフレームも遮蔽なしと同じ絵になる。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_TurningTheCameraLeavesNoHoles(){
        using var With=new Scene(Occlusion:true);
        using var Without=new Scene(Occlusion:false);
        var Entered=0;
        var WasOnScreen=true;
        for(var Frame=0;Frame<9;Frame++){
            var View=Camera.CreateFirstPerson(new Vector3(0f,1.5f,-6f));
            View.Yaw=-1.2f+Frame*0.3f;
            var Image=With.Render(View);
            AssertSameImage(Without.Render(View),Image,$"frame {Frame} yaw {View.Yaw}");
            var Counts=With.Renderer.ReadVirtualGeometryPassCountsForTesting();
            var OnScreen=IsOnScreen(View,VisiblePosition);
            if(Frame>0&&OnScreen&&!WasOnScreen){
                Assert.True(Counts[VisibleSphere].Second>0,$"frame {Frame}: the sphere entered the screen but the second pass drew nothing");
                Entered++;
            }
            WasOnScreen=OnScreen;
        }
        Assert.True(Entered>0,"the sphere never entered the screen; widen the sweep");
        Assert.DoesNotContain(With.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    private static void AssertSameImage(byte[]Expected,byte[]Actual,string Context){
        var Different=0;
        var First=-1;
        for(var Pixel=0;Pixel<Width*Height;Pixel++){
            for(var Channel=0;Channel<3;Channel++){
                if(Expected[Pixel*4+Channel]==Actual[Pixel*4+Channel]){ continue; }
                if(First<0){ First=Pixel; }
                Different++;
                break;
            }
        }
        Assert.True(Different==0,$"{Context}: {Different} pixels differ from the image without occlusion (first at {First % Width},{First / Width})");
    }
    private static bool IsOnScreen(Camera View,Vector3 World){
        var Clip=Vector4.Transform(new Vector4(World,1f),View.GetView()*View.GetProjection((float)Width/Height));
        return Clip.W>0f&&MathF.Abs(Clip.X/Clip.W)<1f&&MathF.Abs(Clip.Y/Clip.W)<1f;
    }
    private static int Luminance(byte[]Pixels,Camera View,Vector3 World){
        var Clip=Vector4.Transform(new Vector4(World,1f),View.GetView()*View.GetProjection((float)Width/Height));
        var X=(int)((Clip.X/Clip.W*0.5f+0.5f)*Width);
        var Y=(int)((0.5f-Clip.Y/Clip.W*0.5f)*Height);
        Assert.InRange(X,0,Width-1);
        Assert.InRange(Y,0,Height-1);
        var Offset=(Y*Width+X)*4;
        return Pixels[Offset]+Pixels[Offset+1]+Pixels[Offset+2];
    }
    private static void WriteResult(string Name,string Result){
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_CAPTURE") is not{ Length:>0 }Capture){ return; }
        var Folder=Path.Combine(Capture,"occlusion-"+Name);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder,"result.txt"),Result);
    }
    private static void WriteCapture(string Name,byte[]Without,byte[]With){
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_CAPTURE") is not{ Length:>0 }Capture){ return; }
        var Folder=Path.Combine(Capture,"occlusion-"+Name);
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder,"without.rgba"),Without);
        File.WriteAllBytes(Path.Combine(Folder,"with.rgba"),With);
    }
    /// <summary>球 A（物体 0、壁の後ろ）、球 B（物体 1、手前の左）、床（物体 2）、壁（物体 3、幅 3 m・高さ 2.5 m・厚さ 0.2 m、z = 0）。光は +x へ低く進む。</summary>
    private sealed class Scene:IDisposable {
        private readonly Win32Window Window;
        internal StageRenderer Renderer { get; }
        internal Scene(bool Occlusion){
            this.Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Geometry Occlusion GPU Test",Width=Width,Height=Height,Visible=false });
            var (Source,Geometry)=VirtualGeometryGpuTests.SphereMeshes();
            static StageMeshCreateInfo Mesh(ProceduralMeshData Data)=>new(){ Positions=Data.Positions,Normals=Data.Normals,
                Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Data.Positions.Length).ToArray(),Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
            this.Renderer=StageRenderer.Create(this.Window.Handle,this.Window.ClientWidth,this.Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
                [Source with{ VirtualGeometry=Geometry },Mesh(ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f)),Mesh(ProceduralMeshFactory.CreateBox(new Vector3(3f,2.5f,0.2f)))],
                [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.6f,0.4f,1f),RoughnessFactor=0.5f,MetallicFactor=0f },
                    new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
                [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                    new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 },new StageObjectCreateInfo { MeshIndex=2,MaterialIndex=1 }]);
            Assert.True(this.Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,this.Renderer.ShaderArtifactDiagnostics));
            this.Renderer.VirtualGeometryOcclusion=Occlusion;
        }
        internal byte[]Render(Camera View){
            this.Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(null,null,null,new ExposureSettings(ExposureMode.PhysicalCamera,2f,0f),ToneMapper.UnrealFilmicSrgb,0f,0.0064f,
                DirectionalShadows:new DirectionalShadowSettings(DirectionalShadowMode.Cascaded)));
            this.Renderer.SetAmbientLight(new Vector3(0.2f));
            this.Renderer.SetCamera(View);
            this.Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(-1f,0.4f,0f)),Vector3.One,3f);
            this.Renderer.SetObjectTransform(HiddenSphere,Matrix4x4.CreateTranslation(HiddenPosition));
            this.Renderer.SetObjectTransform(VisibleSphere,Matrix4x4.CreateTranslation(VisiblePosition));
            this.Renderer.SetObjectTransform(2,Matrix4x4.Identity);
            this.Renderer.SetObjectTransform(3,Matrix4x4.CreateTranslation(0f,1.25f,0f));
            this.Renderer.RenderFrame(CaptureForVerification:true);
            var Captured=this.Renderer.LastCapturedFrame!;
            var Source=Captured.Pixels.ToArray();
            var Pixels=new byte[Width*Height*4];
            for(var Y=0;Y<Height;Y++){
                Array.Copy(Source,(int)(Y*Captured.RowPitch),Pixels,Y*Width*4,Width*4);
            }
            return Pixels;
        }
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
