namespace CSharpGameEngine.Entities.Tests;

public sealed class ChangeTrackingSpec {
    [Fact]
    public void EnableBitsAndVersionsFilterOnlyMatchingLiveRows(){
        World World=new();
        var BeforeCreate=World.CaptureChangeVersion();
        var PositionOnly=World.Create(new Position { X=1f });
        var Moving=World.Create(new Position { X=2f },new Velocity { X=3f });
        var AfterCreate=World.CaptureChangeVersion();
        Span<Entity>Matches=stackalloc Entity[4];

        var CreatedCount=World.CollectChanged(EcsComponentType.Position,BeforeCreate,Matches);

        Assert.Equal(2,CreatedCount);
        Assert.Contains(PositionOnly,Matches[..CreatedCount].ToArray());
        Assert.Contains(Moving,Matches[..CreatedCount].ToArray());
        Assert.Equal(0,World.CollectChanged(EcsComponentType.Position,AfterCreate,Matches));

        Assert.True(World.SetEntityEnabled(PositionOnly,false));
        Assert.False(World.IsEntityEnabled(PositionOnly));
        Assert.Equal(1,World.CollectChanged(EcsComponentType.Position,BeforeCreate,Matches));
        Assert.Equal(Moving,Matches[0]);

        Assert.True(World.SetComponentEnabled(Moving,EcsComponentType.Position,false));
        Assert.False(World.IsComponentEnabled(Moving,EcsComponentType.Position));
        Assert.Equal(0,World.CollectChanged(EcsComponentType.Position,BeforeCreate,Matches));

        var BeforeEnable=World.CaptureChangeVersion();
        Assert.True(World.SetComponentEnabled(Moving,EcsComponentType.Position,true));
        Assert.True(World.TryGetComponentChangeVersion(Moving,EcsComponentType.Position,out var EnabledVersion));
        Assert.True(EnabledVersion.IsNewerThan(BeforeEnable));
        Assert.Equal(1,World.CollectChanged(EcsComponentType.Position,BeforeEnable,Matches));
        Assert.Equal(Moving,Matches[0]);

        var BeforeIdempotentSet=World.CaptureChangeVersion();
        Assert.True(World.SetComponentEnabled(Moving,EcsComponentType.Position,true));
        Assert.Equal(BeforeIdempotentSet,World.CaptureChangeVersion());
        Assert.False(World.SetComponentEnabled(PositionOnly,EcsComponentType.Velocity,false));
    }

    [Fact]
    public void ChangedQueryHonorsCapacityAndAllocatesNothingAfterWarmup(){
        World World=new();
        var Since=World.CaptureChangeVersion();
        for(var Index=0;Index<32;Index++){ World.Create(new Position { X=Index }); }
        Span<Entity>Matches=stackalloc Entity[7];
        _=World.CollectChanged(EcsComponentType.Position,Since,Matches);
        var Before=GC.GetAllocatedBytesForCurrentThread();

        var Count=World.CollectChanged(EcsComponentType.Position,Since,Matches);
        var After=GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(7,Count);
        Assert.Equal(Before,After);
    }

    [Fact]
    public void ArchetypeMovePreservesExistingEnableBitsAndEnablesOnlyNewComponents(){
        World World=new();
        State State=new(World);
        var Entity=World.Create(new Position());
        Assert.True(World.SetEntityEnabled(Entity,false));
        Assert.True(World.SetComponentEnabled(Entity,EcsComponentType.Position,false));

        Assert.True(World.MoveArchetype(Entity,new Position(),new Velocity(),ref State));
        World.PlaybackStructuralChanges(ref State);

        Assert.False(World.IsEntityEnabled(Entity));
        Assert.False(World.IsComponentEnabled(Entity,EcsComponentType.Position));
        Assert.True(World.IsComponentEnabled(Entity,EcsComponentType.Velocity));
    }

    [Fact]
    public void ChangeVersionComparisonHandlesWrapAndRejectsAmbiguousHistory(){
        Assert.True(new ChangeVersion(1).IsNewerThan(new ChangeVersion(uint.MaxValue)));
        Assert.True(new ChangeVersion(uint.MaxValue).IsNewerThan(new ChangeVersion(uint.MaxValue-1)));
        Assert.False(new ChangeVersion(4).IsNewerThan(new ChangeVersion(4)));
        Assert.False(new ChangeVersion(0x80000001u).IsNewerThan(new ChangeVersion(1)));
    }
}
