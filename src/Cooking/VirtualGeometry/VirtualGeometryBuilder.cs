using System.Numerics;
using CSharpGameEngine.Content.VirtualGeometry;
namespace CSharpGameEngine.Cooking.VirtualGeometry;
/// <summary>仮想ジオメトリのビルドの設定(既定値は Omni と同じ)。</summary>
public sealed record VirtualGeometryBuildSettings {
    /// <summary>塊 1 つの三角形の上限(1〜128)。</summary>
    public int MaxTrianglesPerCluster { get;init; }=128;
    /// <summary>まとまり 1 つの塊の数の目安(2〜8)。小さすぎるまとまりは隣へ合わせるので、最大でこの 2 倍になる。</summary>
    public int GroupSize { get;init; }=8;
    /// <summary>1 段で残す三角形の割合の目標。</summary>
    public float SimplificationRatio { get;init; }=0.5f;
    /// <summary>位置をそろえる格子の細かさ(ビット、0 ならそろえない)。わずかにずれた継ぎ目をつなぎ、その誤差がいちばん細かい段の誤差になる。</summary>
    public int PositionQuantizationBits { get;init; }=16;
    /// <summary>1 段の出力が入力のこの割合(%)を超えたら、そこで段を打ち切って残りを 1 つの根にする(これ以上積んでも粗くならない)。</summary>
    public int MinimumLevelReductionPercent { get;init; }=92;
}
/// <summary>
/// 仮想ジオメトリのビルド(ADR-0100 決定 3、M0130 単位 2)。Omni の VirtualGeometryBuilder の手順を、このエンジンの 1 材質のメッシュ向けに書き直した。
/// <list type="number">
/// <item>位置を、メッシュ全体の格子にそろえる。</item>
/// <item>三角形を、重心の最も広い軸の中央値で再帰的に二分割し、上限以下の塊にする(段 0)。塊 1 つを 1 つのまとまりで包む。</item>
/// <item>段ごとに、塊を空間の近い順に並べ、同じ位置を多く共有する塊どうしをまとまりにする。まとまりの外周(まとまりの中で 1 枚または 3 枚以上の三角形が使う辺)
/// を固定して目標の割合まで減らし、また塊に分ける。前の段の外周は、次の段では別のまとまりの内側になるので減らせる。</item>
/// <item>親の誤差は「子の誤差 + 簡略化の誤差」と「子の誤差 × 1.25」の大きいほう。親の包む球は、入力を作ったまとまりの球を包む。</item>
/// <item>1 段の削減が小さくなったら、残りの塊を簡略化せずに 1 つの根のまとまりにする。</item>
/// </list>
/// 同じ入力なら同じ結果になる(まとまりは並行に処理するが、結果は順番の決まった配列に入れる)。
/// </summary>
public static class VirtualGeometryBuilder {
    /// <summary>親の誤差が、子の誤差に対して最低限増える倍率。ヒステリシス 0.1 までの独立判定の整合に要る 1.223 より大きく取る(Omni と同じ)。</summary>
    public const float MinimumErrorGrowth=1.25f;
    public static VirtualGeometryMesh Build(ReadOnlySpan<VirtualGeometryVertex>Vertices,ReadOnlySpan<uint>Indices,VirtualGeometryBuildSettings?Settings=null){
        Settings??=new VirtualGeometryBuildSettings();
        Validate(Vertices,Indices,Settings);
        var Quantized=Quantize(Vertices,Settings.PositionQuantizationBits,out var QuantizationError);
        var Work=new BuildState(Quantized,Settings);
        Work.BuildLeaves(Indices,QuantizationError);
        Work.BuildHierarchy();
        return Work.Result();
    }
    private static void Validate(ReadOnlySpan<VirtualGeometryVertex>Vertices,ReadOnlySpan<uint>Indices,VirtualGeometryBuildSettings Settings){
        if(Settings.MaxTrianglesPerCluster is<1 or>128){ throw new ArgumentOutOfRangeException(nameof(Settings),"A cluster holds 1 to 128 triangles."); }
        if(Settings.GroupSize is<2 or>8){ throw new ArgumentOutOfRangeException(nameof(Settings),"A group holds 2 to 8 clusters."); }
        if(!(Settings.SimplificationRatio>0f&&Settings.SimplificationRatio<1f)){ throw new ArgumentOutOfRangeException(nameof(Settings),"The simplification ratio lies strictly between 0 and 1."); }
        if(Settings.PositionQuantizationBits is<0 or>24){ throw new ArgumentOutOfRangeException(nameof(Settings),"Position quantization uses 0 to 24 bits."); }
        if(Indices.Length==0||Indices.Length%3!=0){ throw new ArgumentException("Indices must hold complete triangles.",nameof(Indices)); }
        foreach(var Index in Indices){
            if(Index>=Vertices.Length){ throw new ArgumentException("An index refers to a vertex outside the vertex array.",nameof(Indices)); }
        }
        foreach(var Vertex in Vertices){
            if(!float.IsFinite(Vertex.Position.X)||!float.IsFinite(Vertex.Position.Y)||!float.IsFinite(Vertex.Position.Z)){ throw new ArgumentException("Vertex positions must be finite.",nameof(Vertices)); }
        }
    }
    /// <summary>位置を、包む箱を 2^bits − 1 段に分けた格子へそろえる。最大のずれを返す。</summary>
    private static VirtualGeometryVertex[]Quantize(ReadOnlySpan<VirtualGeometryVertex>Vertices,int Bits,out float MaximumError){
        var Result=Vertices.ToArray();
        MaximumError=0f;
        if(Bits==0||Result.Length==0){ return Result; }
        var (Minimum,Maximum)=Bounds(Result);
        var Size=Maximum-Minimum;
        var Levels=(float)((1<<Bits)-1);
        for(var Index=0;Index<Result.Length;Index++){
            var P=Result[Index].Position;
            var Q=new Vector3(Axis(P.X,Minimum.X,Size.X,Levels),Axis(P.Y,Minimum.Y,Size.Y,Levels),Axis(P.Z,Minimum.Z,Size.Z,Levels));
            MaximumError=MathF.Max(MaximumError,Vector3.Distance(P,Q));
            Result[Index]=Result[Index] with{ Position=QuadricSimplifier.CanonicalPosition(Q) };
        }
        return Result;
        static float Axis(float Value,float Minimum,float Size,float Levels)=>Size<=1e-20f?Minimum:Minimum+MathF.Round(Math.Clamp((Value-Minimum)/Size,0f,1f)*Levels)/Levels*Size;
    }
    private static (Vector3 Minimum,Vector3 Maximum)Bounds(ReadOnlySpan<VirtualGeometryVertex>Vertices){
        var Minimum=new Vector3(float.PositiveInfinity);
        var Maximum=new Vector3(float.NegativeInfinity);
        foreach(var Vertex in Vertices){
            Minimum=Vector3.Min(Minimum,Vertex.Position);
            Maximum=Vector3.Max(Maximum,Vertex.Position);
        }
        return (Minimum,Maximum);
    }
    /// <summary>まとまり 1 つの簡略化の結果。<see cref="Sizes"/> は出力の塊ごとの三角形の数。</summary>
    private readonly record struct GroupResult(uint[]Indices,int[]Sizes,float Error,bool Progress);
    private sealed class BuildState(VirtualGeometryVertex[]Vertices,VirtualGeometryBuildSettings Settings) {
        private readonly SimplifierVertex[]SimplifierVertices=[..Vertices.Select(static Vertex=>new SimplifierVertex(Vertex.Position,Vertex.Normal,Vertex.Uv))];
        private readonly List<uint>Indices=[];
        private readonly List<VirtualGeometryCluster>Clusters=[];
        private readonly List<VirtualGeometryGroup>Groups=[];
        private readonly Dictionary<Vector3,int>PositionIds=[];
        private Vector3 Minimum;
        private Vector3 Size;
        private int RootGroup=-1;
        private int Levels=1;
        public void BuildLeaves(ReadOnlySpan<uint>Source,float QuantizationError){
            (this.Minimum,var Maximum)=VirtualGeometryBuilder.Bounds(Vertices);
            this.Size=Maximum-this.Minimum;
            List<uint>Triangles=[];
            for(var Corner=0;Corner<Source.Length;Corner+=3){
                uint A=Source[Corner],B=Source[Corner+1],C=Source[Corner+2];
                // 格子にそろえて位置が重なった三角形は何も描かないので外す。
                if(this.PositionId(A)==this.PositionId(B)||this.PositionId(B)==this.PositionId(C)||this.PositionId(A)==this.PositionId(C)){ continue; }
                Triangles.Add(A);Triangles.Add(B);Triangles.Add(C);
            }
            if(Triangles.Count==0){ throw new ArgumentException("Every triangle collapses on the position grid.",nameof(Source)); }
            var Sorted=this.SortByBisection([..Triangles],out var Sizes);
            var Start=0;
            foreach(var Size in Sizes){
                var Group=this.Groups.Count;
                var Cluster=this.AppendCluster(Sorted.AsSpan(Start*3,Size*3),Group);
                var Bounds=this.Clusters[Cluster];
                this.Groups.Add(new VirtualGeometryGroup(Bounds.BoundsCentre,Bounds.BoundsRadius,QuantizationError,0,Cluster,1));
                Start+=Size;
            }
        }
        public void BuildHierarchy(){
            List<int>Play=[..Enumerable.Range(0,this.Clusters.Count)];
            var Level=1;
            while(Play.Count>1){
                if(Level>64){ throw new InvalidOperationException("The hierarchy did not converge within 64 levels."); }
                var Groups=this.PartitionByAdjacency(this.OrderByMorton(Play));
                var OnlyGroup=Groups.Count==1;
                var Results=new GroupResult[Groups.Count];
                Parallel.For(0,Groups.Count,Index=>Results[Index]=this.SimplifyGroup(Groups[Index],OnlyGroup,Simplify:true));
                List<int>Next=[];
                var Progress=false;
                long TrianglesIn=0,TrianglesOut=0;
                foreach(var Cluster in Play){ TrianglesIn+=this.Clusters[Cluster].IndexCount/3; }
                for(var Index=0;Index<Groups.Count;Index++){
                    Progress|=Results[Index].Progress;
                    var Group=this.AppendGroup(Level,Groups[Index],Results[Index]);
                    var Created=this.Groups[Group];
                    for(var Cluster=0;Cluster<Created.ClusterCount;Cluster++){ Next.Add(Created.FirstCluster+Cluster); }
                }
                foreach(var Cluster in Next){ TrianglesOut+=this.Clusters[Cluster].IndexCount/3; }
                if(!Progress||TrianglesOut*100>TrianglesIn*Settings.MinimumLevelReductionPercent){
                    // これ以上積んでも粗くならない。この段の出力をすべて、簡略化しない 1 つの根のまとまりにする。
                    var All=Next.ToArray();
                    this.RootGroup=this.AppendGroup(Level+1,All,this.SimplifyGroup(All,OnlyGroup:true,Simplify:false));
                    this.Levels=Level+2;
                    return;
                }
                Play=Next;
                Level++;
            }
            this.RootGroup=this.Clusters[Play[0]].Group;
            this.Levels=Level;
        }
        public VirtualGeometryMesh Result()=>new(Vertices,[..this.Indices],[..this.Clusters],[..this.Groups],this.RootGroup,this.Levels);
        private int PositionId(uint Vertex){
            var Position=Vertices[Vertex].Position;
            if(!this.PositionIds.TryGetValue(Position,out var Id)){
                Id=this.PositionIds.Count;
                this.PositionIds.Add(Position,Id);
            }
            return Id;
        }
        /// <summary>まとまりの三角形をつなげ、外周を固定して減らす。減らせなければ入力の塊をそのまま写す。</summary>
        private GroupResult SimplifyGroup(int[]Inputs,bool OnlyGroup,bool Simplify){
            List<uint>Combined=[];
            List<int>InputSizes=[];
            var ChildError=0f;
            foreach(var Cluster in Inputs){
                var Source=this.Clusters[Cluster];
                ChildError=MathF.Max(ChildError,this.Groups[Source.Group].Error);
                for(var Index=0;Index<Source.IndexCount;Index++){ Combined.Add(this.Indices[Source.FirstIndex+Index]); }
                InputSizes.Add(Source.IndexCount/3);
            }
            var Representation=Combined.ToArray();
            var Sizes=InputSizes.ToArray();
            var Error=ChildError;
            var Progress=false;
            if(Simplify&&Representation.Length>3){
                // 最後に残った 1 つのまとまりは、隣が無いので外周を固定しない(閉じていない縁は簡略化が縁の面で守る)。
                var Locks=OnlyGroup?new HashSet<uint>():this.BoundaryLocks(Representation);
                var Source=Representation.Length/3;
                var Target=Math.Max(1,(int)MathF.Floor(Source*Settings.SimplificationRatio));
                var Simplified=QuadricSimplifier.Simplify(this.SimplifierVertices,Representation,Locks,Target);
                if(Simplified.Indices.Length>0&&Simplified.Indices.Length<Representation.Length){
                    Representation=this.SortByBisection(Simplified.Indices,out Sizes);
                    Error=ChildError+Simplified.Error;
                    Progress=true;
                }
            }
            return new GroupResult(Representation,Sizes,MathF.Max(Error,ChildError*MinimumErrorGrowth),Progress);
        }
        /// <summary>まとまりの外周の頂点。位置で見て、まとまりの中の 2 枚の三角形が共有する辺は内側(法線や UV の継ぎ目を含む)。それ以外の辺の頂点を固定する。</summary>
        private HashSet<uint>BoundaryLocks(uint[]Representation){
            var EdgeUse=new Dictionary<(int,int),int>();
            for(var Corner=0;Corner<Representation.Length;Corner+=3){
                for(var Side=0;Side<3;Side++){
                    int A=this.PositionIdReadOnly(Representation[Corner+Side]),B=this.PositionIdReadOnly(Representation[Corner+(Side+1)%3]);
                    var Key=A<B?(A,B):(B,A);
                    EdgeUse[Key]=EdgeUse.GetValueOrDefault(Key)+1;
                }
            }
            HashSet<int>LockedPositions=[];
            foreach(var (Edge,Count) in EdgeUse){
                if(Count==2){ continue; }
                LockedPositions.Add(Edge.Item1);
                LockedPositions.Add(Edge.Item2);
            }
            HashSet<uint>Locks=[];
            foreach(var Vertex in Representation){
                if(LockedPositions.Contains(this.PositionIdReadOnly(Vertex))){ Locks.Add(Vertex); }
            }
            return Locks;
        }
        /// <summary>並行に呼ばれる間は位置の表を書き換えない(段 0 で全頂点の位置を登録済み)。</summary>
        private int PositionIdReadOnly(uint Vertex)=>this.PositionIds[Vertices[Vertex].Position];
        /// <summary>まとまりを足す。出力の塊は新しいまとまりが作ったもので、入力の塊の親は新しいまとまり。球は、入力を作ったまとまりの球を包む。</summary>
        private int AppendGroup(int Level,int[]Inputs,GroupResult Result){
            var Group=this.Groups.Count;
            var FirstCluster=this.Clusters.Count;
            var Start=0;
            foreach(var Size in Result.Sizes){
                this.AppendCluster(Result.Indices.AsSpan(Start*3,Size*3),Group);
                Start+=Size;
            }
            var Producers=new SortedSet<int>();
            foreach(var Cluster in Inputs){
                Producers.Add(this.Clusters[Cluster].Group);
                this.Clusters[Cluster]=this.Clusters[Cluster] with{ ParentGroup=Group };
            }
            var (Centre,Radius)=this.EnclosingSphere(Producers);
            this.Groups.Add(new VirtualGeometryGroup(Centre,Radius,Result.Error,Level,FirstCluster,this.Clusters.Count-FirstCluster));
            return Group;
        }
        private int AppendCluster(ReadOnlySpan<uint>Triangles,int Group){
            var FirstIndex=this.Indices.Count;
            var Minimum=new Vector3(float.PositiveInfinity);
            var Maximum=new Vector3(float.NegativeInfinity);
            foreach(var Vertex in Triangles){
                this.Indices.Add(Vertex);
                Minimum=Vector3.Min(Minimum,Vertices[Vertex].Position);
                Maximum=Vector3.Max(Maximum,Vertices[Vertex].Position);
            }
            var Centre=(Minimum+Maximum)*0.5f;
            var Radius=0f;
            foreach(var Vertex in Triangles){ Radius=MathF.Max(Radius,Vector3.Distance(Centre,Vertices[Vertex].Position)); }
            this.Clusters.Add(new VirtualGeometryCluster(FirstIndex,Triangles.Length,Centre,Radius,Group,-1));
            return this.Clusters.Count-1;
        }
        /// <summary>まとまりたちの球をすべて包む球(包む箱の中心と、そこから各球の外側までの最大の距離)。</summary>
        private (Vector3 Centre,float Radius)EnclosingSphere(IEnumerable<int>Members){
            var Minimum=new Vector3(float.PositiveInfinity);
            var Maximum=new Vector3(float.NegativeInfinity);
            foreach(var Member in Members){
                var Group=this.Groups[Member];
                Minimum=Vector3.Min(Minimum,Group.BoundsCentre-new Vector3(Group.BoundsRadius));
                Maximum=Vector3.Max(Maximum,Group.BoundsCentre+new Vector3(Group.BoundsRadius));
            }
            var Centre=(Minimum+Maximum)*0.5f;
            var Radius=0f;
            foreach(var Member in Members){
                var Group=this.Groups[Member];
                Radius=MathF.Max(Radius,Vector3.Distance(Centre,Group.BoundsCentre)+Group.BoundsRadius);
            }
            return (Centre,Radius);
        }
        /// <summary>三角形を、重心の最も広い軸の中央値で再帰的に二分割し、上限以下の塊ごとに並べる。塊ごとの三角形の数を返す。</summary>
        private uint[]SortByBisection(uint[]Source,out int[]Sizes){
            var Count=Source.Length/3;
            var Order=new int[Count];
            var Centroids=new Vector3[Count];
            for(var Triangle=0;Triangle<Count;Triangle++){
                Order[Triangle]=Triangle;
                Centroids[Triangle]=(Vertices[Source[Triangle*3]].Position+Vertices[Source[Triangle*3+1]].Position+Vertices[Source[Triangle*3+2]].Position)/3f;
            }
            List<int[]>Chunks=[];
            Bisect(Order,0,Count,Math.Max(1,(Count+Settings.MaxTrianglesPerCluster-1)/Settings.MaxTrianglesPerCluster),Centroids,Chunks);
            var Result=new uint[Source.Length];
            Sizes=new int[Chunks.Count];
            var Write=0;
            for(var Chunk=0;Chunk<Chunks.Count;Chunk++){
                Sizes[Chunk]=Chunks[Chunk].Length;
                foreach(var Triangle in Chunks[Chunk]){
                    Result[Write++]=Source[Triangle*3];
                    Result[Write++]=Source[Triangle*3+1];
                    Result[Write++]=Source[Triangle*3+2];
                }
            }
            return Result;
        }
        private static void Bisect(int[]Order,int First,int Count,int Leaves,Vector3[]Centroids,List<int[]>Output){
            if(Leaves<=1||Count<=1){
                Output.Add(Order.AsSpan(First,Count).ToArray());
                return;
            }
            var Minimum=new Vector3(float.PositiveInfinity);
            var Maximum=new Vector3(float.NegativeInfinity);
            for(var Index=0;Index<Count;Index++){
                var C=Centroids[Order[First+Index]];
                Minimum=Vector3.Min(Minimum,C);
                Maximum=Vector3.Max(Maximum,C);
            }
            var Extent=Maximum-Minimum;
            var Axis=Extent.X>=Extent.Y&&Extent.X>=Extent.Z?0:Extent.Y>=Extent.Z?1:2;
            Array.Sort(Order,First,Count,Comparer<int>.Create((A,B)=>{
                var Result=Centroids[A][Axis].CompareTo(Centroids[B][Axis]);
                return Result!=0?Result:A.CompareTo(B);
            }));
            var LeftLeaves=Leaves/2;
            var LeftCount=Math.Clamp((int)(((long)Count*LeftLeaves*2+Leaves)/(2L*Leaves)),1,Count-1);
            Bisect(Order,First,LeftCount,LeftLeaves,Centroids,Output);
            Bisect(Order,First+LeftCount,Count-LeftCount,Leaves-LeftLeaves,Centroids,Output);
        }
        private int[]OrderByMorton(List<int>Play){
            var Keyed=new (uint Morton,int Cluster)[Play.Count];
            for(var Index=0;Index<Keyed.Length;Index++){ Keyed[Index]=(this.Morton(this.Clusters[Play[Index]].BoundsCentre),Play[Index]); }
            Array.Sort(Keyed,static (Left,Right)=>{
                var Result=Left.Morton.CompareTo(Right.Morton);
                return Result!=0?Result:Left.Cluster.CompareTo(Right.Cluster);
            });
            return [..Keyed.Select(static Entry=>Entry.Cluster)];
        }
        private uint Morton(Vector3 Point){
            var Unit=Vector3.Clamp((Point-this.Minimum)/Vector3.Max(this.Size,new Vector3(1e-20f)),Vector3.Zero,Vector3.One);
            return Expand((uint)(Unit.X*1023f))|Expand((uint)(Unit.Y*1023f))<<1|Expand((uint)(Unit.Z*1023f))<<2;
            static uint Expand(uint Value){
                Value=(Value*0x00010001u)&0xFF0000FFu;
                Value=(Value*0x00000101u)&0x0F00F00Fu;
                Value=(Value*0x00000011u)&0xC30C30C3u;
                return (Value*0x00000005u)&0x49249249u;
            }
        }
        /// <summary>
        /// 同じ位置を共有する塊どうしを、共有する数の多い順にまとまりの大きさまで束ねる(Omni と同じ)。離れた部品の塊だけは空間の近い順の塊で埋める。
        /// 小さすぎるまとまり(大きさの半分未満)は、共有の最も多い隣のまとまりへ合わせる(合わせた後も大きさの 2 倍まで)。
        /// </summary>
        private List<int[]>PartitionByAdjacency(int[]Order){
            var Count=Order.Length;
            var Fanout=Settings.GroupSize;
            if(Count<=Fanout){ return [(int[])Order.Clone()]; }
            List<(int Position,int Rank)>Pairs=[];
            for(var Rank=0;Rank<Count;Rank++){
                var Cluster=this.Clusters[Order[Rank]];
                HashSet<int>Seen=[];
                for(var Index=0;Index<Cluster.IndexCount;Index++){
                    var Position=this.PositionIdReadOnly(this.Indices[Cluster.FirstIndex+Index]);
                    if(Seen.Add(Position)){ Pairs.Add((Position,Rank)); }
                }
            }
            Pairs.Sort();
            var Neighbours=new Dictionary<int,int>[Count];
            for(var Rank=0;Rank<Count;Rank++){ Neighbours[Rank]=[]; }
            for(var Start=0;Start<Pairs.Count;){
                var End=Start+1;
                while(End<Pairs.Count&&Pairs[End].Position==Pairs[Start].Position){ End++; }
                for(var First=Start;First<End;First++){
                    for(var Second=First+1;Second<End;Second++){
                        int A=Pairs[First].Rank,B=Pairs[Second].Rank;
                        Neighbours[A][B]=Neighbours[A].GetValueOrDefault(B)+1;
                        Neighbours[B][A]=Neighbours[B].GetValueOrDefault(A)+1;
                    }
                }
                Start=End;
            }
            var Taken=new bool[Count];
            List<List<int>>RankGroups=[];
            var Candidates=new Dictionary<int,int>();
            for(var Seed=0;Seed<Count;Seed++){
                if(Taken[Seed]){ continue; }
                List<int>Members=[Seed];
                Taken[Seed]=true;
                Candidates.Clear();
                var Isolated=Neighbours[Seed].Count==0;
                Absorb(Neighbours[Seed],Taken,Candidates);
                var Fill=Seed+1;
                while(Members.Count<Fanout){
                    int Best=-1,BestWeight=0;
                    foreach(var (Candidate,Weight) in Candidates){
                        if(Taken[Candidate]){ continue; }
                        if(Weight>BestWeight||(Weight==BestWeight&&Best>=0&&Candidate<Best)){
                            Best=Candidate;
                            BestWeight=Weight;
                        }
                    }
                    if(Best<0){
                        if(!Isolated){ break; }
                        while(Fill<Count&&Taken[Fill]){ Fill++; }
                        if(Fill>=Count){ break; }
                        Best=Fill;
                    }
                    Members.Add(Best);
                    Taken[Best]=true;
                    Candidates.Remove(Best);
                    Absorb(Neighbours[Best],Taken,Candidates);
                }
                RankGroups.Add(Members);
            }
            var Minimum=Math.Max(2,Fanout/2);
            var GroupOfRank=new int[Count];
            for(var Group=0;Group<RankGroups.Count;Group++){
                foreach(var Rank in RankGroups[Group]){ GroupOfRank[Rank]=Group; }
            }
            var BySize=Enumerable.Range(0,RankGroups.Count).OrderBy(Group=>RankGroups[Group].Count).ThenBy(static Group=>Group).ToArray();
            foreach(var Group in BySize){
                if(RankGroups[Group].Count==0||RankGroups[Group].Count>=Minimum){ continue; }
                var WeightToGroup=new Dictionary<int,int>();
                foreach(var Rank in RankGroups[Group]){
                    foreach(var (Other,Weight) in Neighbours[Rank]){
                        var OtherGroup=GroupOfRank[Other];
                        if(OtherGroup==Group||RankGroups[OtherGroup].Count==0){ continue; }
                        WeightToGroup[OtherGroup]=WeightToGroup.GetValueOrDefault(OtherGroup)+Weight;
                    }
                }
                int Target=-1,TargetWeight=0;
                foreach(var (Other,Weight) in WeightToGroup){
                    if(RankGroups[Other].Count+RankGroups[Group].Count>Fanout*2){ continue; }
                    if(Weight>TargetWeight||(Weight==TargetWeight&&Other<Target)){
                        Target=Other;
                        TargetWeight=Weight;
                    }
                }
                if(Target<0){ continue; }
                foreach(var Rank in RankGroups[Group]){
                    GroupOfRank[Rank]=Target;
                    RankGroups[Target].Add(Rank);
                }
                RankGroups[Group].Clear();
            }
            List<int[]>Result=[];
            foreach(var Members in RankGroups){
                if(Members.Count==0){ continue; }
                Members.Sort();
                Result.Add([..Members.Select(Rank=>Order[Rank])]);
            }
            return Result;
            static void Absorb(Dictionary<int,int>Weights,bool[]Taken,Dictionary<int,int>Candidates){
                foreach(var (Other,Weight) in Weights){
                    if(!Taken[Other]){ Candidates[Other]=Candidates.GetValueOrDefault(Other)+Weight; }
                }
            }
        }
    }
}
