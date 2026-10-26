using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

internal readonly record struct D3D12ComputeReadbackResult(
    float[]Values,string AdapterDescription,int ErrorOrWorseMessageCount);

/// <summary>非公開 L0 の WARP / ハードウェア共通コンピュートプリミティブ。ネイティブ型はこの境界を越えない。</summary>
internal static unsafe class D3D12ComputeReadback {
    private const uint ThreadGroupSize=64;
    private const int MaximumElementCount=1_048_576;

    internal static D3D12ComputeReadbackResult Execute(
        ReadOnlySpan<float>Input,string ShaderArtifactPath,bool UseWarp){
        if(Input.IsEmpty||Input.Length>MaximumElementCount){
            throw new ArgumentOutOfRangeException(nameof(Input));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(ShaderArtifactPath);
        if(!ShaderArtifactRuntime.TryLoad(
            ShaderArtifactPath,ShaderArtifactTargetKind.Compute,
            out ShaderArtifactPackage?Package,out string Error)||Package is null){
            throw new InvalidDataException($"Compute shader artifact is invalid: {Error}");
        }
        byte[]Bytecode=Package.GetBytecode("CSMain");
        using GraphicsDevice Device=GraphicsDevice.Create(new GraphicsDeviceOptions {
            UseWarp=UseWarp,
            EnableDebugLayer=true,
        });
        ID3D12RootSignature*RootSignature=null;
        ID3D12PipelineState*PipelineState=null;
        ID3D12CommandAllocator*Allocator=null;
        ID3D12GraphicsCommandList*CommandList=null;
        ID3D12Resource*InputBuffer=null;
        ID3D12Resource*OutputBuffer=null;
        ID3D12Resource*ReadbackBuffer=null;
        ID3D12Fence*Fence=null;
        HANDLE FenceEvent=HANDLE.NULL;
        try {
            RootSignature=CreateRootSignature(Device);
            PipelineState=CreatePipelineState(Device,RootSignature,Bytecode);
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateCommandAllocator(
                    D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),
                "ID3D12Device::CreateCommandAllocator","creating the compute allocator");
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateCommandList(
                    0,D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,PipelineState,
                    __uuidof<ID3D12GraphicsCommandList>(),(void**)&CommandList),
                "ID3D12Device::CreateCommandList","creating the compute command list");

            ulong ByteSize=checked((ulong)Input.Length*(ulong)sizeof(float));
            InputBuffer=CreateBuffer(
                Device,ByteSize,D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
                D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE);
            OutputBuffer=CreateBuffer(
                Device,ByteSize,D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
            ReadbackBuffer=CreateBuffer(
                Device,ByteSize,D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_READBACK,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE);
            CopyToUploadBuffer(InputBuffer,Input);

            CommandList->SetComputeRootSignature(RootSignature);
            CommandList->SetPipelineState(PipelineState);
            CommandList->SetComputeRootShaderResourceView(0,InputBuffer->GetGPUVirtualAddress());
            CommandList->SetComputeRootUnorderedAccessView(1,OutputBuffer->GetGPUVirtualAddress());
            CommandList->Dispatch(
                checked((uint)(Input.Length+(int)ThreadGroupSize-1))/ThreadGroupSize,1,1);
            D3D12_RESOURCE_BARRIER ToCopy=D3D12_RESOURCE_BARRIER.InitTransition(
                OutputBuffer,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            CommandList->ResourceBarrier(1,&ToCopy);
            CommandList->CopyBufferRegion(ReadbackBuffer,0,OutputBuffer,0,ByteSize);
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateFence(
                    0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,
                    __uuidof<ID3D12Fence>(),(void**)&Fence),
                "ID3D12Device::CreateFence","creating the compute fence");
            FenceEvent=CreateEventW(null,FALSE,FALSE,null);
            if(FenceEvent==HANDLE.NULL){
                throw new InvalidOperationException("CreateEventW failed for the compute fence.");
            }
            D3D12Diagnostics.EnsureSuccess(
                CommandList->Close(),
                "ID3D12GraphicsCommandList::Close","closing the compute command list");
            ID3D12CommandList*BaseCommandList=(ID3D12CommandList*)CommandList;
            Device.CommandQueue->ExecuteCommandLists(1,&BaseCommandList);

            D3D12Diagnostics.EnsureSuccess(
                Device.CommandQueue->Signal(Fence,1),
                "ID3D12CommandQueue::Signal","signaling compute completion");
            if(Fence->GetCompletedValue()<1){
                D3D12Diagnostics.EnsureSuccess(
                    Fence->SetEventOnCompletion(1,FenceEvent),
                    "ID3D12Fence::SetEventOnCompletion","waiting for compute completion");
                _=WaitForSingleObjectEx(FenceEvent,INFINITE,FALSE);
            }

            float[]Values=ReadBack(ReadbackBuffer,Input.Length,ByteSize);
            Device.PollDebugMessages();
            return new D3D12ComputeReadbackResult(
                Values,Device.AdapterDescription,Device.ErrorOrWorseMessageCount);
        }
        finally {
            if(FenceEvent!=HANDLE.NULL){ _=CloseHandle(FenceEvent); }
            if(Fence!=null){ _=Fence->Release(); }
            if(ReadbackBuffer!=null){ _=ReadbackBuffer->Release(); }
            if(OutputBuffer!=null){ _=OutputBuffer->Release(); }
            if(InputBuffer!=null){ _=InputBuffer->Release(); }
            if(CommandList!=null){ _=CommandList->Release(); }
            if(Allocator!=null){ _=Allocator->Release(); }
            if(PipelineState!=null){ _=PipelineState->Release(); }
            if(RootSignature!=null){ _=RootSignature->Release(); }
        }
    }

    private static ID3D12RootSignature*CreateRootSignature(GraphicsDevice Device){
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[2];
        Parameters[0]=new D3D12_ROOT_PARAMETER {
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        Parameters[0].Descriptor=new D3D12_ROOT_DESCRIPTOR {
            ShaderRegister=0,
            RegisterSpace=0,
        };
        Parameters[1]=new D3D12_ROOT_PARAMETER {
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        Parameters[1].Descriptor=new D3D12_ROOT_DESCRIPTOR {
            ShaderRegister=0,
            RegisterSpace=0,
        };
        D3D12_ROOT_SIGNATURE_DESC Description=new() {
            NumParameters=2,
            pParameters=Parameters,
        };
        ID3DBlob*Signature=null;
        ID3DBlob*Errors=null;
        try {
            HRESULT Result=D3D12SerializeRootSignature(
                &Description,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,
                &Signature,&Errors);
            if(Result.FAILED){
                string Detail=Errors!=null
                    ?new string((sbyte*)Errors->GetBufferPointer())
                    :"(no error blob)";
                throw new D3D12Exception(
                    "D3D12SerializeRootSignature",Result.Value,
                    "serializing the compute root signature",Detail);
            }
            ID3D12RootSignature*RootSignature;
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateRootSignature(
                    0,Signature->GetBufferPointer(),Signature->GetBufferSize(),
                    __uuidof<ID3D12RootSignature>(),(void**)&RootSignature),
                "ID3D12Device::CreateRootSignature","creating the compute root signature");
            return RootSignature;
        }
        finally {
            if(Errors!=null){ _=Errors->Release(); }
            if(Signature!=null){ _=Signature->Release(); }
        }
    }

    private static ID3D12PipelineState*CreatePipelineState(
        GraphicsDevice Device,ID3D12RootSignature*RootSignature,byte[]Bytecode){
        fixed(byte*Shader=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Description=new() {
                pRootSignature=RootSignature,
                CS=new D3D12_SHADER_BYTECODE {
                    pShaderBytecode=Shader,
                    BytecodeLength=(nuint)Bytecode.Length,
                },
            };
            ID3D12PipelineState*PipelineState;
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateComputePipelineState(
                    &Description,__uuidof<ID3D12PipelineState>(),(void**)&PipelineState),
                "ID3D12Device::CreateComputePipelineState","creating the compute pipeline");
            return PipelineState;
        }
    }

    private static ID3D12Resource*CreateBuffer(
        GraphicsDevice Device,ulong ByteSize,D3D12_HEAP_TYPE HeapType,
        D3D12_RESOURCE_STATES InitialState,D3D12_RESOURCE_FLAGS Flags){
        D3D12_HEAP_PROPERTIES HeapProperties=new(HeapType);
        D3D12_RESOURCE_DESC Description=D3D12_RESOURCE_DESC.Buffer(ByteSize);
        Description.Flags=Flags;
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateCommittedResource(
                &HeapProperties,D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                &Description,InitialState,null,
                __uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a compute buffer");
        return Resource;
    }

    private static void CopyToUploadBuffer(
        ID3D12Resource*Resource,ReadOnlySpan<float>Values){
        D3D12_RANGE NoRead=new();
        void*Mapped;
        D3D12Diagnostics.EnsureSuccess(
            Resource->Map(0,&NoRead,&Mapped),
            "ID3D12Resource::Map","mapping compute input");
        try {
            Values.CopyTo(new Span<float>(Mapped,Values.Length));
        }
        finally {
            Resource->Unmap(0,null);
        }
    }

    private static float[]ReadBack(
        ID3D12Resource*Resource,int Count,ulong ByteSize){
        D3D12_RANGE ReadRange=new() { Begin=0,End=(nuint)ByteSize };
        void*Mapped;
        D3D12Diagnostics.EnsureSuccess(
            Resource->Map(0,&ReadRange,&Mapped),
            "ID3D12Resource::Map","mapping compute readback");
        try {
            float[]Result=new float[Count];
            new ReadOnlySpan<float>(Mapped,Count).CopyTo(Result);
            return Result;
        }
        finally {
            D3D12_RANGE NoWrite=new();
            Resource->Unmap(0,&NoWrite);
        }
    }
}
