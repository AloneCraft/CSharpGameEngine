using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>Phase 4のRotate/Scaleハンドル。Moveと同じframe・overlay・解析hit経路を使う。</summary>
internal static partial class TransformGizmo {
    // M0033: Move/Rotate/Scaleを常時同時表示にするにあたり、半径帯を重ならないよう再設計した
    // (旧RotateRingRadius=0.9fはScaleAxisLength=0.9fと完全一致しキューブとリングが同座標に重なり、
    // またMove平面ハンドルと同一平面上にあるため対角最大到達距離0.68より内側だと平面ハンドルの
    // 当たり判定を貫通していた)。詳細はDocs/Milestones/M0030-SceneView-Gizmo-Transform-Editing.md
    // のM0033追記を参照。TransformGizmo.csのAxisLength/ShaftEnd(Move矢印)も同じ理由で合わせて
    // 外側へ再配置してある。
    // M0037: internal化して<see cref="GizmoDragHud"/>から扇の半径として再利用する。
    internal const float RotateRingRadius=0.78f;
    // M0037: Move矢印の全長をAxisLength=1.60fへ伸ばしたため、最外周であり続けるようこちらも押し出した。
    internal const float RotateViewRingRadius=1.85f;
    private const float RotateRingThickness=0.07f;
    private const float ScaleAxisStart=0.16f;
    private const float ScaleAxisLength=1.0f;
    private const float ScaleAxisRadius=0.075f;
    private const float ScaleCubeHalfExtent=0.075f;
    private const float ScaleUniformHalfExtent=0.11f;

    internal static GizmoHandleId?TryHitRotate(
        in MoveGizmoFrame Frame,Vector3 RayOrigin,Vector3 RayDirection,Vector3 CameraForward,
        out float BestRayDistance,ICollection<GizmoHandleId>?Disabled=null){
        MoveGizmoFrame Value=Frame;
        RayDirection=Vector3.Normalize(RayDirection);
        CameraForward=NormalizeOr(CameraForward,Vector3.UnitZ);
        GizmoHandleId?Best=null;
        float NearestRayDistance=float.PositiveInfinity;
        TryRing(GizmoHandleId.RotateX,Value.AxisX,RotateRingRadius);
        TryRing(GizmoHandleId.RotateY,Value.AxisY,RotateRingRadius);
        TryRing(GizmoHandleId.RotateZ,Value.AxisZ,RotateRingRadius);
        TryRing(GizmoHandleId.RotateView,CameraForward,RotateViewRingRadius);
        BestRayDistance=NearestRayDistance;
        return Best;

        void TryRing(GizmoHandleId Handle,Vector3 Normal,float Radius){
            if(Disabled?.Contains(Handle)==true
                ||!TryIntersectPlane(RayOrigin,RayDirection,Value.Pivot,Normal,out Vector3 Point,out float RayDistance)){
                return;
            }

            float RingDistance=Math.Abs(Vector3.Distance(Point,Value.Pivot)-(Radius*Value.WorldScale));
            if(RingDistance<=RotateRingThickness*Value.WorldScale&&RayDistance<NearestRayDistance){
                Best=Handle;
                NearestRayDistance=RayDistance;
            }
        }
    }

    internal static IReadOnlyList<GizmoOverlayPrimitive>BuildRotateOverlay(
        in MoveGizmoFrame Frame,Vector3 CameraForward,GizmoHandleId?Hovered,GizmoHandleId?Active,
        ICollection<GizmoHandleId>?Disabled=null){
        MoveGizmoFrame Value=Frame;
        List<GizmoOverlayPrimitive>Primitives=[];
        AddRing(GizmoHandleId.RotateX,Value.AxisX,RotateRingRadius,new Vector4(1f,0.15f,0.1f,1f));
        AddRing(GizmoHandleId.RotateY,Value.AxisY,RotateRingRadius,new Vector4(0.2f,1f,0.2f,1f));
        AddRing(GizmoHandleId.RotateZ,Value.AxisZ,RotateRingRadius,new Vector4(0.2f,0.45f,1f,1f));
        AddRing(GizmoHandleId.RotateView,NormalizeOr(CameraForward,Vector3.UnitZ),RotateViewRingRadius,Vector4.One);
        return Primitives;

        Vector4 ResolveColor(GizmoHandleId Handle,Vector4 Base)=>
            Disabled?.Contains(Handle)==true
                ?new Vector4(Base.X*0.35f,Base.Y*0.35f,Base.Z*0.35f,0.2f)
                :Active==Handle?Vector4.One:Hovered==Handle?Vector4.Lerp(Base,Vector4.One,0.55f):Base;

        // M0035: 細い線ループだけでなく、内外半径を持つ平帯(annulus)のTriangleも生成し「面積」を
        // 持たせる。帯の半幅は当たり判定の許容量RotateRingThicknessをそのまま流用し、見えている
        // 太さと実際に掴める太さを一致させる。
        void AddRing(GizmoHandleId Handle,Vector3 Normal,float Radius,Vector4 Base){
            const int Segments=48;
            (Vector3 First,Vector3 Second)=CreatePlaneBasis(Normal);
            Vector4 Color=ResolveColor(Handle,Base);
            float InnerRadius=(Radius-RotateRingThickness)*Value.WorldScale;
            float OuterRadius=(Radius+RotateRingThickness)*Value.WorldScale;
            List<GizmoOverlayVertex>Outline=[];
            List<GizmoOverlayVertex>Band=[];
            for(var Segment=0;Segment<Segments;Segment++){
                float AngleA=Segment*FloatMath.Pi*2f/Segments;
                float AngleB=(Segment+1)*FloatMath.Pi*2f/Segments;
                Vector3 DirA=(First*FloatMath.Cos(AngleA))+(Second*FloatMath.Sin(AngleA));
                Vector3 DirB=(First*FloatMath.Cos(AngleB))+(Second*FloatMath.Sin(AngleB));
                Outline.Add(new GizmoOverlayVertex(Value.Pivot+(DirA*Radius*Value.WorldScale),Color));
                Outline.Add(new GizmoOverlayVertex(Value.Pivot+(DirB*Radius*Value.WorldScale),Color));

                Vector3 InnerA=Value.Pivot+(DirA*InnerRadius);
                Vector3 OuterA=Value.Pivot+(DirA*OuterRadius);
                Vector3 InnerB=Value.Pivot+(DirB*InnerRadius);
                Vector3 OuterB=Value.Pivot+(DirB*OuterRadius);
                Band.Add(new GizmoOverlayVertex(InnerA,Color));
                Band.Add(new GizmoOverlayVertex(OuterA,Color));
                Band.Add(new GizmoOverlayVertex(OuterB,Color));
                Band.Add(new GizmoOverlayVertex(InnerA,Color));
                Band.Add(new GizmoOverlayVertex(OuterB,Color));
                Band.Add(new GizmoOverlayVertex(InnerB,Color));
            }
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Outline));
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,Band));
        }
    }

    internal static GizmoHandleId?TryHitScale(
        in MoveGizmoFrame Frame,Vector3 RayOrigin,Vector3 RayDirection,
        out float BestRayDistance,ICollection<GizmoHandleId>?Disabled=null){
        MoveGizmoFrame Value=Frame;
        RayDirection=Vector3.Normalize(RayDirection);
        float PivotRayDistance=Math.Max(0f,Vector3.Dot(Frame.Pivot-RayOrigin,RayDirection));
        float PivotDistance=Vector3.Distance(RayOrigin+(RayDirection*PivotRayDistance),Frame.Pivot);
        if(Disabled?.Contains(GizmoHandleId.ScaleUniform)!=true
            &&PivotDistance<=ScaleUniformHalfExtent*Frame.WorldScale){
            BestRayDistance=PivotRayDistance;
            return GizmoHandleId.ScaleUniform;
        }

        GizmoHandleId?Best=null;
        float NearestRayDistance=float.PositiveInfinity;
        TryAxis(GizmoHandleId.ScaleX,Value.AxisX);
        TryAxis(GizmoHandleId.ScaleY,Value.AxisY);
        TryAxis(GizmoHandleId.ScaleZ,Value.AxisZ);
        BestRayDistance=NearestRayDistance;
        return Best;

        void TryAxis(GizmoHandleId Handle,Vector3 Axis){
            if(Disabled?.Contains(Handle)==true){
                return;
            }
            Vector3 Start=Value.Pivot+(Axis*(ScaleAxisStart*Value.WorldScale));
            Vector3 End=Value.Pivot+(Axis*(ScaleAxisLength*Value.WorldScale));
            ClosestRaySegment(RayOrigin,RayDirection,Start,End,out float RayDistance,out float Distance);
            if(Distance<=ScaleAxisRadius*Value.WorldScale&&RayDistance>=0f&&RayDistance<NearestRayDistance){
                Best=Handle;
                NearestRayDistance=RayDistance;
            }
        }
    }

    internal static IReadOnlyList<GizmoOverlayPrimitive>BuildScaleOverlay(
        in MoveGizmoFrame Frame,GizmoHandleId?Hovered,GizmoHandleId?Active,
        ICollection<GizmoHandleId>?Disabled=null){
        MoveGizmoFrame Value=Frame;
        List<GizmoOverlayPrimitive>Primitives=[];
        AddAxis(GizmoHandleId.ScaleX,Value.AxisX,new Vector4(1f,0.15f,0.1f,1f));
        AddAxis(GizmoHandleId.ScaleY,Value.AxisY,new Vector4(0.2f,1f,0.2f,1f));
        AddAxis(GizmoHandleId.ScaleZ,Value.AxisZ,new Vector4(0.2f,0.45f,1f,1f));
        // M0035: 基準色を濃いグレーにする。ResolveColorのhover lerpは白へ向かうため、これだけで
        // 「既定=濃いグレー、hover=明るいグレー」になる(以前はBase自体が白でlerpが無効化されていた)。
        AddCube(GizmoHandleId.ScaleUniform,Value.Pivot,ScaleUniformHalfExtent,new Vector4(0.35f,0.35f,0.35f,1f));
        if(Hovered==GizmoHandleId.ScaleUniform||Active==GizmoHandleId.ScaleUniform){
            AddOutlineCube(Value.Pivot,ScaleUniformHalfExtent);
        }
        return Primitives;

        Vector4 ResolveColor(GizmoHandleId Handle,Vector4 Base)=>
            Disabled?.Contains(Handle)==true
                ?new Vector4(Base.X*0.35f,Base.Y*0.35f,Base.Z*0.35f,0.2f)
                :Active==Handle?Vector4.One:Hovered==Handle?Vector4.Lerp(Base,Vector4.One,0.55f):Base;

        void AddAxis(GizmoHandleId Handle,Vector3 Axis,Vector4 Base){
            Vector4 Color=ResolveColor(Handle,Base);
            Vector3 End=Value.Pivot+(Axis*(ScaleAxisLength*Value.WorldScale));
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(Value.Pivot,Color),new GizmoOverlayVertex(End,Color),
            ]));

            const int Segments=8;
            const float ShaftRadius=0.025f;
            Vector3 Reference=Math.Abs(Vector3.Dot(Axis,Vector3.UnitY))<0.9f
                ?Vector3.UnitY
                :Vector3.UnitX;
            Vector3 SideA=Vector3.Normalize(Vector3.Cross(Axis,Reference));
            Vector3 SideB=Vector3.Normalize(Vector3.Cross(Axis,SideA));
            Vector3 ShaftStart=Value.Pivot+(Axis*(ScaleAxisStart*Value.WorldScale));
            Vector3 ShaftFinish=Value.Pivot+(Axis*((ScaleAxisLength-ScaleCubeHalfExtent)*Value.WorldScale));
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
                Solid.Add(new GizmoOverlayVertex(StartA,Color));
                Solid.Add(new GizmoOverlayVertex(StartB,Color));
                Solid.Add(new GizmoOverlayVertex(FinishB,Color));
                Solid.Add(new GizmoOverlayVertex(StartA,Color));
                Solid.Add(new GizmoOverlayVertex(FinishB,Color));
                Solid.Add(new GizmoOverlayVertex(FinishA,Color));
            }
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,Solid));
            AddCube(Handle,End,ScaleCubeHalfExtent,Base);
        }

        void AddCube(GizmoHandleId Handle,Vector3 Center,float HalfExtent,Vector4 Base){
            Vector4 Color=ResolveColor(Handle,Base);
            float Size=HalfExtent*Value.WorldScale;
            Vector3 X=Value.AxisX*Size;
            Vector3 Y=Value.AxisY*Size;
            Vector3 Z=Value.AxisZ*Size;
            Vector3[]Corners=[
                Center-X-Y-Z,Center+X-Y-Z,Center+X+Y-Z,Center-X+Y-Z,
                Center-X-Y+Z,Center+X-Y+Z,Center+X+Y+Z,Center-X+Y+Z,
            ];
            int[]Indices=[
                0,2,1,0,3,2,4,5,6,4,6,7,
                0,1,5,0,5,4,3,7,6,3,6,2,
                0,4,7,0,7,3,1,2,6,1,6,5,
            ];
            List<GizmoOverlayVertex>Vertices=[];
            foreach(int Index in Indices){ Vertices.Add(new GizmoOverlayVertex(Corners[Index],Color)); }
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,Vertices));
        }

        // M0035: hover中のScaleUniformキューブへ、白→黒の二重ワイヤーフレーム輪郭を重ねる。
        // 明るい背景では黒線が、暗い背景では白線が効くため、どんな背景色でも視認できる。
        void AddOutlineCube(Vector3 Center,float HalfExtent){
            AddWireCube(Center,HalfExtent*1.12f,new Vector4(1f,1f,1f,1f));
            AddWireCube(Center,HalfExtent*1.28f,new Vector4(0f,0f,0f,1f));
        }

        void AddWireCube(Vector3 Center,float HalfExtent,Vector4 Color){
            float Size=HalfExtent*Value.WorldScale;
            Vector3 X=Value.AxisX*Size;
            Vector3 Y=Value.AxisY*Size;
            Vector3 Z=Value.AxisZ*Size;
            Vector3[]Corners=[
                Center-X-Y-Z,Center+X-Y-Z,Center+X+Y-Z,Center-X+Y-Z,
                Center-X-Y+Z,Center+X-Y+Z,Center+X+Y+Z,Center-X+Y+Z,
            ];
            int[]Edges=[0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7];
            List<GizmoOverlayVertex>Vertices=[];
            foreach(int Index in Edges){ Vertices.Add(new GizmoOverlayVertex(Corners[Index],Color)); }
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Vertices));
        }
    }

    /// <summary>M0034: <c>SurfaceSnapGizmo</c>が武装表示・ヒットテストで再利用するため公開。</summary>
    internal static(Vector3 First,Vector3 Second)CreatePlaneBasis(Vector3 Normal){
        Normal=NormalizeOr(Normal,Vector3.UnitZ);
        Vector3 Reference=Math.Abs(Vector3.Dot(Normal,Vector3.UnitY))<0.9f?Vector3.UnitY:Vector3.UnitX;
        Vector3 First=Vector3.Normalize(Vector3.Cross(Normal,Reference));
        return(First,Vector3.Normalize(Vector3.Cross(Normal,First)));
    }

    private static Vector3 NormalizeOr(Vector3 Value,Vector3 Fallback)=>
        Value.LengthSquared()>1e-10f?Vector3.Normalize(Value):Fallback;
}

internal sealed class RotateGizmoDrag {
    private readonly MoveGizmoFrame Frame;
    private readonly Vector3 Normal;
    private readonly Vector3 StartDirection;

    private RotateGizmoDrag(in MoveGizmoFrame Frame,Vector3 Normal,Vector3 StartDirection){
        this.Frame=Frame;
        this.Normal=Normal;
        this.StartDirection=StartDirection;
    }

    /// <summary>M0037: <see cref="GizmoDragHud"/>が扇形プレビューを描くために公開。</summary>
    internal Vector3 RingNormal=>this.Normal;

    internal Vector3 StartDirectionVector=>this.StartDirection;

    internal static bool TryBegin(
        in MoveGizmoFrame Frame,GizmoHandleId Handle,Vector3 RayOrigin,Vector3 RayDirection,
        Vector3 CameraForward,out RotateGizmoDrag?Drag){
        Vector3 Normal=Handle switch {
            GizmoHandleId.RotateX=>Frame.AxisX,
            GizmoHandleId.RotateY=>Frame.AxisY,
            GizmoHandleId.RotateZ=>Frame.AxisZ,
            GizmoHandleId.RotateView=>CameraForward,
            _=>Vector3.Zero,
        };
        if(Normal.LengthSquared()<=1e-10f
            ||!TransformGizmo.TryIntersectPlane(
                RayOrigin,Vector3.Normalize(RayDirection),Frame.Pivot,Vector3.Normalize(Normal),out Vector3 Point,out _)
            ||(Point-Frame.Pivot).LengthSquared()<=1e-10f){
            Drag=null;
            return false;
        }

        Normal=Vector3.Normalize(Normal);
        Drag=new RotateGizmoDrag(Frame,Normal,Vector3.Normalize(Point-Frame.Pivot));
        return true;
    }

    internal bool TryUpdate(
        Vector3 RayOrigin,Vector3 RayDirection,out float Radians,out Quaternion Delta){
        if(!TransformGizmo.TryIntersectPlane(
            RayOrigin,Vector3.Normalize(RayDirection), this.Frame.Pivot, this.Normal,out Vector3 Point,out _)
            ||(Point- this.Frame.Pivot).LengthSquared()<=1e-10f){
            Radians=0f;
            Delta=Quaternion.Identity;
            return false;
        }

        Vector3 Current=Vector3.Normalize(Point- this.Frame.Pivot);
        Radians=FloatMath.Atan2(
            Vector3.Dot(this.Normal,Vector3.Cross(this.StartDirection,Current)),
            Math.Max(-1f,Math.Min(1f,Vector3.Dot(this.StartDirection,Current))));
        Delta=Quaternion.CreateFromAxisAngle(this.Normal,Radians);
        return true;
    }

    internal Quaternion CreateDelta(float Radians)=>Quaternion.CreateFromAxisAngle(this.Normal,Radians);
}

internal sealed class ScaleGizmoDrag {
    private readonly MoveGizmoFrame Frame;
    private readonly GizmoHandleId Handle;
    private readonly Vector3 ConstraintNormal;
    private readonly float StartMeasure;

    private ScaleGizmoDrag(
        in MoveGizmoFrame Frame,GizmoHandleId Handle,Vector3 ConstraintNormal,float StartMeasure){
        this.Frame=Frame;
        this.Handle=Handle;
        this.ConstraintNormal=ConstraintNormal;
        this.StartMeasure=StartMeasure;
    }

    internal static bool TryBegin(
        in MoveGizmoFrame Frame,GizmoHandleId Handle,Vector3 RayOrigin,Vector3 RayDirection,
        Vector3 CameraForward,out ScaleGizmoDrag?Drag){
        bool Uniform=Handle==GizmoHandleId.ScaleUniform;
        Vector3 Axis=GetAxis(Frame,Handle);
        Vector3 Normal=Uniform?CameraForward:CameraForward-(Axis*Vector3.Dot(CameraForward,Axis));
        if(Normal.LengthSquared()<=1e-8f){
            Vector3 Fallback=Math.Abs(Vector3.Dot(Axis,Vector3.UnitY))<0.9f?Vector3.UnitY:Vector3.UnitX;
            Normal=Fallback-(Axis*Vector3.Dot(Fallback,Axis));
        }
        Normal=Vector3.Normalize(Normal);
        if(!TransformGizmo.TryIntersectPlane(
            RayOrigin,Vector3.Normalize(RayDirection),Frame.Pivot,Normal,out Vector3 Point,out _)){
            Drag=null;
            return false;
        }

        float Measure=Uniform?(Point-Frame.Pivot).Length():Vector3.Dot(Point-Frame.Pivot,Axis);
        float Minimum=Frame.WorldScale*0.05f;
        if(Math.Abs(Measure)<Minimum){ Measure=Minimum; }
        Drag=new ScaleGizmoDrag(Frame,Handle,Normal,Measure);
        return true;
    }

    internal bool TryUpdate(Vector3 RayOrigin,Vector3 RayDirection,out Vector3 Factors){
        if(!TransformGizmo.TryIntersectPlane(
            RayOrigin,Vector3.Normalize(RayDirection), this.Frame.Pivot, this.ConstraintNormal,out Vector3 Point,out _)){
            Factors=Vector3.One;
            return false;
        }

        bool Uniform= this.Handle==GizmoHandleId.ScaleUniform;
        Vector3 Axis=GetAxis(this.Frame, this.Handle);
        float Current=Uniform?(Point- this.Frame.Pivot).Length():Vector3.Dot(Point- this.Frame.Pivot,Axis);
        float Factor=Math.Max(0.01f,Math.Min(100f,Current/ this.StartMeasure));
        Factors= this.Handle switch {
            GizmoHandleId.ScaleX=>new Vector3(Factor,1f,1f),
            GizmoHandleId.ScaleY=>new Vector3(1f,Factor,1f),
            GizmoHandleId.ScaleZ=>new Vector3(1f,1f,Factor),
            GizmoHandleId.ScaleUniform=>new Vector3(Factor),
            _=>Vector3.One,
        };
        return true;
    }

    private static Vector3 GetAxis(in MoveGizmoFrame Frame,GizmoHandleId Handle)=>Handle switch {
        GizmoHandleId.ScaleX=>Frame.AxisX,
        GizmoHandleId.ScaleY=>Frame.AxisY,
        GizmoHandleId.ScaleZ=>Frame.AxisZ,
        _=>Vector3.Zero,
    };
}

internal static class TransformSnap {
    internal const float MoveIncrement=1f;
    internal const float RotationIncrementRadians=15f*FloatMath.Pi/180f;
    internal const float ScaleIncrement=0.1f;

    internal static Vector3 SnapTranslation(
        Vector3 Delta,in MoveGizmoFrame Frame,GizmoHandleId Handle){
        float X=Snap(Vector3.Dot(Delta,Frame.AxisX),MoveIncrement);
        float Y=Snap(Vector3.Dot(Delta,Frame.AxisY),MoveIncrement);
        float Z=Snap(Vector3.Dot(Delta,Frame.AxisZ),MoveIncrement);
        return Handle switch {
            GizmoHandleId.AxisX=>Frame.AxisX*X,
            GizmoHandleId.AxisY=>Frame.AxisY*Y,
            GizmoHandleId.AxisZ=>Frame.AxisZ*Z,
            GizmoHandleId.PlaneXY=>(Frame.AxisX*X)+(Frame.AxisY*Y),
            GizmoHandleId.PlaneYZ=>(Frame.AxisY*Y)+(Frame.AxisZ*Z),
            GizmoHandleId.PlaneZX=>(Frame.AxisZ*Z)+(Frame.AxisX*X),
            _=>Delta,
        };
    }

    internal static float SnapRotationRadians(float Radians)=>Snap(Radians,RotationIncrementRadians);

    internal static Vector3 SnapScaleFactors(Vector3 Factors,GizmoHandleId Handle)=>Handle switch {
        GizmoHandleId.ScaleX=>new Vector3(Math.Max(ScaleIncrement,Snap(Factors.X,ScaleIncrement)),1f,1f),
        GizmoHandleId.ScaleY=>new Vector3(1f,Math.Max(ScaleIncrement,Snap(Factors.Y,ScaleIncrement)),1f),
        GizmoHandleId.ScaleZ=>new Vector3(1f,1f,Math.Max(ScaleIncrement,Snap(Factors.Z,ScaleIncrement))),
        GizmoHandleId.ScaleUniform=>new Vector3(Math.Max(ScaleIncrement,Snap(Factors.X,ScaleIncrement))),
        _=>Factors,
    };

    private static float Snap(float Value,float Increment)=>FloatMath.Round(Value/Increment,MidpointRounding.AwayFromZero)*Increment;
}

/// <summary>
/// M0033: ギズモ上のLocal/Global(World)切り替えアイコン。ピボットには既にScaleUniformの
/// キューブがあるため、カメラのRight/Up方向へ固定オフセットした場所(全ハンドルの外側)に置く
/// (RotateViewリングのカメラ相対法線と同じ考え方)。ドラッグは生成せず、クリックで即座に
/// 空間を切り替えるトグルとして扱われる(呼び出し側は<c>SceneHwndHost.BeginGizmoDrag</c>参照)。
/// </summary>
internal static class TransformSpaceToggleGizmo {
    private const float BadgeDistance=1.65f;
    private const float BadgeHalfExtent=0.09f;
    private const float BadgeHitRadius=0.18f;

    internal static Vector3 ComputePosition(in MoveGizmoFrame Frame,Vector3 CameraRight,Vector3 CameraUp){
        Vector3 Direction=Vector3.Normalize(CameraRight-CameraUp);
        return Frame.Pivot+(Direction*(BadgeDistance*Frame.WorldScale));
    }

    internal static GizmoHandleId?TryHit(
        in MoveGizmoFrame Frame,Vector3 CameraRight,Vector3 CameraUp,
        Vector3 RayOrigin,Vector3 RayDirection,out float RayDistance){
        Vector3 Position=ComputePosition(Frame,CameraRight,CameraUp);
        RayDirection=Vector3.Normalize(RayDirection);
        float ForwardDistance=Math.Max(0f,Vector3.Dot(Position-RayOrigin,RayDirection));
        Vector3 Closest=RayOrigin+(RayDirection*ForwardDistance);
        if(Vector3.Distance(Closest,Position)<=BadgeHitRadius*Frame.WorldScale){
            RayDistance=ForwardDistance;
            return GizmoHandleId.TransformSpaceToggle;
        }

        RayDistance=float.PositiveInfinity;
        return null;
    }

    internal static IReadOnlyList<GizmoOverlayPrimitive>BuildOverlay(
        in MoveGizmoFrame Frame,Vector3 CameraRight,Vector3 CameraUp,
        SceneTransformSpace Space,GizmoHandleId?Hovered){
        Vector3 Position=ComputePosition(Frame,CameraRight,CameraUp);
        Vector4 Color=Hovered==GizmoHandleId.TransformSpaceToggle
            ?Vector4.One
            :new Vector4(0.85f,0.85f,0.9f,1f);
        float Size=BadgeHalfExtent*Frame.WorldScale;
        List<GizmoOverlayVertex>Vertices=[];
        if(Space==SceneTransformSpace.Local){
            Vector3 X=Frame.AxisX*Size;
            Vector3 Y=Frame.AxisY*Size;
            Vector3 Z=Frame.AxisZ*Size;
            Vector3[]Corners=[
                Position-X-Y-Z,Position+X-Y-Z,Position+X+Y-Z,Position-X+Y-Z,
                Position-X-Y+Z,Position+X-Y+Z,Position+X+Y+Z,Position-X+Y+Z,
            ];
            int[]Edges=[0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7];
            foreach(int Index in Edges){ Vertices.Add(new GizmoOverlayVertex(Corners[Index],Color)); }
        } else {
            const int Segments=16;
            for(var Segment=0;Segment<Segments;Segment++){
                float AngleA=Segment*FloatMath.Pi*2f/Segments;
                float AngleB=(Segment+1)*FloatMath.Pi*2f/Segments;
                Vertices.Add(new GizmoOverlayVertex(
                    Position+(((CameraRight*FloatMath.Cos(AngleA))+(CameraUp*FloatMath.Sin(AngleA)))*Size),Color));
                Vertices.Add(new GizmoOverlayVertex(
                    Position+(((CameraRight*FloatMath.Cos(AngleB))+(CameraUp*FloatMath.Sin(AngleB)))*Size),Color));
            }
        }

        return [new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Vertices)];
    }
}
