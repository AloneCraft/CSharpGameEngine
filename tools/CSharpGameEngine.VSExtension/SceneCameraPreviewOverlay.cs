using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>Builds the Scene View card border and index labels as ordinary wire overlay input.</summary>
internal static class SceneCameraPreviewOverlay {
    internal static IReadOnlyList<GizmoOverlayPrimitive>Build(
        IReadOnlyList<RenderPreviewCameraPass>Previews,
        in Matrix4x4 View,in Matrix4x4 Projection,int Width,int Height){
        if(Previews is null){ throw new ArgumentNullException(nameof(Previews)); }
        if(Previews.Count==0||Width<=0||Height<=0
            ||!Matrix4x4.Invert(View*Projection,out Matrix4x4 InverseViewProjection)){
            return [];
        }

        List<GizmoOverlayPrimitive>Result=[];
        for(int Index=0;Index<Previews.Count;Index++){
            RenderPreviewCameraPass Preview=Previews[Index];
            Vector4 Color=Index==0
                ?new Vector4(1f,0.72f,0.1f,1f)
                :new Vector4(0.35f,0.75f,1f,1f);
            Vector3 TopLeft=ScreenToWorld(
                Preview.X,Preview.Y,InverseViewProjection,Width,Height);
            Vector3 TopRight=ScreenToWorld(
                Preview.X+Preview.Width,Preview.Y,InverseViewProjection,Width,Height);
            Vector3 BottomRight=ScreenToWorld(
                Preview.X+Preview.Width,Preview.Y+Preview.Height,
                InverseViewProjection,Width,Height);
            Vector3 BottomLeft=ScreenToWorld(
                Preview.X,Preview.Y+Preview.Height,InverseViewProjection,Width,Height);
            Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new(TopLeft,Color),new(TopRight,Color),
                new(TopRight,Color),new(BottomRight,Color),
                new(BottomRight,Color),new(BottomLeft,Color),
                new(BottomLeft,Color),new(TopLeft,Color),
            ]));
            IReadOnlyList<GizmoOverlayVertex>Label=GizmoDragHud.BuildScreenText(
                (Index+1).ToString(CultureInfo.InvariantCulture),
                Preview.X+6f,Preview.Y+18f,14f,Color,
                InverseViewProjection,Width,Height);
            if(Label.Count>0){
                Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Label));
            }
        }
        return Result;
    }

    private static Vector3 ScreenToWorld(
        float X,float Y,in Matrix4x4 InverseViewProjection,int Width,int Height){
        float NdcX=(X/Width*2f)-1f;
        float NdcY=1f-(Y/Height*2f);
        return Vector3.Transform(
            new Vector3(NdcX,NdcY,0f),InverseViewProjection);
    }
}
