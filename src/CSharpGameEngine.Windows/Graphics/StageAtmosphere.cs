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
/// scene depth) and t5-t6 (cloud panorama, cloud noise), 2 = UAV table u0 (the pass output texture), 3 = root UAV u1 (irradiance SH),
/// 4 = root SRV t4 (auto exposure state, read by the sky composite in histogram mode), 5 = UAV table u2 (the cloud noise volume),
/// s0 = linear clamp, s1 = linear wrap.
/// ADR-0078/M0110 adds the volumetric clouds (a panorama of the upper hemisphere traced through the cloud layer, refreshed one texel of each
/// 4 x 4 block per frame, and read by the sky composite and the sky light) and the sky sphere (geometry beyond it is covered by the sky).
/// </summary>
internal sealed unsafe class StageAtmosphere:IDisposable {
    internal const string ArtifactFileName="Atmosphere.cgeshaderartifact";
    internal const int ShCoefficientCount=9;
    private const uint PassTransmittance=0;
    private const uint PassMultiScattering=1;
    private const uint PassSkyView=2;
    private const uint PassSkyLightSh=3;
    private const uint PassSkyBackground=4;
    private const uint PassCloudNoise=5;
    private const uint PassCloudPanorama=6;
    private const uint PassSkyRadianceMap=7;
    private const uint PassCount=8;
    private const uint ConstantsStride=512;
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
    private const uint SrvCloudPanorama=4;
    private const uint SrvCloudNoise=5;
    private const uint UavTransmittance=6;
    private const uint UavMultiScattering=7;
    private const uint UavSkyView=8;
    private const uint UavSceneColor=9;
    private const uint UavCloudPanorama=10;
    private const uint UavCloudNoise=11;
    private const uint SrvSkyRadianceMap=12;
    private const uint UavSkyRadianceMap=13;
    private const uint DescriptorCount=14;
    /// <summary>Texels per side of the sky radiance map (M0111), octahedral with +Z as the pole.</summary>
    internal const int SkyRadianceMapSize=128;
    private const DXGI_FORMAT CloudPanoramaFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const DXGI_FORMAT CloudNoiseFormat=DXGI_FORMAT_R16_FLOAT;
    /// <summary>Texels of the tiling cloud noise volume per side (Atmosphere.hlsl CloudNoiseSize).</summary>
    internal const int CloudNoiseSize=32;
    /// <summary>Frames that refresh every texel of the cloud panorama once (one texel of each 4 x 4 block per frame).</summary>
    internal const int CloudRefreshFrames=16;
    private const D3D12_RESOURCE_STATES ShaderResourceState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE|D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private ID3D12Resource*TransmittanceLut;
    private ID3D12Resource*MultiScatteringLut;
    private ID3D12Resource*SkyViewLut;
    private ID3D12Resource*ShBuffer;
    private ID3D12Resource*CloudPanorama;
    private ID3D12Resource*CloudNoise;
    private ID3D12Resource*SkyRadianceMap;
    private CloudKey?CloudPanoramaKey;
    private int CloudRefreshPhase;
    private int CloudSkyLightVersion;
    private bool CloudNoiseReady;
    private ID3D12Resource*ConstantsBuffer;
    private byte*MappedConstants;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private uint DescriptorSize;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private SkyAtmosphereSettings?LutSettings;
    private SkyLightKey?SkyViewState;
    private nint DescribedDepth;
    private nint DescribedSceneColor;
    private bool Disposed;
    internal StageAtmosphere(GraphicsDevice Device,string StageArtifactPath,uint FrameCount){
        this.Device=Device;
        // WARP draws on the CPU: a coarser panorama and fewer samples keep its frames usable (M0110); a GPU gets the finer one.
        (this.CloudPanoramaWidth,this.CloudPanoramaHeight,this.CloudViewSamples)=Device.IsWarp?(1024,256,24):(2048,512,48);
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
    internal int CloudPanoramaWidth { get; }
    internal int CloudPanoramaHeight { get; }
    /// <summary>View-ray samples per panorama texel.</summary>
    internal int CloudViewSamples { get; }
    /// <summary>Whether the cloud panorama holds this frame's clouds (it is refreshed in <see cref="Update"/> when the frame has clouds).</summary>
    private bool CloudsDrawn;
    private bool SkyRadianceMapCurrent;
    /// <summary>
    /// ADR-0078/M0111: set by the renderer while the Lumen-like lighting runs; <see cref="Update"/> then keeps <see cref="SkyRadianceMapResource"/>
    /// (the sky light's radiance by direction, octahedral like the GI probes, <see cref="SkyRadianceMapSize"/> texels per side) up to date with the
    /// sky light SH.
    /// </summary>
    internal bool SkyRadianceMapRequested { get;set; }
    /// <summary>The sky radiance map (shader-readable once the sky has been updated; null before).</summary>
    internal ID3D12Resource*SkyRadianceMapResource=>this.SkyRadianceMap;
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
        var SkyViewChanged=this.SkyViewState?.SkyView!=Key;
        if(SkyViewChanged){
            this.Dispatch(CommandList,FrameIndex,PassSkyView,Constants,this.SkyViewLut,UavSkyView,SkyViewWidth,SkyViewHeight);
        }
        // M0110: the clouds are traced after the sky view and before the sky light, which sees them. The sky light is projected again whenever
        // the panorama has been refreshed completely (CloudSkyLightVersion), so it follows the moving clouds.
        this.CloudsDrawn=false;
        if(Frame.Clouds is VolumetricCloudSettings Clouds){
            this.UpdateClouds(CommandList,FrameIndex,Frame,Clouds);
            this.CloudsDrawn=true;
        } else {
            this.CloudPanoramaKey=null;
        }
        var State=new SkyLightKey(Key,this.CloudsDrawn?this.CloudSkyLightVersion:-1);
        if(this.SkyViewState!=State){
            this.DispatchSh(CommandList,FrameIndex,Constants);
            this.SkyViewState=State;
            this.SkyRadianceMapCurrent=false;
        }
        // M0111: the sky by direction for the Lumen-like rays that leave the scene, only while something reads it.
        if(this.SkyRadianceMapRequested&&!this.SkyRadianceMapCurrent){
            this.Dispatch(CommandList,FrameIndex,PassSkyRadianceMap,Constants,this.SkyRadianceMap,UavSkyRadianceMap,SkyRadianceMapSize,SkyRadianceMapSize);
            this.SkyRadianceMapCurrent=true;
        }
        this.HasValidSkyLight=true;
        this.LastSunDirection=Frame.SunDirection;
    }
    /// <summary>
    /// Makes the cloud noise once, then refreshes the panorama: every texel when the clouds, the atmosphere or the panorama are new, otherwise
    /// one texel of each 4 x 4 block (the next of the 16 Bayer phases). Leaves both readable by shaders.
    /// </summary>
    private void UpdateClouds(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in AtmosphereFrame Frame,in VolumetricCloudSettings Clouds){
        if(!this.CloudNoiseReady){
            D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(this.CloudNoise,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            CommandList->ResourceBarrier(1,&ToUav);
            this.BindAndDispatch(CommandList,FrameIndex,PassCloudNoise,Frame,UavCloudPanorama,0,0,GroupsX:CloudNoiseSize/8,GroupsY:CloudNoiseSize/8,GroupsZ:CloudNoiseSize);
            D3D12_RESOURCE_BARRIER ToSrv=D3D12_RESOURCE_BARRIER.InitTransition(this.CloudNoise,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
            CommandList->ResourceBarrier(1,&ToSrv);
            this.CloudNoiseReady=true;
        }
        var Key=new CloudKey(Clouds with { CameraPositionKm=Vector2.Zero,WindOffsetKm=Vector2.Zero },Frame.Atmosphere,Frame.SunDirection,Frame.SunIlluminance);
        var Everything=this.CloudPanoramaKey!=Key;
        if(Everything){
            this.CloudPanoramaKey=Key;
            this.CloudRefreshPhase=0;
        }
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CloudPanorama,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(2,Before);
        var Width=(uint)this.CloudPanoramaWidth;
        var Height=(uint)this.CloudPanoramaHeight;
        var Threads=Everything?(X:Width,Y:Height):(X:(Width+3u)/4u,Y:(Height+3u)/4u);
        this.BindAndDispatch(CommandList,FrameIndex,PassCloudPanorama,Frame,UavCloudPanorama,Width,Height,
            GroupsX:(Threads.X+7u)/8u,GroupsY:(Threads.Y+7u)/8u,GroupsZ:1,CloudPhase:(uint)this.CloudRefreshPhase,CloudEverything:Everything);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CloudPanorama,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(2,After);
        if(Everything){
            this.CloudSkyLightVersion++;
        } else {
            this.CloudRefreshPhase=(this.CloudRefreshPhase+1)%CloudRefreshFrames;
            if(this.CloudRefreshPhase==0){
                this.CloudSkyLightVersion++;
            }
        }
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
        this.BindAndDispatch(CommandList,FrameIndex,PassSkyBackground,Frame,UavSceneColor,(uint)Width,(uint)Height,
            GroupsX:((uint)Width+7u)/8u,GroupsY:((uint)Height+7u)/8u,GroupsZ:1,UseExposureState:UseExposureState);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[3];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(Depth,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(SceneColor,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_RENDER_TARGET);
        After[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(3,After);
    }
    private void Dispatch(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,uint Pass,in AtmosphereFrame Frame,ID3D12Resource*Target,uint UavSlot,int Width,int Height){
        D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(Target,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToUav);
        this.BindAndDispatch(CommandList,FrameIndex,Pass,Frame,UavSlot,(uint)Width,(uint)Height,GroupsX:((uint)Width+7u)/8u,GroupsY:((uint)Height+7u)/8u,GroupsZ:1);
        D3D12_RESOURCE_BARRIER ToSrv=D3D12_RESOURCE_BARRIER.InitTransition(Target,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(1,&ToSrv);
    }
    private void DispatchSh(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in AtmosphereFrame Frame){
        D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToUav);
        this.BindAndDispatch(CommandList,FrameIndex,PassSkyLightSh,Frame,UavSkyView,8,8,GroupsX:1,GroupsY:1,GroupsZ:1);
        D3D12_RESOURCE_BARRIER ToSrv=D3D12_RESOURCE_BARRIER.InitTransition(this.ShBuffer,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(1,&ToSrv);
    }
    private void BindAndDispatch(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,uint Pass,in AtmosphereFrame Frame,uint UavSlot,uint Width,uint Height,
        uint GroupsX,uint GroupsY,uint GroupsZ,bool UseExposureState=false,uint CloudPhase=0,bool CloudEverything=false){
        var Slot=FrameIndex*PassCount+Pass;
        this.WriteConstants(Slot,Pass,Frame,Width,Height,UseExposureState,CloudPhase,CloudEverything);
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
        CommandList->SetComputeRootDescriptorTable(5,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)UavCloudNoise,this.DescriptorSize));
        CommandList->Dispatch(GroupsX,GroupsY,GroupsZ);
    }
    private void WriteConstants(uint Slot,uint Pass,in AtmosphereFrame Frame,uint Width,uint Height,bool UseExposureState,uint CloudPhase,bool CloudEverything){
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
        // M0110: clouds (zero when the frame has none, so the composite and the sky light skip them) and the sky sphere.
        var Clouds=Frame.Clouds;
        var Bottom=Atmosphere.BottomRadiusKm;
        Write(16,new Vector3(Bottom+(Clouds?.LayerBottomAltitudeKm??0f),Bottom+(Clouds?.LayerBottomAltitudeKm??0f)+(Clouds?.LayerHeightKm??1f),Clouds?.TracingStartMaxDistanceKm??0f),Clouds?.TracingMaxDistanceKm??0f);
        Write(17,new Vector3((Clouds?.DensityPerMeter??0f)*1000f,Clouds?.Coverage??0f,Clouds?.LayoutScaleKm??1f),Clouds?.StopTracingTransmittanceThreshold??0f);
        Write(18,Clouds?.Albedo??Vector3.Zero,Clouds?.SkyLightBottomOcclusion??0f);
        Write(19,new Vector3(Clouds?.PhaseG??0f,Clouds?.PhaseG2??0f,Clouds?.PhaseBlend??0f),this.CloudsDrawn&&Clouds is not null?1f:0f);
        Write(20,new Vector3(Clouds?.MultiScatteringContribution??0f,Clouds?.MultiScatteringOcclusion??0f,Clouds?.MultiScatteringEccentricity??0f),this.CloudViewSamples);
        var CameraKm=Clouds?.CameraPositionKm??Vector2.Zero;
        var WindKm=Clouds?.WindOffsetKm??Vector2.Zero;
        Write(21,new Vector3(CameraKm.X,CameraKm.Y,WindKm.X),WindKm.Y);
        var Update=(uint*)(Target+88);
        Update[0]=CloudPhase;
        Update[1]=CloudEverything?1u:0u;
        Update[2]=(uint)this.CloudPanoramaWidth;
        Update[3]=(uint)this.CloudPanoramaHeight;
        var Sphere=Frame.SkySphere;
        Write(23,Sphere?.Center??Vector3.Zero,Sphere?.RadiusMeters??0f);
        Write(24,new Vector3(Frame.DepthMapping.X,Frame.DepthMapping.Y,Frame.DepthMapping.Z),0f);
        Write(25,Frame.CameraPosition,0f);
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
        // t0-t3 then, after the root SRV t4, t5-t6 (cloud panorama and noise), all from one contiguous run of the heap.
        D3D12_DESCRIPTOR_RANGE*SrvRanges=stackalloc D3D12_DESCRIPTOR_RANGE[2];
        SrvRanges[0]=new D3D12_DESCRIPTOR_RANGE { RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=4,BaseShaderRegister=0,OffsetInDescriptorsFromTableStart=0 };
        SrvRanges[1]=new D3D12_DESCRIPTOR_RANGE { RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=2,BaseShaderRegister=5,OffsetInDescriptorsFromTableStart=4 };
        D3D12_DESCRIPTOR_RANGE UavRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_DESCRIPTOR_RANGE NoiseRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=1,BaseShaderRegister=2 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[6];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=2,pDescriptorRanges=SrvRanges };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&UavRange };
        Parameters[3]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[3].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        Parameters[4]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[4].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=4 };
        Parameters[5]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[5].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&NoiseRange };
        D3D12_STATIC_SAMPLER_DESC*Samplers=stackalloc D3D12_STATIC_SAMPLER_DESC[2];
        Samplers[0]=new(){
            Filter=D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            ComparisonFunc=D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        Samplers[1]=Samplers[0];
        Samplers[1].AddressU=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        Samplers[1].AddressV=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        Samplers[1].AddressW=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        Samplers[1].ShaderRegister=1;
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=6,pParameters=Parameters,NumStaticSamplers=2,pStaticSamplers=Samplers };
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
        // M0110: the cloud targets exist with the sky, so their descriptors are written once and never while a frame may read them.
        this.CloudPanorama=this.CreateCloudTexture(false,CloudPanoramaFormat,(ulong)this.CloudPanoramaWidth,(uint)this.CloudPanoramaHeight,1,SrvCloudPanorama,UavCloudPanorama);
        this.CloudNoise=this.CreateCloudTexture(true,CloudNoiseFormat,CloudNoiseSize,CloudNoiseSize,CloudNoiseSize,SrvCloudNoise,UavCloudNoise);
        this.SkyRadianceMap=this.CreateLut(SkyRadianceMapSize,SkyRadianceMapSize,SrvSkyRadianceMap,UavSkyRadianceMap);
    }
    private ID3D12Resource*CreateCloudTexture(bool Volume,DXGI_FORMAT Format,ulong Width,uint Height,ushort Depth,uint SrvSlot,uint UavSlot){
        var Desc=Volume?D3D12_RESOURCE_DESC.Tex3D(Format,Width,Height,Depth,1):D3D12_RESOURCE_DESC.Tex2D(Format,Width,Height,1,1);
        Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,ShaderResourceState,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a volumetric cloud texture");
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=Format,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
        D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=Format };
        if(Volume){
            Srv.ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE3D;
            Srv.Texture3D.MipLevels=1;
            Uav.ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE3D;
            Uav.Texture3D.WSize=Depth;
        } else {
            Srv.ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D;
            Srv.Texture2D.MipLevels=1;
            Uav.ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D;
        }
        this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(SrvSlot));
        this.Device.Device->CreateUnorderedAccessView(Resource,null,&Uav,this.CpuHandle(UavSlot));
        return Resource;
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
    /// <summary>Test-only copy of the cloud panorama (rgb cloud luminance, a sky kept), row-major; the caller guarantees the queue is idle.</summary>
    internal (Vector4[]Texels,int Width,int Height) ReadCloudPanoramaForTesting(){
        if(this.CloudPanorama==null){
            throw new InvalidOperationException("The atmosphere has not rendered a frame yet.");
        }
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device,this.CloudPanorama,ShaderResourceState);
        var Result=new Vector4[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                var Row=(Half*)(Bytes+Y*RowPitch);
                for(var X=0;X<Width;X++){
                    Result[Y*Width+X]=new Vector4((float)Row[X*4],(float)Row[X*4+1],(float)Row[X*4+2],(float)Row[X*4+3]);
                }
            }
        }
        return (Result,Width,Height);
    }
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
        if(this.CloudPanorama!=null){ _=this.CloudPanorama->Release(); }
        if(this.CloudNoise!=null){ _=this.CloudNoise->Release(); }
        if(this.SkyRadianceMap!=null){ _=this.SkyRadianceMap->Release(); }
    }
    private readonly record struct SkyViewKey(Vector3 SunDirection,Vector3 SunIlluminance,float AltitudeBucket,SkyLightSettings SkyLight);
    /// <summary>The sky light depends on the sky view and on which refresh of the cloud panorama it saw (-1 = no clouds).</summary>
    private readonly record struct SkyLightKey(SkyViewKey SkyView,int CloudVersion);
    /// <summary>What forces a full refresh of the cloud panorama: the cloud values (without the camera position and wind), the atmosphere and the sun.</summary>
    private readonly record struct CloudKey(VolumetricCloudSettings Clouds,SkyAtmosphereSettings Atmosphere,Vector3 SunDirection,Vector3 SunIlluminance);
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
    Matrix4x4 InverseViewProjection,
    VolumetricCloudSettings?Clouds=null,
    SkySphereSettings?SkySphere=null,
    Vector3 DepthMapping=default,
    Vector3 CameraPosition=default);
