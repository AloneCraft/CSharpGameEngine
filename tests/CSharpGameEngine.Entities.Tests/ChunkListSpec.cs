namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// <c>ChunkList&lt;T&gt;</c>は生成されたArchetypeリストの土台であり、特に
/// <see cref="ChunkList{T}.RemoveAtSwapBack"/>の「詰め替えが起きたかどうか」の戻り値に、生成される
/// <c>World.Detach</c>の位置表修復が依存している。
/// </summary>
public class ChunkListSpec
{
    [Fact]
    public void Add_GrowsAcrossChunkBoundaries_AndPreservesOrder()
    {
        ChunkList<int> list = new();

        // 既定のチャンクサイズ(4096)を跨いで伸長させる。
        for (int i = 0; i < 5000; i++)
        {
            list.Add(i);
        }

        Assert.Equal(5000, list.Count);
        Assert.Equal(0, list[0]);
        Assert.Equal(4095, list[4095]);
        Assert.Equal(4096, list[4096]);
        Assert.Equal(4999, list[4999]);
    }

    [Fact]
    public void Indexer_ReturnsByReference_SoTheStoredElementIsMutatedInPlace()
    {
        ChunkList<Position> list = new();
        list.Add(new Position { X = 1f });

        ref Position slot = ref list[0];
        slot.X = 42f;

        Assert.Equal(42f, list[0].X);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        ChunkList<int> list = new();
        list.Add(7);

        Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[-1]);
    }

    [Fact]
    public void RemoveAtSwapBack_MovesTheLastElement_AndReportsThatItMoved()
    {
        ChunkList<int> list = new();
        list.Add(10);
        list.Add(20);
        list.Add(30);

        bool moved = list.RemoveAtSwapBack(0, out int movedValue);

        Assert.True(moved);
        Assert.Equal(30, movedValue);
        Assert.Equal(2, list.Count);
        Assert.Equal(30, list[0]);
        Assert.Equal(20, list[1]);
    }

    [Fact]
    public void RemoveAtSwapBack_OnTheLastElement_ReportsThatNothingMoved()
    {
        ChunkList<int> list = new();
        list.Add(10);
        list.Add(20);

        bool moved = list.RemoveAtSwapBack(1, out _);

        Assert.False(moved);
        Assert.Equal(1, list.Count);
        Assert.Equal(10, list[0]);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        ChunkList<int> list = new();
        list.Add(1);
        list.Add(2);

        list.Clear();

        Assert.Equal(0, list.Count);
    }
}
