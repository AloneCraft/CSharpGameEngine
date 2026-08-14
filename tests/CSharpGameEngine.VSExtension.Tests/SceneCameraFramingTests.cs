using System;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneCameraFraming"/> の視点ポリシー。M0020 Phase 0 でバウンズ計算を
/// <see cref="SceneRenderer"/> から移設したことで、GPU も HWND も無しに検証できるようになった。
/// </summary>
public sealed class SceneCameraFramingTests {
    private const float Tolerance=1e-3f;

    [Fact]
    public void ComputeBounds_NoObjects_ReturnsOriginAndUnitRadius(){
        (Vector3 Center,float Radius)=SceneCameraFraming.ComputeBounds([]);

        Assert.Equal(Vector3.Zero,Center);
        Assert.Equal(1f,Radius);
    }

    [Fact]
    public void ComputeBounds_TransformsVerticesByWorldBeforeMeasuring(){
        // ローカル頂点は原点±1だが、World で +10 動かしてあるので中心は 10 になる。
        SceneRenderObject Object=CreateObject(
            Matrix4x4.CreateTranslation(10f,0f,0f),
            new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f));

        (Vector3 Center,float Radius)=SceneCameraFraming.ComputeBounds([Object]);

        Assert.Equal(10f,Center.X,3);
        Assert.Equal(1f,Radius,3);
    }

    [Fact]
    public void ComputeBounds_IgnoresObjectsExcludedFromBounds(){
        SceneRenderObject Stage=CreateObject(Matrix4x4.Identity,new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f));
        SceneRenderObject SkyDome=CreateObject(
            Matrix4x4.CreateScale(400f),
            new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f),
            IncludeInBounds:false);

        (Vector3 Center,float Radius)=SceneCameraFraming.ComputeBounds([Stage,SkyDome]);

        Assert.Equal(Vector3.Zero,Center);
        Assert.Equal(1f,Radius,3);
    }

    [Fact]
    public void ComputeBounds_RadiusIsTheMaxCenterToVertexDistance(){
        // AABB は (0,0,0)-(4,0,0) なので中心は (2,0,0)、半径は 2(AABB 半対角ではなく実距離)。
        SceneRenderObject Object=CreateObject(
            Matrix4x4.Identity,
            Vector3.Zero,new Vector3(4f,0f,0f));

        (Vector3 Center,float Radius)=SceneCameraFraming.ComputeBounds([Object]);

        Assert.Equal(2f,Center.X,3);
        Assert.Equal(2f,Radius,3);
    }

    [Fact]
    public void ComputeSelectionBounds_IncludesSkyDomeFlaggedObject_UnlikeComputeBounds(){
        SceneRenderObject SkyDome=CreateObject(
            Matrix4x4.CreateScale(400f),
            new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f),
            IncludeInBounds:false);

        (Vector3 ComputeBoundsCenter,float ComputeBoundsRadius)=SceneCameraFraming.ComputeBounds([SkyDome]);
        (Vector3 SelectionCenter,float SelectionRadius)=SceneCameraFraming.ComputeSelectionBounds([SkyDome]);

        // ComputeBounds は IncludeInBounds=false を除外するので縮退フォールバックへ落ちる。
        Assert.Equal(Vector3.Zero,ComputeBoundsCenter);
        Assert.Equal(1f,ComputeBoundsRadius);

        // ComputeSelectionBounds は明示的に選択されたオブジェクトなので実際の頂点から半径を出す。
        Assert.Equal(Vector3.Zero,SelectionCenter);
        Assert.Equal(400f,SelectionRadius,3);
    }

    [Fact]
    public void ComputeSelectionBounds_AllObjectsIncluded_MatchesComputeBounds(){
        SceneRenderObject Object=CreateObject(Matrix4x4.Identity,Vector3.Zero,new Vector3(4f,0f,0f));

        (Vector3 ComputeBoundsCenter,float ComputeBoundsRadius)=SceneCameraFraming.ComputeBounds([Object]);
        (Vector3 SelectionCenter,float SelectionRadius)=SceneCameraFraming.ComputeSelectionBounds([Object]);

        Assert.Equal(ComputeBoundsCenter,SelectionCenter);
        Assert.Equal(ComputeBoundsRadius,SelectionRadius,3);
    }

    [Fact]
    public void ComputeDistance_IsTheTangentDistanceForTheVerticalFieldOfViewWithSafetyMargin(){
        const float Radius=5f;
        const float FovY=(float)(Math.PI/3.0);

        var Distance=SceneCameraFraming.ComputeDistance(Radius,FovY);

        var Expected=Radius/(float)Math.Sin(FovY/2f)*1.1f;
        Assert.Equal(Expected,Distance,3);
    }

    [Fact]
    public void ComputeDistance_AtFortyFiveDegrees_StaysWithinFivePercentOfTheLegacyThreeRadiusFraming(){
        // M0020 Phase 0 までは距離が固定の 3.0*Radius だった。FOV 導出形に替えても見た目が実質
        // 変わらないことを固定する(実測 2.874*Radius = -4.2%)。
        const float Radius=12f;

        var Distance=SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians);

        var Legacy=Radius*3.0f;
        Assert.True(Math.Abs(Distance-Legacy)/Legacy<0.05f,$"distance {Distance} diverged from legacy {Legacy}");
    }

    [Fact]
    public void CreateView_PlacesTheEyeOnTheFixedDiagonalAndLooksAtTheBoundsCenter(){
        Vector3 Center=new(1f,2f,3f);
        const float Radius=4f;

        Matrix4x4 View=SceneCameraFraming.CreateView(Center,Radius,SceneCameraFraming.DefaultFovYRadians);

        Assert.True(Matrix4x4.Invert(View,out Matrix4x4 InverseView));
        Vector3 Eye=InverseView.Translation;
        Vector3 Offset=Eye-Center;
        Assert.Equal(SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians),Offset.Length(),2);
        Vector3 Direction=Vector3.Normalize(Offset);
        Assert.Equal(SceneCameraFraming.ViewDirection.X,Direction.X,3);
        Assert.Equal(SceneCameraFraming.ViewDirection.Y,Direction.Y,3);
        Assert.Equal(SceneCameraFraming.ViewDirection.Z,Direction.Z,3);
    }

    [Fact]
    public void FrameBounds_KeepsTheWholeBoundingSphereInsideTheVerticalFieldOfView(){
        // 式の写し間違いを検出する唯一の behavioral テスト: 境界球の上下端を実際に投影して
        // NDC の縦方向に収まることを確認する。
        Vector3 Center=new(3f,-2f,7f);
        const float Radius=6f;
        (Matrix4x4 View,Matrix4x4 Projection)=SceneCameraFraming.FrameBounds(
            Center,Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1f);
        Matrix4x4 ViewProjection=View*Projection;

        foreach(Vector3 Direction in new[]{ Vector3.UnitY,-Vector3.UnitY,Vector3.UnitX,-Vector3.UnitX }){
            Vector4 Clip=Vector4.Transform(new Vector4(Center+(Direction*Radius),1f),ViewProjection);
            Assert.True(Clip.W>0f,"bounding sphere extent fell behind the near plane");
            var NdcY=Clip.Y/Clip.W;
            Assert.True(Math.Abs(NdcY)<=1f+Tolerance,$"NDC y {NdcY} left the vertical field of view");
        }
    }

    [Fact]
    public void FrameBounds_DegenerateBoundsAndAspect_ProduceFiniteMatrices(){
        (Matrix4x4 View,Matrix4x4 Projection)=SceneCameraFraming.FrameBounds(
            Vector3.Zero,BoundsRadius:0f,FovYRadians:0f,AspectRatio:0f);

        Assert.False(float.IsNaN(View.M11)||float.IsInfinity(View.M11));
        Assert.False(float.IsNaN(Projection.M11)||float.IsInfinity(Projection.M11));
    }

    private static SceneRenderObject CreateObject(
        Matrix4x4 World,Vector3 First,Vector3 Second,bool IncludeInBounds=true)=>
        new(){
            Mesh=new SimpleMesh {
                Positions=[First,Second],
                Normals=[Vector3.UnitY,Vector3.UnitY],
                Indices=[0,1,0],
                BaseColorFactor=Vector4.One,
                BoundsCenter=Vector3.Zero,
                BoundsRadius=1f,
            },
            World=World,
            NodeId=NodePath.Root,
            IncludeInBounds=IncludeInBounds,
        };
}
