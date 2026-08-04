namespace CSharpGameEngine.Ecs.Tests;

public sealed class EntityAllocatorSpec
{
    [Fact]
    public void Allocate_ReturnsUniqueIncrementingIds()
    {
        EntityAllocator allocator = new();

        Entity first = allocator.Allocate();
        Entity second = allocator.Allocate();

        Assert.NotEqual(first, second);
        Assert.Equal(0, first.Id);
        Assert.Equal(1, second.Id);
    }

    [Fact]
    public void IsAlive_TrueAfterAllocate_FalseAfterDestroy()
    {
        EntityAllocator allocator = new();
        Entity entity = allocator.Allocate();

        Assert.True(allocator.IsAlive(entity));

        allocator.Destroy(entity);

        Assert.False(allocator.IsAlive(entity));
    }

    [Fact]
    public void Validate_OnDestroyedEntity_Throws()
    {
        EntityAllocator allocator = new();
        Entity entity = allocator.Allocate();
        allocator.Destroy(entity);

        Assert.Throws<InvalidOperationException>(() => allocator.Validate(entity));
    }

    [Fact]
    public void Destroy_ThenAllocate_ReusesIdWithIncrementedGeneration()
    {
        EntityAllocator allocator = new();
        Entity first = allocator.Allocate();

        allocator.Destroy(first);
        Entity second = allocator.Allocate();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Generation + 1, second.Generation);
    }

    [Fact]
    public void IsAlive_StaleHandleAfterReuse_ReturnsFalse()
    {
        EntityAllocator allocator = new();
        Entity first = allocator.Allocate();

        allocator.Destroy(first);
        allocator.Allocate();

        Assert.False(allocator.IsAlive(first));
    }
}
