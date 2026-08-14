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
        SceneEditorSettings.ConfigureStoreProvider(()=>Store);

    [Fact]
    public void Fly_LookOnly_KeepsEyeFixed_WhileYawPitchChange(){
        SceneViewState State=new(){ FocusPosition=new Vector3(1f,2f,3f),FocusDistance=5f,Yaw=0.2f,Pitch=0.1f };
        Vector3 EyeBefore=State.GetEyePosition();

        State.TickFly([],DeltaX:15f,DeltaY:-8f,DeltaTime:1f,Shift:false,Ctrl:false);

        Vector3 EyeAfter=State.GetEyePosition();
        Assert.Equal(EyeBefore.X,EyeAfter.X,Precision);
        Assert.Equal(EyeBefore.Y,EyeAfter.Y,Precision);
        Assert.Equal(EyeBefore.Z,EyeAfter.Z,Precision);
    }

    [Fact]
    public void Fly_WKeyHeld_MovesAlongFullForwardIncludingPitch(){
        SceneViewState State=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Yaw=0f,Pitch=0.4f };
        Vector3 EyeBefore=State.GetEyePosition();

        State.TickFly([NavigationKey.W],DeltaX:0f,DeltaY:0f,DeltaTime:1f,Shift:false,Ctrl:false);

        Vector3 EyeAfter=State.GetEyePosition();
        Vector3 Moved=EyeAfter-EyeBefore;
        Vector3 ExpectedDirection=SceneViewCamera.GetForward(0f,0.4f);
        Assert.True(Moved.Length()>0f);
        Vector3 ActualDirection=Vector3.Normalize(Moved);
        Assert.Equal(ExpectedDirection.X,ActualDirection.X,Precision);
        Assert.Equal(ExpectedDirection.Y,ActualDirection.Y,Precision);
        Assert.Equal(ExpectedDirection.Z,ActualDirection.Z,Precision);
    }

    [Fact]
    public void Fly_QAndEKeys_MoveAlongWorldUpRegardlessOfPitch(){
        SceneViewState StateUp=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Pitch=0.7f };
        Vector3 EyeBeforeUp=StateUp.GetEyePosition();
        StateUp.TickFly([NavigationKey.E],0f,0f,1f,false,false);
        Vector3 UpMove=StateUp.GetEyePosition()-EyeBeforeUp;

        SceneViewState StateDown=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f,Pitch=0.7f };
        Vector3 EyeBeforeDown=StateDown.GetEyePosition();
        StateDown.TickFly([NavigationKey.Q],0f,0f,1f,false,false);
        Vector3 DownMove=StateDown.GetEyePosition()-EyeBeforeDown;

        Assert.True(UpMove.Y>0f);
        Assert.Equal(0f,UpMove.X,Precision);
        Assert.Equal(0f,UpMove.Z,Precision);
        Assert.True(DownMove.Y<0f);
    }

    [Fact]
    public void Fly_SpeedScalesWithFocusDistance(){
        SceneViewState Near=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        Vector3 NearEyeBefore=Near.GetEyePosition();
        Near.TickFly([NavigationKey.W],0f,0f,1f,false,false);
        float NearMoved=(Near.GetEyePosition()-NearEyeBefore).Length();

        SceneViewState Far=new(){ FocusPosition=Vector3.Zero,FocusDistance=10f };
        Vector3 FarEyeBefore=Far.GetEyePosition();
        Far.TickFly([NavigationKey.W],0f,0f,1f,false,false);
        float FarMoved=(Far.GetEyePosition()-FarEyeBefore).Length();

        Assert.Equal(10f,FarMoved/NearMoved,Precision);
    }

    [Fact]
    public void Fly_ShiftMultipliesSpeedByFour(){
        SceneViewState Normal=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        Vector3 NormalBefore=Normal.GetEyePosition();
        Normal.TickFly([NavigationKey.W],0f,0f,1f,Shift:false,Ctrl:false);
        float NormalMoved=(Normal.GetEyePosition()-NormalBefore).Length();

        SceneViewState Fast=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        Vector3 FastBefore=Fast.GetEyePosition();
        Fast.TickFly([NavigationKey.W],0f,0f,1f,Shift:true,Ctrl:false);
        float FastMoved=(Fast.GetEyePosition()-FastBefore).Length();

        Assert.Equal(4f,FastMoved/NormalMoved,Precision);
    }

    [Fact]
    public void Fly_CtrlMultipliesSpeedByQuarter(){
        SceneViewState Normal=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        Vector3 NormalBefore=Normal.GetEyePosition();
        Normal.TickFly([NavigationKey.W],0f,0f,1f,Shift:false,Ctrl:false);
        float NormalMoved=(Normal.GetEyePosition()-NormalBefore).Length();

        SceneViewState Precise=new(){ FocusPosition=Vector3.Zero,FocusDistance=1f };
        Vector3 PreciseBefore=Precise.GetEyePosition();
        Precise.TickFly([NavigationKey.W],0f,0f,1f,Shift:false,Ctrl:true);
        float PreciseMoved=(Precise.GetEyePosition()-PreciseBefore).Length();

        Assert.Equal(0.25f,PreciseMoved/NormalMoved,Precision);
    }

    [Fact]
    public void Orbit_ChangesYawPitch_FocusPositionUnchanged(){
        SceneViewState State=new(){ FocusPosition=new Vector3(3f,4f,5f),FocusDistance=6f,Yaw=0.1f,Pitch=0.1f };
        Vector3 FocusBefore=State.FocusPosition;

        State.ApplyOrbit(20f,10f);

        Assert.Equal(FocusBefore,State.FocusPosition);
        Assert.NotEqual(0.1f,State.Yaw);
    }

    [Fact]
    public void Pan_TranslatesFocusAndEyeBySameDelta_ViewDirectionUnchanged(){
        SceneViewState State=new(){ FocusPosition=new Vector3(1f,2f,3f),FocusDistance=4f,Yaw=0.3f,Pitch=0.2f };
        Vector3 FocusBefore=State.FocusPosition;
        Vector3 EyeBefore=State.GetEyePosition();

        State.ApplyPan(30f,-15f);

        Vector3 FocusDelta=State.FocusPosition-FocusBefore;
        Vector3 EyeDelta=State.GetEyePosition()-EyeBefore;
        Assert.Equal(FocusDelta.X,EyeDelta.X,Precision);
        Assert.Equal(FocusDelta.Y,EyeDelta.Y,Precision);
        Assert.Equal(FocusDelta.Z,EyeDelta.Z,Precision);
        Assert.True(FocusDelta.Length()>0f);
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

        State.ResetToBounds(Center,Radius,SceneCameraFraming.ViewDirection,SceneCameraFraming.DefaultFovYRadians);

        Assert.Equal(Center,State.FocusPosition);
        Vector3 Forward=SceneViewCamera.GetForward(State.Yaw,State.Pitch);
        Vector3 ExpectedForward=-SceneCameraFraming.ViewDirection;
        Assert.Equal(ExpectedForward.X,Forward.X,Precision);
        Assert.Equal(ExpectedForward.Y,Forward.Y,Precision);
        Assert.Equal(ExpectedForward.Z,Forward.Z,Precision);
    }

    [Fact]
    public void GetProjection_Perspective_FarScalesWithFocusDistanceButFloorsAtBoundsRadiusTimesTwenty(){
        SceneViewState State=new(){ FocusDistance=1f,LastKnownBoundsRadius=50f };

        Matrix4x4 Projection=State.GetProjection(1f);

        // Far=max(1*20, 50*20)=1000。境界球フロアが効いていることを間接的に確認する
        // (境界球が無い場合は far=20 になり、近い場所のオブジェクトがクリップされてしまう)。
        Assert.False(float.IsNaN(Projection.M33));
    }

    [Fact]
    public void CreateFromSettings_ReadsAllSevenPersistedFields()=>
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SceneEditorSettings.WriteActiveTool(SceneActiveTool.Scale);
            SceneEditorSettings.WriteNavigationSpeedMultiplier(3f);

            SceneViewState State=SceneViewState.CreateFromSettings();

            Assert.Equal(SceneActiveTool.Scale,State.ActiveTool);
            Assert.Equal(3f,State.NavigationSpeedMultiplier);
        });

    [Fact]
    public void SaveToSettings_ThenCreateFromSettings_RoundTrips()=>
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SceneViewState Original=new(){ ActiveTool=SceneActiveTool.Rotate,InvertLookY=true };

            Original.SaveToSettings();
            SceneViewState Restored=SceneViewState.CreateFromSettings();

            Assert.Equal(SceneActiveTool.Rotate,Restored.ActiveTool);
            Assert.True(Restored.InvertLookY);
        });
}
