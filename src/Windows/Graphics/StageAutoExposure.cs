using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_RANGE_TYPE;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// ヒストグラム自動露出（ADR-0069）を GPU 上で保持する。LuminanceHistogram.hlsl が HDR 描画先を区分けし、EV100 を測定して状態バッファを適応させる（
/// float4：EV100、露出スケール、収束済み、有効）。Stage プログラム（t15）と空の合成処理が読み取る。CPU は毎フレーム結果をマップしない。WARP が前フレームを実行中に読み戻し
/// バッファをマップすると、デバッグレイヤーの下でデバイスが失われたためである。ルートシグネチャはホストが所有する：0 = ルート定数16個 b0、1 = SRV テーブル t0（HDR 色）、2 =
/// ルート UAV u0（ビン）、3 = ルート UAV u1（状態）。
/// </summary>
internal sealed unsafe class StageAutoExposure:IDisposable {
    internal const string ArtifactFileName="LuminanceHistogram.cgeshaderartifact";
    internal const int BinCount=64;
    internal const float MinLog2Luminance=-16f;
    internal const float MaxLog2Luminance=16f;
    internal const float LowPercent=0.1f;
    internal const float HighPercent=0.9f;
    private const uint StateBytes=16;
    private const uint PassInitialize=0;
    private const uint PassClear=1;
    private const uint PassAccumulate=2;
    private const uint PassAdapt=3;
    internal const D3D12_RESOURCE_STATES ShaderResourceState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE|D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private ID3D12Resource*State;
    private ID3D12Resource*Bins;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private uint DescriptorSize;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private bool Disposed;
    internal StageAutoExposure(GraphicsDevice Device,string StageArtifactPath,uint FrameCount){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
        this.State=this.CreateBuffer(StateBytes,ShaderResourceState,"creating the auto exposure state");
    }
    /// <summary>float4 露出状態の GPU アドレス。構築後から有効で、フレーム間にシェーダーから読み取り可能。</summary>
    internal ulong StateAddress=>this.State->GetGPUVirtualAddress();
    /// <summary>状態バッファ。パス間は <see cref="ShaderResourceState"/> にする。M0111 では露出値をシーンカラー履歴と同様にコピーする。</summary>
    internal ID3D12Resource*StateResource=>this.State;
    internal static float BinWidth=>(MaxLog2Luminance-MinLog2Luminance)/BinCount;
    /// <summary>Stage プログラムの t15 用に、状態を1つの float4 として構造化バッファビューへ書き込む。</summary>
    internal void WriteShadingDescriptor(D3D12_CPU_DESCRIPTOR_HANDLE Handle){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){
            Format=DXGI_FORMAT_UNKNOWN,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Srv.Buffer.NumElements=1;
        Srv.Buffer.StructureByteStride=StateBytes;
        this.Device.Device->CreateShaderResourceView(this.State,&Srv,Handle);
    }
    /// <summary>フレーム中に初めて状態を読み取る前に、最小 EV100 から測定を再開する。</summary>
    internal void RecordInitialize(ID3D12GraphicsCommandList*CommandList,in ExposureSettings Settings){
        this.EnsurePipeline();
        D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(this.State,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToUav);
        this.Bind(CommandList);
        SetConstants(CommandList,PassInitialize,0,0,Settings,0f);
        CommandList->Dispatch(1,1,1);
        D3D12_RESOURCE_BARRIER ToRead=D3D12_RESOURCE_BARRIER.InitTransition(this.State,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(1,&ToRead);
    }
    /// <summary>このフレームで使ったスケールを使って HDR 描画先（RENDER_TARGET）を区分けし、後で状態を復元して次のフレーム向けに適応させる。コンピュートルートシグネチャ、パイプライン、ディスクリプターヒープを置き換える。</summary>
    internal void RecordMetering(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,ID3D12Resource*SceneColor,int Width,int Height,in ExposureSettings Settings,float DeltaSeconds){
        this.EnsurePipeline();
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){
            Format=DXGI_FORMAT_R16G16B16A16_FLOAT,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Srv.Texture2D.MipLevels=1;
        // フレームスロットごとにディスクリプターを1つ使う。このスロットの前回の GPU 使用は完了している。
        this.Device.Device->CreateShaderResourceView(SceneColor,&Srv,new D3D12_CPU_DESCRIPTOR_HANDLE(this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart(),(int)FrameIndex,this.DescriptorSize));
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(SceneColor,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.State,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(2,Before);
        this.Bind(CommandList);
        CommandList->SetComputeRootDescriptorTable(1,new D3D12_GPU_DESCRIPTOR_HANDLE(this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart(),(int)FrameIndex,this.DescriptorSize));
        SetConstants(CommandList,PassClear,Width,Height,Settings,DeltaSeconds);
        CommandList->Dispatch(4,1,1);
        D3D12_RESOURCE_BARRIER BinsWritten=D3D12_RESOURCE_BARRIER.InitUAV(this.Bins);
        CommandList->ResourceBarrier(1,&BinsWritten);
        SetConstants(CommandList,PassAccumulate,Width,Height,Settings,DeltaSeconds);
        CommandList->Dispatch(((uint)Width+15u)/16u,((uint)Height+15u)/16u,1);
        CommandList->ResourceBarrier(1,&BinsWritten);
        SetConstants(CommandList,PassAdapt,Width,Height,Settings,DeltaSeconds);
        CommandList->Dispatch(1,1,1);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(SceneColor,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.State,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(2,After);
    }
    private void Bind(ID3D12GraphicsCommandList*CommandList){
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        CommandList->SetComputeRootUnorderedAccessView(2,this.Bins->GetGPUVirtualAddress());
        CommandList->SetComputeRootUnorderedAccessView(3,this.State->GetGPUVirtualAddress());
    }
    private static void SetConstants(ID3D12GraphicsCommandList*CommandList,uint Pass,int Width,int Height,in ExposureSettings Settings,float DeltaSeconds){
        uint*Values=stackalloc uint[16];
        var Floats=(float*)Values;
        Values[0]=(uint)Width;
        Values[1]=(uint)Height;
        Values[2]=Pass;
        Values[3]=0;
        Floats[4]=MinLog2Luminance;
        Floats[5]=1f/BinWidth;
        Floats[6]=DeltaSeconds;
        Floats[7]=Settings.Compensation;
        Floats[8]=Settings.Ev100;
        Floats[9]=Settings.MaxEv100;
        Floats[10]=Settings.SpeedUp;
        Floats[11]=Settings.SpeedDown;
        Floats[12]=LowPercent;
        Floats[13]=HighPercent;
        Floats[14]=0f;
        Floats[15]=0f;
        CommandList->SetComputeRoot32BitConstants(0,16,Values,0);
    }
    /// <summary>状態（EV100、スケール、収束済み、有効）をテスト専用にコピーする。キューがアイドルであることは呼び出し側が保証する。</summary>
    internal Vector4 ReadStateForTesting(){
        ID3D12CommandAllocator*Allocator=null;
        ID3D12GraphicsCommandList*List=null;
        ID3D12Resource*Readback=null;
        ID3D12Fence*Fence=null;
        HANDLE Event=HANDLE.NULL;
        try {
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),"CreateCommandAllocator","exposure readback");
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&List),"CreateCommandList","exposure readback");
            D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_READBACK);
            var Desc=D3D12_RESOURCE_DESC.Buffer(StateBytes);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_COPY_DEST,null,__uuidof<ID3D12Resource>(),(void**)&Readback),"CreateCommittedResource","exposure readback");
            D3D12_RESOURCE_BARRIER ToCopy=D3D12_RESOURCE_BARRIER.InitTransition(this.State,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            List->ResourceBarrier(1,&ToCopy);
            List->CopyBufferRegion(Readback,0,this.State,0,StateBytes);
            D3D12_RESOURCE_BARRIER Back=D3D12_RESOURCE_BARRIER.InitTransition(this.State,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
            List->ResourceBarrier(1,&Back);
            D3D12Diagnostics.EnsureSuccess(List->Close(),"Close","exposure readback");
            ID3D12CommandList*Base=(ID3D12CommandList*)List;
            this.Device.CommandQueue->ExecuteCommandLists(1,&Base);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateFence(0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),"CreateFence","exposure readback");
            Event=CreateEventW(null,FALSE,FALSE,null);
            D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->Signal(Fence,1),"Signal","exposure readback");
            if(Fence->GetCompletedValue()<1){
                D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(1,Event),"SetEventOnCompletion","exposure readback");
                _=WaitForSingleObjectEx(Event,INFINITE,FALSE);
            }
            Vector4*Mapped;
            D3D12_RANGE Range=new(){ Begin=0,End=StateBytes };
            D3D12Diagnostics.EnsureSuccess(Readback->Map(0,&Range,(void**)&Mapped),"Map","exposure readback");
            var Result=*Mapped;
            D3D12_RANGE NoWrite=new();
            Readback->Unmap(0,&NoWrite);
            return Result;
        } finally {
            if(Event!=HANDLE.NULL){ _=CloseHandle(Event); }
            if(Fence!=null){ _=Fence->Release(); }
            if(Readback!=null){ _=Readback->Release(); }
            if(List!=null){ _=List->Release(); }
            if(Allocator!=null){ _=Allocator->Release(); }
        }
    }
    private void EnsurePipeline(){
        if(this.PipelineState!=null){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"Histogram auto exposure requires '{this.ArtifactPath}': {Error}");
        }
        var Bytecode=Package.GetBytecode("CSMain");
        this.RootSignature=this.CreateRootSignature();
        fixed(byte*Code=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Desc=new(){
                pRootSignature=this.RootSignature,
                CS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Code,BytecodeLength=(nuint)Bytecode.Length },
            };
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateComputePipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                "ID3D12Device::CreateComputePipelineState","creating the auto exposure pipeline");
            this.PipelineState=Pipeline;
        }
        D3D12_DESCRIPTOR_HEAP_DESC HeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=this.FrameCount,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
        ID3D12DescriptorHeap*DescriptorHeap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&HeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&DescriptorHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the auto exposure descriptor heap");
        this.DescriptorHeap=DescriptorHeap;
        this.DescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        this.Bins=this.CreateBuffer(BinCount*sizeof(uint),D3D12_RESOURCE_STATE_UNORDERED_ACCESS,"creating the luminance histogram bins");
    }
    private ID3D12Resource*CreateBuffer(ulong Size,D3D12_RESOURCE_STATES State,string Context){
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Size,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,State,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource",Context);
        return Resource;
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE SrvRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[4];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,Num32BitValues=16 };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[3]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[3].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=4,pParameters=Parameters };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the auto exposure root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the auto exposure root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        if(this.State!=null){ _=this.State->Release(); }
        if(this.Bins!=null){ _=this.Bins->Release(); }
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release(); }
    }
}
