using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// Test-only synchronous copy of one 2D texture subresource to the CPU. The caller guarantees the
/// queue is idle and passes the state the texture rests in; the copy restores it.
/// </summary>
internal static unsafe class GpuTextureReadback {
    internal static (byte[]Data,int RowPitch,int Width,int Height) Read(GraphicsDevice Device,ID3D12Resource*Texture,D3D12_RESOURCE_STATES RestingState){
        ID3D12CommandAllocator*Allocator=null;
        ID3D12GraphicsCommandList*List=null;
        ID3D12Resource*Readback=null;
        ID3D12Fence*Fence=null;
        HANDLE Event=HANDLE.NULL;
        try {
            D3D12Diagnostics.EnsureSuccess(Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),"CreateCommandAllocator","texture readback");
            D3D12Diagnostics.EnsureSuccess(Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&List),"CreateCommandList","texture readback");
            var Desc=Texture->GetDesc();
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
            ulong TotalBytes;
            Device.Device->GetCopyableFootprints(&Desc,0,1,0,&Footprint,null,null,&TotalBytes);
            D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_READBACK);
            var BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);
            D3D12Diagnostics.EnsureSuccess(Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,null,__uuidof<ID3D12Resource>(),(void**)&Readback),"CreateCommittedResource","texture readback");
            D3D12_RESOURCE_BARRIER ToCopy=D3D12_RESOURCE_BARRIER.InitTransition(Texture,RestingState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            List->ResourceBarrier(1,&ToCopy);
            D3D12_TEXTURE_COPY_LOCATION Destination=new(){ pResource=Readback,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT,PlacedFootprint=Footprint };
            D3D12_TEXTURE_COPY_LOCATION Source=new(){ pResource=Texture,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            List->CopyTextureRegion(&Destination,0,0,0,&Source,null);
            D3D12_RESOURCE_BARRIER Back=D3D12_RESOURCE_BARRIER.InitTransition(Texture,D3D12_RESOURCE_STATE_COPY_SOURCE,RestingState);
            List->ResourceBarrier(1,&Back);
            D3D12Diagnostics.EnsureSuccess(List->Close(),"Close","texture readback");
            ID3D12CommandList*Base=(ID3D12CommandList*)List;
            Device.CommandQueue->ExecuteCommandLists(1,&Base);
            D3D12Diagnostics.EnsureSuccess(Device.Device->CreateFence(0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),"CreateFence","texture readback");
            Event=CreateEventW(null,FALSE,FALSE,null);
            D3D12Diagnostics.EnsureSuccess(Device.CommandQueue->Signal(Fence,1),"Signal","texture readback");
            if(Fence->GetCompletedValue()<1){
                D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(1,Event),"SetEventOnCompletion","texture readback");
                _=WaitForSingleObjectEx(Event,INFINITE,FALSE);
            }
            byte*Mapped;
            D3D12_RANGE Range=new(){ Begin=0,End=(nuint)TotalBytes };
            D3D12Diagnostics.EnsureSuccess(Readback->Map(0,&Range,(void**)&Mapped),"Map","texture readback");
            try {
                return (new ReadOnlySpan<byte>(Mapped+Footprint.Offset,checked((int)(TotalBytes-Footprint.Offset))).ToArray(),(int)Footprint.Footprint.RowPitch,(int)Desc.Width,(int)Desc.Height);
            } finally {
                D3D12_RANGE NoWrite=new();
                Readback->Unmap(0,&NoWrite);
            }
        } finally {
            if(Event!=HANDLE.NULL){ _=CloseHandle(Event); }
            if(Fence!=null){ _=Fence->Release(); }
            if(Readback!=null){ _=Readback->Release(); }
            if(List!=null){ _=List->Release(); }
            if(Allocator!=null){ _=Allocator->Release(); }
        }
    }
}
