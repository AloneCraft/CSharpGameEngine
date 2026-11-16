using Xunit;
using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>L4 の光源の計画（M0120 単位 2）。L0 から移した式の境界を、GPU を使わずに確かめる。</summary>
public sealed class StageLightPlannerTests {
    private static readonly Matrix4x4 ViewProjection=Matrix4x4.CreateLookAt(new Vector3(0f,0f,10f),Vector3.Zero,Vector3.UnitY)
        *Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3f,2f,0.1f,1000f);
    [Fact]
    public void LocalLightsSortByPriorityThenContributionThenSubmissionOrder(){
        StageLightRank[]Ranks=[
            new(0,0,5f,0),
            new(1,1,1f,1),
            new(2,0,5f,2),
            new(3,0,9f,3),
            new(4,1,1f,4),
        ];
        StageLightPlanner.SortLocal(Ranks);
        Assert.Equal([1,4,3,0,2],Ranks.Select(static Rank=>Rank.Index));
    }
    [Fact]
    public void DirectionalLightsIgnorePriorityAndSortByContributionThenSubmissionOrder(){
        StageLightRank[]Ranks=[new(0,9,1f,0),new(1,0,3f,1),new(2,0,3f,2)];
        StageLightPlanner.SortDirectional(Ranks);
        Assert.Equal([1,2,0],Ranks.Select(static Rank=>Rank.Index));
    }
    [Fact]
    public void LocalContributionFallsWithTheSquareOfDepth(){
        var Near=StageLightPlanner.LocalScreenContribution(Vector3.One,new Vector3(0f,0f,8f),1f,ViewProjection);
        var Far=StageLightPlanner.LocalScreenContribution(Vector3.One,new Vector3(0f,0f,6f),1f,ViewProjection);
        Assert.True(Near>Far);
        Assert.Equal(4f,Near/Far,3);
    }
    [Fact]
    public void NonFiniteLightsAreAlwaysKeptByTheirContribution(){
        Assert.Equal(float.MaxValue,StageLightPlanner.LocalScreenContribution(new Vector3(float.NaN,0f,0f),Vector3.Zero,1f,ViewProjection));
        Assert.Equal(float.MaxValue,StageLightPlanner.LocalScreenContribution(Vector3.One,Vector3.Zero,float.PositiveInfinity,ViewProjection));
        Assert.Equal(float.MaxValue,StageLightPlanner.DirectionalScreenContribution(new Vector3(0f,float.PositiveInfinity,0f)));
        Assert.Equal(0f,StageLightPlanner.DirectionalScreenContribution(new Vector3(-1f,-2f,-3f)));
        // カメラの位置にある光は深度で割らない。
        Assert.Equal(4f,StageLightPlanner.LocalScreenContribution(Vector3.One,new Vector3(0f,0f,10f),2f,ViewProjection));
    }
    [Fact]
    public void ALightInFrontCoversTheTilesAroundItsCentre(){
        Assert.True(StageLightPlanner.TryGetTileRange(Vector3.Zero,0.5f,ViewProjection,160,80,10,5,16,out var Tiles));
        Assert.InRange(Tiles.MinimumX,3,4);
        Assert.InRange(Tiles.MaximumX,5,6);
        Assert.InRange(Tiles.MinimumY,1,2);
        Assert.InRange(Tiles.MaximumY,2,3);
    }
    [Fact]
    public void ALightOutsideTheScreenHasNoTiles(){
        Assert.False(StageLightPlanner.TryGetTileRange(new Vector3(500f,0f,0f),1f,ViewProjection,160,80,10,5,16,out _));
        Assert.False(StageLightPlanner.TryGetTileRange(Vector3.Zero,1f,ViewProjection,160,80,0,0,16,out _));
    }
    [Fact]
    public void ALightReachingBehindTheCameraOrNotFiniteCoversEveryTile(){
        var Everything=new StageTileRange(0,9,0,4);
        Assert.True(StageLightPlanner.TryGetTileRange(new Vector3(0f,0f,9f),5f,ViewProjection,160,80,10,5,16,out var Behind));
        Assert.Equal(Everything,Behind);
        Assert.True(StageLightPlanner.TryGetTileRange(new Vector3(float.NaN,0f,0f),1f,ViewProjection,160,80,10,5,16,out var NotFinite));
        Assert.Equal(Everything,NotFinite);
        Assert.True(StageLightPlanner.TryGetTileRange(Vector3.Zero,0f,ViewProjection,160,80,10,5,16,out var NoRange));
        Assert.Equal(Everything,NoRange);
    }
    [Fact]
    public void AHugeLightIsClampedToTheScreen(){
        Assert.True(StageLightPlanner.TryGetTileRange(Vector3.Zero,8f,ViewProjection,160,80,10,5,16,out var Tiles));
        Assert.Equal(new StageTileRange(0,9,0,4),Tiles);
    }
}
