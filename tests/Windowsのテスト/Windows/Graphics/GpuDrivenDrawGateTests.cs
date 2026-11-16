using Xunit;
using CSharpGameEngine.Compiler.Hlsl;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0129 単位 1 の関門。仮想化描画（ADR-0100）が前提にする 3 つの D3D12 の機能が、WARP と実機で動くことを、エンジンの描画器を通さずに確かめる。
/// 1. 計算シェーダーが書いた「描く数」で間接描画する（ExecuteIndirect）。
/// 2. 頂点シェーダーが頂点番号とインスタンス番号だけから、構造化バッファを引いて形を作る（頂点バッファなし）。
/// 3. 画素シェーダーが、上限なしのテクスチャ配列を番号で引く（番号引き）。
/// ここが通らなければ、GPU シーン化より前に計画を見直す。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed unsafe class GpuDrivenDrawGateTests {
    private const int Columns=4;
    private const int Rows=2;
    private const int CellPixels=16;
    private const int Width=Columns*CellPixels;
    private const int Height=Rows*CellPixels;
    private const int InstanceCount=Columns*Rows;
    private const int TextureCount=4;
    private const uint VisibleBase=3;
    private const uint VerticesPerInstance=6;
    private const string DrawSource="""
struct InstanceData { float2 CellOrigin; uint TextureIndex; uint Padding; };
StructuredBuffer<InstanceData> Instances : register(t0);
StructuredBuffer<uint> VisibleInstances : register(t1);
Texture2D<float4> Textures[] : register(t0, space1);
cbuffer DrawConstants : register(b0) { uint VisibleBase; uint Reserved0; uint Reserved1; uint Reserved2; };
struct VSOutput { float4 Position : SV_Position; nointerpolation uint TextureIndex : TEXINDEX; };
VSOutput VSMain(uint VertexId : SV_VertexID, uint InstanceId : SV_InstanceID) {
    static const float2 Corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };
    InstanceData Instance = Instances[VisibleInstances[VisibleBase + InstanceId]];
    float2 Cell = Instance.CellOrigin + Corners[VertexId] * float2(0.25, 0.5);
    VSOutput Output;
    Output.Position = float4(Cell.x * 2.0 - 1.0, 1.0 - Cell.y * 2.0, 0.0, 1.0);
    Output.TextureIndex = Instance.TextureIndex;
    return Output;
}
float4 PSMain(VSOutput Input) : SV_Target {
    return Textures[NonUniformResourceIndex(Input.TextureIndex)].Load(int3(0, 0, 0));
}
""";
    private const string SelectSource="""
StructuredBuffer<uint> VisibleFlags : register(t0);
RWStructuredBuffer<uint> VisibleInstances : register(u0);
RWStructuredBuffer<uint> DrawArguments : register(u1);
cbuffer SelectConstants : register(b0) { uint InstanceCount; uint VerticesPerInstance; uint VisibleBase; uint Reserved; };
[numthreads(1, 1, 1)]
void CSMain() {
    uint Count = 0;
    for (uint Index = 0; Index < InstanceCount; Index++) {
        if (VisibleFlags[Index] != 0) {
            VisibleInstances[VisibleBase + Count] = Index;
            Count++;
        }
    }
    DrawArguments[0] = VerticesPerInstance;
    DrawArguments[1] = Count;
    DrawArguments[2] = 0;
    DrawArguments[3] = 0;
}
""";
    private static readonly uint[]VisibleFlags=[1,0,1,1,0,1,0,1];
    private static readonly float[][]TextureColors=[[1f,0f,0f,1f],[0f,1f,0f,1f],[0f,0f,1f,1f],[1f,1f,0f,1f]];
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_ComputeWrittenIndirectDrawSamplesIndexedTextures()=>Run(UseWarp:true);
    /// <summary>実機での確認。<c>CGE_PERFORMANCE_EVIDENCE=1</c> のときだけ動く。</summary>
    [PerformanceEvidenceFact]
    [Trait("Category","GPU")]
    public void Hardware_ComputeWrittenIndirectDrawSamplesIndexedTextures()=>Run(UseWarp:false);
    private static uint TextureIndexOf(int Instance)=>(uint)(((Instance*3)+1)%TextureCount);
    private static void Run(bool UseWarp){
        var WorkDirectory=Path.Combine(AppContext.BaseDirectory,"GpuDrivenDrawGate");
        Directory.CreateDirectory(WorkDirectory);
        var VertexShader=Compile(WorkDirectory,"GateDraw.hlsl",DrawSource,"VSMain","vs_6_0");
        var PixelShader=Compile(WorkDirectory,"GateDraw.hlsl",DrawSource,"PSMain","ps_6_0");
        var ComputeShader=Compile(WorkDirectory,"GateSelect.hlsl",SelectSource,"CSMain","cs_6_0");
        using var Device=GraphicsDevice.Create(new GraphicsDeviceOptions { UseWarp=UseWarp,EnableDebugLayer=true });
        Assert.Equal(UseWarp,Device.IsWarp);
        Assert.True(Device.ResourceBindingTier>=2,$"{Device.AdapterDescription} reports resource binding tier {Device.ResourceBindingTier}; indexed texture arrays need tier 2.");
        var Owned=new List<nint>();
        var FenceEvent=HANDLE.NULL;
        try {
            var Native=Device.Device;
            var ComputeRoot=Own(Owned,CreateComputeRootSignature(Device));
            var GraphicsRoot=Own(Owned,CreateGraphicsRootSignature(Device));
            var ComputePipeline=Own(Owned,CreateComputePipeline(Device,ComputeRoot,ComputeShader));
            var GraphicsPipeline=Own(Owned,CreateGraphicsPipeline(Device,GraphicsRoot,VertexShader,PixelShader));
            D3D12_INDIRECT_ARGUMENT_DESC Argument=new(){ Type=D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DRAW };
            D3D12_COMMAND_SIGNATURE_DESC SignatureDesc=new(){ ByteStride=(uint)sizeof(D3D12_DRAW_ARGUMENTS),NumArgumentDescs=1,pArgumentDescs=&Argument };
            ID3D12CommandSignature*CommandSignature;
            D3D12Diagnostics.EnsureSuccess(Native->CreateCommandSignature(&SignatureDesc,null,__uuidof<ID3D12CommandSignature>(),(void**)&CommandSignature),"ID3D12Device::CreateCommandSignature","creating the draw command signature");
            Own(Owned,CommandSignature);
            // 描画先と、番号で引かれる 4 枚の単色テクスチャ。単色はクリアで作るので、アップロードは要らない。
            float[]Transparent=[0f,0f,0f,0f];
            var Target=Own(Owned,CreateColorTarget(Device,Width,Height,Transparent));
            var Textures=new ID3D12Resource*[TextureCount];
            for(var Index=0;Index<TextureCount;Index++){
                Textures[Index]=Own(Owned,CreateColorTarget(Device,1,1,TextureColors[Index]));
            }
            D3D12_DESCRIPTOR_HEAP_DESC RtvHeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV,NumDescriptors=TextureCount+1 };
            ID3D12DescriptorHeap*RtvHeap;
            D3D12Diagnostics.EnsureSuccess(Native->CreateDescriptorHeap(&RtvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&RtvHeap),"ID3D12Device::CreateDescriptorHeap","creating the gate RTV heap");
            Own(Owned,RtvHeap);
            D3D12_DESCRIPTOR_HEAP_DESC SrvHeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=TextureCount,Flags=D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
            ID3D12DescriptorHeap*SrvHeap;
            D3D12Diagnostics.EnsureSuccess(Native->CreateDescriptorHeap(&SrvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&SrvHeap),"ID3D12Device::CreateDescriptorHeap","creating the gate SRV heap");
            Own(Owned,SrvHeap);
            var RtvSize=Native->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
            var SrvSize=Native->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
            D3D12_CPU_DESCRIPTOR_HANDLE TargetRtv=new(RtvHeap->GetCPUDescriptorHandleForHeapStart(),TextureCount,RtvSize);
            Native->CreateRenderTargetView(Target,null,TargetRtv);
            for(var Index=0;Index<TextureCount;Index++){
                Native->CreateRenderTargetView(Textures[Index],null,new D3D12_CPU_DESCRIPTOR_HANDLE(RtvHeap->GetCPUDescriptorHandleForHeapStart(),Index,RtvSize));
                Native->CreateShaderResourceView(Textures[Index],null,new D3D12_CPU_DESCRIPTOR_HANDLE(SrvHeap->GetCPUDescriptorHandleForHeapStart(),Index,SrvSize));
            }
            // 物体の表（位置とテクスチャ番号）と、見えるかどうかの印。
            var InstanceBytes=(ulong)(InstanceCount*16);
            var Instances=Own(Owned,CreateBuffer(Device,InstanceBytes,D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD,D3D12_RESOURCE_STATE_GENERIC_READ,D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE));
            var Flags=Own(Owned,CreateBuffer(Device,InstanceCount*sizeof(uint),D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD,D3D12_RESOURCE_STATE_GENERIC_READ,D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE));
            var VisibleBytes=(ulong)((VisibleBase+InstanceCount)*sizeof(uint));
            var Visible=Own(Owned,CreateBuffer(Device,VisibleBytes,D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS));
            var Arguments=Own(Owned,CreateBuffer(Device,(ulong)sizeof(D3D12_DRAW_ARGUMENTS),D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS));
            void*Mapped;
            D3D12_RANGE NoRead=new();
            D3D12Diagnostics.EnsureSuccess(Instances->Map(0,&NoRead,&Mapped),"ID3D12Resource::Map","mapping the gate instances");
            for(var Index=0;Index<InstanceCount;Index++){
                var Row=(byte*)Mapped+(Index*16);
                *(float*)Row=(Index%Columns)/(float)Columns;
                *(float*)(Row+4)=(Index/Columns)/(float)Rows;
                *(uint*)(Row+8)=TextureIndexOf(Index);
                *(uint*)(Row+12)=0;
            }
            Instances->Unmap(0,null);
            D3D12Diagnostics.EnsureSuccess(Flags->Map(0,&NoRead,&Mapped),"ID3D12Resource::Map","mapping the gate visibility flags");
            VisibleFlags.CopyTo(new Span<uint>(Mapped,InstanceCount));
            Flags->Unmap(0,null);
            ID3D12CommandAllocator*Allocator;
            D3D12Diagnostics.EnsureSuccess(Native->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),"ID3D12Device::CreateCommandAllocator","creating the gate allocator");
            Own(Owned,Allocator);
            ID3D12GraphicsCommandList*List;
            D3D12Diagnostics.EnsureSuccess(Native->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&List),"ID3D12Device::CreateCommandList","creating the gate command list");
            Own(Owned,List);
            var Barriers=stackalloc D3D12_RESOURCE_BARRIER[TextureCount+2];
            for(var Index=0;Index<TextureCount;Index++){
                fixed(float*Color=TextureColors[Index]){
                    List->ClearRenderTargetView(new D3D12_CPU_DESCRIPTOR_HANDLE(RtvHeap->GetCPUDescriptorHandleForHeapStart(),Index,RtvSize),Color,0,null);
                }
                Barriers[Index]=D3D12_RESOURCE_BARRIER.InitTransition(Textures[Index],D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            }
            // 選択：見える物の番号を詰め、描く数を引数バッファへ書く。CPU は数を知らない。
            var SelectConstants=stackalloc uint[4]{ InstanceCount,VerticesPerInstance,VisibleBase,0 };
            List->SetComputeRootSignature(ComputeRoot);
            List->SetPipelineState(ComputePipeline);
            List->SetComputeRoot32BitConstants(0,4,SelectConstants,0);
            List->SetComputeRootShaderResourceView(1,Flags->GetGPUVirtualAddress());
            List->SetComputeRootUnorderedAccessView(2,Visible->GetGPUVirtualAddress());
            List->SetComputeRootUnorderedAccessView(3,Arguments->GetGPUVirtualAddress());
            List->Dispatch(1,1,1);
            Barriers[TextureCount]=D3D12_RESOURCE_BARRIER.InitTransition(Visible,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
            Barriers[TextureCount+1]=D3D12_RESOURCE_BARRIER.InitTransition(Arguments,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
            List->ResourceBarrier(TextureCount+2,Barriers);
            // 描画：頂点バッファを使わず、描く数は GPU が書いた引数から取る。
            var DrawConstants=stackalloc uint[4]{ VisibleBase,0,0,0 };
            D3D12_VIEWPORT Viewport=new(){ Width=Width,Height=Height,MinDepth=0f,MaxDepth=1f };
            RECT Scissor=new(){ left=0,top=0,right=Width,bottom=Height };
            List->SetGraphicsRootSignature(GraphicsRoot);
            List->SetPipelineState(GraphicsPipeline);
            List->SetDescriptorHeaps(1,&SrvHeap);
            List->RSSetViewports(1,&Viewport);
            List->RSSetScissorRects(1,&Scissor);
            List->OMSetRenderTargets(1,&TargetRtv,FALSE,null);
            fixed(float*Clear=Transparent){
                List->ClearRenderTargetView(TargetRtv,Clear,0,null);
            }
            List->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            List->SetGraphicsRoot32BitConstants(0,4,DrawConstants,0);
            List->SetGraphicsRootShaderResourceView(1,Instances->GetGPUVirtualAddress());
            List->SetGraphicsRootShaderResourceView(2,Visible->GetGPUVirtualAddress());
            List->SetGraphicsRootDescriptorTable(3,SrvHeap->GetGPUDescriptorHandleForHeapStart());
            List->ExecuteIndirect(CommandSignature,1,Arguments,0,null,0);
            D3D12Diagnostics.EnsureSuccess(List->Close(),"ID3D12GraphicsCommandList::Close","closing the gate command list");
            var BaseList=(ID3D12CommandList*)List;
            Device.CommandQueue->ExecuteCommandLists(1,&BaseList);
            ID3D12Fence*Fence;
            D3D12Diagnostics.EnsureSuccess(Native->CreateFence(0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),"ID3D12Device::CreateFence","creating the gate fence");
            Own(Owned,Fence);
            FenceEvent=CreateEventW(null,FALSE,FALSE,null);
            Assert.True(FenceEvent!=HANDLE.NULL);
            D3D12Diagnostics.EnsureSuccess(Device.CommandQueue->Signal(Fence,1),"ID3D12CommandQueue::Signal","signaling the gate frame");
            if(Fence->GetCompletedValue()<1){
                D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(1,FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the gate frame");
                _=WaitForSingleObjectEx(FenceEvent,INFINITE,FALSE);
            }
            var WrittenArguments=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,uint>(GpuBufferReadback.Read(Device,Arguments,(ulong)sizeof(D3D12_DRAW_ARGUMENTS),D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT)).ToArray();
            var (Pixels,RowPitch,ReadWidth,ReadHeight)=GpuTextureReadback.Read(Device,Target,D3D12_RESOURCE_STATE_RENDER_TARGET);
            Device.PollDebugMessages();
            Assert.True(Device.ErrorOrWorseMessageCount==0,string.Join(Environment.NewLine,Device.CollectedMessages.Where(Message=>Message.IsErrorOrWorse)));
            // 描く数は GPU が数えた「見える物の数」になっている。
            Assert.Equal([VerticesPerInstance,(uint)VisibleFlags.Count(Flag=>Flag!=0),0u,0u],WrittenArguments);
            Assert.Equal((Width,Height),(ReadWidth,ReadHeight));
            // 見える物のマスは、その物が番号で指したテクスチャの色。見えない物のマスは、クリアしたまま。
            for(var Index=0;Index<InstanceCount;Index++){
                var Expected=VisibleFlags[Index]!=0?TextureColors[TextureIndexOf(Index)]:Transparent;
                foreach(var (OffsetX,OffsetY) in new[]{ (0,0),(CellPixels-1,0),(0,CellPixels-1),(CellPixels-1,CellPixels-1),(CellPixels/2,CellPixels/2) }){
                    var X=((Index%Columns)*CellPixels)+OffsetX;
                    var Y=((Index/Columns)*CellPixels)+OffsetY;
                    var Offset=(Y*RowPitch)+(X*4);
                    var Actual=new[]{ Pixels[Offset],Pixels[Offset+1],Pixels[Offset+2],Pixels[Offset+3] };
                    Assert.True(Expected.Select(Channel=>(byte)(Channel*255f)).SequenceEqual(Actual),$"instance {Index} pixel ({X},{Y}) on {Device.AdapterDescription}: expected [{string.Join(",",Expected.Select(Channel=>(byte)(Channel*255f)))}], got [{string.Join(",",Actual)}]");
                }
            }
        } finally {
            if(FenceEvent!=HANDLE.NULL){ _=CloseHandle(FenceEvent); }
            for(var Index=Owned.Count-1;Index>=0;Index--){ _=((IUnknown*)Owned[Index])->Release(); }
        }
    }
    private static T*Own<T>(List<nint>Owned,T*Pointer)where T:unmanaged{
        Owned.Add((nint)Pointer);
        return Pointer;
    }
    private static byte[]Compile(string WorkDirectory,string FileName,string Source,string EntryPoint,string Profile){
        var FilePath=Path.Combine(WorkDirectory,FileName);
        File.WriteAllText(FilePath,Source);
        var Result=HlslCompiler.Compile(new HlslCompileRequest { FilePath=FilePath,EntryPoint=EntryPoint,TargetProfile=Profile });
        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        return Result.Bytecode;
    }
    private static ID3D12Resource*CreateBuffer(GraphicsDevice Device,ulong ByteSize,D3D12_HEAP_TYPE HeapType,D3D12_RESOURCE_STATES InitialState,D3D12_RESOURCE_FLAGS Flags){
        D3D12_HEAP_PROPERTIES Heap=new(HeapType);
        var Desc=D3D12_RESOURCE_DESC.Buffer(ByteSize);
        Desc.Flags=Flags;
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,&Desc,InitialState,null,__uuidof<ID3D12Resource>(),(void**)&Resource),"ID3D12Device::CreateCommittedResource","creating a gate buffer");
        return Resource;
    }
    private static ID3D12Resource*CreateColorTarget(GraphicsDevice Device,int TargetWidth,int TargetHeight,float[]ClearColor){
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT);
        var Desc=D3D12_RESOURCE_DESC.Tex2D(DXGI_FORMAT_R8G8B8A8_UNORM,(uint)TargetWidth,(uint)TargetHeight,mipLevels:1);
        Desc.Flags=D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        D3D12_CLEAR_VALUE Clear=new(){ Format=DXGI_FORMAT_R8G8B8A8_UNORM };
        for(var Channel=0;Channel<4;Channel++){
            Clear.Anonymous.Color[Channel]=ClearColor[Channel];
        }
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_RENDER_TARGET,&Clear,__uuidof<ID3D12Resource>(),(void**)&Resource),"ID3D12Device::CreateCommittedResource","creating a gate colour target");
        return Resource;
    }
    private static ID3D12RootSignature*CreateComputeRootSignature(GraphicsDevice Device){
        var Parameters=stackalloc D3D12_ROOT_PARAMETER[4];
        Parameters[0]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS };
        Parameters[0].Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,Num32BitValues=4 };
        Parameters[1]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_SRV };
        Parameters[1].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[2]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_UAV };
        Parameters[2].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[3]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_UAV };
        Parameters[3].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=4,pParameters=Parameters };
        return SerializeRootSignature(Device,&Desc);
    }
    private static ID3D12RootSignature*CreateGraphicsRootSignature(GraphicsDevice Device){
        // 上限なし（NumDescriptors=uint.MaxValue）の SRV の並び。register(t0, space1) の配列を番号で引く。
        D3D12_DESCRIPTOR_RANGE Range=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=uint.MaxValue,BaseShaderRegister=0,RegisterSpace=1 };
        var Parameters=stackalloc D3D12_ROOT_PARAMETER[4];
        Parameters[0]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS };
        Parameters[0].Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,Num32BitValues=4 };
        Parameters[1]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_VERTEX };
        Parameters[1].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[2]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_VERTEX };
        Parameters[2].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        Parameters[3]=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_PIXEL };
        Parameters[3].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&Range };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=4,pParameters=Parameters };
        return SerializeRootSignature(Device,&Desc);
    }
    private static ID3D12RootSignature*SerializeRootSignature(GraphicsDevice Device,D3D12_ROOT_SIGNATURE_DESC*Desc){
        ID3DBlob*Signature=null;
        ID3DBlob*Errors=null;
        try {
            var Result=D3D12SerializeRootSignature(Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Errors);
            Assert.True(Result.SUCCEEDED,Errors!=null?new string((sbyte*)Errors->GetBufferPointer()):$"D3D12SerializeRootSignature failed 0x{Result.Value:X8}");
            ID3D12RootSignature*RootSignature;
            D3D12Diagnostics.EnsureSuccess(Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&RootSignature),"ID3D12Device::CreateRootSignature","creating a gate root signature");
            return RootSignature;
        } finally {
            if(Errors!=null){ _=Errors->Release(); }
            if(Signature!=null){ _=Signature->Release(); }
        }
    }
    private static ID3D12PipelineState*CreateComputePipeline(GraphicsDevice Device,ID3D12RootSignature*RootSignature,byte[]Bytecode){
        fixed(byte*Shader=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Desc=new(){ pRootSignature=RootSignature,CS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Shader,BytecodeLength=(nuint)Bytecode.Length } };
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(Device.Device->CreateComputePipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),"ID3D12Device::CreateComputePipelineState","creating the gate selection pipeline");
            return Pipeline;
        }
    }
    private static ID3D12PipelineState*CreateGraphicsPipeline(GraphicsDevice Device,ID3D12RootSignature*RootSignature,byte[]VertexShader,byte[]PixelShader){
        fixed(byte*Vertex=VertexShader)
        fixed(byte*Pixel=PixelShader){
            D3D12_GRAPHICS_PIPELINE_STATE_DESC Desc=new(){
                pRootSignature=RootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Vertex,BytecodeLength=(nuint)VertexShader.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Pixel,BytecodeLength=(nuint)PixelShader.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=1,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            Desc.RTVFormats[0]=DXGI_FORMAT_R8G8B8A8_UNORM;
            Desc.RasterizerState.CullMode=D3D12_CULL_MODE.D3D12_CULL_MODE_NONE;
            Desc.DepthStencilState.DepthEnable=FALSE;
            Desc.DepthStencilState.StencilEnable=FALSE;
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(Device.Device->CreateGraphicsPipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),"ID3D12Device::CreateGraphicsPipelineState","creating the gate draw pipeline");
            return Pipeline;
        }
    }
}
