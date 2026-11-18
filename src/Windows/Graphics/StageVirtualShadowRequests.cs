using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
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
/// 仮想シャドウのページの要求（ADR-0100 決定 5、M0132 単位 2）。主パスの後、距離の画像から VirtualShadowRequest.hlsl が段とページの印を付け、
/// 印を読み戻しの輪へ写す。描画器はその枠が回ってきたとき（数フレーム後）に読む。
/// 計算のルートシグネチャ（ここが持つ）: 0 = 定数バッファ b0、1 = 記述子の表 t0（距離の画像）、2 = u0 印。
/// </summary>
internal sealed unsafe class StageVirtualShadowRequests:IDisposable {
    internal const string ArtifactFileName="VirtualShadowRequest.cgeshaderartifact";
    /// <summary>印の数（段 × 128 × 128 ビットを 32 ビットずつ）。</summary>
    internal const int WordCount=VirtualShadowMapPlanner.TableLength/32;
    private const uint ConstantBytes=(16+16*16+4+4)*sizeof(float);
    private const uint ConstantStride=(ConstantBytes+255u)&~255u;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private ID3D12Resource*DescribedSceneDistance;
    private ID3D12Resource*Constants;
    private byte*MappedConstants;
    private ID3D12Resource*Bits;
    private ID3D12Resource*Zeros;
    private D3D12_RESOURCE_STATES BitsState=D3D12_RESOURCE_STATE_COMMON;
    private StageFeedbackReadback?Readback;
    private uint LastFrameIndex;
    internal StageVirtualShadowRequests(GraphicsDevice Device,string StageArtifactPath,uint FrameCount){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
    }
    /// <summary>
    /// 印を付けて読み戻しの輪の枠 <paramref name="FrameIndex"/> へ写す。距離の画像は描画先の状態で受け取り、描画先の状態で返す。
    /// 計算のルートシグネチャ・PSO・記述子の置き場を置き換える。
    /// </summary>
    internal void Record(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,ID3D12Resource*SceneDistance,int Width,int Height,in Matrix4x4 InverseViewProjection,
        ReadOnlySpan<Matrix4x4>LevelProjections,Vector3 CameraPosition,float PixelsPerMeterAtOneMeter){
        this.EnsureResources();
        if(this.DescribedSceneDistance!=SceneDistance){
            D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=DXGI_FORMAT_R32_FLOAT,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
                Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
            Srv.Texture2D.MipLevels=1;
            this.Device.Device->CreateShaderResourceView(SceneDistance,&Srv,this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart());
            this.DescribedSceneDistance=SceneDistance;
        }
        var Slot=this.MappedConstants+FrameIndex*ConstantStride;
        var Inverse=InverseViewProjection;
        Unsafe.CopyBlock(Slot,&Inverse,64);
        fixed(Matrix4x4*Levels=LevelProjections){ Unsafe.CopyBlock(Slot+64,Levels,64u*VirtualShadowMapPlanner.LevelCount); }
        *(Vector4*)(Slot+64+64*VirtualShadowMapPlanner.LevelCount)=new(CameraPosition,0f);
        *(Vector4*)(Slot+64+64*VirtualShadowMapPlanner.LevelCount+16)=new(Width,Height,PixelsPerMeterAtOneMeter,0f);
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(SceneDistance,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.Bits,this.BitsState,D3D12_RESOURCE_STATE_COPY_DEST);
        CommandList->ResourceBarrier(2,Before);
        CommandList->CopyBufferRegion(this.Bits,0,this.Zeros,0,WordCount*sizeof(uint));
        var ToWrite=D3D12_RESOURCE_BARRIER.InitTransition(this.Bits,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToWrite);
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        CommandList->SetComputeRootConstantBufferView(0,this.Constants->GetGPUVirtualAddress()+FrameIndex*ConstantStride);
        CommandList->SetComputeRootDescriptorTable(1,this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart());
        CommandList->SetComputeRootUnorderedAccessView(2,this.Bits->GetGPUVirtualAddress());
        CommandList->Dispatch(((uint)Width+7u)/8u,((uint)Height+7u)/8u,1);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(SceneDistance,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.Bits,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_COPY_SOURCE);
        CommandList->ResourceBarrier(2,After);
        this.BitsState=D3D12_RESOURCE_STATE_COPY_SOURCE;
        CommandList->CopyBufferRegion(this.Readback!.Resource,this.Readback.SlotOffset(FrameIndex),this.Bits,0,WordCount*sizeof(uint));
        this.Readback.MarkWritten(FrameIndex);
        this.LastFrameIndex=FrameIndex;
    }
    /// <summary>枠 <paramref name="FrameIndex"/> へ前に写した印を読む（その枠のフェンスが済んでいること）。まだ無ければ false。</summary>
    internal bool TryRead(uint FrameIndex,Span<uint>Destination)=>this.Readback is not null&&this.Readback.TryRead(FrameIndex,Destination);
    /// <summary>試験用: 最後に付けた印（読み戻しの輪の、そのフレームの枠から読む）。GPU が止まっていることは呼び出し側が保証する。</summary>
    internal uint[]ReadLatestForTesting(){
        var Result=new uint[WordCount];
        if(!this.TryRead(this.LastFrameIndex,Result)){ throw new InvalidOperationException("No virtual shadow request has been recorded."); }
        return Result;
    }
    private void EnsureResources(){
        if(this.PipelineState!=null){ return; }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"Virtual shadow maps require '{this.ArtifactPath}': {Error}");
        }
        var Bytecode=Package.GetBytecode("CSMain");
        this.RootSignature=this.CreateRootSignature();
        fixed(byte*Code=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Desc=new(){ pRootSignature=this.RootSignature,CS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Code,BytecodeLength=(nuint)Bytecode.Length } };
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateComputePipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                "ID3D12Device::CreateComputePipelineState","creating the virtual shadow request pipeline");
            this.PipelineState=Pipeline;
        }
        D3D12_DESCRIPTOR_HEAP_DESC HeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=1,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&HeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the virtual shadow request descriptor heap");
        this.DescriptorHeap=Heap;
        this.Constants=this.CreateBuffer(D3D12_HEAP_TYPE_UPLOAD,(ulong)ConstantStride*this.FrameCount,D3D12_RESOURCE_FLAG_NONE,D3D12_RESOURCE_STATE_GENERIC_READ,"virtual shadow request constants");
        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(this.Constants->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the virtual shadow request constants");
        this.MappedConstants=Mapped;
        this.Bits=this.CreateBuffer(D3D12_HEAP_TYPE_DEFAULT,WordCount*sizeof(uint),D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_COMMON,"virtual shadow request bits");
        this.Zeros=this.CreateBuffer(D3D12_HEAP_TYPE_UPLOAD,WordCount*sizeof(uint),D3D12_RESOURCE_FLAG_NONE,D3D12_RESOURCE_STATE_GENERIC_READ,"virtual shadow request clear");
        byte*Clear;
        D3D12Diagnostics.EnsureSuccess(this.Zeros->Map(0,&NoRead,(void**)&Clear),"ID3D12Resource::Map","clearing the virtual shadow request source");
        Unsafe.InitBlock(Clear,0,WordCount*sizeof(uint));
        this.Zeros->Unmap(0,null);
        this.Readback=new StageFeedbackReadback(this.Device,this.FrameCount,WordCount*sizeof(uint),"virtual shadow requests");
    }
    private ID3D12Resource*CreateBuffer(D3D12_HEAP_TYPE Type,ulong Size,D3D12_RESOURCE_FLAGS Flags,D3D12_RESOURCE_STATES State,string Context){
        D3D12_HEAP_PROPERTIES Heap=new(Type);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Size,Flags);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,State,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource",$"creating the {Context}");
        return Resource;
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE Range=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[3];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&Range };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=3,pParameters=Parameters };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the virtual shadow request root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the virtual shadow request root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    public void Dispose(){
        if(this.Constants!=null){ this.Constants->Unmap(0,null);_=this.Constants->Release();this.Constants=null; }
        if(this.Bits!=null){ _=this.Bits->Release();this.Bits=null; }
        if(this.Zeros!=null){ _=this.Zeros->Release();this.Zeros=null; }
        if(this.PipelineState!=null){ _=this.PipelineState->Release();this.PipelineState=null; }
        if(this.RootSignature!=null){ _=this.RootSignature->Release();this.RootSignature=null; }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release();this.DescriptorHeap=null; }
        this.Readback?.Dispose();
        this.Readback=null;
    }
}
