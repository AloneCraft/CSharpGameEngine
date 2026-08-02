using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Minimal in-process D3D12 renderer for the VSIX tool window (ADR-0008), built on
/// <c>Vortice.Windows</c> (netstandard2.0-compatible) instead of the net10.0-only
/// <c>TerraFX.Interop.Windows</c> the rest of the engine uses. Deliberately not shared with
/// <c>CSharpGameEngine.Graphics.D3D12</c> - simplest possible synchronous (no frames-in-flight)
/// renderer: fixed camera, Lambertian shading, no textures/materials beyond baseColorFactor.
/// </summary>
internal sealed class VorticeRenderer : IDisposable
{
    private const int FrameCount = 2;
    private static readonly Format BackBufferFormat = Format.R8G8B8A8_UNorm;
    private static readonly Format DepthBufferFormat = Format.D32_Float;

    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue commandQueue;
    private readonly IDXGISwapChain3 swapChain;
    private readonly ID3D12DescriptorHeap rtvHeap;
    private readonly int rtvDescriptorSize;
    private readonly ID3D12Resource[] renderTargets = new ID3D12Resource[FrameCount];
    private readonly ID3D12DescriptorHeap dsvHeap;
    private ID3D12Resource depthBuffer;
    private readonly ID3D12CommandAllocator commandAllocator;
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly ID3D12RootSignature rootSignature;
    private readonly ID3D12PipelineState pipelineState;
    private readonly ID3D12Resource vertexBuffer;
    private readonly VertexBufferView vertexBufferView;
    private readonly ID3D12Resource indexBuffer;
    private readonly IndexBufferView indexBufferView;
    private readonly int indexCount;
    private readonly ID3D12Resource constantBuffer;
    private readonly int constantBufferSize;
    private readonly ID3D12Fence fence;
    private ulong fenceValue;
    private readonly IntPtr fenceEvent;

    private readonly Vector3 boundsCenter;
    private readonly float boundsRadius;
    private readonly Vector4 baseColorFactor;

    private int width;
    private int height;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TransformConstants
    {
        public Matrix4x4 Mvp;
        public Vector4 BaseColorFactor;
    }

    private const string ShaderSource = """
        cbuffer TransformCB : register(b0)
        {
            row_major float4x4 MVP;
            float4 BaseColorFactor;
        };

        struct VertexInput { float3 Position : POSITION; float3 Normal : NORMAL; };
        struct VertexOutput { float4 Position : SV_Position; float3 Normal : NORMAL; };

        static const float3 LightDirectionRaw = float3(0.4, 0.7, 0.5);
        static const float3 AmbientColor = float3(0.12, 0.12, 0.15);

        VertexOutput VSMain(VertexInput input)
        {
            VertexOutput output;
            output.Position = mul(float4(input.Position, 1.0), MVP);
            output.Normal = input.Normal;
            return output;
        }

        float4 PSMain(VertexOutput input) : SV_Target
        {
            float3 n = normalize(input.Normal);
            float3 lightDirection = normalize(LightDirectionRaw);
            float ndotl = max(dot(n, lightDirection), 0.0);
            return float4(saturate(AmbientColor + BaseColorFactor.rgb * ndotl), 1.0);
        }
        """;

    public VorticeRenderer(IntPtr hwnd, int width, int height, SimpleMesh mesh)
    {
        this.width = Math.Max(1, width);
        this.height = Math.Max(1, height);
        boundsCenter = mesh.BoundsCenter;
        boundsRadius = mesh.BoundsRadius > 0f ? mesh.BoundsRadius : 1f;
        baseColorFactor = mesh.BaseColorFactor;

        using IDXGIFactory4 factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();

        IDXGIAdapter1? chosenAdapter = null;
        for (int i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
        {
            if ((adapter.Description1.Flags & AdapterFlags.Software) != 0)
            {
                adapter.Dispose();
                continue;
            }

            chosenAdapter = adapter;
            break;
        }

        device = chosenAdapter is not null
            ? D3D12.D3D12CreateDevice<ID3D12Device>(chosenAdapter, FeatureLevel.Level_11_0)
            : D3D12.D3D12CreateDevice<ID3D12Device>(null, FeatureLevel.Level_11_0);
        chosenAdapter?.Dispose();

        commandQueue = device.CreateCommandQueue(CommandListType.Direct);

        SwapChainDescription1 swapChainDescription = new()
        {
            Width = this.width,
            Height = this.height,
            Format = BackBufferFormat,
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = FrameCount,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipDiscard,
        };

        using IDXGISwapChain1 tempSwapChain = factory.CreateSwapChainForHwnd(commandQueue, hwnd, swapChainDescription);
        swapChain = tempSwapChain.QueryInterface<IDXGISwapChain3>();

        rtvHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount));
        rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        CreateRenderTargetViews();

        dsvHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1));
        depthBuffer = CreateDepthBuffer(this.width, this.height);

        commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, commandAllocator);
        commandList.Close();

        RootParameter1 transformParameter = new(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All);
        RootSignatureDescription1 rootSignatureDescription = new(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [transformParameter]);
        rootSignature = device.CreateRootSignature(0, new VersionedRootSignatureDescription(rootSignatureDescription));

        Result vsCompileResult = Compiler.Compile(ShaderSource, "VSMain", "VorticeRenderer", "vs_5_0", out Blob? vertexShaderBlob, out Blob? vsErrorBlob);
        if (vsCompileResult.Failure || vertexShaderBlob is null)
        {
            throw new InvalidOperationException($"Vertex shader compilation failed: {vsErrorBlob?.AsString()}");
        }

        Result psCompileResult = Compiler.Compile(ShaderSource, "PSMain", "VorticeRenderer", "ps_5_0", out Blob? pixelShaderBlob, out Blob? psErrorBlob);
        if (psCompileResult.Failure || pixelShaderBlob is null)
        {
            throw new InvalidOperationException($"Pixel shader compilation failed: {psErrorBlob?.AsString()}");
        }

        using Blob vertexShaderBlobOwned = vertexShaderBlob;
        using Blob pixelShaderBlobOwned = pixelShaderBlob;
        vsErrorBlob?.Dispose();
        psErrorBlob?.Dispose();

        InputElementDescription[] inputElements =
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
        ];

        GraphicsPipelineStateDescription psoDescription = new()
        {
            RootSignature = rootSignature,
            VertexShader = vertexShaderBlobOwned.AsBytes(),
            PixelShader = pixelShaderBlobOwned.AsBytes(),
            InputLayout = new InputLayoutDescription(inputElements),
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RasterizerState = RasterizerDescription.CullNone,
            BlendState = BlendDescription.Opaque,
            DepthStencilState = DepthStencilDescription.Default,
            DepthStencilFormat = DepthBufferFormat,
            RenderTargetFormats = [BackBufferFormat],
            SampleDescription = new SampleDescription(1, 0),
        };
        pipelineState = device.CreateGraphicsPipelineState(psoDescription);

        Vertex[] vertices = new Vertex[mesh.Positions.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vertex { Position = mesh.Positions[i], Normal = mesh.Normals[i] };
        }

        int vertexBufferSize = vertices.Length * Marshal.SizeOf<Vertex>();
        vertexBuffer = CreateUploadBuffer(vertexBufferSize, vertices);
        vertexBufferView = new VertexBufferView(vertexBuffer.GPUVirtualAddress, vertexBufferSize, Marshal.SizeOf<Vertex>());

        indexCount = mesh.Indices.Length;
        int indexBufferSize = indexCount * sizeof(uint);
        indexBuffer = CreateUploadBuffer(indexBufferSize, mesh.Indices);
        // is32Bit: true - mesh.Indices is uint[] (32-bit). Passing false here silently
        // reinterprets the index buffer as 16-bit indices, scrambling the triangle list into
        // garbage geometry (confirmed empirically - this was the cause of the "bowtie"-looking
        // Box.glb render).
        indexBufferView = new IndexBufferView(indexBuffer.GPUVirtualAddress, indexBufferSize, true);

        constantBufferSize = (Marshal.SizeOf<TransformConstants>() + 255) & ~255;
        constantBuffer = device.CreateCommittedResource(
            HeapType.Upload,
            new ResourceDescription
            {
                Dimension = ResourceDimension.Buffer,
                Width = (ulong)constantBufferSize,
                Height = 1,
                DepthOrArraySize = 1,
                MipLevels = 1,
                SampleDescription = new SampleDescription(1, 0),
                Layout = TextureLayout.RowMajor,
                Format = Format.Unknown,
            },
            ResourceStates.GenericRead);

        fence = device.CreateFence(0);
        fenceValue = 1;
        fenceEvent = NativeEvent.CreateAutoResetEvent();

        UpdateTransformConstantBuffer();
    }

    private ID3D12Resource CreateUploadBuffer<T>(int sizeInBytes, T[] data) where T : struct
    {
        ID3D12Resource resource = device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer((ulong)sizeInBytes),
            ResourceStates.GenericRead);

        // Avoid `unsafe`/`fixed`/raw pointers: the WPF XAML markup-compile pass builds this
        // project through a generated temporary .csproj that does not inherit AllowUnsafeBlocks
        // (confirmed empirically - see ADR-0008), so unsafe pointer code fails to compile there
        // even though this project's own csproj enables it. MemoryMarshal.Write works on a Span
        // without needing an unsafe context.
        Span<byte> mapped = resource.Map<byte>(0, sizeInBytes);
        int elementSize = Marshal.SizeOf<T>();
        for (int i = 0; i < data.Length; i++)
        {
            MemoryMarshal.Write(mapped.Slice(i * elementSize, elementSize), ref data[i]);
        }
        resource.Unmap(0, null);
        return resource;
    }

    private ID3D12Resource CreateDepthBuffer(int w, int h)
    {
        ResourceDescription description = ResourceDescription.Texture2D(DepthBufferFormat, (uint)w, (uint)h, 1, 1);
        description.Flags = ResourceFlags.AllowDepthStencil;

        ID3D12Resource buffer = device.CreateCommittedResource(
            HeapType.Default,
            description,
            ResourceStates.DepthWrite,
            new ClearValue(DepthBufferFormat, 1.0f, 0));

        device.CreateDepthStencilView(buffer, null, dsvHeap.GetCPUDescriptorHandleForHeapStart());
        return buffer;
    }

    private void CreateRenderTargetViews()
    {
        CpuDescriptorHandle handle = rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (int i = 0; i < FrameCount; i++)
        {
            renderTargets[i] = swapChain.GetBuffer<ID3D12Resource>(i);
            device.CreateRenderTargetView(renderTargets[i], null, handle);
            handle += rtvDescriptorSize;
        }
    }

    private void UpdateTransformConstantBuffer()
    {
        Vector3 eye = boundsCenter + Vector3.Normalize(new Vector3(1, 0.75f, 1)) * (boundsRadius * 3.0f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, boundsCenter, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            (float)(Math.PI / 4.0), Math.Max(1, width) / (float)Math.Max(1, height), 0.05f, boundsRadius * 20f);
        Matrix4x4 mvp = view * projection;

        TransformConstants constants = new() { Mvp = mvp, BaseColorFactor = baseColorFactor };
        Span<byte> mapped = constantBuffer.Map<byte>(0, constantBufferSize);
        MemoryMarshal.Write(mapped, ref constants);
        constantBuffer.Unmap(0, null);
    }

    public void Resize(int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);
        if (newWidth == width && newHeight == height)
        {
            return;
        }

        WaitForGpuIdle();

        for (int i = 0; i < FrameCount; i++)
        {
            renderTargets[i].Dispose();
        }
        depthBuffer.Dispose();

        width = newWidth;
        height = newHeight;
        swapChain.ResizeBuffers(FrameCount, width, height, BackBufferFormat, SwapChainFlags.None);
        CreateRenderTargetViews();
        depthBuffer = CreateDepthBuffer(width, height);

        UpdateTransformConstantBuffer();
    }

    public void RenderFrame()
    {
        commandAllocator.Reset();
        commandList.Reset(commandAllocator, pipelineState);

        int frameIndex = swapChain.CurrentBackBufferIndex;

        commandList.ResourceBarrierTransition(renderTargets[frameIndex], ResourceStates.Present, ResourceStates.RenderTarget);

        CpuDescriptorHandle rtvHandle = rtvHeap.GetCPUDescriptorHandleForHeapStart() + (frameIndex * rtvDescriptorSize);
        CpuDescriptorHandle dsvHandle = dsvHeap.GetCPUDescriptorHandleForHeapStart();
        commandList.OMSetRenderTargets(rtvHandle, dsvHandle);
        commandList.ClearRenderTargetView(rtvHandle, new Vortice.Mathematics.Color4(5 / 255f, 5 / 255f, 20 / 255f, 1f));
        commandList.ClearDepthStencilView(dsvHandle, ClearFlags.Depth, 1.0f, 0);

        commandList.SetGraphicsRootSignature(rootSignature);
        commandList.SetGraphicsRootConstantBufferView(0, constantBuffer.GPUVirtualAddress);
        commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commandList.IASetVertexBuffers(0, vertexBufferView);
        commandList.IASetIndexBuffer(indexBufferView);
        commandList.RSSetViewport(0, 0, width, height);
        commandList.RSSetScissorRect(width, height);
        commandList.DrawIndexedInstanced(indexCount, 1, 0, 0, 0);

        commandList.ResourceBarrierTransition(renderTargets[frameIndex], ResourceStates.RenderTarget, ResourceStates.Present);
        commandList.Close();

        commandQueue.ExecuteCommandList(commandList);
        swapChain.Present(1, PresentFlags.None);

        WaitForGpuIdle();
    }

    private void WaitForGpuIdle()
    {
        ulong valueToSignal = fenceValue++;
        commandQueue.Signal(fence, valueToSignal);
        if (fence.CompletedValue < valueToSignal)
        {
            fence.SetEventOnCompletion(valueToSignal, fenceEvent);
            NativeEvent.Wait(fenceEvent);
        }
    }

    public void Dispose()
    {
        WaitForGpuIdle();

        constantBuffer.Dispose();
        indexBuffer.Dispose();
        vertexBuffer.Dispose();
        pipelineState.Dispose();
        rootSignature.Dispose();
        commandList.Dispose();
        commandAllocator.Dispose();
        depthBuffer.Dispose();
        dsvHeap.Dispose();
        for (int i = 0; i < FrameCount; i++)
        {
            renderTargets[i].Dispose();
        }
        rtvHeap.Dispose();
        swapChain.Dispose();
        commandQueue.Dispose();
        fence.Dispose();
        NativeEvent.Close(fenceEvent);
        device.Dispose();
    }
}
