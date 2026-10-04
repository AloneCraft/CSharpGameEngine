using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ナビゲーションと編集操作の排他的状態。<c>GizmoDragging</c>はM0020 Phase 3、
/// <c>MarqueeSelecting</c>はM0020後続Task Cで追加。
/// </summary>
internal enum SceneNavState { Idle, Flying, Panning, Orbiting, PendingSelect, MarqueeSelecting, GizmoDragging }

/// <summary>
/// M0095 (user request 2026-10-01): the selection's gizmos are hidden while an Alt+LMB orbit drags. Holding
/// Alt alone keeps them, so Alt on a handle still begins a duplicate drag (Scene-Editor-Design §6.2).
/// </summary>
internal static class SceneGizmoVisibilityPolicy {
    internal static bool ShowsGizmos(SceneNavState State)=>State!=SceneNavState.Orbiting;
}

internal enum SceneCaptureAction { None, Begin, End }

/// <summary>
/// <see cref="SceneInputStateMachine.Handle"/>の戻り値。<see cref="DeltaX"/>/<see cref="DeltaY"/>は
/// <see cref="PointerEventKind.Move"/>由来のティックのみ非ゼロ。<see cref="SelectionConfirmed"/>は
/// <see cref="SceneNavState.PendingSelect"/>からLMB Upで戻った時のみtrue(M0020 Phase 2)。
/// </summary>
internal readonly struct SceneInputStepResult {
    internal SceneInputStepResult(
        SceneNavState State,SceneCaptureAction Capture,float DeltaX,float DeltaY,
        bool SelectionConfirmed=false,NodePath?ConfirmedNodePath=null,bool ConfirmedCtrl=false,
        bool MarqueeUpdated=false,bool MarqueeSelectionConfirmed=false,bool MarqueeCanceled=false,
        ScreenRectangle MarqueeRectangle=default,
        bool GizmoDragBegan=false,bool GizmoCommitRequested=false,bool GizmoCanceled=false,
        GizmoHandleId?GizmoHandle=null,bool GizmoDuplicate=false){
        this.State=State;
        this.Capture=Capture;
        this.DeltaX=DeltaX;
        this.DeltaY=DeltaY;
        this.SelectionConfirmed=SelectionConfirmed;
        this.ConfirmedNodePath=ConfirmedNodePath;
        this.ConfirmedCtrl=ConfirmedCtrl;
        this.MarqueeUpdated=MarqueeUpdated;
        this.MarqueeSelectionConfirmed=MarqueeSelectionConfirmed;
        this.MarqueeCanceled=MarqueeCanceled;
        this.MarqueeRectangle=MarqueeRectangle;
        this.GizmoDragBegan=GizmoDragBegan;
        this.GizmoCommitRequested=GizmoCommitRequested;
        this.GizmoCanceled=GizmoCanceled;
        this.GizmoHandle=GizmoHandle;
        this.GizmoDuplicate=GizmoDuplicate;
    }

    internal SceneNavState State { get; }

    internal SceneCaptureAction Capture { get; }

    internal float DeltaX { get; }

    internal float DeltaY { get; }

    internal bool SelectionConfirmed { get; }

    /// <summary>Down時にヒットしたNodePath。ヒット無し(空間クリック=選択解除)ならnull。</summary>
    internal NodePath?ConfirmedNodePath { get; }

    /// <summary>Down時のCtrl押下状態(複数選択トグル)。</summary>
    internal bool ConfirmedCtrl { get; }

    internal bool MarqueeUpdated { get; }

    internal bool MarqueeSelectionConfirmed { get; }

    internal bool MarqueeCanceled { get; }

    internal ScreenRectangle MarqueeRectangle { get; }

    internal bool GizmoDragBegan { get; }

    internal bool GizmoCommitRequested { get; }

    internal bool GizmoCanceled { get; }

    internal GizmoHandleId?GizmoHandle { get; }

    internal bool GizmoDuplicate { get; }
}

/// <summary>
/// Scene View のナビゲーション入力状態機械。WPF/Win32型に一切依存しない純粋クラス
/// (<see cref="PointerEvent"/>/<see cref="NavigationKey"/>のみを扱う)。
///
/// <para>
/// 仕様§12の優先順位(1. Modal操作 2. ナビゲーション 3. ギズモ操作 4. 選択 5. コンテキストメニュー)
/// を状態遷移として実装する: <see cref="SceneNavState.Idle"/>以外の状態にいる間は、対応ボタンの
/// Upまたは<see cref="PointerEventKind.CaptureLost"/>以外の全イベントを無視する
/// (RMBドラッグ中に選択・他ナビゲーションを開始しない)。
/// </para>
///
/// <para>
/// <see cref="PointerEventKind.CaptureLost"/>はAlt+Tabやモーダルダイアログでキャプチャが奪われた
/// 通知。これを処理しないと、対応するボタンUpが二度と届かず状態機械がドラッグ状態へ永久固着する。
/// </para>
/// </summary>
internal sealed class SceneInputStateMachine {
    private const int MarqueeDragThresholdPixels=4;
    private int LastX;
    private int LastY;
    private readonly HashSet<NavigationKey>HeldKeys=[];
    private NodePath?PendingSelectNodePath;
    private bool PendingSelectCtrl;
    private int SelectionStartX;
    private int SelectionStartY;
    private GizmoHandleId?ActiveGizmoHandle;

    internal SceneNavState State { get;private set;}=SceneNavState.Idle;

    internal IReadOnlyCollection<NavigationKey>FlyHeldKeys=> this.HeldKeys;

    /// <summary>
    /// <paramref name="HitNodePath"/>はM0020 Phase 2のピッキング結果 — ヒットテスト自体は
    /// <c>SceneHwndHost</c>側(<c>ScenePicker</c>)で行い、ここには「Down位置で何にヒットしたか」の
    /// 結果だけを渡す(状態機械はScenePicker/SceneRenderObjectを一切知らない、既存の純粋性方針
    /// を維持するため)。LMB Down以外のイベントでは無視される。
    /// </summary>
    internal SceneInputStepResult Handle(
        PointerEvent Event,NodePath?HitNodePath=null,GizmoHandleId?HitGizmoHandle=null){
        switch(this.State){
            case SceneNavState.Idle:
                if(IsButtonDown(Event,PointerButton.Right)){ return this.Begin(SceneNavState.Flying,Event);}
                if(IsButtonDown(Event,PointerButton.Middle)){ return this.Begin(SceneNavState.Panning,Event);}
                if(IsButtonDown(Event,PointerButton.Left)&&HitGizmoHandle is { }Handle){ return this.BeginGizmo(Event,Handle);}
                if(IsButtonDown(Event,PointerButton.Left)&&Event.Modifiers.Alt){ return this.Begin(SceneNavState.Orbiting,Event);}
                if(IsButtonDown(Event,PointerButton.Left)){ return this.BeginSelect(Event,HitNodePath);}
                return this.Stay();

            case SceneNavState.Flying:
                if(IsButtonUp(Event,PointerButton.Right)){ return this.End();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return this.LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return this.Move(Event);}
                return this.Stay();

            case SceneNavState.Panning:
                if(IsButtonUp(Event,PointerButton.Middle)){ return this.End();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return this.LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return this.Move(Event);}
                return this.Stay();

            case SceneNavState.Orbiting:
                if(IsButtonUp(Event,PointerButton.Left)){ return this.End();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return this.LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return this.Move(Event);}
                return this.Stay();

            case SceneNavState.PendingSelect:
                // 仕様§12優先順位: Navigation(2) > Selection(4)。選択待ち中にRMB/MMBが来たら
                // 選択を中断してナビゲーションへ切り替えて良い(選択はまだ何も確定していない)。
                if(IsButtonDown(Event,PointerButton.Right)){ return this.Begin(SceneNavState.Flying,Event);}
                if(IsButtonDown(Event,PointerButton.Middle)){ return this.Begin(SceneNavState.Panning,Event);}
                if(IsButtonUp(Event,PointerButton.Left)){ return this.ConfirmSelect();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return this.LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return this.TryBeginMarquee(Event);}
                return this.Stay();

            case SceneNavState.MarqueeSelecting:
                // ドラッグ開始後は操作を固定し、LMB UpまたはCaptureLostまでNavigationへ切り替えない。
                if(IsButtonUp(Event,PointerButton.Left)){ return this.ConfirmMarquee(Event);}
                if(Event.Kind==PointerEventKind.CaptureLost){ return this.LoseMarqueeCapture();}
                if(Event.Kind==PointerEventKind.Move){ return this.UpdateMarquee(Event);}
                return this.Stay();

            case SceneNavState.GizmoDragging:
                if(IsButtonUp(Event,PointerButton.Left)){ return this.CommitGizmo();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return this.LoseGizmoCapture();}
                if(Event.Kind==PointerEventKind.Move){ return this.Move(Event);}
                return this.Stay();

            default:
                return this.Stay();
        }
    }

    /// <summary>Flying中はAdjustNavigationSpeed、それ以外(Idle含む)はDolly/Zoom(NavigationDistance)。</summary>
    internal SceneWheelAction HandleWheel()=> this.State==SceneNavState.Flying?SceneWheelAction.AdjustSpeed:SceneWheelAction.NavigationDistance;

    /// <summary>Flying中(RMB押下中)だけ押下集合へ追加する。仕様§5「RMB Navigation中はW/E/Q/A/S/Dを優先」。</summary>
    internal void HandleKeyDown(NavigationKey Key){
        if(this.State==SceneNavState.Flying){
            this.HeldKeys.Add(Key);
        }
    }

    internal void HandleKeyUp(NavigationKey Key)=> this.HeldKeys.Remove(Key);

    /// <summary>WPFの<c>LostKeyboardFocus</c>から呼ぶ。状態遷移はしない(RMBは離されていない)。</summary>
    internal void HandleLostKeyboardFocus()=> this.HeldKeys.Clear();

    internal SceneInputStepResult CancelCurrent(){
        if(this.State==SceneNavState.Idle){
            return this.Stay();
        }

        var WasGizmo= this.State==SceneNavState.GizmoDragging;
        var WasMarquee= this.State==SceneNavState.MarqueeSelecting;
        var Handle= this.ActiveGizmoHandle;
        this.State=SceneNavState.Idle;
        this.HeldKeys.Clear();
        this.PendingSelectNodePath=null;
        this.PendingSelectCtrl=false;
        this.ActiveGizmoHandle=null;
        return new(this.State,SceneCaptureAction.End,0f,0f,
            MarqueeCanceled:WasMarquee,
            GizmoCanceled:WasGizmo,GizmoHandle:Handle);
    }

    /// <summary><c>SceneHwndHost.DestroyWindowCore</c>から呼ぶ。</summary>
    internal void Reset(){
        this.HeldKeys.Clear();
        this.PendingSelectNodePath=null;
        this.PendingSelectCtrl=false;
        this.ActiveGizmoHandle=null;
        this.State=SceneNavState.Idle;
    }

    private static bool IsButtonDown(PointerEvent Event,PointerButton Button)=>
        Event.Kind==PointerEventKind.Down&&Event.Button==Button;

    private static bool IsButtonUp(PointerEvent Event,PointerButton Button)=>
        Event.Kind==PointerEventKind.Up&&Event.Button==Button;

    private SceneInputStepResult Begin(SceneNavState NewState,PointerEvent Event){
        this.State=NewState;
        this.LastX=Event.X;
        this.LastY=Event.Y;
        return new(this.State,SceneCaptureAction.Begin,0f,0f);
    }

    private SceneInputStepResult Move(PointerEvent Event){
        float DeltaX=Event.X- this.LastX;
        float DeltaY=Event.Y- this.LastY;
        this.LastX=Event.X;
        this.LastY=Event.Y;
        return new(this.State,SceneCaptureAction.None,DeltaX,DeltaY);
    }

    private SceneInputStepResult End(){
        this.State=SceneNavState.Idle;
        this.HeldKeys.Clear();
        return new(this.State,SceneCaptureAction.End,0f,0f);
    }

    private SceneInputStepResult BeginSelect(PointerEvent Event,NodePath?HitNodePath){
        this.State=SceneNavState.PendingSelect;
        this.PendingSelectNodePath=HitNodePath;
        this.PendingSelectCtrl=Event.Modifiers.Ctrl;
        this.SelectionStartX=Event.X;
        this.SelectionStartY=Event.Y;
        return new(this.State,SceneCaptureAction.Begin,0f,0f);
    }

    private SceneInputStepResult TryBeginMarquee(PointerEvent Event){
        if(this.PendingSelectNodePath is not null){
            return this.Stay();
        }

        var DeltaX=Event.X- this.SelectionStartX;
        var DeltaY=Event.Y- this.SelectionStartY;
        if((DeltaX*DeltaX)+(DeltaY*DeltaY)<MarqueeDragThresholdPixels*MarqueeDragThresholdPixels){
            return this.Stay();
        }

        this.State=SceneNavState.MarqueeSelecting;
        return new(this.State,SceneCaptureAction.None,0f,0f,
            MarqueeUpdated:true,
            MarqueeRectangle:ScreenRectangle.FromPoints(this.SelectionStartX, this.SelectionStartY,Event.X,Event.Y));
    }

    private SceneInputStepResult UpdateMarquee(PointerEvent Event)=>
        new(this.State,SceneCaptureAction.None,0f,0f,
            MarqueeUpdated:true,
            MarqueeRectangle:ScreenRectangle.FromPoints(this.SelectionStartX, this.SelectionStartY,Event.X,Event.Y));

    private SceneInputStepResult ConfirmMarquee(PointerEvent Event){
        var Rectangle=ScreenRectangle.FromPoints(this.SelectionStartX, this.SelectionStartY,Event.X,Event.Y);
        var ConfirmedCtrl= this.PendingSelectCtrl;
        this.State=SceneNavState.Idle;
        this.PendingSelectCtrl=false;
        return new(this.State,SceneCaptureAction.End,0f,0f,
            ConfirmedCtrl:ConfirmedCtrl,
            MarqueeSelectionConfirmed:true,
            MarqueeRectangle:Rectangle);
    }

    private SceneInputStepResult LoseMarqueeCapture(){
        this.State=SceneNavState.Idle;
        this.PendingSelectCtrl=false;
        return new(this.State,SceneCaptureAction.None,0f,0f,MarqueeCanceled:true);
    }

    private SceneInputStepResult BeginGizmo(PointerEvent Event,GizmoHandleId Handle){
        this.State=SceneNavState.GizmoDragging;
        this.LastX=Event.X;
        this.LastY=Event.Y;
        this.ActiveGizmoHandle=Handle;
        return new(this.State,SceneCaptureAction.Begin,0f,0f,GizmoDragBegan:true,GizmoHandle:Handle,
            GizmoDuplicate:Event.Modifiers.Alt);
    }

    private SceneInputStepResult CommitGizmo(){
        var Handle= this.ActiveGizmoHandle;
        this.ActiveGizmoHandle=null;
        this.State=SceneNavState.Idle;
        return new(this.State,SceneCaptureAction.End,0f,0f,GizmoCommitRequested:true,GizmoHandle:Handle);
    }

    private SceneInputStepResult LoseGizmoCapture(){
        var Handle= this.ActiveGizmoHandle;
        this.ActiveGizmoHandle=null;
        this.State=SceneNavState.Idle;
        return new(this.State,SceneCaptureAction.None,0f,0f,GizmoCanceled:true,GizmoHandle:Handle);
    }

    private SceneInputStepResult ConfirmSelect(){
        this.State=SceneNavState.Idle;
        var ConfirmedPath= this.PendingSelectNodePath;
        var ConfirmedCtrl= this.PendingSelectCtrl;
        this.PendingSelectNodePath=null;
        this.PendingSelectCtrl=false;
        return new(this.State,SceneCaptureAction.End,0f,0f,SelectionConfirmed:true,ConfirmedPath,ConfirmedCtrl);
    }

    private SceneInputStepResult LoseCapture(){
        // 既にキャプチャは奪われているのでReleaseCaptureは呼ばない(Endとの違い)。
        this.State=SceneNavState.Idle;
        this.HeldKeys.Clear();
        this.PendingSelectNodePath=null;
        this.PendingSelectCtrl=false;
        return new(this.State,SceneCaptureAction.None,0f,0f);
    }

    private SceneInputStepResult Stay()=>new(this.State,SceneCaptureAction.None,0f,0f);
}

/// <summary>Wheelイベントの意味づけ。<see cref="SceneViewState"/>側がProjectionModeでDolly/Zoomを選ぶ。</summary>
internal enum SceneWheelAction { NavigationDistance, AdjustSpeed }
