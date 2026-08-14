using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneInputStateMachine"/>の状態遷移。仕様§12の優先順位
/// (Modal操作>ナビゲーション>ギズモ>選択>コンテキストメニュー)をRMB/MMB/Alt+LMBのドラッグが
/// 他の操作を排他的に占有する形で固定する。<c>PendingSelect</c>(M0020 Phase 2)関連のテストは
/// Navigation(RMB/MMB)がSelectionより優先順位が高いこと(選択待ち中の中断)も固定する。
/// </summary>
public sealed class SceneInputStateMachineTests {
    private static readonly RawInputModifiers NoModifiers=default;
    private static readonly RawInputModifiers AltModifiers=new(Shift:false,Ctrl:false,Alt:true);

    [Fact]
    public void Idle_RightButtonDown_TransitionsToFlyingAndBeginsCapture(){
        SceneInputStateMachine Machine=new();

        SceneInputStepResult Result=Machine.Handle(Down(PointerButton.Right,0,0));

        Assert.Equal(SceneNavState.Flying,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
        Assert.Equal(SceneNavState.Flying,Machine.State);
    }

    [Fact]
    public void Idle_MiddleButtonDown_TransitionsToPanningAndBeginsCapture(){
        SceneInputStateMachine Machine=new();

        SceneInputStepResult Result=Machine.Handle(Down(PointerButton.Middle,0,0));

        Assert.Equal(SceneNavState.Panning,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    [Fact]
    public void Idle_AltLeftButtonDown_TransitionsToOrbiting(){
        SceneInputStateMachine Machine=new();

        SceneInputStepResult Result=Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,0,0,AltModifiers));

        Assert.Equal(SceneNavState.Orbiting,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    [Fact]
    public void Idle_LeftButtonDown_TransitionsToPendingSelectAndBeginsCapture(){
        SceneInputStateMachine Machine=new();

        SceneInputStepResult Result=Machine.Handle(Down(PointerButton.Left,0,0));

        Assert.Equal(SceneNavState.PendingSelect,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
        Assert.Equal(SceneNavState.PendingSelect,Machine.State);
    }

    [Fact]
    public void Flying_IgnoresLeftAndMiddleButtonDown(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));

        Machine.Handle(Down(PointerButton.Left,10,10));
        Machine.Handle(Down(PointerButton.Middle,10,10));

        Assert.Equal(SceneNavState.Flying,Machine.State);
    }

    [Fact]
    public void Flying_MouseMove_EmitsDeltaRelativeToLastPosition(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,100,100));

        SceneInputStepResult Result=Machine.Handle(Move(115,92));

        Assert.Equal(15f,Result.DeltaX);
        Assert.Equal(-8f,Result.DeltaY);
        Assert.Equal(SceneCaptureAction.None,Result.Capture);
    }

    [Fact]
    public void Flying_SecondMove_IsRelativeToPreviousMoveNotOriginalDown(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));
        Machine.Handle(Move(10,0));

        SceneInputStepResult Result=Machine.Handle(Move(25,0));

        Assert.Equal(15f,Result.DeltaX);
    }

    [Fact]
    public void Flying_RightButtonUp_TransitionsToIdleAndEndsCapture(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));

        SceneInputStepResult Result=Machine.Handle(Up(PointerButton.Right,0,0));

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.End,Result.Capture);
    }

    [Fact]
    public void Flying_CaptureLost_TransitionsToIdleAndClearsHeldKeys(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));
        Machine.HandleKeyDown(NavigationKey.W);

        SceneInputStepResult Result=Machine.Handle(CaptureLost());

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.None,Result.Capture);
        Assert.Empty(Machine.FlyHeldKeys);
    }

    [Fact]
    public void Panning_MiddleButtonUp_TransitionsToIdle(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Middle,0,0));

        SceneInputStepResult Result=Machine.Handle(Up(PointerButton.Middle,0,0));

        Assert.Equal(SceneNavState.Idle,Result.State);
    }

    [Fact]
    public void Orbiting_LeftButtonUp_TransitionsToIdle(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,0,0,AltModifiers));

        SceneInputStepResult Result=Machine.Handle(Up(PointerButton.Left,0,0));

        Assert.Equal(SceneNavState.Idle,Result.State);
    }

    [Fact]
    public void PendingSelect_LeftButtonUp_ConfirmsStoredHitAndReturnsToIdle(){
        SceneInputStateMachine Machine=new();
        NodePath Hit=NodePath.Root.Append(2);
        Machine.Handle(Down(PointerButton.Left,0,0),Hit);

        SceneInputStepResult Result=Machine.Handle(Up(PointerButton.Left,0,0));

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.End,Result.Capture);
        Assert.True(Result.SelectionConfirmed);
        Assert.Equal(Hit,Result.ConfirmedNodePath);
        Assert.False(Result.ConfirmedCtrl);
    }

    [Fact]
    public void PendingSelect_LeftButtonUp_NoHit_ConfirmsNullNodePath(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),HitNodePath:null);

        SceneInputStepResult Result=Machine.Handle(Up(PointerButton.Left,0,0));

        Assert.True(Result.SelectionConfirmed);
        Assert.Null(Result.ConfirmedNodePath);
    }

    [Fact]
    public void PendingSelect_LeftButtonUp_WithCtrlHeld_ConfirmsCtrlFlag(){
        SceneInputStateMachine Machine=new();
        RawInputModifiers CtrlModifiers=new(Shift:false,Ctrl:true,Alt:false);
        Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,0,0,CtrlModifiers),NodePath.Root);

        SceneInputStepResult Result=Machine.Handle(Up(PointerButton.Left,0,0));

        Assert.True(Result.SelectionConfirmed);
        Assert.True(Result.ConfirmedCtrl);
    }

    [Fact]
    public void PendingSelect_CaptureLost_ReturnsToIdleWithoutConfirming(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),NodePath.Root);

        SceneInputStepResult Result=Machine.Handle(CaptureLost());

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.False(Result.SelectionConfirmed);
    }

    [Fact]
    public void PendingSelect_RightButtonDown_PreemptsToFlying(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),NodePath.Root);

        SceneInputStepResult Result=Machine.Handle(Down(PointerButton.Right,0,0));

        Assert.Equal(SceneNavState.Flying,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    [Fact]
    public void PendingSelect_MiddleButtonDown_PreemptsToPanning(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),NodePath.Root);

        SceneInputStepResult Result=Machine.Handle(Down(PointerButton.Middle,0,0));

        Assert.Equal(SceneNavState.Panning,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    [Fact]
    public void Wheel_WhileFlying_ReportsAdjustSpeedNotNavigationDistance(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));

        Assert.Equal(SceneWheelAction.AdjustSpeed,Machine.HandleWheel());
    }

    [Fact]
    public void Wheel_WhileIdle_ReportsNavigationDistance()=>
        Assert.Equal(SceneWheelAction.NavigationDistance,new SceneInputStateMachine().HandleWheel());

    [Fact]
    public void KeyDown_WhileNotFlying_DoesNotAddToHeldKeys(){
        SceneInputStateMachine Machine=new();

        Machine.HandleKeyDown(NavigationKey.W);

        Assert.Empty(Machine.FlyHeldKeys);
    }

    [Fact]
    public void KeyDown_WhileFlying_AddsToHeldKeys_KeyUpRemoves(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));

        Machine.HandleKeyDown(NavigationKey.W);
        Assert.Contains(NavigationKey.W,Machine.FlyHeldKeys);

        Machine.HandleKeyUp(NavigationKey.W);
        Assert.DoesNotContain(NavigationKey.W,Machine.FlyHeldKeys);
    }

    [Fact]
    public void LostKeyboardFocus_ClearsHeldKeysWithoutChangingState(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));
        Machine.HandleKeyDown(NavigationKey.W);

        Machine.HandleLostKeyboardFocus();

        Assert.Empty(Machine.FlyHeldKeys);
        Assert.Equal(SceneNavState.Flying,Machine.State);
    }

    [Fact]
    public void Reset_ClearsHeldKeysAndReturnsToIdle(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));
        Machine.HandleKeyDown(NavigationKey.W);

        Machine.Reset();

        Assert.Equal(SceneNavState.Idle,Machine.State);
        Assert.Empty(Machine.FlyHeldKeys);
    }

    private static PointerEvent Down(PointerButton Button,int X,int Y)=>
        new(PointerEventKind.Down,Button,X,Y,NoModifiers);

    private static PointerEvent Up(PointerButton Button,int X,int Y)=>
        new(PointerEventKind.Up,Button,X,Y,NoModifiers);

    private static PointerEvent Move(int X,int Y)=>
        new(PointerEventKind.Move,PointerButton.None,X,Y,NoModifiers);

    private static PointerEvent CaptureLost()=>
        new(PointerEventKind.CaptureLost,PointerButton.None,0,0,NoModifiers);
}
