using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="ScenePicker"/>のMöller–Trumbore三角形交差(design doc §5)。GPU/HWND無しで固定する。
/// VertexSnap/SurfaceSnapクエリも同じCPU mesh/worldデータだけを使用する。
/// </summary>
public sealed class ScenePickerTests {
    [Fact]
    public void TryPick_RayThroughTriangle_ReturnsHitWithNodePath(){
        SceneRenderObject Object=CreateTriangleAtZ(5f,NodePath.Root);

        bool Hit=ScenePicker.TryPick([Object],RayOrigin:new Vector3(0.5f,0.5f,0f),RayDirection:Vector3.UnitZ,out NodePath HitPath);

        Assert.True(Hit);
        Assert.Equal(NodePath.Root,HitPath);
    }

    [Fact]
    public void TryPick_RayMissesTriangle_ReturnsFalse(){
        SceneRenderObject Object=CreateTriangleAtZ(5f,NodePath.Root);

        bool Hit=ScenePicker.TryPick([Object],RayOrigin:new Vector3(10f,10f,0f),RayDirection:Vector3.UnitZ,out _);

        Assert.False(Hit);
    }

    [Fact]
    public void TryPick_HitsBackFace_StillReturnsHit(){
        SceneRenderObject Object=CreateTriangleAtZ(5f,NodePath.Root);

        bool Hit=ScenePicker.TryPick(
            [Object],RayOrigin:new Vector3(0.5f,0.5f,10f),RayDirection:-Vector3.UnitZ,out NodePath HitPath);

        Assert.True(Hit);
        Assert.Equal(NodePath.Root,HitPath);
    }

    [Fact]
    public void TryPick_TwoObjectsAlongSameRay_ReturnsNearestOne(){
        NodePath NearPath=NodePath.Root;
        NodePath FarPath=NodePath.Root.Append(1);
        SceneRenderObject Near=CreateTriangleAtZ(5f,NearPath);
        SceneRenderObject Far=CreateTriangleAtZ(8f,FarPath);

        bool Hit=ScenePicker.TryPick(
            [Far,Near],RayOrigin:new Vector3(0.5f,0.5f,0f),RayDirection:Vector3.UnitZ,out NodePath HitPath);

        Assert.True(Hit);
        Assert.Equal(NearPath,HitPath);
    }

    [Fact]
    public void TryPick_EmptyRenderObjectList_ReturnsFalse(){
        bool Hit=ScenePicker.TryPick([],RayOrigin:Vector3.Zero,RayDirection:Vector3.UnitZ,out _);

        Assert.False(Hit);
    }

    [Fact]
    public void TryPick_WorldTransformApplied_HitAccountsForTranslation(){
        // ローカルは原点付近の三角形だが、World の平行移動で Z=5 へ動かしてある。
        SceneRenderObject Object=new(){
            Mesh=CreateRightTriangleMesh(),
            World=Matrix4x4.CreateTranslation(0f,0f,5f),
            NodeId=NodePath.Root,
        };

        bool Hit=ScenePicker.TryPick([Object],RayOrigin:new Vector3(0.5f,0.5f,0f),RayDirection:Vector3.UnitZ,out NodePath HitPath);

        Assert.True(Hit);
        Assert.Equal(NodePath.Root,HitPath);
    }

    /// <summary>
    /// M0020追加(Orbitのピクセル完全一致ドラッグ用)。既存の交差判定は距離を内部で計算済みだが
    /// 呼び出し元へ返していなかった — 多重定義で公開しただけで既存の判定ロジックは無改造。
    /// <c>RayDirection</c>は正規化されている前提なので、世界座標のヒット点は
    /// <c>RayOrigin+RayDirection*HitDistance</c>で得られるはず。
    /// </summary>
    [Fact]
    public void TryPick_WithDistanceOverload_ReturnsDistanceAlongRay(){
        SceneRenderObject Object=CreateTriangleAtZ(5f,NodePath.Root);
        Vector3 RayOrigin=new(0.5f,0.5f,0f);
        Vector3 RayDirection=Vector3.UnitZ;

        bool Hit=ScenePicker.TryPick(
            [Object],RayOrigin,RayDirection,out NodePath HitPath,out float HitDistance);

        Assert.True(Hit);
        Assert.Equal(NodePath.Root,HitPath);
        Assert.Equal(5f,HitDistance,3);
        Vector3 HitPoint=RayOrigin+(RayDirection*HitDistance);
        Assert.Equal(new Vector3(0.5f,0.5f,5f),HitPoint);
    }

    /// <summary>従来の3引数版は新しい5引数版へ転送しているだけであることの回帰防止。</summary>
    [Fact]
    public void TryPick_ThreeArgumentOverload_StillWorksIdenticallyToFiveArgumentOverload(){
        SceneRenderObject Object=CreateTriangleAtZ(5f,NodePath.Root);

        bool Hit=ScenePicker.TryPick(
            [Object],RayOrigin:new Vector3(0.5f,0.5f,0f),RayDirection:Vector3.UnitZ,out NodePath HitPath);

        Assert.True(Hit);
        Assert.Equal(NodePath.Root,HitPath);
    }

    [Fact]
    public void PickMarquee_RectangleIntersectsObjectScreenBounds_ReturnsEveryIntersectingNode(){
        NodePath FirstPath=NodePath.Root;
        NodePath SecondPath=NodePath.Root.Append(1);
        SceneRenderObject First=CreateScreenTriangle(-0.5f,0.5f,FirstPath);
        SceneRenderObject Second=CreateScreenTriangle(0.5f,-0.5f,SecondPath);

        IReadOnlyList<NodePath>Hits=ScenePicker.PickMarquee(
            [First,Second],Matrix4x4.Identity,Matrix4x4.Identity,
            ScreenRectangle.FromPoints(20,20,80,80),ViewportWidth:100,ViewportHeight:100);

        Assert.Equal([FirstPath,SecondPath],Hits);
    }

    [Fact]
    public void PickMarquee_RectangleTouchesObjectBoundsEdge_CountsAsIntersection(){
        NodePath Path=NodePath.Root;
        SceneRenderObject Object=CreateScreenTriangle(-0.5f,0.5f,Path);

        IReadOnlyList<NodePath>Hits=ScenePicker.PickMarquee(
            [Object],Matrix4x4.Identity,Matrix4x4.Identity,
            ScreenRectangle.FromPoints(0,0,25,25),ViewportWidth:100,ViewportHeight:100);

        Assert.Equal([Path],Hits);
    }

    [Fact]
    public void PickMarquee_ObjectOutsideRectangle_IsExcluded(){
        SceneRenderObject Object=CreateScreenTriangle(-0.5f,0.5f,NodePath.Root);

        IReadOnlyList<NodePath>Hits=ScenePicker.PickMarquee(
            [Object],Matrix4x4.Identity,Matrix4x4.Identity,
            ScreenRectangle.FromPoints(70,70,90,90),ViewportWidth:100,ViewportHeight:100);

        Assert.Empty(Hits);
    }

    [Fact]
    public void PickMarquee_DuplicateRenderObjectsForSameNode_ReturnsNodeOnce(){
        NodePath Path=NodePath.Root;
        SceneRenderObject First=CreateScreenTriangle(-0.5f,0.5f,Path);
        SceneRenderObject Second=CreateScreenTriangle(-0.4f,0.4f,Path);

        IReadOnlyList<NodePath>Hits=ScenePicker.PickMarquee(
            [First,Second],Matrix4x4.Identity,Matrix4x4.Identity,
            ScreenRectangle.FromPoints(0,0,60,60),ViewportWidth:100,ViewportHeight:100);

        Assert.Equal([Path],Hits);
    }

    [Fact]
    public void TryVertexSnapQuery_ReturnsClosestProjectedWorldVertexWithinEightPixels(){
        NodePath Path=NodePath.Root.Append(1);
        SceneRenderObject Object=CreateScreenTriangle(-0.5f,0.5f,Path);

        bool Found=ScenePicker.TryVertexSnapQuery(
            [Object],Matrix4x4.Identity,Matrix4x4.Identity,
            CursorX:27f,CursorY:26f,ViewportWidth:100,ViewportHeight:100,
            ExcludedRoots:[],out VertexSnapHit Hit);

        Assert.True(Found);
        Assert.Equal(Path,Hit.NodePath);
        Assert.Equal(new Vector3(-0.5f,0.5f,0f),Hit.Position);
    }

    [Fact]
    public void TryVertexSnapQuery_VertexOutsideEightPixels_ReturnsFalse(){
        SceneRenderObject Object=CreateScreenTriangle(-0.5f,0.5f,NodePath.Root.Append(1));

        bool Found=ScenePicker.TryVertexSnapQuery(
            [Object],Matrix4x4.Identity,Matrix4x4.Identity,
            CursorX:50f,CursorY:50f,ViewportWidth:100,ViewportHeight:100,
            ExcludedRoots:[],out _);

        Assert.False(Found);
    }

    [Fact]
    public void TryVertexSnapQuery_ExcludesDraggedRootAndItsDescendants(){
        NodePath Root=NodePath.Root.Append(1);
        SceneRenderObject Child=CreateScreenTriangle(-0.5f,0.5f,Root.Append(0));

        bool Found=ScenePicker.TryVertexSnapQuery(
            [Child],Matrix4x4.Identity,Matrix4x4.Identity,
            CursorX:25f,CursorY:25f,ViewportWidth:100,ViewportHeight:100,
            ExcludedRoots:[Root],out _);

        Assert.False(Found);
    }

    [Fact]
    public void TrySurfaceSnapQuery_ReturnsNearestPointAndGeometricNormalWhileExcludingDraggedRoot(){
        NodePath Dragged=NodePath.Root.Append(0);
        NodePath SurfacePath=NodePath.Root.Append(1);
        SceneRenderObject OwnSurface=CreateTriangleAtZ(2f,Dragged.Append(0));
        SceneRenderObject TargetSurface=CreateTriangleAtZ(5f,SurfacePath);

        bool Found=ScenePicker.TrySurfaceSnapQuery(
            [OwnSurface,TargetSurface],new Vector3(0.5f,0.5f,0f),Vector3.UnitZ,
            ExcludedRoots:[Dragged],out SurfaceSnapHit Hit);

        Assert.True(Found);
        Assert.Equal(SurfacePath,Hit.NodePath);
        Assert.Equal(new Vector3(0.5f,0.5f,5f),Hit.Position);
        Assert.Equal(Vector3.UnitZ,Hit.Normal);
    }

    /// <summary>直角三角形 (0,0)-(2,0)-(0,2) を Z=<paramref name="Z"/> 平面に置く。(0.5,0.5) は内部の点。</summary>
    private static SceneRenderObject CreateTriangleAtZ(float Z,NodePath NodeId)=>
        new(){
            Mesh=CreateRightTriangleMesh(),
            World=Matrix4x4.CreateTranslation(0f,0f,Z),
            NodeId=NodeId,
        };

    private static SimpleMesh CreateRightTriangleMesh()=>
        new(){
            Positions=[Vector3.Zero,new Vector3(2f,0f,0f),new Vector3(0f,2f,0f)],
            Normals=[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],
            Indices=[0,1,2],
            BaseColorFactor=Vector4.One,
            BoundsCenter=Vector3.Zero,
            BoundsRadius=2f,
        };

    private static SceneRenderObject CreateScreenTriangle(float NdcX,float NdcY,NodePath NodeId)=>
        new(){
            Mesh=new SimpleMesh{
                Positions=[
                    new Vector3(NdcX,NdcY,0f),
                    new Vector3(NdcX+0.25f,NdcY,0f),
                    new Vector3(NdcX,NdcY-0.25f,0f),
                ],
                Normals=[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],
                Indices=[0,1,2],
                BaseColorFactor=Vector4.One,
                BoundsCenter=new Vector3(NdcX,NdcY,0f),
                BoundsRadius=0.25f,
            },
            World=Matrix4x4.Identity,
            NodeId=NodeId,
        };
}
