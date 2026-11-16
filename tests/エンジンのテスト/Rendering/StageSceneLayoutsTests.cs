using Xunit;
using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>
/// GPU シーンの表の形（M0129 単位 3、ADR-0100 決定 2）。シェーダーとの取り決め（stage-v17）が名前に持つ大きさと、材質の値から表の 1 件を作る規則を、
/// GPU を使わずに確かめる。シェーダー側の配置との一致は、DXC の反射で確かめる（StageConstantLayoutReflectionTests）。
/// </summary>
public sealed class StageSceneLayoutsTests {
    [Fact]
    public void TheTablesHaveTheSizesTheShaderContractNames(){
        Assert.Equal(84u,StageGpuLayouts.InstanceDataSize);
        Assert.Equal(128u,StageGpuLayouts.MaterialDataSize);
        Assert.Equal(3u,StageGpuLayouts.DrawConstantCount);
    }
    [Fact]
    public void AMaterialWithoutAGridLeavesEveryGridFieldZero(){
        var Data=StageMaterialData.Create(new Vector4(0.1f,0.2f,0.3f,1f),0.7f,0.25f,new Vector3(5f,6f,7f),WorldGrid:null,new Vector3(9f,9f,9f),new StageUInt4 { X=1,Y=2,Z=3,W=4 });
        Assert.Equal(new Vector4(0.1f,0.2f,0.3f,1f),Data.BaseColorFactor);
        Assert.Equal(new Vector4(0.7f,0.25f,0f,0f),Data.MaterialParams);
        Assert.Equal(new Vector4(5f,6f,7f,0f),Data.EmissiveNits);
        Assert.Equal(Vector4.Zero,Data.WorldGridParams);
        Assert.Equal(Vector4.Zero,Data.WorldGridColor);
        Assert.Equal(Vector4.Zero,Data.WorldSubGridColor);
        Assert.Equal(Vector4.Zero,Data.WorldGridAnchor);
        Assert.Equal((1u,2u,3u,4u),(Data.Textures.X,Data.Textures.Y,Data.Textures.Z,Data.Textures.W));
    }
    [Fact]
    public void AGridCarriesItsAnchorUnlessItFollowsItsObject(){
        StageWorldGrid Grid=new(GridSizeMeters:2f,SubGridDivisions:4,WireHalfWidth:0.01f,GridColor:new Vector3(1f,0f,0f),SubGridColor:new Vector3(0f,1f,0f));
        var World=StageMaterialData.Create(Vector4.One,1f,0f,Vector3.Zero,Grid,new Vector3(0.5f,1f,1.5f),default);
        Assert.Equal(new Vector4(2f,4f,0.01f,0f),World.WorldGridParams);
        Assert.Equal(new Vector4(1f,0f,0f,0f),World.WorldGridColor);
        Assert.Equal(new Vector4(0f,1f,0f,0f),World.WorldSubGridColor);
        Assert.Equal(new Vector4(0.5f,1f,1.5f,0f),World.WorldGridAnchor);
        // 物体に固定する格子は、ずれを持たず、w の印でシェーダーに物体の位置を使わせる。
        var Object=StageMaterialData.Create(Vector4.One,1f,0f,Vector3.Zero,Grid with{ AnchorToObject=true },new Vector3(0.5f,1f,1.5f),default);
        Assert.Equal(new Vector4(0f,0f,0f,1f),Object.WorldGridAnchor);
    }
}
