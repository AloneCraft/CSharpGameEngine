using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// One drawable object in the Scene Designer preview: a mesh, its resolved world transform, and the
/// <see cref="NodePath"/> it represents (for selection highlighting). Identity switched from a
/// <c>.scene</c>-JSON <c>Guid</c> to <see cref="NodePath"/> (ADR-0040 Phase 9) since the live-Roslyn
/// SceneModel pipeline has no Guid concept - NodePath is already the sole node identity Hierarchy uses.
/// </summary>
internal sealed class SceneRenderObject {
    public required SimpleMesh Mesh { get; init;}

    public required Matrix4x4 World { get; init;}

    public required NodePath NodeId { get; init;}
}

/// <summary>
/// Multi-object counterpart to <see cref="VorticeRenderer"/> (ADR-0008) for the "シーン" preview
/// window (ADR-0020): one vertex/index buffer per object, a per-object constant buffer slot, and a
/// selection tint driven by <see cref="SetSelectedNode"/> (published via <see cref="SceneEditorHub"/>
/// when a Hierarchy node is clicked). Deliberately a separate class from <see cref="VorticeRenderer"/>
/// rather than generalizing it in place, so the existing single-mesh <see cref="ViewerHwndHost"/>/
/// <see cref="AssetPreviewHwndHost"/> paths are not put at risk by this change.
/// </summary>
internal sealed class SceneRenderer:IDisposable {
    private const int FrameCount=2;
    private static readonly Format BackBufferFormat=Format.R8G8B8A8_UNorm;
    private static readonly Format DepthBufferFormat=Format.D32_Float;

    private readonly ID3D12Device Device;
    private readonly ID3D12CommandQueue CommandQueue;
    private readonly IDXGISwapChain3 SwapChain;
    private readonly ID3D12DescriptorHeap RtvHeap;
    private readonly int RtvDescriptorSize;
    private readonly ID3D12Resource[]RenderTargets=new ID3D12Resource[FrameCount];
    private readonly ID3D12DescriptorHeap DsvHeap;
    private ID3D12Resource DepthBuffer;
    private readonly ID3D12CommandAllocator CommandAllocator;
    private readonly ID3D12GraphicsCommandList CommandList;
    private readonly ID3D12RootSignature RootSignature;
    private readonly ID3D12PipelineState PipelineState;
    private readonly ID3D12Fence Fence;
    private ulong FenceValue;
    private readonly IntPtr FenceEvent;

    private readonly ID3D12Resource ConstantBuffer;
    private readonly int AlignedSlotSize;

    private sealed class ObjectResources {
        public required NodePath NodeId;
        public required Matrix4x4 World;
        public required ID3D12Resource VertexBuffer;
        public required VertexBufferView VertexBufferView;
        public required ID3D12Resource IndexBuffer;
        public required IndexBufferView IndexBufferView;
        public required int IndexCount;
        public required Vector4 BaseColorFactor;
    }

    private readonly List<ObjectResources>Objects=[];
    private readonly Vector3 BoundsCenter;
    private readonly float BoundsRadius;
    private NodePath?SelectedNodeId;

    private int Width;
    private int Height;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex {
        public Vector3 Position;
        public Vector3 Normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TransformConstants {
        public Matrix4x4 Mvp;
        public Vector4 BaseColorFactor;
        public float Selected;
    }

    private const string ShaderSource="""
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

    public SceneRenderer(IntPtr Hwnd,int Width,int Height,IReadOnlyList<SceneRenderObject>RenderObjects){
        if(RenderObjects.Count==0){
            throw new ArgumentException("シーンに描画可能なオブジェクトがありません。",nameof(RenderObjects));
        }

        this.Width=Math.Max(1,Width);
        this.Height=Math.Max(1,Height);

        (BoundsCenter,BoundsRadius)=ComputeSceneBounds(RenderObjects);

        using IDXGIFactory4 Factory=DXGI.CreateDXGIFactory1<IDXGIFactory4>();

        IDXGIAdapter1?ChosenAdapter=null;
        for(var i=0;Factory.EnumAdapters1(i,out IDXGIAdapter1 Adapter).Success;i++){
            if((Adapter.Description1.Flags&AdapterFlags.Software)!=0){
                Adapter.Dispose();
                continue;
            }

            ChosenAdapter=Adapter;
            break;
        }

        Device=ChosenAdapter is not null
            ?D3D12.D3D12CreateDevice<ID3D12Device>(ChosenAdapter,FeatureLevel.Level_11_0)
            :D3D12.D3D12CreateDevice<ID3D12Device>(null,FeatureLevel.Level_11_0);
        ChosenAdapter?.Dispose();

        CommandQueue=Device.CreateCommandQueue(CommandListType.Direct);

        SwapChainDescription1 swapChainDescription=new(){
            Width=this.Width,
            Height=this.Height,
            Format=BackBufferFormat,
            BufferUsage=Usage.RenderTargetOutput,
            BufferCount=FrameCount,
            SampleDescription=new SampleDescription(1,0),
            SwapEffect=SwapEffect.FlipDiscard,
        };

        using IDXGISwapChain1 TempSwapChain=Factory.CreateSwapChainForHwnd(CommandQueue,Hwnd,swapChainDescription);
        SwapChain=TempSwapChain.QueryInterface<IDXGISwapChain3>();

        RtvHeap=Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView,FrameCount));
        RtvDescriptorSize=Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        CreateRenderTargetViews();

        DsvHeap=Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView,1));
        DepthBuffer=CreateDepthBuffer(this.Width,this.Height);

        CommandAllocator=Device.CreateCommandAllocator(CommandListType.Direct);
        CommandList=Device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct,CommandAllocator);
        CommandList.Close();

        RootParameter1 TransformParameter=new(RootParameterType.ConstantBufferView,new RootDescriptor1(0,0),ShaderVisibility.All);
        RootSignatureDescription1 rootSignatureDescription=new(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [TransformParameter]);
        RootSignature=Device.CreateRootSignature(0,new VersionedRootSignatureDescription(rootSignatureDescription));

        Result VsCompileResult=Compiler.Compile(ShaderSource,"VSMain","SceneRenderer","vs_5_0",out Blob?VertexShaderBlob,out Blob?VsErrorBlob);
        if(VsCompileResult.Failure||VertexShaderBlob is null){
            throw new InvalidOperationException($"Vertex shader compilation failed: {VsErrorBlob?.AsString()}");
        }

        Result PsCompileResult=Compiler.Compile(ShaderSource,"PSMain","SceneRenderer","ps_5_0",out Blob?PixelShaderBlob,out Blob?PsErrorBlob);
        if(PsCompileResult.Failure||PixelShaderBlob is null){
            throw new InvalidOperationException($"Pixel shader compilation failed: {PsErrorBlob?.AsString()}");
        }

        using Blob VertexShaderBlobOwned=VertexShaderBlob;
        using Blob PixelShaderBlobOwned=PixelShaderBlob;
        VsErrorBlob?.Dispose();
        PsErrorBlob?.Dispose();

        InputElementDescription[]InputElements=[
            new InputElementDescription("POSITION",0,Format.R32G32B32_Float,0,0),
            new InputElementDescription("NORMAL",0,Format.R32G32B32_Float,12,0),
        ];

        GraphicsPipelineStateDescription PsoDescription=new(){
            RootSignature=RootSignature,
            VertexShader=VertexShaderBlobOwned.AsBytes(),
            PixelShader=PixelShaderBlobOwned.AsBytes(),
            InputLayout=new InputLayoutDescription(InputElements),
            SampleMask=uint.MaxValue,
            PrimitiveTopologyType=PrimitiveTopologyType.Triangle,
            RasterizerState=RasterizerDescription.CullNone,
            BlendState=BlendDescription.Opaque,
            DepthStencilState=DepthStencilDescription.Default,
            DepthStencilFormat=DepthBufferFormat,
            RenderTargetFormats=[BackBufferFormat],
            SampleDescription=new SampleDescription(1,0),
        };
        PipelineState=Device.CreateGraphicsPipelineState(PsoDescription);

        AlignedSlotSize=(Marshal.SizeOf<TransformConstants>()+255)&~255;
        ConstantBuffer=Device.CreateCommittedResource(
            HeapType.Upload,
            new ResourceDescription {
                Dimension=ResourceDimension.Buffer,
                Width=(ulong)(AlignedSlotSize*RenderObjects.Count),
                Height=1,
                DepthOrArraySize=1,
                MipLevels=1,
                SampleDescription=new SampleDescription(1,0),
                Layout=TextureLayout.RowMajor,
                Format=Format.Unknown,
            },
            ResourceStates.GenericRead);

        foreach(SceneRenderObject RenderObject in RenderObjects){
            Objects.Add(CreateObjectResources(RenderObject));
        }

        Fence=Device.CreateFence(0);
        FenceValue=1;
        FenceEvent=NativeEvent.CreateAutoResetEvent();

        UpdateAllConstantBuffers();
    }

    private ObjectResources CreateObjectResources(SceneRenderObject RenderObject){
        SimpleMesh Mesh=RenderObject.Mesh;

        Vertex[]Vertices=new Vertex[Mesh.Positions.Length];
        for(var i=0;i<Vertices.Length;i++){
            Vertices[i]=new Vertex { Position=Mesh.Positions[i],Normal=Mesh.Normals[i]};
        }

        var VertexBufferSize=Vertices.Length*Marshal.SizeOf<Vertex>();
        ID3D12Resource VertexBuffer=CreateUploadBuffer(VertexBufferSize,Vertices);
        VertexBufferView vertexBufferView=new(VertexBuffer.GPUVirtualAddress,VertexBufferSize,Marshal.SizeOf<Vertex>());

        var IndexCount=Mesh.Indices.Length;
        var IndexBufferSize=IndexCount*sizeof(uint);
        ID3D12Resource IndexBuffer=CreateUploadBuffer(IndexBufferSize,Mesh.Indices);
        // is32Bit: true - mesh.Indices is uint[] (32-bit); see VorticeRenderer.cs for the empirical
        // "bowtie geometry" pitfall this avoids.
        IndexBufferView indexBufferView=new(IndexBuffer.GPUVirtualAddress,IndexBufferSize,true);

        return new ObjectResources {
            NodeId=RenderObject.NodeId,
            World=RenderObject.World,
            VertexBuffer=VertexBuffer,
            VertexBufferView=vertexBufferView,
            IndexBuffer=IndexBuffer,
            IndexBufferView=indexBufferView,
            IndexCount=IndexCount,
            BaseColorFactor=Mesh.BaseColorFactor,
        };
    }

    private ID3D12Resource CreateUploadBuffer<T>(int SizeInBytes,T[]Data) where T:struct {
        ID3D12Resource Resource=Device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer((ulong)SizeInBytes),
            ResourceStates.GenericRead);

        // Avoid unsafe/fixed raw pointers - the WPF XAML markup-compile pass builds this project
        // through a generated temporary csproj that does not inherit AllowUnsafeBlocks (ADR-0008).
        Span<byte>Mapped=Resource.Map<byte>(0,SizeInBytes);
        var ElementSize=Marshal.SizeOf<T>();
        for(var i=0;i<Data.Length;i++){
            MemoryMarshal.Write(Mapped.Slice(i*ElementSize,ElementSize),ref Data[i]);
        }
        Resource.Unmap(0,null);
        return Resource;
    }

    private ID3D12Resource CreateDepthBuffer(int W,int H){
        ResourceDescription Description=ResourceDescription.Texture2D(DepthBufferFormat,(uint)W,(uint)H,1,1);
        Description.Flags=ResourceFlags.AllowDepthStencil;

        ID3D12Resource buffer=Device.CreateCommittedResource(
            HeapType.Default,
            Description,
            ResourceStates.DepthWrite,
            new ClearValue(DepthBufferFormat,1.0f,0));

        Device.CreateDepthStencilView(buffer,null,DsvHeap.GetCPUDescriptorHandleForHeapStart());
        return buffer;
    }

    private void CreateRenderTargetViews(){
        CpuDescriptorHandle Handle=RtvHeap.GetCPUDescriptorHandleForHeapStart();
        for(var i=0;i<FrameCount;i++){
            RenderTargets[i]=SwapChain.GetBuffer<ID3D12Resource>(i);
            Device.CreateRenderTargetView(RenderTargets[i],null,Handle);
            Handle+=RtvDescriptorSize;
        }
    }

    private static(Vector3 Center,float Radius)ComputeSceneBounds(IReadOnlyList<SceneRenderObject>RenderObjects){
        Vector3?Min=null;
        Vector3?Max=null;

        foreach(SceneRenderObject RenderObject in RenderObjects){
            foreach(Vector3 LocalPosition in RenderObject.Mesh.Positions){
                Vector3 WorldPosition=Vector3.Transform(LocalPosition,RenderObject.World);
                Min=Min is null?WorldPosition:Vector3.Min(Min.Value,WorldPosition);
                Max=Max is null?WorldPosition:Vector3.Max(Max.Value,WorldPosition);
            }
        }

        Vector3 Center=(Min!.Value+Max!.Value)*0.5f;
        var Radius=0f;
        foreach(SceneRenderObject renderObject in RenderObjects){
            foreach(Vector3 localPosition in renderObject.Mesh.Positions){
                Vector3 worldPosition=Vector3.Transform(localPosition,renderObject.World);
                Radius=Math.Max(Radius,Vector3.Distance(Center,worldPosition));
            }
        }

        return(Center,Radius>0f?Radius:1f);
    }

    private void UpdateAllConstantBuffers(){
        Matrix4x4 View=ComputeViewMatrix();
        Matrix4x4 Projection=ComputeProjectionMatrix();

        var TotalSize=AlignedSlotSize*Objects.Count;
        Span<byte>Mapped=ConstantBuffer.Map<byte>(0,TotalSize);
        for(var i=0;i<Objects.Count;i++){
            ObjectResources Obj=Objects[i];
            Matrix4x4 Mvp=Obj.World*View*Projection;
            var Selected=SelectedNodeId is { } Selection&&Obj.NodeId.Equals(Selection)?1f:0f;
            TransformConstants Constants=new(){ Mvp=Mvp,BaseColorFactor=Obj.BaseColorFactor,Selected=Selected };
            MemoryMarshal.Write(Mapped.Slice(i*AlignedSlotSize,Marshal.SizeOf<TransformConstants>()),ref Constants);
        }
        ConstantBuffer.Unmap(0,null);
    }

    private Matrix4x4 ComputeViewMatrix(){
        Vector3 Eye=BoundsCenter+Vector3.Normalize(new Vector3(1,0.75f,1))*(BoundsRadius*3.0f);
        return Matrix4x4.CreateLookAt(Eye,BoundsCenter,Vector3.UnitY);
    }

    private Matrix4x4 ComputeProjectionMatrix()=>
        Matrix4x4.CreatePerspectiveFieldOfView(
            (float)(Math.PI/4.0),Math.Max(1,Width)/(float)Math.Max(1,Height),0.05f,BoundsRadius*20f);

    /// <summary>Called when <see cref="SceneEditorHub.SelectedNodePathChanged"/> fires from the Hierarchy tool window.</summary>
    public void SetSelectedNode(NodePath?NodeId){
        SelectedNodeId=NodeId;
        UpdateAllConstantBuffers();
    }

    public void Resize(int NewWidth,int NewHeight){
        NewWidth=Math.Max(1,NewWidth);
        NewHeight=Math.Max(1,NewHeight);
        if(NewWidth==Width&&NewHeight==Height){
            return;
        }

        WaitForGpuIdle();

        for(var i=0;i<FrameCount;i++){
            RenderTargets[i].Dispose();
        }
        DepthBuffer.Dispose();

        Width=NewWidth;
        Height=NewHeight;
        SwapChain.ResizeBuffers(FrameCount,Width,Height,BackBufferFormat,SwapChainFlags.None);
        CreateRenderTargetViews();
        DepthBuffer=CreateDepthBuffer(Width,Height);

        UpdateAllConstantBuffers();
    }

    public void RenderFrame(){
        CommandAllocator.Reset();
        CommandList.Reset(CommandAllocator,PipelineState);

        var FrameIndex=SwapChain.CurrentBackBufferIndex;

        CommandList.ResourceBarrierTransition(RenderTargets[FrameIndex],ResourceStates.Present,ResourceStates.RenderTarget);

        CpuDescriptorHandle RtvHandle=RtvHeap.GetCPUDescriptorHandleForHeapStart()+(FrameIndex*RtvDescriptorSize);
        CpuDescriptorHandle DsvHandle=DsvHeap.GetCPUDescriptorHandleForHeapStart();
        CommandList.OMSetRenderTargets(RtvHandle,DsvHandle);
        CommandList.ClearRenderTargetView(RtvHandle,new Vortice.Mathematics.Color4(5/255f,5/255f,20/255f,1f));
        CommandList.ClearDepthStencilView(DsvHandle,ClearFlags.Depth,1.0f,0);

        CommandList.SetGraphicsRootSignature(RootSignature);
        CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        CommandList.RSSetViewport(0,0,Width,Height);
        CommandList.RSSetScissorRect(Width,Height);

        for(var i=0;i<Objects.Count;i++){
            ObjectResources Obj=Objects[i];
            CommandList.SetGraphicsRootConstantBufferView(0,ConstantBuffer.GPUVirtualAddress+(ulong)(i*AlignedSlotSize));
            CommandList.IASetVertexBuffers(0,Obj.VertexBufferView);
            CommandList.IASetIndexBuffer(Obj.IndexBufferView);
            CommandList.DrawIndexedInstanced(Obj.IndexCount,1,0,0,0);
        }

        CommandList.ResourceBarrierTransition(RenderTargets[FrameIndex],ResourceStates.RenderTarget,ResourceStates.Present);
        CommandList.Close();

        CommandQueue.ExecuteCommandList(CommandList);
        SwapChain.Present(1,PresentFlags.None);

        WaitForGpuIdle();
    }

    private void WaitForGpuIdle(){
        var ValueToSignal=FenceValue++;
        CommandQueue.Signal(Fence,ValueToSignal);
        if(Fence.CompletedValue<ValueToSignal){
            Fence.SetEventOnCompletion(ValueToSignal,FenceEvent);
            NativeEvent.Wait(FenceEvent);
        }
    }

    public void Dispose(){
        WaitForGpuIdle();

        ConstantBuffer.Dispose();
        foreach(ObjectResources Obj in Objects){
            Obj.IndexBuffer.Dispose();
            Obj.VertexBuffer.Dispose();
        }
        PipelineState.Dispose();
        RootSignature.Dispose();
        CommandList.Dispose();
        CommandAllocator.Dispose();
        DepthBuffer.Dispose();
        DsvHeap.Dispose();
        for(var i=0;i<FrameCount;i++){
            RenderTargets[i].Dispose();
        }
        RtvHeap.Dispose();
        SwapChain.Dispose();
        CommandQueue.Dispose();
        Fence.Dispose();
        NativeEvent.Close(FenceEvent);
        Device.Dispose();
    }
}
