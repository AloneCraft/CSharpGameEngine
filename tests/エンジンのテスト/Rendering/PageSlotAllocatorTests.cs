using Xunit;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>空き枠の割り当てと報告（M0129 単位 6）。予算と置き場の数を超えない、固定したものと今使うものは追い出さない、同じ入力なら同じ結果。</summary>
public sealed class PageSlotAllocatorTests {
    [Fact]
    public void NewPagesUseTheLowestFreeSlotsInRequestOrderWithinTheBudget(){
        var Allocator=new PageSlotAllocator(4);
        var Report=new StreamingReport();
        List<PageAllocation>Allocations=[];
        Allocator.Update(1,[30,10,20],NewPageBudget:2,Allocations,Report);
        Assert.Equal([new PageAllocation(30,0,null),new PageAllocation(10,1,null)],Allocations);
        Assert.Equal((3,0,2,0,1,0),(Report.Requested,Report.AlreadyResident,Report.Allocated,Report.Evicted,Report.DeniedByBudget,Report.DeniedFull));
        Assert.False(Allocator.TryGetSlot(20,out _));
        // 断った 20 は、次のフレームでまた要求すれば入る。置いている 30 と 10 は数えるだけ。
        Allocations.Clear();
        Report.Clear();
        Allocator.Update(2,[30,10,20],NewPageBudget:2,Allocations,Report);
        Assert.Equal([new PageAllocation(20,2,null)],Allocations);
        Assert.Equal((3,2,1),(Report.Requested,Report.AlreadyResident,Report.Allocated));
    }
    [Fact]
    public void WhenFullTheOldestUnpinnedPageNotUsedThisFrameIsEvicted(){
        var Allocator=new PageSlotAllocator(3);
        List<PageAllocation>Allocations=[];
        var Report=new StreamingReport();
        Allocator.Update(1,[1,2,3],8,Allocations,Report);
        Allocator.Update(2,[2],8,Allocations,Report);
        Allocator.Update(3,[3],8,Allocations,Report);
        Allocations.Clear();
        Report.Clear();
        // 1 はフレーム 1 から使っていないので最も古い。
        Allocator.Update(4,[4],8,Allocations,Report);
        Assert.Equal([new PageAllocation(4,0,1)],Allocations);
        Assert.Equal(1,Report.Evicted);
        // 古さが同じなら、キーの小さいほうを追い出す。
        var Tie=new PageSlotAllocator(2);
        Allocations.Clear();
        Tie.Update(1,[9,5],8,Allocations,Report);
        Allocations.Clear();
        Tie.Update(2,[7],8,Allocations,Report);
        Assert.Equal([new PageAllocation(7,1,5)],Allocations);
    }
    [Fact]
    public void PinnedPagesAndPagesRequestedThisFrameAreNeverEvicted(){
        var Allocator=new PageSlotAllocator(2);
        List<PageAllocation>Allocations=[];
        var Report=new StreamingReport();
        Allocator.Update(1,[1,2],8,Allocations,Report);
        Allocator.Pin(1);
        Allocations.Clear();
        Report.Clear();
        // 2 はこのフレームで使うので追い出せない。1 は固定。置き場は満杯なので 3 は断る。
        Allocator.Update(2,[2,3],8,Allocations,Report);
        Assert.Empty(Allocations);
        Assert.Equal((1,1),(Report.AlreadyResident,Report.DeniedFull));
        Assert.True(Allocator.TryGetSlot(1,out _));
        Assert.True(Allocator.TryGetSlot(2,out _));
        Assert.Throws<InvalidOperationException>(()=>Allocator.Release(1));
        // 固定を外せば、古い 1 が追い出される。
        Allocator.Unpin(1);
        Allocations.Clear();
        Allocator.Update(3,[3],8,Allocations,Report);
        Assert.Equal([new PageAllocation(3,0,1)],Allocations);
        Assert.Throws<InvalidOperationException>(()=>Allocator.Pin(99));
    }
    [Fact]
    public void ReleasedSlotsAreReusedFromTheLowestNumber(){
        var Allocator=new PageSlotAllocator(3);
        List<PageAllocation>Allocations=[];
        var Report=new StreamingReport();
        Allocator.Update(1,[1,2,3],8,Allocations,Report);
        Assert.True(Allocator.Release(2));
        Assert.True(Allocator.Release(1));
        Assert.False(Allocator.Release(1));
        Assert.Equal(1,Allocator.ResidentCount);
        Allocations.Clear();
        Allocator.Update(2,[8,9],8,Allocations,Report);
        Assert.Equal([new PageAllocation(8,0,null),new PageAllocation(9,1,null)],Allocations);
    }
    /// <summary>決まった乱数で長い列を 2 回流し、結果が同じで、どのフレームでも予算と置き場の数を超えないことを確かめる。</summary>
    [Fact]
    public void ALongRandomSequenceIsDeterministicAndNeverExceedsTheBudgetOrTheCapacity(){
        var First=Run(out var FirstResident);
        var Second=Run(out var SecondResident);
        Assert.Equal(First,Second);
        Assert.Equal(FirstResident,SecondResident);
        static List<PageAllocation>Run(out int Resident){
            var Random=new Random(129);
            var Allocator=new PageSlotAllocator(64);
            List<PageAllocation>All=[];
            List<PageAllocation>Frame=[];
            var Report=new StreamingReport();
            Allocator.Update(0,[1000],1,Frame,Report);
            Allocator.Pin(1000);
            for(ulong Index=1;Index<=300;Index++){
                var Requests=new ulong[Random.Next(1,40)];
                for(var Request=0;Request<Requests.Length;Request++){ Requests[Request]=(ulong)Random.Next(0,200); }
                var Budget=Random.Next(0,9);
                Frame.Clear();
                Report.Clear();
                Allocator.Update(Index,Requests,Budget,Frame,Report);
                Assert.True(Frame.Count<=Budget);
                Assert.True(Allocator.ResidentCount<=Allocator.Capacity);
                Assert.True(Allocator.TryGetSlot(1000,out _));
                Assert.Equal(Requests.Length,Report.Requested);
                Assert.Equal(Report.Requested,Report.AlreadyResident+Report.Allocated+Report.DeniedByBudget+Report.DeniedFull);
                Assert.DoesNotContain(Frame,Allocation=>Allocation.EvictedKey==1000);
                All.AddRange(Frame);
            }
            Resident=Allocator.ResidentCount;
            return All;
        }
    }
}
