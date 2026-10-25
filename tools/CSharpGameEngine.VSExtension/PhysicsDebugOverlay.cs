using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

internal readonly record struct PhysicsDebugOverlayOptions(
    bool Bounds,bool Velocity,bool CenterOfMass,bool Sleep,bool Contacts,bool Tree,bool Bonds);

internal static class PhysicsDebugOverlay {
    internal const int MaximumBodies=512;
    internal const int MaximumVertices=12288;

    internal static IReadOnlyList<GizmoOverlayPrimitive> Build(
        ScenePhysicsDebugSnapshot?Snapshot,in PhysicsDebugOverlayOptions Options,
        IReadOnlyList<ScenePhysicsFractureBond>?FractureBonds=null){
        List<GizmoOverlayPrimitive> Result=[];
        int VertexCount=0;
        if(Options.Contacts&&Snapshot is not null){
            Vector4 ContactColor=new(1f,0.2f,0.15f,1f);
            Vector4 NormalColor=new(1f,0.9f,0.1f,1f);
            foreach(ScenePhysicsDebugContact Contact in Snapshot.Contacts){
                if(VertexCount+8>MaximumVertices){ break; }
                AddCross(Result,Contact.Position,ContactColor);
                float Length=Contact.Penetration<0.15f?0.15f:Contact.Penetration>1f?1f:Contact.Penetration;
                Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                    new GizmoOverlayVertex(Contact.Position,NormalColor),
                    new GizmoOverlayVertex(Contact.Position+Vector3.Normalize(Contact.Normal)*Length,NormalColor)]));
                VertexCount+=8;
            }
        }
        if(Options.Bonds&&FractureBonds is not null){
            Vector4 BondColor=new(1f,0.35f,0.9f,0.85f);
            // ADR-0078/M0113: the fragments' outlines of the Fracture preview, in a pale orange under the bonds.
            Vector4 OutlineColor=new(1f,0.75f,0.35f,0.7f);
            foreach(ScenePhysicsFractureBond Bond in FractureBonds){
                if(VertexCount+2>MaximumVertices){ break; }
                Vector4 Color=Bond.Outline?OutlineColor:BondColor;
                Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                    new GizmoOverlayVertex(Bond.First,Color),new GizmoOverlayVertex(Bond.Second,Color)]));
                VertexCount+=2;
            }
        }
        if(Options.Tree&&Snapshot is not null){
            foreach(ScenePhysicsDebugTreeNode Node in Snapshot.TreeNodes){
                if(VertexCount+24>MaximumVertices){ break; }
                float DepthShade=0.3f+0.7f*(Node.Depth%8)/7f;
                Vector4 Color=Node.IsLeaf?new Vector4(0.25f,0.8f,1f,0.65f):new Vector4(0.65f,0.25f+0.5f*DepthShade,1f,0.45f);
                AddBox(Result,Node.Min,Node.Max,Color);VertexCount+=24;
            }
        }
        int Count=0;
        foreach(ScenePhysicsDebugBody Body in Snapshot?.Bodies??[]){
            if(Count++>=MaximumBodies){ break; }
            Vector4 Color=Options.Sleep&&Body.IsSleeping?new Vector4(0.25f,0.55f,1f,0.9f):new Vector4(0.2f,1f,0.35f,0.9f);
            if(Options.Bounds&&VertexCount+24<=MaximumVertices){ AddBox(Result,Body.Min,Body.Max,Color);VertexCount+=24; }
            if(Options.Velocity&&Body.Velocity.LengthSquared()>1e-10f&&VertexCount+2<=MaximumVertices){
                Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[new GizmoOverlayVertex(Body.Position,new Vector4(1f,0.25f,0.1f,1f)),new GizmoOverlayVertex(Body.Position+Body.Velocity,new Vector4(1f,0.9f,0.1f,1f))]));
                VertexCount+=2;
            }
            if(Options.CenterOfMass&&VertexCount+6<=MaximumVertices){ AddCross(Result,Body.Position,Color);VertexCount+=6; }
            if(VertexCount>=MaximumVertices){ break; }
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
