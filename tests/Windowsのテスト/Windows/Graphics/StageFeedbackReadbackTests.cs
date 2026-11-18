using Xunit;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 読み戻しの輪（M0129 単位 5、ADR-0100 決定 1）。フレームの枠ごとの置き場へ GPU が写した値を、フェンスが済んでから読める。写していない枠は読めない。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed unsafe class StageFeedbackReadbackTests {
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_EachFrameSlotReturnsWhatTheGpuCopiedIntoIt(){
        using var Device=GraphicsDevice.Create(new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true });
        using var Ring=new StageFeedbackReadback(Device,FrameCount:3,SlotBytes:16,"test");
        var Native=Device.Device;
        ID3D12Resource*Source=null;
        ID3D12CommandAllocator*Allocator=null;
        ID3D12GraphicsCommandList*List=null;
        ID3D12Fence*Fence=null;
        var Event=CreateEventW(null,FALSE,FALSE,null);
        try {
            D3D12_HEAP_PROPERTIES Upload=new(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD);
            var Desc=D3D12_RESOURCE_DESC.Buffer(3*16);
            D3D12Diagnostics.EnsureSuccess(Native->CreateCommittedResource(&Upload,D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Source),"create","source");
            uint*Mapped;
            D3D12Diagnostics.EnsureSuccess(Source->Map(0,null,(void**)&Mapped),"map","source");
            for(uint Index=0;Index<12;Index++){ Mapped[Index]=1000+Index; }
            Source->Unmap(0,null);
            D3D12Diagnostics.EnsureSuccess(Native->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),"create","allocator");
            D3D12Diagnostics.EnsureSuccess(Native->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&List),"create","list");
            // 枠 0 と 2 にだけ写す。枠 1 は何も写さない。枠 2 へは、元の 3 つ目の 16 バイトを写す。
            List->CopyBufferRegion(Ring.Resource,Ring.SlotOffset(0),Source,0,16);
            Ring.MarkWritten(0);
            List->CopyBufferRegion(Ring.Resource,Ring.SlotOffset(2),Source,32,16);
            Ring.MarkWritten(2);
            D3D12Diagnostics.EnsureSuccess(List->Close(),"close","list");
            var Base=(ID3D12CommandList*)List;
            Device.CommandQueue->ExecuteCommandLists(1,&Base);
            D3D12Diagnostics.EnsureSuccess(Native->CreateFence(0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),"create","fence");
            D3D12Diagnostics.EnsureSuccess(Device.CommandQueue->Signal(Fence,1),"signal","fence");
            D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(1,Event),"wait","fence");
            _=WaitForSingleObjectEx(Event,INFINITE,FALSE);
            Span<uint>Values=stackalloc uint[4];
            Assert.True(Ring.TryRead(0,Values));
            Assert.Equal([1000u,1001u,1002u,1003u],Values.ToArray());
            Assert.False(Ring.TryRead(1,Values));
            Assert.True(Ring.TryRead(2,Values));
            Assert.Equal([1008u,1009u,1010u,1011u],Values.ToArray());
            // 枠より大きく読もうとすると、置き場の外を読まずに断る。
            Assert.Throws<ArgumentOutOfRangeException>(()=>Ring.TryRead(0,new uint[5]));
            Ring.Invalidate();
            Assert.False(Ring.TryRead(0,Values));
            Device.PollDebugMessages();
            Assert.Equal(0,Device.ErrorOrWorseMessageCount);
        } finally {
            if(Fence!=null){ _=Fence->Release(); }
            if(List!=null){ _=List->Release(); }
            if(Allocator!=null){ _=Allocator->Release(); }
            if(Source!=null){ _=Source->Release(); }
            _=CloseHandle(Event);
        }
    }
}
