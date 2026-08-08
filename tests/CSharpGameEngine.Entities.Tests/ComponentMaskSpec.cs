namespace CSharpGameEngine.Entities.Tests;

public class ComponentMaskSpec
{
    [Fact]
    public void ContainsAll_TrueWhenSupersetOfRequired()
    {
        ComponentMask full = ComponentMask.FromSortedIds([1, 5, 130]);
        ComponentMask required = ComponentMask.FromSortedIds([5]);

        Assert.True(full.ContainsAll(required));
    }

    [Fact]
    public void ContainsAll_FalseWhenMissingOne()
    {
        ComponentMask full = ComponentMask.FromSortedIds([1, 5]);
        ComponentMask required = ComponentMask.FromSortedIds([1, 5, 130]);

        Assert.False(full.ContainsAll(required));
    }

    [Fact]
    public void ContainsAll_TrueWhenRequiredIsEmpty()
    {
        ComponentMask full = ComponentMask.FromSortedIds([1]);
        ComponentMask empty = ComponentMask.FromSortedIds([]);

        Assert.True(full.ContainsAll(empty));
    }

    [Fact]
    public void OverlapsAny_TrueWhenSharingABit()
    {
        ComponentMask a = ComponentMask.FromSortedIds([1, 5]);
        ComponentMask b = ComponentMask.FromSortedIds([5, 200]);

        Assert.True(a.OverlapsAny(b));
    }

    [Fact]
    public void OverlapsAny_FalseWhenDisjoint()
    {
        ComponentMask a = ComponentMask.FromSortedIds([1, 5]);
        ComponentMask b = ComponentMask.FromSortedIds([6, 200]);

        Assert.False(a.OverlapsAny(b));
    }

    [Fact]
    public void OverlapsAny_FalseWhenOtherIsEmpty()
    {
        ComponentMask a = ComponentMask.FromSortedIds([1, 5]);
        ComponentMask empty = ComponentMask.FromSortedIds([]);

        Assert.False(a.OverlapsAny(empty));
    }

    [Fact]
    public void Bits_AtWordBoundaries_AreAllRepresented()
    {
        ComponentMask mask = ComponentMask.FromSortedIds([0, 63, 64, 127, 128, 191, 192, 255]);
        ComponentMask required = ComponentMask.FromSortedIds([192, 255]);

        Assert.True(mask.ContainsAll(required));
    }
}
