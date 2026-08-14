using System.Collections.Generic;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ナビゲーション状態。<c>GizmoDragging</c>/<c>PendingSelect</c>はPhase 2/3で追加、
/// 今回(Phase 1)は実装しない。
/// </summary>
internal enum SceneNavState { Idle, Flying, Panning, Orbiting }

internal enum SceneCaptureAction { None, Begin, End }

/// <summary>
/// <see cref="SceneInputStateMachine.Handle"/>の戻り値。<see cref="DeltaX"/>/<see cref="DeltaY"/>は
/// <see cref="PointerEventKind.Move"/>由来のティックのみ非ゼロ。
/// </summary>
internal readonly struct SceneInputStepResult {
    internal SceneInputStepResult(SceneNavState State,SceneCaptureAction Capture,float DeltaX,float DeltaY){
        this.State=State;
        this.Capture=Capture;
        this.DeltaX=DeltaX;
        this.DeltaY=DeltaY;
    }

    internal SceneNavState State { get; }

    internal SceneCaptureAction Capture { get; }

    internal float DeltaX { get; }

    internal float DeltaY { get; }
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
    private int LastX;
    private int LastY;
    private readonly HashSet<NavigationKey>HeldKeys=[];

    internal SceneNavState State { get;private set;}=SceneNavState.Idle;

    internal IReadOnlyCollection<NavigationKey>FlyHeldKeys=>HeldKeys;

    internal SceneInputStepResult Handle(PointerEvent Event){
        switch(State){
            case SceneNavState.Idle:
                if(IsButtonDown(Event,PointerButton.Right)){ return Begin(SceneNavState.Flying,Event);}
                if(IsButtonDown(Event,PointerButton.Middle)){ return Begin(SceneNavState.Panning,Event);}
                if(IsButtonDown(Event,PointerButton.Left)&&Event.Modifiers.Alt){ return Begin(SceneNavState.Orbiting,Event);}
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

    /// <summary><c>SceneHwndHost.DestroyWindowCore</c>から呼ぶ。</summary>
    internal void Reset(){
        HeldKeys.Clear();
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

    private SceneInputStepResult LoseCapture(){
        // 既にキャプチャは奪われているのでReleaseCaptureは呼ばない(Endとの違い)。
        State=SceneNavState.Idle;
        HeldKeys.Clear();
        return new(State,SceneCaptureAction.None,0f,0f);
    }

    private SceneInputStepResult Stay()=>new(State,SceneCaptureAction.None,0f,0f);
}

/// <summary>Wheelイベントの意味づけ。<see cref="SceneViewState"/>側がProjectionModeでDolly/Zoomを選ぶ。</summary>
internal enum SceneWheelAction { NavigationDistance, AdjustSpeed }
