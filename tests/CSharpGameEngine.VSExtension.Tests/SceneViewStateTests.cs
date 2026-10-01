using System;
using System.Numerics;
using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneViewState"/>の概念操作(Look/Fly/Orbit/Pan/Dolly/Zoom/AdjustNavigationSpeed)を
/// 検証する。永続化を伴う<see cref="SceneViewState.CreateFromSettings"/>/<c>SaveToSettings</c>は
/// <see cref="MockedVsFixture"/>経由でVS SDKのUIスレッド判定を満たす。
/// </summary>
[Collection(MockedVsCollection.Name)]
public sealed class SceneViewStateTests {
    private const int Precision=3;

    private readonly FakeWritableSettingsStore Store=new();

    public SceneViewStateTests(MockedVsFixture Fixture)=>
        SceneEditorSettings.ConfigureStoreProvider(()=> this.Store);

    /// <summary>
    /// M0030 / M0020残課題R1。ドラッグ操作中に実際に使われるのは<see cref="SceneCameraFraming"/>ではなく
    /// こちらの投影なので、far が <c>FocusDistance*20</c> まで伸びる実運用値で比率を固定する。
    /// </summary>
    [Theory]
    [InlineData(5f,1f)]
    [InlineData(60f,50f)]
    [InlineData(500f,400f)]
    public void GetProjection_Perspective_KeepsTheDepthRatioWithinTheBudget(
        float FocusDistance,float BoundsRadius){
        SceneViewState State=new(){
            FocusDistance=FocusDistance,LastKnownBoundsRadius=BoundsRadius,
            ProjectionMode=SceneProjectionMode.Perspective };

        (var Near,var Far)=SceneCameraFramingTests.ReadPerspectiveDepthRange(State.GetProjection(1.6f));

        Assert.True(Near>0f,$"near {Near} must be positive");
        Assert.True(Near<Far,$"near {Near} must stay in front of far {Far}");
        Assert.True(
            Far/Near<=SceneCameraFraming.MaxDepthRatio*1.01f,
            $"depth ratio {Far/Near} exceeded the {SceneCameraFraming.MaxDepthRatio} budget");
    }

    /// <summary>正射投影の深度は線形なので near を遠ざける理由がない。手前を切らないことを固定する。</summary>
    [Fact]
    public void GetProjection_Orthographic_KeepsTheBaseNearPlane(){
        SceneViewState State=new(){
            FocusDistance=500f,LastKnownBoundsRadius=400f,OrthographicSize=20f,
            ProjectionMode=SceneProjectionMode.Orthographic };

        var Projection=State.GetProjection(1.6f);

        // CreateOrthographic: M33=1/(near-far)、M43=near/(near-far)。
        var Near=Projection.M43/Projection.M33;
        Assert.Equal(SceneCameraFraming.BaseNearPlane,Near,Precision);
    }

    [Fact]
    public void Fly_LookOnly_KeepsEyeFixed_WhileYawPitchChange(){
        SceneViewState State=new(){ FocusPosition=new Vector3(1f,2f,3f),FocusDistance=5f,Yaw=0.2f,Pitch=0.1f };
        var EyeBefore=State.GetEyePosition();

        State.TickFly([],DeltaX:15f,DeltaY:-8f,DeltaTime:1f,Shift:false,Ctrl:false);

        var EyeAfter=State.GetEyePosition();
        Assert.Equal(EyeBefore.X,EyeAfter.X,Precision);
        Assert.Equal(EyeBefore.Y,EyeAfter.Y,Precision);
        Assert.Equal(EyeBefore.Z,EyeAfter.Z,Precision);
    }

    [Fact]
    public void Fly_WKeyHeld_MovesAlongFullForwardIncludingPitch(){
        SceneViewState State=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Yaw=0f,Pitch=0.4f };
        var EyeBefore=State.GetEyePosition();

        State.TickFly([NavigationKey.W],DeltaX:0f,DeltaY:0f,DeltaTime:1f,Shift:false,Ctrl:false);

        var EyeAfter=State.GetEyePosition();
        var Moved=EyeAfter-EyeBefore;
        var ExpectedDirection=SceneViewCamera.GetForward(0f,0.4f);
        Assert.True(Moved.Length()>0f);
        var ActualDirection=Vector3.Normalize(Moved);
        Assert.Equal(ExpectedDirection.X,ActualDirection.X,Precision);
        Assert.Equal(ExpectedDirection.Y,ActualDirection.Y,Precision);
        Assert.Equal(ExpectedDirection.Z,ActualDirection.Z,Precision);
    }

    [Fact]
    public void Fly_QAndEKeys_MoveAlongWorldUpRegardlessOfPitch(){
        SceneViewState StateUp=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Pitch=0.7f };
        var EyeBeforeUp=StateUp.GetEyePosition();
        StateUp.TickFly([NavigationKey.E],0f,0f,1f,false,false);
        var UpMove=StateUp.GetEyePosition()-EyeBeforeUp;

        SceneViewState StateDown=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Pitch=0.7f };
        var EyeBeforeDown=StateDown.GetEyePosition();
        StateDown.TickFly([NavigationKey.Q],0f,0f,1f,false,false);
        var DownMove=StateDown.GetEyePosition()-EyeBeforeDown;

        Assert.True(UpMove.Y>0f);
        Assert.Equal(0f,UpMove.X,Precision);
        Assert.Equal(0f,UpMove.Z,Precision);
        Assert.True(DownMove.Y<0f);
    }

    [Fact]
    public void Fly_SpeedScalesWithFocusDistance(){
        SceneViewState Near=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        var NearEyeBefore=Near.GetEyePosition();
        Near.TickFly([NavigationKey.W],0f,0f,1f,false,false);
        var NearMoved=(Near.GetEyePosition()-NearEyeBefore).Length();

        SceneViewState Far=new(){ FocusPosition=Vector3.Zero,FocusDistance=10f };
        var FarEyeBefore=Far.GetEyePosition();
        Far.TickFly([NavigationKey.W],0f,0f,1f,false,false);
        var FarMoved=(Far.GetEyePosition()-FarEyeBefore).Length();

        Assert.Equal(10f,FarMoved/NearMoved,Precision);
    }

    [Fact]
    public void Fly_ShiftMultipliesSpeedByFour(){
        SceneViewState Normal=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        var NormalBefore=Normal.GetEyePosition();
        Normal.TickFly([NavigationKey.W],0f,0f,1f,Shift:false,Ctrl:false);
        var NormalMoved=(Normal.GetEyePosition()-NormalBefore).Length();

        SceneViewState Fast=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        var FastBefore=Fast.GetEyePosition();
        Fast.TickFly([NavigationKey.W],0f,0f,1f,Shift:true,Ctrl:false);
        var FastMoved=(Fast.GetEyePosition()-FastBefore).Length();

        Assert.Equal(4f,FastMoved/NormalMoved,Precision);
    }

    [Fact]
    public void Fly_CtrlMultipliesSpeedByQuarter(){
        SceneViewState Normal=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        var NormalBefore=Normal.GetEyePosition();
        Normal.TickFly([NavigationKey.W],0f,0f,1f,Shift:false,Ctrl:false);
        var NormalMoved=(Normal.GetEyePosition()-NormalBefore).Length();

        SceneViewState Precise=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        var PreciseBefore=Precise.GetEyePosition();
        Precise.TickFly([NavigationKey.W],0f,0f,1f,Shift:false,Ctrl:true);
        var PreciseMoved=(Precise.GetEyePosition()-PreciseBefore).Length();

        Assert.Equal(0.25f,PreciseMoved/NormalMoved,Precision);
    }

    [Fact]
    public void FocusOnBounds_ChangesFocusPositionAndDistance_PreservesYawPitch(){
        SceneViewState State=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Yaw=0.5f,Pitch=0.3f };

        State.FocusOnBounds(new Vector3(5f,6f,7f),BoundsRadius:2f,AspectRatio:1f);

        Assert.Equal(new Vector3(5f,6f,7f),State.FocusPosition);
        // ComputeDistance本体(初期シーンフレーミング用、1.1倍余白)に加えて、単一オブジェクトへの
        // FocusSelectionが窮屈になりすぎないよう追加の余白(1.7倍)がかかる(実機検証で発見)。
        Assert.Equal(
            SceneCameraFraming.ComputeDistance(2f,State.FovYRadians,AspectRatio:1f)*1.7f,
            State.FocusDistance,
            Precision);
        Assert.Equal(0.5f,State.Yaw,Precision);
        Assert.Equal(0.3f,State.Pitch,Precision);
    }

    [Fact]
    public void FocusOnBounds_DoesNotChangeLastKnownBoundsRadius(){
        SceneViewState State=new(){ LastKnownBoundsRadius=50f };

        State.FocusOnBounds(Vector3.One,BoundsRadius:2f,AspectRatio:1f);

        Assert.Equal(50f,State.LastKnownBoundsRadius);
    }

    [Fact]
    public void Orbit_ChangesYawPitch_FocusPositionUnchanged(){
        SceneViewState State=new(){ FocusPosition=new Vector3(3f,4f,5f),FocusDistance=6f,Yaw=0.1f,Pitch=0.1f };
        var FocusBefore=State.FocusPosition;

        State.ApplyOrbit(20f,10f);

        Assert.Equal(FocusBefore,State.FocusPosition);
        Assert.NotEqual(0.1f,State.Yaw);
    }

    /// <summary>
    /// 反転スイッチがすべてオフのときの基準: RMB/Alt+RMBのLookは右ドラッグでYawが減り、下ドラッグでPitchが
    /// 下がる。Alt+LMBのOrbitも同じ向きになる(M0096: 垂直はユーザーの依頼で、それまでチェックを入れたときの
    /// 向きを既定にした)。
    /// </summary>
    [Fact]
    public void Look_WithSwitchesOff_UsesTheConfirmedBaselineDirections(){
        SceneViewState Right=new(){ Yaw=0f,Pitch=0f };
        Right.TickFly([],DeltaX:10f,DeltaY:0f,DeltaTime:0f,Shift:false,Ctrl:false);
        Assert.True(Right.Yaw<0f,$"yaw {Right.Yaw}");

        SceneViewState Down=new(){ Yaw=0f,Pitch=0f };
        Down.TickFly([],DeltaX:0f,DeltaY:10f,DeltaTime:0f,Shift:false,Ctrl:false);
        Assert.True(Down.Pitch<0f,$"pitch {Down.Pitch}");
    }

    [Fact]
    public void Orbit_WithSwitchesOff_UsesTheSameBaselineDirectionsAsLook(){
        SceneViewState Right=new(){ Yaw=0f,Pitch=0f };
        Right.ApplyOrbit(10f,0f);
        Assert.True(Right.Yaw<0f,$"yaw {Right.Yaw}");

        SceneViewState Down=new(){ Yaw=0f,Pitch=0f };
        Down.ApplyOrbit(0f,10f);
        Assert.True(Down.Pitch<0f,$"pitch {Down.Pitch}");
    }

    [Fact]
    public void Look_InvertSwitches_NegateTheBaselineDeltas(){
        SceneViewState Normal=new();
        Normal.TickFly([],10f,6f,0f,false,false);
        SceneViewState Inverted=new(){ InvertLookX=true,InvertLookY=true };
        Inverted.TickFly([],10f,6f,0f,false,false);
        Assert.Equal(-Normal.Yaw,Inverted.Yaw,Precision);
        Assert.Equal(-Normal.Pitch,Inverted.Pitch,Precision);
    }

    /// <summary>ピクセル完全一致ドラッグは「右ドラッグでYaw増・下ドラッグでPitch増」の向きのときだけ成立する。</summary>
    [Fact]
    public void LookUsesPixelLock_OnlyForTheGrabTheWorldDirections(){
        Assert.False(new SceneViewState().LookUsesPixelLock);
        Assert.False(new SceneViewState(){ InvertLookX=true }.LookUsesPixelLock);
        Assert.False(new SceneViewState(){ InvertLookY=true }.LookUsesPixelLock);
        Assert.True(new SceneViewState(){ InvertLookX=true,InvertLookY=true }.LookUsesPixelLock);
    }

    /// <summary>OrbitはこれまでInvertX:false,InvertY:false固定だった。M0020でLookと同じく独立して反転可能にした。</summary>
    [Fact]
    public void Orbit_InvertOrbitX_NegatesYawDelta(){
        SceneViewState Normal=new(){ InvertOrbitX=false };
        Normal.ApplyOrbit(10f,0f);

        SceneViewState Inverted=new(){ InvertOrbitX=true };
        Inverted.ApplyOrbit(10f,0f);

        Assert.Equal(-Normal.Yaw,Inverted.Yaw,Precision);
    }

    [Fact]
    public void Orbit_InvertOrbitY_NegatesPitchDelta(){
        SceneViewState Normal=new(){ InvertOrbitY=false };
        Normal.ApplyOrbit(0f,10f);

        SceneViewState Inverted=new(){ InvertOrbitY=true };
        Inverted.ApplyOrbit(0f,10f);

        Assert.Equal(-Normal.Pitch,Inverted.Pitch,Precision);
    }

    [Fact]
    public void Pan_TranslatesFocusAndEyeBySameDelta_ViewDirectionUnchanged(){
        SceneViewState State=new(){ FocusPosition=new Vector3(1f,2f,3f),FocusDistance=4f,Yaw=0.3f,Pitch=0.2f };
        var FocusBefore=State.FocusPosition;
        var EyeBefore=State.GetEyePosition();

        State.ApplyPan(30f,-15f);

        var FocusDelta=State.FocusPosition-FocusBefore;
        var EyeDelta=State.GetEyePosition()-EyeBefore;
        Assert.Equal(FocusDelta.X,EyeDelta.X,Precision);
        Assert.Equal(FocusDelta.Y,EyeDelta.Y,Precision);
        Assert.Equal(FocusDelta.Z,EyeDelta.Z,Precision);
        Assert.True(FocusDelta.Length()>0f);
    }

    [Fact]
    public void FollowViewMove_AppliesTotalObjectDeltaFromCapturedViewAndCanRestore(){
        SceneViewState State=new(){ FocusPosition=new Vector3(1f,2f,3f),FocusDistance=4f,Yaw=0.3f,Pitch=0.2f };
        var Snapshot=SceneViewFollowSnapshot.Capture(State);
        var EyeBefore=State.GetEyePosition();

        Snapshot.ApplyMove(State,new Vector3(5f,-2f,1f));

        Assert.Equal(new Vector3(6f,0f,4f),State.FocusPosition);
        var MovedEye=State.GetEyePosition();
        var ExpectedMovedEye=EyeBefore+new Vector3(5f,-2f,1f);
        Assert.Equal(ExpectedMovedEye.X,MovedEye.X,5);
        Assert.Equal(ExpectedMovedEye.Y,MovedEye.Y,5);
        Assert.Equal(ExpectedMovedEye.Z,MovedEye.Z,5);
        Snapshot.Restore(State);
        Assert.Equal(new Vector3(1f,2f,3f),State.FocusPosition);
        var RestoredEye=State.GetEyePosition();
        Assert.Equal(EyeBefore.X,RestoredEye.X,5);
        Assert.Equal(EyeBefore.Y,RestoredEye.Y,5);
        Assert.Equal(EyeBefore.Z,RestoredEye.Z,5);
    }

    [Fact]
    public void FollowViewRotate_RotatesEyeAndForwardAroundSharedPivot(){
        SceneViewState State=new(){ FocusPosition=Vector3.Zero,FocusDistance=2f,Yaw=0f,Pitch=0f };
        var Snapshot=SceneViewFollowSnapshot.Capture(State);
        var EyeBefore=State.GetEyePosition();
        var Delta=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(Math.PI/2d));

        Snapshot.ApplyRotation(State,Vector3.Zero,Delta);

        var ExpectedEye=Vector3.Transform(EyeBefore,Delta);
        Assert.Equal(ExpectedEye.X,State.GetEyePosition().X,3);
        Assert.Equal(ExpectedEye.Y,State.GetEyePosition().Y,3);
        Assert.Equal(ExpectedEye.Z,State.GetEyePosition().Z,3);
        var ExpectedForward=Vector3.Transform(Vector3.UnitZ,Delta);
        var ActualForward=SceneViewCamera.GetForward(State.Yaw,State.Pitch);
        Assert.Equal(ExpectedForward.X,ActualForward.X,3);
        Assert.Equal(ExpectedForward.Y,ActualForward.Y,3);
        Assert.Equal(ExpectedForward.Z,ActualForward.Z,3);
    }

    [Fact]
    public void Dolly_MultipliesFocusDistanceByPointNineToThePowerOfNotches(){
        SceneViewState State=new(){ FocusDistance=10f };

        State.ApplyDolly(3);

        Assert.Equal(10f*(float)Math.Pow(0.9,3),State.FocusDistance,Precision);
    }

    [Fact]
    public void Dolly_ClampsToMinimumFocusDistance(){
        SceneViewState State=new(){ FocusDistance=0.001f };

        State.ApplyDolly(50);

        Assert.Equal(0.01f,State.FocusDistance,Precision);
    }

    [Fact]
    public void Zoom_MultipliesOrthographicSizeSameFormulaAsDolly(){
        SceneViewState State=new(){ OrthographicSize=10f };

        State.ApplyZoom(2);

        Assert.Equal(10f*(float)Math.Pow(0.9,2),State.OrthographicSize,Precision);
    }

    [Fact]
    public void AdjustNavigationSpeed_MultipliesByOnePointTwoToThePowerOfNotches(){
        SceneViewState State=new(){ NavigationSpeedMultiplier=1f };

        State.ApplyNavigationSpeedAdjust(2);

        Assert.Equal((float)Math.Pow(1.2,2),State.NavigationSpeedMultiplier,Precision);
    }

    [Fact]
    public void AdjustNavigationSpeed_ClampsToConfiguredBounds(){
        SceneViewState High=new(){ NavigationSpeedMultiplier=1f };
        High.ApplyNavigationSpeedAdjust(100);
        Assert.Equal(20f,High.NavigationSpeedMultiplier,Precision);

        SceneViewState Low=new(){ NavigationSpeedMultiplier=1f };
        Low.ApplyNavigationSpeedAdjust(-100);
        Assert.Equal(0.05f,Low.NavigationSpeedMultiplier,Precision);
    }

    [Fact]
    public void ResetToBounds_YawPitchMatchesLegacyCreateBoundsFitDerivation(){
        SceneViewState State=new();
        Vector3 Center=new(2f,3f,4f);
        const float Radius=8f;

        State.ResetToBounds(
            Center,Radius,SceneCameraFraming.ViewDirection,SceneCameraFraming.DefaultFovYRadians,AspectRatio:1f);

        Assert.Equal(Center,State.FocusPosition);
        var Forward=SceneViewCamera.GetForward(State.Yaw,State.Pitch);
        var ExpectedForward=-SceneCameraFraming.ViewDirection;
        Assert.Equal(ExpectedForward.X,Forward.X,Precision);
        Assert.Equal(ExpectedForward.Y,Forward.Y,Precision);
        Assert.Equal(ExpectedForward.Z,Forward.Z,Precision);
    }

    [Fact]
    public void GetProjection_Perspective_FarScalesWithFocusDistanceButFloorsAtBoundsRadiusTimesTwenty(){
        SceneViewState State=new(){ FocusDistance=1f,LastKnownBoundsRadius=50f };

        var Projection=State.GetProjection(1f);

        // Far=max(1*20, 50*20)=1000。境界球フロアが効いていることを間接的に確認する
        // (境界球が無い場合は far=20 になり、近い場所のオブジェクトがクリップされてしまう)。
        Assert.False(float.IsNaN(Projection.M33));
    }

    [Fact]
    public void CreateFromSettings_ReadsAllSevenPersistedFields()=>
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SceneEditorSettings.WriteTransformSpace(SceneTransformSpace.Local);
            SceneEditorSettings.WriteNavigationSpeedMultiplier(3f);

            var State=SceneViewState.CreateFromSettings();

            Assert.Equal(SceneTransformSpace.Local,State.TransformSpace);
            Assert.Equal(3f,State.NavigationSpeedMultiplier);
        });

    [Fact]
    public void SaveToSettings_ThenCreateFromSettings_RoundTrips()=>
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SceneViewState Original=new(){
                TransformSpace=SceneTransformSpace.Local,InvertLookY=true,InvertOrbitX=true,InvertOrbitY=true,
            };

            Original.SaveToSettings();
            var Restored=SceneViewState.CreateFromSettings();

            Assert.Equal(SceneTransformSpace.Local,Restored.TransformSpace);
            Assert.True(Restored.InvertLookY);
            Assert.True(Restored.InvertOrbitX);
            Assert.True(Restored.InvertOrbitY);
        });

    // ----- M0020追加: RMB Look / Alt+LMB Orbit のピクセル完全一致ドラッグ -----

    [Fact]
    public void ApplyLookPixelLock_SetsYawPitchToSolvedValue(){
        // 画面中央(ローカルaxes=(0,0,1))を注視方向Dwへ厳密に合わせる、最も単純な形。
        var AnchorWorldDirection=SceneViewCamera.GetForward(Yaw:0.5f,Pitch:0.2f);
        SceneViewState State=new(){ Yaw=0f,Pitch=0f };

        State.ApplyLookPixelLock(AnchorWorldDirection,CurrentLocalAxes:new Vector3(0f,0f,1f));

        Assert.Equal(0.5f,State.Yaw,Precision);
        Assert.Equal(0.2f,State.Pitch,Precision);
    }

    [Fact]
    public void Orbit_ZeroDeltaDoesNotChangeCamera(){
        SceneViewState State=new(){
            FocusPosition=new Vector3(1f,2f,3f),FocusDistance=10f,Yaw=0.2f,Pitch=0.1f,
        };
        var FocusBefore=State.FocusPosition;
        // シードそのままの位置(=アンカーが既に映っている画面座標)を目標にする — Orbitの
        // 不変条件(FocusPositionは変更しない)だけを確認したいので、解自体が難しいケースを
        // 選ぶ必要はない。
        State.ApplyOrbit(0f,0f);

        Assert.Equal(FocusBefore,State.FocusPosition);
        Assert.Equal(0.2f,State.Yaw,Precision);
        Assert.Equal(0.1f,State.Pitch,Precision);
    }

    /// <summary>
    /// ピクセル完全一致中は回転が既にムーブイベントごとに確定済みなので、
    /// <see cref="SceneViewState.TickFlyMovementOnly"/>はWASDQEの移動だけを適用する。
    /// 同じYaw/Pitch/HeldKeysであれば<see cref="SceneViewState.TickFly"/>の移動結果(DeltaX/DeltaY=0で
    /// 呼んだ場合、つまり回転を伴わない場合)と一致するはずで、抽出リファクタが挙動を変えていないことを
    /// 固定する。
    /// </summary>
    [Fact]
    public void TickFlyMovementOnly_MovesEyeIdenticallyToTickFlyWasdqe_GivenSameYawPitch(){
        SceneViewState ViaTickFly=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Yaw=0.3f,Pitch=0.2f };
        ViaTickFly.TickFly([NavigationKey.W,NavigationKey.D],DeltaX:0f,DeltaY:0f,DeltaTime:1f,Shift:false,Ctrl:false);

        SceneViewState ViaMovementOnly=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Yaw=0.3f,Pitch=0.2f };
        ViaMovementOnly.TickFlyMovementOnly([NavigationKey.W,NavigationKey.D],DeltaTime:1f,Shift:false,Ctrl:false);

        Assert.Equal(ViaTickFly.FocusPosition.X,ViaMovementOnly.FocusPosition.X,Precision);
        Assert.Equal(ViaTickFly.FocusPosition.Y,ViaMovementOnly.FocusPosition.Y,Precision);
        Assert.Equal(ViaTickFly.FocusPosition.Z,ViaMovementOnly.FocusPosition.Z,Precision);
        Assert.Equal(ViaTickFly.Yaw,ViaMovementOnly.Yaw,Precision);
        Assert.Equal(ViaTickFly.Pitch,ViaMovementOnly.Pitch,Precision);
    }

    /// <summary>
    /// M0095 (user request 2026-10-01): Alt+LMB orbit around the selection keeps the selection's center
    /// where it is on screen, even off the view's center, and at the same distance, so a sphere there
    /// draws exactly the same while the camera goes around it. The turn equals the plain orbit's.
    /// </summary>
    [Theory]
    [InlineData(40f,0f)]
    [InlineData(0f,-30f)]
    [InlineData(-25f,35f)]
    [InlineData(0f,900f)]
    public void OrbitAroundAPivot_KeepsThePivotsScreenPointAndDistance(float DeltaX,float DeltaY){
        SceneViewState State=new(){ FocusPosition=new Vector3(0f,0.5f,0f),FocusDistance=8f,Yaw=0.4f,Pitch=-0.3f };
        SceneViewState Plain=new(){ FocusPosition=State.FocusPosition,FocusDistance=State.FocusDistance,Yaw=State.Yaw,Pitch=State.Pitch };
        var Pivot=new Vector3(2.5f,1f,-1.5f);
        var Before=Project(State,Pivot);
        var DistanceBefore=Vector3.Distance(State.GetEyePosition(),Pivot);
        Assert.True(Math.Abs(Before.X)>0.1f||Math.Abs(Before.Y)>0.1f,"the pivot must start off the view's center");

        State.ApplyOrbit(DeltaX,DeltaY,Pivot);
        Plain.ApplyOrbit(DeltaX,DeltaY);

        var After=Project(State,Pivot);
        Assert.Equal(Before.X,After.X,4);
        Assert.Equal(Before.Y,After.Y,4);
        Assert.Equal(DistanceBefore,Vector3.Distance(State.GetEyePosition(),Pivot),4);
        Assert.Equal(Plain.Yaw,State.Yaw,5);
        Assert.Equal(Plain.Pitch,State.Pitch,5);
        Assert.Equal(8f,State.FocusDistance,5);
    }

    private static Vector2 Project(SceneViewState State,Vector3 Point){
        var Clip=Vector4.Transform(new Vector4(Point,1f),State.GetView()*State.GetProjection(16f/9f));
        return new Vector2(Clip.X/Clip.W,Clip.Y/Clip.W);
    }
}
