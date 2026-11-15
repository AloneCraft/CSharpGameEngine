using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>Ctrl/Shift/Altの押下状態。マウスwParamのMKフラグは使わず、全ポインタイベントで
/// <c>GetKeyState</c>から統一的に読む(AltはそもそもMKフラグに存在しないため)。</summary>
internal readonly struct RawInputModifiers:IEquatable<RawInputModifiers> {
    internal RawInputModifiers(bool Shift,bool Ctrl,bool Alt){
        this.Shift=Shift;
        this.Ctrl=Ctrl;
        this.Alt=Alt;
    }

    internal bool Shift { get; }

    internal bool Ctrl { get; }

    internal bool Alt { get; }

    public bool Equals(RawInputModifiers Other)=> this.Shift==Other.Shift&& this.Ctrl==Other.Ctrl&& this.Alt==Other.Alt;

    public override bool Equals(object?Obj)=>Obj is RawInputModifiers Other&& this.Equals(Other);

    public override int GetHashCode()=>(this.Shift, this.Ctrl, this.Alt).GetHashCode();
}

internal enum PointerButton { None, Left, Right, Middle }

internal enum PointerEventKind { Down, Up, Move, CaptureLost }

/// <summary>座標は物理ピクセル(サーフェスのクライアント座標)。CaptureLostはX/Y/Buttonを持たない。</summary>
internal readonly struct PointerEvent:IEquatable<PointerEvent> {
    internal PointerEvent(PointerEventKind Kind,PointerButton Button,int X,int Y,RawInputModifiers Modifiers){
        this.Kind=Kind;
        this.Button=Button;
        this.X=X;
        this.Y=Y;
        this.Modifiers=Modifiers;
    }

    internal PointerEventKind Kind { get; }

    internal PointerButton Button { get; }

    internal int X { get; }

    internal int Y { get; }

    internal RawInputModifiers Modifiers { get; }

    public bool Equals(PointerEvent Other)=> this.Kind==Other.Kind&& this.Button==Other.Button&& this.X==Other.X&& this.Y==Other.Y&& this.Modifiers.Equals(Other.Modifiers);

    public override bool Equals(object?Obj)=>Obj is PointerEvent Other&& this.Equals(Other);

    public override int GetHashCode()=>(this.Kind, this.Button, this.X, this.Y, this.Modifiers).GetHashCode();
}

/// <summary>
/// Win32マウスメッセージを<see cref="PointerEvent"/>へ変換する純粋関数。WPF/Win32型に依存せず
/// <see cref="RenderSurfaceWindow.WndProc"/>から呼ばれる想定だが、それ自体は知らない
/// (ユニットテストで固定できるようにするための分離)。
/// </summary>
internal static class RenderSurfaceMouseMessageTranslator {
    /// <summary>
    /// <paramref name="SelfHwnd"/>は<c>WM_CAPTURECHANGED</c>の自己再取得を見分けるためだけに使う。
    /// このメッセージのLParamは「キャプチャを取得する側」のハンドルなので、それが自分自身なら
    /// 奪われたのではなく自分で取り直しただけであり<see cref="PointerEventKind.CaptureLost"/>を
    /// 発生させてはならない(発生させるとSceneInputStateMachineがIdleへ戻り、
    /// PendingSelectからのナビゲーションへのプリエンプトが不発になる —
    /// <see cref="RenderSurfaceWindow.SetPointerCapture"/>参照)。
    ///
    /// <para>
    /// 既定の<c>IntPtr.Zero</c>はこの判定を無効化する(自己ハンドルが不明な呼び出し向け)。
    /// LParamが<c>IntPtr.Zero</c>の場合は「どのウィンドウもキャプチャを取得しない」= 完全な解放を
    /// 意味するので、<paramref name="SelfHwnd"/>が有効なときも<c>CaptureLost</c>として扱う必要が
    /// ある。そのため一致判定は<paramref name="SelfHwnd"/>が非ゼロの場合のみ行う。
    /// </para>
    /// </summary>
    internal static PointerEvent?Translate(
        uint Message,IntPtr LParam,RawInputModifiers Modifiers,IntPtr SelfHwnd=default)=>Message switch {
        NativeMethods.WM_LBUTTONDOWN=>Down(PointerButton.Left,LParam,Modifiers),
        NativeMethods.WM_LBUTTONUP=>Up(PointerButton.Left,LParam,Modifiers),
        NativeMethods.WM_RBUTTONDOWN=>Down(PointerButton.Right,LParam,Modifiers),
        NativeMethods.WM_RBUTTONUP=>Up(PointerButton.Right,LParam,Modifiers),
        NativeMethods.WM_MBUTTONDOWN=>Down(PointerButton.Middle,LParam,Modifiers),
        NativeMethods.WM_MBUTTONUP=>Up(PointerButton.Middle,LParam,Modifiers),
        NativeMethods.WM_MOUSEMOVE=>new PointerEvent(
            PointerEventKind.Move,PointerButton.None,DecodeX(LParam),DecodeY(LParam),Modifiers),
        NativeMethods.WM_CAPTURECHANGED=>SelfHwnd!=IntPtr.Zero&&LParam==SelfHwnd
            ?null
            :new PointerEvent(PointerEventKind.CaptureLost,PointerButton.None,0,0,default),
        _=>null,
    };

    private static PointerEvent Down(PointerButton Button,IntPtr LParam,RawInputModifiers Modifiers)=>
        new(PointerEventKind.Down,Button,DecodeX(LParam),DecodeY(LParam),Modifiers);

    private static PointerEvent Up(PointerButton Button,IntPtr LParam,RawInputModifiers Modifiers)=>
        new(PointerEventKind.Up,Button,DecodeX(LParam),DecodeY(LParam),Modifiers);

    // WM_SIZEの幅・高さは非負なので無符号抽出で正しいが(RenderSurfaceWindow.WndProc参照)、
    // マウス座標はキャプチャ中にウィンドウ外へドラッグされると負になりうる符号付き値。
    // (short)キャストを省くと負の座標が65535近辺の巨大な正の値へ化けるバグになる。
    internal static int DecodeX(IntPtr LParam)=>unchecked((short)(LParam.ToInt64()&0xFFFF));

    internal static int DecodeY(IntPtr LParam)=>unchecked((short)((LParam.ToInt64()>>16)&0xFFFF));

    /// <summary>WM_MOUSEWHEELのwParam上位ワードは符号付きの回転量(WHEEL_DELTA=120単位)。</summary>
    internal static int DecodeWheelDelta(IntPtr WParam)=>unchecked((short)((WParam.ToInt64()>>16)&0xFFFF));
}
