using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.Streaming;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 仮想ジオメトリのページの出し入れ（ADR-0100 決定 1・3、M0131 単位 4）。メッシュごとに、索引の枠と頂点の区画の置き場（<see cref="VirtualGeometrySlotLayout"/>）を
/// 予算の大きさで持ち、どのページを置くかは <see cref="VirtualGeometryResidency"/> が決める。
/// <list type="number">
/// <item>選択の前に、そのフレームの枠へ数フレーム前に写した知らせ（描いたページと要求）を読み戻しの輪から読み、ページを置き・追い出す。
/// 置いたページの頂点・索引・塊の表の 1 件は、CPU から書ける置き場へ直接書く（新しく置く所は、どの進行中のフレームも使っていない）。</item>
/// <item>準備済みの印を、フレームの枠ごとの区画へ書く（進行中のフレームの印を書き換えないため）。</item>
/// <item>選択の後に、知らせを読み戻しの輪へ写す。</item>
/// </list>
/// 置き場は、選択を初めて動かすときに作る（予算などの設定を、描画器を作った後に変えられるように）。取り込んだ成果物は CPU に全部持ち、GPU の置き場だけを出し入れする
/// （ファイルの一部だけを読むのは後の段階）。
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary>索引の枠 1 つと頂点の区画 1 つの GPU の大きさ（どちらも 1,536 バイト）。</summary>
    private static readonly long VirtualGeometrySlotBytes=VirtualGeometrySlotLayout.IndicesPerSlot*sizeof(uint);
    private static readonly long VirtualGeometryBlockBytes=VirtualGeometrySlotLayout.VerticesPerBlock*sizeof(StageVirtualGeometryVertex);
    /// <summary>メッシュ 1 つの置き場に使ってよい GPU の量（バイト）。足りなければ要るページだけを置き、残りは粗く描く。既定は上限なし（全部を置く）。</summary>
    internal long VirtualGeometryPoolBytesPerMesh { get;set; }=long.MaxValue;
    /// <summary>1 フレームで新しく置いてよいページの数。</summary>
    internal int VirtualGeometryPagesPerFrame { get;set; }=64;
    /// <summary>試験用: 置き場を作るときに先に置くページの数の上限（根のページを含む）。既定は入る限り全部。</summary>
    internal int VirtualGeometryPreloadPagesForTesting { get;set; }=int.MaxValue;
    /// <summary>出し入れの報告（描画器を作ってからの合計）。</summary>
    internal StreamingReport VirtualGeometryStreamingReport { get; }=new();
    /// <summary>メッシュの置き場が無ければ作り、入る限りのページを根から置く。</summary>
    private void EnsureVirtualGeometryPool(ref VirtualGeometryMeshBuffers Mesh){
        if(Mesh.Residency is not null){ return; }
        var Layout=Mesh.Layout;
        var GroupCount=Mesh.GroupCount;
        long Slots=Layout.ClusterCount;
        long Blocks=Layout.TotalBlockCount;
        var Full=Slots*VirtualGeometrySlotBytes+Blocks*VirtualGeometryBlockBytes;
        if(this.VirtualGeometryPoolBytesPerMesh<Full){
            // 予算を枠と区画の比で分ける（全部を置くときの比と同じ）。根のページが入らない予算は、根のページの分まで広げる。
            Slots=Math.Max(1,this.VirtualGeometryPoolBytesPerMesh*Slots/Full);
            Blocks=Math.Max(1,this.VirtualGeometryPoolBytesPerMesh*Blocks/Full);
            var (RootSlots,RootBlocks)=RootPageSize(Mesh);
            Slots=Math.Max(Slots,RootSlots);
            Blocks=Math.Max(Blocks,RootBlocks);
        }
        Mesh.Residency=new VirtualGeometryResidency(Layout,Mesh.SourceClusters,GroupCount,(int)Slots,(int)Blocks,(int)FrameCount);
        this.CreateUploadBuffer(checked((uint)(Blocks*VirtualGeometryBlockBytes)),out Mesh.Vertices,out Mesh.MappedVertices,"creating the virtual geometry vertex pool");
        this.CreateUploadBuffer(checked((uint)(Slots*VirtualGeometrySlotBytes)),out Mesh.Indices,out Mesh.MappedIndices,"creating the virtual geometry index pool");
        this.CreateUploadBuffer(checked((uint)(Layout.ClusterCount*sizeof(StageVirtualGeometryCluster))),out Mesh.Clusters,out Mesh.MappedClusters,"creating the virtual geometry cluster table");
        this.CreateUploadBuffer(checked((uint)(FrameCount*GroupCount*sizeof(uint))),out Mesh.Ready,out Mesh.MappedReady,"creating the virtual geometry ready flags");
        // 置いていない塊の表の 1 件も、元の値（まとまりと親）を持たせる。選択はまとまりと親だけを読み、置いていない塊は描かない。
        Mesh.SourceClusters.CopyTo(new Span<StageVirtualGeometryCluster>(Mesh.MappedClusters,Layout.ClusterCount));
        var FeedbackBytes=checked((ulong)Layout.ClusterCount*2*sizeof(uint));
        Mesh.Feedback=this.CreateVirtualGeometryDefaultBuffer(FeedbackBytes,"creating the virtual geometry feedback");
        Mesh.FeedbackZeros=this.CreateFilledUploadBuffer<uint>(new uint[Layout.ClusterCount*2],"creating the virtual geometry feedback clear");
        Mesh.FeedbackState=D3D12_RESOURCE_STATE_COMMON;
        Mesh.FeedbackReadback=new StageFeedbackReadback(this.Device!,FrameCount,(uint)FeedbackBytes,"virtual geometry feedback");
        List<int>Loads=[];
        Mesh.Residency.Preload(Loads,this.VirtualGeometryPreloadPagesForTesting);
        WriteVirtualGeometryPages(ref Mesh,Loads);
    }
    private static (long Slots,long Blocks)RootPageSize(in VirtualGeometryMeshBuffers Mesh){
        long Slots=0,Blocks=0;
        for(var Cluster=0;Cluster<Mesh.SourceClusters.Length;Cluster++){
            if(Mesh.SourceClusters[Cluster].ParentGroup>=0){ continue; }
            Slots++;
            Blocks+=Mesh.Layout.BlockCount(Cluster);
        }
        return (Slots,Blocks);
    }
    /// <summary>置いたページの塊を、置き場の枠と区画へ書き、塊の表の FirstIndex を置いた枠へ向ける。</summary>
    private static void WriteVirtualGeometryPages(ref VirtualGeometryMeshBuffers Mesh,List<int>Pages){
        var Vertices=(StageVirtualGeometryVertex*)Mesh.MappedVertices;
        var Indices=(uint*)Mesh.MappedIndices;
        var Table=(StageVirtualGeometryCluster*)Mesh.MappedClusters;
        Span<uint>SlotIndices=stackalloc uint[VirtualGeometrySlotLayout.IndicesPerSlot];
        Span<StageVirtualGeometryVertex>Block=stackalloc StageVirtualGeometryVertex[VirtualGeometrySlotLayout.VerticesPerBlock];
        foreach(var Page in Pages){
            foreach(var Cluster in Mesh.Residency!.PageClusterIndices(Page)){
                if(!Mesh.Residency.TryGetPlacement(Cluster,out var Slot,out var Blocks)){ throw new InvalidOperationException($"Cluster {Cluster} of a loaded page has no placement."); }
                for(var Number=0;Number<Blocks.Length;Number++){
                    Mesh.Layout.WriteBlock(Cluster,Number,Block);
                    Block.CopyTo(new Span<StageVirtualGeometryVertex>(Vertices+(long)Blocks[Number]*VirtualGeometrySlotLayout.VerticesPerBlock,VirtualGeometrySlotLayout.VerticesPerBlock));
                }
                Mesh.Layout.WriteIndices(Cluster,Blocks,SlotIndices);
                SlotIndices.CopyTo(new Span<uint>(Indices+(long)Slot*VirtualGeometrySlotLayout.IndicesPerSlot,VirtualGeometrySlotLayout.IndicesPerSlot));
                Table[Cluster]=Mesh.Layout.SlotCluster(Cluster,Slot);
            }
        }
    }
    /// <summary>
    /// このフレームの出し入れ: 読み戻しの輪から数フレーム前の知らせを読み、ページを置き・追い出し、準備済みの印をこのフレームの区画へ書く。
    /// その後、知らせを 0 に戻す命令を記録する（知らせの置き場は写し先の状態で終わる）。
    /// </summary>
    private void UpdateVirtualGeometryStreaming(ref VirtualGeometryMeshBuffers Mesh){
        var Residency=Mesh.Residency!;
        var Count=Mesh.Layout.ClusterCount;
        var Feedback=new uint[Count*2];
        if(Mesh.FeedbackReadback!.TryRead<uint>(this.FrameIndex,Feedback)){
            List<int>Used=[];
            List<(int Group,float Priority)>Requests=[];
            for(var Group=0;Group<Mesh.GroupCount;Group++){
                if(Feedback[Group]!=0){ Used.Add(Group); }
                if(Feedback[Count+Group]!=0){ Requests.Add((Group,BitConverter.UInt32BitsToSingle(Feedback[Count+Group]))); }
            }
            List<int>Loads=[];
            Residency.Update(++Mesh.StreamingFrame,[..Used],[..Requests],this.VirtualGeometryPagesPerFrame,Loads,this.VirtualGeometryStreamingReport);
            WriteVirtualGeometryPages(ref Mesh,Loads);
        }
        Residency.Ready.CopyTo(new Span<uint>(Mesh.MappedReady+(long)this.FrameIndex*Mesh.GroupCount*sizeof(uint),Mesh.GroupCount));
        var ToCopy=D3D12_RESOURCE_BARRIER.InitTransition(Mesh.Feedback,Mesh.FeedbackState,D3D12_RESOURCE_STATE_COPY_DEST);
        this.CommandList->ResourceBarrier(1,&ToCopy);
        this.CommandList->CopyBufferRegion(Mesh.Feedback,0,Mesh.FeedbackZeros,0,(ulong)Count*2*sizeof(uint));
        var ToWrite=D3D12_RESOURCE_BARRIER.InitTransition(Mesh.Feedback,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        this.CommandList->ResourceBarrier(1,&ToWrite);
        Mesh.FeedbackState=D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }
    /// <summary>選択の後: 知らせを読み戻しの輪のこのフレームの枠へ写す。</summary>
    private void CopyVirtualGeometryFeedback(ref VirtualGeometryMeshBuffers Mesh){
        var ToSource=D3D12_RESOURCE_BARRIER.InitTransition(Mesh.Feedback,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_COPY_SOURCE);
        this.CommandList->ResourceBarrier(1,&ToSource);
        Mesh.FeedbackState=D3D12_RESOURCE_STATE_COPY_SOURCE;
        var Readback=Mesh.FeedbackReadback!;
        this.CommandList->CopyBufferRegion(Readback.Resource,Readback.SlotOffset(this.FrameIndex),Mesh.Feedback,0,Readback.SlotBytes);
        Readback.MarkWritten(this.FrameIndex);
    }
    /// <summary>このフレームの準備済みの印の GPU の位置。</summary>
    private ulong VirtualGeometryReadyAddress(in VirtualGeometryMeshBuffers Mesh)=>Mesh.Ready->GetGPUVirtualAddress()+(ulong)this.FrameIndex*(ulong)Mesh.GroupCount*sizeof(uint);
    /// <summary>試験用: メッシュ <paramref name="MeshIndex"/> で置いているページの数と、ページの総数。</summary>
    internal (int Resident,int Total)VirtualGeometryResidentPagesForTesting(int MeshIndex){
        var Residency=this.VirtualGeometryMeshes[MeshIndex].Residency;
        return Residency is null?(0,this.VirtualGeometryMeshes[MeshIndex].GroupCount):(Residency.ResidentPageCount,Residency.GroupCount);
    }
}
