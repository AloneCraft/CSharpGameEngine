using System.Globalization;
using System.Numerics;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Rendering.Tests;

public sealed class RenderWorldSnapshotTests {
    private static readonly StableId NamespaceId=StableId.Parse("b06e594a-7f21-5d1f-a8ea-8c113370ae26");
    private static readonly RenderMeshId NearMesh=new(Id("mesh-near"));
    private static readonly RenderMeshId FarMesh=new(Id("mesh-far"));
    private static readonly RenderMaterialId MaterialA=new(Id("material-a"));
    private static readonly RenderMaterialId MaterialB=new(Id("material-b"));

    [Fact]
    public void SnapshotCopiesAndCanonicalizesInstancesByStableId(){
        RenderLodLevel[]Lods=[new(NearMesh,10f),new(FarMesh,1000f)];
        RenderInstanceRecord[]Instances=[
            Instance(Id("second"),MaterialA,new Vector3(0f,0f,2f)),
            Instance(Id("first"),MaterialA,new Vector3(0f,0f,1f)),
        ];
        var Expected=Instances.Select(static Instance=>Instance.InstanceId).Order().ToArray();

        RenderWorldSnapshot Snapshot=RenderWorldSnapshot.Create(42,Instances,Lods);
        Instances[0]=default;
        Lods[0]=default;

        Assert.Equal(42ul,Snapshot.Tick);
        Assert.Equal(Expected,Snapshot.Instances.ToArray().Select(static Instance=>Instance.InstanceId));
        Assert.Equal(NearMesh,Snapshot.Lods[0].Mesh);
    }

    [Fact]
    public void FrustumSelectionRejectsOutsideInstancesAndWritesStableState(){
        RenderWorldSnapshot Snapshot=CreateSnapshot(
            Instance(Id("inside"),MaterialA,new Vector3(0f,0f,5f)),
            Instance(Id("outside"),MaterialA,new Vector3(50f,0f,5f)));
        var States=new RenderLodState[Snapshot.Instances.Length];
        var Visible=new VisibleRenderInstance[Snapshot.Instances.Length];
        RenderVisibilityView View=new(
            RenderFrustum.CreateAxisAligned(new Vector3(-10f),new Vector3(10f)),0.1f);

        RenderVisibilityQueryResult Result=RenderVisibility.Select(
            Snapshot,View,[],States,Visible);

        Assert.Equal(1,Result.VisibleCount);
        Assert.Equal(Snapshot.Instances.Length,Result.StateCount);
        Assert.Equal(Id("inside"),Visible[0].InstanceId);
        Assert.All(States,static State=>Assert.True(State.InstanceId.IsValid));
    }

    [Fact]
    public void ReextractionAfterCameraMovementCannotRetainAStaleVisibleInstance(){
        StableId FirstId=Id("camera-first");
        StableId SecondId=Id("camera-second");
        RenderWorldSnapshot First=CreateSnapshot(
            Instance(FirstId,MaterialA,new Vector3(0f,0f,5f)),
            Instance(SecondId,MaterialA,new Vector3(50f,0f,5f)));
        RenderWorldSnapshot Second=CreateSnapshot(
            Instance(FirstId,MaterialA,new Vector3(50f,0f,5f)),
            Instance(SecondId,MaterialA,new Vector3(0f,0f,5f)));
        RenderVisibilityView View=new(
            RenderFrustum.CreateAxisAligned(new Vector3(-10f),new Vector3(10f)),0.1f);
        var FirstStates=new RenderLodState[2];
        var SecondStates=new RenderLodState[2];
        var Visible=new VisibleRenderInstance[2];

        RenderVisibilityQueryResult Initial=RenderVisibility.Select(
            First,View,[],FirstStates,Visible);
        Assert.Equal(FirstId,Assert.Single(Visible.AsSpan(0,Initial.VisibleCount).ToArray()).InstanceId);

        RenderVisibilityQueryResult Moved=RenderVisibility.Select(
            Second,View,FirstStates,SecondStates,Visible);
        Assert.Equal(SecondId,Assert.Single(Visible.AsSpan(0,Moved.VisibleCount).ToArray()).InstanceId);
    }

    [Theory]
    [InlineData(11f,0,0)]
    [InlineData(13f,0,1)]
    [InlineData(9f,1,1)]
    [InlineData(7f,1,0)]
    public void LodHysteresisRequiresCrossingTheExitOrEnterBand(
        float Distance,int PriorLod,int ExpectedLod){
        StableId InstanceId=Id("lod-instance");
        RenderWorldSnapshot Snapshot=CreateSnapshot(
            Instance(InstanceId,MaterialA,new Vector3(Distance,0f,0f),Radius:0f));
        RenderLodState[]Previous=[new(InstanceId,PriorLod)];
        var Next=new RenderLodState[1];
        var Visible=new VisibleRenderInstance[1];
        RenderVisibilityView View=new(
            RenderFrustum.CreateAxisAligned(new Vector3(-100f),new Vector3(100f)),0.2f);

        RenderVisibility.Select(Snapshot,View,Previous,Next,Visible);

        Assert.Equal(ExpectedLod,Next[0].LodIndex);
        Assert.Equal(ExpectedLod,Visible[0].LodIndex);
    }

    [Fact]
    public void BatchesAreGroupedByMeshMaterialAndLodWithStableInstanceOrder(){
        VisibleRenderInstance[]Visible=[
            new(2,Id("c"),1,FarMesh,MaterialA),
            new(0,Id("a"),0,NearMesh,MaterialA),
            new(3,Id("d"),0,NearMesh,MaterialB),
            new(1,Id("b"),0,NearMesh,MaterialA),
        ];
        int[]Order=new int[Visible.Length];
        RenderInstanceBatch[]Batches=new RenderInstanceBatch[Visible.Length];

        int Count=RenderVisibility.BuildBatches(Visible,Order,Batches);

        Assert.Equal(3,Count);
        RenderInstanceBatch First=Assert.Single(
            Batches.AsSpan(0,Count).ToArray(),
            static Batch=>Batch.Mesh==NearMesh&&Batch.Material==MaterialA&&Batch.LodIndex==0);
        Assert.Equal(2,First.InstanceCount);
        StableId[]FirstIds=[
            Visible[Order[First.OrderStart]].InstanceId,
            Visible[Order[First.OrderStart+1]].InstanceId,
        ];
        Assert.True(FirstIds[0].CompareTo(FirstIds[1])<0);
    }

    [Fact]
    public void CanonicallyOrderedBatchesKeepTheirCallerOrder(){
        VisibleRenderInstance[]Visible=[..
            new VisibleRenderInstance[]{
            new(0,Id("ordered-a"),0,NearMesh,MaterialA),
            new(1,Id("ordered-b"),0,NearMesh,MaterialA),
            new(2,Id("ordered-c"),0,NearMesh,MaterialB),
            new(3,Id("ordered-d"),1,FarMesh,MaterialA),
            }.OrderBy(static Instance=>Instance.Mesh)
                .ThenBy(static Instance=>Instance.Material)
                .ThenBy(static Instance=>Instance.LodIndex)
                .ThenBy(static Instance=>Instance.InstanceId)];
        int[]Order=new int[Visible.Length];
        RenderInstanceBatch[]Batches=new RenderInstanceBatch[Visible.Length];

        int BatchCount=RenderVisibility.BuildBatches(Visible,Order,Batches);

        Assert.Equal([0,1,2,3],Order);
        Assert.Equal(3,BatchCount);
        Assert.Contains(Batches.AsSpan(0,BatchCount).ToArray(),
            static Batch=>Batch.InstanceCount==2);
    }

    [Fact]
    public void VisibilityAndBatchingAllocateNothingAfterWarmupForOneHundredThousandInstances(){
        const int Count=100_000;
        RenderLodLevel[]Lods=[new(NearMesh,10_000f)];
        RenderInstanceRecord[]Instances=new RenderInstanceRecord[Count];
        for(var Index=0;Index<Instances.Length;Index++){
            Instances[Index]=new(
                StableId.FromName(NamespaceId,Index.ToString(CultureInfo.InvariantCulture)),
                MaterialA,Matrix4x4.Identity,
                new Vector3(Index%100,0f,Index/100),0.25f,0,1);
        }
        RenderWorldSnapshot Snapshot=RenderWorldSnapshot.Create(1,Instances,Lods);
        var States=new RenderLodState[Count];
        var NextStates=new RenderLodState[Count];
        var Visible=new VisibleRenderInstance[Count];
        var Order=new int[Count];
        var Batches=new RenderInstanceBatch[Count];
        RenderVisibilityView View=new(
            RenderFrustum.CreateAxisAligned(new Vector3(-1f),new Vector3(2000f)),0.1f);
        RenderVisibilityQueryResult Warm=RenderVisibility.Select(Snapshot,View,[],States,Visible);
        _=RenderVisibility.BuildBatches(Visible.AsSpan(0,Warm.VisibleCount),Order,Batches);

        long Before=GC.GetAllocatedBytesForCurrentThread();
        RenderVisibilityQueryResult Result=RenderVisibility.Select(Snapshot,View,States,NextStates,Visible);
        int BatchCount=RenderVisibility.BuildBatches(
            Visible.AsSpan(0,Result.VisibleCount),Order,Batches);
        long Allocated=GC.GetAllocatedBytesForCurrentThread()-Before;

        Assert.Equal(Count,Result.VisibleCount);
        Assert.Equal(1,BatchCount);
        Assert.Equal(0,Allocated);
    }

    [Fact]
    public void InvalidFrustumAndOverlappingStateBuffersAreRejectedBeforeSelection(){
        RenderWorldSnapshot Snapshot=CreateSnapshot(
            Instance(Id("validation"),MaterialA,Vector3.Zero));
        var States=new RenderLodState[1];
        var Visible=new VisibleRenderInstance[1];
        Assert.Throws<ArgumentException>(()=>RenderVisibility.Select(
            Snapshot,new RenderVisibilityView(default,0.1f),[],States,Visible));
        States[0]=new(Snapshot.Instances[0].InstanceId,0);
        RenderVisibilityView Valid=new(
            RenderFrustum.CreateAxisAligned(new Vector3(-1f),new Vector3(1f)),0.1f);
        Assert.Throws<ArgumentException>(()=>RenderVisibility.Select(
            Snapshot,Valid,States,States,Visible));
    }

    [Fact]
    public void SnapshotRejectsDuplicateIdentityAndInvalidLodRanges(){
        StableId Duplicate=Id("duplicate");
        Assert.Throws<ArgumentException>(()=>RenderWorldSnapshot.Create(
            0,
            [Instance(Duplicate,MaterialA,Vector3.Zero),Instance(Duplicate,MaterialB,Vector3.One)],
            [new RenderLodLevel(NearMesh,10f),new RenderLodLevel(FarMesh,1000f)]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderWorldSnapshot.Create(
            0,
            [Instance(Id("bad-range"),MaterialA,Vector3.Zero) with { LodStart=2 }],
            [new RenderLodLevel(NearMesh,10f),new RenderLodLevel(FarMesh,1000f)]));
    }

    private static RenderWorldSnapshot CreateSnapshot(params RenderInstanceRecord[]Instances)=>
        RenderWorldSnapshot.Create(
            1,Instances,[new RenderLodLevel(NearMesh,10f),new RenderLodLevel(FarMesh,1000f)]);

    private static RenderInstanceRecord Instance(
        StableId Id,RenderMaterialId Material,Vector3 Center,float Radius=1f)=>new(
            Id,Material,Matrix4x4.CreateTranslation(Center),Center,Radius,0,2);

    private static StableId Id(string Name)=>StableId.FromName(NamespaceId,Name);
}
