using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ナビゲーションと編集操作の排他的状態。<c>GizmoDragging</c>はM0020 Phase 3、
/// <c>MarqueeSelecting</c>はM0020後続Task Cで追加。
/// </summary>
internal enum SceneNavState { Idle, Flying, Panning, Orbiting, PendingSelect, MarqueeSelecting, GizmoDragging }

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

    internal IReadOnlyCollection<NavigationKey>FlyHeldKeys=>HeldKeys;

    /// <summary>
    /// <paramref name="HitNodePath"/>はM0020 Phase 2のピッキング結果 — ヒットテスト自体は
    /// <c>SceneHwndHost</c>側(<c>ScenePicker</c>)で行い、ここには「Down位置で何にヒットしたか」の
    /// 結果だけを渡す(状態機械はScenePicker/SceneRenderObjectを一切知らない、既存の純粋性方針
    /// を維持するため)。LMB Down以外のイベントでは無視される。
    /// </summary>
    internal SceneInputStepResult Handle(
        PointerEvent Event,NodePath?HitNodePath=null,GizmoHandleId?HitGizmoHandle=null){
        switch(State){
            case SceneNavState.Idle:
                if(IsButtonDown(Event,PointerButton.Right)){ return Begin(SceneNavState.Flying,Event);}
                if(IsButtonDown(Event,PointerButton.Middle)){ return Begin(SceneNavState.Panning,Event);}
                if(IsButtonDown(Event,PointerButton.Left)&&HitGizmoHandle is { }Handle){ return BeginGizmo(Event,Handle);}
                if(IsButtonDown(Event,PointerButton.Left)&&Event.Modifiers.Alt){ return Begin(SceneNavState.Orbiting,Event);}
                if(IsButtonDown(Event,PointerButton.Left)){ return BeginSelect(Event,HitNodePath);}
                return Stay();

            case SceneNavState.Flying:
                if(IsButtonUp(Event,PointerButton.Right)){ return End();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return Move(Event);}
                return Stay();

            case SceneNavState.Panning:
                if(IsButtonUp(Event,PointerButton.Middle)){ return End();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return Move(Event);}
                return Stay();

            case SceneNavState.Orbiting:
                if(IsButtonUp(Event,PointerButton.Left)){ return End();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return Move(Event);}
                return Stay();

            case SceneNavState.PendingSelect:
                // 仕様§12優先順位: Navigation(2) > Selection(4)。選択待ち中にRMB/MMBが来たら
                // 選択を中断してナビゲーションへ切り替えて良い(選択はまだ何も確定していない)。
                if(IsButtonDown(Event,PointerButton.Right)){ return Begin(SceneNavState.Flying,Event);}
                if(IsButtonDown(Event,PointerButton.Middle)){ return Begin(SceneNavState.Panning,Event);}
                if(IsButtonUp(Event,PointerButton.Left)){ return ConfirmSelect();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return LoseCapture();}
                if(Event.Kind==PointerEventKind.Move){ return TryBeginMarquee(Event);}
                return Stay();

            case SceneNavState.MarqueeSelecting:
                // ドラッグ開始後は操作を固定し、LMB UpまたはCaptureLostまでNavigationへ切り替えない。
                if(IsButtonUp(Event,PointerButton.Left)){ return ConfirmMarquee(Event);}
                if(Event.Kind==PointerEventKind.CaptureLost){ return LoseMarqueeCapture();}
                if(Event.Kind==PointerEventKind.Move){ return UpdateMarquee(Event);}
                return Stay();

            case SceneNavState.GizmoDragging:
                if(IsButtonUp(Event,PointerButton.Left)){ return CommitGizmo();}
                if(Event.Kind==PointerEventKind.CaptureLost){ return LoseGizmoCapture();}
                if(Event.Kind==PointerEventKind.Move){ return Move(Event);}
                return Stay();

            default:
                return Stay();
        }
    }

    /// <summary>Flying中はAdjustNavigationSpeed、それ以外(Idle含む)はDolly/Zoom(NavigationDistance)。</summary>
    internal SceneWheelAction HandleWheel()=>
        State==SceneNavState.Flying?SceneWheelAction.AdjustSpeed:SceneWheelAction.NavigationDistance;

    /// <summary>Flying中(RMB押下中)だけ押下集合へ追加する。仕様§5「RMB Navigation中はW/E/Q/A/S/Dを優先」。</summary>
    internal void HandleKeyDown(NavigationKey Key){
        if(State==SceneNavState.Flying){
            HeldKeys.Add(Key);
        }
    }

    internal void HandleKeyUp(NavigationKey Key)=>HeldKeys.Remove(Key);

    /// <summary>WPFの<c>LostKeyboardFocus</c>から呼ぶ。状態遷移はしない(RMBは離されていない)。</summary>
    internal void HandleLostKeyboardFocus()=>HeldKeys.Clear();

    internal SceneInputStepResult CancelCurrent(){
        if(State==SceneNavState.Idle){
            return Stay();
        }

        bool WasGizmo=State==SceneNavState.GizmoDragging;
        bool WasMarquee=State==SceneNavState.MarqueeSelecting;
        GizmoHandleId?Handle=ActiveGizmoHandle;
        State=SceneNavState.Idle;
        HeldKeys.Clear();
        PendingSelectNodePath=null;
        PendingSelectCtrl=false;
        ActiveGizmoHandle=null;
        return new(State,SceneCaptureAction.End,0f,0f,
            MarqueeCanceled:WasMarquee,
            GizmoCanceled:WasGizmo,GizmoHandle:Handle);
    }

    /// <summary><c>SceneHwndHost.DestroyWindowCore</c>から呼ぶ。</summary>
    internal void Reset(){
        HeldKeys.Clear();
        PendingSelectNodePath=null;
        PendingSelectCtrl=false;
        ActiveGizmoHandle=null;
        State=SceneNavState.Idle;
    }

    private static bool IsButtonDown(PointerEvent Event,PointerButton Button)=>
        Event.Kind==PointerEventKind.Down&&Event.Button==Button;

    private static bool IsButtonUp(PointerEvent Event,PointerButton Button)=>
        Event.Kind==PointerEventKind.Up&&Event.Button==Button;

    private SceneInputStepResult Begin(SceneNavState NewState,PointerEvent Event){
        State=NewState;
        LastX=Event.X;
        LastY=Event.Y;
        return new(State,SceneCaptureAction.Begin,0f,0f);
    }

    private SceneInputStepResult Move(PointerEvent Event){
        float DeltaX=Event.X-LastX;
        float DeltaY=Event.Y-LastY;
        LastX=Event.X;
        LastY=Event.Y;
        return new(State,SceneCaptureAction.None,DeltaX,DeltaY);
    }

    private SceneInputStepResult End(){
        State=SceneNavState.Idle;
        HeldKeys.Clear();
        return new(State,SceneCaptureAction.End,0f,0f);
    }

    private SceneInputStepResult BeginSelect(PointerEvent Event,NodePath?HitNodePath){
        State=SceneNavState.PendingSelect;
        PendingSelectNodePath=HitNodePath;
        PendingSelectCtrl=Event.Modifiers.Ctrl;
        SelectionStartX=Event.X;
        SelectionStartY=Event.Y;
        return new(State,SceneCaptureAction.Begin,0f,0f);
    }

    private SceneInputStepResult TryBeginMarquee(PointerEvent Event){
        if(PendingSelectNodePath is not null){
            return Stay();
        }

        int DeltaX=Event.X-SelectionStartX;
        int DeltaY=Event.Y-SelectionStartY;
        if((DeltaX*DeltaX)+(DeltaY*DeltaY)<MarqueeDragThresholdPixels*MarqueeDragThresholdPixels){
            return Stay();
        }

        State=SceneNavState.MarqueeSelecting;
        return new(State,SceneCaptureAction.None,0f,0f,
            MarqueeUpdated:true,
            MarqueeRectangle:ScreenRectangle.FromPoints(SelectionStartX,SelectionStartY,Event.X,Event.Y));
    }

    private SceneInputStepResult UpdateMarquee(PointerEvent Event)=>
        new(State,SceneCaptureAction.None,0f,0f,
            MarqueeUpdated:true,
            MarqueeRectangle:ScreenRectangle.FromPoints(SelectionStartX,SelectionStartY,Event.X,Event.Y));

    private SceneInputStepResult ConfirmMarquee(PointerEvent Event){
        ScreenRectangle Rectangle=ScreenRectangle.FromPoints(SelectionStartX,SelectionStartY,Event.X,Event.Y);
        bool ConfirmedCtrl=PendingSelectCtrl;
        State=SceneNavState.Idle;
        PendingSelectCtrl=false;
        return new(State,SceneCaptureAction.End,0f,0f,
            ConfirmedCtrl:ConfirmedCtrl,
            MarqueeSelectionConfirmed:true,
            MarqueeRectangle:Rectangle);
    }

    private SceneInputStepResult LoseMarqueeCapture(){
        State=SceneNavState.Idle;
        PendingSelectCtrl=false;
        return new(State,SceneCaptureAction.None,0f,0f,MarqueeCanceled:true);
    }

    private SceneInputStepResult BeginGizmo(PointerEvent Event,GizmoHandleId Handle){
        State=SceneNavState.GizmoDragging;
        LastX=Event.X;
        LastY=Event.Y;
        ActiveGizmoHandle=Handle;
        return new(State,SceneCaptureAction.Begin,0f,0f,GizmoDragBegan:true,GizmoHandle:Handle,
            GizmoDuplicate:Event.Modifiers.Alt);
    }

    private SceneInputStepResult CommitGizmo(){
        GizmoHandleId?Handle=ActiveGizmoHandle;
        ActiveGizmoHandle=null;
        State=SceneNavState.Idle;
        return new(State,SceneCaptureAction.End,0f,0f,GizmoCommitRequested:true,GizmoHandle:Handle);
    }

    private SceneInputStepResult LoseGizmoCapture(){
        GizmoHandleId?Handle=ActiveGizmoHandle;
        ActiveGizmoHandle=null;
        State=SceneNavState.Idle;
        return new(State,SceneCaptureAction.None,0f,0f,GizmoCanceled:true,GizmoHandle:Handle);
    }

    private SceneInputStepResult ConfirmSelect(){
        State=SceneNavState.Idle;
        NodePath?ConfirmedPath=PendingSelectNodePath;
        bool ConfirmedCtrl=PendingSelectCtrl;
        PendingSelectNodePath=null;
        PendingSelectCtrl=false;
        return new(State,SceneCaptureAction.End,0f,0f,SelectionConfirmed:true,ConfirmedPath,ConfirmedCtrl);
    }

    private SceneInputStepResult LoseCapture(){
        // 既にキャプチャは奪われているのでReleaseCaptureは呼ばない(Endとの違い)。
        State=SceneNavState.Idle;
        HeldKeys.Clear();
        PendingSelectNodePath=null;
        PendingSelectCtrl=false;
        return new(State,SceneCaptureAction.None,0f,0f);
    }

    private SceneInputStepResult Stay()=>new(State,SceneCaptureAction.None,0f,0f);
}

/// <summary>Wheelイベントの意味づけ。<see cref="SceneViewState"/>側がProjectionModeでDolly/Zoomを選ぶ。</summary>
internal enum SceneWheelAction { NavigationDistance, AdjustSpeed }
