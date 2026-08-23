using CSharpGameEngine.Entities;
using Logical=CSharpGameEngine.Numerics.Logical256;
namespace CSharpGameEngine.Runtime.Windows.Tests;
public sealed class SpatialPartitioningTests {
    [Fact]
    public void Queries_FilterByLayerAndReturnEachEntityOnce(){
        EntityLooseOctree Index=new();
        Assert.True(SpatialAabb.TryCreate(new Logical.Vector3(0,0,0),new Logical.Vector3(1,1,1),out SpatialAabb FirstBounds));
        Assert.True(SpatialAabb.TryCreate(new Logical.Vector3(4,0,0),new Logical.Vector3(1,1,1),out SpatialAabb SecondBounds));
        Assert.Equal(SpatialUpdateStatus.Success,Index.Upsert(new Entity(1),FirstBounds,new SpatialLayer(0)));
        Assert.Equal(SpatialUpdateStatus.Success,Index.Upsert(new Entity(2),SecondBounds,new SpatialLayer(1)));
        List<Entity> Results=[];
        SpatialQueryResult Query=Index.QueryAabb(FirstBounds,SpatialLayerMask.FromLayer(0),Results);
        Assert.Equal(SpatialQueryStatus.Success,Query.Status);
        Assert.Equal(1,Query.AddedCount);
        Assert.Equal([new Entity(1)],Results);
    }
    [Fact]
    public void SphereAndRayQueries_IncludeBoundaryContact(){
        EntityLooseOctree Index=new();
        Assert.True(SpatialAabb.TryCreate(new Logical.Vector3(2,0,0),new Logical.Vector3(1,1,1),out SpatialAabb Bounds));
        Assert.Equal(SpatialUpdateStatus.Success,Index.Upsert(new Entity(9),Bounds,new SpatialLayer(0)));
        Assert.True(SpatialSphere.TryCreate(new Logical.Vector3(0,0,0),1,out SpatialSphere Sphere));
        Assert.True(SpatialRay.TryCreate(new Logical.Vector3(0,0,0),Logical.Vector3.UnitR,10,out SpatialRay Ray));
        List<Entity> SphereResults=[];
        List<SpatialRayHit> RayResults=[];
        Assert.Equal(SpatialQueryStatus.Success,Index.QuerySphere(Sphere,SpatialLayerMask.FromLayer(0),SphereResults).Status);
        Assert.Equal([new Entity(9)],SphereResults);
        Assert.Equal(SpatialQueryStatus.Success,Index.QueryRay(Ray,SpatialLayerMask.FromLayer(0),RayResults).Status);
        Assert.Single(RayResults);
        Assert.Equal(new Entity(9),RayResults[0].Entity);
        Assert.Equal(1d,(double)RayResults[0].Distance);
    }
    [Fact]
    public void DebugEnumeration_ContainsRootAndNonEmptyNodesWithoutDuplicates(){
        EntityLooseOctree Index=new(new SpatialIndexSettings(1,4,Logical.Scalar.FromDouble(1.5),Logical.Scalar.One));
        Assert.True(SpatialAabb.TryCreate(new Logical.Vector3(-8,0,0),new Logical.Vector3(0.25,0.25,0.25),out SpatialAabb Left));
        Assert.True(SpatialAabb.TryCreate(new Logical.Vector3(8,0,0),new Logical.Vector3(0.25,0.25,0.25),out SpatialAabb Right));
        Assert.Equal(SpatialUpdateStatus.Success,Index.Upsert(new Entity(1),Left,new SpatialLayer(0)));
        Assert.Equal(SpatialUpdateStatus.Success,Index.Upsert(new Entity(2),Right,new SpatialLayer(0)));
        List<SpatialDebugNode> Nodes=[];
        int Count=Index.CollectDebugNodes(Nodes);
        Assert.Equal(Nodes.Count,Count);
        Assert.Contains(Nodes,Node=>Node.IsRoot&&Node.Depth==0);
        Assert.All(Nodes.Where(Node=>!Node.IsRoot),Node=>Assert.True(Node.EntryCount>0));
        Assert.Equal(Nodes.Count,Nodes.Select(Node=>(Node.Depth,Node.Bounds.Min.R.ToDouble(),Node.Bounds.Min.U.ToDouble(),Node.Bounds.Min.F.ToDouble())).Distinct().Count());
    }
    [Fact]
    public void UpsertOutsideRootAndCompact_PreservesQueryResults(){
        EntityLooseOctree Index=new();
        Assert.True(SpatialAabb.TryCreate(new Logical.Vector3(0,0,0),new Logical.Vector3(1,1,1),out SpatialAabb Near));
        Assert.True(SpatialAabb.TryCreate(new Logical.Vector3(4096,0,0),new Logical.Vector3(1,1,1),out SpatialAabb Far));
        Assert.Equal(SpatialUpdateStatus.Success,Index.Upsert(new Entity(1),Near,new SpatialLayer(0)));
        Assert.Equal(SpatialUpdateStatus.Success,Index.Upsert(new Entity(2),Far,new SpatialLayer(0)));
        Assert.True(Index.Remove(new Entity(1)));
        Index.Compact();
        List<Entity> Results=[];
        Assert.Equal(SpatialQueryStatus.Success,Index.QueryPoint(new Logical.Vector3(4096,0,0),SpatialLayerMask.FromLayer(0),Results).Status);
        Assert.Equal([new Entity(2)],Results);
    }
}
