using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 仮想ジオメトリのページの出し入れ（M0131 単位 4、ADR-0100 決定 1・3）。根のページだけから始めた描画器が、選択の知らせを読み戻してページを置き、
/// 全部を置いた描画器と同じ絵に追いつく。途中のどのフレームも球に穴が無い。予算が全部に足りなければ、粗いまま穴なく描き、理由を報告する。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualGeometryStreamingGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=320;
    private const int Height=180;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_StartingFromTheRootCatchesUpWithTheFullImage(){
        using var Full=new Scene(Configure:null);
        using var Streamed=new Scene(Configure:static Renderer=>{
            Renderer.VirtualGeometryPreloadPagesForTesting=1;
            Renderer.VirtualGeometryPagesPerFrame=8;
        });
        var View=Camera.CreateFirstPerson(new Vector3(0f,0f,-2.2f));
        var Expected=Full.Render(View,out var Mask);
        Expected=Full.Render(View,out Mask);
        var CaughtUp=-1;
        var FirstResident=-1;
        byte[]?First=null;
        for(var Frame=0;Frame<80&&CaughtUp<0;Frame++){
            var Image=Streamed.Render(View,out var Distances);
            if(First is null){
                First=Image;
                // 置き場は最初のフレームの選択で作り、根のページだけを置く（知らせは数フレーム後に届く）。
                FirstResident=Streamed.Renderer.VirtualGeometryResidentPagesForTesting(0).Resident;
                Assert.Equal(1,FirstResident);
            }
            AssertNoHole(Mask,Distances,$"frame {Frame}");
            if(Image.AsSpan().SequenceEqual(Expected)){ CaughtUp=Frame; }
        }
        var (Resident,Total)=Streamed.Renderer.VirtualGeometryResidentPagesForTesting(0);
        var Report=Streamed.Renderer.VirtualGeometryStreamingReport;
        WriteCapture("catch-up",First!,Expected,$"caught up at frame {CaughtUp}; resident pages {FirstResident} at the start, {Resident} of {Total} at the end; allocated {Report.Allocated}, evicted {Report.Evicted}, denied by the per-frame budget {Report.DeniedByBudget}, denied full {Report.DeniedFull}");
        Assert.True(CaughtUp>0,$"the streamed image never matched the full image in 80 frames ({Resident} of {Total} pages resident)");
        Assert.True(Resident>FirstResident,$"no page was streamed in ({FirstResident} at the start, {Resident} now)");
        Assert.Equal(0,Streamed.Renderer.VirtualGeometryStreamingReport.DeniedFull);
        Assert.DoesNotContain(Streamed.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_ASmallBudgetDrawsCoarserWithoutHolesAndReportsIt(){
        using var Full=new Scene(Configure:null);
        using var Small=new Scene(Configure:static Renderer=>Renderer.VirtualGeometryPoolBytesPerMesh=96*1024);
        var View=Camera.CreateFirstPerson(new Vector3(0f,0f,-1.8f));
        _=Full.Render(View,out var Mask);
        _=Full.Render(View,out Mask);
        for(var Frame=0;Frame<30;Frame++){
            _=Small.Render(View,out var Distances);
            AssertNoHole(Mask,Distances,$"frame {Frame}");
        }
        var FullClusters=Full.Renderer.ReadVirtualGeometryClusterCountsForTesting()[0];
        var SmallClusters=Small.Renderer.ReadVirtualGeometryClusterCountsForTesting()[0];
        var (Resident,Total)=Small.Renderer.VirtualGeometryResidentPagesForTesting(0);
        var SmallReport=Small.Renderer.VirtualGeometryStreamingReport;
        WriteCapture("small-budget",Small.Render(View,out _),Full.Render(View,out _),$"96 KiB pool: resident pages {Resident} of {Total}, clusters drawn {SmallClusters} against {FullClusters} with every page; allocated {SmallReport.Allocated}, evicted {SmallReport.Evicted}, denied full {SmallReport.DeniedFull}");
        Assert.True(Resident<Total,$"a 96 KiB pool held every page ({Resident} of {Total})");
        Assert.True(SmallClusters<FullClusters,$"the small pool drew {SmallClusters} clusters, not fewer than {FullClusters}");
        Assert.True(Small.Renderer.VirtualGeometryStreamingReport.DeniedFull>0,"the small pool never reported a full pool");
        Assert.DoesNotContain(Small.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    private static void WriteCapture(string Name,byte[]Streamed,byte[]Full,string Result){
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_CAPTURE") is not{ Length:>0 }Capture){ return; }
        var Folder=Path.Combine(Capture,"streaming-"+Name);
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder,"source.rgba"),Streamed);
        File.WriteAllBytes(Path.Combine(Folder,"virtual.rgba"),Full);
        File.WriteAllText(Path.Combine(Folder,"result.txt"),Result);
    }
    /// <summary>全部を置いた絵で球が写っている所のうち、縁から 6 画素より内側（粗い球の多角形も必ず覆う所）は、距離が書かれている（穴が無い）。</summary>
    private static void AssertNoHole(float[]Mask,float[]Distances,string Context){
        const int Margin=6;
        var Holes=0;
        for(var Y=Margin;Y<Height-Margin;Y++){
            for(var X=Margin;X<Width-Margin;X++){
                var Inside=true;
                for(var Dy=-Margin;Dy<=Margin&&Inside;Dy+=Margin){
                    for(var Dx=-Margin;Dx<=Margin&&Inside;Dx+=Margin){ Inside=Mask[(Y+Dy)*Width+X+Dx]>0f; }
                }
                if(Inside&&Distances[Y*Width+X]<=0f){ Holes++; }
            }
        }
        Assert.True(Holes==0,$"{Context}: {Holes} pixels inside the sphere show the background");
    }
    private sealed class Scene:IDisposable {
        private readonly Win32Window Window;
        internal StageRenderer Renderer { get; }
        internal Scene(Action<StageRenderer>?Configure){
            this.Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Geometry Streaming GPU Test",Width=Width,Height=Height,Visible=false });
            var (Source,Geometry)=VirtualGeometryGpuTests.SphereMeshes();
            this.Renderer=StageRenderer.Create(this.Window.Handle,this.Window.ClientWidth,this.Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
                [Source with{ VirtualGeometry=Geometry }],
                [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.6f,0.4f,1f),RoughnessFactor=0.5f,MetallicFactor=0f }],
                [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
            Assert.True(this.Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,this.Renderer.ShaderArtifactDiagnostics));
            Configure?.Invoke(this.Renderer);
        }
        internal byte[]Render(Camera View,out float[]Distances){
            this.Renderer.SetAmbientLight(new Vector3(0.2f));
            this.Renderer.SetCamera(View);
            this.Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.4f,0.8f,-0.5f)),Vector3.One,3f);
            this.Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            this.Renderer.RenderFrame(CaptureForVerification:true);
            Distances=this.Renderer.ReadSceneDistancesForTesting();
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
