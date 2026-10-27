namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetViewportResizeQueueTests {
    [Fact]
    public void SizeNotificationsAreCoalescedUntilTheRenderTick(){
        var Queue=new AssetViewportResizeQueue();
        Queue.Request(800,600);
        Queue.Request(1200,700);
        var Calls=0;
        Assert.True(Queue.Apply((Width,Height)=>{
            Calls++;
            Assert.Equal(1200,Width);
            Assert.Equal(700,Height);
        }));
        Assert.False(Queue.Apply((_,_)=>Calls++));
        Assert.Equal(1,Calls);
    }
    [Theory]
    [InlineData(0,0)]
    [InlineData(0,600)]
    [InlineData(800,0)]
    public void AHiddenSurfaceUsesTheSuspendedSize(int Width,int Height){
        var Queue=new AssetViewportResizeQueue();
        Queue.Request(Width,Height);
        Queue.Apply((W,H)=>{ Assert.Equal(0,W);Assert.Equal(0,H); });
    }
    [Fact]
    public void AHostFailureIsRaisedOnApplyAndIsNotRetriedOnTheFaultedClient(){
        var Queue=new AssetViewportResizeQueue();
        var Failure=new RenderPreviewHostException("host stopped",RequiresFreshSurfaceGeneration:true);
        Queue.Request(800,600);
        Assert.Same(Failure,Assert.Throws<RenderPreviewHostException>(()=>Queue.Apply((_,_)=>throw Failure)));
        Assert.False(Queue.Apply((_,_)=>throw Failure));
        Queue.Request(1024,768);
        Queue.Clear();
        Assert.False(Queue.Apply((_,_)=>throw Failure));
    }
}
