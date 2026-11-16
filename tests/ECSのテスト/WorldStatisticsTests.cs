namespace CSharpGameEngine.Entities.Tests;
public sealed class WorldStatisticsTests {
    [Fact]
    public void GetStatistics_ReportsAliveSlotsAndDeferredCommands(){
        World World=new();
        State State=new(World){ Enabled=true };
        var First=World.Create(new Position());
        var Second=World.Create(new Position());
        World.Destroy(First);
        World.DestroyDeferred(Second,ref State);
        var Statistics=World.GetStatistics();
        Assert.Equal(1,Statistics.AliveEntityCount);
        Assert.Equal(2,Statistics.EntitySlotCount);
        Assert.Equal(1,Statistics.PendingDestroyCount);
        Assert.Equal(0,Statistics.PendingSceneUnloadCount);
        Assert.Equal(0,Statistics.PendingCellUnloadCount);
    }
    [Fact]
    public void GetStatistics_RemainsBoundedAcrossSlotReuse(){
        World World=new();
        for(var Index=0;Index<10000;Index++){
            var Entity=World.Create(new Position());
            World.Destroy(Entity);
        }
        var Current=World.Create(new Position());
        var Statistics=World.GetStatistics();
        Assert.True(World.IsAlive(Current));
        Assert.Equal(1,Statistics.AliveEntityCount);
        Assert.Equal(1,Statistics.EntitySlotCount);
    }
}
