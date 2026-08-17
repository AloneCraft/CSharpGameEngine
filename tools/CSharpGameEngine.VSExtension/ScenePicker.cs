using System;
using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ビューポートのクライアント座標で表す正規化済み矩形。ドラッグ方向に関係なく
/// <see cref="Left"/>≦<see cref="Right"/>、<see cref="Top"/>≦<see cref="Bottom"/>となる。
/// </summary>
internal readonly struct ScreenRectangle:IEquatable<ScreenRectangle> {
    private ScreenRectangle(float Left,float Top,float Right,float Bottom){
        this.Left=Left;
        this.Top=Top;
        this.Right=Right;
        this.Bottom=Bottom;
    }

    internal float Left { get; }

    internal float Top { get; }

    internal float Right { get; }

    internal float Bottom { get; }

    internal static ScreenRectangle FromPoints(float StartX,float StartY,float EndX,float EndY)=>
        new(Math.Min(StartX,EndX),Math.Min(StartY,EndY),Math.Max(StartX,EndX),Math.Max(StartY,EndY));

    internal bool Intersects(float OtherLeft,float OtherTop,float OtherRight,float OtherBottom)=>
        this.Left<=OtherRight&&this.Right>=OtherLeft&&this.Top<=OtherBottom&&this.Bottom>=OtherTop;

    public bool Equals(ScreenRectangle Other)=>
        this.Left.Equals(Other.Left)&&this.Top.Equals(Other.Top)
        &&this.Right.Equals(Other.Right)&&this.Bottom.Equals(Other.Bottom);

    public override bool Equals(object?Obj)=>Obj is ScreenRectangle Other&&Equals(Other);

    public override int GetHashCode()=>HashCode.Combine(this.Left,this.Top,this.Right,this.Bottom);

    public static bool operator ==(ScreenRectangle Left,ScreenRectangle Right)=>Left.Equals(Right);

    public static bool operator !=(ScreenRectangle Left,ScreenRectangle Right)=>!Left.Equals(Right);
}

internal readonly struct VertexSnapHit {
    internal VertexSnapHit(NodePath NodePath,Vector3 Position,float ScreenDistance){
        this.NodePath=NodePath;
        this.Position=Position;
        this.ScreenDistance=ScreenDistance;
    }

    internal NodePath NodePath { get; }
    internal Vector3 Position { get; }
    internal float ScreenDistance { get; }
}

internal readonly struct SurfaceSnapHit {
    internal SurfaceSnapHit(NodePath NodePath,Vector3 Position,Vector3 Normal,float RayDistance){
        this.NodePath=NodePath;
        this.Position=Position;
        this.Normal=Normal;
        this.RayDistance=RayDistance;
    }

    internal NodePath NodePath { get; }
    internal Vector3 Position { get; }
    internal Vector3 Normal { get; }
    internal float RayDistance { get; }
}

/// <summary>
/// M0020 Phase 2 選択用のCPUレイキャスト(ADR-0041 決定事項3)。<see cref="SceneRenderObject"/>は
/// CPU側に<see cref="SimpleMesh"/>(positions/indices)とワールド行列を既に保持しているため、
/// GPUピッキングは使わず全三角形を線形走査する(現行のシーン規模では加速構造は不要 — ADR-0041)。
///
/// VertexSnapQuery/SurfaceSnapQueryも同じCPUデータを走査し、ドラッグ対象rootと子孫を除外する。
/// </summary>
internal static class ScenePicker {
    /// <summary>最近ヒットの<see cref="SceneRenderObject.NodeId"/>を返す。表裏どちらの面もヒットする
    /// (design doc §5「三角形交差: Möller–Trumbore」)。</summary>
    internal static bool TryPick(
        IReadOnlyList<SceneRenderObject>RenderObjects,Vector3 RayOrigin,Vector3 RayDirection,out NodePath HitNodePath)=>
        TryPick(RenderObjects,RayOrigin,RayDirection,out HitNodePath,out _);

    /// <summary>
    /// <paramref name="HitDistance"/>付きの多重定義(M0020追加、Orbitのピクセル完全一致ドラッグ用)。
    /// <c>RayDirection</c>は正規化されている前提なので、世界座標のヒット点は
    /// <c>RayOrigin+RayDirection*HitDistance</c>で得られる。既存の交差判定ロジックは無改造 —
    /// 各三角形の交差判定が既に計算していた距離を、これまで捨てていただけである。
    /// </summary>
    internal static bool TryPick(
        IReadOnlyList<SceneRenderObject>RenderObjects,Vector3 RayOrigin,Vector3 RayDirection,
        out NodePath HitNodePath,out float HitDistance){
        float BestDistance=float.PositiveInfinity;
        NodePath BestPath=default;
        bool Found=false;

        foreach(SceneRenderObject Object in RenderObjects){
            if(!Object.IsPickable){ continue; }
            if(TryIntersectMesh(Object.Mesh,Object.World,RayOrigin,RayDirection,out float Distance)&&Distance<BestDistance){
                BestDistance=Distance;
                BestPath=Object.NodeId;
                Found=true;
            }
        }

        HitNodePath=BestPath;
        HitDistance=BestDistance;
        return Found;
    }

    /// <summary>物理ピクセル半径8px以内に投影された最近頂点を返す。距離同値では手前を優先する。</summary>
    internal static bool TryVertexSnapQuery(
        IReadOnlyList<SceneRenderObject>RenderObjects,Matrix4x4 View,Matrix4x4 Projection,
        float CursorX,float CursorY,int ViewportWidth,int ViewportHeight,
        IReadOnlyCollection<NodePath>ExcludedRoots,out VertexSnapHit Hit){
        const float RadiusPixels=8f;
        const float Epsilon=1e-6f;
        Hit=default;
        if(ViewportWidth<=0||ViewportHeight<=0){ return false; }

        bool Found=false;
        float BestScreenDistanceSquared=RadiusPixels*RadiusPixels;
        float BestDepth=float.PositiveInfinity;
        Matrix4x4 ViewProjection=View*Projection;
        foreach(SceneRenderObject Object in RenderObjects){
            if(IsExcluded(Object.NodeId,ExcludedRoots)||!Object.IsPickable){ continue; }
            Matrix4x4 WorldViewProjection=Object.World*ViewProjection;
            foreach(Vector3 LocalPosition in Object.Mesh.Positions){
                Vector4 Clip=Vector4.Transform(new Vector4(LocalPosition,1f),WorldViewProjection);
                if(Clip.W<=Epsilon||Clip.Z<0f||Clip.Z>Clip.W
                    ||!IsFinite(Clip.X)||!IsFinite(Clip.Y)||!IsFinite(Clip.Z)||!IsFinite(Clip.W)){
                    continue;
                }

                float ScreenX=((Clip.X/Clip.W)+1f)*0.5f*ViewportWidth;
                float ScreenY=(1f-(Clip.Y/Clip.W))*0.5f*ViewportHeight;
                float DeltaX=ScreenX-CursorX;
                float DeltaY=ScreenY-CursorY;
                float ScreenDistanceSquared=(DeltaX*DeltaX)+(DeltaY*DeltaY);
                float Depth=Clip.Z/Clip.W;
                if(ScreenDistanceSquared>BestScreenDistanceSquared
                    ||(Found&&ScreenDistanceSquared==BestScreenDistanceSquared&&Depth>=BestDepth)){
                    continue;
                }

                Found=true;
                BestScreenDistanceSquared=ScreenDistanceSquared;
                BestDepth=Depth;
                Hit=new VertexSnapHit(
                    Object.NodeId,Vector3.Transform(LocalPosition,Object.World),(float)Math.Sqrt(ScreenDistanceSquared));
            }
        }
        return Found;
    }

    /// <summary>ドラッグ対象を除外し、レイに最も近い三角形の交点とworld幾何法線を返す。</summary>
    internal static bool TrySurfaceSnapQuery(
        IReadOnlyList<SceneRenderObject>RenderObjects,Vector3 RayOrigin,Vector3 RayDirection,
        IReadOnlyCollection<NodePath>ExcludedRoots,out SurfaceSnapHit Hit){
        Hit=default;
        if(RayDirection.LengthSquared()<=1e-10f){ return false; }
        RayDirection=Vector3.Normalize(RayDirection);
        bool Found=false;
        float BestDistance=float.PositiveInfinity;
        foreach(SceneRenderObject Object in RenderObjects){
            if(IsExcluded(Object.NodeId,ExcludedRoots)||!Object.IsPickable){ continue; }
            SimpleMesh Mesh=Object.Mesh;
            for(int I=0;I+2<Mesh.Indices.Length;I+=3){
                Vector3 A=Vector3.Transform(Mesh.Positions[(int)Mesh.Indices[I]],Object.World);
                Vector3 B=Vector3.Transform(Mesh.Positions[(int)Mesh.Indices[I+1]],Object.World);
                Vector3 C=Vector3.Transform(Mesh.Positions[(int)Mesh.Indices[I+2]],Object.World);
                Vector3 Cross=Vector3.Cross(B-A,C-A);
                if(Cross.LengthSquared()<=1e-10f
                    ||!TryIntersectTriangle(RayOrigin,RayDirection,A,B,C,out float Distance)
                    ||Distance>=BestDistance){
                    continue;
                }

                Found=true;
                BestDistance=Distance;
                Hit=new SurfaceSnapHit(
                    Object.NodeId,RayOrigin+(RayDirection*Distance),Vector3.Normalize(Cross),Distance);
            }
        }
        return Found;
    }

    /// <summary>
    /// 各オブジェクトの投影済みメッシュ境界と選択矩形が交差するNodePathを描画順で返す。
    /// 同じNodePathを複数のrender objectが構成する場合も一度だけ返す。
    /// </summary>
    internal static IReadOnlyList<NodePath>PickMarquee(
        IReadOnlyList<SceneRenderObject>RenderObjects,Matrix4x4 View,Matrix4x4 Projection,
        ScreenRectangle SelectionRectangle,int ViewportWidth,int ViewportHeight){
        if(ViewportWidth<=0||ViewportHeight<=0){
            return Array.Empty<NodePath>();
        }

        List<NodePath>Hits=[];
        HashSet<NodePath>Seen=[];
        Matrix4x4 ViewProjection=View*Projection;
        foreach(SceneRenderObject Object in RenderObjects){
            if(!Object.IsPickable||Seen.Contains(Object.NodeId)
                ||!TryProjectBounds(Object,ViewProjection,ViewportWidth,ViewportHeight,
                    out float Left,out float Top,out float Right,out float Bottom)
                ||!SelectionRectangle.Intersects(Left,Top,Right,Bottom)){
                continue;
            }

            Seen.Add(Object.NodeId);
            Hits.Add(Object.NodeId);
        }

        return Hits;
    }

    private static bool TryProjectBounds(
        SceneRenderObject Object,Matrix4x4 ViewProjection,int ViewportWidth,int ViewportHeight,
        out float Left,out float Top,out float Right,out float Bottom){
        const float Epsilon=1e-6f;
        Left=float.PositiveInfinity;
        Top=float.PositiveInfinity;
        Right=float.NegativeInfinity;
        Bottom=float.NegativeInfinity;
        bool ProjectedAny=false;
        bool DepthVisible=false;
        Matrix4x4 WorldViewProjection=Object.World*ViewProjection;

        foreach(Vector3 Position in Object.Mesh.Positions){
            Vector4 Clip=Vector4.Transform(new Vector4(Position,1f),WorldViewProjection);
            if(Clip.W<=Epsilon||!IsFinite(Clip.X)||!IsFinite(Clip.Y)||!IsFinite(Clip.Z)||!IsFinite(Clip.W)){
                continue;
            }

            float NdcX=Clip.X/Clip.W;
            float NdcY=Clip.Y/Clip.W;
            float ScreenX=(NdcX+1f)*0.5f*ViewportWidth;
            float ScreenY=(1f-NdcY)*0.5f*ViewportHeight;
            Left=Math.Min(Left,ScreenX);
            Top=Math.Min(Top,ScreenY);
            Right=Math.Max(Right,ScreenX);
            Bottom=Math.Max(Bottom,ScreenY);
            ProjectedAny=true;
            DepthVisible|=Clip.Z>=0f&&Clip.Z<=Clip.W;
        }

        return ProjectedAny&&DepthVisible;
    }

    private static bool IsFinite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);

    private static bool IsExcluded(NodePath Path,IReadOnlyCollection<NodePath>ExcludedRoots){
        foreach(NodePath Root in ExcludedRoots){
            if(Path.Equals(Root)||Path.IsDescendantOf(Root)){ return true; }
        }
        return false;
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
