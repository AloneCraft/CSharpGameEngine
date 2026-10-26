namespace CSharpGameEngine.Runtime.Physics;

internal readonly record struct PhysicsCollisionShape(
    Collider Collider,LogicalVector3 Position,LogicalQuaternion Rotation,PhysicsShapeAsset? Asset);

internal readonly struct PhysicsContactCandidate {
    private readonly LogicalVector3 Point0;
    private readonly LogicalVector3 Point1;
    private readonly LogicalVector3 Point2;
    private readonly LogicalVector3 Point3;
    private readonly LogicalScalar Penetration0;
    private readonly LogicalScalar Penetration1;
    private readonly LogicalScalar Penetration2;
    private readonly LogicalScalar Penetration3;

    internal PhysicsContactCandidate(
        LogicalVector3 Position,LogicalVector3 Normal,LogicalScalar Penetration,uint FeatureId){
        this.Point0=Position;
        this.Point1=default;
        this.Point2=default;
        this.Point3=default;
        this.Penetration0=Penetration;
        this.Penetration1=default;
        this.Penetration2=default;
        this.Penetration3=default;
        this.Normal=Normal;
        this.FeatureId=FeatureId;
        this.Count=1;
    }

    private PhysicsContactCandidate(
        LogicalVector3 Normal,uint FeatureId,int Count,
        LogicalVector3 Point0,LogicalVector3 Point1,LogicalVector3 Point2,LogicalVector3 Point3,
        LogicalScalar Penetration0,LogicalScalar Penetration1,LogicalScalar Penetration2,LogicalScalar Penetration3){
        this.Normal=Normal;
        this.FeatureId=FeatureId;
        this.Count=Count;
        this.Point0=Point0;
        this.Point1=Point1;
        this.Point2=Point2;
        this.Point3=Point3;
        this.Penetration0=Penetration0;
        this.Penetration1=Penetration1;
        this.Penetration2=Penetration2;
        this.Penetration3=Penetration3;
    }

    internal LogicalVector3 Normal { get; }
    /// <summary>接触した三角形の面法線。もう一方の形状を向く。単一の面がない接触ではゼロ。</summary>
    internal LogicalVector3 SurfaceNormal { get;init; }
    internal uint FeatureId { get; }
    internal int Count { get; }
    internal LogicalVector3 Position=>this.Point0;
    internal LogicalScalar Penetration=>Max(this.Penetration0,Max(this.Penetration1,Max(this.Penetration2,this.Penetration3)));
    internal LogicalVector3 GetPosition(int Index)=>Index switch {
        0=>this.Point0,1=>this.Point1,2=>this.Point2,3=>this.Point3,_=>throw new ArgumentOutOfRangeException(nameof(Index)),
    };
    internal LogicalScalar GetPenetration(int Index)=>Index switch {
        0=>this.Penetration0,1=>this.Penetration1,2=>this.Penetration2,3=>this.Penetration3,_=>throw new ArgumentOutOfRangeException(nameof(Index)),
    };

    internal static PhysicsContactCandidate CreatePatch(
        LogicalVector3 Normal,uint FeatureId,Span<LogicalVector3> Points,Span<LogicalScalar> Penetrations,int Count){
        if(Count is <1 or >4){ throw new ArgumentOutOfRangeException(nameof(Count)); }
        return new PhysicsContactCandidate(
            Normal,FeatureId,Count,
            Points[0],Count>1?Points[1]:default,Count>2?Points[2]:default,Count>3?Points[3]:default,
            Penetrations[0],Count>1?Penetrations[1]:default,Count>2?Penetrations[2]:default,Count>3?Penetrations[3]:default);
    }

    private static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;
}

/// <summary>サポート写像を使う凸形状の狭域判定。メッシュは調理済み BVH を使って問い合わせる。</summary>
internal sealed class PhysicsNarrowPhase {
    private const int GjkIterations=32;
    private const int EpaIterations=48;
    private const int MaximumEpaVertices=64;
    private const int MaximumEpaFaces=128;
    private static readonly LogicalScalar EpaTolerance=LogicalScalar.FromSingle(0.0001f);
    private static readonly LogicalScalar TriangleHalfThickness=LogicalScalar.FromSingle(0.0005f);
    /// <summary>
    /// ボックスの角が接触パッチに含まれる、メッシュ面より上側の許容範囲（ADR-0078/M0109）。高速のボックスは、まず低い角から面に接触する（速度30 m/sなら1ティックに0.5 m進む）。接触し
    /// た角だけでパッチを作ると衝撃全体が2点に集中してボックスが回転するため、面のすぐ上にある角にも予測接触点として荷重を分担させる（深さを負の値とし、接触前に近づける速度を制限する）。
    /// </summary>
    private static readonly LogicalScalar BoxMeshSpeculativeMargin=LogicalScalar.FromSingle(0.05f);
    // 接触の許容幅に、サポート写像による問い合わせ境界の丸め分を加える。
    private static readonly LogicalScalar TriangleCullSlack=TriangleHalfThickness+LogicalScalar.FromSingle(0.001f);
    private readonly List<int> TriangleCandidates=[];
    private readonly Dictionary<PhysicsShapeAsset,AssetBoundsEntry> AssetBounds=new(ReferenceEqualityComparer.Instance);
    private const int MaximumPrimitiveBoundsEntries=256;
    private readonly Dictionary<PrimitiveBoundsKey,AssetBoundsEntry> PrimitiveBounds=[];

    private readonly record struct PrimitiveBoundsKey(
        ColliderShape Shape,LogicalVector3 Center,LogicalVector3 HalfExtents,LogicalScalar Radius,LogicalScalar HalfHeight,LogicalQuaternion Rotation);

    private readonly record struct AssetBoundsEntry(
        LogicalQuaternion Rotation,LogicalVector3 ColliderCenter,
        LogicalVector3 PositiveR,LogicalVector3 PositiveU,LogicalVector3 PositiveF,
        LogicalVector3 NegativeR,LogicalVector3 NegativeU,LogicalVector3 NegativeF);

    private readonly record struct SupportPoint(LogicalVector3 Difference,LogicalVector3 First,LogicalVector3 Second);
    private readonly record struct EpaFace(int A,int B,int C,LogicalVector3 Normal,LogicalScalar Distance,bool Active);
    private readonly record struct EpaEdge(int A,int B);

    /// <summary>
    /// 1つの接触が同時に保持できる面の向きの最大数（M0124）。メッシュ三角形に乗るボックス、多面体、複合形状（車が縁石の斜面と地面をまたぐ場合など）、または何らかの形状と接触する複合形状では、向きごとに
    /// 最大4点のパッチを1つずつ作る。そのため、1つの形状対に含まれる接触点は <see cref="MaximumContactPoints"/> 以下となる。
    /// </summary>
    internal const int MaximumContactPatches=3;
    internal const int MaximumContactPoints=MaximumContactPatches*4;
    /// <summary>内積が0.95以上（およそ18度以内）となる2つの接触法線は、同じ向きとして扱う。</summary>
    private static readonly LogicalScalar SameOrientation=LogicalScalar.FromSingle(0.95f);
    private readonly List<PhysicsContactCandidate>SinglePatches=[];

    /// <summary><see cref="TryCreateContacts"/> が返す、最も深いパッチ（向きが1つの形状対なら接触全体）。</summary>
    internal bool TryCreateContact(
        in PhysicsCollisionShape First,in PhysicsCollisionShape Second,
        out PhysicsContactCandidate Contact){
        this.SinglePatches.Clear();
        if(!this.TryCreateContacts(First,Second,this.SinglePatches)){
            Contact=default;
            return false;
        }
        Contact=this.SinglePatches[0];
        return true;
    }

    /// <summary>
    /// 2つの形状の接触を <paramref name="Patches"/> に追加する。面の向きごとに最大 <see cref="MaximumContactPatches"/> 個のパッチを作り、深い
    /// 順に並べる。各パッチの法線は第1形状から第2形状へ向く。接触していない場合は false（何も追加しない）。
    /// </summary>
    internal bool TryCreateContacts(in PhysicsCollisionShape First,in PhysicsCollisionShape Second,List<PhysicsContactCandidate>Patches){
        if(First.Collider.Shape==ColliderShape.Compound||Second.Collider.Shape==ColliderShape.Compound){
            return this.TryCompoundContacts(First,Second,Patches);
        }
        if(First.Collider.Shape==ColliderShape.StaticTriangleMesh){
            return this.TryMeshContacts(Second,First,true,Patches);
        }
        if(Second.Collider.Shape==ColliderShape.StaticTriangleMesh){
            return this.TryMeshContacts(First,Second,false,Patches);
        }
        if(!this.TryConvexPairContact(First,Second,out var Contact)){ return false; }
        Patches.Add(Contact);
        return true;
    }

    private bool TryConvexPairContact(in PhysicsCollisionShape First,in PhysicsCollisionShape Second,out PhysicsContactCandidate Contact){
        Contact=default;
        if(!IsConvex(First.Collider.Shape)||!IsConvex(Second.Collider.Shape)){ return false; }
        if(TryPrimitiveContact(First,Second,out Contact)){ return true; }
        // ADR-0078/M0113：凸多面体同士（少なくとも一方は破片などの多面体）。多くの場合、面の平面を使って判定する（TryPolytopeContact を参照）。
        if((IsPolyhedron(First)||IsPolyhedron(Second))&&HasPolytope(First)&&HasPolytope(Second)
            &&this.TryPolytopeContact(First,Second,out var Touching,out Contact)){ return Touching; }
        if(!TryConvexContact(new ShapeProxy(First),new ShapeProxy(Second),0,out Contact)){ return false; }
        // ADR-0078/M0113：凸多面体（破片）が面に接触したとき、GJK の1点ではなく、もう一方の形状内にある各角から接触パッチを作る。
        if((IsPolyhedron(First)||IsPolyhedron(Second))&&HasPolytope(First)&&HasPolytope(Second)){
            Contact=ExpandPolytopeManifold(First,Second,Contact);
        }
        return true;
    }

    /// <summary>面の平面を持つ凸形状アセット（<see cref="PhysicsShapeAsset.CreateConvexPolyhedron"/>）。</summary>
    private static bool IsPolyhedron(in PhysicsCollisionShape Shape)=>
        Shape.Collider.Shape==ColliderShape.ConvexAsset&&Shape.Asset is { Kind:PhysicsShapeAssetKind.Convex,PlaneCount:>0 };

    /// <summary>角と内部形状が既知の形状。ボックスまたは凸多面体。</summary>
    private static bool HasPolytope(in PhysicsCollisionShape Shape)=>Shape.Collider.Shape==ColliderShape.Box||IsPolyhedron(Shape);

    private const int MaximumPolytopeVertices=64;

    /// <summary>ボックスのワールド座標上の角（8点）または多面体の頂点を <paramref name="Corners"/> に書き込む。格納できない、またはオーバーフローした場合は -1。</summary>
    private static int PolytopeCorners(in PhysicsCollisionShape Shape,Span<LogicalVector3>Corners){
        if(!TryColliderCenter(Shape,out var Center)){ return -1; }
        if(Shape.Collider.Shape==ColliderShape.Box){
            var Half=Shape.Collider.HalfExtents;
            for(var Index=0;Index<8;Index++){
                var Local=new LogicalVector3((Index&1)==0?-Half.R:Half.R,(Index&2)==0?-Half.U:Half.U,(Index&4)==0?-Half.F:Half.F);
                if(!Shape.Rotation.TryRotate(Local,out var World)){ return -1; }
                Corners[Index]=Center+World;
            }
            return 8;
        }
        var Vertices=Shape.Asset!.Vertices;
        if(Vertices.Count>Corners.Length){ return -1; }
        for(var Index=0;Index<Vertices.Count;Index++){
            if(!Shape.Rotation.TryRotate(Vertices[Index],out var World)){ return -1; }
            Corners[Index]=Center+World;
        }
        return Vertices.Count;
    }

    /// <summary>ワールド座標上の点が、各面から <paramref name="Tolerance"/> 以内でボックスまたは多面体の内部にあるか。</summary>
    private static bool PolytopeContains(in PhysicsCollisionShape Shape,LogicalVector3 Point,LogicalScalar Tolerance){
        if(!TryColliderCenter(Shape,out var Center)||!Shape.Rotation.Conjugate().TryRotate(Point-Center,out var Local)){ return false; }
        if(Shape.Collider.Shape==ColliderShape.Box){
            var Half=Shape.Collider.HalfExtents;
            return Abs(Local.R)<=Half.R+Tolerance&&Abs(Local.U)<=Half.U+Tolerance&&Abs(Local.F)<=Half.F+Tolerance;
        }
        return Shape.Asset!.ContainsLocal(Local,Tolerance);
    }

    /// <summary>
    /// ADR-0078/M0113：GJK/EPA が2つの凸形状（少なくとも一方は凸多面体）に対して求めた接触を、接触パッチに広げる。一方の形状の角がもう一方の内部にあれば（0.1 mm以内）、その角を
    /// 接触点にする。深さは、接触法線に沿って測ったもう一方の形状の最外点から求める。最大4点を選び、最も深い点を先頭にし、その後は既に選んだ点から最も遠い点を選んで、接触面全体を覆うパッチにする。2点未満の
    /// 場合は、EPA の1点接触を維持する（辺または頂点での接触）。
    /// </summary>
    private static PhysicsContactCandidate ExpandPolytopeManifold(in PhysicsCollisionShape First,in PhysicsCollisionShape Second,in PhysicsContactCandidate Contact){
        if(!Contact.Normal.TryNormalize(out var Normal)){ return Contact; }
        Span<LogicalVector3>FirstCorners=stackalloc LogicalVector3[MaximumPolytopeVertices];
        Span<LogicalVector3>SecondCorners=stackalloc LogicalVector3[MaximumPolytopeVertices];
        var FirstCount=PolytopeCorners(First,FirstCorners);
        var SecondCount=PolytopeCorners(Second,SecondCorners);
        if(FirstCount<0||SecondCount<0){ return Contact; }
        var FirstTop=LogicalVector3.Dot(new ShapeProxy(First).Support(Normal),Normal);
        var SecondBottom=LogicalVector3.Dot(new ShapeProxy(Second).Support(-Normal),Normal);
        Span<LogicalVector3>Points=stackalloc LogicalVector3[MaximumPolytopeVertices*2];
        Span<LogicalScalar>Depths=stackalloc LogicalScalar[MaximumPolytopeVertices*2];
        var Count=0;
        var Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
        for(var Index=0;Index<SecondCount;Index++){
            var Corner=SecondCorners[Index];
            var Depth=FirstTop-LogicalVector3.Dot(Corner,Normal);
            if(Depth<-EpaTolerance||!PolytopeContains(First,Corner,EpaTolerance)){ continue; }
            Points[Count]=Corner+Normal*(Max(Depth,LogicalScalar.Zero)*Half);
            Depths[Count++]=Max(EpaTolerance,Min(Depth,Contact.Penetration+EpaTolerance));
        }
        for(var Index=0;Index<FirstCount;Index++){
            var Corner=FirstCorners[Index];
            var Depth=LogicalVector3.Dot(Corner,Normal)-SecondBottom;
            if(Depth<-EpaTolerance||!PolytopeContains(Second,Corner,EpaTolerance)){ continue; }
            Points[Count]=Corner-Normal*(Max(Depth,LogicalScalar.Zero)*Half);
            Depths[Count++]=Max(EpaTolerance,Min(Depth,Contact.Penetration+EpaTolerance));
        }
        if(Count<2){ return Contact; }
        Span<LogicalVector3>Patch=stackalloc LogicalVector3[4];
        Span<LogicalScalar>PatchDepths=stackalloc LogicalScalar[4];
        var Kept=SpreadPoints(Points[..Count],Depths[..Count],Patch,PatchDepths);
        return PhysicsContactCandidate.CreatePatch(Contact.Normal,Contact.FeatureId,Patch,PatchDepths,Kept) with { SurfaceNormal=Contact.SurfaceNormal };
    }

    private const int MaximumPolytopePlanes=64;

    /// <summary>ボックス（6面）または多面体のワールド座標上の面平面（外向き法線と距離）を書き込む。格納できない、またはオーバーフローした場合は -1。</summary>
    private static int PolytopePlanes(in PhysicsCollisionShape Shape,Span<LogicalVector3>Normals,Span<LogicalScalar>Distances){
        if(!TryColliderCenter(Shape,out var Center)){ return -1; }
        if(Shape.Collider.Shape==ColliderShape.Box){
            Span<LogicalVector3>Axes=stackalloc LogicalVector3[3];
            if(!TryAxes(Shape.Rotation,Axes)){ return -1; }
            var Half=Shape.Collider.HalfExtents;
            for(var Axis=0;Axis<3;Axis++){
                var Extent=GetAxis(Half,Axis);
                var Along=LogicalVector3.Dot(Axes[Axis],Center);
                Normals[Axis*2]=Axes[Axis];
                Distances[Axis*2]=Along+Extent;
                Normals[Axis*2+1]=-Axes[Axis];
                Distances[Axis*2+1]=Extent-Along;
            }
            return 6;
        }
        var Asset=Shape.Asset!;
        if(Asset.PlaneCount>Normals.Length){ return -1; }
        for(var Plane=0;Plane<Asset.PlaneCount;Plane++){
            if(!Shape.Rotation.TryRotate(Asset.PlaneNormal(Plane),out var Normal)){ return -1; }
            Normals[Plane]=Normal;
            Distances[Plane]=LogicalVector3.Dot(Normal,Center)+Asset.PlaneDistance(Plane);
        }
        return Asset.PlaneCount;
    }

    private static bool Inside(ReadOnlySpan<LogicalVector3>Normals,ReadOnlySpan<LogicalScalar>Distances,LogicalVector3 Point){
        for(var Plane=0;Plane<Normals.Length;Plane++){
            if(LogicalVector3.Dot(Normals[Plane],Point)>Distances[Plane]+EpaTolerance){ return false; }
        }
        return true;
    }

    /// <summary>
    /// 1つの姿勢におけるワールド空間の凸形状。各角、外向き面平面、中心、および形状全体を囲む中心を基準とした球の半径を保持する。多面体の座標枠はアセットごとに保持し、姿勢が変わったときだけ再作成する。これに
    /// より、1ティック中にボディが関わるすべての形状対で回転計算を共有できる（Q64.64 では回転に約10 µs、内積に約1 µsかかる）。
    /// </summary>
    private sealed class PolytopeFrame {
        internal readonly LogicalVector3[]Corners=new LogicalVector3[MaximumPolytopeVertices];
        internal readonly LogicalVector3[]Normals=new LogicalVector3[MaximumPolytopePlanes];
        internal readonly LogicalScalar[]Distances=new LogicalScalar[MaximumPolytopePlanes];
        internal int CornerCount;
        internal int PlaneCount;
        internal LogicalVector3 Center;
        internal LogicalScalar Radius;
        internal LogicalVector3 Position;
        internal LogicalQuaternion Rotation;
        internal LogicalVector3 ColliderCenter;
        internal LogicalVector3 HalfExtents;
        internal bool Valid;
        internal ReadOnlySpan<LogicalVector3>CornerSpan=>this.Corners.AsSpan(0,this.CornerCount);
        internal ReadOnlySpan<LogicalVector3>NormalSpan=>this.Normals.AsSpan(0,this.PlaneCount);
        internal ReadOnlySpan<LogicalScalar>DistanceSpan=>this.Distances.AsSpan(0,this.PlaneCount);
    }
    private readonly Dictionary<PhysicsShapeAsset,PolytopeFrame>PolyhedronFrames=new(ReferenceEqualityComparer.Instance);
    private readonly PolytopeFrame FirstBoxFrame=new();
    private readonly PolytopeFrame SecondBoxFrame=new();
    /// <summary>形状対の両方が同一の多面体アセットである場合の、第2形状の座標枠（そのような各形状対について再作成する）。</summary>
    private readonly PolytopeFrame SharedSecondFrame=new();
    private const int MaximumPolyhedronFrames=4096;

    /// <summary>
    /// ボックスまたは多面体のワールド座標枠（格納不能またはオーバーフロー時は null）。<paramref name="Taken"/> は形状対の第1形状がすでに保持している座標枠。同じ破砕アセットから
    /// 作られた2つのボディ（同じ調理結果を使う箱など）は各破片の衝突形状を共有するため、第2形状で第1形状の座標枠を上書きしてはならない。上書きすると形状対が自身と衝突判定され、完全に重なったものとして扱わ
    /// れる（ボディが互いに沈み込み、積み上げた物体がいつまでも跳ね続ける）。
    /// </summary>
    private PolytopeFrame?Frame(in PhysicsCollisionShape Shape,bool First,PolytopeFrame?Taken=null){
        PolytopeFrame Frame;
        if(Shape.Collider.Shape==ColliderShape.Box){
            Frame=First?this.FirstBoxFrame:this.SecondBoxFrame;
            if(Frame.Valid&&Frame.Position==Shape.Position&&Frame.Rotation==Shape.Rotation&&Frame.ColliderCenter==Shape.Collider.Center&&Frame.HalfExtents==Shape.Collider.HalfExtents){ return Frame; }
        }
        else {
            var Asset=Shape.Asset!;
            if(!this.PolyhedronFrames.TryGetValue(Asset,out var Existing)){
                if(this.PolyhedronFrames.Count>=MaximumPolyhedronFrames){ this.PolyhedronFrames.Clear(); }
                Existing=new PolytopeFrame();
                this.PolyhedronFrames.Add(Asset,Existing);
            }
            Frame=Existing;
            if(ReferenceEquals(Frame,Taken)){ Frame=this.SharedSecondFrame; }
            else if(Frame.Valid&&Frame.Position==Shape.Position&&Frame.Rotation==Shape.Rotation&&Frame.ColliderCenter==Shape.Collider.Center){ return Frame; }
        }
        Frame.Valid=false;
        var Corners=PolytopeCorners(Shape,Frame.Corners);
        var Planes=PolytopePlanes(Shape,Frame.Normals,Frame.Distances);
        if(Corners<4||Planes<4||!TryColliderCenter(Shape,out var Center)){ return null; }
        Frame.CornerCount=Corners;
        Frame.PlaneCount=Planes;
        Frame.Center=Center;
        var Reach=LogicalScalar.Zero;
        for(var Index=0;Index<Corners;Index++){
            var Distance=(Frame.Corners[Index]-Center).LengthSquared;
            if(Distance>Reach){ Reach=Distance; }
        }
        Frame.Radius=LogicalScalar.TrySquareRoot(Reach,out var Radius)?Radius:LogicalScalar.MaxValue;
        Frame.Position=Shape.Position;
        Frame.Rotation=Shape.Rotation;
        Frame.ColliderCenter=Shape.Collider.Center;
        Frame.HalfExtents=Shape.Collider.HalfExtents;
        Frame.Valid=true;
        return Frame;
    }

    /// <summary>
    /// ADR-0078/M0113：GJK/EPA の代わりに、2つの凸形状（それぞれボックスまたは凸多面体で、少なくとも一方は多面体）を面平面で判定する。形状を囲む2つの球が接触しなければ、すぐに非接触と
    /// 判断する。もう一方の形状の全頂点が面の外側（0.1 mmを超える距離）にある面が見つかれば、非接触と判断する。そうでなければ、重なりが最小の面から法線と深さを求め、その面について相手形状の内部にあるす
    /// べての角を接触パッチにする（<see cref="ExpandPolytopeManifold"/> と同様）。内部の角が見つからず判定できない場合は false を返す。辺同士の交差や、辺と辺の軸だ
    /// けで分離できる2形状などを、GJK/EPA で判定する。
    /// </summary>
    private bool TryPolytopeContact(in PhysicsCollisionShape First,in PhysicsCollisionShape Second,out bool Touching,out PhysicsContactCandidate Contact){
        Touching=false;
        Contact=default;
        if(this.Frame(First,true) is not { }A||this.Frame(Second,false,A) is not { }B){ return false; }
        var Reach=A.Radius+B.Radius+EpaTolerance;
        if((B.Center-A.Center).LengthSquared>Reach*Reach){ return true; }
        var FirstCorners=A.CornerSpan;
        var SecondCorners=B.CornerSpan;
        var FirstNormals=A.NormalSpan;
        var SecondNormals=B.NormalSpan;
        var FirstDistances=A.DistanceSpan;
        var SecondDistances=B.DistanceSpan;
        var BestDepth=LogicalScalar.MaxValue;
        var Normal=LogicalVector3.UnitU;
        uint Feature=0;
        for(var Plane=0;Plane<FirstNormals.Length;Plane++){
            var Lowest=LogicalScalar.MaxValue;
            foreach(var Corner in SecondCorners){
                var Height=LogicalVector3.Dot(FirstNormals[Plane],Corner);
                if(Height<Lowest){ Lowest=Height; }
            }
            var Gap=Lowest-FirstDistances[Plane];
            if(Gap>EpaTolerance){ return true; }
            if(-Gap<BestDepth){ BestDepth=-Gap;Normal=FirstNormals[Plane];Feature=(uint)Plane; }
        }
        for(var Plane=0;Plane<SecondNormals.Length;Plane++){
            var Lowest=LogicalScalar.MaxValue;
            foreach(var Corner in FirstCorners){
                var Height=LogicalVector3.Dot(SecondNormals[Plane],Corner);
                if(Height<Lowest){ Lowest=Height; }
            }
            var Gap=Lowest-SecondDistances[Plane];
            if(Gap>EpaTolerance){ return true; }
            if(-Gap<BestDepth){ BestDepth=-Gap;Normal=-SecondNormals[Plane];Feature=(uint)(MaximumPolytopePlanes+Plane); }
        }
        var FirstTop=LogicalScalar.MinValue;
        foreach(var Corner in FirstCorners){
            var Height=LogicalVector3.Dot(Corner,Normal);
            if(Height>FirstTop){ FirstTop=Height; }
        }
        var SecondBottom=LogicalScalar.MaxValue;
        foreach(var Corner in SecondCorners){
            var Height=LogicalVector3.Dot(Corner,Normal);
            if(Height<SecondBottom){ SecondBottom=Height; }
        }
        Span<LogicalVector3>Points=stackalloc LogicalVector3[MaximumPolytopeVertices*2];
        Span<LogicalScalar>Depths=stackalloc LogicalScalar[MaximumPolytopeVertices*2];
        var Count=0;
        var Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
        var Limit=Max(BestDepth,LogicalScalar.Zero)+EpaTolerance;
        foreach(var Corner in SecondCorners){
            var Depth=FirstTop-LogicalVector3.Dot(Corner,Normal);
            if(Depth<-EpaTolerance||!Inside(FirstNormals,FirstDistances,Corner)){ continue; }
            Points[Count]=Corner+Normal*(Max(Depth,LogicalScalar.Zero)*Half);
            Depths[Count++]=Max(EpaTolerance,Min(Depth,Limit));
        }
        foreach(var Corner in FirstCorners){
            var Depth=LogicalVector3.Dot(Corner,Normal)-SecondBottom;
            if(Depth<-EpaTolerance||!Inside(SecondNormals,SecondDistances,Corner)){ continue; }
            Points[Count]=Corner-Normal*(Max(Depth,LogicalScalar.Zero)*Half);
            Depths[Count++]=Max(EpaTolerance,Min(Depth,Limit));
        }
        if(Count==0){ return false; }
        Span<LogicalVector3>Patch=stackalloc LogicalVector3[4];
        Span<LogicalScalar>PatchDepths=stackalloc LogicalScalar[4];
        var Kept=SpreadPoints(Points[..Count],Depths[..Count],Patch,PatchDepths);
        Contact=PhysicsContactCandidate.CreatePatch(Normal,Feature,Patch,PatchDepths,Kept);
        Touching=true;
        return true;
    }
    /// <summary>
    /// <paramref name="Points"/> から最大4点を選び、入力された順序のまま返す。これにより各点はティック間で同じスロットとウォームスタート済み力積を保つ。まず最も深い点を選び、その後
    /// は選択済みの点から最も遠い点を順に選び、接触領域を幅広く覆うパッチにする。深さの差が0.5 mm以内、または距離の差が1 mm²以内なら同値として、先に現れた点を選ぶ。面に載ったボディの角は深さがほぼ
    /// 等しいため、毎ティック選択が入れ替わるとウォームスタート済み力積が別の角へ移り、ボディが揺れ続ける。
    /// </summary>
    private static int SpreadPoints(ReadOnlySpan<LogicalVector3>Points,ReadOnlySpan<LogicalScalar>Depths,Span<LogicalVector3>Patch,Span<LogicalScalar>PatchDepths){
        var First=0;
        for(var Index=1;Index<Points.Length;Index++){
            if(Depths[Index]>Depths[First]+SameDepth){ First=Index; }
        }
        Span<bool>Taken=stackalloc bool[Points.Length];
        Span<int>Chosen=stackalloc int[4];
        Taken[First]=true;
        Chosen[0]=First;
        var Kept=1;
        // M0124：まず面に入り込んだ点（深さが正の点）を広げて配置し、残ったスロットにだけ予測接触点（深さが負の、
        // 面から少し浮いた角など）を割り当てる。遠く離れた予測接触点が、ボディを支える稜線の反対端より先に選ばれ、ボディが1点を軸に回転していた。
        var EnteredOnly=Depths[First]>LogicalScalar.Zero;
        while(Kept<4){
            var Best=-1;
            var BestDistance=LogicalScalar.Zero;
            for(var Index=0;Index<Points.Length;Index++){
                if(Taken[Index]||EnteredOnly&&Depths[Index]<=LogicalScalar.Zero){ continue; }
                var Nearest=LogicalScalar.MaxValue;
                for(var Pick=0;Pick<Kept;Pick++){
                    var Distance=(Points[Index]-Points[Chosen[Pick]]).LengthSquared;
                    if(Distance<Nearest){ Nearest=Distance; }
                }
                if(Best<0?Nearest>LogicalScalar.Zero:Nearest>BestDistance+SameDistanceSquared){ Best=Index;BestDistance=Nearest; }
            }
            // 既に採用した点と重なる（0.1 mm以内の）点は、追加しても効果がない。
            if(Best<0||BestDistance<=EpaTolerance*EpaTolerance){
                if(EnteredOnly){ EnteredOnly=false;continue; }
                break;
            }
            Taken[Best]=true;
            Chosen[Kept++]=Best;
        }
        Chosen[..Kept].Sort();
        for(var Index=0;Index<Kept;Index++){
            Patch[Index]=Points[Chosen[Index]];
            PatchDepths[Index]=Depths[Chosen[Index]];
        }
        return Kept;
    }
    private static readonly LogicalScalar SameDepth=LogicalScalar.FromSingle(0.0005f);
    private static readonly LogicalScalar SameDistanceSquared=LogicalScalar.FromSingle(0.000001f);

    private readonly List<PhysicsContactCandidate>CompoundCandidates=[];
    private readonly List<int>CompoundChildren=[];
    private readonly List<LogicalVector3>CompoundPoints=[];
    private readonly List<LogicalScalar>CompoundDepths=[];

    /// <summary>複合形状の子要素1つ。複合形状と同じ位置に配置する。</summary>
    private static PhysicsCollisionShape CompoundChild(in PhysicsCollisionShape Compound,PhysicsShapeAsset Child)=>
        new(Compound.Collider with { Shape=ColliderShape.ConvexAsset,Asset=Child.Id },Compound.Position,Compound.Rotation,Child);

    private readonly List<PhysicsContactCandidate>ChildPatches=[];
    private readonly List<bool>CompoundUsed=[];

    /// <summary>
    /// ADR-0078/M0113：複合形状（複数の破片からなる破片）と別の形状、または複合形状同士の接触を求める。境界が重なる各子要素を、それぞれ相手形状（または相手側の子要素）と判定する。子要素と
    /// メッシュの接触では、面の向きごとにパッチができる。最も深い子要素パッチの法線を基準にし、そこから約18度以内にある各パッチの点を1つの最大4点のパッチにまとめる（深い点を先にし、その後は広がりが最大に
    /// なるよう選ぶ）。M0124：三角形メッシュとの接触では、残った子要素パッチも同様に次の向きとしてまとめる（残った点のうち最深のものから始め、最大
    /// <see cref="MaximumContactPatches"/> 個）。先行パッチの点から1 mm以内にある点は除外する。凸形状との接触では向きは1つだけとする。この処理により、複数の破片が接地
    /// する破片は、接触するメッシュの各面で支えられる。
    /// </summary>
    private bool TryCompoundContacts(in PhysicsCollisionShape First,in PhysicsCollisionShape Second,List<PhysicsContactCandidate>Patches){
        this.CompoundCandidates.Clear();
        this.CompoundChildren.Clear();
        var FirstCompound=First.Collider.Shape==ColliderShape.Compound;
        var SecondCompound=Second.Collider.Shape==ColliderShape.Compound;
        if(FirstCompound&&First.Asset is not { Kind:PhysicsShapeAssetKind.Compound }||SecondCompound&&Second.Asset is not { Kind:PhysicsShapeAssetKind.Compound }){ return false; }
        var FirstChildren=FirstCompound?First.Asset!.Children.Count:1;
        var SecondChildren=SecondCompound?Second.Asset!.Children.Count:1;
        var SecondWhole=SecondCompound?default:this.CalculateBounds(Second);
        for(var FirstIndex=0;FirstIndex<FirstChildren;FirstIndex++){
            var FirstShape=FirstCompound?CompoundChild(First,First.Asset!.Children[FirstIndex]):First;
            var FirstBounds=this.CalculateBounds(FirstShape);
            if(!SecondCompound&&!FirstBounds.Intersects(SecondWhole)){ continue; }
            for(var SecondIndex=0;SecondIndex<SecondChildren;SecondIndex++){
                var SecondShape=SecondCompound?CompoundChild(Second,Second.Asset!.Children[SecondIndex]):Second;
                if(SecondCompound&&!FirstBounds.Intersects(this.CalculateBounds(SecondShape))){ continue; }
                this.ChildPatches.Clear();
                if(!this.TryCreateContacts(FirstShape,SecondShape,this.ChildPatches)){ continue; }
                foreach(var Candidate in this.ChildPatches){
                    this.CompoundCandidates.Add(Candidate);
                    this.CompoundChildren.Add(FirstIndex*SecondChildren+SecondIndex);
                }
            }
        }
        if(this.CompoundCandidates.Count==0){ return false; }
        this.CompoundUsed.Clear();
        for(var Index=0;Index<this.CompoundCandidates.Count;Index++){ this.CompoundUsed.Add(false); }
        var Before=Patches.Count;
        Span<LogicalVector3>Patch=stackalloc LogicalVector3[4];
        Span<LogicalScalar>PatchDepths=stackalloc LogicalScalar[4];
        // 凸形状（箱形の床、別の破片）との接触は従来どおり1つの向きとする。M0124 で向きを増やしたところ、
        // 破壊サンプルの破片の落ち着き方が変わり、爆発が最後の箱に届かなくなった。1つのボディ内に複数の面向きがあるのはメッシュだけ。
        var MostPatches=First.Collider.Shape==ColliderShape.StaticTriangleMesh||Second.Collider.Shape==ColliderShape.StaticTriangleMesh?MaximumContactPatches:1;
        while(Patches.Count-Before<MostPatches){
            var Deepest=-1;
            for(var Index=0;Index<this.CompoundCandidates.Count;Index++){
                if(this.CompoundUsed[Index]){ continue; }
                if(Deepest<0||this.CompoundCandidates[Index].Penetration>this.CompoundCandidates[Deepest].Penetration+SameDepth){ Deepest=Index; }
            }
            if(Deepest<0){ break; }
            var Best=this.CompoundCandidates[Deepest];
            this.CompoundUsed[Deepest]=true;
            if(!Best.Normal.TryNormalize(out var Normal)){ continue; }
            this.CompoundPoints.Clear();
            this.CompoundDepths.Clear();
            for(var Index=0;Index<this.CompoundCandidates.Count;Index++){
                var Candidate=this.CompoundCandidates[Index];
                if(Index!=Deepest&&(this.CompoundUsed[Index]||!Candidate.Normal.TryNormalize(out var CandidateNormal)||LogicalVector3.Dot(CandidateNormal,Normal)<SameOrientation)){ continue; }
                this.CompoundUsed[Index]=true;
                for(var Point=0;Point<Candidate.Count;Point++){
                    var Position=Candidate.GetPosition(Point);
                    if(NearAnEarlierPatch(Patches,Before,Position)){ continue; }
                    this.CompoundPoints.Add(Position);
                    this.CompoundDepths.Add(Candidate.GetPenetration(Point));
                }
            }
            if(this.CompoundPoints.Count==0){ continue; }
            var Kept=SpreadPoints(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.CompoundPoints),System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.CompoundDepths),Patch,PatchDepths);
            // 形状要素には最深の子要素対を記録し、ソルバーのウォームスタートがティックごとに同じ子要素を追跡できるようにする。
            var Feature=unchecked(((uint)this.CompoundChildren[Deepest]<<16)^Best.FeatureId);
            Patches.Add(PhysicsContactCandidate.CreatePatch(Best.Normal,Feature,Patch,PatchDepths,Kept) with { SurfaceNormal=Best.SurfaceNormal });
        }
        return Patches.Count>Before;
    }

    /// <summary><paramref name="Position"/> が、<paramref name="Before"/> 以降に追加されたパッチのいずれかの点から1 mm以内にあるか（同じ点をすでに保持しているか）。</summary>
    private static bool NearAnEarlierPatch(List<PhysicsContactCandidate>Patches,int Before,LogicalVector3 Position){
        for(var Index=Before;Index<Patches.Count;Index++){
            var Earlier=Patches[Index];
            for(var Point=0;Point<Earlier.Count;Point++){
                if((Earlier.GetPosition(Point)-Position).LengthSquared<=SameDistanceSquared){ return true; }
            }
        }
        return false;
    }

    internal PhysicsAabb CalculateBounds(in PhysicsCollisionShape Shape){
        if(Shape.Collider.Shape is not (ColliderShape.ConvexAsset or ColliderShape.StaticTriangleMesh or ColliderShape.Compound)){
            // キャラクターとスプリングアームの問い合わせでは、ティックごとに同じカプセルの境界を何十回も求める。その
            // サポートオフセットは基本形状の寸法と回転だけで決まる。
            ShapeProxy PrimitiveProxy=new(Shape);
            PrimitiveBoundsKey Key=new(Shape.Collider.Shape,Shape.Collider.Center,Shape.Collider.HalfExtents,Shape.Collider.Radius,Shape.Collider.HalfHeight,Shape.Rotation);
            if(!this.PrimitiveBounds.TryGetValue(Key,out AssetBoundsEntry Primitive)){
                if(this.PrimitiveBounds.Count>=MaximumPrimitiveBoundsEntries){ this.PrimitiveBounds.Clear(); }
                Primitive=CreateBoundsEntry(PrimitiveProxy,Shape.Rotation,Shape.Collider.Center);
                this.PrimitiveBounds.Add(Key,Primitive);
            }
            return OffsetBounds(PrimitiveProxy.Center,Primitive);
        }
        if(Shape.Asset is null){ throw new InvalidOperationException("Cooked shape asset is unavailable."); }
        // アセットのサポート判定は全頂点を走査し、静的メッシュの境界も移動していないのにティックごとに複数回求める。
        // プロキシ中心からの6つのサポート点は、
        // アセット、回転、コライダー中心だけで決まり、平行移動は正確な加算なので、キャッシュした
        // エントリーから CalculateBoundsUncached とビット単位で同じ結果を再現できる。
        ShapeProxy Proxy=new(Shape);
        if(!this.AssetBounds.TryGetValue(Shape.Asset,out AssetBoundsEntry Entry)
            ||Entry.Rotation!=Shape.Rotation||Entry.ColliderCenter!=Shape.Collider.Center){
            Entry=CreateBoundsEntry(Proxy,Shape.Rotation,Shape.Collider.Center);
            this.AssetBounds[Shape.Asset]=Entry;
        }
        return OffsetBounds(Proxy.Center,Entry);
    }

    private static AssetBoundsEntry CreateBoundsEntry(in ShapeProxy Proxy,LogicalQuaternion Rotation,LogicalVector3 ColliderCenter)=>new(
        Rotation,ColliderCenter,
        Proxy.Support(LogicalVector3.UnitR)-Proxy.Center,Proxy.Support(LogicalVector3.UnitU)-Proxy.Center,Proxy.Support(LogicalVector3.UnitF)-Proxy.Center,
        Proxy.Support(-LogicalVector3.UnitR)-Proxy.Center,Proxy.Support(-LogicalVector3.UnitU)-Proxy.Center,Proxy.Support(-LogicalVector3.UnitF)-Proxy.Center);

    private static PhysicsAabb OffsetBounds(LogicalVector3 Center,in AssetBoundsEntry Entry)=>new(
        new LogicalVector3((Center+Entry.NegativeR).R,(Center+Entry.NegativeU).U,(Center+Entry.NegativeF).F),
        new LogicalVector3((Center+Entry.PositiveR).R,(Center+Entry.PositiveU).U,(Center+Entry.PositiveF).F));

    internal static PhysicsAabb CalculateBoundsUncached(in PhysicsCollisionShape Shape){
        if(Shape.Collider.Shape is ColliderShape.ConvexAsset or ColliderShape.StaticTriangleMesh or ColliderShape.Compound){
            if(Shape.Asset is null){ throw new InvalidOperationException("Cooked shape asset is unavailable."); }
        }
        ShapeProxy Proxy=new(Shape);
        LogicalVector3 Maximum=new(
            Proxy.Support(LogicalVector3.UnitR).R,
            Proxy.Support(LogicalVector3.UnitU).U,
            Proxy.Support(LogicalVector3.UnitF).F);
        LogicalVector3 Minimum=new(
            Proxy.Support(-LogicalVector3.UnitR).R,
            Proxy.Support(-LogicalVector3.UnitU).U,
            Proxy.Support(-LogicalVector3.UnitF).F);
        return new PhysicsAabb(Minimum,Maximum);
    }

    private bool TryMeshContacts(
        in PhysicsCollisionShape Convex,in PhysicsCollisionShape Mesh,bool MeshIsFirst,
        List<PhysicsContactCandidate>Patches){
        if(!IsConvex(Convex.Collider.Shape)||Mesh.Asset is not { Kind:PhysicsShapeAssetKind.StaticTriangleMesh } Asset){ return false; }
        PhysicsAabb WorldBounds=this.CalculateBounds(Convex);
        if(!TryTransformBoundsToLocal(WorldBounds,Mesh.Position,Mesh.Rotation,out PhysicsAabb LocalBounds)){ return false; }
        this.TriangleCandidates.Clear();
        Asset.QueryTriangles(LocalBounds,this.TriangleCandidates);
        // ADR-0078/M0109：メッシュ面に載る、または押し付けられたボックスは面で接触するため、接触面より下にある角ごとに接触点を作る。
        // （最大4点）。最深点1つだけでは、メッシュ床の上のボックス（車体）が揺れ、回転し、沈み込んだ。ADR-0078/M0113：凸多面体
        // （破片）も同様に、独自の角を使って接触させる。M0124：面の向きごとにパッチを作り、形状の面に食い込むメッシュの稜線も扱う。
        // 辺同士の接触は GJK に任せる。
        if(HasPolytope(Convex)&&this.TryPolytopeMeshContacts(Convex,Mesh,Asset,MeshIsFirst,Patches)){ return true; }
        bool Found=false;
        PhysicsContactCandidate Best=default;
        ShapeProxy ConvexProxy=new(Convex);
        foreach(int Triangle in this.TriangleCandidates){
            Asset.GetTriangle(Triangle,out LogicalVector3 A,out LogicalVector3 B,out LogicalVector3 C);
            if(Convex.Collider.Shape==ColliderShape.Sphere
                &&TrySphereTriangleContact(Convex,Mesh,A,B,C,MeshIsFirst,checked((uint)Triangle),out PhysicsContactCandidate SphereContact)){
                if(!Found||SphereContact.Penetration>Best.Penetration){ Best=SphereContact;Found=true; }
                continue;
            }
            if(Convex.Collider.Shape==ColliderShape.Capsule){
                if(TryCapsuleTriangleContact(Convex,Mesh,A,B,C,MeshIsFirst,checked((uint)Triangle),out PhysicsContactCandidate CapsuleContact)
                    &&(!Found||CapsuleContact.Penetration>Best.Penetration)){ Best=CapsuleContact;Found=true; }
                continue;
            }
            ShapeProxy TriangleProxy=new(Mesh.Position,Mesh.Rotation,A,B,C);
            bool Hit=MeshIsFirst
                ?TryConvexContact(TriangleProxy,ConvexProxy,checked((uint)Triangle),out PhysicsContactCandidate Candidate)
                :TryConvexContact(ConvexProxy,TriangleProxy,checked((uint)Triangle),out Candidate);
            if(Hit&&(!Found||Candidate.Penetration>Best.Penetration)){
                Best=Candidate;
                Found=true;
            }
        }
        if(!Found){ return false; }
        Patches.Add(Best);
        return true;
    }

    private const int MaximumRidgePoints=64;
    /// <summary>メッシュの稜線が形状の面に入り込むパッチの特徴ビット（メッシュ三角形の下にある形状の角ではない）。</summary>
    private const uint RidgeFeature=0x8000_0000u;

    /// <summary>
    /// メッシュの候補三角形とボックスまたは凸多面体との接触を、面の向きごとにパッチとして求める（ADR-0078/M0109、M0113、M0124）。各三角形について、分離軸テストと同様に2種類の重なりを
    /// 比較する。1つ目は、形状の角が三角形の面よりどれだけ深く入り込んでいるか（形状中心側の面を採用するため、薄い壁に押し付けられたボックスは元来た側へ押し戻される）。三角形内に投影され、三角形面から
    /// 形状内部の深さの範囲にある角と、<see cref="BoxMeshSpeculativeMargin"/> 以内の上方にある角を予測接触点として含める。2つ目は、三角形が形状の面を通じてどれだけ内部
    /// に入り込むか（車の床に縁石上端の稜線が入り込む場合など）。その深さは、形状の他の面の内側にある三角形部分を面から測る。重なりの小さい方を、最も分離しにくい軸として採用する。三角形の面が形状の面とほぼ
    /// 平行（約18度以内）なら、角だけを使う（ボックスの下の床など）。M0124 より前は角だけを数え、最深の角が属する面の角だけを使っていた。そのため、縁石の斜面と地面をまたぐ車が地面の支えを失う、
    /// 縁石上端の稜線が車体の床に見えないまま入り込む、斜面の小さな三角形の面がボックス中心より上を通るとボックスを引き下げる、といった問題があった。各角は最も深い面について一度だけ数える。接触パッチでは、最
    /// も深く入り込んだ点が最初の向きを決め、法線が約18度以内の各点を同じパッチに加える。残りから最も深い点を選び、次のパッチを作る。最大数は
    /// <see cref="MaximumContactPatches"/>。ボックスでは候補順（角が先）のまま最深の4点を使い、多面体では最深点に続けて最も広がる点を選ぶ。先行パッチの点から1 mm以内に
    /// ある点は除外する。入り込んだ点がない場合は false（辺同士の接触は GJK に渡す）。
    /// </summary>
    private bool TryPolytopeMeshContacts(in PhysicsCollisionShape Convex,in PhysicsCollisionShape Mesh,PhysicsShapeAsset Asset,bool MeshIsFirst,List<PhysicsContactCandidate>Patches){
        if(!TryColliderCenter(Convex,out var Center)){ return false; }
        var IsBox=Convex.Collider.Shape==ColliderShape.Box;
        Span<LogicalVector3>Corners=stackalloc LogicalVector3[MaximumPolytopeVertices];
        var CornerCount=PolytopeCorners(Convex,Corners);
        if(CornerCount<4){ return false; }
        Corners=Corners[..CornerCount];
        Span<LogicalVector3>PlaneNormals=stackalloc LogicalVector3[MaximumPolytopePlanes];
        Span<LogicalScalar>PlaneDistances=stackalloc LogicalScalar[MaximumPolytopePlanes];
        var PlaneCount=PolytopePlanes(Convex,PlaneNormals,PlaneDistances);
        if(PlaneCount<4){ return false; }
        PlaneNormals=PlaneNormals[..PlaneCount];
        PlaneDistances=PlaneDistances[..PlaneCount];
        var Half=Convex.Collider.HalfExtents;
        LogicalVector3 AxisR=default,AxisU=default,AxisF=default;
        if(IsBox&&(!Convex.Rotation.TryRotate(LogicalVector3.UnitR,out AxisR)||!Convex.Rotation.TryRotate(LogicalVector3.UnitU,out AxisU)||!Convex.Rotation.TryRotate(LogicalVector3.UnitF,out AxisF))){ return false; }
        Span<LogicalScalar>CornerDepth=stackalloc LogicalScalar[CornerCount];
        Span<int>CornerTriangle=stackalloc int[CornerCount];
        Span<LogicalVector3>CornerNormal=stackalloc LogicalVector3[CornerCount];
        Span<LogicalScalar>Heights=stackalloc LogicalScalar[CornerCount];
        Span<LogicalScalar>TriangleDepths=stackalloc LogicalScalar[CornerCount];
        CornerTriangle.Fill(-1);
        Span<LogicalVector3>RidgePoints=stackalloc LogicalVector3[MaximumRidgePoints];
        Span<LogicalScalar>RidgeDepths=stackalloc LogicalScalar[MaximumRidgePoints];
        Span<LogicalVector3>RidgeNormals=stackalloc LogicalVector3[MaximumRidgePoints];
        Span<int>RidgeTriangles=stackalloc int[MaximumRidgePoints];
        var RidgeCount=0;
        Span<LogicalVector3>Polygon=stackalloc LogicalVector3[3+MaximumPolytopePlanes];
        Span<LogicalVector3>Clipped=stackalloc LogicalVector3[3+MaximumPolytopePlanes];
        foreach(int Triangle in this.TriangleCandidates){
            Asset.GetTriangle(Triangle,out var A,out var B,out var C);
            if(!Mesh.Rotation.TryRotate(A,out A)||!Mesh.Rotation.TryRotate(B,out B)||!Mesh.Rotation.TryRotate(C,out C)){ continue; }
            A+=Mesh.Position;B+=Mesh.Position;C+=Mesh.Position;
            var Face=FaceToward(A,B,C,Center);
            if(Face.LengthSquared.IsZero){ continue; }
            var Highest=LogicalScalar.MinValue;
            var Lowest=LogicalScalar.MaxValue;
            for(var Index=0;Index<CornerCount;Index++){
                var Height=LogicalVector3.Dot(Corners[Index]-A,Face);
                Heights[Index]=Height;
                if(Height>Highest){ Highest=Height; }
                if(Height<Lowest){ Lowest=Height; }
            }
            // 面に沿った形状の深さ：形状全体より深い角は、面の向こう側にある。
            LogicalScalar Spread;
            if(IsBox){
                var Thickness=Abs(LogicalVector3.Dot(AxisR,Face))*Half.R+Abs(LogicalVector3.Dot(AxisU,Face))*Half.U+Abs(LogicalVector3.Dot(AxisF,Face))*Half.F;
                Spread=Thickness+Thickness;
            }
            else {
                Spread=Highest-Lowest;
            }
            var Deepest=-Spread;
            var Parallel=false;
            for(var Plane=0;Plane<PlaneCount&&!Parallel;Plane++){
                if(Abs(LogicalVector3.Dot(PlaneNormals[Plane],Face))>=SameOrientation){ Parallel=true; }
            }
            var CornerOverlap=LogicalScalar.MinValue;
            for(var Index=0;Index<CornerCount;Index++){
                TriangleDepths[Index]=LogicalScalar.MinValue;
                var Height=Heights[Index];
                if(Height>=TriangleHalfThickness+BoxMeshSpeculativeMargin||Height<=Deepest){ continue; }
                var Projected=Corners[Index]-Face*Height;
                if(!InsideTriangle(Projected,A,B,C)){ continue; }
                var Depth=TriangleHalfThickness-Height;
                // M0124：傾いた面の下に実際に入り込んだ角では、その角を囲む形状の側面が面の側にあるため、その真上の三角形上の点は内部に含まれる。
                // （角から面の法線方向に進んでも、その角で交わる各面の範囲内にとどまる）。一方、
                // 誤った側にある面（その平面が形状中心より上を通る）では、その点はすぐに形状の外へ出る。距離は角の「深さ」と同程度。
                if(!Parallel&&Depth>LogicalScalar.Zero&&Outside(PlaneNormals,PlaneDistances,Projected)>TriangleHalfThickness+Depth/LogicalScalar.FromInt64(2)){ continue; }
                TriangleDepths[Index]=Depth;
                if(Depth>CornerOverlap){ CornerOverlap=Depth; }
            }
            // 形状の各面を通じた、三角形の形状内部への入り込み（面とほぼ平行な三角形では発生しない）。
            var RidgePlane=-1;
            var RidgeOverlap=LogicalScalar.MaxValue;
            // 形状の面で分離される三角形（接触許容幅を超えるもの）には、入り込んだ角がない。その下にあるように見える角は、
            // 平面を辺の外まで延長した部分の下にあり、形状中心側にあるのは平面が中心より上を通るためだけである（大きなボックスの横にある小さな斜面三角形や、登坂中に傾いたボックスの下など）。予測接触点は残し、面に届く分だけ支える。テストは、稜線の候補となる各三角形と、入り込んだ角を持つ各三角形（1ティックに数個）に対して行う。形状が上に突き出ているだけで接触しない平行三角形には行わない。
            // 接触許容幅以内で面に触れているだけの傾斜三角形にも、入り込んだ角はない。傾いた面の下に実際に入り込んだ角では、その
            // 三角形は近くにある形状のすべての面を越えて内部に達する。平行な三角形では、角を残す（床の三角形の継ぎ目にある角など）。
            // その形状が上に突き出ているだけで接触しない。
            var NoEnteredCorners=false;
            if(!Parallel||CornerOverlap>LogicalScalar.Zero){
                var Separated=false;
                for(var Plane=0;Plane<PlaneCount;Plane++){
                    var Normal=PlaneNormals[Plane];
                    var Reach=PlaneDistances[Plane]-Min3(LogicalVector3.Dot(Normal,A),LogicalVector3.Dot(Normal,B),LogicalVector3.Dot(Normal,C));
                    if(Reach<-TriangleHalfThickness){ NoEnteredCorners=true;break; }
                    if(Reach<=LogicalScalar.Zero){ Separated=true; }
                    if(Reach<RidgeOverlap){ RidgeOverlap=Reach;RidgePlane=Plane; }
                }
                // 面に接するだけの傾斜三角形にも、入り込んだ角はない。傾いた面の下に実際に入り込んだ角では、
                // 三角形が近くにある形状のすべての面を越えて内部に達する。平行な三角形では角を残す（床の三角形の継ぎ目にある角など）。
                if(Separated&&!Parallel){ NoEnteredCorners=true; }
                if(Parallel||Separated||NoEnteredCorners){ RidgePlane=-1; }
            }
            // どちらの重なりも、点の深さと同様に三角形の半分の厚みを含める。小さい方が、2形状を最も分離しにくい軸を表す。
            if(RidgePlane>=0&&RidgeOverlap+TriangleHalfThickness<CornerOverlap||RidgePlane>=0&&CornerOverlap<=LogicalScalar.Zero){
                // 三角形のうち、形状の他のすべての面の内側にある部分。その頂点が稜線側の面の内側にあれば接触点となり、その面に沿って押し戻す。
                Polygon[0]=A;Polygon[1]=B;Polygon[2]=C;
                var Count=3;
                for(var Plane=0;Plane<PlaneCount&&Count>0;Plane++){
                    if(Plane==RidgePlane){ continue; }
                    Count=ClipPolygonToPlane(Polygon[..Count],Clipped,PlaneNormals[Plane],PlaneDistances[Plane]);
                    Clipped[..Count].CopyTo(Polygon);
                }
                // 深さには三角形の半分の厚みも含める。これにより、面にちょうど触れる稜線を落として次の判定で再検出することがない。
                var RidgeNormal=PlaneNormals[RidgePlane];
                var RidgeStart=RidgeCount;
                for(var Index=0;Index<Count&&RidgeCount<MaximumRidgePoints;Index++){
                    var Depth=TriangleHalfThickness+PlaneDistances[RidgePlane]-LogicalVector3.Dot(RidgeNormal,Polygon[Index]);
                    if(Depth<=LogicalScalar.Zero){ continue; }
                    RidgePoints[RidgeCount]=Polygon[Index];
                    RidgeDepths[RidgeCount]=Depth;
                    RidgeNormals[RidgeCount]=-RidgeNormal;
                    RidgeTriangles[RidgeCount++]=Triangle;
                }
                if(RidgeCount>RidgeStart){ continue; }
                // 稜線側の重なりが小さくても、接触許容幅を超える深さがない場合、角の重なりも実際の接触ではない。
                NoEnteredCorners=true;
            }
            for(var Index=0;Index<CornerCount;Index++){
                var Depth=TriangleDepths[Index];
                if(Depth==LogicalScalar.MinValue||NoEnteredCorners&&Depth>LogicalScalar.Zero||CornerTriangle[Index]>=0&&Depth<=CornerDepth[Index]){ continue; }
                CornerDepth[Index]=Depth;
                CornerTriangle[Index]=Triangle;
                CornerNormal[Index]=Face;
            }
        }
        // すべての候補点。角（角の順）を先にし、次に稜線の点（三角形の順）を並べる。
        var Total=0;
        Span<LogicalVector3>Points=stackalloc LogicalVector3[MaximumPolytopeVertices+MaximumRidgePoints];
        Span<LogicalScalar>Depths=stackalloc LogicalScalar[MaximumPolytopeVertices+MaximumRidgePoints];
        Span<LogicalVector3>Normals=stackalloc LogicalVector3[MaximumPolytopeVertices+MaximumRidgePoints];
        Span<uint>Features=stackalloc uint[MaximumPolytopeVertices+MaximumRidgePoints];
        for(var Index=0;Index<CornerCount;Index++){
            if(CornerTriangle[Index]<0){ continue; }
            Points[Total]=Corners[Index];
            Depths[Total]=CornerDepth[Index];
            Normals[Total]=CornerNormal[Index];
            Features[Total++]=checked((uint)CornerTriangle[Index]);
        }
        for(var Index=0;Index<RidgeCount;Index++){
            Points[Total]=RidgePoints[Index];
            Depths[Total]=RidgeDepths[Index];
            Normals[Total]=RidgeNormals[Index];
            Features[Total++]=checked((uint)RidgeTriangles[Index])|RidgeFeature;
        }
        Span<bool>Used=stackalloc bool[Total];
        Span<LogicalVector3>Members=stackalloc LogicalVector3[Total];
        Span<LogicalScalar>MemberDepths=stackalloc LogicalScalar[Total];
        Span<LogicalVector3>Patch=stackalloc LogicalVector3[4];
        Span<LogicalScalar>PatchDepths=stackalloc LogicalScalar[4];
        var Before=Patches.Count;
        while(Patches.Count-Before<MaximumContactPatches){
            // どの点も面に入り込んでいない場合は接触なし。許容幅内の点は予測接触点なので、実際に入り込んだパッチと同じ向きにのみ加える。
            var Seed=-1;
            for(var Index=0;Index<Total;Index++){
                if(!Used[Index]&&Depths[Index]>LogicalScalar.Zero&&(Seed<0||Depths[Index]>Depths[Seed])){ Seed=Index; }
            }
            if(Seed<0){ break; }
            var Normal=Normals[Seed];
            var MemberCount=0;
            for(var Index=0;Index<Total;Index++){
                if(Used[Index]||LogicalVector3.Dot(Normals[Index],Normal)<SameOrientation){ continue; }
                Used[Index]=true;
                if(NearAnEarlierPatch(Patches,Before,Points[Index])){ continue; }
                Members[MemberCount]=Points[Index];
                MemberDepths[MemberCount++]=Depths[Index];
            }
            if(MemberCount==0){ continue; }
            var Kept=IsBox
                ?DeepestFour(Members[..MemberCount],MemberDepths[..MemberCount],Patch,PatchDepths)
                :SpreadPoints(Members[..MemberCount],MemberDepths[..MemberCount],Patch,PatchDepths);
            // 接触法線は第1形状から第2形状へ向く。面法線はメッシュから形状へ向く。
            Patches.Add(PhysicsContactCandidate.CreatePatch(MeshIsFirst?Normal:-Normal,Features[Seed],Patch,PatchDepths,Kept) with { SurfaceNormal=Normal });
        }
        return Patches.Count>Before;
    }

    /// <summary>凸多面体の面平面に対する点の外側への距離（超過量の最大値。0以下なら内側）。</summary>
    private static LogicalScalar Outside(ReadOnlySpan<LogicalVector3>Normals,ReadOnlySpan<LogicalScalar>Distances,LogicalVector3 Point){
        var Result=LogicalScalar.MinValue;
        for(var Plane=0;Plane<Normals.Length;Plane++){
            var Excess=LogicalVector3.Dot(Normals[Plane],Point)-Distances[Plane];
            if(Excess>Result){ Result=Excess; }
        }
        return Result;
    }

    /// <summary><paramref name="Points"/> のうち最も深い4点（深さが同じなら先の点）を、入力と同じ順に返す。これにより同じ姿勢では同じパッチになる。</summary>
    private static int DeepestFour(ReadOnlySpan<LogicalVector3>Points,ReadOnlySpan<LogicalScalar>Depths,Span<LogicalVector3>Patch,Span<LogicalScalar>PatchDepths){
        Span<bool>Taken=stackalloc bool[Points.Length];
        for(var Pick=0;Pick<4;Pick++){
            var Next=-1;
            for(var Index=0;Index<Points.Length;Index++){
                if(!Taken[Index]&&(Next<0||Depths[Index]>Depths[Next])){ Next=Index; }
            }
            if(Next<0){ break; }
            Taken[Next]=true;
        }
        var Count=0;
        for(var Index=0;Index<Points.Length;Index++){
            if(!Taken[Index]){ continue; }
            Patch[Count]=Points[Index];
            PatchDepths[Count++]=Depths[Index];
        }
        return Count;
    }

    /// <summary>平面の内側（法線 · 点 ≤ 距離）にある凸多角形部分を <paramref name="Output"/> に書き込み、頂点数を返す。</summary>
    private static int ClipPolygonToPlane(ReadOnlySpan<LogicalVector3>Polygon,Span<LogicalVector3>Output,LogicalVector3 Normal,LogicalScalar Distance){
        var Count=0;
        for(var Index=0;Index<Polygon.Length;Index++){
            var Current=Polygon[Index];
            var Next=Polygon[(Index+1)%Polygon.Length];
            var CurrentSide=LogicalVector3.Dot(Normal,Current)-Distance;
            var NextSide=LogicalVector3.Dot(Normal,Next)-Distance;
            if(CurrentSide<=LogicalScalar.Zero){ Output[Count++]=Current; }
            if(CurrentSide<LogicalScalar.Zero&&NextSide>LogicalScalar.Zero||CurrentSide>LogicalScalar.Zero&&NextSide<LogicalScalar.Zero){
                var Fraction=CurrentSide/(CurrentSide-NextSide);
                Output[Count++]=Current+(Next-Current)*Fraction;
            }
        }
        return Count;
    }

    /// <summary>三角形の平面上にある <paramref name="Point"/> が、辺を含めて三角形 ABC の内側にあるか。</summary>
    private static bool InsideTriangle(LogicalVector3 Point,LogicalVector3 A,LogicalVector3 B,LogicalVector3 C){
        LogicalVector3 Normal=LogicalVector3.Cross(B-A,C-A);
        return LogicalVector3.Dot(LogicalVector3.Cross(B-A,Point-A),Normal)>=LogicalScalar.Zero
            &&LogicalVector3.Dot(LogicalVector3.Cross(C-B,Point-B),Normal)>=LogicalScalar.Zero
            &&LogicalVector3.Dot(LogicalVector3.Cross(A-C,Point-C),Normal)>=LogicalScalar.Zero;
    }

    /// <summary>
    /// <paramref name="Mesh"/> 内で球またはカプセルが貫入している三角形をすべて返す。三角形順に並べ、法線は問い合わせ形状からメッシュへ向ける。床と壁の両方に接する場合など、
    /// キャラクター移動では最深の接触だけではなくすべてが必要になる。
    /// </summary>
    internal int CollectMeshContacts(in PhysicsCollisionShape Query,in PhysicsCollisionShape Mesh,List<PhysicsContactCandidate> Results)=>
        this.CollectMeshContacts(Query,Mesh,Results,true);

    /// <param name="CullTriangles">
    /// 接触許容幅だけ拡大した問い合わせ境界と交差しない境界を持つ葉ノードの三角形を除外する。接触半径内の三角形には、掃引問い合わせ領域に含まれる点があるため、除外しても結果は変わらない。テストでは <see langword="false"/> と比較する。
    /// </param>
    internal int CollectMeshContacts(in PhysicsCollisionShape Query,in PhysicsCollisionShape Mesh,List<PhysicsContactCandidate> Results,bool CullTriangles){
        if(Query.Collider.Shape is not (ColliderShape.Sphere or ColliderShape.Capsule)
            ||Mesh.Asset is not { Kind:PhysicsShapeAssetKind.StaticTriangleMesh } Asset){ return 0; }
        PhysicsAabb WorldBounds=this.CalculateBounds(Query);
        if(!TryTransformBoundsToLocal(WorldBounds,Mesh.Position,Mesh.Rotation,out PhysicsAabb LocalBounds)){ return 0; }
        this.TriangleCandidates.Clear();
        Asset.QueryTriangles(LocalBounds,this.TriangleCandidates);
        LogicalVector3 Slack=new(TriangleCullSlack,TriangleCullSlack,TriangleCullSlack);
        PhysicsAabb CullBounds=new(LocalBounds.Min-Slack,LocalBounds.Max+Slack);
        int Start=Results.Count;
        foreach(int Triangle in this.TriangleCandidates){
            Asset.GetTriangle(Triangle,out LogicalVector3 A,out LogicalVector3 B,out LogicalVector3 C);
            if(CullTriangles&&!TriangleTouches(CullBounds,A,B,C)){ continue; }
            bool Hit=Query.Collider.Shape==ColliderShape.Sphere
                ?TrySphereTriangleContact(Query,Mesh,A,B,C,false,checked((uint)Triangle),out PhysicsContactCandidate Contact)
                :TryCapsuleTriangleContact(Query,Mesh,A,B,C,false,checked((uint)Triangle),out Contact);
            if(Hit){ Results.Add(Contact); }
        }
        return Results.Count-Start;
    }

    /// <summary>
    /// 光線が静的三角形メッシュに最初に当たる点(両面。三角形の向きは問わない)。光線はワールドの座標で、メッシュの置き場所
    /// (<see cref="PhysicsCollisionShape.Position"/>・<see cref="PhysicsCollisionShape.Rotation"/>)でメッシュのローカルへ移して判定する。
    /// <paramref name="Normal"/>は当たった三角形の面の法線で、光線に向いた側(ワールド)。メッシュの外接ボックスではなく三角形そのものに当たるので、
    /// 坂や穴のある形でも正確。
    /// </summary>
    internal bool TryRaycastMesh(in PhysicsRay Ray,in PhysicsCollisionShape Mesh,out LogicalScalar Distance,out LogicalVector3 Normal){
        Distance=default;
        Normal=default;
        if(Mesh.Asset is not { Kind:PhysicsShapeAssetKind.StaticTriangleMesh } Asset){ return false; }
        LogicalQuaternion Inverse=Mesh.Rotation.Conjugate();
        if(!Inverse.TryRotate(Ray.Origin-Mesh.Position,out LogicalVector3 Origin)||!Inverse.TryRotate(Ray.Direction,out LogicalVector3 Direction)){ return false; }
        LogicalVector3 End=Origin+Direction*Ray.MaxDistance;
        PhysicsAabb Bounds=new(
            new LogicalVector3(Min3(Origin.R,End.R,End.R),Min3(Origin.U,End.U,End.U),Min3(Origin.F,End.F,End.F)),
            new LogicalVector3(Max3(Origin.R,End.R,End.R),Max3(Origin.U,End.U,End.U),Max3(Origin.F,End.F,End.F)));
        this.TriangleCandidates.Clear();
        Asset.QueryTriangles(Bounds,this.TriangleCandidates);
        bool Found=false;
        LogicalScalar Best=Ray.MaxDistance;
        LogicalVector3 BestNormal=default;
        foreach(int Triangle in this.TriangleCandidates){
            Asset.GetTriangle(Triangle,out LogicalVector3 A,out LogicalVector3 B,out LogicalVector3 C);
            // Möller–Trumbore。行列式が0なら光線は三角形と平行。
            LogicalVector3 Edge1=B-A;
            LogicalVector3 Edge2=C-A;
            LogicalVector3 P=LogicalVector3.Cross(Direction,Edge2);
            LogicalScalar Determinant=LogicalVector3.Dot(Edge1,P);
            if(Determinant.IsZero){ continue; }
            LogicalVector3 ToOrigin=Origin-A;
            LogicalScalar U=LogicalVector3.Dot(ToOrigin,P)/Determinant;
            if(U<LogicalScalar.Zero||U>LogicalScalar.One){ continue; }
            LogicalVector3 Q=LogicalVector3.Cross(ToOrigin,Edge1);
            LogicalScalar V=LogicalVector3.Dot(Direction,Q)/Determinant;
            if(V<LogicalScalar.Zero||U+V>LogicalScalar.One){ continue; }
            LogicalScalar T=LogicalVector3.Dot(Edge2,Q)/Determinant;
            if(T<LogicalScalar.Zero||T>Best||(Found&&T==Best)){ continue; }
            LogicalVector3 Face=LogicalVector3.Cross(Edge1,Edge2);
            if(!Face.TryNormalize(out Face)){ continue; }
            Found=true;
            Best=T;
            BestNormal=LogicalVector3.Dot(Face,Direction)>LogicalScalar.Zero?-Face:Face;
        }
        if(!Found||!Mesh.Rotation.TryRotate(BestNormal,out Normal)){ return false; }
        Distance=Best;
        return true;
    }

    private static bool TriangleTouches(in PhysicsAabb Bounds,LogicalVector3 A,LogicalVector3 B,LogicalVector3 C)=>
        Max3(A.R,B.R,C.R)>=Bounds.Min.R&&Min3(A.R,B.R,C.R)<=Bounds.Max.R
        &&Max3(A.U,B.U,C.U)>=Bounds.Min.U&&Min3(A.U,B.U,C.U)<=Bounds.Max.U
        &&Max3(A.F,B.F,C.F)>=Bounds.Min.F&&Min3(A.F,B.F,C.F)<=Bounds.Max.F;

    private static LogicalScalar Max3(LogicalScalar A,LogicalScalar B,LogicalScalar C){
        LogicalScalar Result=A>B?A:B;
        return Result>C?Result:C;
    }

    private static LogicalScalar Min3(LogicalScalar A,LogicalScalar B,LogicalScalar C){
        LogicalScalar Result=A<B?A:B;
        return Result<C?Result:C;
    }

    /// <summary>カプセルと三角形の接触を閉形式で求める。カプセルの線分と三角形の最近接点を使う。線分が三角形を貫通する場合は、カプセル中心側へ向く面法線に沿って押し出し、線分の両端がともに面の外へ出る深さを確保する。</summary>
    private static bool TryCapsuleTriangleContact(
        in PhysicsCollisionShape Capsule,in PhysicsCollisionShape Mesh,
        LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,bool MeshIsFirst,uint Feature,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(!TryColliderCenter(Capsule,out LogicalVector3 Center)
            ||!Capsule.Rotation.TryRotate(new LogicalVector3(LogicalScalar.Zero,Capsule.Collider.HalfHeight,LogicalScalar.Zero),out LogicalVector3 Axis)
            ||!Mesh.Rotation.TryRotate(A,out A)||!Mesh.Rotation.TryRotate(B,out B)||!Mesh.Rotation.TryRotate(C,out C)){ return false; }
        A+=Mesh.Position;B+=Mesh.Position;C+=Mesh.Position;
        LogicalVector3 Bottom=Center-Axis;
        LogicalVector3 Top=Center+Axis;
        LogicalScalar Radius=Capsule.Collider.Radius+TriangleHalfThickness;
        bool Crossing=ClosestSegmentTriangle(Bottom,Top,A,B,C,out LogicalVector3 OnSegment,out LogicalVector3 OnTriangle);
        LogicalVector3 Delta=OnTriangle-OnSegment;
        LogicalScalar DistanceSquared=Delta.LengthSquared;
        if(!Crossing&&DistanceSquared>=Radius*Radius){ return false; }
        LogicalVector3 Normal;
        LogicalScalar Penetration;
        if(Crossing||DistanceSquared.IsZero){
            if(!LogicalVector3.Cross(B-A,C-A).TryNormalize(out LogicalVector3 Face)){ return false; }
            LogicalVector3 Outward=LogicalVector3.Dot(Face,Center-A)>=LogicalScalar.Zero?Face:-Face;
            LogicalScalar BottomHeight=LogicalVector3.Dot(Outward,Bottom-A);
            LogicalScalar TopHeight=LogicalVector3.Dot(Outward,Top-A);
            Normal=-Outward;
            Penetration=Radius-(BottomHeight<TopHeight?BottomHeight:TopHeight);
        }
        else {
            if(!LogicalScalar.TrySquareRoot(DistanceSquared,out LogicalScalar Distance)||!Delta.TryNormalize(out Normal)){ return false; }
            Penetration=Radius-Distance;
        }
        if(MeshIsFirst){ Normal=-Normal; }
        Contact=new PhysicsContactCandidate(OnTriangle,Normal,Penetration,Feature) { SurfaceNormal=FaceToward(A,B,C,Center) };
        return true;
    }

    /// <summary>線分 PQ と三角形 ABC の最近接点を求める。線分が三角形を横切る場合は true を返す。</summary>
    internal static bool ClosestSegmentTriangle(
        LogicalVector3 P,LogicalVector3 Q,LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,
        out LogicalVector3 OnSegment,out LogicalVector3 OnTriangle){
        LogicalVector3 Face=LogicalVector3.Cross(B-A,C-A);
        LogicalScalar PHeight=LogicalVector3.Dot(Face,P-A);
        LogicalScalar QHeight=LogicalVector3.Dot(Face,Q-A);
        bool OppositeSides=PHeight<=LogicalScalar.Zero&&QHeight>=LogicalScalar.Zero||PHeight>=LogicalScalar.Zero&&QHeight<=LogicalScalar.Zero;
        if(OppositeSides&&PHeight!=QHeight){
            LogicalVector3 Crossing=P+(Q-P)*(PHeight/(PHeight-QHeight));
            if(LogicalVector3.Dot(Face,LogicalVector3.Cross(B-A,Crossing-A))>=LogicalScalar.Zero
                &&LogicalVector3.Dot(Face,LogicalVector3.Cross(C-B,Crossing-B))>=LogicalScalar.Zero
                &&LogicalVector3.Dot(Face,LogicalVector3.Cross(A-C,Crossing-C))>=LogicalScalar.Zero){
                OnSegment=Crossing;
                OnTriangle=Crossing;
                return true;
            }
        }
        OnSegment=P;
        OnTriangle=ClosestPointOnTriangle(P,A,B,C);
        LogicalScalar Best=(OnTriangle-OnSegment).LengthSquared;
        Consider(Q,ClosestPointOnTriangle(Q,A,B,C),ref Best,ref OnSegment,ref OnTriangle);
        ClosestSegmentSegment(P,Q,A,B,out LogicalVector3 SegmentPoint,out LogicalVector3 EdgePoint);
        Consider(SegmentPoint,EdgePoint,ref Best,ref OnSegment,ref OnTriangle);
        ClosestSegmentSegment(P,Q,B,C,out SegmentPoint,out EdgePoint);
        Consider(SegmentPoint,EdgePoint,ref Best,ref OnSegment,ref OnTriangle);
        ClosestSegmentSegment(P,Q,C,A,out SegmentPoint,out EdgePoint);
        Consider(SegmentPoint,EdgePoint,ref Best,ref OnSegment,ref OnTriangle);
        return false;
        static void Consider(LogicalVector3 Segment,LogicalVector3 Triangle,ref LogicalScalar Best,ref LogicalVector3 OnSegment,ref LogicalVector3 OnTriangle){
            LogicalScalar Candidate=(Triangle-Segment).LengthSquared;
            if(Candidate<Best){
                Best=Candidate;
                OnSegment=Segment;
                OnTriangle=Triangle;
            }
        }
    }

    /// <summary>線分 P1Q1 と P2Q2 の最近接点を求める（Ericson, Real-Time Collision Detection 5.1.9）。</summary>
    internal static void ClosestSegmentSegment(
        LogicalVector3 P1,LogicalVector3 Q1,LogicalVector3 P2,LogicalVector3 Q2,
        out LogicalVector3 First,out LogicalVector3 Second){
        LogicalVector3 D1=Q1-P1;
        LogicalVector3 D2=Q2-P2;
        LogicalVector3 R=P1-P2;
        LogicalScalar A=LogicalVector3.Dot(D1,D1);
        LogicalScalar E=LogicalVector3.Dot(D2,D2);
        LogicalScalar F=LogicalVector3.Dot(D2,R);
        LogicalScalar S;
        LogicalScalar T;
        if(A.IsZero&&E.IsZero){
            S=LogicalScalar.Zero;
            T=LogicalScalar.Zero;
        }
        else if(A.IsZero){
            S=LogicalScalar.Zero;
            T=Clamp01(F/E);
        }
        else {
            LogicalScalar Cd=LogicalVector3.Dot(D1,R);
            if(E.IsZero){
                T=LogicalScalar.Zero;
                S=Clamp01(-Cd/A);
            }
            else {
                LogicalScalar Bd=LogicalVector3.Dot(D1,D2);
                LogicalScalar Denominator=A*E-Bd*Bd;
                S=Denominator.IsZero?LogicalScalar.Zero:Clamp01((Bd*F-Cd*E)/Denominator);
                T=(Bd*S+F)/E;
                if(T<LogicalScalar.Zero){
                    T=LogicalScalar.Zero;
                    S=Clamp01(-Cd/A);
                }
                else if(T>LogicalScalar.One){
                    T=LogicalScalar.One;
                    S=Clamp01((Bd-Cd)/A);
                }
            }
        }
        First=P1+D1*S;
        Second=P2+D2*T;
        static LogicalScalar Clamp01(LogicalScalar Value)=>Value<LogicalScalar.Zero?LogicalScalar.Zero:Value>LogicalScalar.One?LogicalScalar.One:Value;
    }

    private static bool TryPrimitiveContact(
        in PhysicsCollisionShape First,in PhysicsCollisionShape Second,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(First.Collider.Shape==ColliderShape.Box&&Second.Collider.Shape==ColliderShape.Box){
            return TryBoxBoxContact(First,Second,out Contact);
        }
        if(First.Collider.Shape==ColliderShape.Sphere&&Second.Collider.Shape==ColliderShape.Sphere){
            return TrySphereSphereContact(First,Second,out Contact);
        }
        if(First.Collider.Shape==ColliderShape.Sphere&&Second.Collider.Shape==ColliderShape.Box){
            return TrySphereBoxContact(First,Second,false,out Contact);
        }
        if(First.Collider.Shape==ColliderShape.Box&&Second.Collider.Shape==ColliderShape.Sphere){
            return TrySphereBoxContact(Second,First,true,out Contact);
        }
        return false;
    }

    private static bool TryBoxBoxContact(
        in PhysicsCollisionShape First,in PhysicsCollisionShape Second,
        out PhysicsContactCandidate Contact){
        Contact=default;
        Span<LogicalVector3> FirstAxes=stackalloc LogicalVector3[3];
        Span<LogicalVector3> SecondAxes=stackalloc LogicalVector3[3];
        if(!TryAxes(First.Rotation,FirstAxes)||!TryAxes(Second.Rotation,SecondAxes)
            ||!TryColliderCenter(First,out LogicalVector3 FirstCenter)
            ||!TryColliderCenter(Second,out LogicalVector3 SecondCenter)){ return false; }
        LogicalVector3 Delta=SecondCenter-FirstCenter;
        LogicalScalar Minimum=LogicalScalar.MaxValue;
        LogicalVector3 Best=default;
        uint Feature=0;
        uint CandidateFeature=0;
        for(int Index=0;Index<3;Index++){
            if(!TestObbAxis(First,Second,FirstAxes,SecondAxes,Delta,FirstAxes[Index],CandidateFeature++,ref Minimum,ref Best,ref Feature)){ return false; }
        }
        for(int Index=0;Index<3;Index++){
            if(!TestObbAxis(First,Second,FirstAxes,SecondAxes,Delta,SecondAxes[Index],CandidateFeature++,ref Minimum,ref Best,ref Feature)){ return false; }
        }
        for(int FirstIndex=0;FirstIndex<3;FirstIndex++){
            for(int SecondIndex=0;SecondIndex<3;SecondIndex++){
                LogicalVector3 Axis=LogicalVector3.Cross(FirstAxes[FirstIndex],SecondAxes[SecondIndex]);
                if(Axis.LengthSquared<=EpaTolerance*EpaTolerance){ CandidateFeature++;continue; }
                if(!TestObbAxis(First,Second,FirstAxes,SecondAxes,Delta,Axis,CandidateFeature++,ref Minimum,ref Best,ref Feature)){ return false; }
            }
        }
        if(Best.LengthSquared.IsZero){ return false; }
        if(LogicalVector3.Dot(Best,Delta)<LogicalScalar.Zero){ Best=-Best; }
        return TryBuildBoxPatch(
            First,Second,FirstAxes,SecondAxes,FirstCenter,SecondCenter,Best,Minimum,Feature,out Contact);
    }

    private static bool TryBuildBoxPatch(
        in PhysicsCollisionShape First,in PhysicsCollisionShape Second,
        Span<LogicalVector3> FirstAxes,Span<LogicalVector3> SecondAxes,
        LogicalVector3 FirstCenter,LogicalVector3 SecondCenter,
        LogicalVector3 Normal,LogicalScalar Minimum,uint Feature,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(Feature>=6){
            ShapeProxy FirstProxy=new(First);
            ShapeProxy SecondProxy=new(Second);
            LogicalVector3 Position=(FirstProxy.Support(Normal)+SecondProxy.Support(-Normal))/LogicalScalar.FromInt64(2);
            Contact=new PhysicsContactCandidate(Position,Normal,Minimum,Feature);
            return true;
        }
        bool ReferenceIsFirst=Feature<3;
        int ReferenceAxis=checked((int)(Feature%3));
        Span<LogicalVector3> ReferenceAxes=ReferenceIsFirst?FirstAxes:SecondAxes;
        Span<LogicalVector3> IncidentAxes=ReferenceIsFirst?SecondAxes:FirstAxes;
        LogicalVector3 ReferenceCenter=ReferenceIsFirst?FirstCenter:SecondCenter;
        LogicalVector3 IncidentCenter=ReferenceIsFirst?SecondCenter:FirstCenter;
        LogicalVector3 ReferenceExtents=ReferenceIsFirst?First.Collider.HalfExtents:Second.Collider.HalfExtents;
        LogicalVector3 IncidentExtents=ReferenceIsFirst?Second.Collider.HalfExtents:First.Collider.HalfExtents;
        LogicalVector3 ReferenceNormal=ReferenceIsFirst?Normal:-Normal;
        LogicalScalar ReferenceExtent=GetAxis(ReferenceExtents,ReferenceAxis);
        LogicalScalar ReferenceSign=LogicalVector3.Dot(ReferenceAxes[ReferenceAxis],ReferenceNormal)>=LogicalScalar.Zero
            ?LogicalScalar.One:-LogicalScalar.One;
        LogicalVector3 ReferenceFaceCenter=ReferenceCenter+ReferenceAxes[ReferenceAxis]*(ReferenceExtent*ReferenceSign);

        int IncidentAxis=0;
        LogicalScalar Alignment=Abs(LogicalVector3.Dot(IncidentAxes[0],ReferenceNormal));
        for(int Axis=1;Axis<3;Axis++){
            LogicalScalar Candidate=Abs(LogicalVector3.Dot(IncidentAxes[Axis],ReferenceNormal));
            if(Candidate>Alignment){ Alignment=Candidate;IncidentAxis=Axis; }
        }
        LogicalScalar IncidentSign=LogicalVector3.Dot(IncidentAxes[IncidentAxis],ReferenceNormal)>LogicalScalar.Zero
            ?-LogicalScalar.One:LogicalScalar.One;
        LogicalVector3 IncidentFaceCenter=IncidentCenter
            +IncidentAxes[IncidentAxis]*(GetAxis(IncidentExtents,IncidentAxis)*IncidentSign);
        int IncidentSide0=(IncidentAxis+1)%3;
        int IncidentSide1=(IncidentAxis+2)%3;
        LogicalVector3 IncidentOffset0=IncidentAxes[IncidentSide0]*GetAxis(IncidentExtents,IncidentSide0);
        LogicalVector3 IncidentOffset1=IncidentAxes[IncidentSide1]*GetAxis(IncidentExtents,IncidentSide1);
        Span<LogicalVector3> PolygonA=stackalloc LogicalVector3[8];
        Span<LogicalVector3> PolygonB=stackalloc LogicalVector3[8];
        PolygonA[0]=IncidentFaceCenter-IncidentOffset0-IncidentOffset1;
        PolygonA[1]=IncidentFaceCenter+IncidentOffset0-IncidentOffset1;
        PolygonA[2]=IncidentFaceCenter+IncidentOffset0+IncidentOffset1;
        PolygonA[3]=IncidentFaceCenter-IncidentOffset0+IncidentOffset1;
        int Count=4;
        int ReferenceSide0=(ReferenceAxis+1)%3;
        int ReferenceSide1=(ReferenceAxis+2)%3;
        Count=ClipPolygon(PolygonA,Count,PolygonB,ReferenceCenter,ReferenceAxes[ReferenceSide0],GetAxis(ReferenceExtents,ReferenceSide0));
        Count=ClipPolygon(PolygonB,Count,PolygonA,ReferenceCenter,-ReferenceAxes[ReferenceSide0],GetAxis(ReferenceExtents,ReferenceSide0));
        Count=ClipPolygon(PolygonA,Count,PolygonB,ReferenceCenter,ReferenceAxes[ReferenceSide1],GetAxis(ReferenceExtents,ReferenceSide1));
        Count=ClipPolygon(PolygonB,Count,PolygonA,ReferenceCenter,-ReferenceAxes[ReferenceSide1],GetAxis(ReferenceExtents,ReferenceSide1));
        Span<LogicalVector3> Points=stackalloc LogicalVector3[4];
        Span<LogicalScalar> Penetrations=stackalloc LogicalScalar[4];
        int PointCount=0;
        for(int Index=0;Index<Count&&PointCount<4;Index++){
            LogicalScalar Separation=LogicalVector3.Dot(PolygonA[Index]-ReferenceFaceCenter,ReferenceNormal);
            if(Separation>EpaTolerance){ continue; }
            LogicalVector3 Point=PolygonA[Index]-ReferenceNormal*(Separation/LogicalScalar.FromInt64(2));
            bool Duplicate=false;
            for(int Existing=0;Existing<PointCount;Existing++){
                if((Points[Existing]-Point).LengthSquared<=EpaTolerance*EpaTolerance){ Duplicate=true;break; }
            }
            if(Duplicate){ continue; }
            Points[PointCount]=Point;
            Penetrations[PointCount]=Max(EpaTolerance,-Separation);
            PointCount++;
        }
        if(PointCount==0){
            Contact=new PhysicsContactCandidate(
                (FirstCenter+SecondCenter)/LogicalScalar.FromInt64(2),Normal,Minimum,Feature);
            return true;
        }
        Contact=PhysicsContactCandidate.CreatePatch(Normal,Feature,Points,Penetrations,PointCount);
        return true;
    }

    private static int ClipPolygon(
        Span<LogicalVector3> Input,int Count,Span<LogicalVector3> Output,
        LogicalVector3 PlaneCenter,LogicalVector3 PlaneNormal,LogicalScalar Limit){
        if(Count==0){ return 0; }
        int OutputCount=0;
        LogicalVector3 Previous=Input[Count-1];
        LogicalScalar PreviousDistance=LogicalVector3.Dot(Previous-PlaneCenter,PlaneNormal)-Limit;
        bool PreviousInside=PreviousDistance<=LogicalScalar.Zero;
        for(int Index=0;Index<Count;Index++){
            LogicalVector3 Current=Input[Index];
            LogicalScalar CurrentDistance=LogicalVector3.Dot(Current-PlaneCenter,PlaneNormal)-Limit;
            bool CurrentInside=CurrentDistance<=LogicalScalar.Zero;
            if(CurrentInside!=PreviousInside){
                LogicalScalar Denominator=PreviousDistance-CurrentDistance;
                if(!Denominator.IsZero){
                    LogicalScalar T=PreviousDistance/Denominator;
                    Output[OutputCount++]=Previous+(Current-Previous)*T;
                }
            }
            if(CurrentInside){ Output[OutputCount++]=Current; }
            Previous=Current;
            PreviousDistance=CurrentDistance;
            PreviousInside=CurrentInside;
        }
        return OutputCount;
    }

    private static LogicalScalar GetAxis(LogicalVector3 Value,int Axis)=>Axis switch {
        0=>Value.R,1=>Value.U,2=>Value.F,_=>throw new ArgumentOutOfRangeException(nameof(Axis)),
    };

    private static bool TestObbAxis(
        in PhysicsCollisionShape First,in PhysicsCollisionShape Second,
        Span<LogicalVector3> FirstAxes,Span<LogicalVector3> SecondAxes,LogicalVector3 Delta,
        LogicalVector3 Axis,uint CandidateFeature,
        ref LogicalScalar Minimum,ref LogicalVector3 Best,ref uint Feature){
        if(!Axis.TryNormalize(out LogicalVector3 Unit)){ return true; }
        LogicalScalar FirstRadius=ProjectionRadius(First.Collider.HalfExtents,FirstAxes,Unit);
        LogicalScalar SecondRadius=ProjectionRadius(Second.Collider.HalfExtents,SecondAxes,Unit);
        LogicalScalar Overlap=FirstRadius+SecondRadius-Abs(LogicalVector3.Dot(Delta,Unit));
        if(Overlap<=LogicalScalar.Zero){ return false; }
        if(Overlap<Minimum){
            Minimum=Overlap;
            Best=Unit;
            Feature=CandidateFeature;
        }
        return true;
    }

    private static LogicalScalar ProjectionRadius(
        LogicalVector3 HalfExtents,Span<LogicalVector3> Axes,LogicalVector3 Axis)=>
        HalfExtents.R*Abs(LogicalVector3.Dot(Axes[0],Axis))
        +HalfExtents.U*Abs(LogicalVector3.Dot(Axes[1],Axis))
        +HalfExtents.F*Abs(LogicalVector3.Dot(Axes[2],Axis));

    private static bool TrySphereSphereContact(
        in PhysicsCollisionShape First,in PhysicsCollisionShape Second,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(!TryColliderCenter(First,out LogicalVector3 FirstCenter)||!TryColliderCenter(Second,out LogicalVector3 SecondCenter)){ return false; }
        LogicalVector3 Delta=SecondCenter-FirstCenter;
        LogicalScalar Radius=First.Collider.Radius+Second.Collider.Radius;
        LogicalScalar DistanceSquared=Delta.LengthSquared;
        if(DistanceSquared>=Radius*Radius){ return false; }
        LogicalVector3 Normal;
        LogicalScalar Distance;
        if(DistanceSquared.IsZero){ Normal=LogicalVector3.UnitU;Distance=LogicalScalar.Zero; }
        else if(!LogicalScalar.TrySquareRoot(DistanceSquared,out Distance)||!Delta.TryNormalize(out Normal)){ return false; }
        Contact=new PhysicsContactCandidate(
            FirstCenter+Normal*(First.Collider.Radius-(Radius-Distance)/LogicalScalar.FromInt64(2)),
            Normal,Radius-Distance,0);
        return true;
    }

    private static bool TrySphereBoxContact(
        in PhysicsCollisionShape Sphere,in PhysicsCollisionShape Box,bool Reverse,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(!TryColliderCenter(Sphere,out LogicalVector3 SphereCenter)||!TryColliderCenter(Box,out LogicalVector3 BoxCenter)){ return false; }
        LogicalQuaternion Inverse=Box.Rotation.Conjugate();
        if(!Inverse.TryRotate(SphereCenter-BoxCenter,out LogicalVector3 Local)){ return false; }
        LogicalVector3 Closest=new(
            Clamp(Local.R,-Box.Collider.HalfExtents.R,Box.Collider.HalfExtents.R),
            Clamp(Local.U,-Box.Collider.HalfExtents.U,Box.Collider.HalfExtents.U),
            Clamp(Local.F,-Box.Collider.HalfExtents.F,Box.Collider.HalfExtents.F));
        LogicalVector3 DeltaLocal=Closest-Local;
        LogicalScalar DistanceSquared=DeltaLocal.LengthSquared;
        if(DistanceSquared>=Sphere.Collider.Radius*Sphere.Collider.Radius){ return false; }
        LogicalVector3 LocalNormal;
        LogicalScalar Distance;
        if(DistanceSquared.IsZero){
            LogicalVector3 FaceDistance=new(
                Box.Collider.HalfExtents.R-Abs(Local.R),
                Box.Collider.HalfExtents.U-Abs(Local.U),
                Box.Collider.HalfExtents.F-Abs(Local.F));
            LocalNormal=FaceDistance.R<=FaceDistance.U&&FaceDistance.R<=FaceDistance.F
                ?new LogicalVector3(Local.R>=LogicalScalar.Zero?LogicalScalar.One:-LogicalScalar.One,0,0)
                :FaceDistance.U<=FaceDistance.F
                    ?new LogicalVector3(0,Local.U>=LogicalScalar.Zero?LogicalScalar.One:-LogicalScalar.One,0)
                    :new LogicalVector3(0,0,Local.F>=LogicalScalar.Zero?LogicalScalar.One:-LogicalScalar.One);
            Distance=-Min(FaceDistance.R,Min(FaceDistance.U,FaceDistance.F));
            Closest=Local-LocalNormal*Distance;
        }
        else {
            if(!LogicalScalar.TrySquareRoot(DistanceSquared,out Distance)||!DeltaLocal.TryNormalize(out LocalNormal)){ return false; }
        }
        if(!Box.Rotation.TryRotate(LocalNormal,out LogicalVector3 Normal)
            ||!Box.Rotation.TryRotate(Closest,out LogicalVector3 RotatedClosest)){ return false; }
        LogicalScalar Penetration=Sphere.Collider.Radius-Distance;
        if(Reverse){ Normal=-Normal; }
        Contact=new PhysicsContactCandidate(BoxCenter+RotatedClosest,Normal,Penetration,0);
        return true;
    }

    private static bool TrySphereTriangleContact(
        in PhysicsCollisionShape Sphere,in PhysicsCollisionShape Mesh,
        LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,bool MeshIsFirst,uint Feature,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(!TryColliderCenter(Sphere,out LogicalVector3 Center)
            ||!Mesh.Rotation.TryRotate(A,out A)||!Mesh.Rotation.TryRotate(B,out B)||!Mesh.Rotation.TryRotate(C,out C)){ return false; }
        A+=Mesh.Position;B+=Mesh.Position;C+=Mesh.Position;
        LogicalVector3 Closest=ClosestPointOnTriangle(Center,A,B,C);
        LogicalVector3 Delta=Closest-Center;
        LogicalScalar DistanceSquared=Delta.LengthSquared;
        LogicalScalar Radius=Sphere.Collider.Radius+TriangleHalfThickness;
        if(DistanceSquared>=Radius*Radius){ return false; }
        LogicalVector3 Normal;
        LogicalScalar Distance;
        if(DistanceSquared.IsZero){
            LogicalVector3 Face=LogicalVector3.Cross(B-A,C-A);
            if(!Face.TryNormalize(out Normal)){ return false; }
            if(LogicalVector3.Dot(Normal,Center-A)>LogicalScalar.Zero){ Normal=-Normal; }
            Distance=LogicalScalar.Zero;
        }
        else if(!LogicalScalar.TrySquareRoot(DistanceSquared,out Distance)||!Delta.TryNormalize(out Normal)){ return false; }
        if(MeshIsFirst){ Normal=-Normal; }
        Contact=new PhysicsContactCandidate(Closest,Normal,Radius-Distance,Feature) { SurfaceNormal=FaceToward(A,B,C,Center) };
        return true;
    }

    /// <summary>点 <paramref name="Point"/> 側を向く三角形 ABC の単位面法線。三角形が退化している場合はゼロ。</summary>
    private static LogicalVector3 FaceToward(LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,LogicalVector3 Point){
        if(!LogicalVector3.Cross(B-A,C-A).TryNormalize(out LogicalVector3 Face)){ return LogicalVector3.Zero; }
        return LogicalVector3.Dot(Face,Point-A)>=LogicalScalar.Zero?Face:-Face;
    }

    private static LogicalVector3 ClosestPointOnTriangle(
        LogicalVector3 Point,LogicalVector3 A,LogicalVector3 B,LogicalVector3 C){
        LogicalVector3 AB=B-A;
        LogicalVector3 AC=C-A;
        LogicalVector3 AP=Point-A;
        LogicalScalar D1=LogicalVector3.Dot(AB,AP);
        LogicalScalar D2=LogicalVector3.Dot(AC,AP);
        if(D1<=LogicalScalar.Zero&&D2<=LogicalScalar.Zero){ return A; }
        LogicalVector3 BP=Point-B;
        LogicalScalar D3=LogicalVector3.Dot(AB,BP);
        LogicalScalar D4=LogicalVector3.Dot(AC,BP);
        if(D3>=LogicalScalar.Zero&&D4<=D3){ return B; }
        LogicalScalar VC=D1*D4-D3*D2;
        if(VC<=LogicalScalar.Zero&&D1>=LogicalScalar.Zero&&D3<=LogicalScalar.Zero){
            LogicalScalar V=D1/(D1-D3);
            return A+AB*V;
        }
        LogicalVector3 CP=Point-C;
        LogicalScalar D5=LogicalVector3.Dot(AB,CP);
        LogicalScalar D6=LogicalVector3.Dot(AC,CP);
        if(D6>=LogicalScalar.Zero&&D5<=D6){ return C; }
        LogicalScalar VB=D5*D2-D1*D6;
        if(VB<=LogicalScalar.Zero&&D2>=LogicalScalar.Zero&&D6<=LogicalScalar.Zero){
            LogicalScalar W=D2/(D2-D6);
            return A+AC*W;
        }
        LogicalScalar VA=D3*D6-D5*D4;
        if(VA<=LogicalScalar.Zero&&D4-D3>=LogicalScalar.Zero&&D5-D6>=LogicalScalar.Zero){
            LogicalScalar W=(D4-D3)/((D4-D3)+(D5-D6));
            return B+(C-B)*W;
        }
        LogicalScalar Denominator=LogicalScalar.One/(VA+VB+VC);
        LogicalScalar FaceV=VB*Denominator;
        LogicalScalar FaceW=VC*Denominator;
        return A+AB*FaceV+AC*FaceW;
    }

    private static bool TryAxes(LogicalQuaternion Rotation,Span<LogicalVector3> Axes)=>
        Rotation.TryRotate(LogicalVector3.UnitR,out Axes[0])
        &&Rotation.TryRotate(LogicalVector3.UnitU,out Axes[1])
        &&Rotation.TryRotate(LogicalVector3.UnitF,out Axes[2]);

    private static bool TryColliderCenter(in PhysicsCollisionShape Shape,out LogicalVector3 Center){
        Center=Shape.Position;
        if(!Shape.Rotation.TryRotate(Shape.Collider.Center,out LogicalVector3 Rotated)){ return false; }
        Center+=Rotated;
        return true;
    }

    private static bool TryConvexContact(ShapeProxy First,ShapeProxy Second,uint FeatureId,out PhysicsContactCandidate Contact){
        Contact=default;
        Span<SupportPoint> Simplex=stackalloc SupportPoint[4];
        int Count=0;
        LogicalVector3 Direction=Second.Center-First.Center;
        if(Direction.LengthSquared.IsZero){ Direction=LogicalVector3.UnitR; }
        SupportPoint Initial=Support(First,Second,Direction);
        Simplex[Count++]=Initial;
        Direction=-Initial.Difference;
        for(int Iteration=0;Iteration<GjkIterations;Iteration++){
            if(Direction.LengthSquared.IsZero){
                Direction=ChoosePerpendicular(Simplex[Count-1].Difference);
            }
            SupportPoint Next=Support(First,Second,Direction);
            if(LogicalVector3.Dot(Next.Difference,Direction)<LogicalScalar.Zero){ return false; }
            Simplex[Count++]=Next;
            if(UpdateSimplex(Simplex,ref Count,ref Direction)){
                return TryEpa(First,Second,Simplex,Count,FeatureId,out Contact)
                    ||TryFallbackPenetration(First,Second,FeatureId,out Contact);
            }
        }
        return false;
    }

    private static bool TryFallbackPenetration(
        ShapeProxy First,ShapeProxy Second,uint FeatureId,out PhysicsContactCandidate Contact){
        Contact=default;
        LogicalVector3 Normal=Second.Center-First.Center;
        if(!Normal.TryNormalize(out Normal)){ Normal=LogicalVector3.UnitU; }
        LogicalVector3 FirstPoint=First.Support(Normal);
        LogicalVector3 SecondPoint=Second.Support(-Normal);
        LogicalScalar Penetration=LogicalVector3.Dot(FirstPoint-SecondPoint,Normal);
        if(Penetration<=LogicalScalar.Zero){ return false; }
        Contact=new PhysicsContactCandidate(
            (FirstPoint+SecondPoint)/LogicalScalar.FromInt64(2),Normal,Penetration,FeatureId);
        return true;
    }

    private static bool UpdateSimplex(Span<SupportPoint> Simplex,ref int Count,ref LogicalVector3 Direction){
        return Count switch {
            2=>UpdateLine(Simplex,ref Count,ref Direction),
            3=>UpdateTriangle(Simplex,ref Count,ref Direction),
            4=>UpdateTetrahedron(Simplex,ref Count,ref Direction),
            _=>false,
        };
    }

    private static bool UpdateLine(Span<SupportPoint> Simplex,ref int Count,ref LogicalVector3 Direction){
        SupportPoint A=Simplex[Count-1];
        SupportPoint B=Simplex[Count-2];
        LogicalVector3 AO=-A.Difference;
        LogicalVector3 AB=B.Difference-A.Difference;
        if(LogicalVector3.Dot(AB,AO)>LogicalScalar.Zero){
            Direction=LogicalVector3.Cross(LogicalVector3.Cross(AB,AO),AB);
            if(Direction.LengthSquared.IsZero){ Direction=ChoosePerpendicular(AB); }
            Simplex[0]=B;
            Simplex[1]=A;
            Count=2;
        }
        else {
            Simplex[0]=A;
            Count=1;
            Direction=AO;
        }
        return false;
    }

    private static bool UpdateTriangle(Span<SupportPoint> Simplex,ref int Count,ref LogicalVector3 Direction){
        SupportPoint A=Simplex[Count-1];
        SupportPoint B=Simplex[Count-2];
        SupportPoint C=Simplex[Count-3];
        LogicalVector3 AO=-A.Difference;
        LogicalVector3 AB=B.Difference-A.Difference;
        LogicalVector3 AC=C.Difference-A.Difference;
        LogicalVector3 ABC=LogicalVector3.Cross(AB,AC);
        if(LogicalVector3.Dot(LogicalVector3.Cross(ABC,AC),AO)>LogicalScalar.Zero){
            if(LogicalVector3.Dot(AC,AO)>LogicalScalar.Zero){
                Simplex[0]=C;
                Simplex[1]=A;
                Count=2;
                Direction=LogicalVector3.Cross(LogicalVector3.Cross(AC,AO),AC);
            }
            else {
                Simplex[0]=B;
                Simplex[1]=A;
                Count=2;
                return UpdateLine(Simplex,ref Count,ref Direction);
            }
            return false;
        }
        if(LogicalVector3.Dot(LogicalVector3.Cross(AB,ABC),AO)>LogicalScalar.Zero){
            Simplex[0]=B;
            Simplex[1]=A;
            Count=2;
            return UpdateLine(Simplex,ref Count,ref Direction);
        }
        if(LogicalVector3.Dot(ABC,AO)>LogicalScalar.Zero){
            Simplex[0]=C;
            Simplex[1]=B;
            Simplex[2]=A;
            Direction=ABC;
        }
        else {
            Simplex[0]=B;
            Simplex[1]=C;
            Simplex[2]=A;
            Direction=-ABC;
        }
        Count=3;
        return false;
    }

    private static bool UpdateTetrahedron(Span<SupportPoint> Simplex,ref int Count,ref LogicalVector3 Direction){
        SupportPoint A=Simplex[3];
        SupportPoint B=Simplex[2];
        SupportPoint C=Simplex[1];
        SupportPoint D=Simplex[0];
        LogicalVector3 AO=-A.Difference;
        if(IsOutsideFace(A,B,C,D,AO,out LogicalVector3 Normal)){
            Simplex[0]=C;Simplex[1]=B;Simplex[2]=A;Count=3;Direction=Normal;return false;
        }
        if(IsOutsideFace(A,C,D,B,AO,out Normal)){
            Simplex[0]=D;Simplex[1]=C;Simplex[2]=A;Count=3;Direction=Normal;return false;
        }
        if(IsOutsideFace(A,D,B,C,AO,out Normal)){
            Simplex[0]=B;Simplex[1]=D;Simplex[2]=A;Count=3;Direction=Normal;return false;
        }
        return true;
    }

    private static bool IsOutsideFace(
        SupportPoint A,SupportPoint B,SupportPoint C,SupportPoint Opposite,
        LogicalVector3 AO,out LogicalVector3 Normal){
        Normal=LogicalVector3.Cross(B.Difference-A.Difference,C.Difference-A.Difference);
        if(LogicalVector3.Dot(Normal,Opposite.Difference-A.Difference)>LogicalScalar.Zero){ Normal=-Normal; }
        return LogicalVector3.Dot(Normal,AO)>LogicalScalar.Zero;
    }

    private static bool TryEpa(
        ShapeProxy First,ShapeProxy Second,Span<SupportPoint> Simplex,int SimplexCount,uint FeatureId,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(SimplexCount<4){ return false; }
        Span<SupportPoint> Vertices=stackalloc SupportPoint[MaximumEpaVertices];
        for(int Index=0;Index<4;Index++){ Vertices[Index]=Simplex[Index]; }
        int VertexCount=4;
        Span<EpaFace> Faces=stackalloc EpaFace[MaximumEpaFaces];
        int FaceCount=0;
        if(!TryAppendFace(Vertices,Faces,ref FaceCount,0,1,2)
            ||!TryAppendFace(Vertices,Faces,ref FaceCount,0,3,1)
            ||!TryAppendFace(Vertices,Faces,ref FaceCount,0,2,3)
            ||!TryAppendFace(Vertices,Faces,ref FaceCount,1,3,2)){ return false; }
        Span<EpaEdge> Edges=stackalloc EpaEdge[MaximumEpaFaces*3];
        for(int Iteration=0;Iteration<EpaIterations;Iteration++){
            int Closest=-1;
            for(int Index=0;Index<FaceCount;Index++){
                if(!Faces[Index].Active){ continue; }
                if(Closest<0||Faces[Index].Distance<Faces[Closest].Distance){ Closest=Index; }
            }
            if(Closest<0){ return false; }
            EpaFace Face=Faces[Closest];
            SupportPoint Point=Support(First,Second,Face.Normal);
            LogicalScalar Projection=LogicalVector3.Dot(Point.Difference,Face.Normal);
            if(Projection-Face.Distance<=EpaTolerance||VertexCount==MaximumEpaVertices){
                LogicalVector3 Normal=Face.Normal;
                LogicalVector3 CenterDelta=Second.Center-First.Center;
                if(LogicalVector3.Dot(Normal,CenterDelta)<LogicalScalar.Zero){ Normal=-Normal; }
                LogicalVector3 Position=(Vertices[Face.A].First+Vertices[Face.A].Second
                    +Vertices[Face.B].First+Vertices[Face.B].Second
                    +Vertices[Face.C].First+Vertices[Face.C].Second)/LogicalScalar.FromInt64(6);
                Contact=new PhysicsContactCandidate(Position,Normal,Max(Face.Distance,EpaTolerance),FeatureId);
                return true;
            }
            int NewVertex=VertexCount++;
            Vertices[NewVertex]=Point;
            int EdgeCount=0;
            for(int Index=0;Index<FaceCount;Index++){
                EpaFace Candidate=Faces[Index];
                if(!Candidate.Active){ continue; }
                if(LogicalVector3.Dot(Candidate.Normal,Point.Difference-Vertices[Candidate.A].Difference)<=EpaTolerance){ continue; }
                Faces[Index]=Candidate with { Active=false };
                AddBoundaryEdge(Edges,ref EdgeCount,Candidate.A,Candidate.B);
                AddBoundaryEdge(Edges,ref EdgeCount,Candidate.B,Candidate.C);
                AddBoundaryEdge(Edges,ref EdgeCount,Candidate.C,Candidate.A);
            }
            for(int Index=0;Index<EdgeCount;Index++){
                if(FaceCount==Faces.Length||!TryAppendFace(Vertices,Faces,ref FaceCount,Edges[Index].A,Edges[Index].B,NewVertex)){ return false; }
            }
        }
        return false;
    }

    private static bool TryAppendFace(Span<SupportPoint> Vertices,Span<EpaFace> Faces,ref int Count,int A,int B,int C){
        LogicalVector3 AB=Vertices[B].Difference-Vertices[A].Difference;
        LogicalVector3 AC=Vertices[C].Difference-Vertices[A].Difference;
        LogicalVector3 Normal=LogicalVector3.Cross(AB,AC);
        if(!Normal.TryNormalize(out Normal)){ return false; }
        LogicalScalar Distance=LogicalVector3.Dot(Normal,Vertices[A].Difference);
        if(Distance<LogicalScalar.Zero){
            (B,C)=(C,B);
            Normal=-Normal;
            Distance=-Distance;
        }
        Faces[Count++]=new EpaFace(A,B,C,Normal,Distance,true);
        return true;
    }

    private static void AddBoundaryEdge(Span<EpaEdge> Edges,ref int Count,int A,int B){
        for(int Index=0;Index<Count;Index++){
            if(Edges[Index].A==B&&Edges[Index].B==A){
                Edges[Index]=Edges[--Count];
                return;
            }
        }
        if(Count<Edges.Length){ Edges[Count++]=new EpaEdge(A,B); }
    }

    private static SupportPoint Support(ShapeProxy First,ShapeProxy Second,LogicalVector3 Direction){
        LogicalVector3 FirstPoint=First.Support(Direction);
        LogicalVector3 SecondPoint=Second.Support(-Direction);
        return new SupportPoint(FirstPoint-SecondPoint,FirstPoint,SecondPoint);
    }

    private static bool TryTransformBoundsToLocal(
        in PhysicsAabb Bounds,LogicalVector3 Position,LogicalQuaternion Rotation,out PhysicsAabb Local){
        LogicalQuaternion Inverse=Rotation.Conjugate();
        Span<LogicalVector3> Corners=stackalloc LogicalVector3[8];
        int Index=0;
        for(int R=0;R<2;R++){
            for(int U=0;U<2;U++){
                for(int F=0;F<2;F++){
                    LogicalVector3 Point=new(
                        R==0?Bounds.Min.R:Bounds.Max.R,
                        U==0?Bounds.Min.U:Bounds.Max.U,
                        F==0?Bounds.Min.F:Bounds.Max.F);
                    if(!Inverse.TryRotate(Point-Position,out Corners[Index++])){ Local=default;return false; }
                }
            }
        }
        LogicalVector3 Minimum=Corners[0];
        LogicalVector3 Maximum=Corners[0];
        for(Index=1;Index<Corners.Length;Index++){
            LogicalVector3 Point=Corners[Index];
            Minimum=new LogicalVector3(Min(Minimum.R,Point.R),Min(Minimum.U,Point.U),Min(Minimum.F,Point.F));
            Maximum=new LogicalVector3(Max(Maximum.R,Point.R),Max(Maximum.U,Point.U),Max(Maximum.F,Point.F));
        }
        Local=new PhysicsAabb(Minimum,Maximum);
        return true;
    }

    private static bool IsConvex(ColliderShape Shape)=>Shape is ColliderShape.Box or ColliderShape.Sphere or ColliderShape.Capsule or ColliderShape.ConvexAsset;

    private static LogicalVector3 ChoosePerpendicular(LogicalVector3 Value){
        LogicalVector3 Axis=Abs(Value.R)<=Abs(Value.U)&&Abs(Value.R)<=Abs(Value.F)
            ?LogicalVector3.UnitR:Abs(Value.U)<=Abs(Value.F)?LogicalVector3.UnitU:LogicalVector3.UnitF;
        LogicalVector3 Result=LogicalVector3.Cross(Value,Axis);
        return Result.LengthSquared.IsZero?LogicalVector3.UnitR:Result;
    }

    private static LogicalScalar Abs(LogicalScalar Value)=>Value<LogicalScalar.Zero?-Value:Value;
    private static LogicalScalar Min(LogicalScalar Left,LogicalScalar Right)=>Left<Right?Left:Right;
    private static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;
    private static LogicalScalar Clamp(LogicalScalar Value,LogicalScalar Minimum,LogicalScalar Maximum)=>
        Value<Minimum?Minimum:Value>Maximum?Maximum:Value;

    private readonly struct ShapeProxy {
        private readonly Collider Collider;
        private readonly LogicalQuaternion Rotation;
        private readonly PhysicsShapeAsset? Asset;
        private readonly LogicalVector3 TriangleA;
        private readonly LogicalVector3 TriangleB;
        private readonly LogicalVector3 TriangleC;
        private readonly LogicalVector3 TriangleNormal;
        private readonly bool IsTriangle;

        internal ShapeProxy(in PhysicsCollisionShape Shape){
            this.Collider=Shape.Collider;
            this.Rotation=Shape.Rotation;
            if(!Shape.Rotation.TryRotate(Shape.Collider.Center,out LogicalVector3 RotatedCenter)){ throw new OverflowException(); }
            this.Center=Shape.Position+RotatedCenter;
            this.Asset=Shape.Asset;
            this.TriangleA=default;
            this.TriangleB=default;
            this.TriangleC=default;
            this.TriangleNormal=default;
            this.IsTriangle=false;
        }

        internal ShapeProxy(
            LogicalVector3 Position,LogicalQuaternion Rotation,
            LogicalVector3 A,LogicalVector3 B,LogicalVector3 C){
            this.Collider=default;
            this.Center=Position;
            this.Rotation=Rotation;
            this.Asset=null;
            this.TriangleA=A;
            this.TriangleB=B;
            this.TriangleC=C;
            LogicalVector3 Normal=LogicalVector3.Cross(B-A,C-A);
            this.TriangleNormal=Normal.TryNormalize(out LogicalVector3 Unit)?Unit:LogicalVector3.UnitU;
            this.IsTriangle=true;
        }

        internal LogicalVector3 Center { get; }

        internal LogicalVector3 Support(LogicalVector3 Direction){
            LogicalQuaternion Inverse=this.Rotation.Conjugate();
            if(!Inverse.TryRotate(Direction,out LogicalVector3 LocalDirection)){ throw new OverflowException(); }
            LogicalVector3 Local;
            if(this.IsTriangle){
                LogicalScalar ADot=LogicalVector3.Dot(this.TriangleA,LocalDirection);
                LogicalScalar BDot=LogicalVector3.Dot(this.TriangleB,LocalDirection);
                LogicalScalar CDot=LogicalVector3.Dot(this.TriangleC,LocalDirection);
                Local=ADot>=BDot&&ADot>=CDot?this.TriangleA:BDot>=CDot?this.TriangleB:this.TriangleC;
                Local+=this.TriangleNormal*(LogicalVector3.Dot(this.TriangleNormal,LocalDirection)>=LogicalScalar.Zero?TriangleHalfThickness:-TriangleHalfThickness);
            }
            else {
                Local=this.Collider.Shape switch {
                    ColliderShape.Box=>new LogicalVector3(
                        LocalDirection.R>=LogicalScalar.Zero?this.Collider.HalfExtents.R:-this.Collider.HalfExtents.R,
                        LocalDirection.U>=LogicalScalar.Zero?this.Collider.HalfExtents.U:-this.Collider.HalfExtents.U,
                        LocalDirection.F>=LogicalScalar.Zero?this.Collider.HalfExtents.F:-this.Collider.HalfExtents.F),
                    ColliderShape.Sphere=>NormalizeOrAxis(LocalDirection)*this.Collider.Radius,
                    ColliderShape.Capsule=>new LogicalVector3(
                        LogicalScalar.Zero,
                        LocalDirection.U>=LogicalScalar.Zero?this.Collider.HalfHeight:-this.Collider.HalfHeight,
                        LogicalScalar.Zero)+NormalizeOrAxis(LocalDirection)*this.Collider.Radius,
                    ColliderShape.ConvexAsset when this.Asset is { Kind:PhysicsShapeAssetKind.Convex } Asset=>Asset.Support(LocalDirection),
                    ColliderShape.StaticTriangleMesh when this.Asset is { } Mesh=>Mesh.Support(LocalDirection),
                    ColliderShape.Compound when this.Asset is { Kind:PhysicsShapeAssetKind.Compound } Compound=>Compound.Support(LocalDirection),
                    _=>throw new InvalidOperationException("The collider is not support-mapped."),
                };
                Local+=this.Collider.Center;
            }
            if(!this.Rotation.TryRotate(Local,out LogicalVector3 Rotated)){ throw new OverflowException(); }
            return this.Center+Rotated;
        }

        private static LogicalVector3 NormalizeOrAxis(LogicalVector3 Value)=>Value.TryNormalize(out LogicalVector3 Unit)?Unit:LogicalVector3.UnitR;
    }
}
