using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 主パスの遮蔽判定の効き目を測る（M0131 単位 5）。壁の後ろに仮想ジオメトリの球を 36 個（6×6）、手前に 2 個置き、遮蔽ありとなしで、
/// 主パスで描いた塊の数と、主パスの GPU の時間（実機のときだけ。Hi-Z と調べる分を含む。5 回の中央値）を比べる。壁を外した場面（手前の列の球が後ろの列を一部隠すだけ）も測る。
/// どの場面も、遮蔽ありとなしで画素が完全に同じ。
/// 環境変数 <c>CGE_VIRTUAL_GEOMETRY_MEASURE</c> に場所を書くと数値をそこへ書き出し、<c>CGE_VIRTUAL_GEOMETRY_HARDWARE=1</c> なら実機の GPU で測る。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualGeometryOcclusionMeasurementTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    /// <summary>画面の大きさ。既定は 640×360、環境変数 <c>CGE_VIRTUAL_GEOMETRY_MEASURE_SIZE</c>（例 1920x1080）で変える。</summary>
    private static readonly (int Width,int Height)Size=System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_MEASURE_SIZE") is{ Length:>0 }Text&&Text.Split('x') is[var W,var H]?(int.Parse(W),int.Parse(H)):(640,360);
    private static int Width=>Size.Width;
    private static int Height=>Size.Height;
    private const int HiddenRows=6;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void OcclusionSkipsTheSpheresBehindTheWallAndKeepsTheImage(){
        var Hardware=System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_HARDWARE")=="1";
        List<string>Lines=[$"{(Hardware?"hardware":"WARP")} {Width}x{Height}, {HiddenRows*HiddenRows} spheres behind the wall, 2 in front, each sphere {VirtualGeometryGpuTests.SphereMeshes().Source.Indices.Count/3} source triangles"];
        foreach(var Wall in new[]{ true,false }){
            var Without=Measure(Occlusion:false,Wall,Hardware,out var SelectedWithout,out var DrawnWithout,out var MillisecondsWithout);
            var With=Measure(Occlusion:true,Wall,Hardware,out var SelectedWith,out var DrawnWith,out var MillisecondsWith);
            Assert.Equal(SelectedWithout,SelectedWith);
            Assert.True(Without.AsSpan().SequenceEqual(With),$"wall {Wall}: the image with occlusion differs from the image without");
            Lines.Add($"{(Wall?"wall":"no wall")}: selected {SelectedWith} clusters; main pass draws {DrawnWithout} without occlusion ({MillisecondsWithout:F3} ms), {DrawnWith} with occlusion ({MillisecondsWith:F3} ms)");
            // 壁が無くても、手前の列の球が後ろの列を一部隠すので、描く塊は選んだ数以下になる。
            if(Wall){ Assert.True(DrawnWith*2<DrawnWithout,$"occlusion drew {DrawnWith} of {DrawnWithout} clusters behind a wall"); }
            else { Assert.InRange(DrawnWith,1,DrawnWithout); }
        }
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_MEASURE") is{ Length:>0 }Folder){
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder,$"occlusion-{(Hardware?"hardware":"warp")}-{Width}x{Height}.txt"),Lines);
        }
    }
    /// <summary>描いて、選んだ塊の数、主パスで描いた塊の数（遮蔽ありなら 1・2 パス目の和）、主パスの GPU の時間を返す。</summary>
    private static byte[]Measure(bool Occlusion,bool Wall,bool Hardware,out int Selected,out int Drawn,out float Milliseconds){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Geometry Occlusion Measurement",Width=Width,Height=Height,Visible=false });
        var (Source,Geometry)=VirtualGeometryGpuTests.SphereMeshes();
        var Box=ProceduralMeshFactory.CreateBox(new Vector3(14f,5f,0.3f));
        var Plane=ProceduralMeshFactory.CreatePlane(Width:60f,Depth:60f);
        static StageMeshCreateInfo Mesh(ProceduralMeshData Data)=>new(){ Positions=Data.Positions,Normals=Data.Normals,
            Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Data.Positions.Length).ToArray(),Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
        List<StageObjectCreateInfo>Objects=[];
        List<Matrix4x4>Transforms=[];
        for(var Row=0;Row<HiddenRows;Row++){
            for(var Column=0;Column<HiddenRows;Column++){
                Objects.Add(new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 });
                Transforms.Add(Matrix4x4.CreateTranslation(-6.25f+Column*2.5f,1.2f,3f+Row*2.5f));
            }
        }
        Objects.Add(new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 });
        Transforms.Add(Matrix4x4.CreateTranslation(-3f,1f,-4f));
        Objects.Add(new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 });
        Transforms.Add(Matrix4x4.CreateTranslation(3f,1f,-4f));
        Objects.Add(new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 });
        Transforms.Add(Matrix4x4.Identity);
        if(Wall){
            Objects.Add(new StageObjectCreateInfo { MeshIndex=2,MaterialIndex=1 });
            Transforms.Add(Matrix4x4.CreateTranslation(0f,2.5f,0f));
        }
        var SphereCount=HiddenRows*HiddenRows+2;
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=!Hardware,EnableDebugLayer=!Hardware },ShaderArtifactPath,
            [Source with{ VirtualGeometry=Geometry },Mesh(Plane),Mesh(Box)],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.6f,0.4f,1f),RoughnessFactor=0.5f,MetallicFactor=0f },
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [..Objects]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.VirtualGeometryOcclusion=Occlusion;
        if(Hardware){ Renderer.EnableMainPassTimingForTesting(); }
        var View=Camera.CreateFirstPerson(new Vector3(0f,1.5f,-9f));
        List<float>Times=[];
        var Frames=Hardware?10:3;
        for(var Frame=0;Frame<Frames;Frame++){
            Renderer.SetAmbientLight(new Vector3(0.2f));
            Renderer.SetCamera(View);
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.4f,0.8f,-0.5f)),Vector3.One,3f);
            for(var Index=0;Index<Transforms.Count;Index++){ Renderer.SetObjectTransform(Index,Transforms[Index]); }
            Renderer.RenderFrame(CaptureForVerification:Frame==Frames-1);
            if(Hardware&&Frame>=Frames-5){ Times.Add(Renderer.ReadMainPassGpuMillisecondsForTesting()); }
        }
        Milliseconds=Times.Count==0?0f:Times.Order().ElementAt(Times.Count/2);
        var Counts=Renderer.ReadVirtualGeometryClusterCountsForTesting();
        Selected=Counts.Take(SphereCount).Sum();
        Drawn=Occlusion?Renderer.ReadVirtualGeometryPassCountsForTesting().Take(SphereCount).Sum(static Pass=>Pass.First+Pass.Second):Selected;
        if(!Hardware){ Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse); }
        var Captured=Renderer.LastCapturedFrame!;
        var Pixels=Captured.Pixels.ToArray();
        var Result=new byte[Width*Height*4];
        for(var Y=0;Y<Height;Y++){
            Array.Copy(Pixels,(int)(Y*Captured.RowPitch),Result,Y*Width*4,Width*4);
        }
        return Result;
    }
}
