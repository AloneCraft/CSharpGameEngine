using Xunit;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 材質のテクスチャの番号引き（M0129 単位 4、ADR-0100 決定 2）。材質の数には上限が無く、材質の表と記述子の置き場は足りなくなると広がる。
/// 広げた後も、どの材質もそれ自身のテクスチャを引く。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class MaterialTextureArrayGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Size=64;
    /// <summary>
    /// 初めの材質の枠（1 + DynamicMaterialHeadroom）と、テクスチャの記述子の置き場の両方を越えるだけ材質を足す。最初・途中・最後の材質で描いた画面の中央の画素が、
    /// その材質だけを持つ別の描画器（広げていない）で描いた画素と完全に一致する。違う番号を引けば色が変わるよう、材質ごとに違う色にする。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void MaterialsBeyondTheInitialCapacity_Warp_EachSampleTheirOwnTexture(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Material Texture Array",Width=Size,Height=Size,Visible=false });
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Plane()],[Material(-1)],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        var Initial=Renderer.MaterialTextureViewsForTesting;
        var Count=StageRenderer.DynamicMaterialHeadroom*2+8;
        for(var Index=0;Index<Count;Index++){
            Assert.Equal(Index+1,Renderer.AddMaterial(Material(Index)));
        }
        var Grown=Renderer.MaterialTextureViewsForTesting;
        Assert.True(Grown.Capacity>Initial.Capacity,$"the texture views grew from {Initial.Capacity} to {Grown.Capacity}");
        Assert.True(Grown.Count>Initial.Capacity,$"{Grown.Count} views live after growing past {Initial.Capacity}");
        List<(byte,byte,byte)>Seen=[];
        foreach(var Pick in new[]{ 0,StageRenderer.DynamicMaterialHeadroom+3,Count-1 }){
            var Object=Renderer.AddObject(0,Pick+1);
            Renderer.SetObjectVisibility(0,VisibleInView:false,CastsShadow:false);
            var Pixel=Centre(Renderer);
            Renderer.RemoveObject(Object);
            var Reference=RenderAlone(Material(Pick));
            Assert.True(Pixel==Reference,$"material {Pick+1}: pixel {Pixel} differs from the same material drawn alone {Reference}");
            Seen.Add(Pixel);
        }
        Assert.Equal(Seen.Count,Seen.Distinct().Count());
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [Fact]
    public void ADeviceBelowResourceBindingTierTwo_IsRefusedWithItsName(){
        var Error=Assert.Throws<NotSupportedException>(()=>StageRenderer.RequireResourceBindingTier(1,"Test Adapter"));
        Assert.Contains("Test Adapter",Error.Message,StringComparison.Ordinal);
        Assert.Contains("tier 1",Error.Message,StringComparison.Ordinal);
        StageRenderer.RequireResourceBindingTier(2,"Test Adapter");
    }
    private static (byte,byte,byte)RenderAlone(StageMaterialCreateInfo Material){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Material Texture Reference",Width=Size,Height=Size,Visible=false });
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Plane()],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        return Centre(Renderer);
    }
    /// <summary>真下を向いたカメラで、床を基本色の表示（照明なし）で描き、画面の中央の画素を読む。</summary>
    private static (byte,byte,byte)Centre(StageRenderer Renderer){
        Renderer.SetPresentationMode(RenderPresentationMode.BaseColor);
        var View=Camera.CreateFirstPerson(new Vector3(0f,4f,0f));
        View.Pitch=-1.5f;
        Renderer.SetCamera(View);
        Renderer.RenderFrame(CaptureForVerification:true);
        var Frame=Renderer.LastCapturedFrame!;
        var Offset=(Frame.Height/2*Frame.RowPitch)+(Frame.Width/2*4);
        return (Frame.Pixels[Offset],Frame.Pixels[Offset+1],Frame.Pixels[Offset+2]);
    }
    private static StageMeshCreateInfo Plane(){
        var Plane=ProceduralMeshFactory.CreatePlane(Width:40f,Depth:40f);
        var Tangents=new Vector4[Plane.Positions.Length];
        Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
        return new(){ Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Tangents,Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices };
    }
    /// <summary>
    /// 番号ごとに違う 1 × 1 の基本色と発光のテクスチャを持つ材質。赤は番号を 256 で割った余りから、青は商から決めるので、試験で足す数なら基本色はすべて違う。
    /// 発光は基本色の表示に影響しないが、テクスチャの見え方を材質ごとに 2 つ増やして、記述子の置き場を広げさせる。
    /// </summary>
    private static StageMaterialCreateInfo Material(int Index){
        var Red=(byte)((Index+1)*97%256);
        var Blue=(byte)(60+(Index+1)/256*80);
        var Glow=(byte)((Index+1)%7+1);
        return new(){
            BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f,
            BaseColorTexture=RenderArtifactCodec.CreateProceduralTexture(1,1,[Red,0,Blue,255]),
            EmissiveTexture=RenderArtifactCodec.CreateProceduralTexture(1,1,[Glow,Glow,Red,255]),
        };
    }
}
