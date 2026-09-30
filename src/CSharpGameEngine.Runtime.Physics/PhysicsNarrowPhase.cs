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
    /// <summary>Face normal of the touched triangle, pointing toward the other shape; zero for contacts without a single face.</summary>
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

/// <summary>Support-mapped convex narrow phase. Meshes are queried through their cooked BVH.</summary>
internal sealed class PhysicsNarrowPhase {
    private const int GjkIterations=32;
    private const int EpaIterations=48;
    private const int MaximumEpaVertices=64;
    private const int MaximumEpaFaces=128;
    private static readonly LogicalScalar EpaTolerance=LogicalScalar.FromSingle(0.0001f);
    private static readonly LogicalScalar TriangleHalfThickness=LogicalScalar.FromSingle(0.0005f);
    private readonly List<int> TriangleCandidates=[];
    private readonly Dictionary<PhysicsShapeAsset,AssetBoundsEntry> AssetBounds=new(ReferenceEqualityComparer.Instance);

    private readonly record struct AssetBoundsEntry(
        LogicalQuaternion Rotation,LogicalVector3 ColliderCenter,
        LogicalVector3 PositiveR,LogicalVector3 PositiveU,LogicalVector3 PositiveF,
        LogicalVector3 NegativeR,LogicalVector3 NegativeU,LogicalVector3 NegativeF);

    private readonly record struct SupportPoint(LogicalVector3 Difference,LogicalVector3 First,LogicalVector3 Second);
    private readonly record struct EpaFace(int A,int B,int C,LogicalVector3 Normal,LogicalScalar Distance,bool Active);
    private readonly record struct EpaEdge(int A,int B);

    internal bool TryCreateContact(
        in PhysicsCollisionShape First,in PhysicsCollisionShape Second,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(First.Collider.Shape==ColliderShape.StaticTriangleMesh){
            return this.TryMeshContact(Second,First,true,out Contact);
        }
        if(Second.Collider.Shape==ColliderShape.StaticTriangleMesh){
            return this.TryMeshContact(First,Second,false,out Contact);
        }
        if(!IsConvex(First.Collider.Shape)||!IsConvex(Second.Collider.Shape)){ return false; }
        if(TryPrimitiveContact(First,Second,out Contact)){ return true; }
        return TryConvexContact(new ShapeProxy(First),new ShapeProxy(Second),0,out Contact);
    }

    internal PhysicsAabb CalculateBounds(in PhysicsCollisionShape Shape){
        if(Shape.Collider.Shape is not (ColliderShape.ConvexAsset or ColliderShape.StaticTriangleMesh)){
            return CalculateBoundsUncached(Shape);
        }
        if(Shape.Asset is null){ throw new InvalidOperationException("Cooked shape asset is unavailable."); }
        // Asset supports scan every vertex, and static meshes are re-bounded several times per tick
        // without moving. The six support points relative to the proxy centre depend only on the
        // asset, rotation and collider centre, and the translation is an exact addition, so a cached
        // entry reproduces CalculateBoundsUncached bit for bit.
        ShapeProxy Proxy=new(Shape);
        if(!this.AssetBounds.TryGetValue(Shape.Asset,out AssetBoundsEntry Entry)
            ||Entry.Rotation!=Shape.Rotation||Entry.ColliderCenter!=Shape.Collider.Center){
            Entry=new AssetBoundsEntry(Shape.Rotation,Shape.Collider.Center,
                Proxy.Support(LogicalVector3.UnitR)-Proxy.Center,Proxy.Support(LogicalVector3.UnitU)-Proxy.Center,Proxy.Support(LogicalVector3.UnitF)-Proxy.Center,
                Proxy.Support(-LogicalVector3.UnitR)-Proxy.Center,Proxy.Support(-LogicalVector3.UnitU)-Proxy.Center,Proxy.Support(-LogicalVector3.UnitF)-Proxy.Center);
            this.AssetBounds[Shape.Asset]=Entry;
        }
        return new PhysicsAabb(
            new LogicalVector3((Proxy.Center+Entry.NegativeR).R,(Proxy.Center+Entry.NegativeU).U,(Proxy.Center+Entry.NegativeF).F),
            new LogicalVector3((Proxy.Center+Entry.PositiveR).R,(Proxy.Center+Entry.PositiveU).U,(Proxy.Center+Entry.PositiveF).F));
    }

    internal static PhysicsAabb CalculateBoundsUncached(in PhysicsCollisionShape Shape){
        if(Shape.Collider.Shape is ColliderShape.ConvexAsset or ColliderShape.StaticTriangleMesh){
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

    private bool TryMeshContact(
        in PhysicsCollisionShape Convex,in PhysicsCollisionShape Mesh,bool MeshIsFirst,
        out PhysicsContactCandidate Contact){
        Contact=default;
        if(!IsConvex(Convex.Collider.Shape)||Mesh.Asset is not { Kind:PhysicsShapeAssetKind.StaticTriangleMesh } Asset){ return false; }
        PhysicsAabb WorldBounds=this.CalculateBounds(Convex);
        if(!TryTransformBoundsToLocal(WorldBounds,Mesh.Position,Mesh.Rotation,out PhysicsAabb LocalBounds)){ return false; }
        this.TriangleCandidates.Clear();
        Asset.QueryTriangles(LocalBounds,this.TriangleCandidates);
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
        Contact=Best;
        return Found;
    }

    /// <summary>
    /// Every penetrating triangle of <paramref name="Mesh"/> against a sphere or capsule, in
    /// triangle order, with normals pointing from the query shape into the mesh. Character
    /// movement needs all of them (a floor and a wall at once), not only the deepest.
    /// </summary>
    internal int CollectMeshContacts(in PhysicsCollisionShape Query,in PhysicsCollisionShape Mesh,List<PhysicsContactCandidate> Results){
        if(Query.Collider.Shape is not (ColliderShape.Sphere or ColliderShape.Capsule)
            ||Mesh.Asset is not { Kind:PhysicsShapeAssetKind.StaticTriangleMesh } Asset){ return 0; }
        PhysicsAabb WorldBounds=this.CalculateBounds(Query);
        if(!TryTransformBoundsToLocal(WorldBounds,Mesh.Position,Mesh.Rotation,out PhysicsAabb LocalBounds)){ return 0; }
        this.TriangleCandidates.Clear();
        Asset.QueryTriangles(LocalBounds,this.TriangleCandidates);
        int Start=Results.Count;
        foreach(int Triangle in this.TriangleCandidates){
            Asset.GetTriangle(Triangle,out LogicalVector3 A,out LogicalVector3 B,out LogicalVector3 C);
            bool Hit=Query.Collider.Shape==ColliderShape.Sphere
                ?TrySphereTriangleContact(Query,Mesh,A,B,C,false,checked((uint)Triangle),out PhysicsContactCandidate Contact)
                :TryCapsuleTriangleContact(Query,Mesh,A,B,C,false,checked((uint)Triangle),out Contact);
            if(Hit){ Results.Add(Contact); }
        }
        return Results.Count-Start;
    }

    /// <summary>
    /// Closed-form capsule-triangle contact: the closest points between the capsule's segment and
    /// the triangle. A segment crossing the triangle is pushed out along the face normal toward the
    /// capsule centre's side, deep enough to clear both segment ends.
    /// </summary>
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

    /// <summary>Closest points between segment PQ and triangle ABC; returns true when the segment crosses the triangle.</summary>
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

    /// <summary>Closest points between segments P1Q1 and P2Q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
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

    /// <summary>Unit face normal of triangle ABC on the side of <paramref name="Point"/>; zero for a degenerate triangle.</summary>
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
