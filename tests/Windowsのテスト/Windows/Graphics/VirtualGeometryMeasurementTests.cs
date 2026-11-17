using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Cooking.VirtualGeometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 三角形の下限の計測（M0130 単位 6、ADR-0100 決定 3）。細かい凹凸のある球を、元のメッシュ・仮想ジオメトリ（下限なし）・仮想ジオメトリ（下限あり）で同じカメラから描き、
/// 描いた三角形の数と、元のメッシュとの絵の差を比べる。下限を上げるほど三角形は減り、絵の差は広がる。
/// 環境変数 <c>CGE_VIRTUAL_GEOMETRY_MEASURE</c> に場所を書くと、数値（と実機の GPU の時間）をその場所へ書き出す。<c>CGE_VIRTUAL_GEOMETRY_HARDWARE=1</c> なら実機の GPU で測る。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualGeometryMeasurementTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=1280;
    private const int Height=720;
    private static readonly float[]Floors=[0f,4f,8f,16f,32f];
    private const int NormalMapSize=1024;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void TheTriangleFloorTradesTrianglesForASmallImageDifference(){
        var Hardware=System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_HARDWARE")=="1";
        var (Source,Geometry,Built,SourceVertices)=BumpySphere(128,256);
        var NormalMapTexture=RenderArtifactCodec.CreateProceduralTexture(NormalMapSize,NormalMapSize,ObjectNormalMapBaker.Bake(SourceVertices,[..Source.Indices],NormalMapSize)!);
        List<string>Lines=[$"{(Hardware?"hardware":"WARP")} {Width}x{Height}, source {Source.Indices.Count/3} triangles, {Built.Clusters.Length} clusters, {Built.LevelCount} levels"];
        foreach(var Distance in new[]{ 2.2f,6f }){
            var Plain=Render(Source,Distance,0f,Hardware,out _,out var PlainMilliseconds);
            var MeanWithoutNormalMap=new Dictionary<float,float>();
            Lines.Add($"distance {Distance} m: source {Source.Indices.Count/3} triangles, {PlainMilliseconds:F3} ms");
            var Previous=int.MaxValue;
            var WithoutFloor=0;
            foreach(var (Floor,NormalMap) in Floors.Select(static Floor=>(Floor,false)).Concat(Floors.Where(static Floor=>Floor>0f).Select(static Floor=>(Floor,true)))){
                if(NormalMap&&Floor==Floors[1]){ Previous=int.MaxValue; }
                var Image=Render(Source with{ VirtualGeometry=NormalMap?Geometry with{ ObjectNormalMap=NormalMapTexture }:Geometry },Distance,Floor,Hardware,out var Visible,out var Milliseconds);
                var Triangles=Visible.Sum(Cluster=>Built.Clusters[Cluster].IndexCount/3);
                var (Different,Mean)=Compare(Plain,Image);
                if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_MEASURE") is{ Length:>0 }ImageFolder&&!Hardware&&Floor is 0f or 16f){
                    var Suffix=NormalMap?"-normalmap":"";
                    Directory.CreateDirectory(ImageFolder);
                    File.WriteAllBytes(Path.Combine(ImageFolder,$"distance{Distance}-source.rgba"),Plain);
                    File.WriteAllBytes(Path.Combine(ImageFolder,$"distance{Distance}-floor{Floor}{Suffix}.rgba"),Image);
                }
                Lines.Add($"  floor {Floor} px{(NormalMap?" + normal map":"")}: {Triangles} triangles ({Visible.Length} clusters), {Different} pixels differ by more than 8, mean difference {Mean:F3}, {Milliseconds:F3} ms");
                Assert.True(Triangles<=Previous,$"distance {Distance}: floor {Floor} px draws {Triangles} triangles, more than {Previous}");
                if(Floor==0f){ WithoutFloor=Triangles; }
                // 法線マップは、下限で凹凸の陰影が消えた所（平均の差が 0.2 を超える所）を元へ戻す。全部細かく描く所では、8 ビットと画素の丸めの分だけ少し差が増えてよい。
                if(NormalMap&&MeanWithoutNormalMap[Floor]>0.2f){ Assert.True(Mean<MeanWithoutNormalMap[Floor],$"distance {Distance}: the normal map did not improve floor {Floor} px ({Mean} against {MeanWithoutNormalMap[Floor]})"); }
                else { MeanWithoutNormalMap[Floor]=Mean; }
                Previous=Triangles;
            }
            Assert.True(Previous<WithoutFloor,$"distance {Distance}: the floor did not reduce the triangles ({Previous} of {WithoutFloor})");
        }
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_GEOMETRY_MEASURE") is{ Length:>0 }Folder){
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder,Hardware?"hardware.txt":"warp.txt"),Lines);
        }
    }
    /// <summary>描いて、選んだ塊の番号と、主パスの GPU の時間（実機のときだけ。5 回の中央値）を返す。</summary>
    private static byte[]Render(StageMeshCreateInfo Mesh,float Distance,float Floor,bool Hardware,out int[]Visible,out float Milliseconds){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Geometry Measurement",Width=Width,Height=Height,Visible=false });
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=!Hardware,EnableDebugLayer=!Hardware },ShaderArtifactPath,
            [Mesh],[new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.6f,0.4f,1f),RoughnessFactor=0.5f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.VirtualGeometryTriangleFloorPixels=Floor;
        Renderer.VirtualGeometryTriangleFloorWithoutNormalMapForTesting=true;
        if(Hardware){ Renderer.EnableMainPassTimingForTesting(); }
        var View=Camera.CreateFirstPerson(new Vector3(0f,0f,-Distance));
        List<float>Times=[];
        var Frames=Hardware?8:2;
        for(var Frame=0;Frame<Frames;Frame++){
            Renderer.SetAmbientLight(new Vector3(0.2f));
            Renderer.SetCamera(View);
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.4f,0.8f,-0.5f)),Vector3.One,3f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.RenderFrame(CaptureForVerification:Frame==Frames-1);
            if(Hardware&&Frame>=Frames-5){ Times.Add(Renderer.ReadMainPassGpuMillisecondsForTesting()); }
        }
        Milliseconds=Times.Count==0?0f:Times.Order().ElementAt(Times.Count/2);
        Visible=Mesh.VirtualGeometry is null?[]:Renderer.ReadVirtualGeometryVisibleClustersForTesting(0);
        if(!Hardware){ Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse); }
        var Captured=Renderer.LastCapturedFrame!;
        var Source=Captured.Pixels.ToArray();
        var Pixels=new byte[Width*Height*4];
        for(var Y=0;Y<Height;Y++){
            Array.Copy(Source,(int)(Y*Captured.RowPitch),Pixels,Y*Width*4,Width*4);
        }
        return Pixels;
    }
    private static (int Different,float Mean)Compare(byte[]First,byte[]Second){
        var Different=0;
        long Sum=0;
        for(var Pixel=0;Pixel<Width*Height;Pixel++){
            var Largest=0;
            for(var Channel=0;Channel<3;Channel++){
                var Difference=Math.Abs(First[Pixel*4+Channel]-Second[Pixel*4+Channel]);
                Largest=Math.Max(Largest,Difference);
                Sum+=Difference;
            }
            if(Largest>8){ Different++; }
        }
        return (Different,Sum/(3f*Width*Height));
    }
    /// <summary>半径 1 の球に、緯度と経度で 24 回ずつ波打つ 2 センチの凹凸を付けたもの。法線は面の向きの平均。</summary>
    private static (StageMeshCreateInfo Source,StageVirtualGeometryCreateInfo Geometry,VirtualGeometryMesh Built,VirtualGeometryVertex[]SourceVertices)BumpySphere(int Rings,int Segments){
        var Positions=new List<Vector3>();
        var Uvs=new List<Vector2>();
        for(var Ring=0;Ring<=Rings;Ring++){
            var Polar=MathF.PI*Ring/Rings;
            var (SinPolar,CosPolar)=Ring==0?(0f,1f):Ring==Rings?(0f,-1f):MathF.SinCos(Polar);
            for(var Segment=0;Segment<=Segments;Segment++){
                var Azimuth=2f*MathF.PI*Segment/Segments;
                var (SinAzimuth,CosAzimuth)=Segment==Segments?(0f,1f):MathF.SinCos(Azimuth);
                var Bump=Ring==0||Ring==Rings?0f:0.02f*MathF.Sin(24f*Polar)*MathF.Sin(24f*(Segment==Segments?0f:Azimuth));
                Positions.Add(new Vector3(SinPolar*CosAzimuth,CosPolar,SinPolar*SinAzimuth)*(1f+Bump));
                Uvs.Add(new Vector2((float)Segment/Segments,(float)Ring/Rings));
            }
        }
        var Indices=new List<uint>();
        for(var Ring=0;Ring<Rings;Ring++){
            for(var Segment=0;Segment<Segments;Segment++){
                var A=(uint)(Ring*(Segments+1)+Segment);
                var B=A+(uint)Segments+1;
                if(Ring>0){ Indices.AddRange([A,A+1,B]); }
                if(Ring<Rings-1){ Indices.AddRange([A+1,B+1,B]); }
            }
        }
        // 法線は、同じ位置の頂点（継ぎ目）をまとめて面の向きを足す。
        var Sums=new Dictionary<Vector3,Vector3>();
        for(var Corner=0;Corner<Indices.Count;Corner+=3){
            var (P0,P1,P2)=(Positions[(int)Indices[Corner]],Positions[(int)Indices[Corner+1]],Positions[(int)Indices[Corner+2]]);
            var Face=Vector3.Cross(P1-P0,P2-P0);
            foreach(var P in new[]{ P0,P1,P2 }){ Sums[P]=Sums.GetValueOrDefault(P)+Face; }
        }
        var Normals=Positions.Select(Position=>{
            var Sum=Sums[Position];
            var Normal=Vector3.Normalize(Sum);
            return Vector3.Dot(Normal,Position)<0f?-Normal:Normal;
        }).ToArray();
        var Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Positions.Count).ToArray();
        var Source=new StageMeshCreateInfo { Positions=[..Positions],Normals=Normals,Tangents=Tangents,Uvs=[..Uvs],Indices=[..Indices] };
        VirtualGeometryVertex[]SourceVertices=[..Positions.Select((Position,Index)=>new VirtualGeometryVertex(Position,Normals[Index],Tangents[Index],Uvs[Index]))];
        var Built=VirtualGeometryBuilder.Build(SourceVertices,[..Indices]);
        var Sizes=Built.GroupTriangleSizes();
        var Geometry=new StageVirtualGeometryCreateInfo{
            Vertices=[..Built.Vertices.Select(static Vertex=>new StageVirtualGeometryVertex { Position=Vertex.Position,Normal=Vertex.Normal,Tangent=Vertex.Tangent,Uv=Vertex.Uv })],
            Indices=Built.Indices,
            Clusters=[..Built.Clusters.Select(static Cluster=>new StageVirtualGeometryCluster { FirstIndex=(uint)Cluster.FirstIndex,IndexCount=(uint)Cluster.IndexCount,Group=(uint)Cluster.Group,ParentGroup=Cluster.ParentGroup })],
            Groups=[..Built.Groups.Select((Group,Index)=>new StageVirtualGeometryGroup { BoundsCentre=Group.BoundsCentre,BoundsRadius=Group.BoundsRadius,Error=Group.Error,TriangleSize=Sizes[Index] })],
        };
        return (Source,Geometry,Built,SourceVertices);
    }
}
