using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>L4 の影の計画（M0120 単位 3）。L0 から移した投影の作り方の境界を、GPU を使わずに確かめる。</summary>
public sealed class StageShadowPlannerTests {
    [Fact]
    public void PointFacesMatchTheAtlasPlanAndLookAlongEachAxis(){
        Assert.Equal(PhysicalShadowAtlasPlan.PointTilesPerLight,StageShadowPlanner.PointFaceCount);
        Span<Matrix4x4>Faces=stackalloc Matrix4x4[StageShadowPlanner.PointFaceCount];
        Assert.True(StageShadowPlanner.TryBuildPointFaceViewProjections(new Vector3(1f,2f,3f),10f,Faces));
        Vector3[]Axes=[Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ];
        for(var Face=0;Face<Axes.Length;Face++){
            // 面の向きへ 5 m 先の点は、その面の画面の中央に写る。
            var Clip=Vector4.Transform(new Vector4(new Vector3(1f,2f,3f)+Axes[Face]*5f,1f),Faces[Face]);
            Assert.True(Clip.W>0f);
            Assert.Equal(0f,Clip.X/Clip.W,4);
            Assert.Equal(0f,Clip.Y/Clip.W,4);
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>StageShadowPlanner.GetPointFaceBasis(6,out _,out _));
    }
    [Fact]
    public void InvalidCastersHaveNoProjection(){
        Span<Matrix4x4>Faces=stackalloc Matrix4x4[StageShadowPlanner.PointFaceCount];
        Assert.False(StageShadowPlanner.IsValidPointCaster(Vector3.Zero,0f));
        Assert.False(StageShadowPlanner.IsValidPointCaster(new Vector3(float.NaN,0f,0f),1f));
        Assert.False(StageShadowPlanner.TryBuildPointFaceViewProjections(Vector3.Zero,1f,Faces[..5]));
        Assert.False(StageShadowPlanner.TryBuildSpotViewProjection(Vector3.Zero,Vector3.Zero,5f,0.5f,out var Spot));
        Assert.Equal(Matrix4x4.Identity,Spot);
        Assert.False(StageShadowPlanner.TryBuildSpotViewProjection(Vector3.Zero,Vector3.UnitZ,float.PositiveInfinity,0.5f,out _));
        Assert.False(StageShadowPlanner.TryBuildAreaViewProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitZ,5f,1f,1f,out _));
        Assert.False(StageShadowPlanner.TryBuildAreaViewProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,5f,0f,1f,out _));
    }
    [Fact]
    public void ASpotStraightDownUsesASideUpVectorAndSeesAlongItsDirection(){
        Assert.True(StageShadowPlanner.TryBuildSpotViewProjection(new Vector3(0f,10f,0f),-Vector3.UnitY,20f,MathF.Cos(0.4f),out var Spot));
        var Clip=Vector4.Transform(new Vector4(0f,0f,0f,1f),Spot);
        Assert.True(Clip.W>0f);
        Assert.Equal(0f,Clip.X/Clip.W,4);
        Assert.Equal(0f,Clip.Y/Clip.W,4);
        Assert.True(StageShadowPlanner.TryBuildAreaViewProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,5f,1f,0.5f,out var Area));
        var AreaClip=Vector4.Transform(new Vector4(0f,0f,3f,1f),Area);
        Assert.Equal(0f,AreaClip.X/AreaClip.W,4);
    }
    [Fact]
    public void ProbeCaptureDepthIsLogarithmicAndRejectsAnInvalidRange(){
        var Depth=StageShadowPlanner.ProbeCaptureDepth(0.05f,500f);
        Assert.Equal(RenderDepthEncoding.Logarithmic,Depth.Encoding);
        Assert.Throws<InvalidOperationException>(()=>StageShadowPlanner.ProbeCaptureDepth(5f,1f));
    }
}
