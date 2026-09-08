using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.World.Tests;

public sealed class WorldCellSpatialIndexTests {
    private static readonly StableId Namespace=StableId.Parse("fcebe789-a0f8-5946-a25d-7f3210848c45");
    private static readonly ReferenceFrameId Frame=new(StableId.FromName(Namespace,"frame"));
    private static readonly AssetKey Payload=new(new AssetId(StableId.FromName(Namespace,"payload")),null);
    private static readonly AssetArtifactReference PayloadReference=new(Payload,Hash256.Compute([1,2,3]));

    [Fact]
    public void Select_IsFrameLocalDistanceOrderedAndBudgeted(){
        var Near=Cell("near",1,0);
        var Far=Cell("far",5,1);
        var OtherFrame=new ReferenceFrameId(StableId.FromName(Namespace,"other"));
        var Hidden=Cell("hidden",0,0,OtherFrame);
        var Index=CreateIndex([Far,Hidden,Near]);
        Span<WorldCellId>Selected=stackalloc WorldCellId[2];

        var Result=Index.Select(new WorldCellSelectionQuery(Frame,LogicalVector3.Zero,10,0,1),Selected);

        Assert.True(Result.Succeeded);
        Assert.Equal(2,Result.SelectedCount);
        Assert.Equal(Near.Id,Selected[0]);
        Assert.Equal(Far.Id,Selected[1]);
    }

    [Fact]
    public void Select_PrunesLargeCatalogAndAllocatesNothingAfterWarmup(){
        const int CellCount=4096;
        var Definitions=new WorldCellDefinition[CellCount];
        for(var Index=0;Index<CellCount;Index++){ Definitions[Index]=Cell("cell-"+Index,Index*4,0); }
        var Spatial=CreateIndex(Definitions);
        WorldCellId[]Destination=new WorldCellId[8];
        var Query=new WorldCellSelectionQuery(Frame,LogicalVector3.Zero,1);
        Spatial.Select(Query,Destination);
        var Before=GC.GetAllocatedBytesForCurrentThread();

        var Result=Spatial.Select(Query,Destination);

        var Allocated=GC.GetAllocatedBytesForCurrentThread()-Before;
        Assert.True(Result.Succeeded);
        Assert.Equal(1,Result.SelectedCount);
        Assert.True(Result.VisitedNodeCount<CellCount/8,$"Visited {Result.VisitedNodeCount} nodes.");
        Assert.Equal(0,Allocated);
    }

    [Fact]
    public void ManifestRejectsBoundsWhoseExtentOverflowsLogicalScalar(){
        var Invalid=Cell("overflow",LogicalScalar.MaxValue,0);

        Assert.False(WorldPartitionManifest.TryCreate([Invalid],out _,out var Error));
        Assert.Equal(WorldPartitionErrorCode.InvalidBounds,Error.Code);
    }

    private static WorldCellSpatialIndex CreateIndex(IEnumerable<WorldCellDefinition>Definitions){
        Assert.True(WorldPartitionManifest.TryCreate(Definitions,out var Manifest,out var Error),Error.Message);
        return new WorldCellSpatialIndex(Manifest);
    }

    private static WorldCellDefinition Cell(string Name,LogicalScalar R,int Level,ReferenceFrameId?CellFrame=null)=>new(
        new WorldCellId(StableId.FromName(Namespace,Name)),CellFrame??Frame,
        new FrameLocalBounds(new LogicalVector3(R,0,0),LogicalVector3.One),Payload,Level,
        ArtifactClosure:[PayloadReference]);
}
