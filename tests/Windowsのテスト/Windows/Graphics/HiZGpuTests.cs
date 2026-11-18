using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 遮蔽判定に使う Hi-Z（M0131 単位 1、ADR-0100 決定 3）。床と箱と空が写る場面を WARP で描き、主パスが書いた距離の画像から GPU で作った全段が、
/// CPU で同じ距離から作った段（<see cref="HiZPyramid.Build"/>）と 1 画素ずつ同じになる（最大を取るだけなので丸めの差は無い）。画面は奇数の大きさにして縁も試す。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class HiZGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=317;
    private const int Height=181;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_EveryLevelMatchesTheCpuPyramid(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Hi-Z GPU Test",Width=Width,Height=Height,Visible=false });
        var Plane=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        var Box=ProceduralMeshFactory.CreateBox(Vector3.One);
        static StageMeshCreateInfo Mesh(ProceduralMeshData Source)=>new(){ Positions=Source.Positions,Normals=Source.Normals,
            Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Source.Positions.Length).ToArray(),Uvs=new Vector2[Source.Positions.Length],Indices=Source.Indices };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Mesh(Plane),Mesh(Box)],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.BuildHiZAfterMainPassForTesting=true;
        var View=Camera.CreateFirstPerson(new Vector3(0f,1.5f,-5f));
        View.Pitch=-0.1f;
        for(var Frame=0;Frame<2;Frame++){
            Renderer.SetAmbientLight(new Vector3(0.3f));
            Renderer.SetCamera(View);
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.4f,0.8f,-0.5f)),Vector3.One,3f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0.5f,0.5f,0f));
            Renderer.RenderFrame();
        }
        var Distances=Renderer.ReadSceneDistancesForTesting();
        Assert.Equal(Width*Height,Distances.Length);
        Assert.Contains(Distances,static Distance=>Distance==0f);
        Assert.Contains(Distances,static Distance=>Distance>0f);
        var Expected=HiZPyramid.Build(Distances,Width,Height);
        Assert.Equal(Expected.Length,Renderer.HiZLevelCountForTesting);
        for(var Level=0;Level<Expected.Length;Level++){
            var (Values,LevelWidth,LevelHeight)=Renderer.ReadHiZLevelForTesting(Level);
            Assert.Equal(Expected[Level].Length,LevelWidth*LevelHeight);
            for(var Index=0;Index<Values.Length;Index++){
                Assert.True(Expected[Level][Index].Equals(Values[Index]),$"level {Level} ({LevelWidth}x{LevelHeight}) texel {Index % LevelWidth},{Index / LevelWidth}: GPU {Values[Index]}, CPU {Expected[Level][Index]}");
            }
        }
        // 空が写っているので最後の段は無限に遠く、床と箱だけの所は有限の距離になる。
        Assert.Equal(float.PositiveInfinity,Expected[^1][0]);
        Assert.Contains(Expected[0],static Value=>float.IsFinite(Value));
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
}
