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

        var Result=Machine.Handle(Down(PointerButton.Right,0,0));

        Assert.Equal(SceneNavState.Flying,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
        Assert.Equal(SceneNavState.Flying,Machine.State);
    }

    [Fact]
    public void Idle_MiddleButtonDown_TransitionsToPanningAndBeginsCapture(){
        SceneInputStateMachine Machine=new();

        var Result=Machine.Handle(Down(PointerButton.Middle,0,0));

        Assert.Equal(SceneNavState.Panning,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    [Fact]
    public void Idle_AltLeftButtonDown_TransitionsToOrbiting(){
        SceneInputStateMachine Machine=new();

        var Result=Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,0,0,AltModifiers));

        Assert.Equal(SceneNavState.Orbiting,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    /// <summary>
    /// M0095 (user request 2026-10-01): the selection's gizmos are hidden while an Alt+LMB orbit drags and
    /// come back when it ends. Holding Alt alone keeps them, so Alt on a handle still duplicates (§6.2).
    /// </summary>
    [Fact]
    public void Gizmos_AreHiddenOnlyWhileOrbiting(){
        SceneInputStateMachine Machine=new();
        Assert.True(SceneGizmoVisibilityPolicy.ShowsGizmos(Machine.State));
        Machine.Handle(new PointerEvent(PointerEventKind.Move,PointerButton.None,5,5,AltModifiers));
        Assert.True(SceneGizmoVisibilityPolicy.ShowsGizmos(Machine.State));
        Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,5,5,AltModifiers));
        Assert.False(SceneGizmoVisibilityPolicy.ShowsGizmos(Machine.State));
        Machine.Handle(new PointerEvent(PointerEventKind.Up,PointerButton.Left,40,5,AltModifiers));
        Assert.True(SceneGizmoVisibilityPolicy.ShowsGizmos(Machine.State));
    }

    [Fact]
    public void Idle_AltLeftButtonOnGizmo_BeginsDuplicateGizmoInsteadOfOrbit(){
        SceneInputStateMachine Machine=new();

        var Result=Machine.Handle(
                                  new PointerEvent(PointerEventKind.Down,PointerButton.Left,10,20,AltModifiers),
                                  HitGizmoHandle:GizmoHandleId.AxisX);

        Assert.Equal(SceneNavState.GizmoDragging,Result.State);
        Assert.True(Result.GizmoDragBegan);
        Assert.True(Result.GizmoDuplicate);
        Assert.Equal(GizmoHandleId.AxisX,Result.GizmoHandle);
    }

    [Fact]
    public void Idle_LeftButtonDown_TransitionsToPendingSelectAndBeginsCapture(){
        SceneInputStateMachine Machine=new();

        var Result=Machine.Handle(Down(PointerButton.Left,0,0));

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

        var Result=Machine.Handle(Move(115,92));

        Assert.Equal(15f,Result.DeltaX);
        Assert.Equal(-8f,Result.DeltaY);
        Assert.Equal(SceneCaptureAction.None,Result.Capture);
    }

    [Fact]
    public void Flying_SecondMove_IsRelativeToPreviousMoveNotOriginalDown(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));
        Machine.Handle(Move(10,0));

        var Result=Machine.Handle(Move(25,0));

        Assert.Equal(15f,Result.DeltaX);
    }

    [Fact]
    public void Flying_RightButtonUp_TransitionsToIdleAndEndsCapture(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));

        var Result=Machine.Handle(Up(PointerButton.Right,0,0));

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.End,Result.Capture);
    }

    [Fact]
    public void Flying_CaptureLost_TransitionsToIdleAndClearsHeldKeys(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Right,0,0));
        Machine.HandleKeyDown(NavigationKey.W);

        var Result=Machine.Handle(CaptureLost());

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.None,Result.Capture);
        Assert.Empty(Machine.FlyHeldKeys);
    }

    [Fact]
    public void Panning_MiddleButtonUp_TransitionsToIdle(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Middle,0,0));

        var Result=Machine.Handle(Up(PointerButton.Middle,0,0));

        Assert.Equal(SceneNavState.Idle,Result.State);
    }

    [Fact]
    public void Orbiting_LeftButtonUp_TransitionsToIdle(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,0,0,AltModifiers));

        var Result=Machine.Handle(Up(PointerButton.Left,0,0));

        Assert.Equal(SceneNavState.Idle,Result.State);
    }

    [Fact]
    public void PendingSelect_LeftButtonUp_ConfirmsStoredHitAndReturnsToIdle(){
        SceneInputStateMachine Machine=new();
        var Hit=NodePath.Root.Append(2);
        Machine.Handle(Down(PointerButton.Left,0,0),Hit);

        var Result=Machine.Handle(Up(PointerButton.Left,0,0));

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

        var Result=Machine.Handle(Up(PointerButton.Left,0,0));

        Assert.True(Result.SelectionConfirmed);
        Assert.Null(Result.ConfirmedNodePath);
    }

    [Fact]
    public void PendingSelect_LeftButtonUp_WithCtrlHeld_ConfirmsCtrlFlag(){
        SceneInputStateMachine Machine=new();
        RawInputModifiers CtrlModifiers=new(Shift:false,Ctrl:true,Alt:false);
        Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,0,0,CtrlModifiers),NodePath.Root);

        var Result=Machine.Handle(Up(PointerButton.Left,0,0));

        Assert.True(Result.SelectionConfirmed);
        Assert.True(Result.ConfirmedCtrl);
    }

    [Fact]
    public void PendingSelect_CaptureLost_ReturnsToIdleWithoutConfirming(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),NodePath.Root);

        var Result=Machine.Handle(CaptureLost());

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.False(Result.SelectionConfirmed);
    }

    [Fact]
    public void PendingSelect_RightButtonDown_PreemptsToFlying(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),NodePath.Root);

        var Result=Machine.Handle(Down(PointerButton.Right,0,0));

        Assert.Equal(SceneNavState.Flying,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    [Fact]
    public void PendingSelect_MiddleButtonDown_PreemptsToPanning(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),NodePath.Root);

        var Result=Machine.Handle(Down(PointerButton.Middle,0,0));

        Assert.Equal(SceneNavState.Panning,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
    }

    [Fact]
    public void PendingSelect_EmptySpaceDraggedPastThreshold_BeginsMarqueeSelection(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,10,20),HitNodePath:null);

        var Result=Machine.Handle(Move(15,23));

        Assert.Equal(SceneNavState.MarqueeSelecting,Result.State);
        Assert.True(Result.MarqueeUpdated);
        Assert.Equal(ScreenRectangle.FromPoints(10,20,15,23),Result.MarqueeRectangle);
        Assert.False(Result.MarqueeSelectionConfirmed);
    }

    [Fact]
    public void PendingSelect_EmptySpaceDraggedBelowThreshold_RemainsPendingSelect(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,10,20),HitNodePath:null);

        var Result=Machine.Handle(Move(12,22));

        Assert.Equal(SceneNavState.PendingSelect,Result.State);
        Assert.False(Result.MarqueeUpdated);
    }

    [Fact]
    public void PendingSelect_ObjectStartNeverTransitionsToMarquee(){
        SceneInputStateMachine Machine=new();
        var Hit=NodePath.Root.Append(3);
        Machine.Handle(Down(PointerButton.Left,10,20),Hit);

        var MoveResult=Machine.Handle(Move(100,200));
        var UpResult=Machine.Handle(Up(PointerButton.Left,100,200));

        Assert.Equal(SceneNavState.PendingSelect,MoveResult.State);
        Assert.False(MoveResult.MarqueeUpdated);
        Assert.True(UpResult.SelectionConfirmed);
        Assert.Equal(Hit,UpResult.ConfirmedNodePath);
    }

    [Fact]
    public void MarqueeSelecting_LeftButtonUp_ConfirmsNormalizedRectangleAndCtrlFromDown(){
        SceneInputStateMachine Machine=new();
        RawInputModifiers CtrlModifiers=new(Shift:false,Ctrl:true,Alt:false);
        Machine.Handle(new PointerEvent(PointerEventKind.Down,PointerButton.Left,80,70,CtrlModifiers),HitNodePath:null);
        Machine.Handle(Move(60,40));

        var Result=Machine.Handle(Up(PointerButton.Left,30,20));

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.End,Result.Capture);
        Assert.True(Result.MarqueeSelectionConfirmed);
        Assert.Equal(ScreenRectangle.FromPoints(80,70,30,20),Result.MarqueeRectangle);
        Assert.True(Result.ConfirmedCtrl);
        Assert.False(Result.SelectionConfirmed);
    }

    [Fact]
    public void MarqueeSelecting_CaptureLost_CancelsWithoutConfirming(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,10,20),HitNodePath:null);
        Machine.Handle(Move(30,40));

        var Result=Machine.Handle(CaptureLost());

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.True(Result.MarqueeCanceled);
        Assert.False(Result.MarqueeSelectionConfirmed);
    }

    [Fact]
    public void MarqueeSelecting_CancelCurrent_CancelsAndEndsCapture(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,10,20),HitNodePath:null);
        Machine.Handle(Move(30,40));

        var Result=Machine.CancelCurrent();

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.End,Result.Capture);
        Assert.True(Result.MarqueeCanceled);
        Assert.False(Result.MarqueeSelectionConfirmed);
    }

    [Fact]
    public void MarqueeSelecting_RightButtonDown_DoesNotChangeOperation(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,10,20),HitNodePath:null);
        Machine.Handle(Move(30,40));

        var Result=Machine.Handle(Down(PointerButton.Right,30,40));

        Assert.Equal(SceneNavState.MarqueeSelecting,Result.State);
        Assert.Equal(SceneCaptureAction.None,Result.Capture);
    }

    [Fact]
    public void Idle_LeftButtonOnGizmo_BeginsExclusiveGizmoDrag(){
        SceneInputStateMachine Machine=new();

        var Result=Machine.Handle(
                                  Down(PointerButton.Left,10,20),HitNodePath:NodePath.Root,HitGizmoHandle:GizmoHandleId.AxisX);

        Assert.Equal(SceneNavState.GizmoDragging,Result.State);
        Assert.Equal(SceneCaptureAction.Begin,Result.Capture);
        Assert.True(Result.GizmoDragBegan);
        Assert.Equal(GizmoHandleId.AxisX,Result.GizmoHandle);
        Assert.False(Result.SelectionConfirmed);
    }

    [Fact]
    public void GizmoDragging_LeftButtonUp_RequestsCommitAndEndsCapture(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),HitGizmoHandle:GizmoHandleId.PlaneXY);

        var Result=Machine.Handle(Up(PointerButton.Left,5,5));

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.Equal(SceneCaptureAction.End,Result.Capture);
        Assert.True(Result.GizmoCommitRequested);
        Assert.Equal(GizmoHandleId.PlaneXY,Result.GizmoHandle);
    }

    [Fact]
    public void GizmoDragging_CancelCurrent_CancelsWithoutCommit(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),HitGizmoHandle:GizmoHandleId.AxisY);

        var Result=Machine.CancelCurrent();

        Assert.Equal(SceneNavState.Idle,Result.State);
        Assert.True(Result.GizmoCanceled);
        Assert.False(Result.GizmoCommitRequested);
        Assert.Equal(SceneCaptureAction.End,Result.Capture);
    }

    /// <summary>
    /// M0030 監査 §8「複数ドラッグイベント重畳: 最後の確定イベントのみ採用」。ドラッグ中の追加 Down は
    /// 掴み直しにならず、Up で確定した後の重複 Up も2度目の commit を出さない。これが崩れると
    /// <c>EditTransactionController</c>が Committing 状態で再 Begin され例外になる。
    /// </summary>
    [Fact]
    public void GizmoDragging_RedundantDownAndUpEvents_CommitExactlyOnce(){
        SceneInputStateMachine Machine=new();
        Machine.Handle(Down(PointerButton.Left,0,0),HitGizmoHandle:GizmoHandleId.AxisX);

        var SecondDown=Machine.Handle(
                                      Down(PointerButton.Left,3,3),HitGizmoHandle:GizmoHandleId.AxisY);

        Assert.Equal(SceneNavState.GizmoDragging,SecondDown.State);
        Assert.False(SecondDown.GizmoDragBegan);
        Assert.Equal(SceneCaptureAction.None,SecondDown.Capture);

        var FirstUp=Machine.Handle(Up(PointerButton.Left,9,9));
        var SecondUp=Machine.Handle(Up(PointerButton.Left,9,9));

        Assert.True(FirstUp.GizmoCommitRequested);
        Assert.Equal(GizmoHandleId.AxisX,FirstUp.GizmoHandle);
        Assert.Equal(SceneNavState.Idle,SecondUp.State);
        Assert.False(SecondUp.GizmoCommitRequested);
        Assert.False(SecondUp.GizmoDragBegan);
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

    [Fact]
    public void CancelCurrent_IsTerminalForEveryInputState(){
        foreach(var Target in Enum.GetValues(typeof(SceneNavState)).Cast<SceneNavState>()){
            SceneInputStateMachine Machine=new();
            switch(Target){
                case SceneNavState.Idle:
                    break;
                case SceneNavState.Flying:
                    Machine.Handle(Down(PointerButton.Right,0,0));
                    Machine.HandleKeyDown(NavigationKey.W);
                    break;
                case SceneNavState.Panning:
                    Machine.Handle(Down(PointerButton.Middle,0,0));
                    break;
                case SceneNavState.Orbiting:
                    Machine.Handle(new PointerEvent(
                        PointerEventKind.Down,PointerButton.Left,0,0,AltModifiers));
                    break;
                case SceneNavState.PendingSelect:
                    Machine.Handle(Down(PointerButton.Left,0,0),HitNodePath:NodePath.Root);
                    break;
                case SceneNavState.MarqueeSelecting:
                    Machine.Handle(Down(PointerButton.Left,0,0),HitNodePath:null);
                    Machine.Handle(Move(10,10));
                    break;
                case SceneNavState.GizmoDragging:
                    Machine.Handle(
                        Down(PointerButton.Left,0,0),HitGizmoHandle:GizmoHandleId.AxisX);
                    break;
            }
            Assert.Equal(Target,Machine.State);

            var Result=Machine.CancelCurrent();

            Assert.Equal(SceneNavState.Idle,Machine.State);
            Assert.Empty(Machine.FlyHeldKeys);
            Assert.Equal(
                Target==SceneNavState.Idle?SceneCaptureAction.None:SceneCaptureAction.End,
                Result.Capture);
            Assert.Equal(Target==SceneNavState.MarqueeSelecting,Result.MarqueeCanceled);
            Assert.Equal(Target==SceneNavState.GizmoDragging,Result.GizmoCanceled);
            Assert.False(Result.SelectionConfirmed);
            Assert.False(Result.GizmoCommitRequested);
        }
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
