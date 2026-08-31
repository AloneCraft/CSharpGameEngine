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
    }
}
