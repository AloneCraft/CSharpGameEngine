using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="ScenePicker"/>のMöller–Trumbore三角形交差(design doc §5)。GPU/HWND無しで固定する。
/// VertexSnap/SurfaceSnapクエリ(design doc §5の他2クエリ)はM0020 Phase 5スコープでありここには無い。
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
}
