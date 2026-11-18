using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 仮想シャドウの計測（M0132 単位 5）。実行時と同じくカメラを描画空間の原点にし、区画を渡して、1,200 m 四方の床と 10×10 の箱（間隔 12 m）の上を
/// カメラが秒速 3 m と 30 m で 60 フレーム歩く間の、要求・置いた・作ったページの数と、古くなったページの数（0 であること）、まだ作れていないページの数を数える。実機のときは影のパスの GPU の時間（仮想シャドウのページと、
/// 代わりに使うカスケード影）も測る。環境変数 <c>CGE_VIRTUAL_SHADOW_MEASURE</c> に場所を書くと数値をそこへ書き出し、<c>CGE_VIRTUAL_SHADOW_HARDWARE=1</c> なら実機の GPU で測る。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualShadowMeasurementTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    /// <summary>画面の大きさ。既定は 640×360、環境変数 <c>CGE_VIRTUAL_SHADOW_MEASURE_SIZE</c>（例 1920x1080）で変える。</summary>
    private static readonly (int Width,int Height)Size=System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_SHADOW_MEASURE_SIZE") is{ Length:>0 }Text&&Text.Split('x') is[var W,var H]?(int.Parse(W),int.Parse(H)):(640,360);
    private const int Rows=10;
    private const int MovingFrames=60;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AWalkingCameraReusesPagesAndMakesNoneStale(){
        var Hardware=System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_SHADOW_HARDWARE")=="1";
        var (Width,Height)=Size;
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Shadow Measurement",Width=Width,Height=Height,Visible=false });
        static StageMeshCreateInfo Mesh(ProceduralMeshData Data)=>new(){ Positions=Data.Positions,Normals=Data.Normals,
            Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Data.Positions.Length).ToArray(),Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
        List<StageObjectCreateInfo>Objects=[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }];
        List<Vector3>Positions=[Vector3.Zero];
        for(var Row=0;Row<Rows;Row++){
            for(var Column=0;Column<Rows;Column++){
                Objects.Add(new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 });
                Positions.Add(new Vector3(-54f+Column*12f,1.5f+(Row+Column)%3,10f+Row*12f));
            }
        }
        using var Renderer=StageRenderer.Create(Window.Handle,Width,Height,new GraphicsDeviceOptions { UseWarp=!Hardware,EnableDebugLayer=!Hardware },ShaderArtifactPath,
            [Mesh(ProceduralMeshFactory.CreatePlane(Width:1200f,Depth:1200f)),Mesh(ProceduralMeshFactory.CreateBox(new Vector3(2f,3f,2f)))],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],Objects);
        var View=Camera.CreateFirstPerson(Vector3.Zero);
        View.Pitch=-0.15f;
        var Lines=new List<string>{ $"{(Hardware?"hardware":"WARP")} {Width}x{Height}: 1,200 m floor, {Rows*Rows} boxes, camera-relative drawing with the anchor, {MovingFrames} frames per walk; "
            +$"physical atlas {Budget} pages = {Budget*128L*128*4/(1024*1024)} MiB" };
        static float Median(IEnumerable<float>Values){
            var Sorted=Values.Order().ToArray();
            return Sorted[Sorted.Length/2];
        }
        // 秒速 3 m（0.05 m/フレーム）と秒速 30 m（0.5 m/フレーム、60 fps）で歩く。区画 (2, 0, 0) の中（区画を跨ぐと全部のページを捨てる。1 km に 1 回）。
        foreach(var (Mode,Step) in new[]{ (DirectionalShadowMode.Virtual,0.05f),(DirectionalShadowMode.Virtual,0.5f),(DirectionalShadowMode.Cascaded,0.5f) }){
            Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(Mode) });
            var Start=new Vector3(3000.25f,1.7f,100f);
            var Steps=new List<(VirtualShadowStatistics Statistics,float Virtual,float Cascaded)>();
            for(var Frame=-StageRenderer.FrameCount-3;Frame<MovingFrames;Frame++){
                var CameraWorld=Start+new Vector3(0.2f,0f,1f)*Step*Math.Max(Frame,0);
                Renderer.SetVirtualShadowAnchor(new VirtualShadowAnchor(2,0,0,CameraWorld-new Vector3(2048f,0f,0f)));
                Renderer.SetAmbientLight(new Vector3(0.3f));
                Renderer.SetCamera(View);
                Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.35f,0.8f,-0.45f)),Vector3.One,3f,CastsShadows:true,ShadowBiasMeters:0.005f);
                Renderer.SetObjectTransform(0,Matrix4x4.CreateTranslation(new Vector3(3000f,0f,0f)-CameraWorld));
                for(var Index=1;Index<Positions.Count;Index++){ Renderer.SetObjectTransform(Index,Matrix4x4.CreateTranslation(new Vector3(3000f,0f,0f)+Positions[Index]-CameraWorld)); }
                Renderer.RenderFrame();
                if(Frame>=0){ Steps.Add((Renderer.VirtualShadowStatistics,Renderer.VirtualShadowGpuMilliseconds,Renderer.CascadedShadowGpuMilliseconds)); }
            }
            if(Mode==DirectionalShadowMode.Virtual){
                var Summary=string.Join("; ",Steps.Select(static Step=>$"{Step.Statistics.Requested}/{Step.Statistics.Resident}/{Step.Statistics.Rendered}/{Step.Statistics.Missing}/{Step.Statistics.Invalidated}"));
                // カメラだけが動くので、古くなるページは 0。止まっている最初のフレームは、要求が全部置いてある。
                Assert.True(Steps.All(static Step=>Step.Statistics.Invalidated==0),$"requested/resident/rendered/missing/stale: {Summary}");
                Assert.True(Steps[0].Statistics.Missing==0,$"requested/resident/rendered/missing/stale: {Summary}");
                Lines.Add($"virtual, {Step} m a frame: requested {Steps.Min(static Step=>Step.Statistics.Requested)}-{Steps.Max(static Step=>Step.Statistics.Requested)} pages, "
                    +$"rendered {Steps.Sum(static Step=>Step.Statistics.Rendered)} in {MovingFrames} frames (most in one frame {Steps.Max(static Step=>Step.Statistics.Rendered)}), stale 0, "
                    +$"reused per frame median {Median(Steps.Select(static Step=>(float)Step.Statistics.Reused))}, not yet rendered (drawn from a coarser level) median {Median(Steps.Select(static Step=>(float)Step.Statistics.Missing))}");
                if(Hardware){ Lines.Add($"virtual, {Step} m a frame: shadow GPU median virtual pages {Median(Steps.Select(static Step=>Step.Virtual)):F3} ms + fallback cascades {Median(Steps.Select(static Step=>Step.Cascaded)):F3} ms"); }
            } else if(Hardware){
                Lines.Add($"cascaded: shadow GPU median cascades {Median(Steps.Select(static Step=>Step.Cascaded)):F3} ms");
            }
        }
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_SHADOW_MEASURE") is{ Length:>0 }Folder){
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder,$"measurement-{(Hardware?"hardware":"warp")}-{Width}x{Height}.txt"),Lines);
        }
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    private static readonly int Budget=new DirectionalShadowSettings(DirectionalShadowMode.Virtual).PageBudget;
}
