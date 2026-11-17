using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Streaming;
/// <summary>
/// 仮想ジオメトリの塊を置き場へ置く形（ADR-0100 決定 1・3、M0131 単位 3）。塊ごとに、その塊が使う頂点だけを集めた頂点の並びと、それを指す索引を持つ。
/// 置き場は 2 つの決まった大きさの単位でできている。
/// - 索引の枠: 1 つの塊の索引 <see cref="IndicesPerSlot"/> 個。枠 s に置いた塊の表の FirstIndex は s × <see cref="IndicesPerSlot"/>。
/// - 頂点の区画: 頂点 <see cref="VerticesPerBlock"/> 個。塊は自分の頂点の数に要るだけの区画を使い、区画は置き場のどこにあってもよい
///   （索引が置き場の頂点の位置そのものを指すので、続いている必要がない）。塊の中の i 番目の頂点は、i / 区画の大きさ 番目の区画の i % 区画の大きさ 番目に置く。
/// 頂点シェーダーは「塊の FirstIndex から索引を引き、索引で頂点を引く」ので、置き場の形に変えても読み方は変わらない（取り決めを変えない）。
/// 使わない索引の残りは、塊の最後の索引で埋める（頂点シェーダーは索引の数より後ろを最後の角として描き、面積 0 の三角形になる）。
/// 頂点を枠にまとめずに区画へ分けるのは、頂点の数が塊ごとに大きく違うため（65,024 三角形の球で平均 92、最大 199。最大にそろえると全部を置いて元の 3.6 倍になった）。
/// </summary>
public sealed class VirtualGeometrySlotLayout {
    /// <summary>索引の枠 1 つの索引の数（塊の三角形の上限 × 3）。</summary>
    public const int IndicesPerSlot=StageVirtualGeometryCreateInfo.MaxTrianglesPerCluster*3;
    /// <summary>頂点の区画 1 つの頂点の数。</summary>
    public const int VerticesPerBlock=32;
    private readonly StageVirtualGeometryCluster[]Clusters;
    private readonly StageVirtualGeometryVertex[]LocalVertices;
    private readonly int[]FirstLocalVertex;
    private readonly int[]LocalVertexCount;
    private readonly uint[]LocalIndices;
    private VirtualGeometrySlotLayout(StageVirtualGeometryCluster[]Clusters,StageVirtualGeometryVertex[]LocalVertices,int[]FirstLocalVertex,int[]LocalVertexCount,uint[]LocalIndices){
        this.Clusters=Clusters;
        this.LocalVertices=LocalVertices;
        this.FirstLocalVertex=FirstLocalVertex;
        this.LocalVertexCount=LocalVertexCount;
        this.LocalIndices=LocalIndices;
        this.TotalBlockCount=0;
        for(var Cluster=0;Cluster<Clusters.Length;Cluster++){ this.TotalBlockCount+=this.BlockCount(Cluster); }
    }
    public int ClusterCount=>this.Clusters.Length;
    /// <summary>全部の塊を置くのに要る頂点の区画の数。</summary>
    public int TotalBlockCount { get; }
    /// <summary>塊 <paramref name="Cluster"/> が使う頂点の数。</summary>
    public int VertexCount(int Cluster)=>this.LocalVertexCount[Cluster];
    /// <summary>塊 <paramref name="Cluster"/> が使う頂点の区画の数。</summary>
    public int BlockCount(int Cluster)=>(this.LocalVertexCount[Cluster]+VerticesPerBlock-1)/VerticesPerBlock;
    /// <summary>メッシュ全体の頂点と索引から、塊ごとの頂点と索引を集める。塊の中の頂点の順は、索引に初めて出てきた順。</summary>
    public static VirtualGeometrySlotLayout Create(StageVirtualGeometryCreateInfo Info){
        ArgumentNullException.ThrowIfNull(Info);
        var FirstLocalVertex=new int[Info.Clusters.Length];
        var LocalVertexCount=new int[Info.Clusters.Length];
        var LocalIndices=new uint[Info.Indices.Length];
        List<StageVirtualGeometryVertex>LocalVertices=[];
        var Local=new Dictionary<uint,uint>();
        for(var Index=0;Index<Info.Clusters.Length;Index++){
            var Cluster=Info.Clusters[Index];
            if(Cluster.IndexCount==0||Cluster.IndexCount>IndicesPerSlot||(ulong)Cluster.FirstIndex+Cluster.IndexCount>(ulong)Info.Indices.Length){
                throw new ArgumentException($"Cluster {Index} refers outside its mesh or holds more than {IndicesPerSlot} indices.",nameof(Info));
            }
            Local.Clear();
            FirstLocalVertex[Index]=LocalVertices.Count;
            for(var Corner=Cluster.FirstIndex;Corner<Cluster.FirstIndex+Cluster.IndexCount;Corner++){
                var Vertex=Info.Indices[Corner];
                if(Vertex>=(uint)Info.Vertices.Length){ throw new ArgumentException($"Cluster {Index} refers outside the vertices.",nameof(Info)); }
                if(!Local.TryGetValue(Vertex,out var Number)){
                    Number=(uint)Local.Count;
                    Local.Add(Vertex,Number);
                    LocalVertices.Add(Info.Vertices[Vertex]);
                }
                LocalIndices[Corner]=Number;
            }
            LocalVertexCount[Index]=Local.Count;
        }
        return new VirtualGeometrySlotLayout(Info.Clusters,[..LocalVertices],FirstLocalVertex,LocalVertexCount,LocalIndices);
    }
    /// <summary>塊 <paramref name="Cluster"/> の <paramref name="Block"/> 番目の区画の頂点（<see cref="VerticesPerBlock"/> 個）を書く。使わない頂点は 0。</summary>
    public void WriteBlock(int Cluster,int Block,Span<StageVirtualGeometryVertex>Destination){
        if(Destination.Length!=VerticesPerBlock){ throw new ArgumentException("The destination does not match the block size.",nameof(Destination)); }
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)Block,(uint)this.BlockCount(Cluster),nameof(Block));
        var First=Block*VerticesPerBlock;
        var Count=Math.Min(VerticesPerBlock,this.LocalVertexCount[Cluster]-First);
        this.LocalVertices.AsSpan(this.FirstLocalVertex[Cluster]+First,Count).CopyTo(Destination);
        Destination[Count..].Clear();
    }
    /// <summary>
    /// 塊 <paramref name="Cluster"/> の索引（<see cref="IndicesPerSlot"/> 個）を書く。<paramref name="Blocks"/> は、塊の区画をそれぞれ置き場のどの区画に置いたか
    /// （<see cref="BlockCount"/> 個）。索引は置き場の頂点の位置を指す。使わない索引は最後の索引で埋める。
    /// </summary>
    public void WriteIndices(int Cluster,ReadOnlySpan<int>Blocks,Span<uint>Destination){
        if(Destination.Length!=IndicesPerSlot){ throw new ArgumentException("The destination does not match the slot size.",nameof(Destination)); }
        if(Blocks.Length!=this.BlockCount(Cluster)){ throw new ArgumentException($"Cluster {Cluster} needs {this.BlockCount(Cluster)} blocks.",nameof(Blocks)); }
        var Source=this.Clusters[Cluster];
        var Local=this.LocalIndices.AsSpan((int)Source.FirstIndex,(int)Source.IndexCount);
        for(var Corner=0;Corner<Local.Length;Corner++){
            var Number=(int)Local[Corner];
            Destination[Corner]=checked((uint)(Blocks[Number/VerticesPerBlock]*VerticesPerBlock+Number%VerticesPerBlock));
        }
        Destination[Local.Length..].Fill(Destination[Local.Length-1]);
    }
    /// <summary>塊 <paramref name="Cluster"/> の索引を索引の枠 <paramref name="Slot"/> に置いたときの、塊の表の 1 件（FirstIndex だけが変わる）。</summary>
    public StageVirtualGeometryCluster SlotCluster(int Cluster,int Slot)=>this.Clusters[Cluster] with{ FirstIndex=checked((uint)(Slot*IndicesPerSlot)) };
}
