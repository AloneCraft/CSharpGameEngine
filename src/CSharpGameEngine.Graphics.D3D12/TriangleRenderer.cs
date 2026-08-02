using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Compiler.Hlsl;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_CULL_MODE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_FENCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_SIGNATURE_FLAGS;
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
/// Owns the swap chain, pipeline, and vertex buffer for the Windows + D3D12 vertical slice and
/// draws exactly one colored triangle per frame. Not a general-purpose renderer: the next
/// milestone is expected to replace this with a real render graph.
/// </summary>
public sealed unsafe class TriangleRenderer : IDisposable
{
    public const int FrameCount = 2;
    public const DXGI_FORMAT BackBufferFormat = DXGI_FORMAT_R8G8B8A8_UNORM;

    private static readonly Vector4 ClearColorVector = new(0.02f, 0.02f, 0.08f, 1.0f);
    public static readonly PixelColor ClearColor = new(5, 5, 20, 255);

    private struct Vertex
    {
        public Vector3 Position;
        public Vector4 Color;
    }

    private GraphicsDevice? _device;
    private IDXGISwapChain3* _swapChain;
    private ID3D12DescriptorHeap* _rtvHeap;
    private uint _rtvDescriptorSize;
    private readonly ID3D12Resource*[] _backBuffers = new ID3D12Resource*[FrameCount];
    private readonly ID3D12CommandAllocator*[] _commandAllocators = new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList* _commandList;
    private ID3D12Fence* _fence;
    private readonly ulong[] _fenceValues = new ulong[FrameCount];
    private HANDLE _fenceEvent;
    private ID3D12RootSignature* _rootSignature;
    private ID3D12PipelineState* _pipelineState;
    private ID3D12Resource* _vertexBuffer;
    private D3D12_VERTEX_BUFFER_VIEW _vertexBufferView;
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

    public static TriangleRenderer Create(nint hwnd, int width, int height, GraphicsDeviceOptions deviceOptions, string shaderPath)
    {
        ArgumentNullException.ThrowIfNull(shaderPath);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Initial width/height must both be positive.");
        }

        TriangleRenderer renderer = new()
        {
            _width = width,
            _height = height,
        };

        bool completed = false;
        try
        {
            renderer.Initialize((HWND)hwnd, width, height, deviceOptions, shaderPath);
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

    private TriangleRenderer()
    {
    }

    private void Initialize(HWND hwnd, int width, int height, GraphicsDeviceOptions deviceOptions, string shaderPath)
    {
        _device = GraphicsDevice.Create(deviceOptions);

        CreateSwapChain(hwnd, width, height);
        CreateRtvHeap();
        CreateRenderTargetViews();

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
        CreateVertexBuffer();
        UpdateViewportAndScissor(width, height);
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
        D3D12_ROOT_SIGNATURE_DESC rootSignatureDesc = new()
        {
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
                throw new D3D12Exception("D3D12SerializeRootSignature", hr.Value, "serializing the empty root signature", error);
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
        fixed (byte* pColor = "COLOR\0"u8)
        fixed (byte* pVertexShader = _vertexShaderBytecode)
        fixed (byte* pPixelShader = _pixelShaderBytecode)
        {
            D3D12_INPUT_ELEMENT_DESC* inputElements = stackalloc D3D12_INPUT_ELEMENT_DESC[2]
            {
                new D3D12_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)pPosition,
                    Format = DXGI_FORMAT_R32G32B32_FLOAT,
                    InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)pColor,
                    Format = DXGI_FORMAT_R32G32B32A32_FLOAT,
                    AlignedByteOffset = 12,
                    InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
            };

            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = new()
            {
                InputLayout = new D3D12_INPUT_LAYOUT_DESC
                {
                    pInputElementDescs = inputElements,
                    NumElements = 2,
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
                SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
            };
            psoDesc.DepthStencilState.DepthEnable = FALSE;
            // Vertex winding follows the task's stated top/bottom-left/bottom-right order, which is
            // counter-clockwise in NDC; disable culling rather than reorder the specified vertices.
            psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
            psoDesc.RTVFormats[0] = BackBufferFormat;

            ID3D12PipelineState* pipelineState;
            D3D12Diagnostics.EnsureSuccess(
                _device!.Device->CreateGraphicsPipelineState(&psoDesc, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState),
                "ID3D12Device::CreateGraphicsPipelineState", "creating the triangle pipeline state");
            _pipelineState = pipelineState;
        }
    }

    private void CreateVertexBuffer()
    {
        const int vertexCount = 3;
        Vertex* vertices = stackalloc Vertex[vertexCount]
        {
            new Vertex { Position = new Vector3(0.0f, 0.6f, 0.0f), Color = new Vector4(1, 0, 0, 1) },
            new Vertex { Position = new Vector3(-0.6f, -0.6f, 0.0f), Color = new Vector4(0, 1, 0, 1) },
            new Vertex { Position = new Vector3(0.6f, -0.6f, 0.0f), Color = new Vector4(0, 0, 1, 1) },
        };

        uint bufferSize = (uint)sizeof(Vertex) * vertexCount;

        D3D12_HEAP_PROPERTIES heapProps = new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(bufferSize);

        ID3D12Resource* vertexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            _device!.Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATE_GENERIC_READ, pOptimizedClearValue: null, __uuidof<ID3D12Resource>(), (void**)&vertexBuffer),
            "ID3D12Device::CreateCommittedResource", "creating the triangle vertex buffer (upload heap)");

        D3D12_RANGE noRead = new();
        byte* mapped;
        D3D12Diagnostics.EnsureSuccess(vertexBuffer->Map(0, &noRead, (void**)&mapped), "ID3D12Resource::Map", "mapping the vertex buffer");
        Unsafe.CopyBlock(mapped, vertices, bufferSize);
        vertexBuffer->Unmap(0, null);

        _vertexBuffer = vertexBuffer;
        _vertexBufferView = new D3D12_VERTEX_BUFFER_VIEW
        {
            BufferLocation = vertexBuffer->GetGPUVirtualAddress(),
            StrideInBytes = (uint)sizeof(Vertex),
            SizeInBytes = bufferSize,
        };
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

        // ResizeBuffers restarts the swap chain's back-buffer index cycle (typically back to 0),
        // which would otherwise desynchronize the per-slot fence values from the fence's actual
        // completed value and hang a later SetEventOnCompletion wait on a value nothing ever
        // signals. Resync every slot to the value we just confirmed the GPU has reached.
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

        D3D12Diagnostics.EnsureSuccess(
            _swapChain->ResizeBuffers(FrameCount, (uint)width, (uint)height, BackBufferFormat, 0),
            "IDXGISwapChain3::ResizeBuffers", "resizing the swap chain");

        CreateRenderTargetViews();
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
        _commandList->OMSetRenderTargets(1, &rtvHandle, FALSE, null);

        Vector4 clearColor = ClearColorVector;
        _commandList->ClearRenderTargetView(rtvHandle, (float*)&clearColor, 0, null);

        _commandList->SetGraphicsRootSignature(_rootSignature);
        _commandList->SetPipelineState(_pipelineState);

        fixed (D3D12_VIEWPORT* pViewport = &_viewport)
        fixed (RECT* pScissorRect = &_scissorRect)
        fixed (D3D12_VERTEX_BUFFER_VIEW* pVertexBufferView = &_vertexBufferView)
        {
            _commandList->RSSetViewports(1, pViewport);
            _commandList->RSSetScissorRects(1, pScissorRect);
            _commandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            _commandList->IASetVertexBuffers(0, 1, pVertexBufferView);
            _commandList->DrawInstanced(3, 1, 0, 0);
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

    public PixelReadbackResult? LastCapturedFrame { get; private set; }

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
