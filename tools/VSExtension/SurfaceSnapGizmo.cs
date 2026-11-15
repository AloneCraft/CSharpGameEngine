using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0034: Shift+Ctrl(<c>SceneHwndHost.SurfaceSnapArmed</c>)を押している間だけ現れる、ピボット中心の
/// 平面四角ハンドル。ドラッグするとカーソル直下の別オブジェクト表面へPosition+Rotationを吸着させる
/// (数学自体は<see cref="EditTransactionController.PreviewSurfaceSnap"/>/<see cref="ScenePicker.TrySurfaceSnapQuery"/>
/// に既存)。武装中(未ドラッグ)はカメラ正対、ドラッグ中はヒットした面の法線へ向きを合わせる —
/// どちらの向きで表示するかは呼び出し側(<see cref="SceneHwndHost"/>)が<see cref="BuildOverlay"/>へ
/// 渡すPosition/Normalで決める。
/// </summary>
internal static class SurfaceSnapGizmo {
    private const float QuadHalfExtent=0.18f;
    private const float HitHalfExtent=0.20f;

    /// <summary>武装中(まだドラッグしていない)の表示・ヒット判定に使う位置と向き。</summary>
    internal static(Vector3 Position,Vector3 Normal)GetArmedPlacement(in MoveGizmoFrame Frame,Vector3 CameraForward)=>
        (Frame.Pivot,NormalizeOrFallback(CameraForward));

    internal static GizmoHandleId?TryHit(
        in MoveGizmoFrame Frame,Vector3 CameraForward,Vector3 RayOrigin,Vector3 RayDirection,out float RayDistance){
        (var Position,var Normal)=GetArmedPlacement(Frame,CameraForward);
        RayDirection=Vector3.Normalize(RayDirection);
        if(!TransformGizmo.TryIntersectPlane(RayOrigin,RayDirection,Position,Normal,out var Point,out RayDistance)){
            return null;
        }

        (var First,var Second)=TransformGizmo.CreatePlaneBasis(Normal);
        var Relative=(Point-Position)/Frame.WorldScale;
        var U=Vector3.Dot(Relative,First);
        var V=Vector3.Dot(Relative,Second);
        if(System.Math.Abs(U)<=HitHalfExtent&&System.Math.Abs(V)<=HitHalfExtent){
            return GizmoHandleId.SurfaceSnapPlane;
        }

        RayDistance=float.PositiveInfinity;
        return null;
    }

    /// <summary>
    /// <paramref name="Disabled"/>(対象ノードのRotationが式等でSurfaceSnapPlaneが編集不可)のときは
    /// 褪色させる — Shift+Ctrlを押しても掴めない理由を視覚的に示すため。輪郭色はfillのRGBを
    /// そのまま反転(<c>1-x</c>)する。overlayはunlitな頂点色パイプラインでフレームバッファを
    /// 読めないため、真の「背景色の反転」は不可能で、fill基準の反転が唯一自己完結する解釈になる。
    /// </summary>
    internal static IReadOnlyList<GizmoOverlayPrimitive>BuildOverlay(
        in MoveGizmoFrame Frame,Vector3 Position,Vector3 Normal,bool Disabled){
        (var First,var Second)=TransformGizmo.CreatePlaneBasis(NormalizeOrFallback(Normal));
        var Size=QuadHalfExtent*Frame.WorldScale;
        var A=Position-(First*Size)-(Second*Size);
        var B=Position+(First*Size)-(Second*Size);
        var C=Position+(First*Size)+(Second*Size);
        var D=Position-(First*Size)+(Second*Size);

        var Fill=Disabled?new Vector4(0.5f,0.35f,0.05f,0.18f):new Vector4(1f,0.62f,0.1f,0.35f);
        Vector4 Outline=new(1f-Fill.X,1f-Fill.Y,1f-Fill.Z,Disabled?0.3f:0.95f);

        return [
            new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,[
                new GizmoOverlayVertex(A,Fill),new GizmoOverlayVertex(B,Fill),new GizmoOverlayVertex(C,Fill),
                new GizmoOverlayVertex(A,Fill),new GizmoOverlayVertex(C,Fill),new GizmoOverlayVertex(D,Fill),
            ]),
            new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(A,Outline),new GizmoOverlayVertex(B,Outline),
                new GizmoOverlayVertex(B,Outline),new GizmoOverlayVertex(C,Outline),
                new GizmoOverlayVertex(C,Outline),new GizmoOverlayVertex(D,Outline),
                new GizmoOverlayVertex(D,Outline),new GizmoOverlayVertex(A,Outline),
            ]),
        ];
    }

    private static Vector3 NormalizeOrFallback(Vector3 Value)=>
        Value.LengthSquared()>1e-10f?Vector3.Normalize(Value):Vector3.UnitZ;
}
