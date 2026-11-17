using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 仮想シャドウのページの要求（M0132 単位 2、ADR-0100 決定 5）。広い床と箱を WARP で描き、主パスの後に計算シェーダーが付けた印を、
/// 読み戻した距離の画像から CPU で同じ規則（L4 の <see cref="VirtualShadowMapPlanner.Footprint"/> と <see cref="VirtualShadowMapPlanner.LevelForFootprint"/>）で求めた印と比べる。
/// 近くは細かい段、遠くは粗い段のページが要る。シェーダーの式は L4 の式の写しのまま。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualShadowRequestGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=320;
    private const int Height=180;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_TheShaderRequestsTheSamePagesAsTheCpu(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Shadow Request GPU Test",Width=Width,Height=Height,Visible=false });
        static StageMeshCreateInfo Mesh(ProceduralMeshData Data)=>new(){ Positions=Data.Positions,Normals=Data.Normals,
            Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Data.Positions.Length).ToArray(),Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Mesh(ProceduralMeshFactory.CreatePlane(Width:400f,Depth:400f)),Mesh(ProceduralMeshFactory.CreateBox(Vector3.One))],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.VirtualShadowRequestsForTesting=true;
        var View=Camera.CreateFirstPerson(new Vector3(0.3f,1.7f,-6f));
        View.Pitch=-0.12f;
        for(var Frame=0;Frame<2;Frame++){
            Renderer.SetAmbientLight(new Vector3(0.3f));
            Renderer.SetCamera(View);
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.35f,0.8f,-0.45f)),Vector3.One,3f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0.5f,0.5f,0f));
            Renderer.SetObjectTransform(2,Matrix4x4.CreateScale(2f,4f,2f)*Matrix4x4.CreateTranslation(-4f,2f,12f));
            Renderer.RenderFrame();
        }
        var Distances=Renderer.ReadSceneDistancesForTesting();
        var (Bits,_,Projections,Inverse,CameraPosition,PixelsPerMeter)=Renderer.ReadVirtualShadowRequestsForTesting();
        var Expected=CpuRequests(Distances,Projections,Inverse,CameraPosition,PixelsPerMeter);
        var Gpu=Pages(Bits);
        var Cpu=Pages(Expected);
        var OnlyGpu=Gpu.Except(Cpu).Count();
        var OnlyCpu=Cpu.Except(Gpu).Count();
        var Levels=Gpu.Select(static Page=>Page.Level).Distinct().Order().ToArray();
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_SHADOW_MEASURE") is{ Length:>0 }Folder){
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder,"requests.txt"),$"{Width}x{Height}: {Gpu.Count} pages requested by the GPU, {OnlyGpu} only on the GPU, {OnlyCpu} only on the CPU; pages per level: {string.Join(", ",Gpu.GroupBy(static Page=>Page.Level).OrderBy(static Group=>Group.Key).Select(static Group=>$"{Group.Key}:{Group.Count()}"))}");
        }
        Assert.True(Gpu.Count>20,$"only {Gpu.Count} pages were requested");
        // 段の境目のちょうど上の画素は、GPU と CPU の log2 や正規化の最後の桁で隣の段になってよい。
        Assert.True((OnlyGpu+OnlyCpu)*100<=Gpu.Count,$"{OnlyGpu} pages only on the GPU and {OnlyCpu} only on the CPU, of {Gpu.Count}; levels {string.Join(",",Levels)}");
        Assert.True(Levels.Length>=4,$"the requests use only the levels {string.Join(",",Levels)}; near pixels should need fine levels and far ones coarse levels");
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [Fact]
    public void TheShaderCopiesTheFootprintAndLevelRule(){
        var Text=File.ReadAllText(Path.Combine(RepositoryRoot(),"assets","Shaders","VirtualShadowRequest.hlsl")).ReplaceLineEndings("\n");
        const string Begin="// ---- VirtualShadowMapPlanner.Hlsl の写し";
        const string End="// ---- 写しの終わり ----";
        var Start=Text.IndexOf('\n',Text.IndexOf(Begin,StringComparison.Ordinal))+1;
        var Stop=Text.IndexOf(End,StringComparison.Ordinal);
        Assert.True(Start>0&&Stop>Start,"the copy markers are missing");
        Assert.Equal(VirtualShadowMapPlanner.Hlsl.ReplaceLineEndings("\n").TrimEnd('\n'),Text[Start..Stop].TrimEnd('\n'));
    }
    /// <summary>シェーダーと同じ規則で、CPU で印を付ける。</summary>
    private static uint[]CpuRequests(float[]Distances,Matrix4x4[]Projections,Matrix4x4 Inverse,Vector3 Camera,float PixelsPerMeter){
        var Result=new uint[VirtualShadowMapPlanner.TableLength/32];
        bool Position(int X,int Y,out Vector3 Point,out float Distance){
            Point=default;
            Distance=0f;
            if(X<0||Y<0||X>=Width||Y>=Height){ return false; }
            Distance=Distances[Y*Width+X];
            if(!(Distance>0f)){ return false; }
            var Ndc=new Vector2((X+0.5f)/Width*2f-1f,1f-(Y+0.5f)/Height*2f);
            var Near=Vector4.Transform(new Vector4(Ndc,0f,1f),Inverse);
            var Far=Vector4.Transform(new Vector4(Ndc,1f,1f),Inverse);
            var Direction=Vector3.Normalize(new Vector3(Far.X,Far.Y,Far.Z)/Far.W-new Vector3(Near.X,Near.Y,Near.Z)/Near.W);
            Point=Camera+Direction*Distance;
            return true;
        }
        float PlaneLength(Vector3 Difference){
            var Clip=Vector4.Transform(new Vector4(Difference,0f),Projections[0]);
            return new Vector2(Clip.X,Clip.Y).Length()*4f;
        }
        float Along(int X,int Y,int Dx,int Dy,Vector3 Point){
            var Result=float.PositiveInfinity;
            if(Position(X+Dx,Y+Dy,out var A,out _)){ Result=PlaneLength(A-Point); }
            if(Position(X-Dx,Y-Dy,out var B,out _)){ Result=MathF.Min(Result,PlaneLength(B-Point)); }
            return Result;
        }
        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){
                if(!Position(X,Y,out var Point,out var Distance)){ continue; }
                var Footprint=VirtualShadowMapPlanner.Footprint(Along(X,Y,1,0,Point),Along(X,Y,0,1,Point),Distance/PixelsPerMeter*0.5f);
                for(var Level=VirtualShadowMapPlanner.LevelForFootprint(Footprint);Level<VirtualShadowMapPlanner.LevelCount;Level++){
                    var Clip=Vector4.Transform(new Vector4(Point,1f),Projections[Level]);
                    var U=Clip.X*0.5f+0.5f;
                    var V=0.5f-Clip.Y*0.5f;
                    if(U<0f||V<0f||U>=1f||V>=1f||Clip.Z<0f||Clip.Z>1f){ continue; }
                    var Index=Level*16384+Math.Min((int)(V*128f),127)*128+Math.Min((int)(U*128f),127);
                    Result[Index>>5]|=1u<<(Index&31);
                    break;
                }
            }
        }
        return Result;
    }
    private static HashSet<(int Level,int X,int Y)>Pages(uint[]Bits){
        var Result=new HashSet<(int Level,int X,int Y)>();
        for(var Index=0;Index<Bits.Length*32;Index++){
            if((Bits[Index>>5]&(1u<<(Index&31)))!=0){ Result.Add((Index/16384,Index%128,Index/128%128)); }
        }
        return Result;
    }
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
}
