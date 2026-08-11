namespace CSharpGameEngine.Entities.Tests;

public class ChunkListSpec
{
    [Fact]
    public void EnsureCapacity_GrowsAcrossChunkBoundaries_AndPreservesOrder()
    {
        ChunkList<int> list = new();
        for (int i = 0; i < 5000; i++)
        {
            list.EnsureCapacity(i + 1);
            list.GetRefUnchecked(i) = i;
        }

        Assert.Equal(0, list.GetRefUnchecked(0));
        Assert.Equal(4095, list.GetRefUnchecked(4095));
        Assert.Equal(4096, list.GetRefUnchecked(4096));
        Assert.Equal(4999, list.GetRefUnchecked(4999));
    }

    [Fact]
    public void GetRefUnchecked_ReturnsByReference_SoTheStoredElementIsMutatedInPlace()
    {
        ChunkList<Position> list = new();
        list.EnsureCapacity(1);
        list.GetRefUnchecked(0) = new Position { X = 1f };
        ref Position Slot = ref list.GetRefUnchecked(0);
        Slot.X = 42f;
        Assert.Equal(42f, list.GetRefUnchecked(0).X);
    }

    [Fact]
    public void ChunkBuffer_OwnsLogicalCount_AndClearsIt()
    {
        ChunkBuffer<int> buffer = new();
        buffer.Add(7);
        buffer.Add(9);
        Assert.Equal(2, buffer.Count);
        Assert.Equal(9, buffer.GetRefUnchecked(1));
        buffer.Clear();
        Assert.Equal(0, buffer.Count);
    }
}
