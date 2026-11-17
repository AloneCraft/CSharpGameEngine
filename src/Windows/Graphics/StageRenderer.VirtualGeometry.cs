using System.Numerics;
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
/// 物体ごとに描く塊を選んで、描く数を間接描画の引数へ書く。主パスは選ばれた塊を 1 塊 1 インスタンスの間接描画で描く。
/// 影・Lumen・AO の前処理・カメラプレビューは、今は従来のメッシュ（同じ形の元の三角形）で描く（単位 5 で同じ選択へ移す）。
/// 計算のルートシグネチャ（ここが持つ）: 0 = ルート定数 b0（<see cref="StageVirtualGeometrySelection"/>）、1 = t0 塊、2 = t1 まとまり、3 = u0 描く塊の番号、4 = u1 間接描画の引数。
/// </summary>
public sealed unsafe partial class StageRenderer {
    internal const string VirtualGeometryArtifactFileName="VirtualGeometry.cgeshaderartifact";
    /// <summary>主パスのルートシグネチャで、仮想ジオメトリの頂点（t22）を置く引数の番号。t23 索引、t24 塊、t25 描く塊の番号が続く。</summary>
    private const uint VirtualGeometryVerticesRootParameter=13;
    private const uint VirtualGeometryRootParameterCount=4;
    /// <summary>まとまりの誤差が画面でこの画素数以下になる、いちばん粗い塊を描く。</summary>
    internal const float VirtualGeometryThresholdPixels=1f;
    /// <summary>これより近い距離は、この距離として画素数を求める（カメラの中に入った塊の画素数を無限大にしない）。</summary>
    private const float VirtualGeometryMinimumDistanceMeters=0.01f;
    private const uint VirtualGeometryThreadGroupSize=64;
    private struct VirtualGeometryMeshBuffers {
        public ID3D12Resource*Vertices;
        public ID3D12Resource*Indices;
        public ID3D12Resource*Clusters;
        public ID3D12Resource*Groups;
        public uint ClusterCount;
    }
    private struct VirtualGeometryObjectBuffers {
        public ID3D12Resource*VisibleClusters;
        public ID3D12Resource*DrawArguments;
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
        foreach(var Index in Info.Indices){
            if(Index>=(uint)Info.Vertices.Length){ throw new ArgumentException("A virtual geometry index refers outside the vertices.",nameof(Info)); }
        }
        if(this.VirtualGeometryMeshes.Length<=MeshIndex){
            Array.Resize(ref this.VirtualGeometryMeshes,Math.Max(MeshIndex+1,Math.Max(16,this.VirtualGeometryMeshes.Length*2)));
        }
        this.VirtualGeometryMeshes[MeshIndex]=new(){
            Vertices=this.CreateFilledUploadBuffer<StageVirtualGeometryVertex>(Info.Vertices,"creating virtual geometry vertices"),
            Indices=this.CreateFilledUploadBuffer<uint>(Info.Indices,"creating virtual geometry indices"),
            Clusters=this.CreateFilledUploadBuffer<StageVirtualGeometryCluster>(Info.Clusters,"creating virtual geometry clusters"),
            Groups=this.CreateFilledUploadBuffer<StageVirtualGeometryGroup>(Info.Groups,"creating virtual geometry groups"),
            ClusterCount=(uint)Info.Clusters.Length,
        };
    }
    private bool MeshHasVirtualGeometry(int MeshIndex)=>(uint)MeshIndex<(uint)this.VirtualGeometryMeshes.Length&&this.VirtualGeometryMeshes[MeshIndex].Clusters!=null;
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
    /// <summary>主パスで仮想ジオメトリを使う PSO。この表示モードに仮想ジオメトリの版が無ければ null（その物体は従来のメッシュで描く）。</summary>
    private ID3D12PipelineState*VirtualGeometryPipelineFor(in StagePipelineDesc MainPipeline)=>
        this.VirtualGeometrySelectionPipeline==null?null:this.ResolvePipelineState(StagePipelines.VirtualGeometry(MainPipeline));
    /// <summary>
    /// 主ビューから見て描く塊を、仮想ジオメトリを持つ物体ごとに選ぶ（<see cref="StagePass.VirtualGeometrySelection"/>）。
    /// 引数の描く数を 0 にしてから計算シェーダーを動かし、主パスが読める状態にして終わる。
    /// </summary>
    private void RecordVirtualGeometrySelection(ref FrameRecording Recording){
        this.VirtualGeometryObjectsDrawnLastFrameForTesting=0;
        if(this.VirtualGeometryMeshes.Length==0||this.ResolvePipelineState(StagePipelines.VirtualGeometry(Recording.MainPipelineDesc))==null){
            this.ClearVirtualGeometrySelection();
            return;
        }
        this.EnsureVirtualGeometryPipeline();
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
                Object=new(){
                    VisibleClusters=this.CreateVirtualGeometryDefaultBuffer((ulong)Clusters*sizeof(uint),"creating a virtual geometry visible-cluster list"),
                    DrawArguments=this.CreateVirtualGeometryDefaultBuffer((ulong)sizeof(D3D12_DRAW_ARGUMENTS),"creating virtual geometry draw arguments"),
                    MeshIndex=MeshIndex,
                    Capacity=Clusters,
                    Fresh=true,
                };
            }
            Object.Selected=true;
            Count++;
        }
        if(Count==0){ return; }
        var Barriers=new D3D12_RESOURCE_BARRIER[Count*2];
        // 1. 引数を写し先に、番号の一覧を書き込み先にする。
        var Written=0;
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan(0,this.ObjectCount)){
            if(!Object.Selected){ continue; }
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.DrawArguments,Object.Fresh?D3D12_RESOURCE_STATE_COMMON:D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,D3D12_RESOURCE_STATE_COPY_DEST);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.VisibleClusters,Object.Fresh?D3D12_RESOURCE_STATE_COMMON:D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            Object.Fresh=false;
        }
        this.ResourceBarriers(Barriers,Written);
        // 2. 引数を「塊 1 つ分の頂点、描く数 0」に戻す。
        Written=0;
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan(0,this.ObjectCount)){
            if(!Object.Selected){ continue; }
            this.CommandList->CopyBufferRegion(Object.DrawArguments,0,this.VirtualGeometryArgumentTemplate,0,(ulong)sizeof(D3D12_DRAW_ARGUMENTS));
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.DrawArguments,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        }
        this.ResourceBarriers(Barriers,Written);
        // 3. 物体ごとに塊を選ぶ。
        this.CommandList->SetComputeRootSignature(this.VirtualGeometrySelectionRootSignature);
        this.CommandList->SetPipelineState(this.VirtualGeometrySelectionPipeline);
        var Metric=VirtualGeometryMetric(this.CurrentViewProjection,this.Height);
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            ref var Object=ref this.VirtualGeometryObjects[ObjectIndex];
            if(!Object.Selected){ continue; }
            ref var Mesh=ref this.VirtualGeometryMeshes[Object.MeshIndex];
            var World=this.ObjectTransforms[ObjectIndex];
            StageVirtualGeometrySelection Selection=new(){
                World=World,
                CameraAndScale=new Vector4(this.CurrentCameraPosition,MaximumAxisScale(World)),
                Metric=Metric.ShaderParameters,
                ClusterCount=Mesh.ClusterCount,
                ThresholdPixels=VirtualGeometryThresholdPixels,
            };
            this.CommandList->SetComputeRoot32BitConstants(0,VirtualGeometrySelectionConstantCount,&Selection,0);
            this.CommandList->SetComputeRootShaderResourceView(1,Mesh.Clusters->GetGPUVirtualAddress());
            this.CommandList->SetComputeRootShaderResourceView(2,Mesh.Groups->GetGPUVirtualAddress());
            this.CommandList->SetComputeRootUnorderedAccessView(3,Object.VisibleClusters->GetGPUVirtualAddress());
            this.CommandList->SetComputeRootUnorderedAccessView(4,Object.DrawArguments->GetGPUVirtualAddress());
            this.CommandList->Dispatch((Mesh.ClusterCount+VirtualGeometryThreadGroupSize-1)/VirtualGeometryThreadGroupSize,1,1);
        }
        // 4. 主パスの頂点シェーダーと間接描画が読める状態にする。
        Written=0;
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan(0,this.ObjectCount)){
            if(!Object.Selected){ continue; }
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.DrawArguments,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
            Barriers[Written++]=D3D12_RESOURCE_BARRIER.InitTransition(Object.VisibleClusters,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        }
        this.ResourceBarriers(Barriers,Written);
        this.CommandList->SetPipelineState(Recording.MainPipeline);
    }
    private void ClearVirtualGeometrySelection(){
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan()){ Object.Selected=false; }
    }
    private void ResourceBarriers(D3D12_RESOURCE_BARRIER[]Barriers,int Count){
        if(Count==0){ return; }
        fixed(D3D12_RESOURCE_BARRIER*First=Barriers){ this.CommandList->ResourceBarrier((uint)Count,First); }
    }
    /// <summary>物体 <paramref name="ObjectIndex"/> をこのフレームの主パスで間接描画するか（塊を選んだか）。</summary>
    private bool DrawsWithVirtualGeometry(int ObjectIndex)=>(uint)ObjectIndex<(uint)this.VirtualGeometryObjects.Length&&this.VirtualGeometryObjects[ObjectIndex].Selected;
    /// <summary>主パスで、塊を選んだ物体を間接描画で描く。主パスの PSO を戻して終わる。</summary>
    private void DrawVirtualGeometryObjects(ref FrameRecording Recording){
        var Pipeline=this.VirtualGeometryPipelineFor(Recording.MainPipelineDesc);
        if(Pipeline==null){ return; }
        var Drawn=0;
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.DrawsWithVirtualGeometry(ObjectIndex)){ continue; }
            if(Drawn==0){ this.CommandList->SetPipelineState(Pipeline); }
            ref var Object=ref this.VirtualGeometryObjects[ObjectIndex];
            ref var Mesh=ref this.VirtualGeometryMeshes[Object.MeshIndex];
            this.SetDrawConstants(ObjectIndex);
            this.CommandList->SetGraphicsRootShaderResourceView(VirtualGeometryVerticesRootParameter,Mesh.Vertices->GetGPUVirtualAddress());
            this.CommandList->SetGraphicsRootShaderResourceView(VirtualGeometryVerticesRootParameter+1,Mesh.Indices->GetGPUVirtualAddress());
            this.CommandList->SetGraphicsRootShaderResourceView(VirtualGeometryVerticesRootParameter+2,Mesh.Clusters->GetGPUVirtualAddress());
            this.CommandList->SetGraphicsRootShaderResourceView(VirtualGeometryVerticesRootParameter+3,Object.VisibleClusters->GetGPUVirtualAddress());
            this.CommandList->ExecuteIndirect(this.VirtualGeometryCommandSignature,1,Object.DrawArguments,0,null,0);
            Drawn++;
        }
        if(Drawn>0){ this.CommandList->SetPipelineState(Recording.MainPipeline); }
        this.VirtualGeometryObjectsDrawnLastFrameForTesting=Drawn;
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
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[5];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,Num32BitValues=VirtualGeometrySelectionConstantCount };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        Parameters[3]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[3].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[4]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[4].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=5,pParameters=Parameters };
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
    private void ReleaseVirtualGeometry(){
        static void Release(ref ID3D12Resource*Resource){
            if(Resource!=null){ _=Resource->Release();Resource=null; }
        }
        foreach(ref var Mesh in this.VirtualGeometryMeshes.AsSpan()){
            Release(ref Mesh.Vertices);
            Release(ref Mesh.Indices);
            Release(ref Mesh.Clusters);
            Release(ref Mesh.Groups);
        }
        foreach(ref var Object in this.VirtualGeometryObjects.AsSpan()){
            Release(ref Object.VisibleClusters);
            Release(ref Object.DrawArguments);
        }
        Release(ref this.VirtualGeometryArgumentTemplate);
        if(this.VirtualGeometryCommandSignature!=null){ _=this.VirtualGeometryCommandSignature->Release();this.VirtualGeometryCommandSignature=null; }
        if(this.VirtualGeometrySelectionPipeline!=null){ _=this.VirtualGeometrySelectionPipeline->Release();this.VirtualGeometrySelectionPipeline=null; }
        if(this.VirtualGeometrySelectionRootSignature!=null){ _=this.VirtualGeometrySelectionRootSignature->Release();this.VirtualGeometrySelectionRootSignature=null; }
    }
}
