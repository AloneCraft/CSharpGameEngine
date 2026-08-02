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

/// <summary>
/// Owns the swap chain, depth buffer, pipeline, and geometry buffers for the glTF model-viewer
/// vertical slice: one static indexed mesh, one fixed auto-framed camera, simple directional
/// Lambertian shading. Deliberately a separate class from <see cref="TriangleRenderer"/> rather
/// than a shared abstraction - see ADR-0004.
/// </summary>
public sealed unsafe class ModelRenderer : IDisposable
{
    public const int FrameCount = 2;
    public const DXGI_FORMAT BackBufferFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
    public const DXGI_FORMAT DepthBufferFormat = DXGI_FORMAT_D32_FLOAT;

    private static readonly Vector4 ClearColorVector = new(0.02f, 0.02f, 0.08f, 1.0f);
    public static readonly PixelColor ClearColor = new(5, 5, 20, 255);

    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
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
    private ID3D12Resource* _vertexBuffer;
    private D3D12_VERTEX_BUFFER_VIEW _vertexBufferView;
    private ID3D12Resource* _indexBuffer;
    private D3D12_INDEX_BUFFER_VIEW _indexBufferView;
    private uint _indexCount;
    private ID3D12Resource* _constantBuffer;
    private byte* _constantBufferMapped;
    private ID3D12Resource* _texture;
    private ID3D12DescriptorHeap* _srvHeap;
    private Vector4 _baseColorFactor;
    private ID3D12Resource* _readbackBuffer;
    private ulong _readbackBufferSize;
    private D3D12_PLACED_SUBRESOURCE_FOOTPRINT _readbackFootprint;

    private uint _frameIndex;
    private int _width;
    private int _height;
    private bool _sizeIsZero;
    private bool _disposed;

    private Vector3 _boundsCenter;
    private float _boundsRadius;

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

    /// <summary>False if no base-color texture was supplied, or it failed to decode - in either case a 1x1 white fallback texture is used, see <see cref="TextureLoadError"/>.</summary>
    public bool TextureLoaded { get; private set; }

    public string? TextureLoadError { get; private set; }

    public static ModelRenderer Create(
        nint hwnd, int width, int height, GraphicsDeviceOptions deviceOptions, string shaderPath,
        IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals, IReadOnlyList<Vector2> uvs, IReadOnlyList<uint> indices,
        byte[]? baseColorTextureBytes, Vector4 baseColorFactor,
        Vector3 boundsCenter, float boundsRadius)
    {
        ArgumentNullException.ThrowIfNull(shaderPath);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(uvs);
        ArgumentNullException.ThrowIfNull(indices);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Initial width/height must both be positive.");
        }

        if (positions.Count == 0 || positions.Count != normals.Count || positions.Count != uvs.Count || indices.Count == 0)
        {
            throw new ArgumentException("Mesh must have at least one vertex (positions.Count == normals.Count == uvs.Count) and at least one index.");
        }

        ModelRenderer renderer = new()
        {
            _width = width,
            _height = height,
            _boundsCenter = boundsCenter,
            _boundsRadius = boundsRadius,
            _baseColorFactor = baseColorFactor,
        };

        bool completed = false;
        try
        {
            renderer.Initialize((HWND)hwnd, width, height, deviceOptions, shaderPath, positions, normals, uvs, indices, baseColorTextureBytes);
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

    private ModelRenderer()
    {
    }

    private void Initialize(HWND hwnd, int width, int height, GraphicsDeviceOptions deviceOptions, string shaderPath,
        IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals, IReadOnlyList<Vector2> uvs, IReadOnlyList<uint> indices,
        byte[]? baseColorTextureBytes)
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
        CreateVertexBuffer(positions, normals, uvs);
        CreateIndexBuffer(indices);
        CreateConstantBuffer();
        CreateTexture(baseColorTextureBytes);
        UpdateViewportAndScissor(width, height);
        UpdateTransformConstantBuffer(width, height);
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
        // ShaderVisibility is ALL, not just VERTEX: PSMain also reads BaseColorFactor out of the
        // same TransformCB (b0). A vertex-only-visible root parameter would fail PSO creation
        // since the pixel shader references a root parameter the root signature doesn't expose to
        // that stage.
        D3D12_ROOT_PARAMETER transformParam = new()
        {
            ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL,
        };
        transformParam.Descriptor = new D3D12_ROOT_DESCRIPTOR { ShaderRegister = 0, RegisterSpace = 0 };

        D3D12_DESCRIPTOR_RANGE srvRange = new()
        {
            RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors = 1,
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

        D3D12_ROOT_PARAMETER* rootParams = stackalloc D3D12_ROOT_PARAMETER[2];
        rootParams[0] = transformParam;
        rootParams[1] = textureParam;
        rootParams[1].DescriptorTable = new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges = 1, pDescriptorRanges = &srvRange };

        D3D12_ROOT_SIGNATURE_DESC rootSignatureDesc = new()
        {
            NumParameters = 2,
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
                throw new D3D12Exception("D3D12SerializeRootSignature", hr.Value, "serializing the model root signature", error);
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
        fixed (byte* pTexCoord = "TEXCOORD\0"u8)
        fixed (byte* pVertexShader = _vertexShaderBytecode)
        fixed (byte* pPixelShader = _pixelShaderBytecode)
        {
            D3D12_INPUT_ELEMENT_DESC* inputElements = stackalloc D3D12_INPUT_ELEMENT_DESC[3]
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
                    SemanticName = (sbyte*)pTexCoord,
                    Format = DXGI_FORMAT_R32G32_FLOAT,
                    AlignedByteOffset = 24,
                    InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
            };

            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = new()
            {
                InputLayout = new D3D12_INPUT_LAYOUT_DESC
                {
                    pInputElementDescs = inputElements,
                    NumElements = 3,
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
            // Real backface culling is deferred (see ADR-0004); the depth buffer alone is enough
            // for a closed mesh to look correct from any external viewpoint at this stage.
            psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
            psoDesc.RTVFormats[0] = BackBufferFormat;

            ID3D12PipelineState* pipelineState;
            D3D12Diagnostics.EnsureSuccess(
                _device!.Device->CreateGraphicsPipelineState(&psoDesc, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState),
                "ID3D12Device::CreateGraphicsPipelineState", "creating the model pipeline state");
            _pipelineState = pipelineState;
        }
    }

    private void CreateVertexBuffer(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals, IReadOnlyList<Vector2> uvs)
    {
        int vertexCount = positions.Count;
        Vertex[] vertices = new Vertex[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vertex { Position = positions[i], Normal = normals[i], Uv = uvs[i] };
        }

        uint bufferSize = (uint)(sizeof(Vertex) * vertexCount);

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(bufferSize);

        ID3D12Resource* vertexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&vertexBuffer),
            "ID3D12Device::CreateCommittedResource", "creating the model vertex buffer (upload heap)");

        D3D12_RANGE noRead = new();
        byte* mapped;
        D3D12Diagnostics.EnsureSuccess(vertexBuffer->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping the vertex buffer");
        fixed (Vertex* pVertices = vertices)
        {
            Unsafe.CopyBlock(mapped, pVertices, bufferSize);
        }
        vertexBuffer->Unmap(0, null);

        _vertexBuffer = vertexBuffer;
        _vertexBufferView = new D3D12_VERTEX_BUFFER_VIEW
        {
            BufferLocation = vertexBuffer->GetGPUVirtualAddress(),
            StrideInBytes = (uint)sizeof(Vertex),
            SizeInBytes = bufferSize,
        };
    }

    private void CreateIndexBuffer(IReadOnlyList<uint> indices)
    {
        int indexCount = indices.Count;
        uint[] indexArray = indices as uint[] ?? [.. indices];
        uint bufferSize = (uint)(sizeof(uint) * indexCount);

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(bufferSize);

        ID3D12Resource* indexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&indexBuffer),
            "ID3D12Device::CreateCommittedResource", "creating the model index buffer (upload heap)");

        D3D12_RANGE noRead = new();
        byte* mapped;
        D3D12Diagnostics.EnsureSuccess(indexBuffer->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping the index buffer");
        fixed (uint* pIndices = indexArray)
        {
            Unsafe.CopyBlock(mapped, pIndices, bufferSize);
        }
        indexBuffer->Unmap(0, null);

        _indexBuffer = indexBuffer;
        _indexCount = (uint)indexCount;
        _indexBufferView = new D3D12_INDEX_BUFFER_VIEW
        {
            BufferLocation = indexBuffer->GetGPUVirtualAddress(),
            SizeInBytes = bufferSize,
            Format = DXGI_FORMAT_R32_UINT,
        };
    }

    private void CreateConstantBuffer()
    {
        // D3D12 constant buffer resources must be sized in multiples of 256 bytes even though the
        // shader only reads the first 64 (one float4x4).
        const uint constantBufferSize = 256;

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(constantBufferSize);

        ID3D12Resource* constantBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&constantBuffer),
            "ID3D12Device::CreateCommittedResource", "creating the transform constant buffer");

        D3D12_RANGE noRead = new();
        byte* mapped;
        D3D12Diagnostics.EnsureSuccess(constantBuffer->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping the transform constant buffer");

        _constantBuffer = constantBuffer;
        _constantBufferMapped = mapped;
    }

    private void CreateTexture(byte[]? baseColorTextureBytes)
    {
        DecodedImage image = DecodeOrFallback(baseColorTextureBytes);

        D3D12_RESOURCE_DESC textureDesc = D3D12_RESOURCE_DESC.Tex2D(DXGI_FORMAT_R8G8B8A8_UNORM, (uint)image.Width, (uint)image.Height, mipLevels: 1);

        D3D12_HEAP_PROPERTIES defaultHeapProps = new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource* texture;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&defaultHeapProps, D3D12_HEAP_FLAG_NONE, &textureDesc, D3D12_RESOURCE_STATE_COPY_DEST, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&texture),
            "ID3D12Device::CreateCommittedResource", "creating the base-color texture");
        _texture = texture;

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
        ulong totalBytes;
        _device.Device->GetCopyableFootprints(&textureDesc, 0, 1, 0, &footprint, null, null, &totalBytes);

        D3D12_HEAP_PROPERTIES uploadHeapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC stagingDesc = D3D12_RESOURCE_DESC.Buffer(totalBytes);

        ID3D12Resource* staging;
        D3D12Diagnostics.EnsureSuccess(
            _device.Device->CreateCommittedResource(&uploadHeapProps, D3D12_HEAP_FLAG_NONE, &stagingDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&staging),
            "ID3D12Device::CreateCommittedResource", "creating the texture upload staging buffer");

        try
        {
            // GetCopyableFootprints' RowPitch is 256-byte aligned; the decoded image is tightly
            // packed (stride = Width*4). These differ whenever Width*4 isn't already a multiple of
            // 256, so this must be a per-row copy, not one bulk CopyBlock.
            D3D12_RANGE noRead = new();
            byte* mapped;
            D3D12Diagnostics.EnsureSuccess(staging->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping the texture upload staging buffer");

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

            D3D12Diagnostics.EnsureSuccess(_commandAllocators[0]->Reset(), "ID3D12CommandAllocator::Reset", "resetting the allocator for the texture upload");
            D3D12Diagnostics.EnsureSuccess(_commandList->Reset(_commandAllocators[0], null), "ID3D12GraphicsCommandList::Reset", "resetting the command list for the texture upload");

            D3D12_TEXTURE_COPY_LOCATION dst = new() { pResource = _texture, Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            dst.SubresourceIndex = 0;
            D3D12_TEXTURE_COPY_LOCATION src = new() { pResource = staging, Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            src.PlacedFootprint = footprint;
            _commandList->CopyTextureRegion(&dst, 0, 0, 0, &src, null);

            D3D12_RESOURCE_BARRIER toShaderResource = D3D12_RESOURCE_BARRIER.InitTransition(_texture, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            _commandList->ResourceBarrier(1, &toShaderResource);

            D3D12Diagnostics.EnsureSuccess(_commandList->Close(), "ID3D12GraphicsCommandList::Close", "closing the texture upload command list");

            ID3D12CommandList* commandListBase = (ID3D12CommandList*)_commandList;
            _device.CommandQueue->ExecuteCommandLists(1, &commandListBase);

            WaitForGpuIdle();
        }
        finally
        {
            _ = staging->Release();
        }

        D3D12_DESCRIPTOR_HEAP_DESC srvHeapDesc = new()
        {
            Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors = 1,
            Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap* srvHeap;
        D3D12Diagnostics.EnsureSuccess(
            _device.Device->CreateDescriptorHeap(&srvHeapDesc, __uuidof<ID3D12DescriptorHeap>(), (void**)&srvHeap),
            "ID3D12Device::CreateDescriptorHeap", "creating the SRV descriptor heap");
        _srvHeap = srvHeap;

        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = new()
        {
            Format = DXGI_FORMAT_R8G8B8A8_UNORM,
            ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping = (uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        srvDesc.Texture2D.MipLevels = 1;
        _device.Device->CreateShaderResourceView(_texture, &srvDesc, _srvHeap->GetCPUDescriptorHandleForHeapStart());
    }

    private DecodedImage DecodeOrFallback(byte[]? baseColorTextureBytes)
    {
        // Every mesh gets a real SRV bound, even when there is no source texture (or it fails to
        // decode) - a procedural 1x1 white texture keeps this a single PSO/shader path with no
        // branching, and multiplies harmlessly with BaseColorFactor in the pixel shader.
        static DecodedImage WhiteFallback() => new(1, 1, [255, 255, 255, 255]);

        if (baseColorTextureBytes is null || baseColorTextureBytes.Length == 0)
        {
            TextureLoaded = false;
            return WhiteFallback();
        }

        WicDecodeResult decodeResult = WicImageDecoder.Decode(baseColorTextureBytes);
        if (decodeResult.Success)
        {
            TextureLoaded = true;
            return decodeResult.Image!;
        }

        TextureLoaded = false;
        TextureLoadError = decodeResult.Error;
        return WhiteFallback();
    }

    private void UpdateTransformConstantBuffer(int width, int height)
    {
        float fovY = MathF.PI / 4f;
        float radius = MathF.Max(_boundsRadius, 0.001f);
        float distance = radius / MathF.Sin(fovY / 2f) * 1.5f;

        Vector3 eye = _boundsCenter + Vector3.Normalize(new Vector3(1f, 0.75f, 1f)) * distance;
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, _boundsCenter, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            fovY,
            (float)width / height,
            MathF.Max(distance - (radius * 2f), 0.01f),
            distance + (radius * 4f));

        Matrix4x4 mvp = view * projection;
        Unsafe.CopyBlock(_constantBufferMapped, &mvp, (uint)sizeof(Matrix4x4));

        Vector4 baseColorFactor = _baseColorFactor;
        Unsafe.CopyBlock(_constantBufferMapped + 64, &baseColorFactor, (uint)sizeof(Vector4));
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

        // See TriangleRenderer.Resize / ADR-0003 §8: ResizeBuffers restarts the swap chain's
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
        UpdateTransformConstantBuffer(width, height);
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
        _commandList->SetGraphicsRootConstantBufferView(0, _constantBuffer->GetGPUVirtualAddress());
        _commandList->SetGraphicsRootDescriptorTable(1, _srvHeap->GetGPUDescriptorHandleForHeapStart());

        fixed (D3D12_VIEWPORT* pViewport = &_viewport)
        fixed (RECT* pScissorRect = &_scissorRect)
        fixed (D3D12_VERTEX_BUFFER_VIEW* pVertexBufferView = &_vertexBufferView)
        fixed (D3D12_INDEX_BUFFER_VIEW* pIndexBufferView = &_indexBufferView)
        {
            _commandList->RSSetViewports(1, pViewport);
            _commandList->RSSetScissorRects(1, pScissorRect);
            _commandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            _commandList->IASetVertexBuffers(0, 1, pVertexBufferView);
            _commandList->IASetIndexBuffer(pIndexBufferView);
            _commandList->DrawIndexedInstanced(_indexCount, 1, 0, 0, 0);
        }

        if (captureForVerification)
        {
            EnsureReadbackBuffer();

            D3D12_RESOURCE_BARRIER toCopySource = D3D12_RESOURCE_BARRIER.InitTransition(backBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_COPY_SOURCE);
            _commandList->ResourceBarrier(1, &toCopySource);

            D3D12_TEXTURE_COPY_LOCATION dst = new() { pResource = _readbackBuffer, Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            dst.PlacedFootprint = _readbackFootprint;
            D3D12_TEXTURE_COPY_LOCATION src = new() { pResource = backBuffer, Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
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

        if (_srvHeap != null)
        {
            _ = _srvHeap->Release();
            _srvHeap = null;
        }

        if (_texture != null)
        {
            _ = _texture->Release();
            _texture = null;
        }

        if (_constantBuffer != null)
        {
            _constantBuffer->Unmap(0, null);
            _constantBufferMapped = null;
            _ = _constantBuffer->Release();
            _constantBuffer = null;
        }

        if (_indexBuffer != null)
        {
            _ = _indexBuffer->Release();
            _indexBuffer = null;
        }

        if (_vertexBuffer != null)
        {
            _ = _vertexBuffer->Release();
            _vertexBuffer = null;
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
