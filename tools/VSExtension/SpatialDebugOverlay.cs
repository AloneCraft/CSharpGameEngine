using System;
using System.Collections.Generic;
using System.Numerics;
namespace CSharpGameEngine.VSExtension;
internal static class SpatialDebugOverlay {
    internal const int MaxNodes=512;
    internal static IReadOnlyList<GizmoOverlayPrimitive>Build(SceneSpatialDebugSnapshot Snapshot,in Matrix4x4 ViewProjection){
        List<GizmoOverlayPrimitive>Result=[];
        SceneSpatialDebugNode?Root=null;
        foreach(SceneSpatialDebugNode Node in Snapshot.Nodes){
            if(Node.IsRoot){
                Root=Node;
                break;
            }
        }
        if(Root is { }RootNode){
            AddNode(Result,RootNode,ViewProjection);
        }
        List<SceneSpatialDebugNode>Candidates=[];
        foreach(SceneSpatialDebugNode Node in Snapshot.Nodes){
            if(Node.IsRoot||!IntersectsClip(Node,ViewProjection)){
                continue;
            }
            Candidates.Add(Node);
        }
        Candidates.Sort(static (Left,Right)=>Left.Depth!=Right.Depth?Left.Depth.CompareTo(Right.Depth):Right.EntryCount.CompareTo(Left.EntryCount));
        int Remaining=MaxNodes-Result.Count;
        for(int Index=0;Index<Candidates.Count&&Index<Remaining;Index++){
            AddNode(Result,Candidates[Index],ViewProjection);
        }
        return Result;
    }
    private static void AddNode(List<GizmoOverlayPrimitive>Result,SceneSpatialDebugNode Node,in Matrix4x4 ViewProjection){
        Vector3 Min=Node.Min;
        Vector3 Max=Node.Max;
        Vector3[]Corners=[
            new Vector3(Min.X,Min.Y,Min.Z),new Vector3(Max.X,Min.Y,Min.Z),new Vector3(Max.X,Max.Y,Min.Z),new Vector3(Min.X,Max.Y,Min.Z),
            new Vector3(Min.X,Min.Y,Max.Z),new Vector3(Max.X,Min.Y,Max.Z),new Vector3(Max.X,Max.Y,Max.Z),new Vector3(Min.X,Max.Y,Max.Z),
        ];
        int[]Edges=[0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7];
        float Hue=(Node.Depth%8)/8f;
        Vector4 Color=Node.IsRoot?new Vector4(1f,0.85f,0.1f,1f):new Vector4(0.2f+Hue,0.55f,1f-Hue*0.6f,0.85f);
        List<GizmoOverlayVertex>Vertices=[];
        foreach(int Edge in Edges){
            Vertices.Add(new GizmoOverlayVertex(Corners[Edge],Color));
        }
        Result.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Vertices));
    }
    private static bool IntersectsClip(SceneSpatialDebugNode Node,in Matrix4x4 ViewProjection){
        Vector3 Min=Node.Min;
        Vector3 Max=Node.Max;
        Vector3[]Corners=[
            new Vector3(Min.X,Min.Y,Min.Z),new Vector3(Max.X,Min.Y,Min.Z),new Vector3(Max.X,Max.Y,Min.Z),new Vector3(Min.X,Max.Y,Min.Z),
            new Vector3(Min.X,Min.Y,Max.Z),new Vector3(Max.X,Min.Y,Max.Z),new Vector3(Max.X,Max.Y,Max.Z),new Vector3(Min.X,Max.Y,Max.Z),
        ];
        foreach(Vector3 Corner in Corners){
            Vector4 Clip=Vector4.Transform(new Vector4(Corner,1f),ViewProjection);
            if(Math.Abs(Clip.W)>1e-6f&&Clip.X>=-Clip.W&&Clip.X<=Clip.W&&Clip.Y>=-Clip.W&&Clip.Y<=Clip.W&&Clip.Z>=0f&&Clip.Z<=Clip.W){
                return true;
            }
        }
        return false;
    }
}