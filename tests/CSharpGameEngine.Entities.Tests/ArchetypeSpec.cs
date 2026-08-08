namespace CSharpGameEngine.Entities.Tests;

public class ArchetypeSpec
{
    private static Archetype CreatePositionVelocityArchetype()
    {
        int[] ids = [ComponentType<Position>.Id, ComponentType<Velocity>.Id];
        Array.Sort(ids);

        IComponentColumn[] columns = new IComponentColumn[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            columns[i] = ComponentTypeRegistry.CreateColumn(ids[i]);
        }

        return new Archetype(0, ids, columns);
    }

    [Fact]
    public void AddRow_GrowsAndIncrementsCount()
    {
        Archetype archetype = CreatePositionVelocityArchetype();

        for (int i = 0; i < 10; i++)
        {
            archetype.AddRow(new Entity(i, 0));
        }

        Assert.Equal(10, archetype.Count);
    }

    [Fact]
    public void RemoveRowSwapBack_MovesLastRowIntoRemovedSlot()
    {
        Archetype archetype = CreatePositionVelocityArchetype();
        Entity a = new(0, 0);
        Entity b = new(1, 0);
        Entity c = new(2, 0);
        archetype.AddRow(a);
        archetype.AddRow(b);
        archetype.AddRow(c);

        bool moved = archetype.RemoveRowSwapBack(0, out Entity movedEntity);

        Assert.True(moved);
        Assert.Equal(c, movedEntity);
        Assert.Equal(2, archetype.Count);
        Assert.Equal(c, archetype.GetEntity(0));
    }

    [Fact]
    public void RemoveRowSwapBack_RemovingLastRow_DoesNotReportMove()
    {
        Archetype archetype = CreatePositionVelocityArchetype();
        archetype.AddRow(new Entity(0, 0));

        bool moved = archetype.RemoveRowSwapBack(0, out Entity movedEntity);

        Assert.False(moved);
        Assert.Equal(default, movedEntity);
        Assert.Equal(0, archetype.Count);
    }

    [Fact]
    public void GetSpan_WriteThenReadOnlySpan_ReflectsWrittenValue()
    {
        Archetype archetype = CreatePositionVelocityArchetype();
        int positionColumn = archetype.IndexOfColumn(ComponentType<Position>.Id);
        archetype.AddRow(new Entity(0, 0));

        archetype.GetSpan<Position>(positionColumn)[0] = new Position { X = 1f, Y = 2f, Z = 3f };

        Assert.Equal(1f, archetype.GetReadOnlySpan<Position>(positionColumn)[0].X);
    }

    [Fact]
    public void IndexOfColumn_ReturnsNegativeForMissingComponent()
    {
        Archetype archetype = CreatePositionVelocityArchetype();

        Assert.True(archetype.IndexOfColumn(ComponentType<Health>.Id) < 0);
    }

    [Fact]
    public void IndexOfColumn_ReturnsNonNegativeForPresentComponent()
    {
        Archetype archetype = CreatePositionVelocityArchetype();

        Assert.True(archetype.IndexOfColumn(ComponentType<Position>.Id) >= 0);
        Assert.True(archetype.IndexOfColumn(ComponentType<Velocity>.Id) >= 0);
    }
}
