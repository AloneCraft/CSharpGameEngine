using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneViewCamera"/>の閉形式が legacy
/// <c>CSharpGameEngine.Rendering.View.RenderCamera</c>(net10.0)と一致することを固定する。
/// GetForward/GetFlatRight/ApplyLookDeltaの2件は net10.0 側の
/// CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep /
/// GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep の複製オラクル。
/// </summary>
public sealed class SceneViewCameraTests {
    private const int Precision=4;

    private static readonly float[]Yaws=[-3.0f,-1.5f,-0.7f,0f,0.3f,0.7f,1.5f,3.0f];
    private static readonly float[]Pitches=[-1.2f,-0.5f,0f,0.3f,1.2f];

    [Fact]
    public void GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep(){
        foreach(var Yaw in Yaws){
            foreach(var Pitch in Pitches){
                var Actual=SceneViewCamera.GetForward(Yaw,Pitch);

                var CosPitch=(float)Math.Cos(Pitch);
                var Expected=Vector3.Normalize(new Vector3(
                                                           CosPitch*(float)Math.Sin(Yaw),
                                                           (float)Math.Sin(Pitch),
                                                           CosPitch*(float)Math.Cos(Yaw)));

                AssertApprox(Expected,Actual);
            }
        }
    }

    [Fact]
    public void GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep(){
        foreach(var Yaw in Yaws){
            var Actual=SceneViewCamera.GetFlatRight(Yaw);
            var Expected=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,SceneViewCamera.GetFlatForward(Yaw)));

            AssertApprox(Expected,Actual);
        }
    }

    [Fact]
    public void GetUp_IsOrthogonalToForwardAndFlatRight_AcrossYawPitchSweep(){
        foreach(var Yaw in Yaws){
            foreach(var Pitch in Pitches){
                var Up=SceneViewCamera.GetUp(Yaw,Pitch);
                var Forward=SceneViewCamera.GetForward(Yaw,Pitch);
                var Right=SceneViewCamera.GetFlatRight(Yaw);

                Assert.Equal(0f,Vector3.Dot(Up,Forward),3);
                Assert.Equal(0f,Vector3.Dot(Up,Right),3);
                Assert.Equal(1f,Up.Length(),3);
            }
        }
    }

    [Fact]
    public void GetUp_AtZeroPitch_EqualsWorldUp(){
        var Up=SceneViewCamera.GetUp(Yaw:0.7f,Pitch:0f);

        AssertApprox(Vector3.UnitY,Up);
    }

    [Fact]
    public void GetEyePosition_EqualsFocusMinusForwardTimesDistance(){
        Vector3 Focus=new(3f,4f,5f);
        const float Yaw=0.6f;
        const float Pitch=0.2f;
        const float Distance=7f;

        var Eye=SceneViewCamera.GetEyePosition(Focus,Yaw,Pitch,Distance);

        AssertApprox(Focus-SceneViewCamera.GetForward(Yaw,Pitch)*Distance,Eye);
    }

    [Fact]
    public void GetView_AtPositiveDistance_LooksFromEyeTowardFocus(){
        Vector3 Focus=new(1f,2f,3f);
        var View=SceneViewCamera.GetView(Focus,Yaw:0.4f,Pitch:0.1f,FocusDistance:5f);

        Assert.True(Matrix4x4.Invert(View,out var InverseView));
        var Eye=SceneViewCamera.GetEyePosition(Focus,0.4f,0.1f,5f);
        AssertApprox(Eye,InverseView.Translation);
    }

    [Fact]
    public void ApplyLookDelta_ClampsPitchToOrbitRange(){
        (_,var Pitch)=SceneViewCamera.ApplyLookDelta(
                                                     Yaw:0f,Pitch:0f,DeltaX:0f,DeltaY:-100000f,
                                                     YawSensitivity:0.01f,PitchSensitivity:0.01f,InvertX:false,InvertY:false);

        Assert.Equal(SceneViewCamera.MaxPitchRadians,Pitch,4);
    }

    [Fact]
    public void ApplyLookDelta_InvertX_NegatesYawDelta(){
        (var YawNormal,_)=SceneViewCamera.ApplyLookDelta(0f,0f,10f,0f,0.01f,0.01f,InvertX:false,InvertY:false);
        (var YawInverted,_)=SceneViewCamera.ApplyLookDelta(0f,0f,10f,0f,0.01f,0.01f,InvertX:true,InvertY:false);

        Assert.Equal(-YawNormal,YawInverted,4);
    }

    [Fact]
    public void ApplyLookDelta_InvertY_NegatesPitchDelta(){
        (_,var PitchNormal)=SceneViewCamera.ApplyLookDelta(0f,0f,0f,10f,0.01f,0.01f,InvertX:false,InvertY:false);
        (_,var PitchInverted)=SceneViewCamera.ApplyLookDelta(0f,0f,0f,10f,0.01f,0.01f,InvertX:false,InvertY:true);

        Assert.Equal(-PitchNormal,PitchInverted,4);
    }

    [Fact]
    public void ScreenPointToRay_ViewportCenter_DirectionMatchesForward(){
        Vector3 Focus=new(1f,2f,3f);
        const float Yaw=0.4f;
        const float Pitch=0.15f;
        var View=SceneViewCamera.GetView(Focus,Yaw,Pitch,FocusDistance:5f);
        var Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),1f,0.1f,100f);

        (_,var Direction)=SceneViewCamera.ScreenPointToRay(View,Projection,400f,300f,800f,600f);

        AssertApprox(SceneViewCamera.GetForward(Yaw,Pitch),Direction);
    }

    [Fact]
    public void ScreenPointToRay_KnownWorldPoint_RayPassesThroughIt(){
        var Focus=Vector3.Zero;
        var View=SceneViewCamera.GetView(Focus,Yaw:0.3f,Pitch:0.2f,FocusDistance:10f);
        var Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),800f/600f,0.1f,100f);
        Vector3 WorldPoint=new(2f,1f,-3f);

        var Clip=Vector4.Transform(new Vector4(WorldPoint,1f),View*Projection);
        var Ndc=new Vector3(Clip.X,Clip.Y,Clip.Z)/Clip.W;
        var ScreenX=(Ndc.X+1f)/2f*800f;
        var ScreenY=(1f-Ndc.Y)/2f*600f;

        (var Origin,var Direction)=SceneViewCamera.ScreenPointToRay(View,Projection,ScreenX,ScreenY,800f,600f);

        var T=Vector3.Dot(WorldPoint-Origin,Direction);
        var ClosestPointOnRay=Origin+(Direction*T);
        Assert.Equal(0f,Vector3.Distance(ClosestPointOnRay,WorldPoint),2);
    }

    [Fact]
    public void ScreenPointToRay_Orthographic_ProducesParallelRaysAcrossScreen(){
        var View=SceneViewCamera.GetView(Vector3.Zero,Yaw:0.1f,Pitch:0.1f,FocusDistance:10f);
        var Projection=Matrix4x4.CreateOrthographic(10f,10f,0.1f,100f);

        (_,var DirectionA)=SceneViewCamera.ScreenPointToRay(View,Projection,100f,100f,800f,600f);
        (_,var DirectionB)=SceneViewCamera.ScreenPointToRay(View,Projection,700f,500f,800f,600f);

        AssertApprox(DirectionA,DirectionB);
    }

    /// <summary>
    /// <b><see cref="SceneViewCamera.GetFlatRight"/>は名前に反して画面の「左」(NDC X が負)へ写る。
    /// これは意図的であり、直してはいけない。</b>
    ///
    /// <para>
    /// 基底は左手系(<c>GetFlatForward=(sinY,0,cosY)</c>、ADR-0025のUnity規約)なのに行列は右手系
    /// (<c>Matrix4x4.CreateLookAt</c>は<c>xaxis=Cross(Up,normalize(Eye-Target))=-GetFlatRight</c>を作る)
    /// という食い違いがあるため。2026-08にこれを「バグ」と判断して左手系へ組み替えたが、
    /// <b>ゲーム本体も同じ右手系</b>(<c>src/CSharpGameEngine.Rendering/View/RenderCamera.cs</c>の
    /// <c>BuildViewMatrix</c>/<c>BuildProjection</c>、<c>StageRenderer</c>)なので、
    /// シーンデザイナーだけがゲームと左右反転してしまい差し戻した。シーンデザイナーはゲームの見た目を
    /// 再現するものなので、ゲームとの一致が判断基準になる。
    /// </para>
    ///
    /// <para>
    /// このテストは「左右が反転しているように見える」ことを不変条件として固定し、同じ誤診を
    /// 繰り返さないためにある。上下(<see cref="SceneViewCamera.GetUp"/>)は反転しておらず素直に
    /// 画面の上へ写る(右手系の<c>yaxis=Cross(zaxis,xaxis)</c>が<c>GetUp</c>と一致するため)。
    /// Fly の A/D と <c>SceneViewState.ApplyPan</c> の横方向が符号を反転させているのはこの帰結。
    /// </para>
    /// </summary>
    [Fact]
    public void GetFlatRight_ProjectsToScreenLeft_BecauseTheEngineUsesRightHandedMatrices(){
        foreach(var Yaw in Yaws){
            foreach(var Pitch in Pitches){
                Vector3 Focus=new(3f,-2f,5f);
                var ViewProjection=
                    SceneViewCamera.GetView(Focus,Yaw,Pitch,FocusDistance:10f)
                    *SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1.5f,NearZ:0.1f,FarZ:100f);

                var RightNdc=ProjectToNdc(Focus+(SceneViewCamera.GetFlatRight(Yaw)*2f),ViewProjection);
                var UpNdc=ProjectToNdc(Focus+(SceneViewCamera.GetUp(Yaw,Pitch)*2f),ViewProjection);

                Assert.True(
                    RightNdc.X<0f,
                    $"Yaw={Yaw} Pitch={Pitch}: GetFlatRightがNDC X={RightNdc.X}。"
                    +"正になったなら行列を左手系へ変えた可能性がある — ゲーム本体と一致しなくなる(要約はGetViewのコメント)。");
                Assert.True(UpNdc.Y>0f,$"Yaw={Yaw} Pitch={Pitch}: 上方向がNDC Y={UpNdc.Y}(上下は反転しないはず)");
            }
        }
    }

    /// <summary>
    /// ゲーム本体(<c>Camera.BuildViewMatrix</c>/<c>BuildProjection</c>)と同じ行列構成であること。
    /// シーンデザイナーの見た目がゲームと一致することの根拠なので、どちらかを変えるときは
    /// 両方を一緒に変えなければならない。
    /// </summary>
    [Fact]
    public void ViewAndProjection_MatchTheRuntimeGameConstruction(){
        Vector3 Focus=new(1f,2f,3f);
        const float Yaw=0.6f;
        const float Pitch=0.3f;
        const float Distance=12f;

        var Eye=SceneViewCamera.GetEyePosition(Focus,Yaw,Pitch,Distance);

        Assert.Equal(
            Matrix4x4.CreateLookAt(Eye,Focus,Vector3.UnitY),
            SceneViewCamera.GetView(Focus,Yaw,Pitch,Distance));
        Assert.Equal(
            Matrix4x4.CreatePerspectiveFieldOfView(1f,1.5f,0.1f,100f),
            SceneViewCamera.GetProjection(1f,1.5f,0.1f,100f));
    }

    /// <summary>
    /// 深度の向き: 近い点のNDC Zが遠い点より小さいこと。左手系へ組み替えるときに
    /// 深度を壊していないことを固定する(D3D12は0..1のZを前提)。
    /// </summary>
    [Fact]
    public void NearerPointsGetSmallerNdcDepth(){
        var Focus=Vector3.Zero;
        const float Yaw=0.4f;
        const float Pitch=0.2f;
        var ViewProjection=
            SceneViewCamera.GetView(Focus,Yaw,Pitch,FocusDistance:10f)
            *SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1.5f,NearZ:0.1f,FarZ:100f);

        var Forward=SceneViewCamera.GetForward(Yaw,Pitch);
        var Eye=SceneViewCamera.GetEyePosition(Focus,Yaw,Pitch,FocusDistance:10f);

        var NearDepth=ProjectToNdc(Eye+(Forward*1f),ViewProjection).Z;
        var FarDepth=ProjectToNdc(Eye+(Forward*50f),ViewProjection).Z;

        Assert.True(NearDepth<FarDepth,$"近{NearDepth} 遠{FarDepth}: 深度の向きが逆");
        Assert.True(NearDepth is >=0f and <=1f,$"近点のNDC Z={NearDepth}が0..1の外");
        Assert.True(FarDepth is >=0f and <=1f,$"遠点のNDC Z={FarDepth}が0..1の外");
    }

    // ----- M0020追加: RMB Look / Alt+LMB Orbit のピクセル完全一致ドラッグ -----

    /// <summary>
    /// <see cref="SceneViewCamera.DecomposeDirection"/>の中核的な性質: 固定されたスクリーンピクセルの
    /// ローカル係数は、どの(Yaw,Pitch)を基準に分解しても同じ値になる(スクリーンピクセルの
    /// 光学的な位置関係だけを表すため)。<see cref="SceneViewCamera.SolveLookOrientation"/>の
    /// 前提そのものであり、ここが崩れると閉形式全体が成り立たない。
    /// </summary>
    [Fact]
    public void DecomposeDirection_IsInvariantToReferenceOrientation_ForFixedScreenPixel(){
        Vector3 Focus=new(2f,-1f,4f);
        const float Distance=10f;
        var Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),800f/600f,0.1f,100f);
        const float ScreenX=320f;
        const float ScreenY=180f;

        Vector3?FirstLocalAxes=null;
        foreach(var Yaw in Yaws){
            foreach(var Pitch in Pitches){
                var View=SceneViewCamera.GetView(Focus,Yaw,Pitch,Distance);
                (_,var Direction)=SceneViewCamera.ScreenPointToRay(View,Projection,ScreenX,ScreenY,800f,600f);
                var LocalAxes=SceneViewCamera.DecomposeDirection(Direction,Yaw,Pitch);

                FirstLocalAxes??=LocalAxes;
                AssertApprox(FirstLocalAxes.Value,LocalAxes);
            }
        }
    }

    /// <summary>
    /// エンドツーエンドの本命テスト: シード視点でのある画面ピクセルの局所係数と、目標視点での
    /// 「同じピクセルが指すはずのワールド方向」から解いた(Yaw,Pitch)で、同じピクセルを
    /// 再度アンプロジェクトすると目標方向へ厳密に一致する。RMBドラッグでアンカー方向が
    /// カーソル先端に完全一致し続けることの数学的な証明そのもの。
    /// </summary>
    /// <summary>
    /// シードと目標は実際のドラッグ1フレーム分に相当する小さな差(数度程度)にしてある。
    /// Pitchの解は<c>Phi±acos(...)</c>の2branchのうち「直前に近い方」を選ぶため、シードと目標が
    /// 大きく(無関係に)離れていると、たとえ両branchとも数学的に有効(再構成は成立する)でも
    /// 「近い方」が可動域外の値になりクランプで丸められることがある — これは連続的なドラッグでは
    /// 起こらない(1フレームの回転量は小さいため)。よってこのテストは現実のドラッグを模した
    /// 小さな差だけを検証する。
    /// </summary>
    [Fact]
    public void SolveLookOrientation_ReconstructedDirectionMatchesTarget_AcrossYawPitchFovAspectSweep(){
        var Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),16f/9f,0.1f,100f);
        var Focus=Vector3.Zero;
        const float Distance=8f;
        (float Yaw,float Pitch)[]Seeds=[(0f,0f),(0.5f,-0.3f),(-1.1f,0.2f),(2.3f,0.35f)];
        (float DeltaYaw,float DeltaPitch)[]Deltas=[(0.05f,0.03f),(-0.08f,-0.05f),(0.12f,-0.02f),(-0.04f,0.07f)];
        (float X,float Y)[]ScreenPoints=[(400f,300f),(100f,50f),(700f,550f),(250f,400f)];

        foreach((var SeedYaw,var SeedPitch)in Seeds){
            foreach((var DeltaYaw,var DeltaPitch)in Deltas){
                var TargetYaw=SeedYaw+DeltaYaw;
                var TargetPitch=SeedPitch+DeltaPitch;
                foreach((var ScreenX,var ScreenY)in ScreenPoints){
                    var SeedView=SceneViewCamera.GetView(Focus,SeedYaw,SeedPitch,Distance);
                    (_,var SeedRay)=SceneViewCamera.ScreenPointToRay(SeedView,Projection,ScreenX,ScreenY,800f,600f);
                    var LocalAxes=SceneViewCamera.DecomposeDirection(SeedRay,SeedYaw,SeedPitch);

                    var TargetView=SceneViewCamera.GetView(Focus,TargetYaw,TargetPitch,Distance);
                    (_,var TargetDirection)=SceneViewCamera.ScreenPointToRay(
                                                                             TargetView,Projection,ScreenX,ScreenY,800f,600f);

                    (var SolvedYaw,var SolvedPitch)=SceneViewCamera.SolveLookOrientation(
                        LocalAxes,TargetDirection,SeedYaw,SeedPitch);

                    var SolvedView=SceneViewCamera.GetView(Focus,SolvedYaw,SolvedPitch,Distance);
                    (_,var ReconstructedDirection)=SceneViewCamera.ScreenPointToRay(
                        SolvedView,Projection,ScreenX,ScreenY,800f,600f);

                    // atan2/acos/sin/cosを何段も連鎖させるため、他のテストが使う4桁精度(1e-4)より
                    // 少し緩い1e-3で見る(float32での累積丸め誤差。Plan agentによるdouble精度での
                    // 独立検証では同じ式が1e-14未満の誤差に収まることを確認済み — アルゴリズム自体の
                    // 精度ではなくfloat32の桁数の問題)。
                    Assert.True(
                        Vector3.Distance(TargetDirection,ReconstructedDirection)<1e-3f,
                        $"Yaw/Pitch=({SeedYaw},{SeedPitch})->({TargetYaw},{TargetPitch}) "
                        +$"Screen=({ScreenX},{ScreenY}): target {TargetDirection} vs "
                        +$"reconstructed {ReconstructedDirection}");
                }
            }
        }
    }

    [Fact]
    public void SolveLookOrientation_TargetBeyondPitchClamp_ClampsGracefully(){
        // Pitch=89°は可動域(-80°〜40°)の外。到達不能な目標でも有限値を返しクランプされる
        // ことを確認する(既存のApplyLookDeltaと同じ黙示クランプでの劣化)。
        var TargetDirection=SceneViewCamera.GetForward(Yaw:0f,Pitch:89f*(float)(Math.PI/180.0));
        Vector3 LocalAxes=new(0f,0f,1f); // 画面中央(ローカルForward軸そのもの)。

        (var Yaw,var Pitch)=SceneViewCamera.SolveLookOrientation(LocalAxes,TargetDirection,0f,0f);

        Assert.False(float.IsNaN(Yaw)||float.IsInfinity(Yaw));
        Assert.True(Pitch<=SceneViewCamera.MaxPitchRadians+1e-3f);
        Assert.True(Pitch>=SceneViewCamera.MinPitchRadians-1e-3f);
    }

    /// <summary>
    /// Pitchの解は<c>Phi±acos(...)</c>で2通り存在しうる。ドラッグ中の連続性を保つため、
    /// 直前のPitchに角度的に近い方を選ぶ。
    /// </summary>
    [Fact]
    public void SolveLookOrientation_PicksPitchBranchClosestToPrevious_ForContinuity(){
        // Ly=1,Lz=0 → Phi=atan2(0,1)=0。Dy=cos(0.5)=0.87758 → 候補は ±0.5 rad(共に可動域内)。
        Vector3 LocalAxes=new(0f,1f,0f);
        Vector3 TargetDirection=new(0f,0.87758f,1f);

        (_,var PitchNearPositive)=SceneViewCamera.SolveLookOrientation(
                                                                       LocalAxes,TargetDirection,PreviousYaw:0f,PreviousPitch:0.5f);
        Assert.Equal(0.5f,PitchNearPositive,3);

        (_,var PitchNearNegative)=SceneViewCamera.SolveLookOrientation(
                                                                       LocalAxes,TargetDirection,PreviousYaw:0f,PreviousPitch:-0.5f);
        Assert.Equal(-0.5f,PitchNearNegative,3);
    }

    [Fact]
    public void SolveLookOrientation_DegenerateLocalRay_KeepsPreviousOrientation(){
        // Ly=Lz=0(Rがほぼ0、ローカルレイがほぼFlatRight軸)→解けないので直前の向きを維持する。
        Vector3 LocalAxes=new(1f,0f,0f);
        Vector3 TargetDirection=new(0f,0f,1f);

        (var Yaw,var Pitch)=SceneViewCamera.SolveLookOrientation(
                                                                 LocalAxes,TargetDirection,PreviousYaw:0.42f,PreviousPitch:0.17f);

        Assert.Equal(0.42f,Yaw,4);
        Assert.Equal(0.17f,Pitch,4);
    }

    /// <summary>
    /// アンカーはFocusPositionから明確に離れた実在の点にする — 近すぎる(FocusPositionにほぼ
    /// 一致する)とスクリーン位置のYaw/Pitchに対する感度がほぼ0になり、有限差分ヤコビアンが
    /// 数値的に不安定になる(実際に最初これで失敗し、Pitchが可動域の端まで暴走した。退化ケース
    /// 自体は<see cref="SceneViewCamera.ComputeOrbitAnchorFallbackPoint"/>側の話であり、
    /// このテストの対象ではない)。目標は実際のドラッグ1フレーム分に相当する小さな回転先で
    /// アンカーが実際に映る位置にする。
    /// </summary>
    [Fact]
    public void TrySolveOrbitYawPitch_WellConditionedCase_ReprojectsAnchorToTargetPixel(){
        var FocusPosition=Vector3.Zero;
        const float FocusDistance=10f;
        var Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),800f/600f,0.1f,1000f);
        const float SeedYaw=0.2f;
        const float SeedPitch=0.1f;
        var AnchorWorldPoint=FocusPosition+new Vector3(3f,1.5f,0f);

        const float TargetYaw=0.23f;
        const float TargetPitch=0.14f;
        var TargetView=SceneViewCamera.GetView(FocusPosition,TargetYaw,TargetPitch,FocusDistance);
        var TargetNdc=ProjectToNdc(AnchorWorldPoint,TargetView*Projection);
        var TargetScreenX=(TargetNdc.X+1f)*0.5f*800f;
        var TargetScreenY=(1f-TargetNdc.Y)*0.5f*600f;

        var Converged=SceneViewCamera.TrySolveOrbitYawPitch(
                                                            FocusPosition,FocusDistance,Projection,AnchorWorldPoint,
                                                            TargetScreenX,TargetScreenY,800f,600f,SeedYaw,SeedPitch,
                                                            out var SolvedYaw,out var SolvedPitch);

        var SolvedView=SceneViewCamera.GetView(FocusPosition,SolvedYaw,SolvedPitch,FocusDistance);
        var Ndc=ProjectToNdc(AnchorWorldPoint,SolvedView*Projection);
        var ScreenX=(Ndc.X+1f)*0.5f*800f;
        var ScreenY=(1f-Ndc.Y)*0.5f*600f;

        Assert.True(Converged,$"ScreenX={ScreenX} ScreenY={ScreenY} SolvedYaw={SolvedYaw} SolvedPitch={SolvedPitch}");
        Assert.Equal(TargetScreenX,ScreenX,1);
        Assert.Equal(TargetScreenY,ScreenY,1);
    }

    /// <summary>
    /// OrbitはEye自体が動くため、Look側のような閉形式の保証(常に厳密一致)は無い —
    /// クリック位置とドラッグ量によっては解の枝が折り返し、シードから大きく動けないことがある
    /// (バグではなく構造的な性質、<see cref="SceneViewCamera.TrySolveOrbitYawPitch"/>のドキュメント
    /// 参照)。このテストは「そういう場合でも悪化はさせない」というLevenberg–Marquardtの保証だけを
    /// 固定する。<b>収束しないケースへの対処として反復回数を増やす方向へ直そうとしないこと</b> —
    /// このセッションで一度、左右手系の食い違いを同様に「バグ」と誤診して改悪した前例がある。
    /// </summary>
    [Fact]
    public void TrySolveOrbitYawPitch_NeverIncreasesResidualBeyondTheSeed(){
        var FocusPosition=Vector3.Zero;
        const float FocusDistance=3f; // 近い注視距離+大きなドラッグで枝の折り返しが起こりやすい設定。
        var Projection=SceneViewCamera.GetProjection((float)(Math.PI/3.0),800f/600f,0.1f,1000f);
        const float SeedYaw=0f;
        const float SeedPitch=0f;

        // アンカーはシード視点で画面端(50,50)に実際に映っている点にする(SeedResidualを
        // 意味のある有限値にするため)。
        var SeedView=SceneViewCamera.GetView(FocusPosition,SeedYaw,SeedPitch,FocusDistance);
        (var Origin,var Direction)=SceneViewCamera.ScreenPointToRay(SeedView,Projection,50f,50f,800f,600f);
        var AnchorWorldPoint=Origin+(Direction*8f);

        // 目標は反対側の隅。ドラッグ量が大きいほどOrbitの折り返しに入りやすい。
        const float TargetScreenX=750f;
        const float TargetScreenY=550f;

        var SeedResidual=ComputeScreenResidual(
                                               AnchorWorldPoint,SeedView,Projection,TargetScreenX,TargetScreenY,800f,600f);

        SceneViewCamera.TrySolveOrbitYawPitch(
            FocusPosition,FocusDistance,Projection,AnchorWorldPoint,
            TargetScreenX,TargetScreenY,800f,600f,SeedYaw,SeedPitch,
            out var SolvedYaw,out var SolvedPitch);

        var SolvedView=SceneViewCamera.GetView(FocusPosition,SolvedYaw,SolvedPitch,FocusDistance);
        var SolvedResidual=ComputeScreenResidual(
                                                 AnchorWorldPoint,SolvedView,Projection,TargetScreenX,TargetScreenY,800f,600f);

        Assert.True(
            SolvedResidual<=SeedResidual+1e-2f,
            $"seed残差{SeedResidual}よりsolved残差{SolvedResidual}が大きい — "
            +"Levenberg–Marquardtは残差を悪化させないはずであり、折り返し領域の扱いを崩す変更が入った兆候");
    }

    [Fact]
    public void TrySolveOrbitYawPitch_ClampsPitchToOrbitRange(){
        var FocusPosition=Vector3.Zero;
        const float FocusDistance=5f;
        var Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),800f/600f,0.1f,1000f);

        // 真上から見下ろすような、Pitch可動域(-80°〜40°)を大きく超えた目標を要求する。
        SceneViewCamera.TrySolveOrbitYawPitch(
            FocusPosition,FocusDistance,Projection,AnchorWorldPoint:new Vector3(0f,10f,0.01f),
            TargetScreenX:400f,TargetScreenY:10f,ViewportWidth:800f,ViewportHeight:600f,
            SeedYaw:0f,SeedPitch:0f,out var Yaw,out var Pitch);

        Assert.False(float.IsNaN(Yaw)||float.IsInfinity(Yaw));
        Assert.True(Pitch<=SceneViewCamera.MaxPitchRadians+1e-4f);
        Assert.True(Pitch>=SceneViewCamera.MinPitchRadians-1e-4f);
    }

    [Fact]
    public void ComputeOrbitAnchorFallbackPoint_RayThroughFocusPlane_ReturnsPlaneIntersection(){
        Vector3 FocusPosition=new(0f,0f,10f);
        var Forward=Vector3.UnitZ;
        Vector3 RayOrigin=new(2f,3f,0f);
        var RayDirection=Vector3.UnitZ; // Forwardと平行なレイ、平面Z=10と交わる。

        var Result=SceneViewCamera.ComputeOrbitAnchorFallbackPoint(RayOrigin,RayDirection,FocusPosition,Forward);

        AssertApprox(new Vector3(2f,3f,10f),Result);
    }

    [Fact]
    public void ComputeOrbitAnchorFallbackPoint_RayParallelToPlane_ReturnsFocusPosition(){
        Vector3 FocusPosition=new(0f,0f,10f);
        var Forward=Vector3.UnitZ;
        Vector3 RayOrigin=new(2f,3f,0f);
        var RayDirection=Vector3.UnitX; // 平面(法線Forward)と平行。

        var Result=SceneViewCamera.ComputeOrbitAnchorFallbackPoint(RayOrigin,RayDirection,FocusPosition,Forward);

        AssertApprox(FocusPosition,Result);
    }

    private static float ComputeScreenResidual(
        Vector3 WorldPoint,Matrix4x4 View,Matrix4x4 Projection,
        float TargetX,float TargetY,float Width,float Height){
        var Clip=Vector4.Transform(new Vector4(WorldPoint,1f),View*Projection);
        if(Clip.W<=1e-5f){
            return float.PositiveInfinity;
        }

        var ScreenX=(Clip.X/Clip.W+1f)*0.5f*Width;
        var ScreenY=(1f-Clip.Y/Clip.W)*0.5f*Height;
        return(float)Math.Sqrt(((ScreenX-TargetX)*(ScreenX-TargetX))+((ScreenY-TargetY)*(ScreenY-TargetY)));
    }

    private static Vector3 ProjectToNdc(Vector3 World,Matrix4x4 ViewProjection){
        var Clip=Vector4.Transform(new Vector4(World,1f),ViewProjection);
        return new Vector3(Clip.X,Clip.Y,Clip.Z)/Clip.W;
    }

    private static void AssertApprox(Vector3 Expected,Vector3 Actual){
        Assert.Equal(Expected.X,Actual.X,Precision);
        Assert.Equal(Expected.Y,Actual.Y,Precision);
        Assert.Equal(Expected.Z,Actual.Z,Precision);
    }
}
