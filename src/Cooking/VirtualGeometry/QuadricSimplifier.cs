using System.Numerics;
namespace CSharpGameEngine.Cooking.VirtualGeometry;
/// <summary>簡略化に渡す頂点 1 つ。同じ位置に法線や UV の違う頂点があってよい(継ぎ目)。</summary>
public readonly record struct SimplifierVertex(Vector3 Position,Vector3 Normal,Vector2 Uv);
/// <summary>簡略化の結果。<see cref="Indices"/> は入力の頂点の番号で、<see cref="Error"/> は位置の誤差の上限(位置と同じ単位)。</summary>
public sealed record SimplifyResult(uint[]Indices,float Error);
/// <summary>
/// 誤差の二次形式(QEM)による辺の縮約(ADR-0100 決定 3、M0130 単位 1)。仮想ジオメトリのビルドが、まとまりごとに呼ぶ。
/// <list type="bullet">
/// <item>辺の片側を残す縮約: 頂点 u を隣の頂点 v の位置へ寄せて消す。新しい位置を作らないので、固定した頂点は動かず、残る頂点の法線・UV は元の値のまま。</item>
/// <item>継ぎ目: 同じ位置の頂点を 1 つの「位置」として扱う。u の各頂点は、u と v を共に含む三角形の中の v の頂点へ移す。その移し先が 1 つに決まらない、
/// または決まらない頂点がある縮約は行わない(継ぎ目の向こうの法線や UV を変えないため)。</item>
/// <item>誤差: 寄せた先の位置での、元の面までの距離の二乗の和の平方根。元の面からの最大のずれを小さく見積もらない。</item>
/// <item>行わない縮約: 固定した位置を消すもの、面の向きが大きく変わるもの、形のつながりを壊すもの(リンク条件)、誤差の上限を超えるもの。</item>
/// <item>同じ入力なら同じ結果: 費用が同じなら位置の番号の小さいほうを先にする。</item>
/// </list>
/// </summary>
public static class QuadricSimplifier {
    /// <summary>法線の違いの重み(Omni の meshoptimizer の設定と同じ)。</summary>
    public const float NormalWeight=0.5f;
    /// <summary>UV の違いの重み(同上)。</summary>
    public const float UvWeight=0.1f;
    /// <summary>開いた縁を守る面の重み。縁が固定されていないときに、縁が内側へ縮まないようにする。</summary>
    private const float BoundaryWeight=10f;
    /// <summary>縮約の前後で、面の法線の向きがこれより変わる縮約は行わない(内積の下限)。</summary>
    private const float MinimumNormalAgreement=0.2f;
    /// <summary>
    /// 三角形の数を <paramref name="TargetTriangleCount"/> 以下まで減らす(行える縮約が無くなれば、そこで止める)。
    /// </summary>
    /// <param name="Vertices">頂点。</param>
    /// <param name="Indices">三角形の頂点の番号(3 つずつ)。</param>
    /// <param name="LockedVertices">動かさない頂点の番号。同じ位置の頂点もすべて動かさない。</param>
    /// <param name="TargetTriangleCount">目標の三角形の数(1 以上)。</param>
    /// <param name="MaximumError">誤差の上限。これを超える縮約は行わない。</param>
    public static SimplifyResult Simplify(ReadOnlySpan<SimplifierVertex>Vertices,ReadOnlySpan<uint>Indices,IReadOnlySet<uint>LockedVertices,int TargetTriangleCount,float MaximumError=float.MaxValue){
        ArgumentNullException.ThrowIfNull(LockedVertices);
        if(Indices.Length==0||Indices.Length%3!=0){ throw new ArgumentException("Indices must hold complete triangles.",nameof(Indices)); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TargetTriangleCount);
        if(float.IsNaN(MaximumError)||MaximumError<0f){ throw new ArgumentOutOfRangeException(nameof(MaximumError)); }
        foreach(var Index in Indices){
            if(Index>=Vertices.Length){ throw new ArgumentException("An index refers to a vertex outside the vertex array.",nameof(Indices)); }
        }
        var State=new SimplifierState(Vertices,Indices,LockedVertices);
        State.Run(TargetTriangleCount,MaximumError);
        return new SimplifyResult(State.Output(),State.Error);
    }
    /// <summary>位置を同じかどうか比べる形にする(-0 を 0 にする)。</summary>
    internal static Vector3 CanonicalPosition(Vector3 Position)=>new(Position.X==0f?0f:Position.X,Position.Y==0f?0f:Position.Y,Position.Z==0f?0f:Position.Z);
    /// <summary>対称な 4 × 4 の誤差の二次形式。p^T A p + 2 b·p + c を、面までの距離の二乗の和として持つ。</summary>
    private struct Quadric {
        public float A00,A01,A02,A11,A12,A22,B0,B1,B2,C;
        public static Quadric FromPlane(Vector3 Normal,float Distance,float Weight)=>new(){
            A00=Weight*Normal.X*Normal.X,A01=Weight*Normal.X*Normal.Y,A02=Weight*Normal.X*Normal.Z,
            A11=Weight*Normal.Y*Normal.Y,A12=Weight*Normal.Y*Normal.Z,A22=Weight*Normal.Z*Normal.Z,
            B0=Weight*Normal.X*Distance,B1=Weight*Normal.Y*Distance,B2=Weight*Normal.Z*Distance,C=Weight*Distance*Distance,
        };
        public void Add(in Quadric Other){
            this.A00+=Other.A00;this.A01+=Other.A01;this.A02+=Other.A02;this.A11+=Other.A11;this.A12+=Other.A12;this.A22+=Other.A22;
            this.B0+=Other.B0;this.B1+=Other.B1;this.B2+=Other.B2;this.C+=Other.C;
        }
        public readonly float Evaluate(Vector3 P)=>
            this.A00*P.X*P.X+2f*this.A01*P.X*P.Y+2f*this.A02*P.X*P.Z+this.A11*P.Y*P.Y+2f*this.A12*P.Y*P.Z+this.A22*P.Z*P.Z
            +2f*(this.B0*P.X+this.B1*P.Y+this.B2*P.Z)+this.C;
    }
    /// <summary>縮約の候補: 位置 <see cref="From"/> を位置 <see cref="To"/> へ寄せる。</summary>
    private readonly record struct Collapse(int From,int To);
    private sealed class CollapseOrder:IComparer<(float Cost,int From,int To)> {
        public static readonly CollapseOrder Instance=new();
        public int Compare((float Cost,int From,int To)Left,(float Cost,int From,int To)Right){
            var Result=Left.Cost.CompareTo(Right.Cost);
            if(Result!=0){ return Result; }
            Result=Left.From.CompareTo(Right.From);
            return Result!=0?Result:Left.To.CompareTo(Right.To);
        }
    }
    private sealed class SimplifierState {
        private readonly SimplifierVertex[]Vertices;
        private readonly int[]Corners;
        private readonly bool[]Alive;
        private readonly int[]PositionOf;
        private readonly Vector3[]Positions;
        private readonly Quadric[]Quadrics;
        private readonly bool[]Locked;
        private readonly bool[]Removed;
        private readonly List<int>[]Incident;
        private readonly PriorityQueue<Collapse,(float Cost,int From,int To)>Queue=new(CollapseOrder.Instance);
        private readonly Dictionary<int,int>WedgeMap=[];
        private readonly HashSet<int>Neighbours=[];
        private readonly HashSet<int>OtherNeighbours=[];
        private int AliveCount;
        public float Error { get;private set; }
        public SimplifierState(ReadOnlySpan<SimplifierVertex>Vertices,ReadOnlySpan<uint>Indices,IReadOnlySet<uint>LockedVertices){
            this.Vertices=Vertices.ToArray();
            var PositionIds=new Dictionary<Vector3,int>();
            var PositionOf=new int[Vertices.Length];
            Array.Fill(PositionOf,-1);
            List<Vector3>Positions=[];
            foreach(var Index in Indices){
                if(PositionOf[Index]>=0){ continue; }
                // -0 と 0 は同じ位置。そろえないと、辞書の鍵が分かれて、面の継ぎ目が開いた縁に見える。
                var Position=CanonicalPosition(Vertices[(int)Index].Position);
                if(!PositionIds.TryGetValue(Position,out var Id)){
                    Id=Positions.Count;
                    PositionIds.Add(Position,Id);
                    Positions.Add(Position);
                }
                PositionOf[Index]=Id;
            }
            this.PositionOf=PositionOf;
            this.Positions=[..Positions];
            this.Quadrics=new Quadric[this.Positions.Length];
            this.Locked=new bool[this.Positions.Length];
            this.Removed=new bool[this.Positions.Length];
            this.Incident=new List<int>[this.Positions.Length];
            for(var Position=0;Position<this.Incident.Length;Position++){ this.Incident[Position]=[]; }
            foreach(var Index in LockedVertices){
                if(Index<PositionOf.Length&&PositionOf[Index]>=0){ this.Locked[PositionOf[Index]]=true; }
            }
            // 位置が重なる(面積の無い)三角形は何も描かないので、初めから外す。
            List<int>Corners=[];
            for(var Corner=0;Corner<Indices.Length;Corner+=3){
                int A=(int)Indices[Corner],B=(int)Indices[Corner+1],C=(int)Indices[Corner+2];
                int PA=PositionOf[A],PB=PositionOf[B],PC=PositionOf[C];
                if(PA==PB||PB==PC||PA==PC){ continue; }
                Corners.Add(A);Corners.Add(B);Corners.Add(C);
            }
            this.Corners=[..Corners];
            this.Alive=new bool[this.Corners.Length/3];
            Array.Fill(this.Alive,true);
            this.AliveCount=this.Alive.Length;
            for(var Triangle=0;Triangle<this.Alive.Length;Triangle++){
                for(var Corner=0;Corner<3;Corner++){ this.Incident[this.Pos(Triangle,Corner)].Add(Triangle); }
                var (Normal,Area)=this.FaceNormal(this.Pos(Triangle,0),this.Pos(Triangle,1),this.Pos(Triangle,2));
                if(Area<=0f){ continue; }
                var Plane=Quadric.FromPlane(Normal,-Vector3.Dot(Normal,this.Positions[this.Pos(Triangle,0)]),1f);
                for(var Corner=0;Corner<3;Corner++){ this.Quadrics[this.Pos(Triangle,Corner)].Add(Plane); }
            }
            this.AddBoundaryPlanes();
        }
        private int Pos(int Triangle,int Corner)=>this.PositionOf[this.Corners[Triangle*3+Corner]];
        private (Vector3 Normal,float Area)FaceNormal(int A,int B,int C){
            var Cross=Vector3.Cross(this.Positions[B]-this.Positions[A],this.Positions[C]-this.Positions[A]);
            var Length=Cross.Length();
            return Length>0f?(Cross*(1f/Length),Length*0.5f):(Cross,0f);
        }
        /// <summary>1 枚の三角形だけが使う辺(開いた縁)に、辺を含み面に垂直な面を足す。縁の頂点が縁から外れる縮約の誤差を大きくする。</summary>
        private void AddBoundaryPlanes(){
            var EdgeUse=new Dictionary<(int,int),int>();
            for(var Triangle=0;Triangle<this.Alive.Length;Triangle++){
                for(var Corner=0;Corner<3;Corner++){
                    int A=this.Pos(Triangle,Corner),B=this.Pos(Triangle,(Corner+1)%3);
                    var Key=A<B?(A,B):(B,A);
                    EdgeUse[Key]=EdgeUse.GetValueOrDefault(Key)+1;
                }
            }
            for(var Triangle=0;Triangle<this.Alive.Length;Triangle++){
                var (Normal,Area)=this.FaceNormal(this.Pos(Triangle,0),this.Pos(Triangle,1),this.Pos(Triangle,2));
                if(Area<=0f){ continue; }
                for(var Corner=0;Corner<3;Corner++){
                    int A=this.Pos(Triangle,Corner),B=this.Pos(Triangle,(Corner+1)%3);
                    if(EdgeUse[A<B?(A,B):(B,A)]!=1){ continue; }
                    var Edge=this.Positions[B]-this.Positions[A];
                    var Side=Vector3.Cross(Edge,Normal);
                    var Length=Side.Length();
                    if(Length<=0f){ continue; }
                    Side*=1f/Length;
                    var Plane=Quadric.FromPlane(Side,-Vector3.Dot(Side,this.Positions[A]),BoundaryWeight);
                    this.Quadrics[A].Add(Plane);
                    this.Quadrics[B].Add(Plane);
                }
            }
        }
        public void Run(int TargetTriangleCount,float MaximumError){
            if(this.AliveCount<=TargetTriangleCount){ return; }
            for(var Triangle=0;Triangle<this.Alive.Length;Triangle++){
                for(var Corner=0;Corner<3;Corner++){
                    int A=this.Pos(Triangle,Corner),B=this.Pos(Triangle,(Corner+1)%3);
                    this.Push(A,B);
                    this.Push(B,A);
                }
            }
            var Limit=MaximumError*MaximumError;
            while(this.AliveCount>TargetTriangleCount&&this.Queue.TryDequeue(out var Candidate,out var Priority)){
                if(this.Removed[Candidate.From]||this.Removed[Candidate.To]||this.Locked[Candidate.From]){ continue; }
                if(!this.TryCost(Candidate.From,Candidate.To,out var Cost,out var PositionError)){ continue; }
                // 前に積んだときより費用が増えていれば、今の費用で積み直す(周りの縮約で費用が変わった)。
                if(Cost>Priority.Cost*(1f+1e-6f)+1e-30f){
                    this.Queue.Enqueue(Candidate,(Cost,Candidate.From,Candidate.To));
                    continue;
                }
                if(PositionError>Limit){ continue; }
                if(!this.IsValid(Candidate.From,Candidate.To)){ continue; }
                this.Apply(Candidate.From,Candidate.To);
                this.Error=MathF.Max(this.Error,MathF.Sqrt(MathF.Max(PositionError,0f)));
            }
        }
        private void Push(int From,int To){
            if(this.Locked[From]||!this.TryCost(From,To,out var Cost,out _)){ return; }
            this.Queue.Enqueue(new Collapse(From,To),(Cost,From,To));
        }
        /// <summary>
        /// 費用 = 寄せた先での誤差の二次形式の値 + 法線と UV の違い × 辺の長さの二乗。継ぎ目の頂点の移し先が決まらなければ false。
        /// </summary>
        private bool TryCost(int From,int To,out float Cost,out float PositionError){
            Cost=0f;
            PositionError=0f;
            if(!this.MapWedges(From,To)){ return false; }
            var Merged=this.Quadrics[From];
            Merged.Add(this.Quadrics[To]);
            PositionError=MathF.Max(Merged.Evaluate(this.Positions[To]),0f);
            var Edge=this.Positions[To]-this.Positions[From];
            var LengthSquared=Vector3.Dot(Edge,Edge);
            var Attribute=0f;
            foreach(var (Source,Target) in this.WedgeMap){
                var NormalDelta=this.Vertices[Source].Normal-this.Vertices[Target].Normal;
                var UvDelta=this.Vertices[Source].Uv-this.Vertices[Target].Uv;
                Attribute+=NormalWeight*NormalWeight*NormalDelta.LengthSquared()+UvWeight*UvWeight*UvDelta.LengthSquared();
            }
            Cost=PositionError+Attribute*LengthSquared;
            return true;
        }
        /// <summary>
        /// 位置 <paramref name="From"/> の各頂点の移し先(位置 <paramref name="To"/> の頂点)を <see cref="WedgeMap"/> に作る。移し先は、From と To を共に含む三角形の中の To の頂点。
        /// From を含む三角形のどれもが、移し先の決まった頂点を使うときだけ true。
        /// </summary>
        private bool MapWedges(int From,int To){
            this.WedgeMap.Clear();
            var Shared=false;
            foreach(var Triangle in this.Incident[From]){
                if(!this.Alive[Triangle]){ continue; }
                int FromCorner=-1,ToCorner=-1;
                for(var Corner=0;Corner<3;Corner++){
                    var Position=this.Pos(Triangle,Corner);
                    if(Position==From){ FromCorner=Corner; }
                    else if(Position==To){ ToCorner=Corner; }
                }
                if(ToCorner<0){ continue; }
                Shared=true;
                int Source=this.Corners[Triangle*3+FromCorner],Target=this.Corners[Triangle*3+ToCorner];
                if(this.WedgeMap.TryGetValue(Source,out var Existing)){
                    if(Existing!=Target){ return false; }
                } else {
                    this.WedgeMap.Add(Source,Target);
                }
            }
            if(!Shared){ return false; }
            foreach(var Triangle in this.Incident[From]){
                if(!this.Alive[Triangle]){ continue; }
                for(var Corner=0;Corner<3;Corner++){
                    if(this.Pos(Triangle,Corner)==From&&!this.WedgeMap.ContainsKey(this.Corners[Triangle*3+Corner])){ return false; }
                }
            }
            return true;
        }
        /// <summary>リンク条件と裏返りを調べる。共通の隣の位置の数が、From と To を共に含む三角形の数と同じで、残る三角形の向きが大きく変わらないこと。</summary>
        private bool IsValid(int From,int To){
            this.Neighbours.Clear();
            this.OtherNeighbours.Clear();
            var SharedTriangles=0;
            foreach(var Triangle in this.Incident[From]){
                if(!this.Alive[Triangle]){ continue; }
                var ContainsTo=false;
                for(var Corner=0;Corner<3;Corner++){
                    var Position=this.Pos(Triangle,Corner);
                    if(Position==To){ ContainsTo=true; }
                    if(Position!=From&&Position!=To){ this.Neighbours.Add(Position); }
                }
                if(ContainsTo){
                    SharedTriangles++;
                    continue;
                }
                var (OldNormal,_)=this.FaceNormal(this.Pos(Triangle,0),this.Pos(Triangle,1),this.Pos(Triangle,2));
                var Moved=new int[3];
                for(var Corner=0;Corner<3;Corner++){
                    var Position=this.Pos(Triangle,Corner);
                    Moved[Corner]=Position==From?To:Position;
                }
                var (NewNormal,NewArea)=this.FaceNormal(Moved[0],Moved[1],Moved[2]);
                if(NewArea<=0f||Vector3.Dot(OldNormal,NewNormal)<MinimumNormalAgreement){ return false; }
            }
            foreach(var Triangle in this.Incident[To]){
                if(!this.Alive[Triangle]){ continue; }
                for(var Corner=0;Corner<3;Corner++){
                    var Position=this.Pos(Triangle,Corner);
                    if(Position!=From&&Position!=To){ this.OtherNeighbours.Add(Position); }
                }
            }
            this.Neighbours.IntersectWith(this.OtherNeighbours);
            return this.Neighbours.Count==SharedTriangles;
        }
        private void Apply(int From,int To){
            this.MapWedges(From,To);
            foreach(var Triangle in this.Incident[From]){
                if(!this.Alive[Triangle]){ continue; }
                var ContainsTo=false;
                for(var Corner=0;Corner<3;Corner++){
                    if(this.Pos(Triangle,Corner)==To){ ContainsTo=true; }
                }
                if(ContainsTo){
                    this.Alive[Triangle]=false;
                    this.AliveCount--;
                    continue;
                }
                for(var Corner=0;Corner<3;Corner++){
                    var Index=Triangle*3+Corner;
                    if(this.PositionOf[this.Corners[Index]]==From){ this.Corners[Index]=this.WedgeMap[this.Corners[Index]]; }
                }
                this.Incident[To].Add(Triangle);
            }
            this.Incident[From].Clear();
            this.Quadrics[To].Add(this.Quadrics[From]);
            this.Removed[From]=true;
            this.Incident[To].RemoveAll(Triangle=>!this.Alive[Triangle]);
            foreach(var Triangle in this.Incident[To]){
                for(var Corner=0;Corner<3;Corner++){
                    var Position=this.Pos(Triangle,Corner);
                    if(Position==To){ continue; }
                    this.Push(Position,To);
                    this.Push(To,Position);
                }
            }
        }
        /// <summary>残った三角形を、入力の順のまま返す。</summary>
        public uint[]Output(){
            var Result=new uint[this.AliveCount*3];
            var Write=0;
            for(var Triangle=0;Triangle<this.Alive.Length;Triangle++){
                if(!this.Alive[Triangle]){ continue; }
                for(var Corner=0;Corner<3;Corner++){ Result[Write++]=(uint)this.Corners[Triangle*3+Corner]; }
            }
            return Result;
        }
    }
}
