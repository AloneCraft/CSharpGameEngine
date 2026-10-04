using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

internal enum GizmoHandleId {
    AxisX, AxisY, AxisZ, PlaneXY, PlaneYZ, PlaneZX,
    RotateX, RotateY, RotateZ, RotateView,
    ScaleX, ScaleY, ScaleZ, ScaleUniform,
    CameraFov, CameraNear, CameraFar, CameraOrthographicSize,
    DirectionalDirection, PointRange,
    SpotDirection, SpotInnerCone, SpotOuterCone, SpotRange,
    RectRange, RectWidth, RectHeight,
    TransformSpaceToggle,
    SurfaceSnapPlane,
}

internal enum GizmoPrimitiveKind { Line, Triangle }

internal readonly struct GizmoOverlayVertex {
    internal GizmoOverlayVertex(Vector3 Position,Vector4 Color){
        this.Position=Position;
        this.Color=Color;
    }

    internal Vector3 Position { get; }

    internal Vector4 Color { get; }
}

internal sealed class GizmoOverlayPrimitive {
    internal GizmoOverlayPrimitive(GizmoPrimitiveKind Kind,IReadOnlyList<GizmoOverlayVertex>Vertices){
        this.Kind=Kind;
        this.Vertices=Vertices;
    }

    internal GizmoPrimitiveKind Kind { get; }

    internal IReadOnlyList<GizmoOverlayVertex>Vertices { get; }

    /// <summary>
    /// True when both overlays draw exactly the same primitives. The Scene view rebuilds its overlay
    /// on every UI tick; comparing lets it request a new preview frame only when the overlay changed.
    /// </summary>
    internal static bool SameContent(IReadOnlyList<GizmoOverlayPrimitive>Left,IReadOnlyList<GizmoOverlayPrimitive>Right){
        if(Left.Count!=Right.Count){
            return false;
        }
        for(int Index=0;Index<Left.Count;Index++){
            GizmoOverlayPrimitive First=Left[Index];
            GizmoOverlayPrimitive Second=Right[Index];
            if(First.Kind!=Second.Kind||First.Vertices.Count!=Second.Vertices.Count){
                return false;
            }
            for(int Vertex=0;Vertex<First.Vertices.Count;Vertex++){
                if(!First.Vertices[Vertex].Position.Equals(Second.Vertices[Vertex].Position)
                    ||!First.Vertices[Vertex].Color.Equals(Second.Vertices[Vertex].Color)){
                    return false;
                }
            }
        }
        return true;
    }
}

internal readonly struct MoveGizmoFrame {
    private MoveGizmoFrame(
        Vector3 Pivot,Vector3 AxisX,Vector3 AxisY,Vector3 AxisZ,Quaternion BasisRotation,float WorldScale){
        this.Pivot=Pivot;
        this.AxisX=AxisX;
        this.AxisY=AxisY;
        this.AxisZ=AxisZ;
        this.BasisRotation=BasisRotation;
        this.WorldScale=WorldScale;
    }

    internal Vector3 Pivot { get; }

    internal Vector3 AxisX { get; }

    internal Vector3 AxisY { get; }

    internal Vector3 AxisZ { get; }

    internal Quaternion BasisRotation { get; }

    internal float WorldScale { get; }

    internal static MoveGizmoFrame Create(
        Vector3 Pivot,Quaternion Rotation,SceneTransformSpace Space,float WorldScale){
        Quaternion Basis=Space==SceneTransformSpace.Local
            ?SceneTransformMath.NormalizeRotation(Rotation)
            :Quaternion.Identity;
        return new MoveGizmoFrame(
            Pivot,
            Vector3.Normalize(Vector3.Transform(Vector3.UnitX,Basis)),
            Vector3.Normalize(Vector3.Transform(Vector3.UnitY,Basis)),
            Vector3.Normalize(Vector3.Transform(Vector3.UnitZ,Basis)),
            Basis,
            Math.Max(WorldScale,1e-5f));
    }

    internal Vector3 GetAxis(GizmoHandleId Handle)=>Handle switch {
        GizmoHandleId.AxisX=> this.AxisX,
        GizmoHandleId.AxisY=> this.AxisY,
        GizmoHandleId.AxisZ=> this.AxisZ,
        _=>throw new ArgumentOutOfRangeException(nameof(Handle)),
    };

    internal (Vector3 First,Vector3 Second)GetPlaneAxes(GizmoHandleId Handle)=>Handle switch {
        GizmoHandleId.PlaneXY=>(this.AxisX, this.AxisY),
        GizmoHandleId.PlaneYZ=>(this.AxisY, this.AxisZ),
        GizmoHandleId.PlaneZX=>(this.AxisZ, this.AxisX),
        _=>throw new ArgumentOutOfRangeException(nameof(Handle)),
    };
}

/// <summary>Moveギズモの表示形状、スクリーン一定サイズ、解析的ヒット判定。</summary>
internal static partial class TransformGizmo {
    private const float ScreenFraction=0.16f;
    // M0037: 実機フィードバックでM0035の0.26fでもまだ短く感じるとのことでさらに離した
    // (ScaleUniformキューブの対角到達距離0.19より確実に外側、Move平面ハンドルのPlaneMin(0.22)
    // より少し外側という制約自体は変わらない)。
    private const float AxisStart=0.40f;
    // M0033/M0037: Rotate/Scaleが常時同時表示になったため、Scaleキューブ帯(≈0.93-1.08)より
    // 外側へ押し出し(TransformGizmoPhase4.csのコメント参照)、M0037で矢印全体をさらに伸ばした。
    private const float AxisLength=1.60f;
    private const float AxisRadius=0.08f;
    private const float PlaneMin=0.22f;
    private const float PlaneMax=0.48f;

    internal static float ComputeWorldScale(
        SceneProjectionMode ProjectionMode,float DistanceToCamera,float FovYRadians,float OrthographicSize)=>
        ProjectionMode==SceneProjectionMode.Orthographic
            ?Math.Max(OrthographicSize,0.01f)*ScreenFraction
            :Math.Max(DistanceToCamera,0.01f)*FloatMath.Tan(Math.Max(FovYRadians,0.01f)*0.5f)*ScreenFraction;

    internal static GizmoHandleId?TryHitMove(
        in MoveGizmoFrame Frame,Vector3 RayOrigin,Vector3 RayDirection,out float BestRayDistance,
        ICollection<GizmoHandleId>?Disabled=null){
        RayDirection=Vector3.Normalize(RayDirection);
        GizmoHandleId?Best=null;
        BestRayDistance=float.PositiveInfinity;

        foreach(GizmoHandleId Handle in new[]{GizmoHandleId.PlaneXY,GizmoHandleId.PlaneYZ,GizmoHandleId.PlaneZX}){
            if(Disabled?.Contains(Handle)==true){
                continue;
            }
            (Vector3 First,Vector3 Second)=Frame.GetPlaneAxes(Handle);
            Vector3 Normal=Vector3.Normalize(Vector3.Cross(First,Second));
            if(!TryIntersectPlane(RayOrigin,RayDirection,Frame.Pivot,Normal,out Vector3 Point,out float RayDistance)){
                continue;
            }

            Vector3 Relative=(Point-Frame.Pivot)/Frame.WorldScale;
            float U=Vector3.Dot(Relative,First);
            float V=Vector3.Dot(Relative,Second);
            if(U>=PlaneMin&&U<=PlaneMax&&V>=PlaneMin&&V<=PlaneMax&&RayDistance<BestRayDistance){
                Best=Handle;
                BestRayDistance=RayDistance;
            }
        }

        foreach(GizmoHandleId Handle in new[]{GizmoHandleId.AxisX,GizmoHandleId.AxisY,GizmoHandleId.AxisZ}){
            if(Disabled?.Contains(Handle)==true){
                continue;
            }
            Vector3 Axis=Frame.GetAxis(Handle);
            Vector3 SegmentStart=Frame.Pivot+(Axis*(AxisStart*Frame.WorldScale));
            Vector3 SegmentEnd=Frame.Pivot+(Axis*(AxisLength*Frame.WorldScale));
            ClosestRaySegment(RayOrigin,RayDirection,SegmentStart,SegmentEnd,out float RayDistance,out float Distance);
            if(Distance<=AxisRadius*Frame.WorldScale&&RayDistance>=0f&&RayDistance<BestRayDistance){
                Best=Handle;
                BestRayDistance=RayDistance;
            }
        }

        return Best;
    }

    /// <summary>
    /// Move・Rotate・Scale・Local/GlobalトグルアイコンをまとめてヒットテストしRay距離最小を採用する
    /// (M0033: 3系統の常時同時表示化)。各`TryHit*`は自系統内で独立して最近傍を選ぶだけなので、
    /// 系統をまたいだ優先順位はここで解決する。
    ///
    /// <para>
    /// M0034: <paramref name="SurfaceSnapArmed"/>(Shift+Ctrl押下中)がtrueかつ
    /// <see cref="GizmoHandleId.SurfaceSnapPlane"/>が編集可能な場合、中心の平面四角だけを
    /// ヒットテストし他系統は一切試さない(短絡)。ピボットで<c>ScaleUniform</c>と同座標になり
    /// レイ距離比較では決まらないため、Propertyギズモが先に短絡する既存の前例と同じ形にした。
    /// 四角が無効(Rotationが式等で編集不可)なら通常の統合ヒットテストへフォールバックする —
    /// 修飾キーを押しっぱなしでも他ハンドルを掴めなくなることを避けるため。
    /// </para>
    /// </summary>
    internal static GizmoHandleId?TryHitTransform(
        in MoveGizmoFrame Frame,Vector3 RayOrigin,Vector3 RayDirection,
        Vector3 CameraForward,Vector3 CameraRight,Vector3 CameraUp,
        bool SurfaceSnapArmed=false,ICollection<GizmoHandleId>?Disabled=null){
        if(SurfaceSnapArmed&&Disabled?.Contains(GizmoHandleId.SurfaceSnapPlane)!=true){
            return SurfaceSnapGizmo.TryHit(Frame,CameraForward,RayOrigin,RayDirection,out _);
        }

        GizmoHandleId?Best=null;
        float BestRayDistance=float.PositiveInfinity;
        Consider(TryHitMove(Frame,RayOrigin,RayDirection,out float MoveDistance,Disabled),MoveDistance);
        Consider(TryHitRotate(Frame,RayOrigin,RayDirection,CameraForward,out float RotateDistance,Disabled),RotateDistance);
        Consider(TryHitScale(Frame,RayOrigin,RayDirection,out float ScaleDistance,Disabled),ScaleDistance);
        Consider(TransformSpaceToggleGizmo.TryHit(
            Frame,CameraRight,CameraUp,RayOrigin,RayDirection,out float ToggleDistance),ToggleDistance);
        return Best;

        void Consider(GizmoHandleId?Hit,float RayDistance){
            if(Hit is { }Handle&&RayDistance<BestRayDistance){
                Best=Handle;
                BestRayDistance=RayDistance;
            }
        }
    }

    internal static IReadOnlyList<GizmoOverlayPrimitive>BuildMoveOverlay(
        in MoveGizmoFrame Frame,GizmoHandleId?Hovered,GizmoHandleId?Active,
        ICollection<GizmoHandleId>?Disabled=null){
        MoveGizmoFrame Value=Frame;
        List<GizmoOverlayPrimitive>Primitives=[];
        AddAxis(GizmoHandleId.AxisX,Value.AxisX,new Vector4(1f,0.15f,0.1f,1f));
        AddAxis(GizmoHandleId.AxisY,Value.AxisY,new Vector4(0.2f,1f,0.2f,1f));
        AddAxis(GizmoHandleId.AxisZ,Value.AxisZ,new Vector4(0.2f,0.45f,1f,1f));
        AddPlane(GizmoHandleId.PlaneXY,Value.AxisX,Value.AxisY,new Vector4(1f,1f,0.15f,0.45f));
        AddPlane(GizmoHandleId.PlaneYZ,Value.AxisY,Value.AxisZ,new Vector4(0.15f,1f,1f,0.45f));
        AddPlane(GizmoHandleId.PlaneZX,Value.AxisZ,Value.AxisX,new Vector4(1f,0.15f,1f,0.45f));
        return Primitives;

        Vector4 ResolveColor(GizmoHandleId Handle,Vector4 Base)=>
            Disabled?.Contains(Handle)==true
                ?new Vector4(Base.X*0.35f,Base.Y*0.35f,Base.Z*0.35f,0.2f)
                :Active==Handle?Vector4.One:Hovered==Handle?Vector4.Lerp(Base,Vector4.One,0.55f):Base;

        void AddAxis(GizmoHandleId Handle,Vector3 Axis,Vector4 Base){
            Vector4 Color=ResolveColor(Handle,Base);
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(Value.Pivot,Color),
                new GizmoOverlayVertex(Value.Pivot+(Axis*Value.WorldScale),Color),
            ]));

            const int Segments=8;
            const float ShaftEnd=1.38f;
            const float ShaftRadius=0.025f;
            // M0098 B: コーンの底を判定半径で描き、描いた矢印をTryHitMoveのカプセルに収める(以前の0.085は縁が外れた)。
            const float ArrowRadius=AxisRadius;
            Vector3 Reference=Math.Abs(Vector3.Dot(Axis,Vector3.UnitY))<0.9f
                ?Vector3.UnitY
                :Vector3.UnitX;
            Vector3 SideA=Vector3.Normalize(Vector3.Cross(Axis,Reference));
            Vector3 SideB=Vector3.Normalize(Vector3.Cross(Axis,SideA));
            Vector3 ShaftStart=Value.Pivot+(Axis*(AxisStart*Value.WorldScale));
            Vector3 ShaftFinish=Value.Pivot+(Axis*(ShaftEnd*Value.WorldScale));
            Vector3 Tip=Value.Pivot+(Axis*(AxisLength*Value.WorldScale));
            List<GizmoOverlayVertex>Solid=[];
            for(var Segment=0;Segment<Segments;Segment++){
                float AngleA=Segment*FloatMath.Pi*2f/Segments;
                float AngleB=(Segment+1)*FloatMath.Pi*2f/Segments;
                Vector3 RadialA=(SideA*FloatMath.Cos(AngleA))+(SideB*FloatMath.Sin(AngleA));
                Vector3 RadialB=(SideA*FloatMath.Cos(AngleB))+(SideB*FloatMath.Sin(AngleB));
                Vector3 StartA=ShaftStart+(RadialA*(ShaftRadius*Value.WorldScale));
                Vector3 StartB=ShaftStart+(RadialB*(ShaftRadius*Value.WorldScale));
                Vector3 FinishA=ShaftFinish+(RadialA*(ShaftRadius*Value.WorldScale));
                Vector3 FinishB=ShaftFinish+(RadialB*(ShaftRadius*Value.WorldScale));
                Vector3 ConeA=ShaftFinish+(RadialA*(ArrowRadius*Value.WorldScale));
                Vector3 ConeB=ShaftFinish+(RadialB*(ArrowRadius*Value.WorldScale));
                Solid.Add(new GizmoOverlayVertex(StartA,Color));
                Solid.Add(new GizmoOverlayVertex(StartB,Color));
                Solid.Add(new GizmoOverlayVertex(FinishB,Color));
                Solid.Add(new GizmoOverlayVertex(StartA,Color));
                Solid.Add(new GizmoOverlayVertex(FinishB,Color));
                Solid.Add(new GizmoOverlayVertex(FinishA,Color));
                Solid.Add(new GizmoOverlayVertex(ConeA,Color));
                Solid.Add(new GizmoOverlayVertex(ConeB,Color));
                Solid.Add(new GizmoOverlayVertex(Tip,Color));
            }
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,Solid));
        }

        void AddPlane(GizmoHandleId Handle,Vector3 First,Vector3 Second,Vector4 Base){
            Vector4 Color=ResolveColor(Handle,Base);
            Vector3 A=Value.Pivot+((First+Second)*(PlaneMin*Value.WorldScale));
            Vector3 B=Value.Pivot+((First*PlaneMax+Second*PlaneMin)*Value.WorldScale);
            Vector3 C=Value.Pivot+((First+Second)*(PlaneMax*Value.WorldScale));
            Vector3 D=Value.Pivot+((First*PlaneMin+Second*PlaneMax)*Value.WorldScale);
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,[
                new GizmoOverlayVertex(A,Color),new GizmoOverlayVertex(B,Color),new GizmoOverlayVertex(C,Color),
                new GizmoOverlayVertex(A,Color),new GizmoOverlayVertex(C,Color),new GizmoOverlayVertex(D,Color),
            ]));
        }
    }

    internal static bool TryIntersectPlane(
        Vector3 RayOrigin,Vector3 RayDirection,Vector3 PlanePoint,Vector3 PlaneNormal,
        out Vector3 Point,out float RayDistance){
        float Denominator=Vector3.Dot(RayDirection,PlaneNormal);
        if(Math.Abs(Denominator)<=1e-6f){
            Point=default;
            RayDistance=0f;
            return false;
        }

        RayDistance=Vector3.Dot(PlanePoint-RayOrigin,PlaneNormal)/Denominator;
        if(RayDistance<0f){
            Point=default;
            return false;
        }

        Point=RayOrigin+(RayDirection*RayDistance);
        return true;
    }

    /// <summary>
    /// レイ(<paramref name="RayDistance"/>≥0)と線分の最近点の厳密解(Ericson『Real-Time Collision Detection』5.1.9)。
    /// 線分側をクランプしたらレイ側を解き直し、レイ側を0へクランプしたら線分側を解き直す。M0098 B: 解き直さないと、
    /// 軸と浅い角度で矢印の先端を通るレイや、軸に平行なレイ(正射投影で軸の方向から見る)が、描いた矢印の上でも外れた。
    /// </summary>
    private static void ClosestRaySegment(
        Vector3 RayOrigin,Vector3 RayDirection,Vector3 SegmentStart,Vector3 SegmentEnd,
        out float RayDistance,out float Distance){
        var Segment=SegmentEnd-SegmentStart;
        var Offset=RayOrigin-SegmentStart;
        var A=Vector3.Dot(RayDirection,RayDirection);
        var B=Vector3.Dot(RayDirection,Segment);
        var C=Vector3.Dot(Segment,Segment);
        var D=Vector3.Dot(RayDirection,Offset);
        var E=Vector3.Dot(Segment,Offset);
        var Denominator=(A*C)-(B*B);
        // 平行(長さに依存しない相対判定)なら線分のどの点でも距離は同じなので、始点から解く。
        var SegmentParameter=Denominator>1e-6f*A*C?Clamp01(((A*E)-(B*D))/Denominator):0f;
        RayDistance=((B*SegmentParameter)-D)/A;
        if(RayDistance<0f){
            RayDistance=0f;
            SegmentParameter=Clamp01(E/C);
        }
        var OnRay=RayOrigin+(RayDirection*RayDistance);
        var OnSegment=SegmentStart+(Segment*SegmentParameter);
        Distance=Vector3.Distance(OnRay,OnSegment);
    }

    private static float Clamp01(float Value)=>Math.Max(0f,Math.Min(1f,Value));
}

/// <summary>MouseDown時の拘束を保持し、各rayから開始点との差分を返す。</summary>
internal sealed class MoveGizmoDrag {
    private readonly MoveGizmoFrame Frame;
    private readonly GizmoHandleId Handle;
    private readonly Vector3 ConstraintNormal;
    private readonly Vector3 StartPoint;

    private MoveGizmoDrag(
        in MoveGizmoFrame Frame,GizmoHandleId Handle,Vector3 ConstraintNormal,Vector3 StartPoint){
        this.Frame=Frame;
        this.Handle=Handle;
        this.ConstraintNormal=ConstraintNormal;
        this.StartPoint=StartPoint;
    }

    internal static bool TryBegin(
        in MoveGizmoFrame Frame,GizmoHandleId Handle,
        Vector3 RayOrigin,Vector3 RayDirection,Vector3 CameraForward,out MoveGizmoDrag?Drag){
        Vector3 Normal;
        if(Handle is GizmoHandleId.AxisX or GizmoHandleId.AxisY or GizmoHandleId.AxisZ){
            Vector3 Axis=Frame.GetAxis(Handle);
            Normal=CameraForward-(Axis*Vector3.Dot(CameraForward,Axis));
            if(Normal.LengthSquared()<=1e-8f){
                Vector3 Fallback=Math.Abs(Vector3.Dot(Axis,Vector3.UnitY))<0.9f?Vector3.UnitY:Vector3.UnitX;
                Normal=Fallback-(Axis*Vector3.Dot(Fallback,Axis));
            }
            Normal=Vector3.Normalize(Normal);
        } else {
            (Vector3 First,Vector3 Second)=Frame.GetPlaneAxes(Handle);
            Normal=Vector3.Normalize(Vector3.Cross(First,Second));
        }

        if(!TransformGizmo.TryIntersectPlane(
            RayOrigin,Vector3.Normalize(RayDirection),Frame.Pivot,Normal,out Vector3 StartPoint,out _)){
            Drag=null;
            return false;
        }

        Drag=new MoveGizmoDrag(Frame,Handle,Normal,StartPoint);
        return true;
    }

    internal bool TryUpdate(Vector3 RayOrigin,Vector3 RayDirection,out Vector3 Delta){
        if(!TransformGizmo.TryIntersectPlane(
            RayOrigin,Vector3.Normalize(RayDirection), this.Frame.Pivot, this.ConstraintNormal,out Vector3 Point,out _)){
            Delta=default;
            return false;
        }

        Vector3 Raw=Point- this.StartPoint;
        if(this.Handle is GizmoHandleId.AxisX or GizmoHandleId.AxisY or GizmoHandleId.AxisZ){
            Vector3 Axis= this.Frame.GetAxis(this.Handle);
            Delta=Axis*Vector3.Dot(Raw,Axis);
        } else {
            (Vector3 First,Vector3 Second)= this.Frame.GetPlaneAxes(this.Handle);
            Delta=(First*Vector3.Dot(Raw,First))+(Second*Vector3.Dot(Raw,Second));
        }
        return true;
    }
}
