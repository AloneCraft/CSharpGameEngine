using System.Numerics;
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
/// Screen-space ambient occlusion (ADR-0069). The shared camera surface pass supplies physical
/// distance; <see cref="RecordCompute"/> runs AmbientOcclusion.hlsl at half resolution
/// three times (obscurance, horizontal and vertical depth-aware blur) and leaves the result
/// readable by the Stage program at t14. Compute root signature (host-owned): 0 = root CBV b0 AoCB,
/// 1 = SRV table t0-t1 (physical scene distance, blur source), 2 = UAV table u0 (pass output).
/// </summary>
internal sealed unsafe class StageAmbientOcclusion:IDisposable {
    internal const string ArtifactFileName="AmbientOcclusion.cgeshaderartifact";
    internal const float BiasMeters=0.02f;
    private const uint PassCount=3;
    private const uint ConstantsStride=256;
    private const DXGI_FORMAT OcclusionFormat=DXGI_FORMAT_R16_FLOAT;
    // Descriptor layout: two (distance, source) SRV pairs so no pass binds its own output as an SRV.
    private const uint SrvDistanceWithA=0;
    private const uint SrvDistanceWithB=2;
    private const uint UavA=4;
    private const uint UavB=5;
    private const uint DescriptorCount=6;
    private const D3D12_RESOURCE_STATES ShaderResourceState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE|D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private ID3D12Resource*DescribedSceneDistance;
    private int DescribedWidth;
    private int DescribedHeight;
    private ID3D12Resource*OcclusionA;
    private ID3D12Resource*OcclusionB;
    private ID3D12Resource*ConstantsBuffer;
    private byte*MappedConstants;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private uint DescriptorSize;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private bool Disposed;
    internal StageAmbientOcclusion(GraphicsDevice Device,string StageArtifactPath,uint FrameCount){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
    }
    internal int Width { get;private set; }
    internal int Height { get;private set; }
    /// <summary>Incremented whenever the occlusion texture is (re)created, so the Stage t14 view can be refreshed.</summary>
    internal int Generation { get;private set; }
    internal ID3D12Resource*Result=>this.OcclusionA;
    /// <summary>Half of the screen size, at least one pixel.</summary>
    internal static (int Width,int Height) ResultSize(int ScreenWidth,int ScreenHeight)=>(Math.Max(ScreenWidth/2,1),Math.Max(ScreenHeight/2,1));
    /// <summary>Creates or resizes the targets. The caller guarantees the GPU no longer uses the old ones.</summary>
    internal void EnsureSize(int ScreenWidth,int ScreenHeight,ID3D12Resource*SceneDistance){
        var (Width,Height)=ResultSize(ScreenWidth,ScreenHeight);
        if(this.OcclusionA!=null&&this.Width==Width&&this.Height==Height&&this.DescribedSceneDistance==SceneDistance&&this.DescribedWidth==ScreenWidth&&this.DescribedHeight==ScreenHeight){
            return;
        }
        this.ReleaseTargets();
        this.EnsureHeaps();
        this.Width=Width;
        this.Height=Height;
        this.DescribedSceneDistance=SceneDistance;
        this.DescribedWidth=ScreenWidth;
        this.DescribedHeight=ScreenHeight;
        this.OcclusionA=this.CreateTexture(OcclusionFormat,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,ShaderResourceState,null);
        this.OcclusionB=this.CreateTexture(OcclusionFormat,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,ShaderResourceState,null);
        this.WriteSrv(SceneDistance,DXGI_FORMAT_R32_FLOAT,SrvDistanceWithA);
        this.WriteSrv(this.OcclusionA,OcclusionFormat,SrvDistanceWithA+1);
        this.WriteSrv(SceneDistance,DXGI_FORMAT_R32_FLOAT,SrvDistanceWithB);
        this.WriteSrv(this.OcclusionB,OcclusionFormat,SrvDistanceWithB+1);
        D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=OcclusionFormat,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateUnorderedAccessView(this.OcclusionA,null,&Uav,this.CpuHandle(UavA));
        this.Device.Device->CreateUnorderedAccessView(this.OcclusionB,null,&Uav,this.CpuHandle(UavB));
        this.Generation++;
    }
    /// <summary>
    /// Runs obscurance and the two blur passes. Replaces the compute root signature, pipeline and
    /// descriptor heap; leaves the result in a shader-readable state.
    /// </summary>
    internal void RecordCompute(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in Matrix4x4 ViewProjection,Vector3 CameraPosition,in AmbientOcclusionSettings Settings){
        this.EnsurePipeline();
        if(!Matrix4x4.Invert(ViewProjection,out var Inverse)){
            Inverse=Matrix4x4.Identity;
        }
        var PixelsPerMeter=new Vector3(ViewProjection.M12,ViewProjection.M22,ViewProjection.M32).Length()*this.Height*0.5f;
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.DescribedSceneDistance,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.OcclusionA,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(2,Before);
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        this.Dispatch(CommandList,FrameIndex,0,SrvDistanceWithB,UavA,Inverse,ViewProjection,CameraPosition,Settings,PixelsPerMeter);
        D3D12_RESOURCE_BARRIER*AfterObscurance=stackalloc D3D12_RESOURCE_BARRIER[2];
        AfterObscurance[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.OcclusionA,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        AfterObscurance[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.OcclusionB,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(2,AfterObscurance);
        this.Dispatch(CommandList,FrameIndex,1,SrvDistanceWithA,UavB,Inverse,ViewProjection,CameraPosition,Settings,PixelsPerMeter);
        D3D12_RESOURCE_BARRIER*AfterHorizontal=stackalloc D3D12_RESOURCE_BARRIER[2];
        AfterHorizontal[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.OcclusionB,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        AfterHorizontal[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.OcclusionA,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(2,AfterHorizontal);
        this.Dispatch(CommandList,FrameIndex,2,SrvDistanceWithB,UavA,Inverse,ViewProjection,CameraPosition,Settings,PixelsPerMeter);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[3];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.OcclusionA,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.OcclusionB,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,ShaderResourceState);
        After[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.DescribedSceneDistance,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        CommandList->ResourceBarrier(3,After);
    }
    /// <summary>Writes the Stage t14 view; a null view (reading zero) until the occlusion texture exists.</summary>
    internal void WriteShadingDescriptor(D3D12_CPU_DESCRIPTOR_HANDLE Handle){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){
            Format=OcclusionFormat,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Srv.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(this.OcclusionA,&Srv,Handle);
    }
    /// <summary>Test-only copy of the blurred occlusion (row-major, <see cref="Width"/> x <see cref="Height"/>).</summary>
    internal float[]ReadbackForTesting(){
        if(this.OcclusionA==null){
            throw new InvalidOperationException("Ambient occlusion has not rendered a frame yet.");
        }
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device,this.OcclusionA,ShaderResourceState);
        var Result=new float[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                var Row=(Half*)(Bytes+Y*RowPitch);
                for(var X=0;X<Width;X++){
                    Result[Y*Width+X]=(float)Row[X];
                }
            }
        }
        return Result;
    }
    private void Dispatch(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,uint Pass,uint SrvTable,uint UavSlot,in Matrix4x4 Inverse,in Matrix4x4 ViewProjection,Vector3 CameraPosition,in AmbientOcclusionSettings Settings,float PixelsPerMeter){
        var Slot=FrameIndex*PassCount+Pass;
        var Target=this.MappedConstants+Slot*ConstantsStride;
        var InverseCopy=Inverse;
        Buffer.MemoryCopy(&InverseCopy,Target,64,64);
        var Values=(float*)(Target+64);
        Values[0]=ViewProjection.M14;
        Values[1]=ViewProjection.M24;
        Values[2]=ViewProjection.M34;
        Values[3]=ViewProjection.M44;
        Values[4]=CameraPosition.X;
        Values[5]=CameraPosition.Y;
        Values[6]=CameraPosition.Z;
        Values[7]=0f;
        Values[8]=Settings.RadiusMeters;
        Values[9]=BiasMeters;
        Values[10]=1f;
        Values[11]=PixelsPerMeter;
        var Integers=(uint*)(Target+112);
        Integers[0]=Pass;
        Integers[1]=(uint)this.Width;
        Integers[2]=(uint)this.Height;
        Integers[3]=0;
        var Start=this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart();
        CommandList->SetComputeRootConstantBufferView(0,this.ConstantsBuffer->GetGPUVirtualAddress()+Slot*ConstantsStride);
        CommandList->SetComputeRootDescriptorTable(1,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)SrvTable,this.DescriptorSize));
        CommandList->SetComputeRootDescriptorTable(2,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)UavSlot,this.DescriptorSize));
        CommandList->Dispatch(((uint)this.Width+7u)/8u,((uint)this.Height+7u)/8u,1);
    }
    private void EnsurePipeline(){
        if(this.PipelineState!=null){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"Ambient occlusion requires '{this.ArtifactPath}': {Error}");
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
                "ID3D12Device::CreateComputePipelineState","creating the ambient occlusion pipeline");
            this.PipelineState=Pipeline;
        }
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_UPLOAD);
        var BufferDesc=D3D12_RESOURCE_DESC.Buffer((ulong)this.FrameCount*PassCount*ConstantsStride);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating the ambient occlusion constants");
        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(Resource->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the ambient occlusion constants");
        this.ConstantsBuffer=Resource;
        this.MappedConstants=Mapped;
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE SrvRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=2,BaseShaderRegister=0 };
        D3D12_DESCRIPTOR_RANGE UavRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[3];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&UavRange };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=3,pParameters=Parameters };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the ambient occlusion root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the ambient occlusion root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    private void EnsureHeaps(){
        if(this.DescriptorHeap!=null){
            return;
        }
        D3D12_DESCRIPTOR_HEAP_DESC ShaderDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=DescriptorCount,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
        ID3D12DescriptorHeap*ShaderHeap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&ShaderDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&ShaderHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the ambient occlusion descriptor heap");
        this.DescriptorHeap=ShaderHeap;
        this.DescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    }
    private ID3D12Resource*CreateTexture(DXGI_FORMAT Format,D3D12_RESOURCE_FLAGS Flags,D3D12_RESOURCE_STATES State,D3D12_CLEAR_VALUE*Clear){
        var Desc=D3D12_RESOURCE_DESC.Tex2D(Format,(ulong)this.Width,(uint)this.Height,1,1);
        Desc.Flags|=Flags;
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,State,Clear,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating an ambient occlusion texture");
        return Resource;
    }
    private void WriteSrv(ID3D12Resource*Resource,DXGI_FORMAT Format,uint Slot){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){
            Format=Format,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Srv.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(Slot));
    }
    private D3D12_CPU_DESCRIPTOR_HANDLE CpuHandle(uint Slot)=>new(this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart(),(int)Slot,this.DescriptorSize);
    private void ReleaseTargets(){
        this.DescribedSceneDistance=null;
        if(this.OcclusionA!=null){ _=this.OcclusionA->Release();this.OcclusionA=null; }
        if(this.OcclusionB!=null){ _=this.OcclusionB->Release();this.OcclusionB=null; }
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        this.ReleaseTargets();
        if(this.ConstantsBuffer!=null){
            this.ConstantsBuffer->Unmap(0,null);
            _=this.ConstantsBuffer->Release();
        }
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release(); }
    }
}
