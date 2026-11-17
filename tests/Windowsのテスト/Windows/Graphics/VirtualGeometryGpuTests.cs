using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Cooking.VirtualGeometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 仮想ジオメトリの描画（M0130 単位 4、ADR-0100 決定 3）。主パスが GPU で塊を選び、間接描画で描く。
/// 元のメッシュで描いた絵との差が小さい、近づくと選ぶ塊が増える、デバッグ層のエラーが無い、選択のシェーダーの式が共通の式の写しのまま、を確かめる。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualGeometryGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=320;
    private const int Height=180;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_VirtualGeometryLooksLikeTheSourceMesh(){
        var (Source,Geometry)=SphereMeshes();
        var Plain=Render(Source,AlongZ(4f),out _,out var PlainDrawn);
        var Virtual=Render(Source with{ VirtualGeometry=Geometry },AlongZ(4f),out var Clusters,out var VirtualDrawn);
        Assert.Equal(0,PlainDrawn);
        Assert.Equal(1,VirtualDrawn);
        Assert.True(Clusters[0]>0,"no cluster was selected");
        var (Different,Maximum)=Compare(Plain,Virtual);
        WriteCapture("main",Plain,Virtual,$"{Width}x{Height} clusters={Clusters[0]}/{Geometry.Clusters.Length} different={Different} maximum={Maximum}");
        Assert.True(Different*100<=Width*Height,$"{Different} of {Width*Height} pixels differ by more than 8 (largest difference {Maximum}); {Clusters[0]} of {Geometry.Clusters.Length} clusters drawn");
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_MovingCloserSelectsMoreClusters(){
        var (Source,Geometry)=SphereMeshes();
        var Mesh=Source with{ VirtualGeometry=Geometry };
        _=Render(Mesh,AlongZ(60f),out var Far,out _);
        _=Render(Mesh,AlongZ(2.5f),out var Near,out _);
        Assert.InRange(Far[0],1,Geometry.Clusters.Length);
        Assert.InRange(Near[0],1,Geometry.Clusters.Length);
        Assert.True(Near[0]>Far[0],$"near {Near[0]} clusters, far {Far[0]} clusters");
    }
    /// <summary>
    /// 仮想ジオメトリの物体も、主ビューで選んだ塊で影を落とす（M0130 単位 5）。床の上の球を、元のメッシュと仮想ジオメトリで描いて比べる。
    /// 影は球の左の床に落ち、開いた床より暗い。カスケード影と仮想シャドウの両方。
    /// </summary>
    [WindowsOnlyTheory]
    [Trait("Category","GPU")]
    [InlineData(DirectionalShadowMode.Cascaded)]
    [InlineData(DirectionalShadowMode.Virtual)]
    public void Warp_VirtualGeometryCastsTheSameShadow(DirectionalShadowMode Mode){
        var (Source,Geometry)=SphereMeshes();
        var Environment=Lighting(new DirectionalShadowSettings(Mode));
        var Plain=Render(Source,FloorView,out _,out _,Environment,Floor:true);
        var Virtual=Render(Source with{ VirtualGeometry=Geometry },FloorView,out _,out var Drawn,Environment,Floor:true);
        Assert.Equal(1,Drawn);
        var Shadow=Luminance(Virtual,new Vector3(-1.6f,0f,0.6f));
        var Open=Luminance(Virtual,new Vector3(2.2f,0f,0.6f));
        Assert.True(Shadow+20<Open,$"the floor in the shadow ({Shadow}) is not darker than the open floor ({Open})");
        var (Different,Maximum)=Compare(Plain,Virtual);
        WriteCapture($"shadow-{Mode}",Plain,Virtual,$"shadow={Shadow} open={Open} different={Different} maximum={Maximum}");
        Assert.True(Different*100<=Width*Height,$"{Mode}: {Different} of {Width*Height} pixels differ by more than 8 (largest difference {Maximum})");
    }
    /// <summary>
    /// Lumen（画面の下書きとカード撮影）と光のプローブ（DDGI）を有効にしても、仮想ジオメトリの物体を描けて、デバッグ層のエラーが出ない。
    /// カードと光のプローブは代理メッシュ（根の段の塊）を描くので、元のメッシュとの差は間接光の分だけ出てよい。差の大きさは記録だけにする。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_LumenAndProbesDrawTheProxyWithoutErrors(){
        var (Source,Geometry)=SphereMeshes();
        var Environment=Lighting(null) with{
            GlobalIllumination=new GlobalIlluminationSettings(new Vector3(0f,1.5f,0f),new Vector3(4f,1.5f,4f),2f,8,0f,1f),
            DistanceField=new GlobalDistanceFieldSettings(0.5f,3,32,default,Vector3.Zero),
            Lumen=LumenSettings.Unreal,
        };
        var Virtual=Render(Source with{ VirtualGeometry=Geometry },FloorView,out _,out var Drawn,Environment,Floor:true,Scalability:RenderScalability.High);
        Assert.Equal(1,Drawn);
        Assert.True(Luminance(Virtual,new Vector3(0f,1.2f,-0.9f))>0,"the sphere was not drawn");
    }
    private static readonly Camera FloorView=CreateFloorView();
    private static Camera CreateFloorView(){
        var View=Camera.CreateFirstPerson(new Vector3(0f,2.5f,-5f));
        View.Pitch=-0.35f;
        return View;
    }
    private static EnvironmentLightingSettings Lighting(DirectionalShadowSettings?Shadows)=>new(null,null,null,new ExposureSettings(ExposureMode.PhysicalCamera,2f,0f),ToneMapper.UnrealFilmicSrgb,0f,0.0064f,DirectionalShadows:Shadows);
    private static int Luminance(byte[]Pixels,Vector3 World){
        var Clip=Vector4.Transform(new Vector4(World,1f),FloorView.GetView()*FloorView.GetProjection((float)Width/Height));
        var X=(int)((Clip.X/Clip.W*0.5f+0.5f)*Width);
        var Y=(int)((0.5f-Clip.Y/Clip.W*0.5f)*Height);
        Assert.InRange(X,0,Width-1);
        Assert.InRange(Y,0,Height-1);
        var Offset=(Y*Width+X)*4;
        return Pixels[Offset]+Pixels[Offset+1]+Pixels[Offset+2];
    }
    /// <summary>ビュー投影から作った画面上の大きさの式は、カメラの画角と画面の高さから作った式と同じになる。</summary>
    [Theory]
    [InlineData(1.0f,180)]
    [InlineData(0.4f,1080)]
    public void TheMetricFromTheViewProjectionMatchesTheCamera(float FieldOfView,int ViewportHeight){
        var View=Camera.CreateFirstPerson(new Vector3(3f,2f,-7f));
        View.FovYRadians=FieldOfView;
        View.Yaw=0.7f;
        View.Pitch=-0.3f;
        var ViewProjection=View.GetView()*View.GetProjection(16f/9f);
        var Metric=StageRenderer.VirtualGeometryMetric(ViewProjection,ViewportHeight);
        var Expected=ScreenSizeMetric.Perspective(FieldOfView,ViewportHeight,0.01f);
        Assert.False(Metric.IsOrthographic);
        Assert.Equal(Expected.PixelsPerMeter,Metric.PixelsPerMeter,Expected.PixelsPerMeter*1e-4f);
    }
    /// <summary>計算シェーダーは取り込みを使えないので、選択のシェーダーは共通の式を写して持つ。その写しが共通の式と文字まで同じであること。</summary>
    [Fact]
    public void TheSelectionShaderCarriesAnExactCopyOfTheScreenSizeMetric(){
        var Text=File.ReadAllText(Path.Combine(RepositoryRoot(),"assets","Shaders","VirtualGeometry.hlsl")).ReplaceLineEndings("\n");
        var Start=Text.IndexOf('\n',Text.IndexOf("ScreenSizeMetric.Hlsl の写し",StringComparison.Ordinal))+1;
        var End=Text.IndexOf("// ---- 写しの終わり ----",StringComparison.Ordinal);
        Assert.True(Start>0&&End>Start,"the copy markers are missing");
        Assert.Equal(ScreenSizeMetric.Hlsl.ReplaceLineEndings("\n").TrimEnd('\n'),Text[Start..End].TrimEnd('\n'));
    }
    /// <summary>見比べる画像が要るときだけ、環境変数の場所へ RGBA のまま書き出す（記録用。試験の合否には使わない）。</summary>
    private static void WriteCapture(string Name,byte[]Plain,byte[]Virtual,string Result){
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_CAPTURE") is not{ Length:>0 }Capture){ return; }
        var Folder=Path.Combine(Capture,Name);
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder,"source.rgba"),Plain);
        File.WriteAllBytes(Path.Combine(Folder,"virtual.rgba"),Virtual);
        File.WriteAllText(Path.Combine(Folder,"result.txt"),Result);
    }
    private static Camera AlongZ(float Distance)=>Camera.CreateFirstPerson(new Vector3(0f,0f,-Distance));
    /// <summary>球（物体 0、原点）を描く。<paramref name="Floor"/> なら、球を床から 1.2 メートル上げ、16 メートル四方の床（物体 1）を足す。</summary>
    private static byte[]Render(StageMeshCreateInfo Mesh,Camera View,out int[]Clusters,out int Drawn,EnvironmentLightingSettings?Environment=null,bool Floor=false,RenderScalability?Scalability=null){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Geometry GPU Test",Width=Width,Height=Height,Visible=false });
        var Plane=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        var PlaneMesh=new StageMeshCreateInfo { Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Plane.Positions.Length).ToArray(),
            Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            Floor?[Mesh,PlaneMesh]:[Mesh],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.6f,0.4f,1f),RoughnessFactor=0.5f,MetallicFactor=0f },
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            Floor?[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 }]:[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        if(Scalability is{ }Quality){ Renderer.Scalability=Quality; }
        for(var Frame=0;Frame<3;Frame++){
            if(Environment is{ }Settings){ Renderer.SetEnvironmentLighting(Settings); }
            Renderer.SetAmbientLight(new Vector3(0.2f));
            Renderer.SetCamera(View);
            Renderer.SetDirectionalLight(Floor?Vector3.Normalize(new Vector3(0.7f,0.7f,0f)):Vector3.Normalize(new Vector3(0.4f,0.8f,-0.5f)),Vector3.One,3f);
            Renderer.SetObjectTransform(0,Floor?Matrix4x4.CreateTranslation(0f,1.2f,0f):Matrix4x4.Identity);
            if(Floor){ Renderer.SetObjectTransform(1,Matrix4x4.Identity); }
            Renderer.RenderFrame(CaptureForVerification:Frame==2);
        }
        Clusters=Renderer.ReadVirtualGeometryClusterCountsForTesting();
        Drawn=Renderer.VirtualGeometryObjectsDrawnLastFrameForTesting;
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
        var Captured=Renderer.LastCapturedFrame!;
        var Source=Captured.Pixels.ToArray();
        var Pixels=new byte[Width*Height*4];
        for(var Y=0;Y<Height;Y++){
            Array.Copy(Source,(int)(Y*Captured.RowPitch),Pixels,Y*Width*4,Width*4);
        }
        return Pixels;
    }
    private static (int Different,int Maximum)Compare(byte[]First,byte[]Second){
        var Different=0;
        var Maximum=0;
        for(var Pixel=0;Pixel<Width*Height;Pixel++){
            var Largest=0;
            for(var Channel=0;Channel<3;Channel++){ Largest=Math.Max(Largest,Math.Abs(First[Pixel*4+Channel]-Second[Pixel*4+Channel])); }
            Maximum=Math.Max(Maximum,Largest);
            if(Largest>8){ Different++; }
        }
        return (Different,Maximum);
    }
    /// <summary>半径 1 の UV 球（細かい: 64 段 × 128 列）と、その仮想ジオメトリ。継ぎ目と極は同じ値になるよう、角度を正確に置く。</summary>
    private static (StageMeshCreateInfo Source,StageVirtualGeometryCreateInfo Geometry)SphereMeshes(){
        const int Rings=64;
        const int Segments=128;
        var Positions=new List<Vector3>();
        var Normals=new List<Vector3>();
        var Uvs=new List<Vector2>();
        for(var Ring=0;Ring<=Rings;Ring++){
            var Polar=MathF.PI*Ring/Rings;
            var (SinPolar,CosPolar)=Ring==0?(0f,1f):Ring==Rings?(0f,-1f):MathF.SinCos(Polar);
            for(var Segment=0;Segment<=Segments;Segment++){
                var (SinAzimuth,CosAzimuth)=Segment==Segments?(0f,1f):MathF.SinCos(2f*MathF.PI*Segment/Segments);
                var Normal=new Vector3(SinPolar*CosAzimuth,CosPolar,SinPolar*SinAzimuth);
                Positions.Add(Normal);
                Normals.Add(Normal);
                Uvs.Add(new Vector2((float)Segment/Segments,(float)Ring/Rings));
            }
        }
        var Indices=new List<uint>();
        for(var Ring=0;Ring<Rings;Ring++){
            for(var Segment=0;Segment<Segments;Segment++){
                var A=(uint)(Ring*(Segments+1)+Segment);
                var B=A+Segments+1;
                if(Ring>0){ Indices.AddRange([A,A+1,B]); }
                if(Ring<Rings-1){ Indices.AddRange([A+1,B+1,B]); }
            }
        }
        var Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Positions.Count).ToArray();
        var Source=new StageMeshCreateInfo { Positions=[..Positions],Normals=[..Normals],Tangents=Tangents,Uvs=[..Uvs],Indices=[..Indices] };
        var Built=VirtualGeometryBuilder.Build([..Positions.Select((Position,Index)=>new VirtualGeometryVertex(Position,Normals[Index],Tangents[Index],Uvs[Index]))],[..Indices]);
        var Sizes=Built.GroupTriangleSizes();
        return (Source,new StageVirtualGeometryCreateInfo{
            Vertices=[..Built.Vertices.Select(static Vertex=>new StageVirtualGeometryVertex { Position=Vertex.Position,Normal=Vertex.Normal,Tangent=Vertex.Tangent,Uv=Vertex.Uv })],
            Indices=Built.Indices,
            Clusters=[..Built.Clusters.Select(static Cluster=>new StageVirtualGeometryCluster { FirstIndex=(uint)Cluster.FirstIndex,IndexCount=(uint)Cluster.IndexCount,Group=(uint)Cluster.Group,ParentGroup=Cluster.ParentGroup })],
            Groups=[..Built.Groups.Select((Group,Index)=>new StageVirtualGeometryGroup { BoundsCentre=Group.BoundsCentre,BoundsRadius=Group.BoundsRadius,Error=Group.Error,TriangleSize=Sizes[Index] })],
        });
    }
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
}
