using System.Numerics;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.Streaming;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 仮想ジオメトリの描画（ADR-0100 決定 3、M0130 単位 4）。メッシュの塊の階層を GPU に置き、主パスの前に計算シェーダー（VirtualGeometry.hlsl）が
/// 物体ごとに描く塊を選んで、描く数を間接描画の引数へ書く。選んだ塊は 1 塊 1 インスタンスの間接描画で描く。
/// 影（カスケードの置き場・局所光の置き場・仮想シャドウ）、Lumen の画面の下書き、主パス、カメラプレビューは、主ビューで選んだ同じ塊を描く（M0130 単位 5）。
/// Lumen のカード撮影は、メッシュの空間で根の段の塊（代理メッシュ）を描く。観察者の表示には仮想ジオメトリの版が無いので、元のメッシュで描く。
/// 計算のルートシグネチャ（ここが持つ）: 0 = ルート定数 b0（<see cref="StageVirtualGeometrySelection"/>）、
/// 1 = 定数バッファ b1（<see cref="StageVirtualGeometryOcclusion"/>）、2 = t0 塊、3 = t1 まとまり、4 = t2 塊の包む球、5 = u0 描く塊の番号、6 = u1 間接描画の引数、
/// 7 = u2 遮蔽の一覧、8 = u3 1 パス目と 2 パス目の引数、9 = 記述子の表 t3（Hi-Z）、10 = t4 準備済みの印、11 = u4 知らせ。
/// 主パスの遮蔽（M0131 単位 2）は StageRenderer.Occlusion.cs、ページの出し入れ（単位 4）は StageRenderer.VirtualGeometryStreaming.cs。
/// </summary>
public sealed unsafe partial class StageRenderer {
    internal const string VirtualGeometryArtifactFileName="VirtualGeometry.cgeshaderartifact";
    /// <summary>主パスのルートシグネチャで、仮想ジオメトリの頂点（t22）を置く引数の番号。t23 索引、t24 塊、t25 描く塊の番号が続く。</summary>
    private const uint VirtualGeometryVerticesRootParameter=13;
    private const uint VirtualGeometryRootParameterCount=4;
    /// <summary>影のルートシグネチャで、仮想ジオメトリの頂点（t22）を置く引数の番号。t23〜t25 が続く。</summary>
    private const uint ShadowVirtualGeometryVerticesRootParameter=3;
    /// <summary>まとまりの誤差が画面でこの画素数以下になる、いちばん粗い塊を描く。</summary>
    internal const float VirtualGeometryThresholdPixels=1f;
    /// <summary>
    /// 三角形の下限（画素、M0130 単位 6）。三角形の大きさが画面でこれ以下になったまとまりは、誤差が大きくても細かくしない。0 なら使わない。
    /// 物体の向きの法線マップを持つメッシュにだけ掛ける（細かい陰影は法線マップが持つ）。既定の 16 画素は計測で決めた（M0130 の記録）。
    /// </summary>
    internal float VirtualGeometryTriangleFloorPixels { get;set; }=16f;
    /// <summary>試験用: 法線マップの無いメッシュにも下限を掛ける（下限だけの劣化を測るため）。</summary>
    internal bool VirtualGeometryTriangleFloorWithoutNormalMapForTesting { get;set; }
    /// <summary>これより近い距離は、この距離として画素数を求める（カメラの中に入った塊の画素数を無限大にしない）。</summary>
    private const float VirtualGeometryMinimumDistanceMeters=0.01f;
    private const uint VirtualGeometryThreadGroupSize=64;
    private struct VirtualGeometryMeshBuffers {
        /// <summary>頂点の区画の置き場（CPU から書ける、置いたページだけ中身がある）。</summary>
        public ID3D12Resource*Vertices;
        public byte*MappedVertices;
        /// <summary>索引の枠の置き場。</summary>
        public ID3D12Resource*Indices;
        public byte*MappedIndices;
        /// <summary>塊の表（置いた塊の FirstIndex は置いた枠を指す）。</summary>
        public ID3D12Resource*Clusters;
        public byte*MappedClusters;
        /// <summary>まとまりの準備済みの印（フレームの枠ごとの区画）。</summary>
        public ID3D12Resource*Ready;
        public byte*MappedReady;
        /// <summary>選択が書く知らせ（使ったページの印と要求、塊の数ずつ）。</summary>
        public ID3D12Resource*Feedback;
        public ID3D12Resource*FeedbackZeros;
        public D3D12_RESOURCE_STATES FeedbackState;
        public StageFeedbackReadback?FeedbackReadback;
        public VirtualGeometryResidency?Residency;
        public ulong StreamingFrame;
        /// <summary>元の塊の表（置き場を作るまで持つ）。</summary>
        public StageVirtualGeometryCluster[]SourceClusters;
        public int GroupCount;
        public ID3D12Resource*Groups;
        /// <summary>塊ごとの包む球（メッシュの座標、xyz が中心、w が半径）。遮蔽を調べる計算シェーダーが読む。</summary>
        public ID3D12Resource*ClusterBounds;
        /// <summary>代理メッシュ（根の段の塊）の番号の一覧。</summary>
        public ID3D12Resource*ProxyClusters;
        /// <summary>代理メッシュを描く間接描画の引数（中身は変わらない）。</summary>
        public ID3D12Resource*ProxyArguments;
        public uint ClusterCount;
        /// <summary>塊を置き場へ置く形（M0131 単位 3）。置き場は選択を初めて動かすときに作る（単位 4）。</summary>
        public VirtualGeometrySlotLayout Layout;
        /// <summary>物体の向きの法線マップの、材質のテクスチャの配列での番号 + 1（0 なら無い）。材質の置き場ができてから決める。</summary>
        public uint ObjectNormalMap;
    }
    /// <summary>まだ材質のテクスチャの配列へ置いていない、物体の向きの法線マップ（メッシュの番号ごと）。</summary>
    private readonly Dictionary<int,RenderTextureResource>PendingVirtualGeometryNormalMaps=[];
    private struct VirtualGeometryObjectBuffers {
        public ID3D12Resource*VisibleClusters;
        public ID3D12Resource*DrawArguments;
        /// <summary>遮蔽の一覧: 塊の数ずつ、見えた印（前のフレームで主パスに写った塊が 1）、1 パス目の番号、2 パス目の番号。</summary>
        public ID3D12Resource*Occlusion;
        /// <summary>1 パス目（0 バイト目）と 2 パス目（16 バイト目）の間接描画の引数。</summary>
        public ID3D12Resource*PassArguments;
        public int MeshIndex;
        public uint Capacity;
        /// <summary>作ったばかりで、まだ共通の状態（COMMON）にある。</summary>
        public bool Fresh;
        /// <summary>このフレームで選んだ（主パスがこの物体を間接描画で描く）。</summary>
        public bool Selected;
    }
    private VirtualGeometryMeshBuffers[]VirtualGeometryMeshes=[];
    private VirtualGeometryObjectBuffers[]VirtualGeometryObjects=[];
    private ID3D12Resource*VirtualGeometryArgumentTemplate;
    private ID3D12CommandSignature*VirtualGeometryCommandSignature;
    private ID3D12RootSignature*VirtualGeometrySelectionRootSignature;
    private ID3D12PipelineState*VirtualGeometrySelectionPipeline;
    /// <summary>試験用: 最後のフレームで、間接描画で描いた物体の数。</summary>
    internal int VirtualGeometryObjectsDrawnLastFrameForTesting { get;private set; }
    /// <summary>メッシュの番号 <paramref name="MeshIndex"/> に仮想ジオメトリを置く（無ければ何もしない）。メッシュを作るときに呼ぶ。</summary>
    private void SetVirtualGeometryMesh(int MeshIndex,StageVirtualGeometryCreateInfo?Info){
        if(Info is null){ return; }
        if(Info.Vertices.Length==0||Info.Indices.Length==0||Info.Clusters.Length==0||Info.Groups.Length==0){
            throw new ArgumentException("A virtual geometry mesh needs vertices, indices, clusters and groups.",nameof(Info));
        }
        foreach(var Cluster in Info.Clusters){
            if(Cluster.IndexCount==0||Cluster.IndexCount%3!=0||Cluster.IndexCount>StageVirtualGeometryCreateInfo.MaxTrianglesPerCluster*3
                ||(ulong)Cluster.FirstIndex+Cluster.IndexCount>(ulong)Info.Indices.Length||Cluster.Group>=(uint)Info.Groups.Length||Cluster.ParentGroup>=Info.Groups.Length){
                throw new ArgumentException("A virtual geometry cluster refers outside its mesh.",nameof(Info));
            }
        }
        // まとまりの数は塊の数以下（どのまとまりも 1 つ以上の塊を作る）。知らせはまとまりの番号を塊の数の幅で並べる。
        if(Info.Groups.Length>Info.Clusters.Length){ throw new ArgumentException("A virtual geometry mesh has more groups than clusters.",nameof(Info)); }
        // 塊ごとに頂点と索引を持つ置き場の形にする（索引が頂点の外を指せば、ここで断る）。置き場は選択を初めて動かすときに作る。
        var Layout=VirtualGeometrySlotLayout.Create(Info);
        if(this.VirtualGeometryMeshes.Length<=MeshIndex){
            Array.Resize(ref this.VirtualGeometryMeshes,Math.Max(MeshIndex+1,Math.Max(16,this.VirtualGeometryMeshes.Length*2)));
        }
        this.VirtualGeometryMeshes[MeshIndex]=new(){
            SourceClusters=[..Info.Clusters],
            GroupCount=Info.Groups.Length,
            Groups=this.CreateFilledUploadBuffer<StageVirtualGeometryGroup>(Info.Groups,"creating virtual geometry groups"),
            ClusterBounds=this.CreateFilledUploadBuffer<Vector4>(ClusterBoundingSpheres(Info),"creating virtual geometry cluster bounds"),
            ClusterCount=(uint)Info.Clusters.Length,
            Layout=Layout,
        };
        List<uint>Proxy=[];
        for(var Index=0;Index<Info.Clusters.Length;Index++){
            if(Info.Clusters[Index].ParentGroup<0){ Proxy.Add((uint)Index); }
        }
        if(Proxy.Count==0){ throw new ArgumentException("A virtual geometry mesh needs at least one root cluster.",nameof(Info)); }
        D3D12_DRAW_ARGUMENTS ProxyArguments=new(){ VertexCountPerInstance=StageVirtualGeometryCreateInfo.MaxTrianglesPerCluster*3,InstanceCount=(uint)Proxy.Count };
        this.VirtualGeometryMeshes[MeshIndex].ProxyClusters=this.CreateFilledUploadBuffer<uint>(Proxy.ToArray(),"creating virtual geometry proxy clusters");
        this.VirtualGeometryMeshes[MeshIndex].ProxyArguments=this.CreateFilledUploadBuffer<D3D12_DRAW_ARGUMENTS>(new ReadOnlySpan<D3D12_DRAW_ARGUMENTS>(&ProxyArguments,1),"creating virtual geometry proxy arguments");
        if(Info.ObjectNormalMap is{ }NormalMap){
            this.PendingVirtualGeometryNormalMaps[MeshIndex]=NormalMap;
            if(this.SrvHeap!=null){ this.PlaceVirtualGeometryNormalMaps(); }
        }
    }
    /// <summary>
    /// 物体の向きの法線マップを、材質のテクスチャと同じ配列へ置く。材質の置き場ができてから呼ぶ（作るときは材質の後、作った後のメッシュはすぐ）。
    /// テクスチャは次のフレームの最初に送られる。
    /// </summary>
    private void PlaceVirtualGeometryNormalMaps(){
        foreach(var (MeshIndex,NormalMap) in this.PendingVirtualGeometryNormalMaps){
            var View=this.GetOrAddMaterialTextureView((nint)this.GetOrCreateMaterialTexture(NormalMap,RenderTextureColorSpace.Linear),Srgb:false);
            this.VirtualGeometryMeshes[MeshIndex].ObjectNormalMap=View+1;
        }
        this.PendingVirtualGeometryNormalMaps.Clear();
    }
    /// <summary>塊ごとの包む球（範囲の箱の中心と、そこから最も遠い頂点までの距離）。</summary>
    private static Vector4[]ClusterBoundingSpheres(StageVirtualGeometryCreateInfo Info){
        var Result=new Vector4[Info.Clusters.Length];
        for(var Index=0;Index<Result.Length;Index++){
            var Cluster=Info.Clusters[Index];
            var Low=new Vector3(float.MaxValue);
            var High=new Vector3(float.MinValue);
            for(var Corner=Cluster.FirstIndex;Corner<Cluster.FirstIndex+Cluster.IndexCount;Corner++){
                var Position=Info.Vertices[Info.Indices[Corner]].Position;
                Low=Vector3.Min(Low,Position);
                High=Vector3.Max(High,Position);
            }
            var Centre=(Low+High)*0.5f;
            var Radius=0f;
            for(var Corner=Cluster.FirstIndex;Corner<Cluster.FirstIndex+Cluster.IndexCount;Corner++){
                Radius=MathF.Max(Radius,Vector3.Distance(Centre,Info.Vertices[Info.Indices[Corner]].Position));
            }
            // 頂点の位置の丸めで球の外へ出ないよう、わずかに広げる。
            Result[Index]=new Vector4(Centre,Radius*1.0001f+1e-6f);
        }
        return Result;
    }
    private bool MeshHasVirtualGeometry(int MeshIndex)=>(uint)MeshIndex<(uint)this.VirtualGeometryMeshes.Length&&this.VirtualGeometryMeshes[MeshIndex].Layout is not null;
    private ID3D12Resource*CreateFilledUploadBuffer<T>(ReadOnlySpan<T>Values,string Context)where T:unmanaged{
        var Size=checked((uint)(Values.Length*sizeof(T)));
        this.CreateUploadBuffer(Size,out var Resource,out var Mapped,Context);
        fixed(T*Source=Values){ System.Buffer.MemoryCopy(Source,Mapped,Size,Size); }
        Resource->Unmap(0,null);
        return Resource;
    }
    private ID3D12Resource*CreateVirtualGeometryDefaultBuffer(ulong Size,string Context){
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Size,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_COMMON,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource",Context);
        return Resource;
    }
    /// <summary><paramref name="Desc"/> の仮想ジオメトリの版の PSO。版が無い（観察者の表示など）か、選択をまだ作っていなければ null（その描画は元のメッシュで描く）。</summary>
    private ID3D12PipelineState*VirtualGeometryPipelineFor(in StagePipelineDesc Desc){
        if(this.VirtualGeometrySelectionPipeline==null){ return null; }
        return StagePipelines.VirtualGeometry(Desc) is{ }Variant?this.ResolvePipelineState(Variant):null;
    }
    /// <summary>
    /// 主ビューから見て描く塊を、仮想ジオメトリを持つ物体ごとに選ぶ（<see cref="StagePass.VirtualGeometrySelection"/>）。
    /// 引数の描く数を 0 にしてから計算シェーダーを動かし、主パスが読める状態にして終わる。
    /// </summary>
    private void RecordVirtualGeometrySelection(ref FrameRecording Recording){
        this.VirtualGeometryObjectsDrawnLastFrameForTesting=0;
        if(this.VirtualGeometryMeshes.Length==0||StagePipelines.VirtualGeometry(Recording.MainPipelineDesc) is not{ }Variant||this.ResolvePipelineState(Variant)==null){
            this.ClearVirtualGeometrySelection();
            return;
        }
        this.EnsureVirtualGeometryPipeline();
        // 置き場はメッシュごとに、選択を初めて動かすときに作る（代理メッシュを描く Lumen のカードなども、この後に置き場を読む）。
        for(var MeshIndex=0;MeshIndex<this.VirtualGeometryMeshes.Length;MeshIndex++){
            if(this.MeshHasVirtualGeometry(MeshIndex)){ this.EnsureVirtualGeometryPool(ref this.VirtualGeometryMeshes[MeshIndex]); }
        }
        if(this.VirtualGeometryObjects.Length<this.ObjectCount){
            Array.Resize(ref this.VirtualGeometryObjects,this.ObjectCount);
        }
        var Count=0;
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            ref var Object=ref this.VirtualGeometryObjects[ObjectIndex];
            Object.Selected=false;
            var MeshIndex=this.ObjectMeshIndices[ObjectIndex];
            if(!this.ObjectActive[ObjectIndex]||this.ObjectHiddenFromView[ObjectIndex]||!this.MeshHasVirtualGeometry(MeshIndex)){ continue; }
            var Clusters=this.VirtualGeometryMeshes[MeshIndex].ClusterCount;
            if(Object.VisibleClusters==null||Object.MeshIndex!=MeshIndex||Object.Capacity<Clusters){
                this.Retire(Object.VisibleClusters);
                this.Retire(Object.DrawArguments);
                this.Retire(Object.Occlusion);
                this.Retire(Object.PassArguments);
                // 遮蔽の一覧は作ったときに 0（D3D12 の委託の資源は 0 で始まる）なので、最初のフレームは全部を 2 パス目で調べる。
                Object=new(){
                    VisibleClusters=this.CreateVirtualGeometryDefaultBuffer((ulong)Clusters*sizeof(uint),"creating a virtual geometry visible-cluster list"),
                    DrawArguments=this.CreateVirtualGeometryDefaultBuffer((ulong)sizeof(D3D12_DRAW_ARGUMENTS),"creating virtual geometry draw arguments"),
                    Occlusion=this.CreateVirtualGeometryDefaultBuffer((ulong)Clusters*3*sizeof(uint),"creating a virtual geometry occlusion list"),
                    PassArguments=this.CreateVirtualGeometryDefaultBuffer((ulong)sizeof(D3D12_DRAW_ARGUMENTS)*2,"creating virtual geometry pass arguments"),
                    MeshIndex=MeshIndex,
                    Capacity=Clusters,
                    Fresh=true,
                };
            }
            Object.Selected=true;
            Count++;
        }
        if(Count==0){ return; }
        // 選ぶ物体が使うメッシュごとに、ページを出し入れし、準備済みの印を書き、知らせを 0 に戻す。
        var Streamed=new bool[this.VirtualGeometryMeshes.Length];
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan(0,this.ObjectCount)){
            if(!Object.Selected||Streamed[Object.MeshIndex]){ continue; }
            Streamed[Object.MeshIndex]=true;
            this.UpdateVirtualGeometryStreaming(ref this.VirtualGeometryMeshes[Object.MeshIndex]);
        }
        this.VirtualGeometryOcclusionThisFrame=this.VirtualGeometryOcclusion;
        var Barriers=new D3D12_RESOURCE_BARRIER[Count*4];
        // 1. 引数を写し先に、番号の一覧を書き込み先にする。
        var Written=0;
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan(0,this.ObjectCount)){
            if(!Object.Selected){ continue; }
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.DrawArguments,Object.Fresh?D3D12_RESOURCE_STATE_COMMON:D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,D3D12_RESOURCE_STATE_COPY_DEST);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.VisibleClusters,Object.Fresh?D3D12_RESOURCE_STATE_COMMON:D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.PassArguments,Object.Fresh?D3D12_RESOURCE_STATE_COMMON:D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,D3D12_RESOURCE_STATE_COPY_DEST);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.Occlusion,Object.Fresh?D3D12_RESOURCE_STATE_COMMON:D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            Object.Fresh=false;
        }
        this.ResourceBarriers(Barriers,Written);
        // 2. 引数を「塊 1 つ分の頂点、描く数 0」に戻す。
        Written=0;
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan(0,this.ObjectCount)){
            if(!Object.Selected){ continue; }
            this.CommandList->CopyBufferRegion(Object.DrawArguments,0,this.VirtualGeometryArgumentTemplate,0,(ulong)sizeof(D3D12_DRAW_ARGUMENTS));
            this.CommandList->CopyBufferRegion(Object.PassArguments,0,this.VirtualGeometryArgumentTemplate,0,(ulong)sizeof(D3D12_DRAW_ARGUMENTS));
            this.CommandList->CopyBufferRegion(Object.PassArguments,(ulong)sizeof(D3D12_DRAW_ARGUMENTS),this.VirtualGeometryArgumentTemplate,0,(ulong)sizeof(D3D12_DRAW_ARGUMENTS));
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.DrawArguments,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.PassArguments,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        }
        this.ResourceBarriers(Barriers,Written);
        // 3. 物体ごとに塊を選ぶ。
        this.CommandList->SetComputeRootSignature(this.VirtualGeometrySelectionRootSignature);
        this.CommandList->SetPipelineState(this.VirtualGeometrySelectionPipeline);
        var Flags=this.VirtualGeometryOcclusionThisFrame?StageVirtualGeometrySelectionFlags.Occlusion:StageVirtualGeometrySelectionFlags.None;
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.VirtualGeometryObjects[ObjectIndex].Selected){ continue; }
            this.DispatchVirtualGeometry(ObjectIndex,Flags);
        }
        // 4. 知らせを読み戻しの輪へ写し、主パスの頂点シェーダーと間接描画が読める状態にする。
        for(var MeshIndex=0;MeshIndex<Streamed.Length;MeshIndex++){
            if(Streamed[MeshIndex]){ this.CopyVirtualGeometryFeedback(ref this.VirtualGeometryMeshes[MeshIndex]); }
        }
        this.TransitionVirtualGeometryBuffers(D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        this.CommandList->SetPipelineState(Recording.MainPipeline);
    }
    /// <summary>
    /// 物体 <paramref name="ObjectIndex"/> の塊について選択の計算シェーダーを動かす。ルートシグネチャと PSO は設定済みであること。
    /// 調べる働き（<see cref="StageVirtualGeometrySelectionFlags.Test"/>）では、b1 と Hi-Z の表も設定済みであること。
    /// </summary>
    private void DispatchVirtualGeometry(int ObjectIndex,StageVirtualGeometrySelectionFlags Flags){
        ref var Object=ref this.VirtualGeometryObjects[ObjectIndex];
        ref var Mesh=ref this.VirtualGeometryMeshes[Object.MeshIndex];
        var World=this.ObjectTransforms[ObjectIndex];
        StageVirtualGeometrySelection Selection=new(){
            World=World,
            CameraAndScale=new Vector4(this.CurrentCameraPosition,MaximumAxisScale(World)),
            Metric=VirtualGeometryMetric(this.CurrentViewProjection,this.Height).ShaderParameters,
            ClusterCount=Mesh.ClusterCount,
            ThresholdPixels=VirtualGeometryThresholdPixels,
            TriangleFloorPixels=Mesh.ObjectNormalMap!=0||this.VirtualGeometryTriangleFloorWithoutNormalMapForTesting?this.VirtualGeometryTriangleFloorPixels:0f,
            Flags=(uint)Flags,
        };
        this.CommandList->SetComputeRoot32BitConstants(0,VirtualGeometrySelectionConstantCount,&Selection,0);
        this.CommandList->SetComputeRootShaderResourceView(2,Mesh.Clusters->GetGPUVirtualAddress());
        this.CommandList->SetComputeRootShaderResourceView(3,Mesh.Groups->GetGPUVirtualAddress());
        this.CommandList->SetComputeRootShaderResourceView(4,Mesh.ClusterBounds->GetGPUVirtualAddress());
        this.CommandList->SetComputeRootUnorderedAccessView(5,Object.VisibleClusters->GetGPUVirtualAddress());
        this.CommandList->SetComputeRootUnorderedAccessView(6,Object.DrawArguments->GetGPUVirtualAddress());
        this.CommandList->SetComputeRootUnorderedAccessView(7,Object.Occlusion->GetGPUVirtualAddress());
        this.CommandList->SetComputeRootUnorderedAccessView(8,Object.PassArguments->GetGPUVirtualAddress());
        this.CommandList->SetComputeRootShaderResourceView(10,this.VirtualGeometryReadyAddress(Mesh));
        this.CommandList->SetComputeRootUnorderedAccessView(11,Mesh.Feedback->GetGPUVirtualAddress());
        this.CommandList->Dispatch((Mesh.ClusterCount+VirtualGeometryThreadGroupSize-1)/VirtualGeometryThreadGroupSize,1,1);
    }
    /// <summary>
    /// 選んだ物体ごとに、描く塊の番号の一覧と遮蔽の一覧を <paramref name="ListsBefore"/> から <paramref name="ListsAfter"/> へ、
    /// 引数（全体と 1・2 パス目）を <paramref name="ArgumentsBefore"/> から <paramref name="ArgumentsAfter"/> へ移す。
    /// </summary>
    private void TransitionVirtualGeometryBuffers(D3D12_RESOURCE_STATES ArgumentsBefore,D3D12_RESOURCE_STATES ListsBefore,D3D12_RESOURCE_STATES ArgumentsAfter,D3D12_RESOURCE_STATES ListsAfter){
        var Barriers=new D3D12_RESOURCE_BARRIER[this.ObjectCount*4];
        var Written=0;
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan(0,Math.Min(this.ObjectCount,this.VirtualGeometryObjects.Length))){
            if(!Object.Selected){ continue; }
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.DrawArguments,ArgumentsBefore,ArgumentsAfter);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.PassArguments,ArgumentsBefore,ArgumentsAfter);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.VisibleClusters,ListsBefore,ListsAfter);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.Occlusion,ListsBefore,ListsAfter);
        }
        this.ResourceBarriers(Barriers,Written);
    }
    private void ClearVirtualGeometrySelection(){
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan()){ Object.Selected=false; }
        this.VirtualGeometryOcclusionThisFrame=false;
    }
    private void ResourceBarriers(D3D12_RESOURCE_BARRIER[]Barriers,int Count){
        if(Count==0){ return; }
        fixed(D3D12_RESOURCE_BARRIER*First=Barriers){ this.CommandList->ResourceBarrier((uint)Count,First); }
    }
    /// <summary>物体 <paramref name="ObjectIndex"/> をこのフレームの主パスで間接描画するか（塊を選んだか）。</summary>
    private bool DrawsWithVirtualGeometry(int ObjectIndex)=>(uint)ObjectIndex<(uint)this.VirtualGeometryObjects.Length&&this.VirtualGeometryObjects[ObjectIndex].Selected;
    /// <summary>
    /// 普通の描画で、この物体を飛ばすか。塊を選んだ物体で、<paramref name="VariantAvailable"/>（今の描画の設定に仮想ジオメトリの版がある）なら、
    /// <see cref="DrawSelectedVirtualGeometry"/> が描くので飛ばす。
    /// </summary>
    private bool SkipsNormalDraw(int ObjectIndex,bool VariantAvailable)=>VariantAvailable&&this.DrawsWithVirtualGeometry(ObjectIndex);
    /// <summary>
    /// 塊を選んだ物体のうち <paramref name="Include"/> を満たすものを、<paramref name="Desc"/> の仮想ジオメトリの版で間接描画する。
    /// 1 つでも描いたら <paramref name="Desc"/> の PSO に戻して終わる。描いた物体の数を返す。ルートシグネチャ・描画先・視点は呼び出し側が設定済みであること。
    /// </summary>
    private int DrawSelectedVirtualGeometry(in StagePipelineDesc Desc,uint RootParameter,Func<int,bool>?Include,StageDrawFlags Flags=StageDrawFlags.None,VirtualGeometryList List=VirtualGeometryList.Selected){
        var Pipeline=this.VirtualGeometryPipelineFor(Desc);
        if(Pipeline==null){ return 0; }
        var Drawn=0;
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.DrawsWithVirtualGeometry(ObjectIndex)||Include is not null&&!Include(ObjectIndex)){ continue; }
            if(Drawn==0){ this.CommandList->SetPipelineState(Pipeline); }
            ref var Object=ref this.VirtualGeometryObjects[ObjectIndex];
            ref var Mesh=ref this.VirtualGeometryMeshes[Object.MeshIndex];
            this.SetDrawConstants(ObjectIndex,Flags,Mesh.ObjectNormalMap);
            // 1 パス目と 2 パス目の番号は遮蔽の一覧の 2 つ目と 3 つ目の区画、引数は 1 つの置き場の 0 バイト目と 16 バイト目。
            var Arguments=List==VirtualGeometryList.Selected?Object.DrawArguments:Object.PassArguments;
            var Clusters=List switch{
                VirtualGeometryList.FirstPass=>Object.Occlusion->GetGPUVirtualAddress()+(ulong)Object.Capacity*sizeof(uint),
                VirtualGeometryList.SecondPass=>Object.Occlusion->GetGPUVirtualAddress()+(ulong)Object.Capacity*2*sizeof(uint),
                _=>Object.VisibleClusters->GetGPUVirtualAddress(),
            };
            this.BindVirtualGeometry(RootParameter,Mesh,Clusters);
            this.CommandList->ExecuteIndirect(this.VirtualGeometryCommandSignature,1,Arguments,List==VirtualGeometryList.SecondPass?(ulong)sizeof(D3D12_DRAW_ARGUMENTS):0ul,null,0);
            Drawn++;
        }
        if(Drawn>0){ this.CommandList->SetPipelineState(this.ResolvePipelineState(Desc)); }
        return Drawn;
    }
    /// <summary>
    /// 物体 <paramref name="ObjectIndex"/> の代理メッシュ（根の段の塊）を描く。メッシュに仮想ジオメトリが無いか、<paramref name="Desc"/> に版が無ければ描かずに false。
    /// 描いたら <paramref name="Desc"/> の PSO に戻す。
    /// </summary>
    private bool DrawVirtualGeometryProxy(int ObjectIndex,in StagePipelineDesc Desc,uint RootParameter,StageDrawFlags Flags){
        var MeshIndex=this.ObjectMeshIndices[ObjectIndex];
        if(!this.MeshHasVirtualGeometry(MeshIndex)){ return false; }
        var Pipeline=this.VirtualGeometryPipelineFor(Desc);
        if(Pipeline==null){ return false; }
        ref var Mesh=ref this.VirtualGeometryMeshes[MeshIndex];
        this.CommandList->SetPipelineState(Pipeline);
        this.SetDrawConstants(ObjectIndex,Flags,Mesh.ObjectNormalMap);
        this.BindVirtualGeometry(RootParameter,Mesh,Mesh.ProxyClusters->GetGPUVirtualAddress());
        this.CommandList->ExecuteIndirect(this.VirtualGeometryCommandSignature,1,Mesh.ProxyArguments,0,null,0);
        this.CommandList->SetPipelineState(this.ResolvePipelineState(Desc));
        return true;
    }
    private void BindVirtualGeometry(uint RootParameter,in VirtualGeometryMeshBuffers Mesh,ulong Clusters){
        this.CommandList->SetGraphicsRootShaderResourceView(RootParameter,Mesh.Vertices->GetGPUVirtualAddress());
        this.CommandList->SetGraphicsRootShaderResourceView(RootParameter+1,Mesh.Indices->GetGPUVirtualAddress());
        this.CommandList->SetGraphicsRootShaderResourceView(RootParameter+2,Mesh.Clusters->GetGPUVirtualAddress());
        this.CommandList->SetGraphicsRootShaderResourceView(RootParameter+3,Clusters);
    }
    /// <summary>
    /// ビュー投影から、画面上の大きさの式を作る。投影の W（深さ）が位置で変わらなければ正射影。透視では、縦の 1 メートルが深さ 1 メートルで何画素かを
    /// ビュー投影の y の列の長さと W の列の長さの比で求める（どちらもビューの回転では長さが変わらない）。
    /// </summary>
    internal static ScreenSizeMetric VirtualGeometryMetric(in Matrix4x4 ViewProjection,int ViewportHeightPixels){
        var YScale=new Vector3(ViewProjection.M12,ViewProjection.M22,ViewProjection.M32).Length();
        var DepthScale=new Vector3(ViewProjection.M14,ViewProjection.M24,ViewProjection.M34).Length();
        var Height=Math.Max(ViewportHeightPixels,1);
        if(DepthScale<1e-12f){
            return ScreenSizeMetric.Orthographic(2f/MathF.Max(YScale,1e-12f),Height);
        }
        // 縦の画角 θ について YScale/DepthScale = 1/tan(θ/2)。
        return ScreenSizeMetric.Perspective(2f*MathF.Atan(DepthScale/YScale),Height,VirtualGeometryMinimumDistanceMeters);
    }
    /// <summary>変換のいちばん大きい軸の拡大率（誤差と球の半径を、物体の大きさに合わせて大きく見積もる）。</summary>
    private static float MaximumAxisScale(in Matrix4x4 World)=>MathF.Max(new Vector3(World.M11,World.M12,World.M13).Length(),
        MathF.Max(new Vector3(World.M21,World.M22,World.M23).Length(),new Vector3(World.M31,World.M32,World.M33).Length()));
    private void EnsureVirtualGeometryPipeline(){
        if(this.VirtualGeometrySelectionPipeline!=null){ return; }
        var ArtifactPath=Path.Combine(Path.GetDirectoryName(this.ShaderArtifactPath)??string.Empty,VirtualGeometryArtifactFileName);
        if(!ShaderArtifactRuntime.TryLoad(ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"Virtual geometry requires '{ArtifactPath}': {Error}");
        }
        var Bytecode=Package.GetBytecode("CSMain");
        this.VirtualGeometrySelectionRootSignature=this.CreateVirtualGeometrySelectionRootSignature();
        fixed(byte*Code=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Desc=new(){
                pRootSignature=this.VirtualGeometrySelectionRootSignature,
                CS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Code,BytecodeLength=(nuint)Bytecode.Length },
            };
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateComputePipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                "ID3D12Device::CreateComputePipelineState","creating the virtual geometry selection pipeline");
            this.VirtualGeometrySelectionPipeline=Pipeline;
        }
        D3D12_INDIRECT_ARGUMENT_DESC Argument=new(){ Type=D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DRAW };
        D3D12_COMMAND_SIGNATURE_DESC SignatureDesc=new(){ ByteStride=(uint)sizeof(D3D12_DRAW_ARGUMENTS),NumArgumentDescs=1,pArgumentDescs=&Argument };
        ID3D12CommandSignature*CommandSignature;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandSignature(&SignatureDesc,null,__uuidof<ID3D12CommandSignature>(),(void**)&CommandSignature),
            "ID3D12Device::CreateCommandSignature","creating the virtual geometry draw signature");
        this.VirtualGeometryCommandSignature=CommandSignature;
        D3D12_DRAW_ARGUMENTS Template=new(){ VertexCountPerInstance=StageVirtualGeometryCreateInfo.MaxTrianglesPerCluster*3,InstanceCount=0,StartVertexLocation=0,StartInstanceLocation=0 };
        this.VirtualGeometryArgumentTemplate=this.CreateFilledUploadBuffer<D3D12_DRAW_ARGUMENTS>(new ReadOnlySpan<D3D12_DRAW_ARGUMENTS>(&Template,1),"creating the virtual geometry argument template");
    }
    private ID3D12RootSignature*CreateVirtualGeometrySelectionRootSignature(){
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[12];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,Num32BitValues=VirtualGeometrySelectionConstantCount };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        for(var Register=0u;Register<3;Register++){
            Parameters[2+Register]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
            Parameters[2+Register].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=Register };
        }
        for(var Register=0u;Register<4;Register++){
            Parameters[5+Register]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
            Parameters[5+Register].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=Register };
        }
        D3D12_DESCRIPTOR_RANGE HiZRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=1,BaseShaderRegister=3 };
        Parameters[9]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[9].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&HiZRange };
        Parameters[10]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[10].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=4 };
        Parameters[11]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[11].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=4 };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=12,pParameters=Parameters };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the virtual geometry selection root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the virtual geometry selection root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    /// <summary>試験用: 物体ごとに、最後のフレームで選んだ塊の数（仮想ジオメトリで描いていない物体は -1）。GPU の完了を待って読む。</summary>
    internal int[]ReadVirtualGeometryClusterCountsForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        var Result=new int[this.ObjectCount];
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.DrawsWithVirtualGeometry(ObjectIndex)){
                Result[ObjectIndex]=-1;
                continue;
            }
            var Bytes=GpuBufferReadback.Read(this.Device!,this.VirtualGeometryObjects[ObjectIndex].DrawArguments,(ulong)sizeof(D3D12_DRAW_ARGUMENTS),D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
            Result[ObjectIndex]=BitConverter.ToInt32(Bytes,4);
        }
        return Result;
    }
    /// <summary>試験用: 物体 <paramref name="ObjectIndex"/> で最後のフレームに選んだ塊の番号。GPU の完了を待って読む。</summary>
    internal int[]ReadVirtualGeometryVisibleClustersForTesting(int ObjectIndex){
        var Count=this.ReadVirtualGeometryClusterCountsForTesting()[ObjectIndex];
        if(Count<=0){ return []; }
        var Object=this.VirtualGeometryObjects[ObjectIndex];
        var Bytes=GpuBufferReadback.Read(this.Device!,Object.VisibleClusters,(ulong)Object.Capacity*sizeof(uint),D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        var Result=new int[Count];
        for(var Index=0;Index<Count;Index++){ Result[Index]=BitConverter.ToInt32(Bytes,Index*4); }
        return Result;
    }
    private void ReleaseVirtualGeometry(){
        static void Release(ref ID3D12Resource*Resource){
            if(Resource!=null){ _=Resource->Release();Resource=null; }
        }
        foreach(ref var Mesh in this.VirtualGeometryMeshes.AsSpan()){
            Release(ref Mesh.Vertices);
            Release(ref Mesh.Indices);
            Release(ref Mesh.Clusters);
            Release(ref Mesh.Ready);
            Release(ref Mesh.Feedback);
            Release(ref Mesh.FeedbackZeros);
            Mesh.FeedbackReadback?.Dispose();
            Mesh.FeedbackReadback=null;
            Mesh.Residency=null;
            Release(ref Mesh.Groups);
            Release(ref Mesh.ClusterBounds);
            Release(ref Mesh.ProxyClusters);
            Release(ref Mesh.ProxyArguments);
        }
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan()){
            Release(ref Object.VisibleClusters);
            Release(ref Object.DrawArguments);
            Release(ref Object.Occlusion);
            Release(ref Object.PassArguments);
        }
        Release(ref this.VirtualGeometryArgumentTemplate);
        Release(ref this.VirtualGeometryOcclusionConstants);
        if(this.VirtualGeometryCommandSignature!=null){ _=this.VirtualGeometryCommandSignature->Release();this.VirtualGeometryCommandSignature=null; }
        if(this.VirtualGeometrySelectionPipeline!=null){ _=this.VirtualGeometrySelectionPipeline->Release();this.VirtualGeometrySelectionPipeline=null; }
        if(this.VirtualGeometrySelectionRootSignature!=null){ _=this.VirtualGeometrySelectionRootSignature->Release();this.VirtualGeometrySelectionRootSignature=null; }
    }
}
