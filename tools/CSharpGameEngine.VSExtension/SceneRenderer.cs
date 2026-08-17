using System;
using System.Collections.Generic;
using System.Linq;
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
/// (ADR-0041 decision 1), because mesh/structural changes can dispose and rebuild the renderer.
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
    private readonly ID3D12CommandAllocator[]CommandAllocators=new ID3D12CommandAllocator[FrameCount];
    private readonly ID3D12GraphicsCommandList CommandList;
    private readonly ID3D12RootSignature RootSignature;
    private readonly ID3D12PipelineState PipelineState;
    private readonly ID3D12PipelineState OverlayTrianglePipelineState;
    private readonly ID3D12PipelineState OverlayLinePipelineState;
    private readonly ID3D12Fence Fence;
    private readonly FrameResourceTracker FrameResources=new(FrameCount);
    private readonly IntPtr FenceEvent;

    private readonly ID3D12Resource ConstantBuffer;
    private readonly int AlignedSlotSize;
    private readonly int ObjectCapacity;
    // M0037: internal化してSceneHwndHost.RefreshOverlayがHUD追加前に予算超過を防げるようにする。
    internal const int MaxOverlayVertices=4096;
    private readonly ID3D12Resource OverlayVertexBuffer;
    private readonly ID3D12Resource OverlayConstantBuffer;
    private readonly VertexBufferView[]OverlayTriangleVertexViews=new VertexBufferView[FrameCount];
    private readonly VertexBufferView[]OverlayLineVertexViews=new VertexBufferView[FrameCount];
    private OverlayVertex[]OverlayTriangles=[];
    private OverlayVertex[]OverlayLines=[];
    private int OverlayTriangleVertexCount;
    private int OverlayLineVertexCount;
    private readonly bool[]OverlayGeometryDirty=new bool[FrameCount];
    private readonly bool[]OverlayConstantsDirty=new bool[FrameCount];

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
    private readonly bool[]ConstantsDirty=new bool[FrameCount];

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

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayVertex {
        public Vector3 Position;
        public Vector4 Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayConstants {
        public Matrix4x4 ViewProjection;
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

    private const string OverlayShaderSource="""
        cbuffer OverlayCB : register(b0)
        {
            row_major float4x4 ViewProjection;
        };

        struct VertexInput { float3 Position : POSITION; float4 Color : COLOR; };
        struct VertexOutput { float4 Position : SV_Position; float4 Color : COLOR; };

        VertexOutput VSMain(VertexInput input)
        {
            VertexOutput output;
            output.Position = mul(float4(input.Position, 1.0), ViewProjection);
            output.Color = input.Color;
            return output;
        }

        float4 PSMain(VertexOutput input) : SV_Target
        {
            return input.Color;
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

        for(var FrameIndex=0;FrameIndex<FrameCount;FrameIndex++){
            CommandAllocators[FrameIndex]=Device.CreateCommandAllocator(CommandListType.Direct);
        }
        CommandList=Device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct,CommandAllocators[0]);
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

        Result OverlayVsResult=Compiler.Compile(
            OverlayShaderSource,"VSMain","SceneRendererOverlay","vs_5_0",
            out Blob?OverlayVertexShaderBlob,out Blob?OverlayVsErrorBlob);
        if(OverlayVsResult.Failure||OverlayVertexShaderBlob is null){
            throw new InvalidOperationException($"Overlay vertex shader compilation failed: {OverlayVsErrorBlob?.AsString()}");
        }
        Result OverlayPsResult=Compiler.Compile(
            OverlayShaderSource,"PSMain","SceneRendererOverlay","ps_5_0",
            out Blob?OverlayPixelShaderBlob,out Blob?OverlayPsErrorBlob);
        if(OverlayPsResult.Failure||OverlayPixelShaderBlob is null){
            OverlayVertexShaderBlob.Dispose();
            throw new InvalidOperationException($"Overlay pixel shader compilation failed: {OverlayPsErrorBlob?.AsString()}");
        }

        using Blob OverlayVertexShaderBlobOwned=OverlayVertexShaderBlob;
        using Blob OverlayPixelShaderBlobOwned=OverlayPixelShaderBlob;
        OverlayVsErrorBlob?.Dispose();
        OverlayPsErrorBlob?.Dispose();
        InputElementDescription[]OverlayInputElements=[
            new InputElementDescription("POSITION",0,Format.R32G32B32_Float,0,0),
            new InputElementDescription("COLOR",0,Format.R32G32B32A32_Float,12,0),
        ];
        GraphicsPipelineStateDescription OverlayPsoDescription=new(){
            RootSignature=RootSignature,
            VertexShader=OverlayVertexShaderBlobOwned.AsBytes(),
            PixelShader=OverlayPixelShaderBlobOwned.AsBytes(),
            InputLayout=new InputLayoutDescription(OverlayInputElements),
            SampleMask=uint.MaxValue,
            PrimitiveTopologyType=PrimitiveTopologyType.Triangle,
            RasterizerState=RasterizerDescription.CullNone,
            BlendState=BlendDescription.AlphaBlend,
            DepthStencilState=DepthStencilDescription.None,
            DepthStencilFormat=DepthBufferFormat,
            RenderTargetFormats=[BackBufferFormat],
            SampleDescription=new SampleDescription(1,0),
        };
        OverlayTrianglePipelineState=Device.CreateGraphicsPipelineState(OverlayPsoDescription);
        OverlayPsoDescription.PrimitiveTopologyType=PrimitiveTopologyType.Line;
        OverlayLinePipelineState=Device.CreateGraphicsPipelineState(OverlayPsoDescription);

        AlignedSlotSize=(Marshal.SizeOf<TransformConstants>()+255)&~255;
        ObjectCapacity=Math.Max(1,RenderObjects.Count*2);
        ConstantBuffer=Device.CreateCommittedResource(
            HeapType.Upload,
            new ResourceDescription {
                Dimension=ResourceDimension.Buffer,
                // D3D12 は 0 バイトの buffer を許可しないので、0件でも1スロット分は確保する。
                Width=(ulong)(AlignedSlotSize*ObjectCapacity*FrameCount),
                Height=1,
                DepthOrArraySize=1,
                MipLevels=1,
                SampleDescription=new SampleDescription(1,0),
                Layout=TextureLayout.RowMajor,
                Format=Format.Unknown,
            },
            ResourceStates.GenericRead);

        int OverlayVertexSize=Marshal.SizeOf<OverlayVertex>();
        OverlayVertexBuffer=Device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer((ulong)(OverlayVertexSize*MaxOverlayVertices*FrameCount)),
            ResourceStates.GenericRead);
        OverlayConstantBuffer=Device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer(256*FrameCount),
            ResourceStates.GenericRead);

        foreach(SceneRenderObject RenderObject in RenderObjects){
            Objects.Add(CreateObjectResources(RenderObject));
        }

        Fence=Device.CreateFence(0);
        FenceEvent=NativeEvent.CreateAutoResetEvent();

        MarkAllDirty(ConstantsDirty);
        MarkAllDirty(OverlayGeometryDirty);
        MarkAllDirty(OverlayConstantsDirty);
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
        MarkAllDirty(ConstantsDirty);
        MarkAllDirty(OverlayConstantsDirty);
    }

    /// <summary>ドラッグpreviewではGPU meshを再構築せず、既存objectのworld定数だけを更新する。</summary>
    public bool UpdateObjectTransform(NodePath NodeId,in Matrix4x4 World){
        ObjectResources?Object=Objects.FirstOrDefault(Candidate=>Candidate.NodeId.Equals(NodeId));
        if(Object is null){
            return false;
        }
        Object.World=World;
        MarkAllDirty(ConstantsDirty);
        return true;
    }

    /// <summary>Alt+dragの複製previewを、再解析を待たず一時描画オブジェクトとして末尾へ追加する。</summary>
    public bool TryAddPreviewObjects(
        IReadOnlyList<SceneRenderObject>RenderObjects,out IReadOnlyList<int>Handles){
        if(RenderObjects.Count==0){
            Handles=Array.Empty<int>();
            return true;
        }
        if(RenderObjects.Count>ObjectCapacity-Objects.Count){
            Handles=Array.Empty<int>();
            return false;
        }

        int Start=Objects.Count;
        try {
            foreach(SceneRenderObject RenderObject in RenderObjects){
                Objects.Add(CreateObjectResources(RenderObject));
            }
        } catch {
            while(Objects.Count>Start){
                DisposeObjectResources(Objects[^1]);
                Objects.RemoveAt(Objects.Count-1);
            }
            throw;
        }

        Handles=Enumerable.Range(Start,RenderObjects.Count).ToArray();
        MarkAllDirty(ConstantsDirty);
        return true;
    }

    /// <summary>NodePathが同じ元オブジェクトと区別して、一時previewだけを更新する。</summary>
    public bool UpdatePreviewObjectTransform(int Handle,in Matrix4x4 World){
        if(Handle<0||Handle>=Objects.Count){
            return false;
        }
        Objects[Handle].World=World;
        MarkAllDirty(ConstantsDirty);
        return true;
    }

    /// <summary>一時previewとして末尾へ追加した連続オブジェクトをGPU同期後に破棄する。</summary>
    public void RemovePreviewObjects(IReadOnlyList<int>Handles){
        if(Handles.Count==0){ return; }
        int Start=Objects.Count-Handles.Count;
        for(var I=0;I<Handles.Count;I++){
            if(Handles[I]!=Start+I){
                throw new ArgumentException(nameof(Handles));
            }
        }

        WaitForGpuIdle();
        for(var I=Objects.Count-1;I>=Start;I--){
            DisposeObjectResources(Objects[I]);
            Objects.RemoveAt(I);
        }
        MarkAllDirty(ConstantsDirty);
    }

    /// <summary>ギズモoverlayを固定容量upload bufferへ置き換える。GPU resource自体は再生成しない。</summary>
    public void SetOverlay(IReadOnlyList<GizmoOverlayPrimitive>?Primitives){
        List<OverlayVertex>Triangles=[];
        List<OverlayVertex>Lines=[];
        if(Primitives is not null){
            foreach(GizmoOverlayPrimitive Primitive in Primitives){
                List<OverlayVertex>Target=Primitive.Kind==GizmoPrimitiveKind.Triangle?Triangles:Lines;
                foreach(GizmoOverlayVertex Vertex in Primitive.Vertices){
                    Target.Add(new OverlayVertex { Position=Vertex.Position,Color=Vertex.Color });
                }
            }
        }

        int TotalCount=Triangles.Count+Lines.Count;
        if(TotalCount>MaxOverlayVertices){
            throw new ArgumentOutOfRangeException(nameof(Primitives),$"overlay頂点数{TotalCount}が上限{MaxOverlayVertices}を超えています。");
        }

        OverlayTriangles=Triangles.ToArray();
        OverlayLines=Lines.ToArray();
        OverlayTriangleVertexCount=Triangles.Count;
        OverlayLineVertexCount=Lines.Count;
        MarkAllDirty(OverlayGeometryDirty);
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

    private static void DisposeObjectResources(ObjectResources Object){
        Object.IndexBuffer.Dispose();
        Object.VertexBuffer.Dispose();
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

    private void UpdateAllConstantBuffers(int FrameIndex){
        if(Objects.Count==0){
            // 0件では Map/Unmap 自体を行わない(TotalSize=0 の Map は不正)。
            return;
        }

        int FrameOffset=FrameIndex*ObjectCapacity*AlignedSlotSize;
        int TotalSize=AlignedSlotSize*Objects.Count;
        Span<byte>Mapped=ConstantBuffer.Map<byte>(0,FrameOffset+TotalSize);
        for(var I=0;I<Objects.Count;I++){
            ObjectResources Obj=Objects[I];
            Matrix4x4 Mvp=Obj.World*CurrentView*CurrentProjection;
            float Selected=SceneSelectionTint.GetWeight(Obj.NodeId,SelectedNodeId,SelectedNodeIds);
            TransformConstants Constants=new(){ Mvp=Mvp,BaseColorFactor=Obj.BaseColorFactor,Selected=Selected };
            MemoryMarshal.Write(
                Mapped.Slice(FrameOffset+(I*AlignedSlotSize),Marshal.SizeOf<TransformConstants>()),
                ref Constants);
        }
        ConstantBuffer.Unmap(0,null);
    }

    private void UpdateOverlayConstantBuffer(int FrameIndex){
        OverlayConstants Constants=new(){ ViewProjection=CurrentView*CurrentProjection };
        int FrameOffset=FrameIndex*256;
        Span<byte>Mapped=OverlayConstantBuffer.Map<byte>(
            0,FrameOffset+Marshal.SizeOf<OverlayConstants>());
        MemoryMarshal.Write(
            Mapped.Slice(FrameOffset,Marshal.SizeOf<OverlayConstants>()),ref Constants);
        OverlayConstantBuffer.Unmap(0,null);
    }

    private void UpdateOverlayVertexBuffer(int FrameIndex){
        int VertexSize=Marshal.SizeOf<OverlayVertex>();
        int FrameOffset=FrameIndex*MaxOverlayVertices*VertexSize;
        int TotalCount=OverlayTriangleVertexCount+OverlayLineVertexCount;
        if(TotalCount>0){
            Span<byte>Mapped=OverlayVertexBuffer.Map<byte>(
                0,FrameOffset+(VertexSize*TotalCount));
            int Offset=FrameOffset;
            foreach(OverlayVertex Vertex in OverlayTriangles){
                OverlayVertex Value=Vertex;
                MemoryMarshal.Write(Mapped.Slice(Offset,VertexSize),ref Value);
                Offset+=VertexSize;
            }
            foreach(OverlayVertex Vertex in OverlayLines){
                OverlayVertex Value=Vertex;
                MemoryMarshal.Write(Mapped.Slice(Offset,VertexSize),ref Value);
                Offset+=VertexSize;
            }
            OverlayVertexBuffer.Unmap(0,null);
        }

        ulong FrameAddress=OverlayVertexBuffer.GPUVirtualAddress+(ulong)FrameOffset;
        OverlayTriangleVertexViews[FrameIndex]=new VertexBufferView(
            FrameAddress,OverlayTriangleVertexCount*VertexSize,VertexSize);
        OverlayLineVertexViews[FrameIndex]=new VertexBufferView(
            FrameAddress+(ulong)(OverlayTriangleVertexCount*VertexSize),
            OverlayLineVertexCount*VertexSize,VertexSize);
    }

    /// <summary>
    /// ヒエラルキー/ビューポートの選択が変わったときに呼ばれる(<see cref="SceneHwndHost.SetSelection"/>経由)。
    /// <paramref name="NodeId"/>が primary、<paramref name="NodeIds"/>が複数選択の全体
    /// (primaryを含む)。強さの決定は<see cref="SceneSelectionTint"/>。
    /// </summary>
    public void SetSelectedNode(NodePath?NodeId,IReadOnlyCollection<NodePath>?NodeIds=null){
        SelectedNodeId=NodeId;
        SelectedNodeIds=NodeIds;
        MarkAllDirty(ConstantsDirty);
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
        int FrameIndex=(int)SwapChain.CurrentBackBufferIndex;
        WaitForFrameSlot(FrameIndex);
        if(ConstantsDirty[FrameIndex]){
            UpdateAllConstantBuffers(FrameIndex);
            ConstantsDirty[FrameIndex]=false;
        }
        if(OverlayConstantsDirty[FrameIndex]){
            UpdateOverlayConstantBuffer(FrameIndex);
            OverlayConstantsDirty[FrameIndex]=false;
        }
        if(OverlayGeometryDirty[FrameIndex]){
            UpdateOverlayVertexBuffer(FrameIndex);
            OverlayGeometryDirty[FrameIndex]=false;
        }

        ID3D12CommandAllocator CommandAllocator=CommandAllocators[FrameIndex];
        CommandAllocator.Reset();
        CommandList.Reset(CommandAllocator,PipelineState);

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
            int ConstantOffset=((FrameIndex*ObjectCapacity)+I)*AlignedSlotSize;
            CommandList.SetGraphicsRootConstantBufferView(
                0,ConstantBuffer.GPUVirtualAddress+(ulong)ConstantOffset);
            CommandList.IASetVertexBuffers(0,Obj.VertexBufferView);
            CommandList.IASetIndexBuffer(Obj.IndexBufferView);
            CommandList.DrawIndexedInstanced(Obj.IndexCount,1,0,0,0);
        }

        if(OverlayTriangleVertexCount>0){
            CommandList.SetPipelineState(OverlayTrianglePipelineState);
            CommandList.SetGraphicsRootConstantBufferView(
                0,OverlayConstantBuffer.GPUVirtualAddress+(ulong)(FrameIndex*256));
            CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            CommandList.IASetVertexBuffers(0,OverlayTriangleVertexViews[FrameIndex]);
            CommandList.DrawInstanced(OverlayTriangleVertexCount,1,0,0);
        }
        if(OverlayLineVertexCount>0){
            CommandList.SetPipelineState(OverlayLinePipelineState);
            CommandList.SetGraphicsRootConstantBufferView(
                0,OverlayConstantBuffer.GPUVirtualAddress+(ulong)(FrameIndex*256));
            CommandList.IASetPrimitiveTopology(PrimitiveTopology.LineList);
            CommandList.IASetVertexBuffers(0,OverlayLineVertexViews[FrameIndex]);
            CommandList.DrawInstanced(OverlayLineVertexCount,1,0,0);
        }

        CommandList.ResourceBarrierTransition(RenderTargets[FrameIndex],ResourceStates.RenderTarget,ResourceStates.Present);
        CommandList.Close();

        CommandQueue.ExecuteCommandList(CommandList);
        SwapChain.Present(1,PresentFlags.None);

        SignalFrameSlot(FrameIndex);
    }

    private void WaitForFrameSlot(int FrameIndex){
        ulong RequiredValue=FrameResources.GetRequiredFenceValue(FrameIndex);
        if(RequiredValue==0||Fence.CompletedValue>=RequiredValue){ return; }
        Fence.SetEventOnCompletion(RequiredValue,FenceEvent);
        NativeEvent.Wait(FenceEvent);
    }

    private void SignalFrameSlot(int FrameIndex){
        ulong ValueToSignal=FrameResources.MarkSubmitted(FrameIndex);
        CommandQueue.Signal(Fence,ValueToSignal);
    }

    private void WaitForGpuIdle(){
        ulong ValueToSignal=FrameResources.MarkGpuIdle();
        CommandQueue.Signal(Fence,ValueToSignal);
        if(Fence.CompletedValue<ValueToSignal){
            Fence.SetEventOnCompletion(ValueToSignal,FenceEvent);
            NativeEvent.Wait(FenceEvent);
        }
    }

    public void Dispose(){
        WaitForGpuIdle();

        ConstantBuffer.Dispose();
        OverlayConstantBuffer.Dispose();
        OverlayVertexBuffer.Dispose();
        foreach(ObjectResources Obj in Objects){
            DisposeObjectResources(Obj);
        }
        PipelineState.Dispose();
        OverlayLinePipelineState.Dispose();
        OverlayTrianglePipelineState.Dispose();
        RootSignature.Dispose();
        CommandList.Dispose();
        foreach(ID3D12CommandAllocator CommandAllocator in CommandAllocators){
            CommandAllocator.Dispose();
        }
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

    private static void MarkAllDirty(bool[]Dirty){
        for(var FrameIndex=0;FrameIndex<Dirty.Length;FrameIndex++){
            Dirty[FrameIndex]=true;
        }
    }
}
