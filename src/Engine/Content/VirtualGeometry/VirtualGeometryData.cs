using System.Numerics;
namespace CSharpGameEngine.Content.VirtualGeometry;
/// <summary>仮想ジオメトリの頂点 1 つ(メッシュの中の座標)。</summary>
public readonly record struct VirtualGeometryVertex(Vector3 Position,Vector3 Normal,Vector4 Tangent,Vector2 Uv);
/// <summary>
/// 塊 1 つ(128 三角形以下)。<see cref="FirstIndex"/> から <see cref="IndexCount"/> 個の頂点の番号が三角形。
/// 選び方(ADR-0100 決定 3): 自分を作ったまとまり <see cref="Group"/> の誤差の画素数がしきい値以下で、自分を使って次の段を作ったまとまり
/// <see cref="ParentGroup"/> の誤差の画素数がしきい値より大きいとき(根の段なら親は無い)に描く。
/// </summary>
public readonly record struct VirtualGeometryCluster(int FirstIndex,int IndexCount,Vector3 BoundsCentre,float BoundsRadius,int Group,int ParentGroup);
/// <summary>
/// まとまり 1 つ。誤差と、誤差を画素に直すための包む球を持つ。段 0 は元のメッシュの塊を 1 つずつ包むまとまり(誤差は位置の格子の誤差)。
/// 親のまとまりの球は、自分の入力を作ったまとまりの球を包み、誤差は入力の誤差の 1.25 倍以上(独立判定の穴と重なりを防ぐ)。
/// </summary>
public readonly record struct VirtualGeometryGroup(Vector3 BoundsCentre,float BoundsRadius,float Error,int Level,int FirstCluster,int ClusterCount);
/// <summary>メッシュ 1 つの仮想ジオメトリ。<see cref="RootGroup"/> は最も粗い段のまとまり。</summary>
public sealed record VirtualGeometryMesh(VirtualGeometryVertex[]Vertices,uint[]Indices,VirtualGeometryCluster[]Clusters,VirtualGeometryGroup[]Groups,int RootGroup,int LevelCount) {
    /// <summary>
    /// 代理メッシュ: 最も粗い段(根のまとまり)の塊の三角形(<see cref="Vertices"/> の番号)。細かさの要らない処理(Lumen のカード、DDGI、距離場、ピッキング)に渡す。
    /// </summary>
    public uint[]ProxyIndices(){
        List<uint>Result=[];
        foreach(var Cluster in this.Clusters){
            if(Cluster.ParentGroup>=0){ continue; }
            for(var Index=0;Index<Cluster.IndexCount;Index++){ Result.Add(this.Indices[Cluster.FirstIndex+Index]); }
        }
        return [..Result];
    }
    /// <summary>
    /// まとまりごとの三角形の大きさ（メートル）。自分の塊の三角形の辺の長さの平均と、自分の入力を作った子のまとまりの値の大きいほう。
    /// 親へ行くほど大きくなる（減らない）ので、「三角形が下限より小さければ十分」という選び方も、誤差と同じく割れ目を作らない（ADR-0100、M0130 単位 6）。
    /// 成果物には書かず、読み込むときに塊から求める。
    /// </summary>
    public float[]GroupTriangleSizes(){
        var Sums=new float[this.Groups.Length];
        var Counts=new int[this.Groups.Length];
        foreach(var Cluster in this.Clusters){
            for(var Corner=0;Corner<Cluster.IndexCount;Corner+=3){
                var A=this.Vertices[this.Indices[Cluster.FirstIndex+Corner]].Position;
                var B=this.Vertices[this.Indices[Cluster.FirstIndex+Corner+1]].Position;
                var C=this.Vertices[this.Indices[Cluster.FirstIndex+Corner+2]].Position;
                Sums[Cluster.Group]+=Vector3.Distance(A,B)+Vector3.Distance(B,C)+Vector3.Distance(C,A);
                Counts[Cluster.Group]+=3;
            }
        }
        var Sizes=new float[this.Groups.Length];
        for(var Group=0;Group<Sizes.Length;Group++){ Sizes[Group]=Counts[Group]==0?0f:Sums[Group]/Counts[Group]; }
        // 段の低い順に、子の値を親へ渡す（親の段は子より必ず高い）。
        foreach(var Group in Enumerable.Range(0,this.Groups.Length).OrderBy(Group=>this.Groups[Group].Level)){
            foreach(var Cluster in this.Clusters){
                if(Cluster.Group==Group&&Cluster.ParentGroup>=0){ Sizes[Cluster.ParentGroup]=MathF.Max(Sizes[Cluster.ParentGroup],Sizes[Group]); }
            }
        }
        return Sizes;
    }
}
