using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace CSharpGameEngine.VSExtension;

/// <summary>One drawable object in a "シーン" preview (ADR-0020): a mesh, its resolved world transform, and the scene node id it represents (for selection highlighting).</summary>
internal sealed class SceneRenderObject
{
    public required SimpleMesh Mesh { get; init; }

    public required Matrix4x4 World { get; init; }

    public required Guid NodeId { get; init; }
}

/// <summary>
/// Multi-object counterpart to <see cref="VorticeRenderer"/> (ADR-0008) for the "シーン" preview
/// window (ADR-0020): one vertex/index buffer per object, a per-object constant buffer slot, and a
/// selection tint driven by <see cref="SetSelectedNode"/> (published via <see cref="SceneEditorHub"/>
/// when a Hierarchy node is clicked). Deliberately a separate class from <see cref="VorticeRenderer"/>
/// rather than generalizing it in place, so the existing single-mesh <see cref="ViewerHwndHost"/>/
/// <see cref="AssetPreviewHwndHost"/> paths are not put at risk by this change.
/// </summary>
internal sealed class SceneRenderer : IDisposable
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
    private readonly ID3D12Fence fence;
    private ulong fenceValue;
    private readonly IntPtr fenceEvent;

    private readonly ID3D12Resource constantBuffer;
    private readonly int alignedSlotSize;

    private sealed class ObjectResources
    {
        public required Guid NodeId;
        public required Matrix4x4 World;
        public required ID3D12Resource VertexBuffer;
        public required VertexBufferView VertexBufferView;
        public required ID3D12Resource IndexBuffer;
        public required IndexBufferView IndexBufferView;
        public required int IndexCount;
        public required Vector4 BaseColorFactor;
    }

    private readonly List<ObjectResources> objects = [];
    private readonly Vector3 boundsCenter;
    private readonly float boundsRadius;
    private Guid? selectedNodeId;

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
        public float Selected;
    }

    private const string ShaderSource = """
        cbuffer TransformCB : register(b0)
        {
            row_major float4x4 MVP;
            float4 BaseColorFactor;
            float Selected;
        };

        struct VertexInput { float3 Position : POSITION; float3 Normal : NORMAL; };
        struct VertexOutput { float4 Position : SV_Position; float3 Normal : NORMAL; };

        static const float3 LightDirectionRaw = float3(0.4, 0.7, 0.5);
        static const float3 AmbientColor = float3(0.12, 0.12, 0.15);
        static const float3 SelectionTint = float3(0.6, 0.35, 0.05);

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
            float3 shaded = AmbientColor + BaseColorFactor.rgb * ndotl;
            return float4(saturate(shaded + Selected * SelectionTint), 1.0);
        }
        """;

    public SceneRenderer(IntPtr hwnd, int width, int height, IReadOnlyList<SceneRenderObject> renderObjects)
    {
        if (renderObjects.Count == 0)
        {
            throw new ArgumentException("シーンに描画可能なオブジェクトがありません。", nameof(renderObjects));
        }

        this.width = Math.Max(1, width);
        this.height = Math.Max(1, height);

        (boundsCenter, boundsRadius) = ComputeSceneBounds(renderObjects);

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

        Result vsCompileResult = Compiler.Compile(ShaderSource, "VSMain", "SceneRenderer", "vs_5_0", out Blob? vertexShaderBlob, out Blob? vsErrorBlob);
        if (vsCompileResult.Failure || vertexShaderBlob is null)
        {
            throw new InvalidOperationException($"Vertex shader compilation failed: {vsErrorBlob?.AsString()}");
        }

        Result psCompileResult = Compiler.Compile(ShaderSource, "PSMain", "SceneRenderer", "ps_5_0", out Blob? pixelShaderBlob, out Blob? psErrorBlob);
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

        alignedSlotSize = (Marshal.SizeOf<TransformConstants>() + 255) & ~255;
        constantBuffer = device.CreateCommittedResource(
            HeapType.Upload,
            new ResourceDescription
            {
                Dimension = ResourceDimension.Buffer,
                Width = (ulong)(alignedSlotSize * renderObjects.Count),
                Height = 1,
                DepthOrArraySize = 1,
                MipLevels = 1,
                SampleDescription = new SampleDescription(1, 0),
                Layout = TextureLayout.RowMajor,
                Format = Format.Unknown,
            },
            ResourceStates.GenericRead);

        foreach (SceneRenderObject renderObject in renderObjects)
        {
            objects.Add(CreateObjectResources(renderObject));
        }

        fence = device.CreateFence(0);
        fenceValue = 1;
        fenceEvent = NativeEvent.CreateAutoResetEvent();

        UpdateAllConstantBuffers();
    }

    private ObjectResources CreateObjectResources(SceneRenderObject renderObject)
    {
        SimpleMesh mesh = renderObject.Mesh;

        Vertex[] vertices = new Vertex[mesh.Positions.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vertex { Position = mesh.Positions[i], Normal = mesh.Normals[i] };
        }

        int vertexBufferSize = vertices.Length * Marshal.SizeOf<Vertex>();
        ID3D12Resource vertexBuffer = CreateUploadBuffer(vertexBufferSize, vertices);
        VertexBufferView vertexBufferView = new(vertexBuffer.GPUVirtualAddress, vertexBufferSize, Marshal.SizeOf<Vertex>());

        int indexCount = mesh.Indices.Length;
        int indexBufferSize = indexCount * sizeof(uint);
        ID3D12Resource indexBuffer = CreateUploadBuffer(indexBufferSize, mesh.Indices);
        // is32Bit: true - mesh.Indices is uint[] (32-bit); see VorticeRenderer.cs for the empirical
        // "bowtie geometry" pitfall this avoids.
        IndexBufferView indexBufferView = new(indexBuffer.GPUVirtualAddress, indexBufferSize, true);

        return new ObjectResources
        {
            NodeId = renderObject.NodeId,
            World = renderObject.World,
            VertexBuffer = vertexBuffer,
            VertexBufferView = vertexBufferView,
            IndexBuffer = indexBuffer,
            IndexBufferView = indexBufferView,
            IndexCount = indexCount,
            BaseColorFactor = mesh.BaseColorFactor,
        };
    }

    private ID3D12Resource CreateUploadBuffer<T>(int sizeInBytes, T[] data) where T : struct
    {
        ID3D12Resource resource = device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer((ulong)sizeInBytes),
            ResourceStates.GenericRead);

        // Avoid unsafe/fixed raw pointers - the WPF XAML markup-compile pass builds this project
        // through a generated temporary csproj that does not inherit AllowUnsafeBlocks (ADR-0008).
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

    private static (Vector3 Center, float Radius) ComputeSceneBounds(IReadOnlyList<SceneRenderObject> renderObjects)
    {
        Vector3? min = null;
        Vector3? max = null;

        foreach (SceneRenderObject renderObject in renderObjects)
        {
            foreach (Vector3 localPosition in renderObject.Mesh.Positions)
            {
                Vector3 worldPosition = Vector3.Transform(localPosition, renderObject.World);
                min = min is null ? worldPosition : Vector3.Min(min.Value, worldPosition);
                max = max is null ? worldPosition : Vector3.Max(max.Value, worldPosition);
            }
        }

        Vector3 center = (min!.Value + max!.Value) * 0.5f;
        float radius = 0f;
        foreach (SceneRenderObject renderObject in renderObjects)
        {
            foreach (Vector3 localPosition in renderObject.Mesh.Positions)
            {
                Vector3 worldPosition = Vector3.Transform(localPosition, renderObject.World);
                radius = Math.Max(radius, Vector3.Distance(center, worldPosition));
            }
        }

        return (center, radius > 0f ? radius : 1f);
    }

    private void UpdateAllConstantBuffers()
    {
        Matrix4x4 view = ComputeViewMatrix();
        Matrix4x4 projection = ComputeProjectionMatrix();

        int totalSize = alignedSlotSize * objects.Count;
        Span<byte> mapped = constantBuffer.Map<byte>(0, totalSize);
        for (int i = 0; i < objects.Count; i++)
        {
            ObjectResources obj = objects[i];
            Matrix4x4 mvp = obj.World * view * projection;
            float selected = obj.NodeId == selectedNodeId ? 1f : 0f;
            TransformConstants constants = new() { Mvp = mvp, BaseColorFactor = obj.BaseColorFactor, Selected = selected };
            MemoryMarshal.Write(mapped.Slice(i * alignedSlotSize, Marshal.SizeOf<TransformConstants>()), ref constants);
        }
        constantBuffer.Unmap(0, null);
    }

    private Matrix4x4 ComputeViewMatrix()
    {
        Vector3 eye = boundsCenter + Vector3.Normalize(new Vector3(1, 0.75f, 1)) * (boundsRadius * 3.0f);
        return Matrix4x4.CreateLookAt(eye, boundsCenter, Vector3.UnitY);
    }

    private Matrix4x4 ComputeProjectionMatrix() =>
        Matrix4x4.CreatePerspectiveFieldOfView(
            (float)(Math.PI / 4.0), Math.Max(1, width) / (float)Math.Max(1, height), 0.05f, boundsRadius * 20f);

    /// <summary>Called when <see cref="SceneEditorHub.SelectedNodeChanged"/> fires from the Hierarchy tool window.</summary>
    public void SetSelectedNode(Guid? nodeId)
    {
        selectedNodeId = nodeId;
        UpdateAllConstantBuffers();
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

        UpdateAllConstantBuffers();
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
        commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commandList.RSSetViewport(0, 0, width, height);
        commandList.RSSetScissorRect(width, height);

        for (int i = 0; i < objects.Count; i++)
        {
            ObjectResources obj = objects[i];
            commandList.SetGraphicsRootConstantBufferView(0, constantBuffer.GPUVirtualAddress + (ulong)(i * alignedSlotSize));
            commandList.IASetVertexBuffers(0, obj.VertexBufferView);
            commandList.IASetIndexBuffer(obj.IndexBufferView);
            commandList.DrawIndexedInstanced(obj.IndexCount, 1, 0, 0, 0);
        }

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
        foreach (ObjectResources obj in objects)
        {
            obj.IndexBuffer.Dispose();
            obj.VertexBuffer.Dispose();
        }
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
