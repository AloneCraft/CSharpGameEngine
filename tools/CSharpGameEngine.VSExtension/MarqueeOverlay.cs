using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// スクリーン矩形を既存のPhase AオーバーレイPSOが受け取るworld-spaceプリミティブへ変換する。
/// 各角をnear面から同一レイ上へ僅かに進めるため、Perspective/Orthographicのどちらでも
/// 指定ピクセルへ投影され、専用PSOやGPU resourceを追加しない。
/// </summary>
internal static class MarqueeOverlay {
    private static readonly Vector4 FillColor=new(0.18f,0.52f,1f,0.16f);
    private static readonly Vector4 OutlineColor=new(0.35f,0.72f,1f,0.95f);

    internal static IReadOnlyList<GizmoOverlayPrimitive>Build(
        ScreenRectangle Rectangle,Matrix4x4 View,Matrix4x4 Projection,
        float ViewportWidth,float ViewportHeight){
        if(ViewportWidth<=0f||ViewportHeight<=0f){
            return [];
        }

        Vector3 TopLeft=ToWorld(Rectangle.Left,Rectangle.Top,View,Projection,ViewportWidth,ViewportHeight);
        Vector3 TopRight=ToWorld(Rectangle.Right,Rectangle.Top,View,Projection,ViewportWidth,ViewportHeight);
        Vector3 BottomRight=ToWorld(Rectangle.Right,Rectangle.Bottom,View,Projection,ViewportWidth,ViewportHeight);
        Vector3 BottomLeft=ToWorld(Rectangle.Left,Rectangle.Bottom,View,Projection,ViewportWidth,ViewportHeight);

        return [
            new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,[
                new GizmoOverlayVertex(TopLeft,FillColor),
                new GizmoOverlayVertex(TopRight,FillColor),
                new GizmoOverlayVertex(BottomRight,FillColor),
                new GizmoOverlayVertex(TopLeft,FillColor),
                new GizmoOverlayVertex(BottomRight,FillColor),
                new GizmoOverlayVertex(BottomLeft,FillColor),
            ]),
            new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(TopLeft,OutlineColor),
                new GizmoOverlayVertex(TopRight,OutlineColor),
                new GizmoOverlayVertex(TopRight,OutlineColor),
                new GizmoOverlayVertex(BottomRight,OutlineColor),
                new GizmoOverlayVertex(BottomRight,OutlineColor),
                new GizmoOverlayVertex(BottomLeft,OutlineColor),
                new GizmoOverlayVertex(BottomLeft,OutlineColor),
                new GizmoOverlayVertex(TopLeft,OutlineColor),
            ]),
        ];
    }

    private static Vector3 ToWorld(
        float X,float Y,Matrix4x4 View,Matrix4x4 Projection,float ViewportWidth,float ViewportHeight){
        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(
            View,Projection,X,Y,ViewportWidth,ViewportHeight);
        return Origin+(Direction*0.01f);
    }
}
