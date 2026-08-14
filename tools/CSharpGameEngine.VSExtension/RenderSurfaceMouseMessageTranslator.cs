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

    public bool Equals(RawInputModifiers Other)=>Shift==Other.Shift&&Ctrl==Other.Ctrl&&Alt==Other.Alt;

    public override bool Equals(object?Obj)=>Obj is RawInputModifiers Other&&Equals(Other);

    public override int GetHashCode()=>(Shift,Ctrl,Alt).GetHashCode();
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

    public bool Equals(PointerEvent Other)=>
        Kind==Other.Kind&&Button==Other.Button&&X==Other.X&&Y==Other.Y&&Modifiers.Equals(Other.Modifiers);

    public override bool Equals(object?Obj)=>Obj is PointerEvent Other&&Equals(Other);

    public override int GetHashCode()=>(Kind,Button,X,Y,Modifiers).GetHashCode();
}

/// <summary>
/// Win32マウスメッセージを<see cref="PointerEvent"/>へ変換する純粋関数。WPF/Win32型に依存せず
/// <see cref="RenderSurfaceWindow.WndProc"/>から呼ばれる想定だが、それ自体は知らない
/// (ユニットテストで固定できるようにするための分離)。
/// </summary>
internal static class RenderSurfaceMouseMessageTranslator {
    internal static PointerEvent?Translate(uint Message,IntPtr LParam,RawInputModifiers Modifiers)=>Message switch {
        NativeMethods.WM_LBUTTONDOWN=>Down(PointerButton.Left,LParam,Modifiers),
        NativeMethods.WM_LBUTTONUP=>Up(PointerButton.Left,LParam,Modifiers),
        NativeMethods.WM_RBUTTONDOWN=>Down(PointerButton.Right,LParam,Modifiers),
        NativeMethods.WM_RBUTTONUP=>Up(PointerButton.Right,LParam,Modifiers),
        NativeMethods.WM_MBUTTONDOWN=>Down(PointerButton.Middle,LParam,Modifiers),
        NativeMethods.WM_MBUTTONUP=>Up(PointerButton.Middle,LParam,Modifiers),
        NativeMethods.WM_MOUSEMOVE=>new PointerEvent(
            PointerEventKind.Move,PointerButton.None,DecodeX(LParam),DecodeY(LParam),Modifiers),
        NativeMethods.WM_CAPTURECHANGED=>new PointerEvent(
            PointerEventKind.CaptureLost,PointerButton.None,0,0,default),
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
