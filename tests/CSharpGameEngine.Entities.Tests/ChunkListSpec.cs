namespace CSharpGameEngine.Entities.Tests;

public class ChunkListSpec {
    [Fact]
    public void EnsureCapacity_GrowsAcrossChunkBoundaries_AndPreservesOrder(){
        ChunkList<int>List=new();
        for(var I=0;I<5000;I++){
            List.EnsureCapacity(I+1);
            List.GetRefUnchecked(I)=I;
        }

        Assert.Equal(0,List.GetRefUnchecked(0));
        Assert.Equal(4095,List.GetRefUnchecked(4095));
        Assert.Equal(4096,List.GetRefUnchecked(4096));
        Assert.Equal(4999,List.GetRefUnchecked(4999));
    }

    [Fact]
    public void GetRefUnchecked_ReturnsByReference_SoTheStoredElementIsMutatedInPlace(){
        ChunkList<Position>List=new();
        List.EnsureCapacity(1);
        List.GetRefUnchecked(0)=new Position { X=1f };
        ref Position Slot=ref List.GetRefUnchecked(0);
        Slot.X=42f;
        Assert.Equal(42f,List.GetRefUnchecked(0).X);
    }

    [Fact]
    public void ChunkBuffer_OwnsLogicalCount_AndClearsIt(){
        ChunkBuffer<int>Buffer=new();
        Buffer.Add(7);
        Buffer.Add(9);
        Assert.Equal(2,Buffer.Count);
        Assert.Equal(9,Buffer.GetRefUnchecked(1));
        Buffer.Clear();
        Assert.Equal(0,Buffer.Count);
    }
}
