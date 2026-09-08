using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
namespace CSharpGameEngine.Contracts.Tests;
public sealed class WorldIdentityTests {
    [Fact]
    public void StableIdNameDerivationIsDeterministicAndRfcVariant(){
        var NamespaceId=StableId.Parse("88a446a1-9974-4efd-b37e-cf44e33718a4");
        var First=StableId.FromName(NamespaceId,"world/cell/earth/0");
        var Second=StableId.FromName(NamespaceId,"world/cell/earth/0");
        Assert.Equal(First,Second);
        Assert.NotEqual(First,StableId.FromName(NamespaceId,"world/cell/earth/1"));
        Assert.Equal(First,StableId.Parse(First.ToString()));
        Assert.True(First.IsValid);
    }
    [Fact]
    public void TypedWorldIdentitiesRoundTripWithoutRuntimeEntityMeaning(){
        var Value=StableId.Parse("79ca40f0-44b9-54e5-977f-fbcab7dfe118");
        var Frame=new ReferenceFrameId(Value);
        var Cell=new WorldCellId(Value);
        var PersistentEntity=new PersistentEntityId(Value);
        var Asset=new AssetId(Value);
        Assert.True(Frame.IsValid);
        Assert.Equal(Value,Frame.Value);
        Assert.Equal(Value,Cell.Value);
        Assert.Equal(Value,PersistentEntity.Value);
        Assert.Equal(Value,Asset.Value);
        Assert.Equal(Frame,ReferenceFrameId.Parse(Frame.ToString()));
        Assert.Equal(Cell,WorldCellId.Parse(Cell.ToString()));
        Assert.Equal(PersistentEntity,PersistentEntityId.Parse(PersistentEntity.ToString()));
        Assert.Equal(Asset,AssetId.Parse(Asset.ToString()));
    }
    [Fact]
    public void DefaultWorldIdentitiesAreInvalid(){
        Assert.False(default(ReferenceFrameId).IsValid);
        Assert.False(default(WorldCellId).IsValid);
        Assert.False(default(PersistentEntityId).IsValid);
        Assert.False(default(AssetId).IsValid);
    }
}
