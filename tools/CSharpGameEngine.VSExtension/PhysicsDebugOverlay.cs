using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

internal readonly record struct PhysicsDebugOverlayOptions(bool Bounds,bool Velocity,bool CenterOfMass,bool Sleep);

internal static class PhysicsDebugOverlay {
    internal const int MaximumBodies=512;

    internal static IReadOnlyList<GizmoOverlayPrimitive> Build(ScenePhysicsDebugSnapshot Snapshot,in PhysicsDebugOverlayOptions Options){
        List<GizmoOverlayPrimitive> Result=[];
        int Count=0;
        foreach(ScenePhysicsDebugBody Body in Snapshot.Bodies){
            if(Count++>=MaximumBodies){ break; }
            Vector4 Color=Options.Sleep&&Body.IsSleeping?new Vector4(0.25f,0.55f,1f,0.9f):new Vector4(0.2f,1f,0.35f,0.9f);
            if(Options.Bounds){ AddBox(Result,Body.Min,Body.Max,Color); }
            if(Options.Velocity&&Body.Velocity.LengthSquared()>1e-10f){
                Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[new GizmoOverlayVertex(Body.Position,new Vector4(1f,0.25f,0.1f,1f)),new GizmoOverlayVertex(Body.Position+Body.Velocity,new Vector4(1f,0.9f,0.1f,1f))]));
            }
            if(Options.CenterOfMass){ AddCross(Result,Body.Position,Color); }
        }
        return Result;
    }

    private static void AddBox(List<GizmoOverlayPrimitive> Result,Vector3 Min,Vector3 Max,Vector4 Color){
        Vector3[] C=[new(Min.X,Min.Y,Min.Z),new(Max.X,Min.Y,Min.Z),new(Max.X,Max.Y,Min.Z),new(Min.X,Max.Y,Min.Z),new(Min.X,Min.Y,Max.Z),new(Max.X,Min.Y,Max.Z),new(Max.X,Max.Y,Max.Z),new(Min.X,Max.Y,Max.Z)];
        int[] E=[0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7];
        List<GizmoOverlayVertex> Vertices=[];foreach(int Index in E){ Vertices.Add(new GizmoOverlayVertex(C[Index],Color)); }
        Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Vertices));
    }

    private static void AddCross(List<GizmoOverlayPrimitive> Result,Vector3 P,Vector4 Color){
        const float Radius=0.08f;
        Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
            new GizmoOverlayVertex(P-Vector3.UnitX*Radius,Color),new GizmoOverlayVertex(P+Vector3.UnitX*Radius,Color),
            new GizmoOverlayVertex(P-Vector3.UnitY*Radius,Color),new GizmoOverlayVertex(P+Vector3.UnitY*Radius,Color),
            new GizmoOverlayVertex(P-Vector3.UnitZ*Radius,Color),new GizmoOverlayVertex(P+Vector3.UnitZ*Radius,Color)]));
    }
}
