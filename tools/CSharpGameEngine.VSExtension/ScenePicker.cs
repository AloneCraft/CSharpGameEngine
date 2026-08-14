using System;
using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0020 Phase 2 選択用のCPUレイキャスト(ADR-0041 決定事項3)。<see cref="SceneRenderObject"/>は
/// CPU側に<see cref="SimpleMesh"/>(positions/indices)とワールド行列を既に保持しているため、
/// GPUピッキングは使わず全三角形を線形走査する(現行のシーン規模では加速構造は不要 — ADR-0041)。
///
/// <para>
/// VertexSnapQuery/SurfaceSnapQuery(design doc §5の他2クエリ)はM0020 Phase 5(V/Nホールド)の
/// スコープであり、ここにはまだ存在しない。
/// </para>
/// </summary>
internal static class ScenePicker {
    /// <summary>最近ヒットの<see cref="SceneRenderObject.NodeId"/>を返す。表裏どちらの面もヒットする
    /// (design doc §5「三角形交差: Möller–Trumbore」)。</summary>
    internal static bool TryPick(
        IReadOnlyList<SceneRenderObject>RenderObjects,Vector3 RayOrigin,Vector3 RayDirection,out NodePath HitNodePath){
        float BestDistance=float.PositiveInfinity;
        NodePath BestPath=default;
        bool Found=false;

        foreach(SceneRenderObject Object in RenderObjects){
            if(TryIntersectMesh(Object.Mesh,Object.World,RayOrigin,RayDirection,out float Distance)&&Distance<BestDistance){
                BestDistance=Distance;
                BestPath=Object.NodeId;
                Found=true;
            }
        }

        HitNodePath=BestPath;
        return Found;
    }

    private static bool TryIntersectMesh(
        SimpleMesh Mesh,Matrix4x4 World,Vector3 Origin,Vector3 Direction,out float Distance){
        Distance=float.PositiveInfinity;
        bool Found=false;

        for(int I=0;I+2<Mesh.Indices.Length;I+=3){
            Vector3 A=Vector3.Transform(Mesh.Positions[(int)Mesh.Indices[I]],World);
            Vector3 B=Vector3.Transform(Mesh.Positions[(int)Mesh.Indices[I+1]],World);
            Vector3 C=Vector3.Transform(Mesh.Positions[(int)Mesh.Indices[I+2]],World);

            if(TryIntersectTriangle(Origin,Direction,A,B,C,out float T)&&T<Distance){
                Distance=T;
                Found=true;
            }
        }

        return Found;
    }

    // 標準Möller–Trumbore。Det符号でカリングしない(表裏どちらもヒットさせる、design doc §5)。
    private static bool TryIntersectTriangle(
        Vector3 Origin,Vector3 Direction,Vector3 A,Vector3 B,Vector3 C,out float T){
        const float Epsilon=1e-6f;
        T=0f;

        Vector3 EdgeAB=B-A;
        Vector3 EdgeAC=C-A;
        Vector3 PVec=Vector3.Cross(Direction,EdgeAC);
        float Det=Vector3.Dot(EdgeAB,PVec);
        if(Math.Abs(Det)<Epsilon){
            return false; // レイが三角形平面と平行。
        }

        float InvDet=1f/Det;
        Vector3 TVec=Origin-A;
        float U=Vector3.Dot(TVec,PVec)*InvDet;
        if(U<0f||U>1f){
            return false;
        }

        Vector3 QVec=Vector3.Cross(TVec,EdgeAB);
        float V=Vector3.Dot(Direction,QVec)*InvDet;
        if(V<0f||U+V>1f){
            return false;
        }

        float CandidateT=Vector3.Dot(EdgeAC,QVec)*InvDet;
        if(CandidateT<Epsilon){
            return false; // 三角形は視点の後方。
        }

        T=CandidateT;
        return true;
    }
}
