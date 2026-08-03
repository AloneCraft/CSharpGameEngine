namespace CSharpGameEngine.Ecs.Tests;

public sealed class ComponentColumnSpec
{
    [Fact]
    public void EnsureCapacity_GrowsWithoutLosingExistingData()
    {
        ComponentColumn<FakeHealth> column = new();
        column.EnsureCapacity(1);
        column.Items[0] = new FakeHealth { Value = 42 };

        column.EnsureCapacity(64);

        Assert.True(column.Items.Length >= 64);
        Assert.Equal(42, column.Items[0].Value);
    }

    [Fact]
    public void EnsureCapacity_SmallerThanCurrent_DoesNotShrink()
    {
        ComponentColumn<FakeHealth> column = new();
        column.EnsureCapacity(16);
        int lengthAfterFirstGrowth = column.Items.Length;

        column.EnsureCapacity(1);

        Assert.Equal(lengthAfterFirstGrowth, column.Items.Length);
    }

    [Fact]
    public void MoveRow_CopiesValueToDestination()
    {
        ComponentColumn<FakeHealth> column = new();
        column.EnsureCapacity(2);
        column.Items[0] = new FakeHealth { Value = 7 };

        column.MoveRow(0, 1);

        Assert.Equal(7, column.Items[1].Value);
    }
}
