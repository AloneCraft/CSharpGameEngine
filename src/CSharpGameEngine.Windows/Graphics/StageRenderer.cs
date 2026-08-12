using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Compiler.Hlsl;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D_ROOT_SIGNATURE_VERSION;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_COMPARISON_FUNC;
using static TerraFX.Interop.DirectX.D3D12_CULL_MODE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_RANGE_TYPE;
using static TerraFX.Interop.DirectX.D3D12_FENCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_FILTER;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_ROOT_SIGNATURE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.D3D12_SRV_DIMENSION;
using static TerraFX.Interop.DirectX.D3D12_STATIC_BORDER_COLOR;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_ADDRESS_MODE;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_COPY_TYPE;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.DirectX.DXGI_SWAP_EFFECT;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>One procedural or imported mesh plus its solid base color, for the legacy (pre-ADR-0018) untextured API. Kept only as an input shape for <see cref="StageRenderer.Create(nint, int, int, GraphicsDeviceOptions, string, IReadOnlyList{MeshCreateInfo})"/>, which adapts it into one <see cref="StageMeshCreateInfo"/>+<see cref="StageMaterialCreateInfo"/>+<see cref="StageObjectCreateInfo"/> triple per entry.</summary>
public readonly struct MeshCreateInfo {
    public required IReadOnlyList<Vector3>Positions { get; init;}
    public required IReadOnlyList<Vector3>Normals { get; init;}
    public required IReadOnlyList<uint>Indices { get; init;}
    public required Vector4 BaseColorFactor { get; init;}
}

/// <summary>
/// Renders a fixed set of independently-transformed, textured PBR-lite meshes (ADR-0018 extends
/// the untextured ADR-0016 vertical slice) with a movable camera, one data-driven directional
/// light + ambient term, and a simplified exponential height fog. Deliberately evolved in place
/// rather than duplicated into a third renderer class (see ADR-0018): unlike the
/// ModelRenderer/StageRenderer split in ADR-0004, the old untextured contract here is strictly
/// subsumed by the new one (a 1x1 fallback texture per slot reproduces the old solid-color look
/// exactly), so a third copy of the swap chain/RTV/DSV/fence/Resize plumbing would add no value.
/// </summary>
public sealed unsafe class StageRenderer:IDisposable {
    public const int FrameCount=2;
    public const DXGI_FORMAT BackBufferFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    public const DXGI_FORMAT DepthBufferFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT HdrColorFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const DXGI_FORMAT MaterialTextureFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    private const DXGI_FORMAT ShadowMapResourceFormat=DXGI_FORMAT_R32_TYPELESS;
    private const DXGI_FORMAT ShadowMapDsvFormat=DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT ShadowMapSrvFormat=DXGI_FORMAT_R32_FLOAT;
    private const int ShadowMapResolution=1024;

    /// <summary>D3D12 constant buffer views must start at a 256-byte aligned GPU address.</summary>
    private const uint ConstantAlignment=512;

    /// <summary>Fixed capacity, no light culling/clustering in this vertical slice (ADR-0037). Must match Stage.hlsl's MAX_POINT_LIGHTS.</summary>
    public const int MaxPointLights=4;

    /// <summary>Fixed capacity, no light culling/clustering in this vertical slice (ADR-0037). Must match Stage.hlsl's MAX_SPOT_LIGHTS.</summary>
    public const int MaxSpotLights=4;

    private const uint PointLightStride=32; // 2 float4 - see Stage.hlsl's PointLightData
    private const uint SpotLightStride=48;  // 3 float4 - see Stage.hlsl's SpotLightData

    // FrameCB (b0) byte layout - see Stage.hlsl. Each Set*/Add* method writes only its own slice,
    // so SetCamera/SetDirectionalLight/SetAmbientLight/SetFog/AddPointLight/AddSpotLight may be
    // called in any order per frame.
    private const uint FrameViewProjectionOffset=0;   // float4x4, 64 bytes
    private const uint FrameCameraPositionOffset=64;  // float4 (xyz used)
    private const uint FrameLightDirectionOffset=80;  // float4 (xyz used)
    private const uint FrameLightColorOffset=96;      // float4 (xyz used)
    private const uint FrameAmbientColorOffset=112;   // float4 (xyz used)
    private const uint FrameFogParamsOffset=128;      // float4: x=density, y=heightFalloff
    private const uint FrameFogColorOffset=144;       // float4 (xyz used)
    private const uint FrameLightCountsOffset=160;    // int4: x=active point lights, y=active spot lights
    private const uint FramePointLightsOffset=176;    // MaxPointLights * PointLightStride bytes
    private const uint FrameSpotLightsOffset=FramePointLightsOffset+(MaxPointLights*PointLightStride); // MaxSpotLights * SpotLightStride bytes
    private const uint FrameShadowViewProjectionOffset=FrameSpotLightsOffset+(MaxSpotLights*SpotLightStride);
    private const uint FrameConstantsSize=FrameShadowViewProjectionOffset+64;

    /// <summary>
    /// FrameConstantsBuffer専用のフレームスロット幅。FrameConstantsSize以上、かつD3D12のroot CBV
    /// アライメント要件(256バイト)の倍数でなければならない。汎用のConstantAlignmentを流用せず
    /// FrameConstantsSizeから計算するのは、FrameCBを将来また拡張したとき(ShadowViewProjection追加時に
    /// スロット幅の更新を忘れ、フレームスロット間でデータが衝突・範囲外書き込みを起こしていた実際の
    /// 回帰の再発を防ぐため)にスロット幅が自動的に追従するようにするため。
    /// </summary>
    private const uint FrameConstantsStride=((FrameConstantsSize+255u)/256u)*256u;

    // ObjectCB (b1) byte layout - see Stage.hlsl.
    private const uint ObjectWorldOffset=0;            // float4x4, 64 bytes
    private const uint ObjectBaseColorFactorOffset=64; // float4
    private const uint ObjectMaterialParamsOffset=80;  // float4: x=roughness, y=metallic
    private const uint ObjectEmissiveFactorOffset=96;  // float4: xyz = emissive HDR factor

    // HDR clear values are the inverse ACES values for the public LDR ClearColor. This keeps the
    // empty-scene background stable across the new post-process pass while geometry still benefits
    // from HDR lighting and bloom.
    private static readonly Vector4 ClearColorVector=new(0.23238164f,0.4095592f,1.2785315f,1.0f);
    public static readonly PixelColor ClearColor=new(89,140,217,255);

    private static readonly Vector3 DefaultLightDirectionTowardLight=Vector3.Normalize(new Vector3(0.4f,0.7f,0.5f));
    private static readonly Vector3 DefaultLightColor=new(1.0f,1.0f,1.0f);
    private static readonly Vector3 DefaultAmbientColor=new(0.12f,0.12f,0.15f);

    private struct Vertex {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector2 Uv;
    }

    private GraphicsDevice?Device;
    private IDXGISwapChain3*SwapChain;
    private ID3D12DescriptorHeap*RtvHeap;
    private uint RtvDescriptorSize;
    private readonly ID3D12Resource*[]BackBuffers=new ID3D12Resource*[FrameCount];
    private ID3D12DescriptorHeap*DsvHeap;
    private uint DsvDescriptorSize;
    private ID3D12Resource*DepthBuffer;
    private ID3D12Resource*HdrColorBuffer;
    private ID3D12DescriptorHeap*HdrSrvHeap;
    private ID3D12Resource*ShadowMap;
    private readonly ID3D12CommandAllocator*[]CommandAllocators=new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList*CommandList;
    private ID3D12Fence*Fence;
    private readonly ulong[]FenceValues=new ulong[FrameCount];
    private HANDLE FenceEvent;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12RootSignature*ShadowRootSignature;
    private ID3D12PipelineState*ShadowPipelineState;
    private ID3D12RootSignature*PostProcessRootSignature;
    private ID3D12PipelineState*PostProcessPipelineState;
    private byte[]ShadowVertexShaderBytecode=[];
    private byte[]FullscreenVertexShaderBytecode=[];
    private byte[]PostProcessPixelShaderBytecode=[];

    // Per-mesh (indexed by meshIndex).
    private int MeshCount;
    private ID3D12Resource*[]VertexBuffers=[];
    private D3D12_VERTEX_BUFFER_VIEW[]VertexBufferViews=[];
    private ID3D12Resource*[]IndexBuffers=[];
    private D3D12_INDEX_BUFFER_VIEW[]IndexBufferViews=[];
    private uint[]IndexCounts=[];

    // Per-material (indexed by materialIndex). Three material textures plus one shared shadow SRV.
    private int MaterialCount;
    private ID3D12Resource*[]MaterialTextures=[];
    private ID3D12DescriptorHeap*SrvHeap;
    private uint SrvDescriptorSize;
    private Vector4[]MaterialBaseColorFactors=[];
    private float[]MaterialRoughnessFactors=[];
    private float[]MaterialMetallicFactors=[];
    private Vector3[]MaterialEmissiveFactors=[];

    // Per-object (indexed by objectIndex).
    private int ObjectCount;
    private int[]ObjectMeshIndices=[];
    private int[]ObjectMaterialIndices=[];
    private Matrix4x4[]ObjectTransforms=[];

    private ID3D12Resource*FrameConstantsBuffer;
    private byte*FrameConstantsMapped;
    private ID3D12Resource*ObjectConstantsBuffer;
    private byte*ObjectConstantsMapped;
    private ID3D12Resource*ShadowConstantsBuffer;
    private byte*ShadowConstantsMapped;

    // Point/spot lights are additive per frame (AddPointLight/AddSpotLight), unlike the singleton
    // directional light/ambient/fog which are overwritten in place. Counts reset to zero at the end
    // of RenderFrame so a light that stops calling Add (its owning Entity went away) doesn't linger.
    private int PointLightCount;
    private int SpotLightCount;

    private ID3D12Resource*ReadbackBuffer;
    private ulong ReadbackBufferSize;
    private D3D12_PLACED_SUBRESOURCE_FOOTPRINT ReadbackFootprint;

    private uint FrameIndex;
    private int Width;
    private int Height;
    private bool SizeIsZero;
    private bool Disposed;

    private D3D12_VIEWPORT Viewport;
    private RECT ScissorRect;

    public string AdapterDescription=>Device?.AdapterDescription??string.Empty;

    public bool IsWarp=>Device?.IsWarp??false;

    public bool DebugLayerEnabled=>Device?.DebugLayerEnabled??false;

    public bool GpuBasedValidationEnabled=>Device?.GpuBasedValidationEnabled??false;

    public IReadOnlyList<D3D12DebugMessage>DebugMessages=>Device?.CollectedMessages??[];

    public int ErrorOrWorseMessageCount=>Device?.ErrorOrWorseMessageCount??0;

    public bool ShaderCompilationSucceeded { get;private set;}

    public IReadOnlyList<HlslDiagnostic>ShaderDiagnostics { get;private set;}=[];

    public uint FramesRendered { get;private set;}

    public PixelReadbackResult?LastCapturedFrame { get;private set;}

    public static StageRenderer Create(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderPath,
        IReadOnlyList<StageMeshCreateInfo>Meshes,IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects){
        ArgumentNullException.ThrowIfNull(ShaderPath);
        ArgumentNullException.ThrowIfNull(Meshes);
        ArgumentNullException.ThrowIfNull(Materials);
        ArgumentNullException.ThrowIfNull(Objects);
        if(Width<=0||Height<=0){
            throw new ArgumentOutOfRangeException(nameof(Width),"Initial width/height must both be positive.");
        }

        if(Meshes.Count==0){
            throw new ArgumentException("At least one mesh is required.",nameof(Meshes));
        }
        if(Materials.Count==0){
            throw new ArgumentException("At least one material is required.",nameof(Materials));
        }
        if(Objects.Count==0){
            throw new ArgumentException("At least one object is required.",nameof(Objects));
        }

        foreach(StageMeshCreateInfo Mesh in Meshes){
            if(Mesh.Positions.Count==0||Mesh.Positions.Count!=Mesh.Normals.Count
                ||Mesh.Positions.Count!=Mesh.Tangents.Count||Mesh.Positions.Count!=Mesh.Uvs.Count
                ||Mesh.Indices.Count==0){
                throw new ArgumentException("Each mesh must have at least one vertex with matching Positions/Normals/Tangents/Uvs counts, and at least one index.",nameof(Meshes));
            }
        }

        foreach(StageObjectCreateInfo Obj in Objects){
            if(Obj.MeshIndex<0||Obj.MeshIndex>=Meshes.Count){
                throw new ArgumentOutOfRangeException(nameof(Objects), $"Object references out-of-range MeshIndex {Obj.MeshIndex}.");
            }
            if(Obj.MaterialIndex<0||Obj.MaterialIndex>=Materials.Count){
                throw new ArgumentOutOfRangeException(nameof(Objects), $"Object references out-of-range MaterialIndex {Obj.MaterialIndex}.");
            }
        }

        StageRenderer Renderer=new(){
            Width=Width,
            Height=Height,
        };

        var Completed=false;
        try {
            Renderer.Initialize((HWND)Hwnd,Width,Height,DeviceOptions,ShaderPath,Meshes,Materials,Objects);
            Completed=true;
            return Renderer;
        } finally {
            if(!Completed){
                Renderer.Dispose();
            }
        }
    }

    /// <summary>Legacy adapter (pre-ADR-0018): one untextured solid-color mesh per object, exactly reproducing the old 1:1 mesh/material/object indexing so existing callers (and <c>StageGpuIntegrationTests</c>) keep working unchanged. Synthesized UVs/tangents never matter visually here since every material falls back to its 1x1 default textures.</summary>
    public static StageRenderer Create(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderPath,
        IReadOnlyList<MeshCreateInfo>Meshes){
        ArgumentNullException.ThrowIfNull(Meshes);

        var StageMeshes=new StageMeshCreateInfo[Meshes.Count];
        var StageMaterials=new StageMaterialCreateInfo[Meshes.Count];
        var StageObjects=new StageObjectCreateInfo[Meshes.Count];

        for(var I=0;I<Meshes.Count;I++){
            MeshCreateInfo Mesh=Meshes[I];
            var VertexCount=Mesh.Positions.Count;
            var Uvs=new Vector2[VertexCount];
            var Tangents=new Vector4[VertexCount];
            for(var V=0;V<VertexCount;V++){
                Tangents[V]=ArbitraryTangentFor(Mesh.Normals[V]);
            }

            StageMeshes[I]=new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Mesh.Indices };
            StageMaterials[I]=new StageMaterialCreateInfo { BaseColorFactor=Mesh.BaseColorFactor,RoughnessFactor=1f,MetallicFactor=0f };
            StageObjects[I]=new StageObjectCreateInfo { MeshIndex=I,MaterialIndex=I };
        }

        return Create(Hwnd,Width,Height,DeviceOptions,ShaderPath,StageMeshes,StageMaterials,StageObjects);
    }

    /// <summary>An arbitrary unit tangent perpendicular to <paramref name="Normal"/> - correct for TBN orthonormality even though it isn't aligned to any real UV direction, which is fine when the bound normal texture is always the flat 1x1 fallback (see <see cref="MaterialTextureFormat"/> fallback images). Internal (not private) so <see cref="CSharpGameEngine.Runtime.Windows.RenderServer"/> can reuse the same untextured-mesh UV/tangent synthesis instead of duplicating it.</summary>
    internal static Vector4 ArbitraryTangentFor(Vector3 Normal){
        Vector3 Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
        Vector3 Tangent=Vector3.Normalize(Vector3.Cross(Reference,Normal));
        return new Vector4(Tangent,1f);
    }

    private StageRenderer(){
    }

    private void Initialize(HWND Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderPath,
        IReadOnlyList<StageMeshCreateInfo>Meshes,IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects){
        Device=GraphicsDevice.Create(DeviceOptions);

        CreateSwapChain(Hwnd,Width,Height);
        CreateRtvHeap();
        CreateRenderTargetViews();
        CreateDsvHeap();
        CreateDepthBuffer(Width,Height);
        CreateHdrColorBuffer(Width,Height);
        CreateShadowMap();

        for(var I=0;I<FrameCount;I++){
            ID3D12CommandAllocator*Allocator;
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),
                "ID3D12Device::CreateCommandAllocator","creating a frame command allocator");
            CommandAllocators[I]=Allocator;
        }

        ID3D12GraphicsCommandList*CommandList;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,CommandAllocators[0],null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&CommandList),
            "ID3D12Device::CreateCommandList","creating the graphics command list");
        this.CommandList=CommandList;
        D3D12Diagnostics.EnsureSuccess(CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the initial command list state");

        ID3D12Fence*Fence;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateFence(0,D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),
            "ID3D12Device::CreateFence","creating the frame fence");
        this.Fence=Fence;

        FenceEvent=CreateEventW(lpEventAttributes:null,bManualReset:FALSE,bInitialState:FALSE,lpName:null);
        if(FenceEvent==HANDLE.NULL){
            throw new InvalidOperationException("CreateEventW failed while creating the frame fence event.");
        }

        FrameIndex=SwapChain->GetCurrentBackBufferIndex();
        FenceValues[FrameIndex]=1;

        CompileShaders(ShaderPath);
        CreateRootSignature();
        CreateShadowRootSignature();
        CreatePostProcessRootSignature();
        CreatePipelineState();
        CreateShadowPipelineState();
        CreatePostProcessPipelineState();
        CreateMeshBuffers(Meshes);
        CreateMaterialResources(Materials);
        CreateObjectMappings(Objects);
        CreateConstantBuffers();
        CreateShadowConstantsBuffer();
        UpdateViewportAndScissor(Width,Height);
        InitializeConstantsToDefaults();
    }

    private void CreateSwapChain(HWND Hwnd,int Width,int Height){
        DXGI_SWAP_CHAIN_DESC1 Desc=new(){
            BufferCount=FrameCount,
            Width=(uint)Width,
            Height=(uint)Height,
            Format=BackBufferFormat,
            BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT,
            SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD,
            SampleDesc=new DXGI_SAMPLE_DESC(1,0),
        };

        IDXGISwapChain1*SwapChain1;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Factory->CreateSwapChainForHwnd(
                (IUnknown*)Device.CommandQueue,
                Hwnd,
                &Desc,
                pFullscreenDesc:null,
                pRestrictToOutput:null,
                &SwapChain1),
            "IDXGIFactory4::CreateSwapChainForHwnd","creating the swap chain");

        IDXGISwapChain3*SwapChain3;
        HRESULT Hr=SwapChain1->QueryInterface(__uuidof<IDXGISwapChain3>(),(void**)&SwapChain3);
        _=SwapChain1->Release();
        D3D12Diagnostics.EnsureSuccess(Hr,"IDXGISwapChain1::QueryInterface(IDXGISwapChain3)","upgrading the swap chain interface");

        SwapChain=SwapChain3;
    }

    private void CreateRtvHeap(){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            NumDescriptors=FrameCount+1,
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
        };

        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the RTV descriptor heap");

        RtvHeap=Heap;
        RtvDescriptorSize=Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    }

    private void CreateRenderTargetViews(){
        D3D12_CPU_DESCRIPTOR_HANDLE RtvBase=RtvHeap->GetCPUDescriptorHandleForHeapStart();

        for(uint I=0;I<FrameCount;I++){
            ID3D12Resource*BackBuffer;
            D3D12Diagnostics.EnsureSuccess(
                SwapChain->GetBuffer(I,__uuidof<ID3D12Resource>(),(void**)&BackBuffer),
                "IDXGISwapChain3::GetBuffer","retrieving a back buffer", $"buffer[{I}]");

            D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvBase,(int)I,RtvDescriptorSize);
            Device!.Device->CreateRenderTargetView(BackBuffer,pDesc:null,RtvHandle);

            BackBuffers[I]=BackBuffer;
        }
    }

    private void CreateDsvHeap(){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            NumDescriptors=2,
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_DSV,
        };

        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the DSV descriptor heap");

        DsvHeap=Heap;
        DsvDescriptorSize=Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_DSV);
    }

    private void CreateDepthBuffer(int Width,int Height){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(DepthBufferFormat,(uint)Width,(uint)Height,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE ClearValue=new(DepthBufferFormat,1.0f,0);

        ID3D12Resource*DepthBuffer;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&ResourceDesc,D3D12_RESOURCE_STATE_DEPTH_WRITE,&ClearValue,__uuidof<ID3D12Resource>(),(void**)&DepthBuffer),
            "ID3D12Device::CreateCommittedResource","creating the depth buffer");

        D3D12_DEPTH_STENCIL_VIEW_DESC DsvDesc=new(){
            Format=DepthBufferFormat,
            ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D,
        };
        Device.Device->CreateDepthStencilView(DepthBuffer,&DsvDesc,DsvHeap->GetCPUDescriptorHandleForHeapStart());

        this.DepthBuffer=DepthBuffer;
    }

    private void CreateHdrColorBuffer(int Width,int Height){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(HdrColorFormat,(uint)Width,(uint)Height,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE ClearValue=new(HdrColorFormat,0f,0);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&ResourceDesc,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,&ClearValue,__uuidof<ID3D12Resource>(),(void**)&Resource),
            nameof(CreateHdrColorBuffer),nameof(CreateHdrColorBuffer));

        D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount,RtvDescriptorSize);
        Device.Device->CreateRenderTargetView(Resource,pDesc:null,RtvHandle);
        HdrColorBuffer=Resource;

        if(HdrSrvHeap==null){
            D3D12_DESCRIPTOR_HEAP_DESC SrvHeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=1,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
            ID3D12DescriptorHeap*SrvHeap;
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateDescriptorHeap(&SrvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&SrvHeap),
                nameof(CreateHdrColorBuffer),nameof(CreateHdrColorBuffer));
            HdrSrvHeap=SrvHeap;
        }

        D3D12_SHADER_RESOURCE_VIEW_DESC SrvDesc=new(){
            Format=HdrColorFormat,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        SrvDesc.Texture2D.MipLevels=1;
        Device.Device->CreateShaderResourceView(Resource,&SrvDesc,HdrSrvHeap->GetCPUDescriptorHandleForHeapStart());
    }

    private void CreateShadowMap(){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(ShadowMapResourceFormat,ShadowMapResolution,ShadowMapResolution,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE ClearValue=new(ShadowMapDsvFormat,1.0f,0);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&ResourceDesc,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,&ClearValue,__uuidof<ID3D12Resource>(),(void**)&Resource),
            nameof(CreateShadowMap),nameof(CreateShadowMap));

        D3D12_DEPTH_STENCIL_VIEW_DESC DsvDesc=new(){ Format=ShadowMapDsvFormat,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D };
        D3D12_CPU_DESCRIPTOR_HANDLE ShadowDsv=new(DsvHeap->GetCPUDescriptorHandleForHeapStart(),1,DsvDescriptorSize);
        Device.Device->CreateDepthStencilView(Resource,&DsvDesc,ShadowDsv);
        ShadowMap=Resource;
    }

    private void CompileShaders(string ShaderPath){
        var DebugShaders=false;
#if DEBUG
        DebugShaders = true;
#endif

        HlslCompileResult Vs=HlslCompiler.Compile(new HlslCompileRequest {
            FilePath=ShaderPath,
            EntryPoint="VSMain",
            TargetProfile="vs_6_0",
            EnableDebugInfo=DebugShaders,
        });

        HlslCompileResult Ps=HlslCompiler.Compile(new HlslCompileRequest {
            FilePath=ShaderPath,
            EntryPoint="PSMain",
            TargetProfile="ps_6_0",
            EnableDebugInfo=DebugShaders,
        });

        HlslCompileResult FullscreenVs=HlslCompiler.Compile(new HlslCompileRequest {
            FilePath=ShaderPath,
            EntryPoint="FullscreenVS",
            TargetProfile="vs_6_0",
            EnableDebugInfo=DebugShaders,
        });

        HlslCompileResult PostPs=HlslCompiler.Compile(new HlslCompileRequest {
            FilePath=ShaderPath,
            EntryPoint="PostProcessPS",
            TargetProfile="ps_6_0",
            EnableDebugInfo=DebugShaders,
        });

        HlslCompileResult ShadowVs=HlslCompiler.Compile(new HlslCompileRequest {
            FilePath=ShaderPath,
            EntryPoint="VSShadowMain",
            TargetProfile="vs_6_0",
            EnableDebugInfo=DebugShaders,
        });

        ShaderCompilationSucceeded=Vs.Success&&Ps.Success&&FullscreenVs.Success&&PostPs.Success&&ShadowVs.Success;
        ShaderDiagnostics=[.. Vs.Diagnostics,.. Ps.Diagnostics,.. FullscreenVs.Diagnostics,.. PostPs.Diagnostics,.. ShadowVs.Diagnostics];

        if(!ShaderCompilationSucceeded){
            var Details=string.Join(Environment.NewLine,ShaderDiagnostics);
            throw new InvalidOperationException($"HLSL shader compilation failed for '{ShaderPath}':{Environment.NewLine}{Details}");
        }

        VertexShaderBytecode=Vs.Bytecode;
        PixelShaderBytecode=Ps.Bytecode;
        FullscreenVertexShaderBytecode=FullscreenVs.Bytecode;
        PostProcessPixelShaderBytecode=PostPs.Bytecode;
        ShadowVertexShaderBytecode=ShadowVs.Bytecode;
    }

    private byte[]VertexShaderBytecode=[];
    private byte[]PixelShaderBytecode=[];

    private void CreateRootSignature(){
        D3D12_ROOT_PARAMETER FrameParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        FrameParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0,RegisterSpace=0 };

        D3D12_ROOT_PARAMETER ObjectParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        ObjectParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1,RegisterSpace=0 };

        D3D12_DESCRIPTOR_RANGE SrvRange=new(){
            RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors=4,
            BaseShaderRegister=0,
        };

        D3D12_ROOT_PARAMETER TextureParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_STATIC_SAMPLER_DESC StaticSampler=new(){
            Filter=D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            ComparisonFunc=D3D12_COMPARISON_FUNC_NEVER,
            BorderColor=D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_STATIC_SAMPLER_DESC ShadowSampler=new(){
            Filter=D3D12_FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            ComparisonFunc=D3D12_COMPARISON_FUNC_LESS_EQUAL,
            BorderColor=D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=1,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[3];
        RootParams[0]=FrameParam;
        RootParams[1]=ObjectParam;
        RootParams[2]=TextureParam;
        RootParams[2].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        D3D12_STATIC_SAMPLER_DESC*StaticSamplers=stackalloc D3D12_STATIC_SAMPLER_DESC[2];
        StaticSamplers[0]=StaticSampler;
        StaticSamplers[1]=ShadowSampler;

        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            NumParameters=3,
            pParameters=RootParams,
            NumStaticSamplers=2,
            pStaticSamplers=StaticSamplers,
            Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT,
        };

        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            HRESULT Hr=D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob);
            if(Hr.FAILED){
                var Error=ErrorBlob!=null
                    ?new string((sbyte*)ErrorBlob->GetBufferPointer())
                    :"(no error blob)";
                throw new D3D12Exception("D3D12SerializeRootSignature",Hr.Value,"serializing the stage root signature",Error);
            }

            ID3D12RootSignature*RootSignature;
            D3D12Diagnostics.EnsureSuccess(
                Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&RootSignature),
                "ID3D12Device::CreateRootSignature","creating the root signature");
            this.RootSignature=RootSignature;
        } finally {
            if(SignatureBlob!=null){
                _=SignatureBlob->Release();
            }

            if(ErrorBlob!=null){
                _=ErrorBlob->Release();
            }
        }
    }

    private void CreateShadowRootSignature(){
        D3D12_ROOT_PARAMETER FrameParam=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_VERTEX };
        FrameParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0,RegisterSpace=0 };
        D3D12_ROOT_PARAMETER ObjectParam=new(){ ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_VERTEX };
        ObjectParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1,RegisterSpace=0 };
        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[2];
        RootParams[0]=FrameParam;
        RootParams[1]=ObjectParam;
        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){ NumParameters=2,pParameters=RootParams,Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT };
        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob),nameof(CreateShadowRootSignature),nameof(CreateShadowRootSignature));
            ID3D12RootSignature*Signature;
            D3D12Diagnostics.EnsureSuccess(Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Signature),nameof(CreateShadowRootSignature),nameof(CreateShadowRootSignature));
            ShadowRootSignature=Signature;
        } finally {
            if(SignatureBlob!=null){ _=SignatureBlob->Release(); }
            if(ErrorBlob!=null){ _=ErrorBlob->Release(); }
        }
    }

    private void CreatePostProcessRootSignature(){
        D3D12_DESCRIPTOR_RANGE SceneRange=new(){
            RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors=1,
            BaseShaderRegister=0,
        };
        D3D12_ROOT_PARAMETER SceneParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };
        SceneParam.DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SceneRange };

        D3D12_STATIC_SAMPLER_DESC Sampler=new(){
            Filter=D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            ComparisonFunc=D3D12_COMPARISON_FUNC_NEVER,
            BorderColor=D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[1];
        RootParams[0]=SceneParam;
        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            NumParameters=1,
            pParameters=RootParams,
            NumStaticSamplers=1,
            pStaticSamplers=&Sampler,
        };

        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            D3D12Diagnostics.EnsureSuccess(
                D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob),
                nameof(CreatePostProcessRootSignature),nameof(CreatePostProcessRootSignature));
            ID3D12RootSignature*Signature;
            D3D12Diagnostics.EnsureSuccess(
                Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Signature),
                nameof(CreatePostProcessRootSignature),nameof(CreatePostProcessRootSignature));
            PostProcessRootSignature=Signature;
        } finally {
            if(SignatureBlob!=null){ _=SignatureBlob->Release(); }
            if(ErrorBlob!=null){ _=ErrorBlob->Release(); }
        }
    }

    private void CreatePipelineState(){
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PNormal="NORMAL\0"u8)
        fixed(byte*PTangent="TANGENT\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PVertexShader=VertexShaderBytecode)
        fixed(byte*PPixelShader=PixelShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[4]{
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PPosition,
                    Format=DXGI_FORMAT_R32G32B32_FLOAT,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PNormal,
                    Format=DXGI_FORMAT_R32G32B32_FLOAT,
                    AlignedByteOffset=12,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PTangent,
                    Format=DXGI_FORMAT_R32G32B32A32_FLOAT,
                    AlignedByteOffset=24,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PTexCoord,
                    Format=DXGI_FORMAT_R32G32_FLOAT,
                    AlignedByteOffset=40,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
            };

            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC {
                    pInputElementDescs=InputElements,
                    NumElements=4,
                },
                pRootSignature=RootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)VertexShaderBytecode.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)PixelShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=1,
                DSVFormat=DepthBufferFormat,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            // Real backface culling is deferred, same rationale as ModelRenderer (ADR-0004).
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RTVFormats[0]=HdrColorFormat;

            ID3D12PipelineState*PipelineState;
            D3D12Diagnostics.EnsureSuccess(
                Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&PipelineState),
                "ID3D12Device::CreateGraphicsPipelineState","creating the stage pipeline state");
            this.PipelineState=PipelineState;
        }
    }

    private void CreateShadowPipelineState(){
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PNormal="NORMAL\0"u8)
        fixed(byte*PTangent="TANGENT\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PVertexShader=ShadowVertexShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[4]{
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PPosition,Format=DXGI_FORMAT_R32G32B32_FLOAT,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PNormal,Format=DXGI_FORMAT_R32G32B32_FLOAT,AlignedByteOffset=12,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PTangent,Format=DXGI_FORMAT_R32G32B32A32_FLOAT,AlignedByteOffset=24,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PTexCoord,Format=DXGI_FORMAT_R32G32_FLOAT,AlignedByteOffset=40,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
            };
            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC { pInputElementDescs=InputElements,NumElements=4 },
                pRootSignature=ShadowRootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)ShadowVertexShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=0,
                DSVFormat=ShadowMapDsvFormat,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),nameof(CreateShadowPipelineState),nameof(CreateShadowPipelineState));
            ShadowPipelineState=Pipeline;
        }
    }

    private void CreatePostProcessPipelineState(){
        fixed(byte*PVertexShader=FullscreenVertexShaderBytecode)
        fixed(byte*PPixelShader=PostProcessPixelShaderBytecode){
            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                pRootSignature=PostProcessRootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)FullscreenVertexShaderBytecode.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)PostProcessPixelShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=1,
                RTVFormats=default,
                DSVFormat=DXGI_FORMAT_UNKNOWN,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            PsoDesc.DepthStencilState.DepthEnable=FALSE;
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RTVFormats[0]=BackBufferFormat;

            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(
                Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                nameof(CreatePostProcessPipelineState),nameof(CreatePostProcessPipelineState));
            PostProcessPipelineState=Pipeline;
        }
    }

    private void CreateMeshBuffers(IReadOnlyList<StageMeshCreateInfo>Meshes){
        MeshCount=Meshes.Count;
        VertexBuffers=new ID3D12Resource*[MeshCount];
        VertexBufferViews=new D3D12_VERTEX_BUFFER_VIEW[MeshCount];
        IndexBuffers=new ID3D12Resource*[MeshCount];
        IndexBufferViews=new D3D12_INDEX_BUFFER_VIEW[MeshCount];
        IndexCounts=new uint[MeshCount];

        for(var I=0;I<MeshCount;I++){
            StageMeshCreateInfo Mesh=Meshes[I];
            CreateVertexBuffer(I,Mesh.Positions,Mesh.Normals,Mesh.Tangents,Mesh.Uvs);
            CreateIndexBuffer(I,Mesh.Indices);
        }
    }

    private void CreateVertexBuffer(int MeshIndex,IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,IReadOnlyList<Vector4>Tangents,IReadOnlyList<Vector2>Uvs){
        var VertexCount=Positions.Count;
        Vertex[]Vertices=new Vertex[VertexCount];
        for(var I=0;I<VertexCount;I++){
            Vertices[I]=new Vertex { Position=Positions[I],Normal=Normals[I],Tangent=Tangents[I],Uv=Uvs[I]};
        }

        var BufferSize=(uint)(sizeof(Vertex)*VertexCount);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*VertexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&VertexBuffer),
            "ID3D12Device::CreateCommittedResource","creating a stage vertex buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(VertexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a stage vertex buffer");
        fixed(Vertex*PVertices=Vertices){
            Unsafe.CopyBlock(Mapped,PVertices,BufferSize);
        }
        VertexBuffer->Unmap(0,null);

        VertexBuffers[MeshIndex]=VertexBuffer;
        VertexBufferViews[MeshIndex]=new D3D12_VERTEX_BUFFER_VIEW {
            BufferLocation=VertexBuffer->GetGPUVirtualAddress(),
            StrideInBytes=(uint)sizeof(Vertex),
            SizeInBytes=BufferSize,
        };
    }

    private void CreateIndexBuffer(int MeshIndex,IReadOnlyList<uint>Indices){
        var IndexCount=Indices.Count;
        var IndexArray=Indices as uint[]??[.. Indices];
        var BufferSize=(uint)(sizeof(uint)*IndexCount);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*IndexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&IndexBuffer),
            "ID3D12Device::CreateCommittedResource","creating a stage index buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(IndexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a stage index buffer");
        fixed(uint*PIndices=IndexArray){
            Unsafe.CopyBlock(Mapped,PIndices,BufferSize);
        }
        IndexBuffer->Unmap(0,null);

        IndexBuffers[MeshIndex]=IndexBuffer;
        IndexCounts[MeshIndex]=(uint)IndexCount;
        IndexBufferViews[MeshIndex]=new D3D12_INDEX_BUFFER_VIEW {
            BufferLocation=IndexBuffer->GetGPUVirtualAddress(),
            SizeInBytes=BufferSize,
            Format=DXGI_FORMAT_R32_UINT,
        };
    }

    private void CreateMaterialResources(IReadOnlyList<StageMaterialCreateInfo>Materials){
        MaterialCount=Materials.Count;
        MaterialTextures=new ID3D12Resource*[MaterialCount*3];
        MaterialBaseColorFactors=new Vector4[MaterialCount];
        MaterialRoughnessFactors=new float[MaterialCount];
        MaterialMetallicFactors=new float[MaterialCount];
        MaterialEmissiveFactors=new Vector3[MaterialCount];

        for(var I=0;I<MaterialCount;I++){
            StageMaterialCreateInfo Material=Materials[I];
            MaterialBaseColorFactors[I]=Material.BaseColorFactor;
            MaterialRoughnessFactors[I]=Material.RoughnessFactor;
            MaterialMetallicFactors[I]=Material.MetallicFactor;
            MaterialEmissiveFactors[I]=Material.EmissiveFactor;

            MaterialTextures[(I*3)+0]=CreateTextureResource(Material.BaseColorTextureBytes,WhiteFallback());
            MaterialTextures[(I*3)+1]=CreateTextureResource(Material.NormalTextureBytes,FlatNormalFallback());
            MaterialTextures[(I*3)+2]=CreateTextureResource(Material.MetallicRoughnessTextureBytes,WhiteFallback());
        }

        D3D12_DESCRIPTOR_HEAP_DESC SrvHeapDesc=new(){
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors=(uint)(MaterialCount*4),
            Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap*SrvHeap;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateDescriptorHeap(&SrvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&SrvHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the material SRV descriptor heap");
        this.SrvHeap=SrvHeap;
        SrvDescriptorSize=Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

        D3D12_CPU_DESCRIPTOR_HANDLE SrvHeapStart=SrvHeap->GetCPUDescriptorHandleForHeapStart();
        D3D12_SHADER_RESOURCE_VIEW_DESC SrvDesc=new(){
            Format=MaterialTextureFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        SrvDesc.Texture2D.MipLevels=1;

        for(var I=0;I<MaterialTextures.Length;I++){
            D3D12_CPU_DESCRIPTOR_HANDLE Handle=new(SrvHeapStart,(I/3)*4+(I%3),SrvDescriptorSize);
            Device.Device->CreateShaderResourceView(MaterialTextures[I],&SrvDesc,Handle);
        }

        D3D12_SHADER_RESOURCE_VIEW_DESC ShadowSrvDesc=new(){
            Format=ShadowMapSrvFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        ShadowSrvDesc.Texture2D.MipLevels=1;
        for(var I=0;I<MaterialCount;I++){
            D3D12_CPU_DESCRIPTOR_HANDLE Handle=new(SrvHeapStart,(I*4)+3,SrvDescriptorSize);
            Device.Device->CreateShaderResourceView(ShadowMap,&ShadowSrvDesc,Handle);
        }
    }

    private static DecodedImage WhiteFallback()=>new(1,1,[255,255,255,255]);

    /// <summary>Flat tangent-space normal (0,0,1) encoded as RGB(128,128,255), so an unmapped surface's TBN-transformed normal is exactly its own vertex normal regardless of the (possibly arbitrary) tangent.</summary>
    private static DecodedImage FlatNormalFallback()=>new(1,1,[128,128,255,255]);

    /// <summary>Decodes (or falls back to a procedural default, mirroring <see cref="ModelRenderer"/>'s "every mesh gets a real SRV" rationale) and uploads one material texture slot.</summary>
    private ID3D12Resource*CreateTextureResource(byte[]?EncodedBytes,DecodedImage Fallback){
        DecodedImage Image=DecodeOrFallback(EncodedBytes,Fallback);

        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(MaterialTextureFormat,(uint)Image.Width,(uint)Image.Height,mipLevels:1);

        D3D12_HEAP_PROPERTIES DefaultHeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Texture;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&DefaultHeapProps,D3D12_HEAP_FLAG_NONE,&TextureDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Texture),
            "ID3D12Device::CreateCommittedResource","creating a material texture");

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        Device.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES UploadHeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC StagingDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*Staging;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateCommittedResource(&UploadHeapProps,D3D12_HEAP_FLAG_NONE,&StagingDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Staging),
            "ID3D12Device::CreateCommittedResource","creating a material texture upload staging buffer");

        try {
            // GetCopyableFootprints' RowPitch is 256-byte aligned; the decoded image is tightly
            // packed (stride = Width*4). These differ whenever Width*4 isn't already a multiple of
            // 256, so this must be a per-row copy, not one bulk CopyBlock (same as ModelRenderer).
            D3D12_RANGE NoRead=new();
            byte*Mapped;
            D3D12Diagnostics.EnsureSuccess(Staging->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a material texture upload staging buffer");

            var SrcRowPitch=(uint)Image.Width*4;
            var DstRowPitch=Footprint.Footprint.RowPitch;
            fixed(byte*PSrc=Image.Pixels){
                for(uint Y=0;Y<Image.Height;Y++){
                    Unsafe.CopyBlock(Mapped+(Y*DstRowPitch),PSrc+(Y*SrcRowPitch),SrcRowPitch);
                }
            }
            Staging->Unmap(0,null);

            D3D12Diagnostics.EnsureSuccess(CommandAllocators[0]->Reset(),"ID3D12CommandAllocator::Reset","resetting the allocator for a material texture upload");
            D3D12Diagnostics.EnsureSuccess(CommandList->Reset(CommandAllocators[0],null),"ID3D12GraphicsCommandList::Reset","resetting the command list for a material texture upload");

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource=Texture,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Dst.SubresourceIndex=0;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=Staging,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Src.PlacedFootprint=Footprint;
            CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            D3D12_RESOURCE_BARRIER ToShaderResource=D3D12_RESOURCE_BARRIER.InitTransition(Texture,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            CommandList->ResourceBarrier(1,&ToShaderResource);

            D3D12Diagnostics.EnsureSuccess(CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing a material texture upload command list");

            ID3D12CommandList*CommandListBase=(ID3D12CommandList*)CommandList;
            Device.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

            WaitForGpuIdle();
        } finally {
            _=Staging->Release();
        }

        return Texture;
    }

    private static DecodedImage DecodeOrFallback(byte[]?EncodedBytes,DecodedImage Fallback){
        if(EncodedBytes is null||EncodedBytes.Length==0){
            return Fallback;
        }

        WicDecodeResult DecodeResult=WicImageDecoder.Decode(EncodedBytes);
        return DecodeResult.Success?DecodeResult.Image!:Fallback;
    }

    private void CreateObjectMappings(IReadOnlyList<StageObjectCreateInfo>Objects){
        ObjectCount=Objects.Count;
        ObjectMeshIndices=new int[ObjectCount];
        ObjectMaterialIndices=new int[ObjectCount];
        ObjectTransforms=new Matrix4x4[ObjectCount];

        for(var I=0;I<ObjectCount;I++){
            ObjectMeshIndices[I]=Objects[I].MeshIndex;
            ObjectMaterialIndices[I]=Objects[I].MaterialIndex;
            ObjectTransforms[I]=Matrix4x4.Identity;
        }
    }

    private void CreateConstantBuffers(){
        var FrameBufferSize=FrameConstantsStride*FrameCount;
        CreateUploadBuffer(FrameBufferSize,out FrameConstantsBuffer,out FrameConstantsMapped,"creating the frame constant buffer");

        var ObjectBufferSize=ConstantAlignment*(uint)ObjectCount*FrameCount;
        CreateUploadBuffer(ObjectBufferSize,out ObjectConstantsBuffer,out ObjectConstantsMapped,"creating the object constant buffer");
    }

    private void CreateShadowConstantsBuffer(){
        var ShadowBufferSize=ConstantAlignment*FrameCount;
        CreateUploadBuffer(ShadowBufferSize,out ShadowConstantsBuffer,out ShadowConstantsMapped,nameof(CreateShadowConstantsBuffer));
    }

    private void CreateUploadBuffer(uint Size,out ID3D12Resource*Buffer,out byte*Mapped,string Context){
        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(Size);

        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource",Context);

        D3D12_RANGE NoRead=new();
        byte*Ptr;
        D3D12Diagnostics.EnsureSuccess(Resource->Map(0,&NoRead,(void**)&Ptr),"ID3D12Resource::Map",Context);

        Buffer=Resource;
        Mapped=Ptr;
    }

    /// <summary>
    /// Writes an identity world matrix (with each object's material factors), a default camera,
    /// and default light/ambient/fog values into every frame-in-flight slot, so the very first
    /// Present reads fully-initialized data even if the caller's first SetCamera/SetObjectTransform/
    /// SetDirectionalLight/etc. call happens after this constructor returns.
    /// </summary>
    private void InitializeConstantsToDefaults(){
        Camera DefaultCamera=Camera.CreateDefault(Vector3.Zero);

        for(uint Frame=0;Frame<FrameCount;Frame++){
            WriteViewProjection(Frame,DefaultCamera.GetView()*DefaultCamera.GetProjection((float)Width/Height));
            WriteCameraPosition(Frame,DefaultCamera.GetEyePosition());
            WriteDirectionalLight(Frame,DefaultLightDirectionTowardLight,DefaultLightColor);
            WriteAmbientLight(Frame,DefaultAmbientColor);
            WriteFog(Frame,FogParams.None);
            WriteShadowViewProjection(Frame,BuildShadowViewProjection(DefaultLightDirectionTowardLight));
            WriteLightCounts(Frame); // D3D12 does not guarantee zero-initialized upload-heap memory; without this the very first frame could loop over garbage PointLights/SpotLights entries.

            for(var ObjectIndex=0;ObjectIndex<ObjectCount;ObjectIndex++){
                WriteObjectConstants(Frame,ObjectIndex);
            }
        }
    }

    /// <summary>
    /// Sets the camera used for the frame currently being built. Must be called every frame,
    /// before <see cref="RenderFrame"/> - the constant buffer is double-buffered per frame-in-flight
    /// slot (<see cref="FrameCount"/>) and only the current slot is written here.
    /// </summary>
    public void SetCamera(in Camera Camera,float AspectRatioOverride=0f){
        var AspectRatio=AspectRatioOverride>0f?AspectRatioOverride:(float)Width/Height;
        WriteViewProjection(FrameIndex,Camera.GetView()*Camera.GetProjection(AspectRatio));
        WriteCameraPosition(FrameIndex,Camera.GetEyePosition());
    }

    /// <summary>Sets the single data-driven directional light (sun) for the frame currently being built. <paramref name="DirectionTowardLight"/> points from the scene toward the light (i.e. the direction surfaces should face to be lit), not the direction light travels.</summary>
    public void SetDirectionalLight(Vector3 DirectionTowardLight,Vector3 Color,float Intensity){
        WriteDirectionalLight(FrameIndex,DirectionTowardLight,Color*Intensity);
        WriteShadowViewProjection(FrameIndex,BuildShadowViewProjection(DirectionTowardLight));
    }

    /// <summary>Sets the flat ambient term (a stand-in for sky/indirect lighting) for the frame currently being built.</summary>
    public void SetAmbientLight(Vector3 Color){
        WriteAmbientLight(FrameIndex,Color);
    }

    /// <summary>Sets the simplified exponential height fog for the frame currently being built. Pass <see cref="FogParams.None"/> to disable it (zero density).</summary>
    public void SetFog(FogParams Fog){
        WriteFog(FrameIndex,Fog);
    }

    /// <summary>
    /// Adds one point light (radiates equally in all directions, attenuated by distance with a
    /// smooth falloff that reaches zero at <paramref name="Range"/>) for the frame currently being
    /// built. Unlike <see cref="SetDirectionalLight"/>, this is additive per frame: call it once per
    /// active light, up to <see cref="MaxPointLights"/> - the count resets to zero at the end of
    /// <see cref="RenderFrame"/>, so a light that stops calling this simply stops contributing on
    /// the next frame (no stale state to clear).
    /// </summary>
    public void AddPointLight(Vector3 Position,Vector3 Color,float Intensity,float Range){
        if(PointLightCount>=MaxPointLights){
            Debug.Assert(false, $"CGE-STAGE-LIGHT-001: exceeded MaxPointLights ({MaxPointLights}); extra point lights are silently dropped this frame.");
            return;
        }

        WritePointLight(FrameIndex,PointLightCount,Position,Range,Color*Intensity);
        PointLightCount++;
        WriteLightCounts(FrameIndex);
    }

    /// <summary>
    /// Adds one spot light (a point light additionally narrowed to a cone) for the frame currently
    /// being built. <paramref name="Direction"/> is the direction the cone points toward (need not
    /// be normalized) and the brightness falls off smoothly between
    /// <paramref name="InnerConeAngleRadians"/> (full brightness) and
    /// <paramref name="OuterConeAngleRadians"/> (zero). Additive per frame - see
    /// <see cref="AddPointLight"/> for the accumulate/reset contract.
    /// </summary>
    public void AddSpotLight(Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,float InnerConeAngleRadians,float OuterConeAngleRadians){
        if(SpotLightCount>=MaxSpotLights){
            Debug.Assert(false, $"CGE-STAGE-LIGHT-002: exceeded MaxSpotLights ({MaxSpotLights}); extra spot lights are silently dropped this frame.");
            return;
        }

        WriteSpotLight(FrameIndex,SpotLightCount,Position,Range,Vector3.Normalize(Direction),MathF.Cos(InnerConeAngleRadians),Color*Intensity,MathF.Cos(OuterConeAngleRadians));
        SpotLightCount++;
        WriteLightCounts(FrameIndex);
    }

    /// <summary>Sets the world transform for one object for the frame currently being built (see <see cref="SetCamera"/> for the per-frame-slot contract).</summary>
    public void SetObjectTransform(int ObjectIndex,Matrix4x4 World){
        if(ObjectIndex<0||ObjectIndex>=ObjectCount){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }

        ObjectTransforms[ObjectIndex]=World;
        WriteObjectConstants(FrameIndex,ObjectIndex);
    }

    private byte*FrameSlot(uint FrameIndex)=>FrameConstantsMapped+(FrameIndex*FrameConstantsStride);

    private void WriteViewProjection(uint FrameIndex,Matrix4x4 ViewProjection){
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameViewProjectionOffset,&ViewProjection,(uint)sizeof(Matrix4x4));
    }

    private void WriteCameraPosition(uint FrameIndex,Vector3 CameraPosition){
        Vector4 Packed=new(CameraPosition,0f);
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameCameraPositionOffset,&Packed,(uint)sizeof(Vector4));
    }

    private void WriteDirectionalLight(uint FrameIndex,Vector3 DirectionTowardLight,Vector3 ColorTimesIntensity){
        Vector4 Direction=new(Vector3.Normalize(DirectionTowardLight),0f);
        Vector4 Color=new(ColorTimesIntensity,0f);
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameLightDirectionOffset,&Direction,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameLightColorOffset,&Color,(uint)sizeof(Vector4));
    }

    private void WriteAmbientLight(uint FrameIndex,Vector3 Color){
        Vector4 Packed=new(Color,0f);
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameAmbientColorOffset,&Packed,(uint)sizeof(Vector4));
    }

    private void WriteFog(uint FrameIndex,FogParams Fog){
        Vector4 FogParams0=new(Fog.Density,Fog.HeightFalloff,0f,0f);
        Vector4 FogColor=new(Fog.Color,0f);
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameFogParamsOffset,&FogParams0,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameFogColorOffset,&FogColor,(uint)sizeof(Vector4));
    }

    private static Matrix4x4 BuildShadowViewProjection(Vector3 DirectionTowardLight){
        Vector3 Direction=Vector3.Normalize(DirectionTowardLight);
        Matrix4x4 View=Matrix4x4.CreateLookAt(-(Direction*35f),Vector3.Zero,Vector3.UnitY);
        return View*Matrix4x4.CreateOrthographic(40f,40f,0.1f,80f);
    }

    private void WriteShadowViewProjection(uint FrameIndex,Matrix4x4 ShadowViewProjection){
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameShadowViewProjectionOffset,&ShadowViewProjection,(uint)sizeof(Matrix4x4));
        Unsafe.CopyBlock(ShadowConstantsMapped+(FrameIndex*ConstantAlignment),&ShadowViewProjection,(uint)sizeof(Matrix4x4));
    }

    /// <summary>Writes Stage.hlsl's PointLightData[index] (2 float4: position+range, color*intensity).</summary>
    private void WritePointLight(uint FrameIndex,int Index,Vector3 Position,float Range,Vector3 ColorTimesIntensity){
        var Destination=FrameSlot(FrameIndex)+FramePointLightsOffset+((uint)Index*PointLightStride);
        Vector4 PositionAndRange=new(Position,Range);
        Vector4 ColorPacked=new(ColorTimesIntensity,0f);
        Unsafe.CopyBlock(Destination,&PositionAndRange,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+16,&ColorPacked,(uint)sizeof(Vector4));
    }

    /// <summary>Writes Stage.hlsl's SpotLightData[index] (3 float4: position+range, direction+innerCos, color*intensity+outerCos).</summary>
    private void WriteSpotLight(uint FrameIndex,int Index,Vector3 Position,float Range,Vector3 Direction,float InnerConeCos,Vector3 ColorTimesIntensity,float OuterConeCos){
        var Destination=FrameSlot(FrameIndex)+FrameSpotLightsOffset+((uint)Index*SpotLightStride);
        Vector4 PositionAndRange=new(Position,Range);
        Vector4 DirectionAndInnerCos=new(Direction,InnerConeCos);
        Vector4 ColorAndOuterCos=new(ColorTimesIntensity,OuterConeCos);
        Unsafe.CopyBlock(Destination,&PositionAndRange,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+16,&DirectionAndInnerCos,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+32,&ColorAndOuterCos,(uint)sizeof(Vector4));
    }

    /// <summary>Writes Stage.hlsl's FrameCB.LightCounts (int4: x=active point lights, y=active spot lights).</summary>
    private void WriteLightCounts(uint FrameIndex){
        var Counts=stackalloc int[4]{ PointLightCount,SpotLightCount,0,0 };
        Unsafe.CopyBlock(FrameSlot(FrameIndex)+FrameLightCountsOffset,Counts,16);
    }

    private void WriteObjectConstants(uint FrameIndex,int ObjectIndex){
        var Slot=((ulong)FrameIndex*(ulong)ObjectCount)+(ulong)ObjectIndex;
        var Destination=ObjectConstantsMapped+(Slot*ConstantAlignment);

        Matrix4x4 World=ObjectTransforms[ObjectIndex];
        Unsafe.CopyBlock(Destination+ObjectWorldOffset,&World,(uint)sizeof(Matrix4x4));

        var MaterialIndex=ObjectMaterialIndices[ObjectIndex];
        Vector4 BaseColorFactor=MaterialBaseColorFactors[MaterialIndex];
        Unsafe.CopyBlock(Destination+ObjectBaseColorFactorOffset,&BaseColorFactor,(uint)sizeof(Vector4));

        Vector4 MaterialParams=new(MaterialRoughnessFactors[MaterialIndex],MaterialMetallicFactors[MaterialIndex],0f,0f);
        Unsafe.CopyBlock(Destination+ObjectMaterialParamsOffset,&MaterialParams,(uint)sizeof(Vector4));

        Vector4 EmissiveFactor=new(MaterialEmissiveFactors[MaterialIndex],1f);
        Unsafe.CopyBlock(Destination+ObjectEmissiveFactorOffset,&EmissiveFactor,(uint)sizeof(Vector4));
    }

    private ulong GetFrameConstantsAddress(uint FrameIndex){
        return FrameConstantsBuffer->GetGPUVirtualAddress()+(FrameIndex*FrameConstantsStride);
    }

    private ulong GetObjectConstantsAddress(uint FrameIndex,int ObjectIndex){
        var Slot=((ulong)FrameIndex*(ulong)ObjectCount)+(ulong)ObjectIndex;
        return ObjectConstantsBuffer->GetGPUVirtualAddress()+(Slot*ConstantAlignment);
    }

    private ulong GetShadowConstantsAddress(uint FrameIndex)=>
        ShadowConstantsBuffer->GetGPUVirtualAddress()+(FrameIndex*ConstantAlignment);

    private D3D12_GPU_DESCRIPTOR_HANDLE GetMaterialDescriptorTable(int MaterialIndex){
        return new D3D12_GPU_DESCRIPTOR_HANDLE(SrvHeap->GetGPUDescriptorHandleForHeapStart(),MaterialIndex*4,SrvDescriptorSize);
    }

    private void UpdateViewportAndScissor(int Width,int Height){
        Viewport=new D3D12_VIEWPORT {
            TopLeftX=0,
            TopLeftY=0,
            Width=Width,
            Height=Height,
            MinDepth=0.0f,
            MaxDepth=1.0f,
        };

        ScissorRect=new RECT { left=0,top=0,right=Width,bottom=Height };
        this.Width=Width;
        this.Height=Height;
    }

    public void Resize(int Width,int Height){
        if(Width<=0||Height<=0){
            SizeIsZero=true;
            return;
        }

        SizeIsZero=false;

        if(this.Width==Width&&this.Height==Height){
            return;
        }

        WaitForGpuIdle();

        // See ModelRenderer.Resize / ADR-0003 §8: ResizeBuffers restarts the swap chain's
        // back-buffer index cycle, which would otherwise desynchronize the per-slot fence values.
        var SyncedFenceValue=FenceValues[FrameIndex];
        for(var I=0;I<FrameCount;I++){
            FenceValues[I]=SyncedFenceValue;
        }

        for(var I=0;I<FrameCount;I++){
            if(BackBuffers[I]!=null){
                _=BackBuffers[I]->Release();
                BackBuffers[I]=null;
            }
        }

        ReleaseReadbackBuffer();

        if(DepthBuffer!=null){
            _=DepthBuffer->Release();
            DepthBuffer=null;
        }

        if(HdrColorBuffer!=null){
            _=HdrColorBuffer->Release();
            HdrColorBuffer=null;
        }

        D3D12Diagnostics.EnsureSuccess(
            SwapChain->ResizeBuffers(FrameCount,(uint)Width,(uint)Height,BackBufferFormat,0),
            "IDXGISwapChain3::ResizeBuffers","resizing the swap chain");

        CreateRenderTargetViews();
        CreateDepthBuffer(Width,Height);
        CreateHdrColorBuffer(Width,Height);
        FrameIndex=SwapChain->GetCurrentBackBufferIndex();
        UpdateViewportAndScissor(Width,Height);
    }

    private void RenderShadowPass(){
        D3D12_RESOURCE_BARRIER ToDepth=D3D12_RESOURCE_BARRIER.InitTransition(ShadowMap,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        CommandList->ResourceBarrier(1,&ToDepth);
        D3D12_CPU_DESCRIPTOR_HANDLE ShadowDsv=new(DsvHeap->GetCPUDescriptorHandleForHeapStart(),1,DsvDescriptorSize);
        CommandList->OMSetRenderTargets(0,null,FALSE,&ShadowDsv);
        CommandList->ClearDepthStencilView(ShadowDsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1.0f,0,0,null);
        CommandList->SetGraphicsRootSignature(ShadowRootSignature);
        CommandList->SetPipelineState(ShadowPipelineState);
        CommandList->SetGraphicsRootConstantBufferView(0,GetShadowConstantsAddress(FrameIndex));
        D3D12_VIEWPORT ShadowViewport=new(){ Width=ShadowMapResolution,Height=ShadowMapResolution,MinDepth=0f,MaxDepth=1f };
        RECT ShadowScissor=new(){ left=0,top=0,right=ShadowMapResolution,bottom=ShadowMapResolution };
        CommandList->RSSetViewports(1,&ShadowViewport);
        CommandList->RSSetScissorRects(1,&ShadowScissor);
        CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        for(var I=0;I<ObjectCount;I++){
            var MeshIndex=ObjectMeshIndices[I];
            CommandList->SetGraphicsRootConstantBufferView(1,GetObjectConstantsAddress(FrameIndex,I));
            fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&VertexBufferViews[MeshIndex])
            fixed(D3D12_INDEX_BUFFER_VIEW*PIndexBufferView=&IndexBufferViews[MeshIndex]){
                CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
                CommandList->IASetIndexBuffer(PIndexBufferView);
                CommandList->DrawIndexedInstanced(IndexCounts[MeshIndex],1,0,0,0);
            }
        }
        D3D12_RESOURCE_BARRIER ToShader=D3D12_RESOURCE_BARRIER.InitTransition(ShadowMap,D3D12_RESOURCE_STATE_DEPTH_WRITE,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        CommandList->ResourceBarrier(1,&ToShader);
    }

    public void RenderFrame(bool CaptureForVerification=false){
        if(SizeIsZero){
            return;
        }

        ID3D12CommandAllocator*Allocator=CommandAllocators[FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Allocator->Reset(),"ID3D12CommandAllocator::Reset","resetting the frame command allocator");
        D3D12Diagnostics.EnsureSuccess(CommandList->Reset(Allocator,PipelineState),"ID3D12GraphicsCommandList::Reset","resetting the graphics command list");

        RenderShadowPass();

        ID3D12Resource*BackBuffer=BackBuffers[FrameIndex];

        D3D12_RESOURCE_BARRIER ToHdrRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(HdrColorBuffer,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        CommandList->ResourceBarrier(1,&ToHdrRenderTarget);

        D3D12_CPU_DESCRIPTOR_HANDLE HdrRtvHandle=new(RtvHeap->GetCPUDescriptorHandleForHeapStart(),FrameCount,RtvDescriptorSize);
        D3D12_CPU_DESCRIPTOR_HANDLE DsvHandle=DsvHeap->GetCPUDescriptorHandleForHeapStart();
        CommandList->OMSetRenderTargets(1,&HdrRtvHandle,FALSE,&DsvHandle);

        Vector4 ClearColor=ClearColorVector;
        CommandList->ClearRenderTargetView(HdrRtvHandle,(float*)&ClearColor,0,null);
        CommandList->ClearDepthStencilView(DsvHandle,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1.0f,0,0,null);

        CommandList->SetGraphicsRootSignature(RootSignature);
        CommandList->SetPipelineState(PipelineState);

        ID3D12DescriptorHeap*SrvHeap=this.SrvHeap;
        CommandList->SetDescriptorHeaps(1,&SrvHeap);
        CommandList->SetGraphicsRootConstantBufferView(0,GetFrameConstantsAddress(FrameIndex));

        fixed(D3D12_VIEWPORT*PViewport=&Viewport)
        fixed(RECT*PScissorRect=&ScissorRect){
            CommandList->RSSetViewports(1,PViewport);
            CommandList->RSSetScissorRects(1,PScissorRect);
        }

        CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        for(var I=0;I<ObjectCount;I++){
            var MeshIndex=ObjectMeshIndices[I];
            CommandList->SetGraphicsRootConstantBufferView(1,GetObjectConstantsAddress(FrameIndex,I));
            CommandList->SetGraphicsRootDescriptorTable(2,GetMaterialDescriptorTable(ObjectMaterialIndices[I]));

            fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&VertexBufferViews[MeshIndex])
            fixed(D3D12_INDEX_BUFFER_VIEW*PIndexBufferView=&IndexBufferViews[MeshIndex]){
                CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
                CommandList->IASetIndexBuffer(PIndexBufferView);
                CommandList->DrawIndexedInstanced(IndexCounts[MeshIndex],1,0,0,0);
            }
        }

        D3D12_RESOURCE_BARRIER ToHdrShaderResource=D3D12_RESOURCE_BARRIER.InitTransition(HdrColorBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        CommandList->ResourceBarrier(1,&ToHdrShaderResource);

        D3D12_RESOURCE_BARRIER ToBackBufferRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
        CommandList->ResourceBarrier(1,&ToBackBufferRenderTarget);
        D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvHeap->GetCPUDescriptorHandleForHeapStart(),(int)FrameIndex,RtvDescriptorSize);
        CommandList->OMSetRenderTargets(1,&RtvHandle,FALSE,null);
        CommandList->SetGraphicsRootSignature(PostProcessRootSignature);
        CommandList->SetPipelineState(PostProcessPipelineState);
        ID3D12DescriptorHeap*PostSrvHeap=HdrSrvHeap;
        CommandList->SetDescriptorHeaps(1,&PostSrvHeap);
        CommandList->SetGraphicsRootDescriptorTable(0,HdrSrvHeap->GetGPUDescriptorHandleForHeapStart());
        fixed(D3D12_VIEWPORT*PViewport=&Viewport)
        fixed(RECT*PScissorRect=&ScissorRect){
            CommandList->RSSetViewports(1,PViewport);
            CommandList->RSSetScissorRects(1,PScissorRect);
        }
        CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        CommandList->DrawInstanced(3,1,0,0);

        if(CaptureForVerification){
            EnsureReadbackBuffer();

            D3D12_RESOURCE_BARRIER ToCopySource=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_COPY_SOURCE);
            CommandList->ResourceBarrier(1,&ToCopySource);

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource=ReadbackBuffer,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Dst.PlacedFootprint=ReadbackFootprint;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=BackBuffer,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Src.SubresourceIndex=0;
            CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            D3D12_RESOURCE_BARRIER ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_PRESENT);
            CommandList->ResourceBarrier(1,&ToPresent);
        } else {
            D3D12_RESOURCE_BARRIER ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);
            CommandList->ResourceBarrier(1,&ToPresent);
        }

        D3D12Diagnostics.EnsureSuccess(CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the frame command list");

        ID3D12CommandList*CommandListBase=(ID3D12CommandList*)CommandList;
        Device!.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

        D3D12Diagnostics.EnsureSuccess(SwapChain->Present(1,0),"IDXGISwapChain3::Present","presenting the frame");

        MoveToNextFrame();
        Device.PollDebugMessages();
        FramesRendered++;

        // The point/spot light counts (and the data they gate) have already been submitted to the
        // GPU above; reset so the next frame starts from zero and only reflects lights that call
        // AddPointLight/AddSpotLight again this frame (see the fields' doc comment).
        PointLightCount=0;
        SpotLightCount=0;

        if(CaptureForVerification){
            WaitForGpuIdle();
            LastCapturedFrame=ReadBackCapturedFrame();
        }
    }

    private void EnsureReadbackBuffer(){
        if(this.ReadbackBuffer!=null){
            return;
        }

        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(BackBufferFormat,(uint)Width,(uint)Height,mipLevels:1);

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        Device!.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_READBACK);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*ReadbackBuffer;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&ReadbackBuffer),
            "ID3D12Device::CreateCommittedResource","creating the pixel-verification readback buffer");

        this.ReadbackBuffer=ReadbackBuffer;
        ReadbackBufferSize=TotalBytes;
        ReadbackFootprint=Footprint;
    }

    private void ReleaseReadbackBuffer(){
        if(ReadbackBuffer!=null){
            _=ReadbackBuffer->Release();
            ReadbackBuffer=null;
            ReadbackBufferSize=0;
        }
    }

    private PixelReadbackResult ReadBackCapturedFrame(){
        D3D12_RANGE ReadRange=new(){ Begin=0,End=(nuint)ReadbackBufferSize };
        void*Mapped;
        D3D12Diagnostics.EnsureSuccess(ReadbackBuffer->Map(0,&ReadRange,&Mapped),"ID3D12Resource::Map","mapping the readback buffer");

        var Data=new byte[ReadbackBufferSize];
        fixed(byte*PData=Data){
            Unsafe.CopyBlock(PData,Mapped,(uint)ReadbackBufferSize);
        }

        D3D12_RANGE WrittenRange=new();
        ReadbackBuffer->Unmap(0,&WrittenRange);

        return new PixelReadbackResult {
            Width=Width,
            Height=Height,
            RowPitch=ReadbackFootprint.Footprint.RowPitch,
            Format=BackBufferFormat,
            Pixels=Data,
        };
    }

    private void MoveToNextFrame(){
        var CurrentFenceValue=FenceValues[FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Device!.CommandQueue->Signal(Fence,CurrentFenceValue),"ID3D12CommandQueue::Signal","signaling the frame fence");

        FrameIndex=SwapChain->GetCurrentBackBufferIndex();

        if(Fence->GetCompletedValue()<FenceValues[FrameIndex]){
            D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(FenceValues[FrameIndex],FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the next frame slot");
            _=WaitForSingleObjectEx(FenceEvent,INFINITE,FALSE);
        }

        FenceValues[FrameIndex]=CurrentFenceValue+1;
    }

    private void WaitForGpuIdle(){
        if(Device==null||Fence==null){
            return;
        }

        D3D12Diagnostics.EnsureSuccess(Device.CommandQueue->Signal(Fence,FenceValues[FrameIndex]),"ID3D12CommandQueue::Signal","signaling the GPU-idle fence");
        D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(FenceValues[FrameIndex],FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the GPU to go idle");
        _=WaitForSingleObjectEx(FenceEvent,INFINITE,FALSE);
        FenceValues[FrameIndex]++;
    }

    public void Dispose(){
        if(Disposed){
            return;
        }

        Disposed=true;

        WaitForGpuIdle();

        ReleaseReadbackBuffer();

        if(FrameConstantsBuffer!=null){
            FrameConstantsBuffer->Unmap(0,null);
            FrameConstantsMapped=null;
            _=FrameConstantsBuffer->Release();
            FrameConstantsBuffer=null;
        }

        if(ObjectConstantsBuffer!=null){
            ObjectConstantsBuffer->Unmap(0,null);
            ObjectConstantsMapped=null;
            _=ObjectConstantsBuffer->Release();
            ObjectConstantsBuffer=null;
        }

        if(ShadowConstantsBuffer!=null){
            ShadowConstantsBuffer->Unmap(0,null);
            ShadowConstantsMapped=null;
            _=ShadowConstantsBuffer->Release();
            ShadowConstantsBuffer=null;
        }

        if(HdrColorBuffer!=null){
            _=HdrColorBuffer->Release();
            HdrColorBuffer=null;
        }

        if(HdrSrvHeap!=null){
            _=HdrSrvHeap->Release();
            HdrSrvHeap=null;
        }

        if(SrvHeap!=null){
            _=SrvHeap->Release();
            SrvHeap=null;
        }

        for(var I=0;I<MaterialTextures.Length;I++){
            if(MaterialTextures[I]!=null){
                _=MaterialTextures[I]->Release();
                MaterialTextures[I]=null;
            }
        }

        for(var I=0;I<IndexBuffers.Length;I++){
            if(IndexBuffers[I]!=null){
                _=IndexBuffers[I]->Release();
                IndexBuffers[I]=null;
            }
        }

        for(var I=0;I<VertexBuffers.Length;I++){
            if(VertexBuffers[I]!=null){
                _=VertexBuffers[I]->Release();
                VertexBuffers[I]=null;
            }
        }

        if(PipelineState!=null){
            _=PipelineState->Release();
            PipelineState=null;
        }

        if(ShadowPipelineState!=null){
            _=ShadowPipelineState->Release();
            ShadowPipelineState=null;
        }

        if(PostProcessPipelineState!=null){
            _=PostProcessPipelineState->Release();
            PostProcessPipelineState=null;
        }

        if(RootSignature!=null){
            _=RootSignature->Release();
            RootSignature=null;
        }

        if(ShadowRootSignature!=null){
            _=ShadowRootSignature->Release();
            ShadowRootSignature=null;
        }

        if(PostProcessRootSignature!=null){
            _=PostProcessRootSignature->Release();
            PostProcessRootSignature=null;
        }

        if(FenceEvent!=HANDLE.NULL){
            _=CloseHandle(FenceEvent);
            FenceEvent=HANDLE.NULL;
        }

        if(Fence!=null){
            _=Fence->Release();
            Fence=null;
        }

        if(CommandList!=null){
            _=CommandList->Release();
            CommandList=null;
        }

        for(var I=0;I<FrameCount;I++){
            if(CommandAllocators[I]!=null){
                _=CommandAllocators[I]->Release();
                CommandAllocators[I]=null;
            }
        }

        if(DepthBuffer!=null){
            _=DepthBuffer->Release();
            DepthBuffer=null;
        }

        if(ShadowMap!=null){
            _=ShadowMap->Release();
            ShadowMap=null;
        }

        if(DsvHeap!=null){
            _=DsvHeap->Release();
            DsvHeap=null;
        }

        for(var I=0;I<FrameCount;I++){
            if(BackBuffers[I]!=null){
                _=BackBuffers[I]->Release();
                BackBuffers[I]=null;
            }
        }

        if(RtvHeap!=null){
            _=RtvHeap->Release();
            RtvHeap=null;
        }

        if(SwapChain!=null){
            _=SwapChain->Release();
            SwapChain=null;
        }

        Device?.Dispose();
        Device=null;
    }
}
