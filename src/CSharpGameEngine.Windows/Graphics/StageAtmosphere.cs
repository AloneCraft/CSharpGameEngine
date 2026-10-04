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
/// Owns the physically based sky (ADR-0069): Transmittance / MultiScattering / SkyView LUTs, the
/// sky light's irradiance SH and the sky background composite. Every pass is one dispatch of the
/// single Atmosphere.hlsl compute artifact. Root signature (host-owned, see ShaderArtifactAbi):
/// 0 = root CBV b0 AtmosphereCB, 1 = SRV table t0-t3 (transmittance, multi-scattering, sky view,
/// scene depth), 2 = UAV table u0 (the pass output texture), 3 = root UAV u1 (irradiance SH),
/// 4 = root SRV t4 (auto exposure state, read by the sky composite in histogram mode), s0 = linear clamp.
/// </summary>
internal sealed unsafe class StageAtmosphere:IDisposable {
    internal const string ArtifactFileName="Atmosphere.cgeshaderartifact";
    internal const int ShCoefficientCount=9;
    private const uint PassTransmittance=0;
    private const uint PassMultiScattering=1;
    private const uint PassSkyView=2;
    private const uint PassSkyLightSh=3;
    private const uint PassSkyBackground=4;
    private const uint PassCount=5;
    private const uint ConstantsStride=256;
    private const int TransmittanceWidth=256;
    private const int TransmittanceHeight=64;
    private const int MultiScatteringSize=32;
    private const int SkyViewWidth=192;
    private const int SkyViewHeight=108;
    private const DXGI_FORMAT LutFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const uint SrvTransmittance=0;
    private const uint SrvMultiScattering=1;
    private const uint SrvSkyView=2;
    private const uint SrvDepth=3;
    private const uint UavTransmittance=4;
    private const uint UavMultiScattering=5;
    private const uint UavSkyView=6;
    private const uint UavSceneColor=7;
    private const uint DescriptorCount=8;
    private const D3D12_RESOURCE_STATES ShaderResourceState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE|D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private ID3D12Resource*TransmittanceLut;
    private ID3D12Resource*MultiScatteringLut;
    private ID3D12Resource*SkyViewLut;
    private ID3D12Resource*ShBuffer;
    private ID3D12Resource*ConstantsBuffer;
    private byte*MappedConstants;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private uint DescriptorSize;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private SkyAtmosphereSettings?LutSettings;
    private SkyViewKey?SkyViewState;
    private nint DescribedDepth;
    private nint DescribedSceneColor;
    private bool Disposed;
    internal StageAtmosphere(GraphicsDevice Device,string StageArtifactPath,uint FrameCount){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
        this.ShBuffer=this.CreateBuffer(ShCoefficientCount*16,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,ShaderResourceState,"creating the sky light SH buffer");
    }
    /// <summary>GPU address of the 9 float4 irradiance SH coefficients (Stage t11).</summary>
    internal ulong ShBufferAddress=>this.ShBuffer->GetGPUVirtualAddress();
    internal bool HasValidSkyLight { get;private set; }
    /// <summary>GPU address of StageAutoExposure's state; always bound so every root parameter is set.</summary>
    internal ulong ExposureStateAddress { get;set; }
    internal Vector3 LastSunDirection { get;private set; }
    /// <summary>Records the LUT and sky light passes. Leaves every LUT and the SH buffer readable by shaders.</summary>
    internal void Update(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in AtmosphereFrame Frame){
        this.EnsurePipeline();
        this.EnsureLuts();
        var Constants=Frame;
        var SettingsChanged=this.LutSettings!=Frame.Atmosphere;
        if(SettingsChanged){
            this.Dispatch(CommandList,FrameIndex,PassTransmittance,Constants,this.TransmittanceLut,UavTransmittance,TransmittanceWidth,TransmittanceHeight);
            this.Dispatch(CommandList,FrameIndex,PassMultiScattering,Constants,this.MultiScatteringLut,UavMultiScattering,MultiScatteringSize,MultiScatteringSize);
            this.LutSettings=Frame.Atmosphere;
            this.SkyViewState=null;
        }
        var Key=new SkyViewKey(Frame.SunDirection,Frame.SunIlluminance,MathF.Round(Frame.CameraAltitudeMeters/25f),Frame.SkyLight);
        if(this.SkyViewState!=Key){
            this.Dispatch(CommandList,FrameIndex,PassSkyView,Constants,this.SkyViewLut,UavSkyView,SkyViewWidth,SkyViewHeight);
            this.DispatchSh(CommandList,FrameIndex,Constants);
            this.SkyViewState=Key;
        }
        this.HasValidSkyLight=true;
        this.LastSunDirection=Frame.SunDirection;
    }
    /// <summary>Writes the sky into every far-plane pixel. Expects depth in DEPTH_WRITE and colour in RENDER_TARGET and restores both.</summary>
    internal void CompositeSky(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in AtmosphereFrame Frame,ID3D12Resource*Depth,ID3D12Resource*SceneColor,int Width,int Height,bool UseExposureState){
        this.EnsurePipeline();
        this.EnsureLuts();
        this.WriteSceneDescriptors(Depth,SceneColor);
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[3];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(Depth,D3D12_RESOURCE_STATE_DEPTH_WRITE,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(SceneColor,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        Before[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(3,Before);
        this.BindAndDispatch(CommandList,FrameIndex,PassSkyBackground,Frame,UavSceneColor,(uint)Width,(uint)Height,UseExposureState);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[3];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(Depth,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(SceneColor,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_RENDER_TARGET);
        After[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(3,After);
    }
    private void Dispatch(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,uint Pass,in AtmosphereFrame Frame,ID3D12Resource*Target,uint UavSlot,int Width,int Height){
        D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(Target,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToUav);
        this.BindAndDispatch(CommandList,FrameIndex,Pass,Frame,UavSlot,(uint)Width,(uint)Height);
        D3D12_RESOURCE_BARRIER ToSrv=D3D12_RESOURCE_BARRIER.InitTransition(Target,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(1,&ToSrv);
    }
    private void DispatchSh(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in AtmosphereFrame Frame){
        D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToUav);
        this.BindAndDispatch(CommandList,FrameIndex,PassSkyLightSh,Frame,UavSkyView,8,8);
        D3D12_RESOURCE_BARRIER ToSrv=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(1,&ToSrv);
    }
    private void BindAndDispatch(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,uint Pass,in AtmosphereFrame Frame,uint UavSlot,uint Width,uint Height,bool UseExposureState=false){
        var Slot=FrameIndex*PassCount+Pass;
        this.WriteConstants(Slot,Pass,Frame,Width,Height,UseExposureState);
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        var Start=this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart();
        CommandList->SetComputeRootConstantBufferView(0,this.ConstantsBuffer->GetGPUVirtualAddress()+Slot*ConstantsStride);
        CommandList->SetComputeRootDescriptorTable(1,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)SrvTransmittance,this.DescriptorSize));
        CommandList->SetComputeRootDescriptorTable(2,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)UavSlot,this.DescriptorSize));
        CommandList->SetComputeRootUnorderedAccessView(3,this.ShBuffer->GetGPUVirtualAddress());
        CommandList->SetComputeRootShaderResourceView(4,this.ExposureStateAddress);
        CommandList->Dispatch(Pass==PassSkyLightSh?1u:(Width+7u)/8u,Pass==PassSkyLightSh?1u:(Height+7u)/8u,1);
    }
    private void WriteConstants(uint Slot,uint Pass,in AtmosphereFrame Frame,uint Width,uint Height,bool UseExposureState){
        var Atmosphere=Frame.Atmosphere;
        var Target=(float*)(this.MappedConstants+Slot*ConstantsStride);
        void Write(int Index,Vector3 Value,float W){
            Target[Index*4]=Value.X;
            Target[Index*4+1]=Value.Y;
            Target[Index*4+2]=Value.Z;
            Target[Index*4+3]=W;
        }
        Write(0,Atmosphere.RayleighScatteringPerKm,Atmosphere.RayleighScaleHeightKm);
        Write(1,Atmosphere.MieScatteringPerKm,Atmosphere.MieScaleHeightKm);
        Write(2,Atmosphere.MieAbsorptionPerKm,Atmosphere.MieAnisotropy);
        Write(3,Atmosphere.OzoneAbsorptionPerKm,Atmosphere.OzoneTipAltitudeKm);
        Write(4,new Vector3(Atmosphere.BottomRadiusKm,Atmosphere.TopRadiusKm,Atmosphere.OzoneWidthKm),Atmosphere.MultiScatteringFactor);
        Write(5,Atmosphere.GroundAlbedo,Atmosphere.BottomRadiusKm+MathF.Max(Frame.CameraAltitudeMeters,1f)*0.001f);
        Write(6,Frame.SunDirection,Frame.SunAngularRadiusRadians);
        Write(7,Frame.SunIlluminance,Frame.SkyLight.Intensity);
        Write(8,Frame.SkyLight.LowerHemisphereRadiance,Frame.ExposureScale);
        var Fog=Frame.Fog;
        Write(9,new Vector3(Fog?.RayOriginTerms(Frame.CameraAltitudeMeters)??0f,Fog?.HeightFalloffPerMeter??0f,1f-(Fog?.MaxOpacity??1f)),Fog is null?0f:1f);
        Write(10,Fog?.InscatteringLuminance??Vector3.Zero,Fog?.SkyAtmosphereContributionScale??0f);
        var Inverse=Frame.InverseViewProjection;
        Buffer.MemoryCopy(&Inverse,Target+44,64,64);
        var Pass4=(uint*)(Target+60);
        Pass4[0]=Pass;
        Pass4[1]=Width;
        Pass4[2]=Height;
        Pass4[3]=UseExposureState?1u:0u;
    }
    private void EnsurePipeline(){
        if(this.PipelineState!=null){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"The sky atmosphere requires '{this.ArtifactPath}': {Error}");
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
                "ID3D12Device::CreateComputePipelineState","creating the sky atmosphere pipeline");
            this.PipelineState=Pipeline;
        }
        this.CreateUploadConstants();
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE SrvRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=4,BaseShaderRegister=0 };
        D3D12_DESCRIPTOR_RANGE UavRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[5];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&UavRange };
        Parameters[3]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[3].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        Parameters[4]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[4].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=4 };
        D3D12_STATIC_SAMPLER_DESC Sampler=new(){
            Filter=D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            ComparisonFunc=D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=5,pParameters=Parameters,NumStaticSamplers=1,pStaticSamplers=&Sampler };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the sky atmosphere root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the sky atmosphere root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    private void CreateUploadConstants(){
        var Size=(ulong)(this.FrameCount*PassCount*ConstantsStride);
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_UPLOAD);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Size);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating the sky atmosphere constants");
        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(Resource->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the sky atmosphere constants");
        this.ConstantsBuffer=Resource;
        this.MappedConstants=Mapped;
    }
    private void EnsureLuts(){
        if(this.DescriptorHeap!=null){
            return;
        }
        D3D12_DESCRIPTOR_HEAP_DESC HeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=DescriptorCount,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
        ID3D12DescriptorHeap*DescriptorHeap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&HeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&DescriptorHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the sky atmosphere descriptor heap");
        this.DescriptorHeap=DescriptorHeap;
        this.DescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        this.TransmittanceLut=this.CreateLut(TransmittanceWidth,TransmittanceHeight,SrvTransmittance,UavTransmittance);
        this.MultiScatteringLut=this.CreateLut(MultiScatteringSize,MultiScatteringSize,SrvMultiScattering,UavMultiScattering);
        this.SkyViewLut=this.CreateLut(SkyViewWidth,SkyViewHeight,SrvSkyView,UavSkyView);
    }
    private ID3D12Resource*CreateLut(int Width,int Height,uint SrvSlot,uint UavSlot){
        var Desc=D3D12_RESOURCE_DESC.Tex2D(LutFormat,(ulong)Width,(uint)Height,mipLevels:1);
        Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,ShaderResourceState,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a sky atmosphere LUT");
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){
            Format=LutFormat,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Srv.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(SrvSlot));
        D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=LutFormat,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateUnorderedAccessView(Resource,null,&Uav,this.CpuHandle(UavSlot));
        return Resource;
    }
    /// <summary>Forces the next composite to rewrite its target views; call after the depth or HDR target is recreated.</summary>
    internal void InvalidateSceneDescriptors(){
        this.DescribedDepth=0;
        this.DescribedSceneColor=0;
    }
    private void WriteSceneDescriptors(ID3D12Resource*Depth,ID3D12Resource*SceneColor){
        // The previous frame's composite may still be reading these shader-visible descriptors, so
        // they are written only when the targets change; Resize recreates them with the GPU idle and
        // calls InvalidateSceneDescriptors, because a new target can reuse the old one's address.
        if(this.DescribedDepth==(nint)Depth&&this.DescribedSceneColor==(nint)SceneColor){
            return;
        }
        this.DescribedDepth=(nint)Depth;
        this.DescribedSceneColor=(nint)SceneColor;
        D3D12_SHADER_RESOURCE_VIEW_DESC DepthSrv=new(){
            Format=DXGI_FORMAT_R32_FLOAT,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        DepthSrv.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(Depth,&DepthSrv,this.CpuHandle(SrvDepth));
        D3D12_UNORDERED_ACCESS_VIEW_DESC ColorUav=new(){ Format=DXGI_FORMAT_R16G16B16A16_FLOAT,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateUnorderedAccessView(SceneColor,null,&ColorUav,this.CpuHandle(UavSceneColor));
    }
    private D3D12_CPU_DESCRIPTOR_HANDLE CpuHandle(uint Slot)=>new(this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart(),(int)Slot,this.DescriptorSize);
    private ID3D12Resource*CreateBuffer(ulong Size,D3D12_RESOURCE_FLAGS Flags,D3D12_RESOURCE_STATES State,string Context){
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Size,Flags);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,State,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource",Context);
        return Resource;
    }
    /// <summary>
    /// Test-only GPU readback of the transmittance LUT, the sky view LUT and the irradiance SH.
    /// The caller guarantees the renderer's queue is idle; the copy runs on its own list and fence.
    /// </summary>
    internal AtmosphereReadback ReadbackForTesting(){
        if(this.TransmittanceLut==null){
            throw new InvalidOperationException("The atmosphere has not rendered a frame yet.");
        }
        ID3D12CommandAllocator*Allocator=null;
        ID3D12GraphicsCommandList*List=null;
        ID3D12Resource*Readback=null;
        ID3D12Fence*Fence=null;
        HANDLE Event=HANDLE.NULL;
        try {
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),"CreateCommandAllocator","atmosphere readback");
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&List),"CreateCommandList","atmosphere readback");
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT TransmittanceFootprint;
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT SkyViewFootprint;
            ulong TransmittanceBytes;
            ulong SkyViewBytes;
            var TransmittanceDesc=this.TransmittanceLut->GetDesc();
            var SkyViewDesc=this.SkyViewLut->GetDesc();
            this.Device.Device->GetCopyableFootprints(&TransmittanceDesc,0,1,0,&TransmittanceFootprint,null,null,&TransmittanceBytes);
            this.Device.Device->GetCopyableFootprints(&SkyViewDesc,0,1,0,&SkyViewFootprint,null,null,&SkyViewBytes);
            var SkyViewOffset=(TransmittanceBytes+511UL)&~511UL;
            var ShOffset=(SkyViewOffset+SkyViewBytes+255UL)&~255UL;
            SkyViewFootprint.Offset=SkyViewOffset;
            var TotalBytes=ShOffset+ShCoefficientCount*16;
            D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_READBACK);
            var BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,null,__uuidof<ID3D12Resource>(),(void**)&Readback),"CreateCommittedResource","atmosphere readback");
            D3D12_RESOURCE_BARRIER*ToCopy=stackalloc D3D12_RESOURCE_BARRIER[3];
            ToCopy[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.TransmittanceLut,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            ToCopy[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SkyViewLut,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            ToCopy[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            List->ResourceBarrier(3,ToCopy);
            D3D12_TEXTURE_COPY_LOCATION Destination=new(){ pResource=Readback,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT,PlacedFootprint=TransmittanceFootprint };
            D3D12_TEXTURE_COPY_LOCATION Source=new(){ pResource=this.TransmittanceLut,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            List->CopyTextureRegion(&Destination,0,0,0,&Source,null);
            Destination.PlacedFootprint=SkyViewFootprint;
            Source.pResource=this.SkyViewLut;
            List->CopyTextureRegion(&Destination,0,0,0,&Source,null);
            List->CopyBufferRegion(Readback,ShOffset,this.ShBuffer,0,ShCoefficientCount*16);
            D3D12_RESOURCE_BARRIER*Back=stackalloc D3D12_RESOURCE_BARRIER[3];
            Back[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.TransmittanceLut,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
            Back[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SkyViewLut,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
            Back[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
            List->ResourceBarrier(3,Back);
            D3D12Diagnostics.EnsureSuccess(List->Close(),"Close","atmosphere readback");
            ID3D12CommandList*Base=(ID3D12CommandList*)List;
            this.Device.CommandQueue->ExecuteCommandLists(1,&Base);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateFence(0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),"CreateFence","atmosphere readback");
            Event=CreateEventW(null,FALSE,FALSE,null);
            D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->Signal(Fence,1),"Signal","atmosphere readback");
            if(Fence->GetCompletedValue()<1){
                D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(1,Event),"SetEventOnCompletion","atmosphere readback");
                _=WaitForSingleObjectEx(Event,INFINITE,FALSE);
            }
            byte*Mapped;
            D3D12_RANGE Range=new(){ Begin=0,End=(nuint)TotalBytes };
            D3D12Diagnostics.EnsureSuccess(Readback->Map(0,&Range,(void**)&Mapped),"Map","atmosphere readback");
            try {
                return new AtmosphereReadback(
                    ReadHalfTexture(Mapped,TransmittanceFootprint,TransmittanceWidth,TransmittanceHeight),TransmittanceWidth,TransmittanceHeight,
                    ReadHalfTexture(Mapped,SkyViewFootprint,SkyViewWidth,SkyViewHeight),SkyViewWidth,SkyViewHeight,
                    ReadSh(Mapped+ShOffset));
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
    private static Vector3[]ReadHalfTexture(byte*Mapped,D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint,int Width,int Height){
        var Result=new Vector3[Width*Height];
        for(var Y=0;Y<Height;Y++){
            var Row=(Half*)(Mapped+Footprint.Offset+(ulong)Y*Footprint.Footprint.RowPitch);
            for(var X=0;X<Width;X++){
                Result[Y*Width+X]=new Vector3((float)Row[X*4],(float)Row[X*4+1],(float)Row[X*4+2]);
            }
        }
        return Result;
    }
    private static Vector3[]ReadSh(byte*Mapped){
        var Values=(float*)Mapped;
        var Result=new Vector3[ShCoefficientCount];
        for(var Index=0;Index<ShCoefficientCount;Index++){
            Result[Index]=new Vector3(Values[Index*4],Values[Index*4+1],Values[Index*4+2]);
        }
        return Result;
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        if(this.ConstantsBuffer!=null){
            this.ConstantsBuffer->Unmap(0,null);
            _=this.ConstantsBuffer->Release();
        }
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release(); }
        if(this.TransmittanceLut!=null){ _=this.TransmittanceLut->Release(); }
        if(this.MultiScatteringLut!=null){ _=this.MultiScatteringLut->Release(); }
        if(this.SkyViewLut!=null){ _=this.SkyViewLut->Release(); }
        if(this.ShBuffer!=null){ _=this.ShBuffer->Release(); }
    }
    private readonly record struct SkyViewKey(Vector3 SunDirection,Vector3 SunIlluminance,float AltitudeBucket,SkyLightSettings SkyLight);
}
/// <summary>Test-only copy of the atmosphere's GPU state.</summary>
internal sealed record AtmosphereReadback(
    Vector3[]Transmittance,int TransmittanceWidth,int TransmittanceHeight,
    Vector3[]SkyView,int SkyViewWidth,int SkyViewHeight,
    Vector3[]IrradianceSh);
/// <summary>Per-frame inputs of the atmosphere passes.</summary>
internal readonly record struct AtmosphereFrame(
    SkyAtmosphereSettings Atmosphere,
    SkyLightSettings SkyLight,
    PhysicalHeightFogSettings?Fog,
    Vector3 SunDirection,
    Vector3 SunIlluminance,
    float SunAngularRadiusRadians,
    float CameraAltitudeMeters,
    float ExposureScale,
    Matrix4x4 InverseViewProjection);
