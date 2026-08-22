using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using Vortice;
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

    /// <summary>
    /// M0042: 描画はするがクリック・矩形選択・頂点/面スナップの対象から外す場合はfalse。
    /// メッシュ未宣言ノード(Root・整理用の空グループノード、<see cref="GizmoMeshFactory"/>の
    /// プレースホルダー立方体で代替描画される)とSky domeがこれに当たる——前者はワールド原点付近に
    /// 描画されがちでステージの実オブジェクトへ紛れ込み誤選択しやすく、後者は「何もない空間」の
    /// クリックが背景に当たって選択解除にならない不具合の原因だった。Hierarchyパネル経由の選択は
    /// <see cref="ScenePicker"/>を経由しないためこのフラグの影響を受けない(意図的)。
    /// </summary>
    public bool IsPickable { get; init;}=true;
}

/// <summary>
/// A camera view rendered into a rectangle of the Scene Designer swap chain.
/// The rectangle is expressed in surface pixels with an origin at the upper-left corner.
/// Invalid or partially out-of-bounds rectangles are clipped by <see cref=SceneRenderer/>
/// before they reach D3D12.
/// </summary>
internal readonly struct SceneCameraPreviewPass {
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public Matrix4x4 View { get; }
    public Matrix4x4 Projection { get; }
    /// <summary>
    /// このカードの視点になっているノード。メッシュ未宣言ノードはGizmoMeshFactoryの
    /// プレースホルダー立方体で代替描画されるため、カメラ自身のノードをこのカードで
    /// 描くと視点が立方体の内側に入り込み画面全体を塗り潰す。DrawScenePassはこのIDと
    /// 一致するオブジェクトをこのパスの描画から除外する。
    /// </summary>
    public NodePath CameraNodeId { get; }

    internal SceneCameraPreviewPass(
        int X,int Y,int Width,int Height,in Matrix4x4 View,in Matrix4x4 Projection,NodePath CameraNodeId){
        this.X=X;
        this.Y=Y;
        this.Width=Width;
        this.Height=Height;
        this.View=View;
        this.Projection=Projection;
        this.CameraNodeId=CameraNodeId;
    }
}

/// <summary>Per-card rendering facts used to distinguish an empty frustum from a failed pass.</summary>
internal readonly struct SceneCameraPreviewDiagnostics {
    internal SceneCameraPreviewDiagnostics(int SceneObjectCount,int FrustumCandidateCount,int DrawCallCount,uint ViewProjectionHash){
        this.SceneObjectCount=SceneObjectCount;
        this.FrustumCandidateCount=FrustumCandidateCount;
        this.DrawCallCount=DrawCallCount;
        this.ViewProjectionHash=ViewProjectionHash;
    }

    internal int SceneObjectCount { get; }
    internal int FrustumCandidateCount { get; }
    internal int DrawCallCount { get; }
    internal uint ViewProjectionHash { get; }

    internal static uint ComputeViewProjectionHash(in Matrix4x4 View,in Matrix4x4 Projection){
        Matrix4x4 Value=View*Projection;
        unchecked{
            uint Hash=2166136261;
            Hash=(Hash^(uint)Value.M11.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M12.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M13.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M14.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M21.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M22.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M23.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M24.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M31.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M32.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M33.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M34.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M41.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M42.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M43.GetHashCode())*16777619;
            return(Hash^(uint)Value.M44.GetHashCode())*16777619;
        }
    }
}

/// <summary>
/// 定数バッファの単一フレーム分は<c>ObjectCapacity(=Objects.Count*2、成長余地込み)×CameraPassCount</c>
/// スロット。<see cref="SceneRenderer"/>はメインビューポート(pass 0)とカメラプレビュー最大
/// <see cref="SceneRenderer.MaxCameraPreviews"/>枚(pass 1..N)を1本のバッファへ並べて書くため、
/// Map長・書き込みオフセットのどちらもこの<c>ObjectCapacity</c>基準でなければならない
/// (<c>Objects.Count</c>基準にすると、カメラプレビューが2枚以上あるとき範囲外書き込みになる)。
/// GPUデバイスを必要としない純粋な算術なのでユニットテストできる。
/// </summary>
internal static class SceneConstantBufferLayout {
    internal static int ComputeAllocatedSize(int AlignedSlotSize,int ObjectCapacity,int CameraPassCount,int FrameCount)=>
        AlignedSlotSize*ObjectCapacity*CameraPassCount*FrameCount;

    internal static int ComputeFrameOffset(int FrameIndex,int AlignedSlotSize,int ObjectCapacity,int CameraPassCount)=>
        FrameIndex*ObjectCapacity*CameraPassCount*AlignedSlotSize;

    internal static int ComputeMapLength(int AlignedSlotSize,int ObjectCapacity,int CameraPassCount)=>
        AlignedSlotSize*ObjectCapacity*CameraPassCount;

    internal static int ComputeWriteOffset(int FrameOffset,int Pass,int ObjectIndex,int AlignedSlotSize,int ObjectCapacity)=>
        FrameOffset+(Pass*ObjectCapacity*AlignedSlotSize)+(ObjectIndex*AlignedSlotSize);
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
    /// <summary>Maximum number of camera thumbnails composed over the main Scene View.</summary>
    public const int MaxCameraPreviews=3;
    private const int CameraPassCount=1+MaxCameraPreviews;
    private static readonly Format BackBufferFormat=Format.R8G8B8A8_UNorm;
    private static readonly Format DepthBufferFormat=Format.D32_Float;

    private readonly ID3D12Device Device;
    private readonly ID3D12CommandQueue CommandQueue;
    private readonly IDXGISwapChain3 SwapChain;
    private readonly ID3D12DescriptorHeap RtvHeap;
    private readonly int RtvDescriptorSize;
    private readonly ID3D12Resource[]RenderTargets=new ID3D12Resource[FrameCount];
    private readonly ID3D12DescriptorHeap DsvHeap;
    private readonly int DsvDescriptorSize;
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

    private sealed class PreviewRenderTarget:IDisposable {
        public required int Width;
        public required int Height;
        public required ID3D12Resource Color;
        public required ID3D12Resource Depth;
        public required CpuDescriptorHandle Rtv;
        public required CpuDescriptorHandle Dsv;

        public void Dispose(){
            this.Depth.Dispose();
            this.Color.Dispose();
        }
    }

    private readonly List<ObjectResources>Objects=[];
    private NodePath?SelectedNodeId;
    private IReadOnlyCollection<NodePath>?SelectedNodeIds;

    private Matrix4x4 CurrentView;
    private Matrix4x4 CurrentProjection;
    private readonly SceneCameraPreviewPass[]CameraPreviews=new SceneCameraPreviewPass[MaxCameraPreviews];
    // Every card owns colour and depth state, so cards cannot inherit the Scene View's depth,
    // viewport or scissor state.
    private readonly PreviewRenderTarget?[]CameraPreviewTargets=new PreviewRenderTarget?[MaxCameraPreviews];
    private readonly SceneCameraPreviewDiagnostics[]CameraPreviewDiagnostics=new SceneCameraPreviewDiagnostics[MaxCameraPreviews];
    private int CameraPreviewCount;

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

        using var Factory=DXGI.CreateDXGIFactory1<IDXGIFactory4>();

        IDXGIAdapter1?ChosenAdapter=null;
        for(var I=0;Factory.EnumAdapters1(I,out var Adapter).Success;I++){
            if((Adapter.Description1.Flags&AdapterFlags.Software)!=0){
                Adapter.Dispose();
                continue;
            }

            ChosenAdapter=Adapter;
            break;
        }

        this.Device=ChosenAdapter is not null
                        ?D3D12.D3D12CreateDevice<ID3D12Device>(ChosenAdapter,FeatureLevel.Level_11_0)
                        :D3D12.D3D12CreateDevice<ID3D12Device>(null,FeatureLevel.Level_11_0);
        ChosenAdapter?.Dispose();

        this.CommandQueue= this.Device.CreateCommandQueue(CommandListType.Direct);

        SwapChainDescription1 SwapChainDescription=new(){
            Width=this.Width,
            Height=this.Height,
            Format=BackBufferFormat,
            BufferUsage=Usage.RenderTargetOutput,
            BufferCount=FrameCount,
            SampleDescription=new SampleDescription(1,0),
            SwapEffect=SwapEffect.FlipDiscard,
        };

        using var TempSwapChain=Factory.CreateSwapChainForHwnd(this.CommandQueue,Hwnd,SwapChainDescription);
        this.SwapChain=TempSwapChain.QueryInterface<IDXGISwapChain3>();

        this.RtvHeap= this.Device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.RenderTargetView,FrameCount+MaxCameraPreviews));
        this.RtvDescriptorSize= this.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        this.CreateRenderTargetViews();

        this.DsvHeap= this.Device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.DepthStencilView,1+MaxCameraPreviews));
        this.DsvDescriptorSize= this.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
        this.DepthBuffer= this.CreateDepthBuffer(this.Width,this.Height,this.DsvHeap.GetCPUDescriptorHandleForHeapStart());

        for(var FrameIndex=0;FrameIndex<FrameCount;FrameIndex++){
            this.CommandAllocators[FrameIndex]= this.Device.CreateCommandAllocator(CommandListType.Direct);
        }

        this.CommandList= this.Device.CreateCommandList<ID3D12GraphicsCommandList>(
                                                                                CommandListType.Direct, this.CommandAllocators[0]);
        this.CommandList.Close();

        RootParameter1 TransformParameter=new(RootParameterType.ConstantBufferView,new RootDescriptor1(0,0),ShaderVisibility.All);
        RootSignatureDescription1 RootSignatureDescription=new(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [TransformParameter]);
        this.RootSignature= this.Device.CreateRootSignature(0,new VersionedRootSignatureDescription(RootSignatureDescription));

        var VsCompileResult=Compiler.Compile(ShaderSource,"VSMain","SceneRenderer","vs_5_0",out var VertexShaderBlob,out var VsErrorBlob);
        if(VsCompileResult.Failure||VertexShaderBlob is null){
            throw new InvalidOperationException($"Vertex shader compilation failed: {VsErrorBlob?.AsString()}");
        }

        var PsCompileResult=Compiler.Compile(ShaderSource,"PSMain","SceneRenderer","ps_5_0",out var PixelShaderBlob,out var PsErrorBlob);
        if(PsCompileResult.Failure||PixelShaderBlob is null){
            throw new InvalidOperationException($"Pixel shader compilation failed: {PsErrorBlob?.AsString()}");
        }

        using var VertexShaderBlobOwned=VertexShaderBlob;
        using var PixelShaderBlobOwned=PixelShaderBlob;
        VsErrorBlob?.Dispose();
        PsErrorBlob?.Dispose();

        InputElementDescription[]InputElements=[
            new InputElementDescription("POSITION",0,Format.R32G32B32_Float,0,0),
            new InputElementDescription("NORMAL",0,Format.R32G32B32_Float,12,0),
        ];

        GraphicsPipelineStateDescription PsoDescription=new(){
            RootSignature= this.RootSignature,
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
        this.PipelineState= this.Device.CreateGraphicsPipelineState(PsoDescription);

        var OverlayVsResult=Compiler.Compile(
                                             OverlayShaderSource,"VSMain","SceneRendererOverlay","vs_5_0",
                                             out var OverlayVertexShaderBlob,out var OverlayVsErrorBlob);
        if(OverlayVsResult.Failure||OverlayVertexShaderBlob is null){
            throw new InvalidOperationException($"Overlay vertex shader compilation failed: {OverlayVsErrorBlob?.AsString()}");
        }
        var OverlayPsResult=Compiler.Compile(
                                             OverlayShaderSource,"PSMain","SceneRendererOverlay","ps_5_0",
                                             out var OverlayPixelShaderBlob,out var OverlayPsErrorBlob);
        if(OverlayPsResult.Failure||OverlayPixelShaderBlob is null){
            OverlayVertexShaderBlob.Dispose();
            throw new InvalidOperationException($"Overlay pixel shader compilation failed: {OverlayPsErrorBlob?.AsString()}");
        }

        using var OverlayVertexShaderBlobOwned=OverlayVertexShaderBlob;
        using var OverlayPixelShaderBlobOwned=OverlayPixelShaderBlob;
        OverlayVsErrorBlob?.Dispose();
        OverlayPsErrorBlob?.Dispose();
        InputElementDescription[]OverlayInputElements=[
            new InputElementDescription("POSITION",0,Format.R32G32B32_Float,0,0),
            new InputElementDescription("COLOR",0,Format.R32G32B32A32_Float,12,0),
        ];
        GraphicsPipelineStateDescription OverlayPsoDescription=new(){
            RootSignature= this.RootSignature,
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
        this.OverlayTrianglePipelineState= this.Device.CreateGraphicsPipelineState(OverlayPsoDescription);
        OverlayPsoDescription.PrimitiveTopologyType=PrimitiveTopologyType.Line;
        this.OverlayLinePipelineState= this.Device.CreateGraphicsPipelineState(OverlayPsoDescription);

        this.AlignedSlotSize=(Marshal.SizeOf<TransformConstants>()+255)&~255;
        this.ObjectCapacity=Math.Max(1,RenderObjects.Count*2);
        this.ConstantBuffer= this.Device.CreateCommittedResource(
                                                                 HeapType.Upload,
                                                                 new ResourceDescription {
                                                                     Dimension=ResourceDimension.Buffer,
                                                                     // D3D12 は 0 バイトの buffer を許可しないので、0件でも1スロット分は確保する。
                                                                      Width=(ulong)SceneConstantBufferLayout.ComputeAllocatedSize(
                                                                          this.AlignedSlotSize, this.ObjectCapacity,CameraPassCount,FrameCount),
                                                                     Height=1,
                                                                     DepthOrArraySize=1,
                                                                     MipLevels=1,
                                                                     SampleDescription=new SampleDescription(1,0),
                                                                     Layout=TextureLayout.RowMajor,
                                                                     Format=Format.Unknown,
                                                                 },
                                                                 ResourceStates.GenericRead);

        var OverlayVertexSize=Marshal.SizeOf<OverlayVertex>();
        this.OverlayVertexBuffer= this.Device.CreateCommittedResource(
                                                                      HeapType.Upload,
                                                                      ResourceDescription.Buffer((ulong)(OverlayVertexSize*MaxOverlayVertices*FrameCount)),
                                                                      ResourceStates.GenericRead);
        this.OverlayConstantBuffer= this.Device.CreateCommittedResource(
                                                                        HeapType.Upload,
                                                                        ResourceDescription.Buffer(256*FrameCount),
                                                                        ResourceStates.GenericRead);

        foreach(var RenderObject in RenderObjects){
            this.Objects.Add(this.CreateObjectResources(RenderObject));
        }

        this.Fence= this.Device.CreateFence(0);
        this.FenceEvent=NativeEvent.CreateAutoResetEvent();

        MarkAllDirty(this.ConstantsDirty);
        MarkAllDirty(this.OverlayGeometryDirty);
        MarkAllDirty(this.OverlayConstantsDirty);
    }

    /// <summary>
    /// view/projection を外部(<see cref="SceneHwndHost"/>、M0020 Phase 1 以降は <c>SceneViewState</c>)
    /// から注入する。ADR-0041 決定事項1: レンダラーは編集ビュー状態を持たない。値が変わらなければ
    /// 何もしないので、毎フレーム呼んでも静止時のコストは行列比較2回だけ。
    /// </summary>
    public void SetCamera(in Matrix4x4 View,in Matrix4x4 Projection){
        if(this.CurrentView.Equals(View)&& this.CurrentProjection.Equals(Projection)){
            return;
        }

        this.CurrentView=View;
        this.CurrentProjection=Projection;
        MarkAllDirty(this.ConstantsDirty);
        MarkAllDirty(this.OverlayConstantsDirty);
    }

    /// <summary>
    /// Sets the camera thumbnails rendered after the primary Scene View. At most
    /// <see cref=MaxCameraPreviews/> entries are accepted; invalid rectangles are ignored.
    /// The input is copied so callers may reuse their layout buffer immediately.
    /// </summary>
    public void SetCameraPreviews(IReadOnlyList<SceneCameraPreviewPass>?Previews){
        var Count=0;
        if(Previews is not null){
            for(var I=0;I<Previews.Count&&Count<MaxCameraPreviews;I++){
                var Preview= this.ClipPreview(Previews[I]);
                if(Preview.Width<=0||Preview.Height<=0){
                    continue;
                }
                this.CameraPreviews[Count++]=Preview;
            }
        }

        // Layout changes are infrequent (selection/resize driven), so always invalidate
        // the frame slots. This also handles a caller replacing an entry with another
        // entry after invalid rectangles were filtered and packed.
        var Changed=true;
        this.CameraPreviewCount=Count;
        if(Changed){
            this.EnsurePreviewTargets();
            this.RefreshCameraPreviewDiagnostics();
            MarkAllDirty(this.ConstantsDirty);
        }
    }

    /// <summary>Removes all camera thumbnails from subsequent frames.</summary>
    public void ClearCameraPreviews(){
        if(this.CameraPreviewCount==0){
            return;
        }
        this.CameraPreviewCount=0;
        Array.Clear(this.CameraPreviewDiagnostics,0,this.CameraPreviewDiagnostics.Length);
        MarkAllDirty(this.ConstantsDirty);
    }

    /// <summary>Returns a snapshot of the effective camera preview layout for hit testing.</summary>
    public IReadOnlyList<SceneCameraPreviewPass> GetCameraPreviews(){
        var Snapshot=new SceneCameraPreviewPass[this.CameraPreviewCount];
        Array.Copy(this.CameraPreviews,Snapshot,this.CameraPreviewCount);
        return Snapshot;
    }

    /// <summary>Returns the last prepared per-card pass diagnostics for the Scene Designer status UI.</summary>
    public IReadOnlyList<SceneCameraPreviewDiagnostics> GetCameraPreviewDiagnostics(){
        var Snapshot=new SceneCameraPreviewDiagnostics[this.CameraPreviewCount];
        Array.Copy(this.CameraPreviewDiagnostics,Snapshot,this.CameraPreviewCount);
        return Snapshot;
    }

    private void RefreshCameraPreviewDiagnostics(){
        for(var I=0;I<this.CameraPreviewCount;I++){
            var Preview=this.CameraPreviews[I];
            var ViewProjection=Preview.View*Preview.Projection;
            var Candidates=0;
            var Draws=0;
            foreach(var Obj in this.Objects){
                if(CameraPreviewDrawPolicy.ShouldDraw(I+1,Obj.NodeId,Preview.CameraNodeId)){
                    Draws++;
                }
                var Position=Vector3.Transform(Vector3.Zero,Obj.World);
                var Clip=Vector4.Transform(new Vector4(Position,1f),ViewProjection);
                if(Clip.W>1e-5f&&Math.Abs(Clip.X)<=Clip.W&&Math.Abs(Clip.Y)<=Clip.W
                    &&Clip.Z>=0f&&Clip.Z<=Clip.W){
                    Candidates++;
                }
            }
            this.CameraPreviewDiagnostics[I]=new SceneCameraPreviewDiagnostics(
                this.Objects.Count,Candidates,Draws,ComputeMatrixHash(ViewProjection));
        }
    }

    private static uint ComputeMatrixHash(in Matrix4x4 Value){
        unchecked{
            uint Hash=2166136261;
            Hash=(Hash^(uint)Value.M11.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M12.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M13.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M14.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M21.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M22.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M23.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M24.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M31.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M32.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M33.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M34.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M41.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M42.GetHashCode())*16777619;
            Hash=(Hash^(uint)Value.M43.GetHashCode())*16777619;
            return (Hash^(uint)Value.M44.GetHashCode())*16777619;
        }
    }

    private SceneCameraPreviewPass ClipPreview(in SceneCameraPreviewPass Preview){
        var Left=Clamp(Preview.X,0,this.Width);
        var Top=Clamp(Preview.Y,0,this.Height);
        var Right=Clamp((long)Preview.X+Preview.Width,0L,this.Width);
        var Bottom=Clamp((long)Preview.Y+Preview.Height,0L,this.Height);
        return new SceneCameraPreviewPass(
            Left,Top,(int)Math.Max(0,Right-Left),(int)Math.Max(0,Bottom-Top),
            Preview.View,Preview.Projection,Preview.CameraNodeId);
    }

    // net472 does not provide Math.Clamp; keep clipping local to the renderer so that
    // negative coordinates and integer overflow cannot reach the native rasterizer.
    private static int Clamp(int Value,int Min,int Max)=>Math.Min(Math.Max(Value,Min),Max);
    private static long Clamp(long Value,long Min,long Max)=>Math.Min(Math.Max(Value,Min),Max);

    /// <summary>ドラッグpreviewではGPU meshを再構築せず、既存objectのworld定数だけを更新する。</summary>
    public bool UpdateObjectTransform(NodePath NodeId,in Matrix4x4 World){
        var Object= this.Objects.FirstOrDefault(Candidate=>Candidate.NodeId.Equals(NodeId));
        if(Object is null){
            return false;
        }
        Object.World=World;
        this.RefreshCameraPreviewDiagnostics();
        MarkAllDirty(this.ConstantsDirty);
        return true;
    }

    /// <summary>Alt+dragの複製previewを、再解析を待たず一時描画オブジェクトとして末尾へ追加する。</summary>
    public bool TryAddPreviewObjects(
        IReadOnlyList<SceneRenderObject>RenderObjects,out IReadOnlyList<int>Handles){
        if(RenderObjects.Count==0){
            Handles=Array.Empty<int>();
            return true;
        }
        if(RenderObjects.Count> this.ObjectCapacity- this.Objects.Count){
            Handles=Array.Empty<int>();
            return false;
        }

        var Start= this.Objects.Count;
        try {
            foreach(var RenderObject in RenderObjects){
                this.Objects.Add(this.CreateObjectResources(RenderObject));
            }
        } catch {
            while(this.Objects.Count>Start){
                DisposeObjectResources(this.Objects[^1]);
                this.Objects.RemoveAt(this.Objects.Count-1);
            }
            throw;
        }

        Handles=Enumerable.Range(Start,RenderObjects.Count).ToArray();
        this.RefreshCameraPreviewDiagnostics();
        MarkAllDirty(this.ConstantsDirty);
        return true;
    }

    /// <summary>NodePathが同じ元オブジェクトと区別して、一時previewだけを更新する。</summary>
    public bool UpdatePreviewObjectTransform(int Handle,in Matrix4x4 World){
        if(Handle<0||Handle>= this.Objects.Count){
            return false;
        }

        this.Objects[Handle].World=World;
        this.RefreshCameraPreviewDiagnostics();
        MarkAllDirty(this.ConstantsDirty);
        return true;
    }

    /// <summary>一時previewとして末尾へ追加した連続オブジェクトをGPU同期後に破棄する。</summary>
    public void RemovePreviewObjects(IReadOnlyList<int>Handles){
        if(Handles.Count==0){ return; }
        var Start= this.Objects.Count-Handles.Count;
        for(var I=0;I<Handles.Count;I++){
            if(Handles[I]!=Start+I){
                throw new ArgumentException(nameof(Handles));
            }
        }

        this.WaitForGpuIdle();
        for(var I= this.Objects.Count-1;I>=Start;I--){
            DisposeObjectResources(this.Objects[I]);
            this.Objects.RemoveAt(I);
        }
        this.RefreshCameraPreviewDiagnostics();
        MarkAllDirty(this.ConstantsDirty);
    }

    /// <summary>ギズモoverlayを固定容量upload bufferへ置き換える。GPU resource自体は再生成しない。</summary>
    public void SetOverlay(IReadOnlyList<GizmoOverlayPrimitive>?Primitives){
        List<OverlayVertex>Triangles=[];
        List<OverlayVertex>Lines=[];
        if(Primitives is not null){
            foreach(var Primitive in Primitives){
                var Target=Primitive.Kind==GizmoPrimitiveKind.Triangle?Triangles:Lines;
                foreach(var Vertex in Primitive.Vertices){
                    Target.Add(new OverlayVertex { Position=Vertex.Position,Color=Vertex.Color });
                }
            }
        }

        this.AddCameraPreviewOverlay(Lines);

        var TotalCount=Triangles.Count+Lines.Count;
        if(TotalCount>MaxOverlayVertices){
            throw new ArgumentOutOfRangeException(nameof(Primitives),$"overlay頂点数{TotalCount}が上限{MaxOverlayVertices}を超えています。");
        }

        this.OverlayTriangles=Triangles.ToArray();
        this.OverlayLines=Lines.ToArray();
        this.OverlayTriangleVertexCount=Triangles.Count;
        this.OverlayLineVertexCount=Lines.Count;
        MarkAllDirty(this.OverlayGeometryDirty);
    }

    private void AddCameraPreviewOverlay(List<OverlayVertex>Lines){
        if(this.CameraPreviewCount==0||this.Width<=0||this.Height<=0
            ||!Matrix4x4.Invert(this.CurrentView*this.CurrentProjection,out Matrix4x4 InverseViewProjection)){
            return;
        }

        for(var I=0;I<this.CameraPreviewCount;I++){
            SceneCameraPreviewPass Preview=this.CameraPreviews[I];
            if(Preview.Width<=0||Preview.Height<=0){
                continue;
            }

            Vector4 Color=I==0?new Vector4(1f,0.72f,0.1f,1f):new Vector4(0.35f,0.75f,1f,1f);
            Vector3 TopLeft=ScreenToWorld(Preview.X,Preview.Y,InverseViewProjection);
            Vector3 TopRight=ScreenToWorld(Preview.X+Preview.Width,Preview.Y,InverseViewProjection);
            Vector3 BottomRight=ScreenToWorld(Preview.X+Preview.Width,Preview.Y+Preview.Height,InverseViewProjection);
            Vector3 BottomLeft=ScreenToWorld(Preview.X,Preview.Y+Preview.Height,InverseViewProjection);
            Lines.Add(new OverlayVertex { Position=TopLeft,Color=Color });
            Lines.Add(new OverlayVertex { Position=TopRight,Color=Color });
            Lines.Add(new OverlayVertex { Position=TopRight,Color=Color });
            Lines.Add(new OverlayVertex { Position=BottomRight,Color=Color });
            Lines.Add(new OverlayVertex { Position=BottomRight,Color=Color });
            Lines.Add(new OverlayVertex { Position=BottomLeft,Color=Color });
            Lines.Add(new OverlayVertex { Position=BottomLeft,Color=Color });
            Lines.Add(new OverlayVertex { Position=TopLeft,Color=Color });

            IReadOnlyList<GizmoOverlayVertex>Label=GizmoDragHud.BuildScreenText(
                (I+1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Preview.X+6f,Preview.Y+18f,14f,Color,InverseViewProjection,this.Width,this.Height);
            foreach(GizmoOverlayVertex Vertex in Label){
                Lines.Add(new OverlayVertex { Position=Vertex.Position,Color=Vertex.Color });
            }
        }
    }

    private Vector3 ScreenToWorld(float X,float Y,in Matrix4x4 InverseViewProjection){
        float NdcX=(X/(float)this.Width*2f)-1f;
        float NdcY=1f-(Y/(float)this.Height*2f);
        return Vector3.Transform(new Vector3(NdcX,NdcY,0f),InverseViewProjection);
    }

    private ObjectResources CreateObjectResources(SceneRenderObject RenderObject){
        var Mesh=RenderObject.Mesh;

        var Vertices=new Vertex[Mesh.Positions.Length];
        for(var I=0;I<Vertices.Length;I++){
            Vertices[I]=new Vertex { Position=Mesh.Positions[I],Normal=Mesh.Normals[I]};
        }

        var VertexBufferSize=Vertices.Length*Marshal.SizeOf<Vertex>();
        var VertexBuffer= this.CreateUploadBuffer(VertexBufferSize,Vertices);
        VertexBufferView VertexBufferView=new(VertexBuffer.GPUVirtualAddress,VertexBufferSize,Marshal.SizeOf<Vertex>());

        var IndexCount=Mesh.Indices.Length;
        var IndexBufferSize=IndexCount*sizeof(uint);
        var IndexBuffer= this.CreateUploadBuffer(IndexBufferSize,Mesh.Indices);
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
        var Resource= this.Device.CreateCommittedResource(
                                                          HeapType.Upload,
                                                          ResourceDescription.Buffer((ulong)SizeInBytes),
                                                          ResourceStates.GenericRead);

        // Avoid unsafe/fixed raw pointers - the WPF XAML markup-compile pass builds this project
        // through a generated temporary csproj that does not inherit AllowUnsafeBlocks (ADR-0008).
        var Mapped=Resource.Map<byte>(0,SizeInBytes);
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

    private ID3D12Resource CreateDepthBuffer(int W,int H,CpuDescriptorHandle DsvHandle){
        var Description=ResourceDescription.Texture2D(DepthBufferFormat,(uint)W,(uint)H,1,1);
        Description.Flags=ResourceFlags.AllowDepthStencil;

        var Buffer= this.Device.CreateCommittedResource(
                                                        HeapType.Default,
                                                        Description,
                                                        ResourceStates.DepthWrite,
                                                        new ClearValue(DepthBufferFormat,1.0f,0));

        this.Device.CreateDepthStencilView(Buffer,null,DsvHandle);
        return Buffer;
    }

    private PreviewRenderTarget CreatePreviewTarget(int Index,int W,int H){
        var ColorDescription=ResourceDescription.Texture2D(BackBufferFormat,(uint)W,(uint)H,1,1);
        ColorDescription.Flags=ResourceFlags.AllowRenderTarget;
        var Color=this.Device.CreateCommittedResource(
            HeapType.Default,ColorDescription,ResourceStates.CopySource,
            new ClearValue(BackBufferFormat,new Vortice.Mathematics.Color4(5/255f,5/255f,20/255f,1f)));
        var Rtv=this.RtvHeap.GetCPUDescriptorHandleForHeapStart()+((FrameCount+Index)*this.RtvDescriptorSize);
        var Dsv=this.DsvHeap.GetCPUDescriptorHandleForHeapStart()+((Index+1)*this.DsvDescriptorSize);
        this.Device.CreateRenderTargetView(Color,null,Rtv);
        return new PreviewRenderTarget{
            Width=W,Height=H,Color=Color,
            Depth=this.CreateDepthBuffer(W,H,Dsv),Rtv=Rtv,Dsv=Dsv,
        };
    }

    private void EnsurePreviewTargets(bool GpuIsIdle=false){
        var RequiresRebuild=false;
        for(var I=0;I<this.CameraPreviewCount;I++){
            var Preview=this.CameraPreviews[I];
            var Target=this.CameraPreviewTargets[I];
            if(Target is null||Target.Width!=Preview.Width||Target.Height!=Preview.Height){
                RequiresRebuild=true;
                break;
            }
        }
        if(!RequiresRebuild){ return; }
        if(!GpuIsIdle){ this.WaitForGpuIdle(); }
        for(var I=0;I<this.CameraPreviewCount;I++){
            var Preview=this.CameraPreviews[I];
            var Target=this.CameraPreviewTargets[I];
            if(Target is not null&&(Target.Width!=Preview.Width||Target.Height!=Preview.Height)){
                Target.Dispose();
                this.CameraPreviewTargets[I]=null;
            }
            if(this.CameraPreviewTargets[I] is null){
                this.CameraPreviewTargets[I]=this.CreatePreviewTarget(I,Preview.Width,Preview.Height);
            }
        }
    }

    private void CreateRenderTargetViews(){
        var Handle= this.RtvHeap.GetCPUDescriptorHandleForHeapStart();
        for(var I=0;I<FrameCount;I++){
            this.RenderTargets[I]= this.SwapChain.GetBuffer<ID3D12Resource>(I);
            this.Device.CreateRenderTargetView(this.RenderTargets[I],null,Handle);
            Handle+= this.RtvDescriptorSize;
        }
    }

    private void UpdateAllConstantBuffers(int FrameIndex){
        if(this.Objects.Count==0){
            // 0件では Map/Unmap 自体を行わない(TotalSize=0 の Map は不正)。
            return;
        }

        var FrameOffset=SceneConstantBufferLayout.ComputeFrameOffset(
            FrameIndex, this.AlignedSlotSize, this.ObjectCapacity,CameraPassCount);
        var TotalSize=SceneConstantBufferLayout.ComputeMapLength(
            this.AlignedSlotSize, this.ObjectCapacity,CameraPassCount);
        var Mapped= this.ConstantBuffer.Map<byte>(0,FrameOffset+TotalSize);
        for(var Pass=0;Pass<= this.CameraPreviewCount;Pass++){
            var View=Pass==0?this.CurrentView:this.CameraPreviews[Pass-1].View;
            var Projection=Pass==0?this.CurrentProjection:this.CameraPreviews[Pass-1].Projection;
            for(var I=0;I< this.Objects.Count;I++){
                var Obj= this.Objects[I];
                var Mvp=Obj.World*View*Projection;
                // Selection tint belongs to the editable primary viewport only. Camera
                // thumbnails are clean presentation passes and must not show gizmo state.
                var Selected=Pass==0
                                  ?SceneSelectionTint.GetWeight(Obj.NodeId, this.SelectedNodeId, this.SelectedNodeIds)
                                  :0f;
                TransformConstants Constants=new(){ Mvp=Mvp,BaseColorFactor=Obj.BaseColorFactor,Selected=Selected };
                var WriteOffset=SceneConstantBufferLayout.ComputeWriteOffset(
                    FrameOffset,Pass,I, this.AlignedSlotSize, this.ObjectCapacity);
                MemoryMarshal.Write(
                    Mapped.Slice(WriteOffset,Marshal.SizeOf<TransformConstants>()),
                    ref Constants);
            }
        }

        this.ConstantBuffer.Unmap(0,null);
    }

    private void UpdateOverlayConstantBuffer(int FrameIndex){
        OverlayConstants Constants=new(){ ViewProjection= this.CurrentView* this.CurrentProjection };
        var FrameOffset=FrameIndex*256;
        var Mapped= this.OverlayConstantBuffer.Map<byte>(
                                                         0,FrameOffset+Marshal.SizeOf<OverlayConstants>());
        MemoryMarshal.Write(
            Mapped.Slice(FrameOffset,Marshal.SizeOf<OverlayConstants>()),ref Constants);
        this.OverlayConstantBuffer.Unmap(0,null);
    }

    private void UpdateOverlayVertexBuffer(int FrameIndex){
        var VertexSize=Marshal.SizeOf<OverlayVertex>();
        var FrameOffset=FrameIndex*MaxOverlayVertices*VertexSize;
        var TotalCount= this.OverlayTriangleVertexCount+ this.OverlayLineVertexCount;
        if(TotalCount>0){
            var Mapped= this.OverlayVertexBuffer.Map<byte>(
                                                           0,FrameOffset+(VertexSize*TotalCount));
            var Offset=FrameOffset;
            foreach(var Vertex in this.OverlayTriangles){
                var Value=Vertex;
                MemoryMarshal.Write(Mapped.Slice(Offset,VertexSize),ref Value);
                Offset+=VertexSize;
            }
            foreach(var Vertex in this.OverlayLines){
                var Value=Vertex;
                MemoryMarshal.Write(Mapped.Slice(Offset,VertexSize),ref Value);
                Offset+=VertexSize;
            }

            this.OverlayVertexBuffer.Unmap(0,null);
        }

        var FrameAddress= this.OverlayVertexBuffer.GPUVirtualAddress+(ulong)FrameOffset;
        this.OverlayTriangleVertexViews[FrameIndex]=new VertexBufferView(
                                                                         FrameAddress, this.OverlayTriangleVertexCount*VertexSize,VertexSize);
        this.OverlayLineVertexViews[FrameIndex]=new VertexBufferView(
                                                                     FrameAddress+(ulong)(this.OverlayTriangleVertexCount*VertexSize), this.OverlayLineVertexCount*VertexSize,VertexSize);
    }

    /// <summary>
    /// ヒエラルキー/ビューポートの選択が変わったときに呼ばれる(<see cref="SceneHwndHost.SetSelection"/>経由)。
    /// <paramref name="NodeId"/>が primary、<paramref name="NodeIds"/>が複数選択の全体
    /// (primaryを含む)。強さの決定は<see cref="SceneSelectionTint"/>。
    /// </summary>
    public void SetSelectedNode(NodePath?NodeId,IReadOnlyCollection<NodePath>?NodeIds=null){
        this.SelectedNodeId=NodeId;
        this.SelectedNodeIds=NodeIds;
        MarkAllDirty(this.ConstantsDirty);
    }

    public void Resize(int NewWidth,int NewHeight){
        NewWidth=Math.Max(1,NewWidth);
        NewHeight=Math.Max(1,NewHeight);
        if(NewWidth== this.Width&&NewHeight== this.Height){
            return;
        }

        this.WaitForGpuIdle();

        for(var I=0;I<FrameCount;I++){
            this.RenderTargets[I].Dispose();
        }

        this.DepthBuffer.Dispose();

        this.Width=NewWidth;
        this.Height=NewHeight;
        for(var I=0;I< this.CameraPreviewCount;I++){
            this.CameraPreviews[I]=this.ClipPreview(this.CameraPreviews[I]);
        }
        this.SwapChain.ResizeBuffers(FrameCount, this.Width, this.Height,BackBufferFormat,SwapChainFlags.None);
        this.CreateRenderTargetViews();
        this.DepthBuffer= this.CreateDepthBuffer(
            this.Width,this.Height,this.DsvHeap.GetCPUDescriptorHandleForHeapStart());
        for(var I=0;I<this.CameraPreviewCount;I++){
            this.CameraPreviewTargets[I]?.Dispose();
            this.CameraPreviewTargets[I]=null;
        }
        this.EnsurePreviewTargets(GpuIsIdle:true);

        // アスペクト比に追従した projection は所有者(SceneHwndHost)が SetCamera で入れ直す。
    }

    public void RenderFrame(){
        var FrameIndex=(int)this.SwapChain.CurrentBackBufferIndex;
        this.WaitForFrameSlot(FrameIndex);
        if(this.ConstantsDirty[FrameIndex]){
            this.UpdateAllConstantBuffers(FrameIndex);
            this.ConstantsDirty[FrameIndex]=false;
        }
        if(this.OverlayConstantsDirty[FrameIndex]){
            this.UpdateOverlayConstantBuffer(FrameIndex);
            this.OverlayConstantsDirty[FrameIndex]=false;
        }
        if(this.OverlayGeometryDirty[FrameIndex]){
            this.UpdateOverlayVertexBuffer(FrameIndex);
            this.OverlayGeometryDirty[FrameIndex]=false;
        }

        var CommandAllocator= this.CommandAllocators[FrameIndex];
        CommandAllocator.Reset();
        this.CommandList.Reset(CommandAllocator, this.PipelineState);

        this.CommandList.ResourceBarrierTransition(this.RenderTargets[FrameIndex],ResourceStates.Present,ResourceStates.RenderTarget);

        var RtvHandle= this.RtvHeap.GetCPUDescriptorHandleForHeapStart()+(FrameIndex* this.RtvDescriptorSize);
        var DsvHandle= this.DsvHeap.GetCPUDescriptorHandleForHeapStart();
        this.CommandList.OMSetRenderTargets(RtvHandle,DsvHandle);
        this.CommandList.ClearRenderTargetView(RtvHandle,new Vortice.Mathematics.Color4(5/255f,5/255f,20/255f,1f));
        this.CommandList.ClearDepthStencilView(DsvHandle,ClearFlags.Depth,1.0f,0);

        this.CommandList.SetGraphicsRootSignature(this.RootSignature);
        this.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        this.CommandList.RSSetViewport(0,0, this.Width, this.Height);
        this.CommandList.RSSetScissorRect(this.Width, this.Height);

        this.DrawScenePass(FrameIndex,0);

        // Render every thumbnail into its own RGBA/depth pair and only then copy the colour
        // into the Scene View. This fully separates preview depth, viewport and clear state.
        for(var PreviewIndex=0;PreviewIndex< this.CameraPreviewCount;PreviewIndex++){
            var Preview= this.CameraPreviews[PreviewIndex];
            var PreviewTarget=this.CameraPreviewTargets[PreviewIndex];
            if(Preview.Width<=0||Preview.Height<=0||PreviewTarget is null){
                continue;
            }

            this.CommandList.ResourceBarrierTransition(
                PreviewTarget.Color,ResourceStates.CopySource,ResourceStates.RenderTarget);
            this.CommandList.OMSetRenderTargets(PreviewTarget.Rtv,PreviewTarget.Dsv);
            this.CommandList.ClearRenderTargetView(
                PreviewTarget.Rtv,new Vortice.Mathematics.Color4(5/255f,5/255f,20/255f,1f));
            this.CommandList.ClearDepthStencilView(PreviewTarget.Dsv,ClearFlags.Depth,1.0f,0);
            this.CommandList.SetPipelineState(this.PipelineState);
            this.CommandList.RSSetViewport(0,0,PreviewTarget.Width,PreviewTarget.Height);
            this.CommandList.RSSetScissorRect(PreviewTarget.Width,PreviewTarget.Height);
            this.DrawScenePass(FrameIndex,PreviewIndex+1);

            this.CommandList.ResourceBarrierTransition(
                PreviewTarget.Color,ResourceStates.RenderTarget,ResourceStates.CopySource);
            this.CommandList.ResourceBarrierTransition(
                this.RenderTargets[FrameIndex],ResourceStates.RenderTarget,ResourceStates.CopyDest);
            this.CommandList.CopyTextureRegion(
                new TextureCopyLocation(this.RenderTargets[FrameIndex],0),Preview.X,Preview.Y,0,
                new TextureCopyLocation(PreviewTarget.Color,0),null);
            this.CommandList.ResourceBarrierTransition(
                this.RenderTargets[FrameIndex],ResourceStates.CopyDest,ResourceStates.RenderTarget);
        }

        // Gizmos and selection overlays belong to the editable primary viewport. Restore
        // its rasterizer state before issuing the existing overlay draws below.
        this.CommandList.RSSetViewport(0,0, this.Width, this.Height);
        this.CommandList.RSSetScissorRect(this.Width, this.Height);

        if(this.OverlayTriangleVertexCount>0){
            this.CommandList.SetPipelineState(this.OverlayTrianglePipelineState);
            this.CommandList.SetGraphicsRootConstantBufferView(
                                                               0, this.OverlayConstantBuffer.GPUVirtualAddress+(ulong)(FrameIndex*256));
            this.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            this.CommandList.IASetVertexBuffers(0, this.OverlayTriangleVertexViews[FrameIndex]);
            this.CommandList.DrawInstanced(this.OverlayTriangleVertexCount,1,0,0);
        }
        if(this.OverlayLineVertexCount>0){
            this.CommandList.SetPipelineState(this.OverlayLinePipelineState);
            this.CommandList.SetGraphicsRootConstantBufferView(
                                                               0, this.OverlayConstantBuffer.GPUVirtualAddress+(ulong)(FrameIndex*256));
            this.CommandList.IASetPrimitiveTopology(PrimitiveTopology.LineList);
            this.CommandList.IASetVertexBuffers(0, this.OverlayLineVertexViews[FrameIndex]);
            this.CommandList.DrawInstanced(this.OverlayLineVertexCount,1,0,0);
        }

        this.CommandList.ResourceBarrierTransition(this.RenderTargets[FrameIndex],ResourceStates.RenderTarget,ResourceStates.Present);
        this.CommandList.Close();

        this.CommandQueue.ExecuteCommandList(this.CommandList);
        this.SwapChain.Present(1,PresentFlags.None);

        this.SignalFrameSlot(FrameIndex);
    }

    private void DrawScenePass(int FrameIndex,int PassIndex){
        var CameraNodeId=PassIndex==0?default:this.CameraPreviews[PassIndex-1].CameraNodeId;
        var PassOffset=(FrameIndex* this.ObjectCapacity*CameraPassCount)
                       +(PassIndex* this.ObjectCapacity);
        for(var I=0;I< this.Objects.Count;I++){
            var Obj= this.Objects[I];
            if(!CameraPreviewDrawPolicy.ShouldDraw(PassIndex,Obj.NodeId,CameraNodeId)){
                continue;
            }
            var ConstantOffset=(PassOffset+I)* this.AlignedSlotSize;
            this.CommandList.SetGraphicsRootConstantBufferView(
                                                               0, this.ConstantBuffer.GPUVirtualAddress+(ulong)ConstantOffset);
            this.CommandList.IASetVertexBuffers(0,Obj.VertexBufferView);
            this.CommandList.IASetIndexBuffer(Obj.IndexBufferView);
            this.CommandList.DrawIndexedInstanced(Obj.IndexCount,1,0,0,0);
        }
    }

    private void SetScissorRect(int Left,int Top,int Right,int Bottom){
        RawRect Rect=(System.Drawing.Rectangle)new System.Drawing.Rectangle(Left,Top,Right-Left,Bottom-Top);
        this.CommandList.RSSetScissorRects(Rect);
    }

    private void WaitForFrameSlot(int FrameIndex){
        var RequiredValue= this.FrameResources.GetRequiredFenceValue(FrameIndex);
        if(RequiredValue==0|| this.Fence.CompletedValue>=RequiredValue){ return; }

        this.Fence.SetEventOnCompletion(RequiredValue, this.FenceEvent);
        NativeEvent.Wait(this.FenceEvent);
    }

    private void SignalFrameSlot(int FrameIndex){
        var ValueToSignal= this.FrameResources.MarkSubmitted(FrameIndex);
        this.CommandQueue.Signal(this.Fence,ValueToSignal);
    }

    private void WaitForGpuIdle(){
        var ValueToSignal= this.FrameResources.MarkGpuIdle();
        this.CommandQueue.Signal(this.Fence,ValueToSignal);
        if(this.Fence.CompletedValue<ValueToSignal){
            this.Fence.SetEventOnCompletion(ValueToSignal, this.FenceEvent);
            NativeEvent.Wait(this.FenceEvent);
        }
    }

    public void Dispose(){
        this.WaitForGpuIdle();

        this.ConstantBuffer.Dispose();
        this.OverlayConstantBuffer.Dispose();
        this.OverlayVertexBuffer.Dispose();
        foreach(var Obj in this.Objects){
            DisposeObjectResources(Obj);
        }

        this.PipelineState.Dispose();
        this.OverlayLinePipelineState.Dispose();
        this.OverlayTrianglePipelineState.Dispose();
        this.RootSignature.Dispose();
        this.CommandList.Dispose();
        foreach(var CommandAllocator in this.CommandAllocators){
            CommandAllocator.Dispose();
        }

        this.DepthBuffer.Dispose();
        foreach(var PreviewTarget in this.CameraPreviewTargets){
            PreviewTarget?.Dispose();
        }
        this.DsvHeap.Dispose();
        for(var I=0;I<FrameCount;I++){
            this.RenderTargets[I].Dispose();
        }

        this.RtvHeap.Dispose();
        this.SwapChain.Dispose();
        this.CommandQueue.Dispose();
        this.Fence.Dispose();
        NativeEvent.Close(this.FenceEvent);
        this.Device.Dispose();
    }

    private static void MarkAllDirty(bool[]Dirty){
        for(var FrameIndex=0;FrameIndex<Dirty.Length;FrameIndex++){
            Dirty[FrameIndex]=true;
        }
    }
}
