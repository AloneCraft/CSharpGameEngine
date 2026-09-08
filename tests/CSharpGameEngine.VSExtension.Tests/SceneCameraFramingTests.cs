using System;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneCameraFraming"/> の視点ポリシー。M0020 Phase 0 でバウンズ計算を
/// 旧in-process rendererから移設したことで、GPU も HWND も無しに検証できるようになった。
/// </summary>
public sealed class SceneCameraFramingTests {
    private const float Tolerance=1e-3f;

    /// <summary>
    /// M0030 / M0020残課題R1。near を 0.05 固定にしていた頃、実ステージ半径では far が数百まで伸びて
    /// near:far が 20000:1 に達し、壁・床・Sky dome の境界が Z-fighting していた。比率で頭打ちにする。
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(50f)]
    [InlineData(400f)]
    public void CreateProjection_KeepsTheDepthRatioWithinTheBudget(float BoundsRadius){
        var Projection=SceneCameraFraming.CreateProjection(
                                                           BoundsRadius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1.6f);

        (var Near,var Far)=ReadPerspectiveDepthRange(Projection);
        Assert.True(Near>0f,$"near {Near} must be positive");
        Assert.True(Near<Far,$"near {Near} must stay in front of far {Far}");
        Assert.True(
            Far/Near<=SceneCameraFraming.MaxDepthRatio*1.01f,
            $"depth ratio {Far/Near} exceeded the {SceneCameraFraming.MaxDepthRatio} budget");
    }

    [Fact]
    public void ComputeNearPlane_SmallScenes_KeepTheBaseNearPlaneSoCloseGeometryIsNotClipped(){
        // far が小さいうちは比率が効かず、従来どおり 0.05 のまま手前を残す。
        Assert.Equal(SceneCameraFraming.BaseNearPlane,SceneCameraFraming.ComputeNearPlane(20f),Tolerance);
        Assert.Equal(SceneCameraFraming.BaseNearPlane,SceneCameraFraming.ComputeNearPlane(400f),Tolerance);
        // far が伸びた分だけ near も比例して離れる。実ステージ規模で M0020 R1 の推奨 0.5〜1.0 に入る。
        Assert.Equal(0.8f,SceneCameraFraming.ComputeNearPlane(8000f),Tolerance);
        Assert.Equal(1f,SceneCameraFraming.ComputeNearPlane(10000f),Tolerance);
    }

    /// <summary>
    /// <c>Matrix4x4.CreatePerspectiveFieldOfView</c>(right-handed)の M33/M43 から near/far を復元する。
    /// M33=far/(near-far)、M43=near*far/(near-far)。
    /// </summary>
    internal static(float Near,float Far)ReadPerspectiveDepthRange(in Matrix4x4 Projection)=>(
        Projection.M43/Projection.M33,
        Projection.M43/(1f+Projection.M33));

    [Fact]
    public void ComputeBounds_NoObjects_ReturnsOriginAndUnitRadius(){
        (var Center,var Radius)=SceneCameraFraming.ComputeBounds([]);

        Assert.Equal(Vector3.Zero,Center);
        Assert.Equal(1f,Radius);
    }

    [Fact]
    public void ComputeBounds_TransformsVerticesByWorldBeforeMeasuring(){
        // ローカル頂点は原点±1だが、World で +10 動かしてあるので中心は 10 になる。
        var Object=CreateObject(
                                Matrix4x4.CreateTranslation(10f,0f,0f),
                                new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f));

        (var Center,var Radius)=SceneCameraFraming.ComputeBounds([Object]);

        Assert.Equal(10f,Center.X,3);
        Assert.Equal(1f,Radius,3);
    }

    [Fact]
    public void ComputeBounds_IgnoresObjectsExcludedFromBounds(){
        var Stage=CreateObject(Matrix4x4.Identity,new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f));
        var SkyDome=CreateObject(
                                 Matrix4x4.CreateScale(400f),
                                 new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f),
                                 IncludeInBounds:false);

        (var Center,var Radius)=SceneCameraFraming.ComputeBounds([Stage,SkyDome]);

        Assert.Equal(Vector3.Zero,Center);
        Assert.Equal(1f,Radius,3);
    }

    [Fact]
    public void ComputeBounds_RadiusIsTheMaxCenterToVertexDistance(){
        // AABB は (0,0,0)-(4,0,0) なので中心は (2,0,0)、半径は 2(AABB 半対角ではなく実距離)。
        var Object=CreateObject(
                                Matrix4x4.Identity,
                                Vector3.Zero,new Vector3(4f,0f,0f));

        (var Center,var Radius)=SceneCameraFraming.ComputeBounds([Object]);

        Assert.Equal(2f,Center.X,3);
        Assert.Equal(2f,Radius,3);
    }

    [Fact]
    public void ComputeSelectionBounds_IncludesSkyDomeFlaggedObject_UnlikeComputeBounds(){
        var SkyDome=CreateObject(
                                 Matrix4x4.CreateScale(400f),
                                 new Vector3(-1f,0f,0f),new Vector3(1f,0f,0f),
                                 IncludeInBounds:false);

        (var ComputeBoundsCenter,var ComputeBoundsRadius)=SceneCameraFraming.ComputeBounds([SkyDome]);
        (var SelectionCenter,var SelectionRadius)=SceneCameraFraming.ComputeSelectionBounds([SkyDome]);

        // ComputeBounds は IncludeInBounds=false を除外するので縮退フォールバックへ落ちる。
        Assert.Equal(Vector3.Zero,ComputeBoundsCenter);
        Assert.Equal(1f,ComputeBoundsRadius);

        // ComputeSelectionBounds は明示的に選択されたオブジェクトなので実際の頂点から半径を出す。
        Assert.Equal(Vector3.Zero,SelectionCenter);
        Assert.Equal(400f,SelectionRadius,3);
    }

    [Fact]
    public void ComputeSelectionBounds_AllObjectsIncluded_MatchesComputeBounds(){
        var Object=CreateObject(Matrix4x4.Identity,Vector3.Zero,new Vector3(4f,0f,0f));

        (var ComputeBoundsCenter,var ComputeBoundsRadius)=SceneCameraFraming.ComputeBounds([Object]);
        (var SelectionCenter,var SelectionRadius)=SceneCameraFraming.ComputeSelectionBounds([Object]);

        Assert.Equal(ComputeBoundsCenter,SelectionCenter);
        Assert.Equal(ComputeBoundsRadius,SelectionRadius,3);
    }

    [Fact]
    public void ComputeDistance_IsTheTangentDistanceForTheVerticalFieldOfViewWithSafetyMargin(){
        const float Radius=5f;
        const float FovY=(float)(Math.PI/3.0);

        var Distance=SceneCameraFraming.ComputeDistance(Radius,FovY,AspectRatio:1f);

        var Expected=Radius/(float)Math.Sin(FovY/2f)*1.1f;
        Assert.Equal(Expected,Distance,3);
    }

    [Fact]
    public void ComputeDistance_AtFortyFiveDegrees_StaysWithinFivePercentOfTheLegacyThreeRadiusFraming(){
        // M0020 Phase 0 までは距離が固定の 3.0*Radius だった。FOV 導出形に替えても見た目が実質
        // 変わらないことを固定する(実測 2.874f*Radius = -4.2f%)。
        const float Radius=12f;

        var Distance=SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1f);

        var Legacy=Radius*3.0f;
        Assert.True(Math.Abs(Distance-Legacy)/Legacy<0.05f,$"distance {Distance} diverged from legacy {Legacy}");
    }

    [Fact]
    public void CreateView_PlacesTheEyeOnTheFixedDiagonalAndLooksAtTheBoundsCenter(){
        Vector3 Center=new(1f,2f,3f);
        const float Radius=4f;

        var View=SceneCameraFraming.CreateView(Center,Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1f);

        Assert.True(Matrix4x4.Invert(View,out var InverseView));
        var Eye=InverseView.Translation;
        var Offset=Eye-Center;
        Assert.Equal(
            SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1f),
            Offset.Length(),
            2);
        var Direction=Vector3.Normalize(Offset);
        Assert.Equal(SceneCameraFraming.ViewDirection.X,Direction.X,3);
        Assert.Equal(SceneCameraFraming.ViewDirection.Y,Direction.Y,3);
        Assert.Equal(SceneCameraFraming.ViewDirection.Z,Direction.Z,3);
    }

    /// <summary>
    /// 式の写し間違いを検出する唯一の behavioral テスト: 境界球の各方向の端を実際に投影して
    /// NDC の縦横**両方**に収まることを確認する。
    ///
    /// <para>
    /// 以前はアスペクト比1固定で、しかも <c>±UnitX</c> もループしているのに NDC の Y だけを
    /// 検証していたため、**縦長ウィンドウ(aspect&lt;1)で境界球が左右にはみ出す破綻を素通り**
    /// させていた(M0020の残課題として記録されていた既知の逸脱)。水平FOVは
    /// <c>2*atan(tan(FovY/2)*aspect)</c> なので aspect&lt;1 では縦より狭くなり、距離を縦FOVだけで
    /// 決めると足りない。aspect≥1 側も併せて回すことで、実効FOVの導入が既存の見た目
    /// (Phase 0 以来の距離)を変えていないことも同時に固定する。
    /// </para>
    /// </summary>
    [Fact]
    public void FrameBounds_KeepsTheWholeBoundingSphereInsideBothFieldsOfView(){
        Vector3 Center=new(3f,-2f,7f);
        const float Radius=6f;

        foreach(var AspectRatio in new[]{ 4f,16f/9f,1f,0.75f,0.5f,0.25f }){
            (var View,var Projection)=SceneCameraFraming.FrameBounds(
                                                                     Center,Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio);
            var ViewProjection=View*Projection;

            foreach(var Direction in new[]{
                                              Vector3.UnitY,-Vector3.UnitY,Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ }){
                var Clip=Vector4.Transform(new Vector4(Center+(Direction*Radius),1f),ViewProjection);
                Assert.True(Clip.W>0f,$"aspect {AspectRatio}: bounding sphere extent fell behind the near plane");
                var NdcX=Clip.X/Clip.W;
                var NdcY=Clip.Y/Clip.W;
                Assert.True(Math.Abs(NdcY)<=1f+Tolerance,$"aspect {AspectRatio}: NDC y {NdcY} left the vertical field of view");
                Assert.True(Math.Abs(NdcX)<=1f+Tolerance,$"aspect {AspectRatio}: NDC x {NdcX} left the horizontal field of view");
            }
        }
    }

    [Fact]
    public void ComputeEffectiveFovRadians_SquareAspect_EqualsTheVerticalFieldOfView(){
        const float FovY=(float)(Math.PI/4.0);

        Assert.Equal(FovY,SceneCameraFraming.ComputeEffectiveFovRadians(FovY,AspectRatio:1f),3);
    }

    [Fact]
    public void ComputeEffectiveFovRadians_WideAspect_StaysAtTheVerticalFieldOfView(){
        // 横長では水平FOVの方が広いので、境界球を制約するのは縦FOVのまま。min がそれを選ぶ。
        const float FovY=(float)(Math.PI/4.0);

        Assert.Equal(FovY,SceneCameraFraming.ComputeEffectiveFovRadians(FovY,AspectRatio:16f/9f),3);
        Assert.Equal(FovY,SceneCameraFraming.ComputeEffectiveFovRadians(FovY,AspectRatio:4f),3);
    }

    [Fact]
    public void ComputeEffectiveFovRadians_TallAspect_ReturnsTheNarrowerHorizontalFieldOfView(){
        const float FovY=(float)(Math.PI/4.0);
        const float AspectRatio=0.5f;

        var Effective=SceneCameraFraming.ComputeEffectiveFovRadians(FovY,AspectRatio);

        var ExpectedHorizontal=2f*(float)Math.Atan(Math.Tan(FovY/2f)*AspectRatio);
        Assert.Equal(ExpectedHorizontal,Effective,3);
        Assert.True(Effective<FovY,$"effective fov {Effective} should be narrower than the vertical {FovY}");
    }

    [Fact]
    public void ComputeDistance_TallAspect_PullsTheEyeBackFartherThanTheSquareCase(){
        const float Radius=5f;

        var Square=SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1f);
        var Tall=SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:0.5f);

        Assert.True(Tall>Square,$"tall aspect distance {Tall} should exceed the square case {Square}");
    }

    [Fact]
    public void ComputeDistance_WideAspect_MatchesTheSquareCaseExactly(){
        // aspect≥1 では実効FOVが縦FOVのままなので、M0020 Phase 0 以来の距離が1ビットも変わらない。
        // この不変性が「横長・正方形の既存の見た目を変えていない」ことの回帰防止そのもの。
        const float Radius=5f;

        var Square=SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1f);

        Assert.Equal(Square,SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:16f/9f));
        Assert.Equal(Square,SceneCameraFraming.ComputeDistance(Radius,SceneCameraFraming.DefaultFovYRadians,AspectRatio:4f));
    }

    [Fact]
    public void FrameBounds_DegenerateBoundsAndAspect_ProduceFiniteMatrices(){
        (var View,var Projection)=SceneCameraFraming.FrameBounds(
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
