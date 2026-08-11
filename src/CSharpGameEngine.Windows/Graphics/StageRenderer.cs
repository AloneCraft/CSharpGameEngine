using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Compiler.Hlsl;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
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
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D_ROOT_SIGNATURE_VERSION;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.DirectX.DXGI_SWAP_EFFECT;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>One procedural or imported mesh plus its solid base color, for the legacy (pre-ADR-0018) untextured API. Kept only as an input shape for <see cref="StageRenderer.Create(nint, int, int, GraphicsDeviceOptions, string, IReadOnlyList{MeshCreateInfo})"/>, which adapts it into one <see cref="StageMeshCreateInfo"/>+<see cref="StageMaterialCreateInfo"/>+<see cref="StageObjectCreateInfo"/> triple per entry.</summary>
public readonly struct MeshCreateInfo
{
    public required IReadOnlyList<Vector3> Positions { get; init; }
    public required IReadOnlyList<Vector3> Normals { get; init; }
    public required IReadOnlyList<uint> Indices { get; init; }
    public required Vector4 BaseColorFactor { get; init; }
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
public sealed unsafe class StageRenderer : IDisposable
{
    public const int FrameCount = 2;
    public const DXGI_FORMAT BackBufferFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
    public const DXGI_FORMAT DepthBufferFormat = DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT MaterialTextureFormat = DXGI_FORMAT_R8G8B8A8_UNORM;

    /// <summary>D3D12 constant buffer views must start at a 256-byte aligned GPU address.</summary>
    private const uint ConstantAlignment = 512;

    /// <summary>Fixed capacity, no light culling/clustering in this vertical slice (ADR-0037). Must match Stage.hlsl's MAX_POINT_LIGHTS.</summary>
    public const int MaxPointLights = 4;

    /// <summary>Fixed capacity, no light culling/clustering in this vertical slice (ADR-0037). Must match Stage.hlsl's MAX_SPOT_LIGHTS.</summary>
    public const int MaxSpotLights = 4;

    private const uint PointLightStride = 32; // 2 float4 - see Stage.hlsl's PointLightData
    private const uint SpotLightStride = 48;  // 3 float4 - see Stage.hlsl's SpotLightData

    // FrameCB (b0) byte layout - see Stage.hlsl. Each Set*/Add* method writes only its own slice,
    // so SetCamera/SetDirectionalLight/SetAmbientLight/SetFog/AddPointLight/AddSpotLight may be
    // called in any order per frame.
    private const uint FrameViewProjectionOffset = 0;   // float4x4, 64 bytes
    private const uint FrameCameraPositionOffset = 64;  // float4 (xyz used)
    private const uint FrameLightDirectionOffset = 80;  // float4 (xyz used)
    private const uint FrameLightColorOffset = 96;      // float4 (xyz used)
    private const uint FrameAmbientColorOffset = 112;   // float4 (xyz used)
    private const uint FrameFogParamsOffset = 128;      // float4: x=density, y=heightFalloff
    private const uint FrameFogColorOffset = 144;       // float4 (xyz used)
    private const uint FrameLightCountsOffset = 160;    // int4: x=active point lights, y=active spot lights
    private const uint FramePointLightsOffset = 176;    // MaxPointLights * PointLightStride bytes
    private const uint FrameSpotLightsOffset = FramePointLightsOffset + (MaxPointLights * PointLightStride); // MaxSpotLights * SpotLightStride bytes
    private const uint FrameConstantsSize = FrameSpotLightsOffset + (MaxSpotLights * SpotLightStride);

    // ObjectCB (b1) byte layout - see Stage.hlsl.
    private const uint ObjectWorldOffset = 0;            // float4x4, 64 bytes
    private const uint ObjectBaseColorFactorOffset = 64; // float4
    private const uint ObjectMaterialParamsOffset = 80;  // float4: x=roughness, y=metallic

    private static readonly Vector4 ClearColorVector = new(0.35f, 0.55f, 0.85f, 1.0f);
    public static readonly PixelColor ClearColor = new(89, 140, 217, 255);

    private static readonly Vector3 DefaultLightDirectionTowardLight = Vector3.Normalize(new Vector3(0.4f, 0.7f, 0.5f));
    private static readonly Vector3 DefaultLightColor = new(1.0f, 1.0f, 1.0f);
    private static readonly Vector3 DefaultAmbientColor = new(0.12f, 0.12f, 0.15f);

    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector2 Uv;
    }

    private GraphicsDevice? _device;
    private IDXGISwapChain3* _swapChain;
    private ID3D12DescriptorHeap* _rtvHeap;
    private uint _rtvDescriptorSize;
    private readonly ID3D12Resource*[] _backBuffers = new ID3D12Resource*[FrameCount];
    private ID3D12DescriptorHeap* _dsvHeap;
    private ID3D12Resource* _depthBuffer;
    private readonly ID3D12CommandAllocator*[] _commandAllocators = new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList* _commandList;
    private ID3D12Fence* _fence;
    private readonly ulong[] _fenceValues = new ulong[FrameCount];
    private HANDLE _fenceEvent;
    private ID3D12RootSignature* _rootSignature;
    private ID3D12PipelineState* _pipelineState;

    // Per-mesh (indexed by meshIndex).
    private int _meshCount;
    private ID3D12Resource*[] _vertexBuffers = [];
    private D3D12_VERTEX_BUFFER_VIEW[] _vertexBufferViews = [];
    private ID3D12Resource*[] _indexBuffers = [];
    private D3D12_INDEX_BUFFER_VIEW[] _indexBufferViews = [];
    private uint[] _indexCounts = [];

    // Per-material (indexed by materialIndex). Three textures per material: albedo, normal, metallic-roughness.
    private int _materialCount;
    private ID3D12Resource*[] _materialTextures = [];
    private ID3D12DescriptorHeap* _srvHeap;
    private uint _srvDescriptorSize;
    private Vector4[] _materialBaseColorFactors = [];
    private float[] _materialRoughnessFactors = [];
    private float[] _materialMetallicFactors = [];

    // Per-object (indexed by objectIndex).
    private int _objectCount;
    private int[] _objectMeshIndices = [];
    private int[] _objectMaterialIndices = [];
    private Matrix4x4[] _objectTransforms = [];

    private ID3D12Resource* _frameConstantsBuffer;
    private byte* _frameConstantsMapped;
    private ID3D12Resource* _objectConstantsBuffer;
    private byte* _objectConstantsMapped;

    // Point/spot lights are additive per frame (AddPointLight/AddSpotLight), unlike the singleton
    // directional light/ambient/fog which are overwritten in place. Counts reset to zero at the end
    // of RenderFrame so a light that stops calling Add (its owning Entity went away) doesn't linger.
    private int _pointLightCount;
    private int _spotLightCount;

    private ID3D12Resource* _readbackBuffer;
    private ulong _readbackBufferSize;
    private D3D12_PLACED_SUBRESOURCE_FOOTPRINT _readbackFootprint;

    private uint _frameIndex;
    private int _width;
    private int _height;
    private bool _sizeIsZero;
    private bool _disposed;

    private D3D12_VIEWPORT _viewport;
    private RECT _scissorRect;

    public string AdapterDescription => _device?.AdapterDescription ?? string.Empty;

    public bool IsWarp => _device?.IsWarp ?? false;

    public bool DebugLayerEnabled => _device?.DebugLayerEnabled ?? false;

    public bool GpuBasedValidationEnabled => _device?.GpuBasedValidationEnabled ?? false;

    public IReadOnlyList<D3D12DebugMessage> DebugMessages => _device?.CollectedMessages ?? [];

    public int ErrorOrWorseMessageCount => _device?.ErrorOrWorseMessageCount ?? 0;

    public bool ShaderCompilationSucceeded { get; private set; }

    public IReadOnlyList<HlslDiagnostic> ShaderDiagnostics { get; private set; } = [];

    public uint FramesRendered { get; private set; }

    public PixelReadbackResult? LastCapturedFrame { get; private set; }

    public static StageRenderer Create(
        nint hwnd, int width, int height, GraphicsDeviceOptions deviceOptions, string shaderPath,
        IReadOnlyList<StageMeshCreateInfo> meshes, IReadOnlyList<StageMaterialCreateInfo> materials, IReadOnlyList<StageObjectCreateInfo> objects)
    {
        ArgumentNullException.ThrowIfNull(shaderPath);
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(objects);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Initial width/height must both be positive.");
        }

        if (meshes.Count == 0)
        {
            throw new ArgumentException("At least one mesh is required.", nameof(meshes));
        }
        if (materials.Count == 0)
        {
            throw new ArgumentException("At least one material is required.", nameof(materials));
        }
        if (objects.Count == 0)
        {
            throw new ArgumentException("At least one object is required.", nameof(objects));
        }

        foreach (StageMeshCreateInfo mesh in meshes)
        {
            if (mesh.Positions.Count == 0 || mesh.Positions.Count != mesh.Normals.Count
                || mesh.Positions.Count != mesh.Tangents.Count || mesh.Positions.Count != mesh.Uvs.Count
                || mesh.Indices.Count == 0)
            {
                throw new ArgumentException("Each mesh must have at least one vertex with matching Positions/Normals/Tangents/Uvs counts, and at least one index.", nameof(meshes));
            }
        }

        foreach (StageObjectCreateInfo obj in objects)
        {
            if (obj.MeshIndex < 0 || obj.MeshIndex >= meshes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(objects), $"Object references out-of-range MeshIndex {obj.MeshIndex}.");
            }
            if (obj.MaterialIndex < 0 || obj.MaterialIndex >= materials.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(objects), $"Object references out-of-range MaterialIndex {obj.MaterialIndex}.");
            }
        }

        StageRenderer renderer = new()
        {
            _width = width,
            _height = height,
        };

        bool completed = false;
        try
        {
            renderer.Initialize((HWND)hwnd, width, height, deviceOptions, shaderPath, meshes, materials, objects);
            completed = true;
            return renderer;
        }
        finally
        {
            if (!completed)
            {
                renderer.Dispose();
            }
        }
    }

    /// <summary>Legacy adapter (pre-ADR-0018): one untextured solid-color mesh per object, exactly reproducing the old 1:1 mesh/material/object indexing so existing callers (and <c>StageGpuIntegrationTests</c>) keep working unchanged. Synthesized UVs/tangents never matter visually here since every material falls back to its 1x1 default textures.</summary>
    public static StageRenderer Create(
        nint hwnd, int width, int height, GraphicsDeviceOptions deviceOptions, string shaderPath,
        IReadOnlyList<MeshCreateInfo> meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);

        var stageMeshes = new StageMeshCreateInfo[meshes.Count];
        var stageMaterials = new StageMaterialCreateInfo[meshes.Count];
        var stageObjects = new StageObjectCreateInfo[meshes.Count];

        for (int i = 0; i < meshes.Count; i++)
        {
            MeshCreateInfo mesh = meshes[i];
            int vertexCount = mesh.Positions.Count;
            var uvs = new Vector2[vertexCount];
            var tangents = new Vector4[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                tangents[v] = ArbitraryTangentFor(mesh.Normals[v]);
            }

            stageMeshes[i] = new StageMeshCreateInfo { Positions = mesh.Positions, Normals = mesh.Normals, Tangents = tangents, Uvs = uvs, Indices = mesh.Indices };
            stageMaterials[i] = new StageMaterialCreateInfo { BaseColorFactor = mesh.BaseColorFactor, RoughnessFactor = 1f, MetallicFactor = 0f };
            stageObjects[i] = new StageObjectCreateInfo { MeshIndex = i, MaterialIndex = i };
        }

        return Create(hwnd, width, height, deviceOptions, shaderPath, stageMeshes, stageMaterials, stageObjects);
    }

    /// <summary>An arbitrary unit tangent perpendicular to <paramref name="normal"/> - correct for TBN orthonormality even though it isn't aligned to any real UV direction, which is fine when the bound normal texture is always the flat 1x1 fallback (see <see cref="MaterialTextureFormat"/> fallback images).</summary>
    private static Vector4 ArbitraryTangentFor(Vector3 normal)
    {
        Vector3 reference = MathF.Abs(normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(reference, normal));
        return new Vector4(tangent, 1f);
    }

    private StageRenderer()
    {
    }

    private void Initialize(HWND hwnd, int width, int height, GraphicsDeviceOptions deviceOptions, string shaderPath,
        IReadOnlyList<StageMeshCreateInfo> meshes, IReadOnlyList<StageMaterialCreateInfo> materials, IReadOnlyList<StageObjectCreateInfo> objects)
    {
        _device = GraphicsDevice.Create(deviceOptions);

        CreateSwapChain(hwnd, width, height);
        CreateRtvHeap();
        CreateRenderTargetViews();
        CreateDsvHeap();
        CreateDepthBuffer(width, height);

        for (int i = 0; i < FrameCount; i++)
        {
            ID3D12CommandAllocator* allocator;
            D3D12Diagnostics.EnsureSuccess(
                _device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, __uuidof<ID3D12CommandAllocator>(), (void**)&allocator),
                "ID3D12Device::CreateCommandAllocator", "creating a frame command allocator");
            _commandAllocators[i] = allocator;
        }

        ID3D12GraphicsCommandList* commandList;
        D3D12Diagnostics.EnsureSuccess(
            _device.Device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, _commandAllocators[0], null, __uuidof<ID3D12GraphicsCommandList>(), (void**)&commandList),
            "ID3D12Device::CreateCommandList", "creating the graphics command list");
        _commandList = commandList;
        D3D12Diagnostics.EnsureSuccess(_commandList->Close(), "ID3D12GraphicsCommandList::Close", "closing the initial command list state");

        ID3D12Fence* fence;
        D3D12Diagnostics.EnsureSuccess(
            _device.Device->CreateFence(0, D3D12_FENCE_FLAG_NONE, __uuidof<ID3D12Fence>(), (void**)&fence),
            "ID3D12Device::CreateFence", "creating the frame fence");
        _fence = fence;

        _fenceEvent = CreateEventW(lpEventAttributes: null, bManualReset: FALSE, bInitialState: FALSE, lpName: null);
        if (_fenceEvent == HANDLE.NULL)
        {
            throw new InvalidOperationException("CreateEventW failed while creating the frame fence event.");
        }

        _frameIndex = _swapChain->GetCurrentBackBufferIndex();
        _fenceValues[_frameIndex] = 1;

        CompileShaders(shaderPath);
        CreateRootSignature();
        CreatePipelineState();
        CreateMeshBuffers(meshes);
        CreateMaterialResources(materials);
        CreateObjectMappings(objects);
        CreateConstantBuffers();
        UpdateViewportAndScissor(width, height);
        InitializeConstantsToDefaults();
    }

    private void CreateSwapChain(HWND hwnd, int width, int height)
    {
        DXGI_SWAP_CHAIN_DESC1 desc = new()
        {
            BufferCount = FrameCount,
            Width = (uint)width,
            Height = (uint)height,
            Format = BackBufferFormat,
            BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT,
            SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD,
            SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
        };

        IDXGISwapChain1* swapChain1;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Factory->CreateSwapChainForHwnd(
                (IUnknown*)_device.CommandQueue,
                hwnd,
                &desc,
                pFullscreenDesc: null,
                pRestrictToOutput: null,
                &swapChain1),
            "IDXGIFactory4::CreateSwapChainForHwnd", "creating the swap chain");

        IDXGISwapChain3* swapChain3;
        HRESULT hr = swapChain1->QueryInterface(__uuidof<IDXGISwapChain3>(), (void**)&swapChain3);
        _ = swapChain1->Release();
        D3D12Diagnostics.EnsureSuccess(hr, "IDXGISwapChain1::QueryInterface(IDXGISwapChain3)", "upgrading the swap chain interface");

        _swapChain = swapChain3;
    }

    private void CreateRtvHeap()
    {
        D3D12_DESCRIPTOR_HEAP_DESC desc = new()
        {
            NumDescriptors = FrameCount,
            Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
        };

        ID3D12DescriptorHeap* heap;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateDescriptorHeap(&desc, __uuidof<ID3D12DescriptorHeap>(), (void**)&heap),
            "ID3D12Device::CreateDescriptorHeap", "creating the RTV descriptor heap");

        _rtvHeap = heap;
        _rtvDescriptorSize = _device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    }

    private void CreateRenderTargetViews()
    {
        D3D12_CPU_DESCRIPTOR_HANDLE rtvBase = _rtvHeap->GetCPUDescriptorHandleForHeapStart();

        for (uint i = 0; i < FrameCount; i++)
        {
            ID3D12Resource* backBuffer;
            D3D12Diagnostics.EnsureSuccess(
                _swapChain->GetBuffer(i, __uuidof<ID3D12Resource>(), (void**)&backBuffer),
                "IDXGISwapChain3::GetBuffer", "retrieving a back buffer", $"buffer[{i}]");

            D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle = new(rtvBase, (int)i, _rtvDescriptorSize);
            _device!.Device->CreateRenderTargetView(backBuffer, pDesc: null, rtvHandle);

            _backBuffers[i] = backBuffer;
        }
    }

    private void CreateDsvHeap()
    {
        D3D12_DESCRIPTOR_HEAP_DESC desc = new()
        {
            NumDescriptors = 1,
            Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV,
        };

        ID3D12DescriptorHeap* heap;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateDescriptorHeap(&desc, __uuidof<ID3D12DescriptorHeap>(), (void**)&heap),
            "ID3D12Device::CreateDescriptorHeap", "creating the DSV descriptor heap");

        _dsvHeap = heap;
    }

    private void CreateDepthBuffer(int width, int height)
    {
        D3D12_RESOURCE_DESC resourceDesc = D3D12_RESOURCE_DESC.Tex2D(DepthBufferFormat, (uint)width, (uint)height, mipLevels: 1);
        resourceDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE clearValue = new(DepthBufferFormat, 1.0f, 0);

        ID3D12Resource* depthBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &resourceDesc, D3D12_RESOURCE_STATE_DEPTH_WRITE, &clearValue, __uuidof<ID3D12Resource>(), (void**)&depthBuffer),
            "ID3D12Device::CreateCommittedResource", "creating the depth buffer");

        D3D12_DEPTH_STENCIL_VIEW_DESC dsvDesc = new()
        {
            Format = DepthBufferFormat,
            ViewDimension = D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D,
        };
        _device.Device->CreateDepthStencilView(depthBuffer, &dsvDesc, _dsvHeap->GetCPUDescriptorHandleForHeapStart());

        _depthBuffer = depthBuffer;
    }

    private void CompileShaders(string shaderPath)
    {
        bool debugShaders = false;
#if DEBUG
        debugShaders = true;
#endif

        HlslCompileResult vs = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = shaderPath,
            EntryPoint = "VSMain",
            TargetProfile = "vs_6_0",
            EnableDebugInfo = debugShaders,
        });

        HlslCompileResult ps = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = shaderPath,
            EntryPoint = "PSMain",
            TargetProfile = "ps_6_0",
            EnableDebugInfo = debugShaders,
        });

        ShaderCompilationSucceeded = vs.Success && ps.Success;
        ShaderDiagnostics = [.. vs.Diagnostics, .. ps.Diagnostics];

        if (!ShaderCompilationSucceeded)
        {
            string details = string.Join(Environment.NewLine, ShaderDiagnostics);
            throw new InvalidOperationException($"HLSL shader compilation failed for '{shaderPath}':{Environment.NewLine}{details}");
        }

        _vertexShaderBytecode = vs.Bytecode;
        _pixelShaderBytecode = ps.Bytecode;
    }

    private byte[] _vertexShaderBytecode = [];
    private byte[] _pixelShaderBytecode = [];

    private void CreateRootSignature()
    {
        D3D12_ROOT_PARAMETER frameParam = new()
        {
            ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL,
        };
        frameParam.Descriptor = new D3D12_ROOT_DESCRIPTOR { ShaderRegister = 0, RegisterSpace = 0 };

        D3D12_ROOT_PARAMETER objectParam = new()
        {
            ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL,
        };
        objectParam.Descriptor = new D3D12_ROOT_DESCRIPTOR { ShaderRegister = 1, RegisterSpace = 0 };

        D3D12_DESCRIPTOR_RANGE srvRange = new()
        {
            RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors = 3,
            BaseShaderRegister = 0,
        };

        D3D12_ROOT_PARAMETER textureParam = new()
        {
            ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_STATIC_SAMPLER_DESC staticSampler = new()
        {
            Filter = D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU = D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressV = D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressW = D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            ComparisonFunc = D3D12_COMPARISON_FUNC_NEVER,
            BorderColor = D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD = float.MaxValue,
            ShaderRegister = 0,
            ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_ROOT_PARAMETER* rootParams = stackalloc D3D12_ROOT_PARAMETER[3];
        rootParams[0] = frameParam;
        rootParams[1] = objectParam;
        rootParams[2] = textureParam;
        rootParams[2].DescriptorTable = new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges = 1, pDescriptorRanges = &srvRange };

        D3D12_ROOT_SIGNATURE_DESC rootSignatureDesc = new()
        {
            NumParameters = 3,
            pParameters = rootParams,
            NumStaticSamplers = 1,
            pStaticSamplers = &staticSampler,
            Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT,
        };

        ID3DBlob* signatureBlob = null;
        ID3DBlob* errorBlob = null;
        try
        {
            HRESULT hr = D3D12SerializeRootSignature(&rootSignatureDesc, D3D_ROOT_SIGNATURE_VERSION_1, &signatureBlob, &errorBlob);
            if (hr.FAILED)
            {
                string error = errorBlob != null
                    ? new string((sbyte*)errorBlob->GetBufferPointer())
                    : "(no error blob)";
                throw new D3D12Exception("D3D12SerializeRootSignature", hr.Value, "serializing the stage root signature", error);
            }

            ID3D12RootSignature* rootSignature;
            D3D12Diagnostics.EnsureSuccess(
                _device!.Device->CreateRootSignature(0, signatureBlob->GetBufferPointer(), signatureBlob->GetBufferSize(), __uuidof<ID3D12RootSignature>(), (void**)&rootSignature),
                "ID3D12Device::CreateRootSignature", "creating the root signature");
            _rootSignature = rootSignature;
        }
        finally
        {
            if (signatureBlob != null)
            {
                _ = signatureBlob->Release();
            }

            if (errorBlob != null)
            {
                _ = errorBlob->Release();
            }
        }
    }

    private void CreatePipelineState()
    {
        fixed (byte* pPosition = "POSITION\0"u8)
        fixed (byte* pNormal = "NORMAL\0"u8)
        fixed (byte* pTangent = "TANGENT\0"u8)
        fixed (byte* pTexCoord = "TEXCOORD\0"u8)
        fixed (byte* pVertexShader = _vertexShaderBytecode)
        fixed (byte* pPixelShader = _pixelShaderBytecode)
        {
            D3D12_INPUT_ELEMENT_DESC* inputElements = stackalloc D3D12_INPUT_ELEMENT_DESC[4]
            {
                new D3D12_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)pPosition,
                    Format = DXGI_FORMAT_R32G32B32_FLOAT,
                    InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)pNormal,
                    Format = DXGI_FORMAT_R32G32B32_FLOAT,
                    AlignedByteOffset = 12,
                    InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)pTangent,
                    Format = DXGI_FORMAT_R32G32B32A32_FLOAT,
                    AlignedByteOffset = 24,
                    InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)pTexCoord,
                    Format = DXGI_FORMAT_R32G32_FLOAT,
                    AlignedByteOffset = 40,
                    InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
            };

            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = new()
            {
                InputLayout = new D3D12_INPUT_LAYOUT_DESC
                {
                    pInputElementDescs = inputElements,
                    NumElements = 4,
                },
                pRootSignature = _rootSignature,
                VS = new D3D12_SHADER_BYTECODE { pShaderBytecode = pVertexShader, BytecodeLength = (nuint)_vertexShaderBytecode.Length },
                PS = new D3D12_SHADER_BYTECODE { pShaderBytecode = pPixelShader, BytecodeLength = (nuint)_pixelShaderBytecode.Length },
                RasterizerState = D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState = D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState = D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets = 1,
                DSVFormat = DepthBufferFormat,
                SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
            };
            // Real backface culling is deferred, same rationale as ModelRenderer (ADR-0004).
            psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
            psoDesc.RTVFormats[0] = BackBufferFormat;

            ID3D12PipelineState* pipelineState;
            D3D12Diagnostics.EnsureSuccess(
                _device!.Device->CreateGraphicsPipelineState(&psoDesc, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState),
                "ID3D12Device::CreateGraphicsPipelineState", "creating the stage pipeline state");
            _pipelineState = pipelineState;
        }
    }

    private void CreateMeshBuffers(IReadOnlyList<StageMeshCreateInfo> meshes)
    {
        _meshCount = meshes.Count;
        _vertexBuffers = new ID3D12Resource*[_meshCount];
        _vertexBufferViews = new D3D12_VERTEX_BUFFER_VIEW[_meshCount];
        _indexBuffers = new ID3D12Resource*[_meshCount];
        _indexBufferViews = new D3D12_INDEX_BUFFER_VIEW[_meshCount];
        _indexCounts = new uint[_meshCount];

        for (int i = 0; i < _meshCount; i++)
        {
            StageMeshCreateInfo mesh = meshes[i];
            CreateVertexBuffer(i, mesh.Positions, mesh.Normals, mesh.Tangents, mesh.Uvs);
            CreateIndexBuffer(i, mesh.Indices);
        }
    }

    private void CreateVertexBuffer(int meshIndex, IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals, IReadOnlyList<Vector4> tangents, IReadOnlyList<Vector2> uvs)
    {
        int vertexCount = positions.Count;
        Vertex[] vertices = new Vertex[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vertex { Position = positions[i], Normal = normals[i], Tangent = tangents[i], Uv = uvs[i] };
        }

        uint bufferSize = (uint)(sizeof(Vertex) * vertexCount);

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(bufferSize);

        ID3D12Resource* vertexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&vertexBuffer),
            "ID3D12Device::CreateCommittedResource", "creating a stage vertex buffer (upload heap)");

        D3D12_RANGE noRead = new();
        byte* mapped;
        D3D12Diagnostics.EnsureSuccess(vertexBuffer->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping a stage vertex buffer");
        fixed (Vertex* pVertices = vertices)
        {
            Unsafe.CopyBlock(mapped, pVertices, bufferSize);
        }
        vertexBuffer->Unmap(0, null);

        _vertexBuffers[meshIndex] = vertexBuffer;
        _vertexBufferViews[meshIndex] = new D3D12_VERTEX_BUFFER_VIEW
        {
            BufferLocation = vertexBuffer->GetGPUVirtualAddress(),
            StrideInBytes = (uint)sizeof(Vertex),
            SizeInBytes = bufferSize,
        };
    }

    private void CreateIndexBuffer(int meshIndex, IReadOnlyList<uint> indices)
    {
        int indexCount = indices.Count;
        uint[] indexArray = indices as uint[] ?? [.. indices];
        uint bufferSize = (uint)(sizeof(uint) * indexCount);

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(bufferSize);

        ID3D12Resource* indexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&indexBuffer),
            "ID3D12Device::CreateCommittedResource", "creating a stage index buffer (upload heap)");

        D3D12_RANGE noRead = new();
        byte* mapped;
        D3D12Diagnostics.EnsureSuccess(indexBuffer->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping a stage index buffer");
        fixed (uint* pIndices = indexArray)
        {
            Unsafe.CopyBlock(mapped, pIndices, bufferSize);
        }
        indexBuffer->Unmap(0, null);

        _indexBuffers[meshIndex] = indexBuffer;
        _indexCounts[meshIndex] = (uint)indexCount;
        _indexBufferViews[meshIndex] = new D3D12_INDEX_BUFFER_VIEW
        {
            BufferLocation = indexBuffer->GetGPUVirtualAddress(),
            SizeInBytes = bufferSize,
            Format = DXGI_FORMAT_R32_UINT,
        };
    }

    private void CreateMaterialResources(IReadOnlyList<StageMaterialCreateInfo> materials)
    {
        _materialCount = materials.Count;
        _materialTextures = new ID3D12Resource*[_materialCount * 3];
        _materialBaseColorFactors = new Vector4[_materialCount];
        _materialRoughnessFactors = new float[_materialCount];
        _materialMetallicFactors = new float[_materialCount];

        for (int i = 0; i < _materialCount; i++)
        {
            StageMaterialCreateInfo material = materials[i];
            _materialBaseColorFactors[i] = material.BaseColorFactor;
            _materialRoughnessFactors[i] = material.RoughnessFactor;
            _materialMetallicFactors[i] = material.MetallicFactor;

            _materialTextures[(i * 3) + 0] = CreateTextureResource(material.BaseColorTextureBytes, WhiteFallback());
            _materialTextures[(i * 3) + 1] = CreateTextureResource(material.NormalTextureBytes, FlatNormalFallback());
            _materialTextures[(i * 3) + 2] = CreateTextureResource(material.MetallicRoughnessTextureBytes, WhiteFallback());
        }

        D3D12_DESCRIPTOR_HEAP_DESC srvHeapDesc = new()
        {
            Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors = (uint)(_materialCount * 3),
            Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap* srvHeap;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateDescriptorHeap(&srvHeapDesc, __uuidof<ID3D12DescriptorHeap>(), (void**)&srvHeap),
            "ID3D12Device::CreateDescriptorHeap", "creating the material SRV descriptor heap");
        _srvHeap = srvHeap;
        _srvDescriptorSize = _device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

        D3D12_CPU_DESCRIPTOR_HANDLE srvHeapStart = _srvHeap->GetCPUDescriptorHandleForHeapStart();
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = new()
        {
            Format = MaterialTextureFormat,
            ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping = (uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        srvDesc.Texture2D.MipLevels = 1;

        for (int i = 0; i < _materialTextures.Length; i++)
        {
            D3D12_CPU_DESCRIPTOR_HANDLE handle = new(srvHeapStart, i, _srvDescriptorSize);
            _device.Device->CreateShaderResourceView(_materialTextures[i], &srvDesc, handle);
        }
    }

    private static DecodedImage WhiteFallback() => new(1, 1, [255, 255, 255, 255]);

    /// <summary>Flat tangent-space normal (0,0,1) encoded as RGB(128,128,255), so an unmapped surface's TBN-transformed normal is exactly its own vertex normal regardless of the (possibly arbitrary) tangent.</summary>
    private static DecodedImage FlatNormalFallback() => new(1, 1, [128, 128, 255, 255]);

    /// <summary>Decodes (or falls back to a procedural default, mirroring <see cref="ModelRenderer"/>'s "every mesh gets a real SRV" rationale) and uploads one material texture slot.</summary>
    private ID3D12Resource* CreateTextureResource(byte[]? encodedBytes, DecodedImage fallback)
    {
        DecodedImage image = DecodeOrFallback(encodedBytes, fallback);

        D3D12_RESOURCE_DESC textureDesc = D3D12_RESOURCE_DESC.Tex2D(MaterialTextureFormat, (uint)image.Width, (uint)image.Height, mipLevels: 1);

        D3D12_HEAP_PROPERTIES defaultHeapProps = new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource* texture;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&defaultHeapProps, D3D12_HEAP_FLAG_NONE, &textureDesc, D3D12_RESOURCE_STATE_COPY_DEST, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&texture),
            "ID3D12Device::CreateCommittedResource", "creating a material texture");

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
        ulong totalBytes;
        _device.Device->GetCopyableFootprints(&textureDesc, 0, 1, 0, &footprint, null, null, &totalBytes);

        D3D12_HEAP_PROPERTIES uploadHeapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC stagingDesc = D3D12_RESOURCE_DESC.Buffer(totalBytes);

        ID3D12Resource* staging;
        D3D12Diagnostics.EnsureSuccess(
            _device.Device->CreateCommittedResource(&uploadHeapProps, D3D12_HEAP_FLAG_NONE, &stagingDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&staging),
            "ID3D12Device::CreateCommittedResource", "creating a material texture upload staging buffer");

        try
        {
            // GetCopyableFootprints' RowPitch is 256-byte aligned; the decoded image is tightly
            // packed (stride = Width*4). These differ whenever Width*4 isn't already a multiple of
            // 256, so this must be a per-row copy, not one bulk CopyBlock (same as ModelRenderer).
            D3D12_RANGE noRead = new();
            byte* mapped;
            D3D12Diagnostics.EnsureSuccess(staging->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping a material texture upload staging buffer");

            uint srcRowPitch = (uint)image.Width * 4;
            uint dstRowPitch = footprint.Footprint.RowPitch;
            fixed (byte* pSrc = image.Pixels)
            {
                for (uint y = 0; y < image.Height; y++)
                {
                    Unsafe.CopyBlock(mapped + (y * dstRowPitch), pSrc + (y * srcRowPitch), srcRowPitch);
                }
            }
            staging->Unmap(0, null);

            D3D12Diagnostics.EnsureSuccess(_commandAllocators[0]->Reset(), "ID3D12CommandAllocator::Reset", "resetting the allocator for a material texture upload");
            D3D12Diagnostics.EnsureSuccess(_commandList->Reset(_commandAllocators[0], null), "ID3D12GraphicsCommandList::Reset", "resetting the command list for a material texture upload");

            D3D12_TEXTURE_COPY_LOCATION dst = new() { pResource = texture, Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            dst.SubresourceIndex = 0;
            D3D12_TEXTURE_COPY_LOCATION src = new() { pResource = staging, Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            src.PlacedFootprint = footprint;
            _commandList->CopyTextureRegion(&dst, 0, 0, 0, &src, null);

            D3D12_RESOURCE_BARRIER toShaderResource = D3D12_RESOURCE_BARRIER.InitTransition(texture, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            _commandList->ResourceBarrier(1, &toShaderResource);

            D3D12Diagnostics.EnsureSuccess(_commandList->Close(), "ID3D12GraphicsCommandList::Close", "closing a material texture upload command list");

            ID3D12CommandList* commandListBase = (ID3D12CommandList*)_commandList;
            _device.CommandQueue->ExecuteCommandLists(1, &commandListBase);

            WaitForGpuIdle();
        }
        finally
        {
            _ = staging->Release();
        }

        return texture;
    }

    private static DecodedImage DecodeOrFallback(byte[]? encodedBytes, DecodedImage fallback)
    {
        if (encodedBytes is null || encodedBytes.Length == 0)
        {
            return fallback;
        }

        WicDecodeResult decodeResult = WicImageDecoder.Decode(encodedBytes);
        return decodeResult.Success ? decodeResult.Image! : fallback;
    }

    private void CreateObjectMappings(IReadOnlyList<StageObjectCreateInfo> objects)
    {
        _objectCount = objects.Count;
        _objectMeshIndices = new int[_objectCount];
        _objectMaterialIndices = new int[_objectCount];
        _objectTransforms = new Matrix4x4[_objectCount];

        for (int i = 0; i < _objectCount; i++)
        {
            _objectMeshIndices[i] = objects[i].MeshIndex;
            _objectMaterialIndices[i] = objects[i].MaterialIndex;
            _objectTransforms[i] = Matrix4x4.Identity;
        }
    }

    private void CreateConstantBuffers()
    {
        uint frameBufferSize = ConstantAlignment * FrameCount;
        CreateUploadBuffer(frameBufferSize, out _frameConstantsBuffer, out _frameConstantsMapped, "creating the frame constant buffer");

        uint objectBufferSize = ConstantAlignment * (uint)_objectCount * FrameCount;
        CreateUploadBuffer(objectBufferSize, out _objectConstantsBuffer, out _objectConstantsMapped, "creating the object constant buffer");
    }

    private void CreateUploadBuffer(uint size, out ID3D12Resource* buffer, out byte* mapped, string context)
    {
        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(size);

        ID3D12Resource* resource;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&resource),
            "ID3D12Device::CreateCommittedResource", context);

        D3D12_RANGE noRead = new();
        byte* ptr;
        D3D12Diagnostics.EnsureSuccess(resource->Map(0, &noRead, (void**)&ptr), "ID3D12Resource::Map", context);

        buffer = resource;
        mapped = ptr;
    }

    /// <summary>
    /// Writes an identity world matrix (with each object's material factors), a default camera,
    /// and default light/ambient/fog values into every frame-in-flight slot, so the very first
    /// Present reads fully-initialized data even if the caller's first SetCamera/SetObjectTransform/
    /// SetDirectionalLight/etc. call happens after this constructor returns.
    /// </summary>
    private void InitializeConstantsToDefaults()
    {
        Camera defaultCamera = Camera.CreateDefault(Vector3.Zero);

        for (uint frame = 0; frame < FrameCount; frame++)
        {
            WriteViewProjection(frame, defaultCamera.GetView() * defaultCamera.GetProjection((float)_width / _height));
            WriteCameraPosition(frame, defaultCamera.GetEyePosition());
            WriteDirectionalLight(frame, DefaultLightDirectionTowardLight, DefaultLightColor);
            WriteAmbientLight(frame, DefaultAmbientColor);
            WriteFog(frame, FogParams.None);
            WriteLightCounts(frame); // D3D12 does not guarantee zero-initialized upload-heap memory; without this the very first frame could loop over garbage PointLights/SpotLights entries.

            for (int objectIndex = 0; objectIndex < _objectCount; objectIndex++)
            {
                WriteObjectConstants(frame, objectIndex);
            }
        }
    }

    /// <summary>
    /// Sets the camera used for the frame currently being built. Must be called every frame,
    /// before <see cref="RenderFrame"/> - the constant buffer is double-buffered per frame-in-flight
    /// slot (<see cref="FrameCount"/>) and only the current slot is written here.
    /// </summary>
    public void SetCamera(in Camera camera, float aspectRatioOverride = 0f)
    {
        float aspectRatio = aspectRatioOverride > 0f ? aspectRatioOverride : (float)_width / _height;
        WriteViewProjection(_frameIndex, camera.GetView() * camera.GetProjection(aspectRatio));
        WriteCameraPosition(_frameIndex, camera.GetEyePosition());
    }

    /// <summary>Sets the single data-driven directional light (sun) for the frame currently being built. <paramref name="directionTowardLight"/> points from the scene toward the light (i.e. the direction surfaces should face to be lit), not the direction light travels.</summary>
    public void SetDirectionalLight(Vector3 directionTowardLight, Vector3 color, float intensity)
    {
        WriteDirectionalLight(_frameIndex, directionTowardLight, color * intensity);
    }

    /// <summary>Sets the flat ambient term (a stand-in for sky/indirect lighting) for the frame currently being built.</summary>
    public void SetAmbientLight(Vector3 color)
    {
        WriteAmbientLight(_frameIndex, color);
    }

    /// <summary>Sets the simplified exponential height fog for the frame currently being built. Pass <see cref="FogParams.None"/> to disable it (zero density).</summary>
    public void SetFog(FogParams fog)
    {
        WriteFog(_frameIndex, fog);
    }

    /// <summary>
    /// Adds one point light (radiates equally in all directions, attenuated by distance with a
    /// smooth falloff that reaches zero at <paramref name="range"/>) for the frame currently being
    /// built. Unlike <see cref="SetDirectionalLight"/>, this is additive per frame: call it once per
    /// active light, up to <see cref="MaxPointLights"/> - the count resets to zero at the end of
    /// <see cref="RenderFrame"/>, so a light that stops calling this simply stops contributing on
    /// the next frame (no stale state to clear).
    /// </summary>
    public void AddPointLight(Vector3 position, Vector3 color, float intensity, float range)
    {
        if (_pointLightCount >= MaxPointLights)
        {
            Debug.Assert(false, $"CGE-STAGE-LIGHT-001: exceeded MaxPointLights ({MaxPointLights}); extra point lights are silently dropped this frame.");
            return;
        }

        WritePointLight(_frameIndex, _pointLightCount, position, range, color * intensity);
        _pointLightCount++;
        WriteLightCounts(_frameIndex);
    }

    /// <summary>
    /// Adds one spot light (a point light additionally narrowed to a cone) for the frame currently
    /// being built. <paramref name="direction"/> is the direction the cone points toward (need not
    /// be normalized) and the brightness falls off smoothly between
    /// <paramref name="innerConeAngleRadians"/> (full brightness) and
    /// <paramref name="outerConeAngleRadians"/> (zero). Additive per frame - see
    /// <see cref="AddPointLight"/> for the accumulate/reset contract.
    /// </summary>
    public void AddSpotLight(Vector3 position, Vector3 direction, Vector3 color, float intensity, float range, float innerConeAngleRadians, float outerConeAngleRadians)
    {
        if (_spotLightCount >= MaxSpotLights)
        {
            Debug.Assert(false, $"CGE-STAGE-LIGHT-002: exceeded MaxSpotLights ({MaxSpotLights}); extra spot lights are silently dropped this frame.");
            return;
        }

        WriteSpotLight(_frameIndex, _spotLightCount, position, range, Vector3.Normalize(direction), MathF.Cos(innerConeAngleRadians), color * intensity, MathF.Cos(outerConeAngleRadians));
        _spotLightCount++;
        WriteLightCounts(_frameIndex);
    }

    /// <summary>Sets the world transform for one object for the frame currently being built (see <see cref="SetCamera"/> for the per-frame-slot contract).</summary>
    public void SetObjectTransform(int objectIndex, Matrix4x4 world)
    {
        if (objectIndex < 0 || objectIndex >= _objectCount)
        {
            throw new ArgumentOutOfRangeException(nameof(objectIndex));
        }

        _objectTransforms[objectIndex] = world;
        WriteObjectConstants(_frameIndex, objectIndex);
    }

    private byte* FrameSlot(uint frameIndex) => _frameConstantsMapped + (frameIndex * ConstantAlignment);

    private void WriteViewProjection(uint frameIndex, Matrix4x4 viewProjection)
    {
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameViewProjectionOffset, &viewProjection, (uint)sizeof(Matrix4x4));
    }

    private void WriteCameraPosition(uint frameIndex, Vector3 cameraPosition)
    {
        Vector4 packed = new(cameraPosition, 0f);
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameCameraPositionOffset, &packed, (uint)sizeof(Vector4));
    }

    private void WriteDirectionalLight(uint frameIndex, Vector3 directionTowardLight, Vector3 colorTimesIntensity)
    {
        Vector4 direction = new(Vector3.Normalize(directionTowardLight), 0f);
        Vector4 color = new(colorTimesIntensity, 0f);
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameLightDirectionOffset, &direction, (uint)sizeof(Vector4));
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameLightColorOffset, &color, (uint)sizeof(Vector4));
    }

    private void WriteAmbientLight(uint frameIndex, Vector3 color)
    {
        Vector4 packed = new(color, 0f);
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameAmbientColorOffset, &packed, (uint)sizeof(Vector4));
    }

    private void WriteFog(uint frameIndex, FogParams fog)
    {
        Vector4 fogParams0 = new(fog.Density, fog.HeightFalloff, 0f, 0f);
        Vector4 fogColor = new(fog.Color, 0f);
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameFogParamsOffset, &fogParams0, (uint)sizeof(Vector4));
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameFogColorOffset, &fogColor, (uint)sizeof(Vector4));
    }

    /// <summary>Writes Stage.hlsl's PointLightData[index] (2 float4: position+range, color*intensity).</summary>
    private void WritePointLight(uint frameIndex, int index, Vector3 position, float range, Vector3 colorTimesIntensity)
    {
        byte* destination = FrameSlot(frameIndex) + FramePointLightsOffset + ((uint)index * PointLightStride);
        Vector4 positionAndRange = new(position, range);
        Vector4 colorPacked = new(colorTimesIntensity, 0f);
        Unsafe.CopyBlock(destination, &positionAndRange, (uint)sizeof(Vector4));
        Unsafe.CopyBlock(destination + 16, &colorPacked, (uint)sizeof(Vector4));
    }

    /// <summary>Writes Stage.hlsl's SpotLightData[index] (3 float4: position+range, direction+innerCos, color*intensity+outerCos).</summary>
    private void WriteSpotLight(uint frameIndex, int index, Vector3 position, float range, Vector3 direction, float innerConeCos, Vector3 colorTimesIntensity, float outerConeCos)
    {
        byte* destination = FrameSlot(frameIndex) + FrameSpotLightsOffset + ((uint)index * SpotLightStride);
        Vector4 positionAndRange = new(position, range);
        Vector4 directionAndInnerCos = new(direction, innerConeCos);
        Vector4 colorAndOuterCos = new(colorTimesIntensity, outerConeCos);
        Unsafe.CopyBlock(destination, &positionAndRange, (uint)sizeof(Vector4));
        Unsafe.CopyBlock(destination + 16, &directionAndInnerCos, (uint)sizeof(Vector4));
        Unsafe.CopyBlock(destination + 32, &colorAndOuterCos, (uint)sizeof(Vector4));
    }

    /// <summary>Writes Stage.hlsl's FrameCB.LightCounts (int4: x=active point lights, y=active spot lights).</summary>
    private void WriteLightCounts(uint frameIndex)
    {
        int* counts = stackalloc int[4] { _pointLightCount, _spotLightCount, 0, 0 };
        Unsafe.CopyBlock(FrameSlot(frameIndex) + FrameLightCountsOffset, counts, 16);
    }

    private void WriteObjectConstants(uint frameIndex, int objectIndex)
    {
        ulong slot = ((ulong)frameIndex * (ulong)_objectCount) + (ulong)objectIndex;
        byte* destination = _objectConstantsMapped + (slot * ConstantAlignment);

        Matrix4x4 world = _objectTransforms[objectIndex];
        Unsafe.CopyBlock(destination + ObjectWorldOffset, &world, (uint)sizeof(Matrix4x4));

        int materialIndex = _objectMaterialIndices[objectIndex];
        Vector4 baseColorFactor = _materialBaseColorFactors[materialIndex];
        Unsafe.CopyBlock(destination + ObjectBaseColorFactorOffset, &baseColorFactor, (uint)sizeof(Vector4));

        Vector4 materialParams = new(_materialRoughnessFactors[materialIndex], _materialMetallicFactors[materialIndex], 0f, 0f);
        Unsafe.CopyBlock(destination + ObjectMaterialParamsOffset, &materialParams, (uint)sizeof(Vector4));
    }

    private ulong GetFrameConstantsAddress(uint frameIndex)
    {
        return _frameConstantsBuffer->GetGPUVirtualAddress() + (frameIndex * ConstantAlignment);
    }

    private ulong GetObjectConstantsAddress(uint frameIndex, int objectIndex)
    {
        ulong slot = ((ulong)frameIndex * (ulong)_objectCount) + (ulong)objectIndex;
        return _objectConstantsBuffer->GetGPUVirtualAddress() + (slot * ConstantAlignment);
    }

    private D3D12_GPU_DESCRIPTOR_HANDLE GetMaterialDescriptorTable(int materialIndex)
    {
        return new D3D12_GPU_DESCRIPTOR_HANDLE(_srvHeap->GetGPUDescriptorHandleForHeapStart(), materialIndex * 3, _srvDescriptorSize);
    }

    private void UpdateViewportAndScissor(int width, int height)
    {
        _viewport = new D3D12_VIEWPORT
        {
            TopLeftX = 0,
            TopLeftY = 0,
            Width = width,
            Height = height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };

        _scissorRect = new RECT { left = 0, top = 0, right = width, bottom = height };
        _width = width;
        _height = height;
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            _sizeIsZero = true;
            return;
        }

        _sizeIsZero = false;

        if (width == _width && height == _height)
        {
            return;
        }

        WaitForGpuIdle();

        // See ModelRenderer.Resize / ADR-0003 §8: ResizeBuffers restarts the swap chain's
        // back-buffer index cycle, which would otherwise desynchronize the per-slot fence values.
        ulong syncedFenceValue = _fenceValues[_frameIndex];
        for (int i = 0; i < FrameCount; i++)
        {
            _fenceValues[i] = syncedFenceValue;
        }

        for (int i = 0; i < FrameCount; i++)
        {
            if (_backBuffers[i] != null)
            {
                _ = _backBuffers[i]->Release();
                _backBuffers[i] = null;
            }
        }

        ReleaseReadbackBuffer();

        if (_depthBuffer != null)
        {
            _ = _depthBuffer->Release();
            _depthBuffer = null;
        }

        D3D12Diagnostics.EnsureSuccess(
            _swapChain->ResizeBuffers(FrameCount, (uint)width, (uint)height, BackBufferFormat, 0),
            "IDXGISwapChain3::ResizeBuffers", "resizing the swap chain");

        CreateRenderTargetViews();
        CreateDepthBuffer(width, height);
        _frameIndex = _swapChain->GetCurrentBackBufferIndex();
        UpdateViewportAndScissor(width, height);
    }

    public void RenderFrame(bool captureForVerification = false)
    {
        if (_sizeIsZero)
        {
            return;
        }

        ID3D12CommandAllocator* allocator = _commandAllocators[_frameIndex];
        D3D12Diagnostics.EnsureSuccess(allocator->Reset(), "ID3D12CommandAllocator::Reset", "resetting the frame command allocator");
        D3D12Diagnostics.EnsureSuccess(_commandList->Reset(allocator, _pipelineState), "ID3D12GraphicsCommandList::Reset", "resetting the graphics command list");

        ID3D12Resource* backBuffer = _backBuffers[_frameIndex];

        D3D12_RESOURCE_BARRIER toRenderTarget = D3D12_RESOURCE_BARRIER.InitTransition(backBuffer, D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
        _commandList->ResourceBarrier(1, &toRenderTarget);

        D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle = new(_rtvHeap->GetCPUDescriptorHandleForHeapStart(), (int)_frameIndex, _rtvDescriptorSize);
        D3D12_CPU_DESCRIPTOR_HANDLE dsvHandle = _dsvHeap->GetCPUDescriptorHandleForHeapStart();
        _commandList->OMSetRenderTargets(1, &rtvHandle, FALSE, &dsvHandle);

        Vector4 clearColor = ClearColorVector;
        _commandList->ClearRenderTargetView(rtvHandle, (float*)&clearColor, 0, null);
        _commandList->ClearDepthStencilView(dsvHandle, D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH, 1.0f, 0, 0, null);

        _commandList->SetGraphicsRootSignature(_rootSignature);
        _commandList->SetPipelineState(_pipelineState);

        ID3D12DescriptorHeap* srvHeap = _srvHeap;
        _commandList->SetDescriptorHeaps(1, &srvHeap);
        _commandList->SetGraphicsRootConstantBufferView(0, GetFrameConstantsAddress(_frameIndex));

        fixed (D3D12_VIEWPORT* pViewport = &_viewport)
        fixed (RECT* pScissorRect = &_scissorRect)
        {
            _commandList->RSSetViewports(1, pViewport);
            _commandList->RSSetScissorRects(1, pScissorRect);
        }

        _commandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        for (int i = 0; i < _objectCount; i++)
        {
            int meshIndex = _objectMeshIndices[i];
            _commandList->SetGraphicsRootConstantBufferView(1, GetObjectConstantsAddress(_frameIndex, i));
            _commandList->SetGraphicsRootDescriptorTable(2, GetMaterialDescriptorTable(_objectMaterialIndices[i]));

            fixed (D3D12_VERTEX_BUFFER_VIEW* pVertexBufferView = &_vertexBufferViews[meshIndex])
            fixed (D3D12_INDEX_BUFFER_VIEW* pIndexBufferView = &_indexBufferViews[meshIndex])
            {
                _commandList->IASetVertexBuffers(0, 1, pVertexBufferView);
                _commandList->IASetIndexBuffer(pIndexBufferView);
                _commandList->DrawIndexedInstanced(_indexCounts[meshIndex], 1, 0, 0, 0);
            }
        }

        if (captureForVerification)
        {
            EnsureReadbackBuffer();

            D3D12_RESOURCE_BARRIER toCopySource = D3D12_RESOURCE_BARRIER.InitTransition(backBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_COPY_SOURCE);
            _commandList->ResourceBarrier(1, &toCopySource);

            D3D12_TEXTURE_COPY_LOCATION dst = new() { pResource = _readbackBuffer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            dst.PlacedFootprint = _readbackFootprint;
            D3D12_TEXTURE_COPY_LOCATION src = new() { pResource = backBuffer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            src.SubresourceIndex = 0;
            _commandList->CopyTextureRegion(&dst, 0, 0, 0, &src, null);

            D3D12_RESOURCE_BARRIER toPresent = D3D12_RESOURCE_BARRIER.InitTransition(backBuffer, D3D12_RESOURCE_STATE_COPY_SOURCE, D3D12_RESOURCE_STATE_PRESENT);
            _commandList->ResourceBarrier(1, &toPresent);
        }
        else
        {
            D3D12_RESOURCE_BARRIER toPresent = D3D12_RESOURCE_BARRIER.InitTransition(backBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
            _commandList->ResourceBarrier(1, &toPresent);
        }

        D3D12Diagnostics.EnsureSuccess(_commandList->Close(), "ID3D12GraphicsCommandList::Close", "closing the frame command list");

        ID3D12CommandList* commandListBase = (ID3D12CommandList*)_commandList;
        _device!.CommandQueue->ExecuteCommandLists(1, &commandListBase);

        D3D12Diagnostics.EnsureSuccess(_swapChain->Present(1, 0), "IDXGISwapChain3::Present", "presenting the frame");

        MoveToNextFrame();
        _device.PollDebugMessages();
        FramesRendered++;

        // The point/spot light counts (and the data they gate) have already been submitted to the
        // GPU above; reset so the next frame starts from zero and only reflects lights that call
        // AddPointLight/AddSpotLight again this frame (see the fields' doc comment).
        _pointLightCount = 0;
        _spotLightCount = 0;

        if (captureForVerification)
        {
            WaitForGpuIdle();
            LastCapturedFrame = ReadBackCapturedFrame();
        }
    }

    private void EnsureReadbackBuffer()
    {
        if (_readbackBuffer != null)
        {
            return;
        }

        D3D12_RESOURCE_DESC textureDesc = D3D12_RESOURCE_DESC.Tex2D(BackBufferFormat, (uint)_width, (uint)_height, mipLevels: 1);

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
        ulong totalBytes;
        _device!.Device->GetCopyableFootprints(&textureDesc, 0, 1, 0, &footprint, null, null, &totalBytes);

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_READBACK);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(totalBytes);

        ID3D12Resource* readbackBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_COPY_DEST, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&readbackBuffer),
            "ID3D12Device::CreateCommittedResource", "creating the pixel-verification readback buffer");

        _readbackBuffer = readbackBuffer;
        _readbackBufferSize = totalBytes;
        _readbackFootprint = footprint;
    }

    private void ReleaseReadbackBuffer()
    {
        if (_readbackBuffer != null)
        {
            _ = _readbackBuffer->Release();
            _readbackBuffer = null;
            _readbackBufferSize = 0;
        }
    }

    private PixelReadbackResult ReadBackCapturedFrame()
    {
        D3D12_RANGE readRange = new() { Begin = 0, End = (nuint)_readbackBufferSize };
        void* mapped;
        D3D12Diagnostics.EnsureSuccess(_readbackBuffer->Map(0, &readRange, &mapped), "ID3D12Resource::Map", "mapping the readback buffer");

        byte[] data = new byte[_readbackBufferSize];
        fixed (byte* pData = data)
        {
            Unsafe.CopyBlock(pData, mapped, (uint)_readbackBufferSize);
        }

        D3D12_RANGE writtenRange = new();
        _readbackBuffer->Unmap(0, &writtenRange);

        return new PixelReadbackResult
        {
            Width = _width,
            Height = _height,
            RowPitch = _readbackFootprint.Footprint.RowPitch,
            Format = BackBufferFormat,
            Pixels = data,
        };
    }

    private void MoveToNextFrame()
    {
        ulong currentFenceValue = _fenceValues[_frameIndex];
        D3D12Diagnostics.EnsureSuccess(_device!.CommandQueue->Signal(_fence, currentFenceValue), "ID3D12CommandQueue::Signal", "signaling the frame fence");

        _frameIndex = _swapChain->GetCurrentBackBufferIndex();

        if (_fence->GetCompletedValue() < _fenceValues[_frameIndex])
        {
            D3D12Diagnostics.EnsureSuccess(_fence->SetEventOnCompletion(_fenceValues[_frameIndex], _fenceEvent), "ID3D12Fence::SetEventOnCompletion", "waiting for the next frame slot");
            _ = WaitForSingleObjectEx(_fenceEvent, INFINITE, FALSE);
        }

        _fenceValues[_frameIndex] = currentFenceValue + 1;
    }

    private void WaitForGpuIdle()
    {
        if (_device == null || _fence == null)
        {
            return;
        }

        D3D12Diagnostics.EnsureSuccess(_device.CommandQueue->Signal(_fence, _fenceValues[_frameIndex]), "ID3D12CommandQueue::Signal", "signaling the GPU-idle fence");
        D3D12Diagnostics.EnsureSuccess(_fence->SetEventOnCompletion(_fenceValues[_frameIndex], _fenceEvent), "ID3D12Fence::SetEventOnCompletion", "waiting for the GPU to go idle");
        _ = WaitForSingleObjectEx(_fenceEvent, INFINITE, FALSE);
        _fenceValues[_frameIndex]++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        WaitForGpuIdle();

        ReleaseReadbackBuffer();

        if (_frameConstantsBuffer != null)
        {
            _frameConstantsBuffer->Unmap(0, null);
            _frameConstantsMapped = null;
            _ = _frameConstantsBuffer->Release();
            _frameConstantsBuffer = null;
        }

        if (_objectConstantsBuffer != null)
        {
            _objectConstantsBuffer->Unmap(0, null);
            _objectConstantsMapped = null;
            _ = _objectConstantsBuffer->Release();
            _objectConstantsBuffer = null;
        }

        if (_srvHeap != null)
        {
            _ = _srvHeap->Release();
            _srvHeap = null;
        }

        for (int i = 0; i < _materialTextures.Length; i++)
        {
            if (_materialTextures[i] != null)
            {
                _ = _materialTextures[i]->Release();
                _materialTextures[i] = null;
            }
        }

        for (int i = 0; i < _indexBuffers.Length; i++)
        {
            if (_indexBuffers[i] != null)
            {
                _ = _indexBuffers[i]->Release();
                _indexBuffers[i] = null;
            }
        }

        for (int i = 0; i < _vertexBuffers.Length; i++)
        {
            if (_vertexBuffers[i] != null)
            {
                _ = _vertexBuffers[i]->Release();
                _vertexBuffers[i] = null;
            }
        }

        if (_pipelineState != null)
        {
            _ = _pipelineState->Release();
            _pipelineState = null;
        }

        if (_rootSignature != null)
        {
            _ = _rootSignature->Release();
            _rootSignature = null;
        }

        if (_fenceEvent != HANDLE.NULL)
        {
            _ = CloseHandle(_fenceEvent);
            _fenceEvent = HANDLE.NULL;
        }

        if (_fence != null)
        {
            _ = _fence->Release();
            _fence = null;
        }

        if (_commandList != null)
        {
            _ = _commandList->Release();
            _commandList = null;
        }

        for (int i = 0; i < FrameCount; i++)
        {
            if (_commandAllocators[i] != null)
            {
                _ = _commandAllocators[i]->Release();
                _commandAllocators[i] = null;
            }
        }

        if (_depthBuffer != null)
        {
            _ = _depthBuffer->Release();
            _depthBuffer = null;
        }

        if (_dsvHeap != null)
        {
            _ = _dsvHeap->Release();
            _dsvHeap = null;
        }

        for (int i = 0; i < FrameCount; i++)
        {
            if (_backBuffers[i] != null)
            {
                _ = _backBuffers[i]->Release();
                _backBuffers[i] = null;
            }
        }

        if (_rtvHeap != null)
        {
            _ = _rtvHeap->Release();
            _rtvHeap = null;
        }

        if (_swapChain != null)
        {
            _ = _swapChain->Release();
            _swapChain = null;
        }

        _device?.Dispose();
        _device = null;
    }
}
