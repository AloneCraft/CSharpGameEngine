using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Rendering;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
namespace CSharpGameEngine.Graphics.D3D12;
public sealed unsafe partial class StageRenderer {
    private const int VirtualAtlasSize=4096,VirtualPages=1024;
    private readonly VirtualDirectionalShadowCache VirtualShadowCache=new();
    private readonly List<VirtualShadowBounds>VirtualShadowObjects=[];
    private Camera?LastShadowCamera;
    private float LastShadowCameraAspect;
    private ID3D12Resource*VirtualShadowAtlas;
    private bool VirtualAtlasAllocated;
    private ID3D12DescriptorHeap*VirtualShadowDsv;
    private ID3D12Resource*VirtualPageTableBuffer;
    private byte*VirtualPageTableMapped;
    private ID3D12Resource*VirtualProjectionBuffer;
    private byte*VirtualProjectionMapped;
    private float VirtualOriginStamp;
    private ID3D12QueryHeap*ShadowTimestampHeap;
    private ID3D12Resource*ShadowTimestampReadback;
    private ulong ShadowTimestampFrequency;
    private readonly bool[]ShadowTimestampsValid=new bool[FrameCount];
    public float CascadedShadowGpuMilliseconds{get;private set;}
    public float VirtualShadowGpuMilliseconds{get;private set;}
    public VirtualShadowStatistics VirtualShadowStatistics=>this.VirtualShadowCache.Statistics;
    public DirectionalShadowSettings DirectionalShadows=>this.ShadowSettings;
    private DirectionalShadowSettings ShadowSettings=>this.EnvironmentSettings.DirectionalShadows??new(DirectionalShadowMode.Cascaded);
    private void CreateVirtualShadows(){
        // 無効モードでは有効な1x1リソースをバインドする。64 MiBの物理プールは初回使用時にだけ確保する。
        var Desc=D3D12_RESOURCE_DESC.Tex2D(ShadowAtlasResourceFormat,1,1,mipLevels:1);
        Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
        var Heap=new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        var Clear=new D3D12_CLEAR_VALUE(ShadowAtlasDsvFormat,1f,0);
        ID3D12Resource*Atlas;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,&Clear,__uuidof<ID3D12Resource>(),(void**)&Atlas),nameof(this.CreateVirtualShadows),"virtual shadow atlas");
        this.VirtualShadowAtlas=Atlas;
        var DsvDesc=new D3D12_DESCRIPTOR_HEAP_DESC { Type=D3D12_DESCRIPTOR_HEAP_TYPE_DSV,NumDescriptors=1 };
        ID3D12DescriptorHeap*Dsv;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&DsvDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Dsv),nameof(this.CreateVirtualShadows),"virtual shadow DSV");
        this.VirtualShadowDsv=Dsv;
        var View=new D3D12_DEPTH_STENCIL_VIEW_DESC { Format=ShadowAtlasDsvFormat,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateDepthStencilView(Atlas,&View,Dsv->GetCPUDescriptorHandleForHeapStart());
        this.CreateUploadBuffer(VirtualDirectionalShadowCache.TableLength*4*FrameCount,out this.VirtualPageTableBuffer,out this.VirtualPageTableMapped,"virtual shadow page tables");
        Unsafe.InitBlock(this.VirtualPageTableMapped,0,VirtualDirectionalShadowCache.TableLength*4*FrameCount);
        this.CreateUploadBuffer(VirtualPages*ConstantAlignment*FrameCount,out this.VirtualProjectionBuffer,out this.VirtualProjectionMapped,"virtual shadow projections");
        var QueryDesc=new D3D12_QUERY_HEAP_DESC { Type=D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_TIMESTAMP,Count=FrameCount*4 };
        ID3D12QueryHeap*Query;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateQueryHeap(&QueryDesc,__uuidof<ID3D12QueryHeap>(),(void**)&Query),nameof(this.CreateVirtualShadows),"shadow timestamps");
        this.ShadowTimestampHeap=Query;
        var ReadbackHeap=new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_READBACK);
        var BufferDesc=D3D12_RESOURCE_DESC.Buffer(FrameCount*4*sizeof(ulong));
        ID3D12Resource*Readback;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&ReadbackHeap,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,null,__uuidof<ID3D12Resource>(),(void**)&Readback),nameof(this.CreateVirtualShadows),"shadow timing readback");
        this.ShadowTimestampReadback=Readback;
        ulong Frequency;D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->GetTimestampFrequency(&Frequency),nameof(this.CreateVirtualShadows),"shadow timestamp frequency");this.ShadowTimestampFrequency=Frequency;
    }
    private void WriteVirtualShadowDescriptors(D3D12_CPU_DESCRIPTOR_HANDLE AtlasHandle,D3D12_CPU_DESCRIPTOR_HANDLE TableHandle){
        var Atlas=new D3D12_SHADER_RESOURCE_VIEW_DESC { Format=ShadowAtlasSrvFormat,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
        Atlas.Texture2D.MipLevels=1;
        this.Device!.Device->CreateShaderResourceView(this.VirtualShadowAtlas,&Atlas,AtlasHandle);
        var Table=new D3D12_SHADER_RESOURCE_VIEW_DESC { Format=DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
        Table.Buffer.NumElements=VirtualDirectionalShadowCache.TableLength*FrameCount;Table.Buffer.StructureByteStride=4;
        this.Device.Device->CreateShaderResourceView(this.VirtualPageTableBuffer,&Table,TableHandle);
    }
    private void EnsureVirtualShadowAtlas(){
        if(this.VirtualAtlasAllocated){ return; }
        this.WaitForGpuIdle();
        var Desc=D3D12_RESOURCE_DESC.Tex2D(ShadowAtlasResourceFormat,VirtualAtlasSize,VirtualAtlasSize,mipLevels:1);Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
        var Heap=new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);var Clear=new D3D12_CLEAR_VALUE(ShadowAtlasDsvFormat,1f,0);
        ID3D12Resource*Atlas;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,&Clear,__uuidof<ID3D12Resource>(),(void**)&Atlas),nameof(this.EnsureVirtualShadowAtlas),"physical page pool");
        _=this.VirtualShadowAtlas->Release();this.VirtualShadowAtlas=Atlas;
        var Dsv=new D3D12_DEPTH_STENCIL_VIEW_DESC { Format=ShadowAtlasDsvFormat,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateDepthStencilView(Atlas,&Dsv,this.VirtualShadowDsv->GetCPUDescriptorHandleForHeapStart());
        this.VirtualShadowCache.Reset();this.VirtualAtlasAllocated=true;
        this.WriteEnvironmentDescriptors();
    }
    private void PrepareVirtualShadows(){
        var Frame=this.FrameSlot(this.FrameIndex);
        *(Vector4*)(Frame+FrameVirtualParamsOffset)=Vector4.Zero;
        var Settings=this.ShadowSettings;
        *(Vector4*)(Frame+FrameDirectionalSoftShadowOffset)=new(MathF.Tan(Settings.SourceAngularDiameterDegrees*MathF.PI/360f),Settings.MaximumSoftShadowRadiusMeters,Settings.SoftShadows?1f:0f,0f);
        if(Settings.Mode!=DirectionalShadowMode.Virtual&&!Settings.Compare||this.DirectionalShadowIndex<0){ this.VirtualShadowCache.SuspendFrame();return; }
        var Light=*(Vector4*)(Frame+FrameDirectionalLightsOffset+(uint)this.DirectionalShadowIndex*DirectionalLightStride);
        if(this.VirtualOriginStamp!=this.RenderOriginAltitudeMeters){ this.VirtualShadowCache.Reset();this.VirtualOriginStamp=this.RenderOriginAltitudeMeters; }
        this.VirtualShadowObjects.Clear();
        for(var I=0;I<this.ObjectCount;I++){
            if(!this.ObjectActive[I]){ continue; }
            var Mesh=this.ObjectMeshIndices[I];var Min=new Vector3(float.PositiveInfinity);var Max=new Vector3(float.NegativeInfinity);
            var Outside=new int[6];
            var Bounds=this.Skinning!.ConservativeBounds(Mesh,this.MeshBoundsMinimums[Mesh],this.MeshBoundsMaximums[Mesh]);
            for(var Corner=0;Corner<8;Corner++){
                var A=Bounds.Minimum;var B=Bounds.Maximum;
                var P=Vector3.Transform(new Vector3((Corner&1)==0?A.X:B.X,(Corner&2)==0?A.Y:B.Y,(Corner&4)==0?A.Z:B.Z),this.ObjectTransforms[I]);
                Min=Vector3.Min(Min,P);Max=Vector3.Max(Max,P);
                var Clip=Vector4.Transform(new Vector4(P,1f),this.CurrentViewProjection);
                if(Clip.X< -Clip.W){ Outside[0]++; }if(Clip.X>Clip.W){ Outside[1]++; }
                if(Clip.Y< -Clip.W){ Outside[2]++; }if(Clip.Y>Clip.W){ Outside[3]++; }
                if(Clip.Z<0f){ Outside[4]++; }if(Clip.Z>Clip.W){ Outside[5]++; }
            }
            var Visible=!this.ObjectHiddenFromView[I]&&!Outside.Contains(8)&&Vector3.Distance(this.CurrentCameraPosition,Vector3.Clamp(this.CurrentCameraPosition,Min,Max))<=200f;
            this.VirtualShadowObjects.Add(new(I,Min,Max,Visible,!this.ObjectNoShadow[I],this.Skinning!.IsSkinned(Mesh),Mesh,this.ObjectTransforms[I]));
        }
        this.VirtualShadowCache.Plan(this.CurrentCameraPosition,new(Light.X,Light.Y,Light.Z),System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.VirtualShadowObjects),Settings.PageBudget,Settings.SoftShadows?Settings.MaximumSoftShadowRadiusMeters:0f);
        this.VirtualShadowCache.PageTable.CopyTo(new Span<uint>(this.VirtualPageTableMapped+this.FrameIndex*VirtualDirectionalShadowCache.TableLength*4,VirtualDirectionalShadowCache.TableLength));
        var Projections=this.VirtualShadowCache.Projections;
        fixed(Matrix4x4*Data=Projections){ Unsafe.CopyBlock(Frame+FrameVirtualProjectionOffset,Data,256); }
        *(Vector4*)(Frame+FrameVirtualParamsOffset)=new(1f,this.FrameIndex*VirtualDirectionalShadowCache.TableLength,Settings.Compare?1f:0f,VirtualDirectionalShadowCache.DepthRangeMeters);
        if(this.VirtualShadowCache.PagesToRender.Count==0){ return; }
        var Barrier=D3D12_RESOURCE_BARRIER.InitTransition(this.VirtualShadowAtlas,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        this.CommandList->ResourceBarrier(1,&Barrier);
        var Dsv=this.VirtualShadowDsv->GetCPUDescriptorHandleForHeapStart();
        this.CommandList->OMSetRenderTargets(0,null,FALSE,&Dsv);
        this.CommandList->SetGraphicsRootSignature(this.ShadowRootSignature);
        this.CommandList->SetPipelineState(this.ShadowPipelineState);
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var Index=0;
        foreach(var Page in this.VirtualShadowCache.PagesToRender){
            var Offset=(this.FrameIndex*VirtualPages+(uint)Index++)*ConstantAlignment;
            var Projection=Page.Projection;Unsafe.CopyBlock(this.VirtualProjectionMapped+Offset,&Projection,64);
            this.CommandList->SetGraphicsRootConstantBufferView(0,this.VirtualProjectionBuffer->GetGPUVirtualAddress()+Offset);
            var X=Page.PhysicalIndex%32*128;var Y=Page.PhysicalIndex/32*128;
            var Rect=new RECT { left=X,top=Y,right=X+128,bottom=Y+128 };
            this.CommandList->ClearDepthStencilView(Dsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1f,0,1,&Rect);
            var Viewport=new D3D12_VIEWPORT { TopLeftX=X,TopLeftY=Y,Width=128,Height=128,MaxDepth=1f };
            this.CommandList->RSSetViewports(1,&Viewport);this.CommandList->RSSetScissorRects(1,&Rect);
            foreach(var Object in this.VirtualShadowObjects){
                if(!Object.CastsShadow||!IntersectsProjection(Object,Projection)){ continue; }
                var ObjectIndex=Object.Id;var Mesh=this.ObjectMeshIndices[ObjectIndex];
                this.CommandList->SetGraphicsRootConstantBufferView(1,this.GetObjectConstantsAddress(this.FrameIndex,ObjectIndex));
                fixed(D3D12_VERTEX_BUFFER_VIEW*Vertex=&this.VertexBufferViews[Mesh])
                fixed(D3D12_INDEX_BUFFER_VIEW*Indices=&this.IndexBufferViews[Mesh]){
                    this.CommandList->IASetVertexBuffers(0,1,Vertex);this.CommandList->IASetIndexBuffer(Indices);this.CommandList->DrawIndexedInstanced(this.IndexCounts[Mesh],1,0,0,0);
                }
            }
        }
        Barrier=D3D12_RESOURCE_BARRIER.InitTransition(this.VirtualShadowAtlas,D3D12_RESOURCE_STATE_DEPTH_WRITE,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        this.CommandList->ResourceBarrier(1,&Barrier);
    }
    private static bool IntersectsProjection(VirtualShadowBounds Object,Matrix4x4 Projection){
        Span<int>Outside=stackalloc int[6];
        for(var I=0;I<8;I++){
            var P=Vector4.Transform(new Vector4((I&1)==0?Object.Minimum.X:Object.Maximum.X,(I&2)==0?Object.Minimum.Y:Object.Maximum.Y,(I&4)==0?Object.Minimum.Z:Object.Maximum.Z,1f),Projection);
            if(P.X< -P.W){ Outside[0]++; }if(P.X>P.W){ Outside[1]++; }if(P.Y< -P.W){ Outside[2]++; }if(P.Y>P.W){ Outside[3]++; }if(P.Z<0f){ Outside[4]++; }if(P.Z>P.W){ Outside[5]++; }
        }
        return !Outside.Contains(8);
    }
    private void DisposeVirtualShadows(){
        if(this.VirtualShadowAtlas!=null){ _=this.VirtualShadowAtlas->Release();this.VirtualShadowAtlas=null; }
        if(this.VirtualShadowDsv!=null){ _=this.VirtualShadowDsv->Release();this.VirtualShadowDsv=null; }
        if(this.VirtualPageTableBuffer!=null){ _=this.VirtualPageTableBuffer->Release();this.VirtualPageTableBuffer=null; }
        if(this.VirtualProjectionBuffer!=null){ _=this.VirtualProjectionBuffer->Release();this.VirtualProjectionBuffer=null; }
        if(this.ShadowTimestampHeap!=null){ _=this.ShadowTimestampHeap->Release();this.ShadowTimestampHeap=null; }
        if(this.ShadowTimestampReadback!=null){ _=this.ShadowTimestampReadback->Release();this.ShadowTimestampReadback=null; }
    }
    private void DrawShadowComparison(){
        var Source=this.FrameSlot(this.FrameIndex);
        var Left=this.FrameSlot(this.FrameIndex,1);
        var Right=this.FrameSlot(this.FrameIndex,2);
        Unsafe.CopyBlock(Left,Source,FrameConstantsSize);
        Unsafe.CopyBlock(Right,Source,FrameConstantsSize);
        ((Vector4*)(Left+FrameVirtualParamsOffset))->X=0f;
        var Half=this.Width/2;
        for(var Side=0;Side<2;Side++){
            var X=Side*Half;
            var Viewport=new D3D12_VIEWPORT { TopLeftX=X,Width=Half,Height=this.Height,MaxDepth=1f };
            var Rect=new RECT { left=X,top=0,right=X+Half,bottom=this.Height };
            this.CommandList->RSSetViewports(1,&Viewport);this.CommandList->RSSetScissorRects(1,&Rect);
            this.CommandList->SetGraphicsRootConstantBufferView(0,this.GetFrameConstantsAddress(this.FrameIndex,Side+1));
            this.DrawObjects(-1);
        }
        this.CommandList->SetGraphicsRootConstantBufferView(0,this.GetFrameConstantsAddress(this.FrameIndex));
    }
    private void ReadShadowTimings(uint?CompletedSlot=null){
        var Slot=CompletedSlot??this.FrameIndex;
        if(!this.ShadowTimestampsValid[Slot]){ return; }
        // MoveToNextFrameは、このスロットが再度書き込み可能になる前に対応するフェンスの完了を待機する。
        var Range=new D3D12_RANGE { Begin=(nuint)(Slot*4*sizeof(ulong)),End=(nuint)((Slot+1)*4*sizeof(ulong)) };
        ulong*Data;D3D12Diagnostics.EnsureSuccess(this.ShadowTimestampReadback->Map(0,&Range,(void**)&Data),nameof(this.ReadShadowTimings),"completed shadow timestamps");
        var Start=Slot*4;
        this.CascadedShadowGpuMilliseconds=(Data[Start+1]-Data[Start])*1000f/this.ShadowTimestampFrequency;
        this.VirtualShadowGpuMilliseconds=(Data[Start+3]-Data[Start+2])*1000f/this.ShadowTimestampFrequency;
        var NoWrite=new D3D12_RANGE();this.ShadowTimestampReadback->Unmap(0,&NoWrite);
    }
    private void ShadowTimestamp(uint Index)=>this.CommandList->EndQuery(this.ShadowTimestampHeap,D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP,this.FrameIndex*4+Index);
}
