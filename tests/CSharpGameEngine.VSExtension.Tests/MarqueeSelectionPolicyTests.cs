using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class MarqueeSelectionPolicyTests {
    private static readonly EditorNodeId First=new(1);
    private static readonly EditorNodeId Second=new(2);
    private static readonly EditorNodeId Third=new(3);

    [Fact]
    public void Combine_Replace_UsesAllHitsAndLastHitAsPrimary(){
        MarqueeSelectionUpdate Result=MarqueeSelectionPolicy.Combine(
            [First],[Second,Third],Additive:false,CurrentPrimary:First);

        Assert.Equal([Second,Third],Result.NodeIds);
        Assert.Equal(Third,Result.PrimaryNodeId);
    }

    [Fact]
    public void Combine_Additive_PreservesCurrentAndAppendsOnlyNewHits(){
        MarqueeSelectionUpdate Result=MarqueeSelectionPolicy.Combine(
            [First,Second],[Second,Third],Additive:true,CurrentPrimary:Second);

        Assert.Equal([First,Second,Third],Result.NodeIds);
        Assert.Equal(Third,Result.PrimaryNodeId);
    }

    [Fact]
    public void Combine_ReplaceWithNoHits_ClearsSelection(){
        MarqueeSelectionUpdate Result=MarqueeSelectionPolicy.Combine(
            [First],[],Additive:false,CurrentPrimary:First);

        Assert.Empty(Result.NodeIds);
        Assert.Null(Result.PrimaryNodeId);
    }

    [Fact]
    public void Combine_AdditiveWithNoHits_PreservesSelectionAndPrimary(){
        MarqueeSelectionUpdate Result=MarqueeSelectionPolicy.Combine(
            [First,Second],[],Additive:true,CurrentPrimary:Second);

        Assert.Equal([First,Second],Result.NodeIds);
        Assert.Equal(Second,Result.PrimaryNodeId);
    }
}
