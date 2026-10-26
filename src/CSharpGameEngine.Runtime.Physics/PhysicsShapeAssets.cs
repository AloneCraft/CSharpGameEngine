using System.Security.Cryptography;
using System.Text;

namespace CSharpGameEngine.Runtime.Physics;

public enum PhysicsShapeAssetKind:byte {
    Convex,
    StaticTriangleMesh,
    /// <summary>
    /// ボディ座標枠での凸形状の子要素の結合（ADR-0078/M0113）。複数の破片を含む、壊れた <c>Destructible</c> の破片にあたる。UE Chaos では子形状をクラスター結合とし
    /// て保持する。実行時に作成し、調理済みアーティファクトには含めない。
    /// </summary>
    Compound,
}

/// <summary>不変の調理済み形状データ。実行時のボディはアセットの識別情報だけを保持する。</summary>
public sealed class PhysicsShapeAsset {
    private readonly LogicalVector3[] VertexStorage;
    private readonly int[] IndexStorage;
    private readonly MeshNode[] MeshNodes;
    private readonly int[] MeshTriangles;
    private readonly LogicalVector3[] PlaneNormalStorage;
    private readonly LogicalScalar[] PlaneDistanceStorage;
    private readonly PhysicsShapeAsset[] ChildStorage;

    private readonly record struct MeshNode(PhysicsAabb Bounds,int Left,int Right,int Start,int Count) {
        internal bool IsLeaf=>this.Count>0;
    }

    private PhysicsShapeAsset(string Id,PhysicsShapeAssetKind Kind,LogicalVector3[] Vertices,int[] Indices,
        LogicalVector3[]?PlaneNormals=null,LogicalScalar[]?PlaneDistances=null,PhysicsShapeAsset[]?Children=null){
        this.Id=Id;
        this.Kind=Kind;
        this.VertexStorage=Vertices;
        this.IndexStorage=Indices;
        this.PlaneNormalStorage=PlaneNormals??[];
        this.PlaneDistanceStorage=PlaneDistances??[];
        this.ChildStorage=Children??[];
        this.Bounds=CalculateBounds(Vertices);
        this.Fingerprint=ComputeFingerprint(Id,Kind,Vertices,Indices,this.ChildStorage);
        if(Kind==PhysicsShapeAssetKind.StaticTriangleMesh){
            List<MeshNode> Nodes=[];
            this.MeshTriangles=BuildMeshBvh(Vertices,Indices,Nodes);
            this.MeshNodes=Nodes.ToArray();
        }
        else {
            this.MeshNodes=[];
            this.MeshTriangles=[];
        }
    }

    public string Id { get; }
    public PhysicsShapeAssetKind Kind { get; }
    public IReadOnlyList<LogicalVector3> Vertices=>this.VertexStorage;
    public IReadOnlyList<int> Indices=>this.IndexStorage;
    public PhysicsAabb Bounds { get; }
    public string Fingerprint { get; }
    public int TriangleBvhNodeCount=>this.MeshNodes.Length;
    /// <summary>凸多面体の外向き面平面（<see cref="CreateConvexPolyhedron"/>）。点群から作った凸形状アセットでは空。</summary>
    public int PlaneCount=>this.PlaneNormalStorage.Length;
    /// <summary><see cref="PhysicsShapeAssetKind.Compound"/> の凸形状子要素。ボディの座標枠に格納し、他の種類では空。</summary>
    public IReadOnlyList<PhysicsShapeAsset> Children=>this.ChildStorage;

    public static PhysicsShapeAsset CreateConvex(string Id,IReadOnlyList<LogicalVector3> Vertices){
        ValidateId(Id);
        ArgumentNullException.ThrowIfNull(Vertices);
        if(Vertices.Count<4){ throw new ArgumentException("A convex asset requires at least four vertices.",nameof(Vertices)); }
        LogicalVector3[] Copy=Vertices.ToArray();
        EnsureDistinctVertices(Copy,4);
        return new PhysicsShapeAsset(Id,PhysicsShapeAssetKind.Convex,Copy,[]);
    }

    /// <summary>
    /// 閉じた三角形メッシュ（<paramref name="Indices"/>）から面を得る凸多面体（ADR-0078/M0113、事前破砕メッシュの破片など）。衝突判定は
    /// <see cref="CreateConvex"/> と同様に行う。面平面により狭域判定で内部にある各角を検出できるため、面に載る破片には1点ではなく複数点の接触パッチができ、揺れを防ぐ。面平面は
    /// 頂点中心から外向きである。平面より0.1 mmを超えて外側にある頂点（凸形状ではない）、または面積ゼロの三角形があれば拒否する。
    /// </summary>
    public static PhysicsShapeAsset CreateConvexPolyhedron(string Id,IReadOnlyList<LogicalVector3> Vertices,IReadOnlyList<int> Indices){
        ValidateId(Id);
        ArgumentNullException.ThrowIfNull(Vertices);
        ArgumentNullException.ThrowIfNull(Indices);
        if(Vertices.Count<4){ throw new ArgumentException("A convex polyhedron requires at least four vertices.",nameof(Vertices)); }
        if(Indices.Count<12||Indices.Count%3!=0){ throw new ArgumentException("A convex polyhedron requires a closed triangle mesh.",nameof(Indices)); }
        var VertexCopy=Vertices.ToArray();
        var IndexCopy=Indices.ToArray();
        EnsureDistinctVertices(VertexCopy,4);
        var Center=LogicalVector3.Zero;
        foreach(var Vertex in VertexCopy){ Center+=Vertex; }
        Center/=LogicalScalar.FromInt64(VertexCopy.Length);
        var Tolerance=LogicalScalar.FromSingle(0.0001f);
        List<LogicalVector3>Normals=[];
        List<LogicalScalar>Distances=[];
        for(var Index=0;Index<IndexCopy.Length;Index+=3){
            if((uint)IndexCopy[Index]>=(uint)VertexCopy.Length||(uint)IndexCopy[Index+1]>=(uint)VertexCopy.Length||(uint)IndexCopy[Index+2]>=(uint)VertexCopy.Length){
                throw new ArgumentOutOfRangeException(nameof(Indices));
            }
            var A=VertexCopy[IndexCopy[Index]];
            var B=VertexCopy[IndexCopy[Index+1]];
            var C=VertexCopy[IndexCopy[Index+2]];
            if(!LogicalVector3.Cross(B-A,C-A).TryNormalize(out var Normal)){
                throw new ArgumentException("Convex polyhedron triangles must not be degenerate.",nameof(Indices));
            }
            var Distance=LogicalVector3.Dot(Normal,A);
            if(LogicalVector3.Dot(Normal,Center)>Distance){
                Normal=-Normal;
                Distance=-Distance;
            }
            var Duplicate=false;
            for(var Plane=0;Plane<Normals.Count;Plane++){
                if(LogicalVector3.Dot(Normals[Plane],Normal)>=LogicalScalar.One-Tolerance&&Abs(Distances[Plane]-Distance)<=Tolerance){ Duplicate=true;break; }
            }
            if(Duplicate){ continue; }
            Normals.Add(Normal);
            Distances.Add(Distance);
        }
        for(var Plane=0;Plane<Normals.Count;Plane++){
            foreach(var Vertex in VertexCopy){
                if(LogicalVector3.Dot(Normals[Plane],Vertex)>Distances[Plane]+Tolerance){
                    throw new ArgumentException("A convex polyhedron must be convex: a vertex lies outside a face plane.",nameof(Vertices));
                }
            }
        }
        return new PhysicsShapeAsset(Id,PhysicsShapeAssetKind.Convex,VertexCopy,IndexCopy,[.. Normals],[.. Distances]);
    }

    /// <summary>
    /// <paramref name="Children"/> の凸形状をボディ自身の座標枠で結合し、1つの形状にする（ADR-0078/M0113）。境界や掃引判定では、すべての子要素の頂点の凸包を使う。
    /// 接触判定では、狭域判定が接触したボディごとに子要素の接触を1つのパッチへまとめる。
    /// </summary>
    public static PhysicsShapeAsset CreateCompound(string Id,IReadOnlyList<PhysicsShapeAsset> Children){
        ValidateId(Id);
        ArgumentNullException.ThrowIfNull(Children);
        if(Children.Count==0){ throw new ArgumentException("A compound shape requires at least one child.",nameof(Children)); }
        List<LogicalVector3>Vertices=[];
        foreach(var Child in Children){
            if(Child is not { Kind:PhysicsShapeAssetKind.Convex }){ throw new ArgumentException("Compound children must be convex assets.",nameof(Children)); }
            Vertices.AddRange(Child.VertexStorage);
        }
        return new PhysicsShapeAsset(Id,PhysicsShapeAssetKind.Compound,[.. Vertices],[],null,null,[.. Children]);
    }

    internal LogicalVector3 PlaneNormal(int Plane)=>this.PlaneNormalStorage[Plane];
    internal LogicalScalar PlaneDistance(int Plane)=>this.PlaneDistanceStorage[Plane];

    /// <summary>形状のローカル座標枠内の点が、<paramref name="Tolerance"/> の許容範囲で全ての面平面の内側にあるか。面平面がなければ false。</summary>
    internal bool ContainsLocal(LogicalVector3 Point,LogicalScalar Tolerance){
        if(this.PlaneNormalStorage.Length==0){ return false; }
        for(var Plane=0;Plane<this.PlaneNormalStorage.Length;Plane++){
            if(LogicalVector3.Dot(this.PlaneNormalStorage[Plane],Point)>this.PlaneDistanceStorage[Plane]+Tolerance){ return false; }
        }
        return true;
    }

    private static LogicalScalar Abs(LogicalScalar Value)=>Value<LogicalScalar.Zero?-Value:Value;

    public static PhysicsShapeAsset CreateStaticTriangleMesh(string Id,IReadOnlyList<LogicalVector3> Vertices,IReadOnlyList<int> Indices){
        ValidateId(Id);
        ArgumentNullException.ThrowIfNull(Vertices);
        ArgumentNullException.ThrowIfNull(Indices);
        if(Vertices.Count<3){ throw new ArgumentException("A mesh requires at least three vertices.",nameof(Vertices)); }
        if(Indices.Count==0||Indices.Count%3!=0){ throw new ArgumentException("Mesh indices must contain complete triangles.",nameof(Indices)); }
        LogicalVector3[] VertexCopy=Vertices.ToArray();
        int[] IndexCopy=Indices.ToArray();
        for(int Index=0;Index<IndexCopy.Length;Index++){
            if((uint)IndexCopy[Index]>=(uint)VertexCopy.Length){ throw new ArgumentOutOfRangeException(nameof(Indices)); }
        }
        for(int Index=0;Index<IndexCopy.Length;Index+=3){
            LogicalVector3 A=VertexCopy[IndexCopy[Index]];
            LogicalVector3 B=VertexCopy[IndexCopy[Index+1]];
            LogicalVector3 C=VertexCopy[IndexCopy[Index+2]];
            if(LogicalVector3.Cross(B-A,C-A).LengthSquared.IsZero){
                throw new ArgumentException("Mesh triangles must not be degenerate.",nameof(Indices));
            }
        }
        return new PhysicsShapeAsset(Id,PhysicsShapeAssetKind.StaticTriangleMesh,VertexCopy,IndexCopy);
    }

    /// <summary><see cref="Support"/> が全頂点を走査した回数。定常状態の境界問い合わせが頂点配列を走査しないことをテストで確認する。</summary>
    internal long SupportScanCount;

    internal LogicalVector3 Support(LogicalVector3 Direction){
        this.SupportScanCount++;
        int BestIndex=0;
        LogicalScalar Best=LogicalVector3.Dot(this.VertexStorage[0],Direction);
        for(int Index=1;Index<this.VertexStorage.Length;Index++){
            LogicalScalar Candidate=LogicalVector3.Dot(this.VertexStorage[Index],Direction);
            if(Candidate>Best){
                Best=Candidate;
                BestIndex=Index;
            }
        }
        return this.VertexStorage[BestIndex];
    }

    internal int QueryTriangles(in PhysicsAabb Bounds,List<int> Results){
        int Start=Results.Count;
        if(this.MeshNodes.Length==0){ return 0; }
        Span<int> Initial=stackalloc int[64];
        var Stack=new ValueListBuilder<int>(Initial);
        try {
            Stack.Append(0);
            while(Stack.Length>0){
                int NodeIndex=Stack.Pop();
                MeshNode Node=this.MeshNodes[NodeIndex];
                if(!Node.Bounds.Intersects(Bounds)){ continue; }
                if(Node.IsLeaf){
                    for(int Index=0;Index<Node.Count;Index++){ Results.Add(this.MeshTriangles[Node.Start+Index]); }
                    continue;
                }
                int First=Math.Min(Node.Left,Node.Right);
                int Second=Math.Max(Node.Left,Node.Right);
                Stack.Append(Second);
                Stack.Append(First);
            }
        }
        finally {
            Stack.Dispose();
        }
        Results.Sort(Start,Results.Count-Start,Comparer<int>.Default);
        return Results.Count-Start;
    }

    internal void GetTriangle(int Triangle,out LogicalVector3 A,out LogicalVector3 B,out LogicalVector3 C){
        int Offset=checked(Triangle*3);
        A=this.VertexStorage[this.IndexStorage[Offset]];
        B=this.VertexStorage[this.IndexStorage[Offset+1]];
        C=this.VertexStorage[this.IndexStorage[Offset+2]];
    }

    private static int[] BuildMeshBvh(LogicalVector3[] Vertices,int[] Indices,List<MeshNode> Nodes){
        int[] Triangles=Enumerable.Range(0,Indices.Length/3).ToArray();
        // 各ソート比較では中心を2つとも再計算していた（Q64.64 の除算）。一度だけ計算する。
        LogicalVector3[] Centers=new LogicalVector3[Triangles.Length];
        for(int Triangle=0;Triangle<Centers.Length;Triangle++){ Centers[Triangle]=TriangleCenter(Vertices,Indices,Triangle); }
        BuildNode(Vertices,Indices,Centers,Triangles,0,Triangles.Length,Nodes);
        return Triangles;
    }

    private static int BuildNode(LogicalVector3[] Vertices,int[] Indices,LogicalVector3[] Centers,int[] Triangles,int Start,int Count,List<MeshNode> Nodes){
        PhysicsAabb Bounds=TriangleBounds(Vertices,Indices,Triangles[Start]);
        LogicalVector3 FirstCenter=Centers[Triangles[Start]];
        PhysicsAabb CenterBounds=new(FirstCenter,FirstCenter);
        for(int Index=1;Index<Count;Index++){
            Bounds=Combine(Bounds,TriangleBounds(Vertices,Indices,Triangles[Start+Index]));
            LogicalVector3 Center=Centers[Triangles[Start+Index]];
            CenterBounds=Combine(CenterBounds,new PhysicsAabb(Center,Center));
        }
        int NodeIndex=Nodes.Count;
        Nodes.Add(default);
        if(Count<=4){
            Nodes[NodeIndex]=new MeshNode(Bounds,-1,-1,Start,Count);
            return NodeIndex;
        }
        LogicalVector3 Size=CenterBounds.Max-CenterBounds.Min;
        int Axis=Size.R>=Size.U&&Size.R>=Size.F?0:Size.U>=Size.F?1:2;
        Array.Sort(Triangles,Start,Count,new TriangleCenterComparer(Centers,Axis));
        int LeftCount=Count/2;
        int Left=BuildNode(Vertices,Indices,Centers,Triangles,Start,LeftCount,Nodes);
        int Right=BuildNode(Vertices,Indices,Centers,Triangles,Start+LeftCount,Count-LeftCount,Nodes);
        Nodes[NodeIndex]=new MeshNode(Bounds,Left,Right,0,0);
        return NodeIndex;
    }

    private static PhysicsAabb TriangleBounds(LogicalVector3[] Vertices,int[] Indices,int Triangle){
        int Offset=Triangle*3;
        LogicalVector3 A=Vertices[Indices[Offset]];
        LogicalVector3 B=Vertices[Indices[Offset+1]];
        LogicalVector3 C=Vertices[Indices[Offset+2]];
        return new PhysicsAabb(
            new LogicalVector3(Min(A.R,Min(B.R,C.R)),Min(A.U,Min(B.U,C.U)),Min(A.F,Min(B.F,C.F))),
            new LogicalVector3(Max(A.R,Max(B.R,C.R)),Max(A.U,Max(B.U,C.U)),Max(A.F,Max(B.F,C.F))));
    }

    private static LogicalVector3 TriangleCenter(LogicalVector3[] Vertices,int[] Indices,int Triangle){
        int Offset=Triangle*3;
        return (Vertices[Indices[Offset]]+Vertices[Indices[Offset+1]]+Vertices[Indices[Offset+2]])/LogicalScalar.FromInt64(3);
    }

    private static PhysicsAabb CalculateBounds(LogicalVector3[] Vertices){
        LogicalVector3 MinValue=Vertices[0];
        LogicalVector3 MaxValue=Vertices[0];
        for(int Index=1;Index<Vertices.Length;Index++){
            LogicalVector3 Value=Vertices[Index];
            MinValue=new LogicalVector3(Min(MinValue.R,Value.R),Min(MinValue.U,Value.U),Min(MinValue.F,Value.F));
            MaxValue=new LogicalVector3(Max(MaxValue.R,Value.R),Max(MaxValue.U,Value.U),Max(MaxValue.F,Value.F));
        }
        return new PhysicsAabb(MinValue,MaxValue);
    }

    private static PhysicsAabb Combine(in PhysicsAabb Left,in PhysicsAabb Right)=>new(
        new LogicalVector3(Min(Left.Min.R,Right.Min.R),Min(Left.Min.U,Right.Min.U),Min(Left.Min.F,Right.Min.F)),
        new LogicalVector3(Max(Left.Max.R,Right.Max.R),Max(Left.Max.U,Right.Max.U),Max(Left.Max.F,Right.Max.F)));

    private static string ComputeFingerprint(string Id,PhysicsShapeAssetKind Kind,LogicalVector3[] Vertices,int[] Indices,PhysicsShapeAsset[] Children){
        using MemoryStream Stream=new();
        using(BinaryWriter Writer=new(Stream,Encoding.UTF8,true)){
            Writer.Write(Id);
            Writer.Write((byte)Kind);
            Writer.Write(Vertices.Length);
            foreach(LogicalVector3 Vertex in Vertices){ WriteVector(Writer,Vertex); }
            Writer.Write(Indices.Length);
            foreach(int Index in Indices){ Writer.Write(Index); }
            // 複合形状の子要素（M0113）。他の種類には何も書き込まないため、フィンガープリントは従来どおり。
            foreach(var Child in Children){ Writer.Write(Child.Fingerprint); }
        }
        return Convert.ToHexString(SHA256.HashData(Stream.GetBuffer().AsSpan(0,checked((int)Stream.Length)))).ToLowerInvariant();
    }

    private static void WriteVector(BinaryWriter Writer,in LogicalVector3 Value){
        WriteScalar(Writer,Value.R);
        WriteScalar(Writer,Value.U);
        WriteScalar(Writer,Value.F);
    }

    private static void WriteScalar(BinaryWriter Writer,LogicalScalar Value){
        Span<byte> Bytes=stackalloc byte[LogicalScalar.SerializedSize];
        Value.WriteLittleEndian(Bytes);
        Writer.Write(Bytes);
    }

    private static void ValidateId(string Id){
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if(Id.Length>1024){ throw new ArgumentOutOfRangeException(nameof(Id)); }
    }

    private static void EnsureDistinctVertices(LogicalVector3[] Vertices,int Minimum){
        HashSet<LogicalVector3> Unique=[];
        foreach(LogicalVector3 Vertex in Vertices){ Unique.Add(Vertex); }
        if(Unique.Count<Minimum){ throw new ArgumentException("The asset does not contain enough distinct vertices.",nameof(Vertices)); }
    }

    private static LogicalScalar Min(LogicalScalar Left,LogicalScalar Right)=>Left<Right?Left:Right;
    private static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;

    private sealed class TriangleCenterComparer(LogicalVector3[] Centers,int Axis):IComparer<int> {
        public int Compare(int Left,int Right){
            LogicalVector3 LeftCenter=Centers[Left];
            LogicalVector3 RightCenter=Centers[Right];
            int Result=Axis switch {
                0=>LeftCenter.R.CompareTo(RightCenter.R),
                1=>LeftCenter.U.CompareTo(RightCenter.U),
                _=>LeftCenter.F.CompareTo(RightCenter.F),
            };
            return Result!=0?Result:Left.CompareTo(Right);
        }
    }

    private ref struct ValueListBuilder<T>(Span<T> InitialBuffer) {
        private Span<T> Buffer=InitialBuffer;
        private T[]? Array;
        internal int Length { get;private set; }
        internal void Append(T Value){
            if(this.Length==this.Buffer.Length){ this.Grow(); }
            this.Buffer[this.Length++]=Value;
        }
        internal T Pop()=>this.Buffer[--this.Length];
        internal void Dispose(){
            if(this.Array is not null){ System.Buffers.ArrayPool<T>.Shared.Return(this.Array); }
        }
        private void Grow(){
            T[] NewArray=System.Buffers.ArrayPool<T>.Shared.Rent(Math.Max(4,this.Buffer.Length*2));
            this.Buffer.CopyTo(NewArray);
            T[]? Old=this.Array;
            this.Array=NewArray;
            this.Buffer=NewArray;
            if(Old is not null){ System.Buffers.ArrayPool<T>.Shared.Return(Old); }
        }
    }
}

public sealed class PhysicsShapeCatalog {
    private readonly Dictionary<string,PhysicsShapeAsset> Assets=new(StringComparer.Ordinal);
    public int Count=>this.Assets.Count;

    public void Add(PhysicsShapeAsset Asset){
        ArgumentNullException.ThrowIfNull(Asset);
        if(this.Assets.TryGetValue(Asset.Id,out PhysicsShapeAsset? Existing)){
            if(Existing.Fingerprint==Asset.Fingerprint){ return; }
            throw new InvalidOperationException($"Physics shape asset '{Asset.Id}' already has different cooked content.");
        }
        this.Assets.Add(Asset.Id,Asset);
    }

    public bool TryGet(string Id,out PhysicsShapeAsset Asset)=>this.Assets.TryGetValue(Id,out Asset!);
    internal void Clear()=>this.Assets.Clear();
}

public static class PhysicsShapeArtifact {
    private static ReadOnlySpan<byte> Magic=>"CGEPHYS3"u8;
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    private const int MaximumElementCount=3_000_000;
    private const int MaximumStringBytes=4096;

    public static void Write(Stream Output,PhysicsShapeAsset Asset){
        ArgumentNullException.ThrowIfNull(Output);
        ArgumentNullException.ThrowIfNull(Asset);
        if(Asset.Kind==PhysicsShapeAssetKind.Compound){ throw new ArgumentException("A compound shape is built at run time and has no artifact.",nameof(Asset)); }
        using BinaryWriter Writer=new(Output,Encoding.UTF8,true);
        Writer.Write(Magic);
        Writer.Write(1);
        WriteString(Writer,Asset.Id);
        Writer.Write((byte)Asset.Kind);
        Writer.Write(Asset.Vertices.Count);
        foreach(LogicalVector3 Vertex in Asset.Vertices){ WriteVector(Writer,Vertex); }
        Writer.Write(Asset.Indices.Count);
        foreach(int Index in Asset.Indices){ Writer.Write(Index); }
        WriteString(Writer,Asset.Fingerprint);
    }

    public static bool TryRead(Stream Input,out PhysicsShapeAsset? Asset,out string Error){
        ArgumentNullException.ThrowIfNull(Input);
        Asset=null;
        Error=string.Empty;
        try {
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            byte[] Header=Reader.ReadBytes(Magic.Length);
            if(!Header.AsSpan().SequenceEqual(Magic)||Reader.ReadInt32()!=1){ throw new InvalidDataException(); }
            string Id=ReadString(Reader);
            PhysicsShapeAssetKind Kind=(PhysicsShapeAssetKind)Reader.ReadByte();
            int VertexCount=ReadCount(Reader);
            LogicalVector3[] Vertices=new LogicalVector3[VertexCount];
            for(int Index=0;Index<VertexCount;Index++){ Vertices[Index]=ReadVector(Reader); }
            int IndexCount=ReadCount(Reader);
            int[] Indices=new int[IndexCount];
            for(int Index=0;Index<IndexCount;Index++){ Indices[Index]=Reader.ReadInt32(); }
            string Fingerprint=ReadString(Reader);
            if(Input.CanSeek&&Input.Position!=Input.Length){ throw new InvalidDataException(); }
            Asset=Kind switch {
                PhysicsShapeAssetKind.Convex when Indices.Length>0=>PhysicsShapeAsset.CreateConvexPolyhedron(Id,Vertices,Indices),
                PhysicsShapeAssetKind.Convex=>PhysicsShapeAsset.CreateConvex(Id,Vertices),
                PhysicsShapeAssetKind.StaticTriangleMesh=>PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices),
                _=>throw new InvalidDataException(),
            };
            if(!StringComparer.Ordinal.Equals(Asset.Fingerprint,Fingerprint)){ throw new InvalidDataException(); }
            return true;
        }
        catch(Exception Exception)when(Exception is EndOfStreamException or IOException or InvalidDataException
            or ArgumentException or OverflowException or DecoderFallbackException){
            Asset=null;
            Error="Physics shape artifact is malformed or non-canonical.";
            return false;
        }
    }

    private static int ReadCount(BinaryReader Reader){
        int Count=Reader.ReadInt32();
        return Count is >=0 and <=MaximumElementCount?Count:throw new InvalidDataException();
    }
    private static void WriteString(BinaryWriter Writer,string Value){
        byte[] Bytes=Encoding.UTF8.GetBytes(Value);
        if(Bytes.Length>MaximumStringBytes){ throw new ArgumentOutOfRangeException(nameof(Value)); }
        Writer.Write7BitEncodedInt(Bytes.Length);
        Writer.Write(Bytes);
    }
    private static string ReadString(BinaryReader Reader){
        int Length=Reader.Read7BitEncodedInt();
        if(Length is <0 or >MaximumStringBytes){ throw new InvalidDataException(); }
        byte[] Bytes=Reader.ReadBytes(Length);
        if(Bytes.Length!=Length){ throw new EndOfStreamException(); }
        return StrictUtf8.GetString(Bytes);
    }
    private static void WriteVector(BinaryWriter Writer,in LogicalVector3 Value){ WriteScalar(Writer,Value.R);WriteScalar(Writer,Value.U);WriteScalar(Writer,Value.F); }
    private static LogicalVector3 ReadVector(BinaryReader Reader)=>new(ReadScalar(Reader),ReadScalar(Reader),ReadScalar(Reader));
    private static void WriteScalar(BinaryWriter Writer,LogicalScalar Value){ Span<byte> Bytes=stackalloc byte[LogicalScalar.SerializedSize];Value.WriteLittleEndian(Bytes);Writer.Write(Bytes); }
    private static LogicalScalar ReadScalar(BinaryReader Reader){ Span<byte> Bytes=stackalloc byte[LogicalScalar.SerializedSize];Reader.ReadExactly(Bytes);return LogicalScalar.TryReadLittleEndian(Bytes,out var Value)?Value:throw new InvalidDataException(); }
}
