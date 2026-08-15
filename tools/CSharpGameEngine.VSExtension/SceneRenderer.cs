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

    /// <summary>Sky domeなど、描画するが編集対象ステージの自動フレーミングには含めない場合はfalse。</summary>
    public bool IncludeInBounds { get; init;}=true;
}

/// <summary>
/// Multi-object counterpart to <see cref="VorticeRenderer"/> (ADR-0008) for the Scene Designer tool
/// window (ADR-0020/ADR-0040): one vertex/index buffer per object, a per-object constant buffer slot,
/// and a selection tint driven by <see cref="SetSelectedNode"/>. Deliberately a separate class from
/// <see cref="VorticeRenderer"/> rather than generalizing it in place, so the existing single-mesh
/// <see cref="ViewerHwndHost"/>/<see cref="AssetPreviewHwndHost"/> paths are not put at risk.
///
/// <para>
/// This type holds no editing view state: the camera is injected through <see cref="SetCamera"/> by
/// <see cref="SceneHwndHost"/> and framing/bounds policy lives in <see cref="SceneCameraFraming"/>
/// (ADR-0041 decision 1), because the renderer itself is disposed and rebuilt on every re-analysis.
/// </para>
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
    private NodePath?SelectedNodeId;
    private IReadOnlyCollection<NodePath>?SelectedNodeIds;

    private Matrix4x4 CurrentView;
    private Matrix4x4 CurrentProjection;

    /// <summary>定数バッファの唯一の更新トリガ。ctor/<see cref="SetCamera"/>/<see cref="SetSelectedNode"/>が立て、<see cref="RenderFrame"/>が消費する。</summary>
    private bool ConstantsDirty;

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

    /// <summary>
    /// 描画オブジェクトは0件でも構わない(グリッド/ギズモだけのフレームは正当。M0020 Phase 3 の前提)。
    /// カメラは必須引数にしてある — 「カメラ未設定のまま最初のフレームを描く」未定義状態を
    /// 構造的に作れなくするため(ADR-0041 決定事項1)。
    /// </summary>
    public SceneRenderer(
        IntPtr Hwnd,int Width,int Height,IReadOnlyList<SceneRenderObject>RenderObjects,
        in Matrix4x4 InitialView,in Matrix4x4 InitialProjection){
        this.Width=Math.Max(1,Width);
        this.Height=Math.Max(1,Height);
        this.CurrentView=InitialView;
        this.CurrentProjection=InitialProjection;

        using IDXGIFactory4 Factory=DXGI.CreateDXGIFactory1<IDXGIFactory4>();

        IDXGIAdapter1?ChosenAdapter=null;
        for(var I=0;Factory.EnumAdapters1(I,out IDXGIAdapter1 Adapter).Success;I++){
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

        SwapChainDescription1 SwapChainDescription=new(){
            Width=this.Width,
            Height=this.Height,
            Format=BackBufferFormat,
            BufferUsage=Usage.RenderTargetOutput,
            BufferCount=FrameCount,
            SampleDescription=new SampleDescription(1,0),
            SwapEffect=SwapEffect.FlipDiscard,
        };

        using IDXGISwapChain1 TempSwapChain=Factory.CreateSwapChainForHwnd(CommandQueue,Hwnd,SwapChainDescription);
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
        RootSignatureDescription1 RootSignatureDescription=new(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [TransformParameter]);
        RootSignature=Device.CreateRootSignature(0,new VersionedRootSignatureDescription(RootSignatureDescription));

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
                // D3D12 は 0 バイトの buffer を許可しないので、0件でも1スロット分は確保する。
                Width=(ulong)(AlignedSlotSize*Math.Max(1,RenderObjects.Count)),
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

        ConstantsDirty=true;
    }

    /// <summary>
    /// view/projection を外部(<see cref="SceneHwndHost"/>、M0020 Phase 1 以降は <c>SceneViewState</c>)
    /// から注入する。ADR-0041 決定事項1: レンダラーは編集ビュー状態を持たない。値が変わらなければ
    /// 何もしないので、毎フレーム呼んでも静止時のコストは行列比較2回だけ。
    /// </summary>
    public void SetCamera(in Matrix4x4 View,in Matrix4x4 Projection){
        if(CurrentView.Equals(View)&&CurrentProjection.Equals(Projection)){
            return;
        }

        CurrentView=View;
        CurrentProjection=Projection;
        ConstantsDirty=true;
    }

    private ObjectResources CreateObjectResources(SceneRenderObject RenderObject){
        SimpleMesh Mesh=RenderObject.Mesh;

        Vertex[]Vertices=new Vertex[Mesh.Positions.Length];
        for(var I=0;I<Vertices.Length;I++){
            Vertices[I]=new Vertex { Position=Mesh.Positions[I],Normal=Mesh.Normals[I]};
        }

        var VertexBufferSize=Vertices.Length*Marshal.SizeOf<Vertex>();
        ID3D12Resource VertexBuffer=CreateUploadBuffer(VertexBufferSize,Vertices);
        VertexBufferView VertexBufferView=new(VertexBuffer.GPUVirtualAddress,VertexBufferSize,Marshal.SizeOf<Vertex>());

        var IndexCount=Mesh.Indices.Length;
        var IndexBufferSize=IndexCount*sizeof(uint);
        ID3D12Resource IndexBuffer=CreateUploadBuffer(IndexBufferSize,Mesh.Indices);
        // is32Bit: true - mesh.Indices is uint[] (32-bit); see VorticeRenderer.cs for the empirical
        // "bowtie geometry" pitfall this avoids.
        IndexBufferView IndexBufferView=new(IndexBuffer.GPUVirtualAddress,IndexBufferSize,true);

        return new ObjectResources {
            NodeId=RenderObject.NodeId,
            World=RenderObject.World,
            VertexBuffer=VertexBuffer,
            VertexBufferView=VertexBufferView,
            IndexBuffer=IndexBuffer,
            IndexBufferView=IndexBufferView,
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
        for(var I=0;I<Data.Length;I++){
            MemoryMarshal.Write(Mapped.Slice(I*ElementSize,ElementSize),ref Data[I]);
        }
        Resource.Unmap(0,null);
        return Resource;
    }

    private ID3D12Resource CreateDepthBuffer(int W,int H){
        ResourceDescription Description=ResourceDescription.Texture2D(DepthBufferFormat,(uint)W,(uint)H,1,1);
        Description.Flags=ResourceFlags.AllowDepthStencil;

        ID3D12Resource Buffer=Device.CreateCommittedResource(
            HeapType.Default,
            Description,
            ResourceStates.DepthWrite,
            new ClearValue(DepthBufferFormat,1.0f,0));

        Device.CreateDepthStencilView(Buffer,null,DsvHeap.GetCPUDescriptorHandleForHeapStart());
        return Buffer;
    }

    private void CreateRenderTargetViews(){
        CpuDescriptorHandle Handle=RtvHeap.GetCPUDescriptorHandleForHeapStart();
        for(var I=0;I<FrameCount;I++){
            RenderTargets[I]=SwapChain.GetBuffer<ID3D12Resource>(I);
            Device.CreateRenderTargetView(RenderTargets[I],null,Handle);
            Handle+=RtvDescriptorSize;
        }
    }

    private void UpdateAllConstantBuffers(){
        if(Objects.Count==0){
            // 0件では Map/Unmap 自体を行わない(TotalSize=0 の Map は不正)。
            return;
        }

        var TotalSize=AlignedSlotSize*Objects.Count;
        Span<byte>Mapped=ConstantBuffer.Map<byte>(0,TotalSize);
        for(var I=0;I<Objects.Count;I++){
            ObjectResources Obj=Objects[I];
            Matrix4x4 Mvp=Obj.World*CurrentView*CurrentProjection;
            float Selected=SceneSelectionTint.GetWeight(Obj.NodeId,SelectedNodeId,SelectedNodeIds);
            TransformConstants Constants=new(){ Mvp=Mvp,BaseColorFactor=Obj.BaseColorFactor,Selected=Selected };
            MemoryMarshal.Write(Mapped.Slice(I*AlignedSlotSize,Marshal.SizeOf<TransformConstants>()),ref Constants);
        }
        ConstantBuffer.Unmap(0,null);
    }

    /// <summary>
    /// ヒエラルキー/ビューポートの選択が変わったときに呼ばれる(<see cref="SceneHwndHost.SetSelection"/>経由)。
    /// <paramref name="NodeId"/>が primary、<paramref name="NodeIds"/>が複数選択の全体
    /// (primaryを含む)。強さの決定は<see cref="SceneSelectionTint"/>。
    /// </summary>
    public void SetSelectedNode(NodePath?NodeId,IReadOnlyCollection<NodePath>?NodeIds=null){
        SelectedNodeId=NodeId;
        SelectedNodeIds=NodeIds;
        ConstantsDirty=true;
    }

    public void Resize(int NewWidth,int NewHeight){
        NewWidth=Math.Max(1,NewWidth);
        NewHeight=Math.Max(1,NewHeight);
        if(NewWidth==Width&&NewHeight==Height){
            return;
        }

        WaitForGpuIdle();

        for(var I=0;I<FrameCount;I++){
            RenderTargets[I].Dispose();
        }
        DepthBuffer.Dispose();

        Width=NewWidth;
        Height=NewHeight;
        SwapChain.ResizeBuffers(FrameCount,Width,Height,BackBufferFormat,SwapChainFlags.None);
        CreateRenderTargetViews();
        DepthBuffer=CreateDepthBuffer(Width,Height);

        // アスペクト比に追従した projection は所有者(SceneHwndHost)が SetCamera で入れ直す。
    }

    public void RenderFrame(){
        if(ConstantsDirty){
            // 現状は RenderFrame 末尾の WaitForGpuIdle により、ここでの上書きが前フレームの GPU 読み取りと
            // 競合しない。M0020 Phase 7 で WaitForGpuIdle を外してフレームインフライト化する際は、
            // 定数バッファを FrameCount 分に多重化してからこの書き込みを行う必要がある。
            UpdateAllConstantBuffers();
            ConstantsDirty=false;
        }

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

        for(var I=0;I<Objects.Count;I++){
            ObjectResources Obj=Objects[I];
            CommandList.SetGraphicsRootConstantBufferView(0,ConstantBuffer.GPUVirtualAddress+(ulong)(I*AlignedSlotSize));
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
        for(var I=0;I<FrameCount;I++){
            RenderTargets[I].Dispose();
        }
        RtvHeap.Dispose();
        SwapChain.Dispose();
        CommandQueue.Dispose();
        Fence.Dispose();
        NativeEvent.Close(FenceEvent);
        Device.Dispose();
    }
}
