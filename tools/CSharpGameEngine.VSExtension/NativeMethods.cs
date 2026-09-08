using System;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Minimal hand-written Win32 P/Invoke surface for the native window that hosts the in-process
/// D3D12 swap chain (ADR-0008). <c>TerraFX.Interop.Windows</c> (used elsewhere in this repo)
/// targets <c>net10.0</c> only and cannot be referenced from this <c>net472</c> project.
/// </summary>
internal static class NativeMethods {
    public const uint WS_CHILD=0x40000000;
    public const uint WS_VISIBLE=0x10000000;

    // Required for a DXGI flip-model swap chain (mandatory for D3D12) to actually composite when
    // targeting a WS_CHILD window instead of a normal top-level window - without this, WDM's
    // default redirection bitmap for the child window intercepts/hides the swap chain's own
    // presented surface (confirmed empirically - see ADR-0008).
    public const uint WS_EX_NOREDIRECTIONBITMAP=0x00200000;

    public const uint WM_SIZE=0x0005;
    public const uint WM_SETFOCUS=0x0007;
    public const int WM_KEYDOWN=0x0100;
    public const int WM_KEYUP=0x0101;
    public const uint WM_MOUSEACTIVATE=0x0021;
    public const int MA_NOACTIVATE=3;
    public const int SW_HIDE=0;
    public const int SW_SHOWNA=8;

    // M0020 Phase 1(ADR-0041 決定事項2): マウスのボタン・移動はここで受ける。WM_MOUSEACTIVATEが
    // MA_NOACTIVATEでもヒットテストで子HWNDへ届く。
    public const uint WM_MOUSEMOVE=0x0200;
    public const uint WM_LBUTTONDOWN=0x0201;
    public const uint WM_LBUTTONUP=0x0202;
    public const uint WM_RBUTTONDOWN=0x0204;
    public const uint WM_RBUTTONUP=0x0205;
    public const uint WM_MBUTTONDOWN=0x0207;
    public const uint WM_MBUTTONUP=0x0208;

    // 設計時はWM_MOUSEWHEELを「フォーカスを持つHWND宛」と想定しWPF側のPreviewMouseWheelで
    // 受ける計画だったが、実機のExperimental Instanceで一切発火しないことが判明した(VS自体が
    // カーソル直下のHWNDへホイールをリダイレクトする挙動を持つため、他のマウスメッセージ同様に
    // ヒットテストでこの子HWNDへ届くと見られる)。他のマウスメッセージと同じくここで受ける
    // (M0020 Phase 1 実機検証、ADR-0041訂正済み)。
    public const uint WM_MOUSEWHEEL=0x020A;

    // Alt+Tabやモーダルダイアログ等でキャプチャが奪われた通知。これを処理しないと、対応するボタン
    // Upが二度と届かず入力状態機械がドラッグ状態に永久固着する。
    public const uint WM_CAPTURECHANGED=0x0215;

    public const int VK_SHIFT=0x10;
    public const int VK_ESCAPE=0x1B;
    public const int VK_CONTROL=0x11;
    public const int VK_MENU=0x12;
    public const int VK_S=0x53;
    public const int VK_V=0x56;
    public const int VK_Y=0x59;
    public const int VK_Z=0x5A;

    // WNDCLASSEX.HCursorへ渡す標準矢印カーソル。MAKEINTRESOURCE(32512)であり実ポインタではない。
    public static readonly IntPtr IDC_ARROW=new(32512);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursor(IntPtr HInstance,IntPtr LpCursorName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr WndProcDelegate(IntPtr HWnd,uint Msg,IntPtr WParam,IntPtr LParam);

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    public struct WNDCLASSEX {
        public uint CbSize;
        public uint Style;
        public WndProcDelegate LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]public string?LpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]public string LpszClassName;
        public IntPtr HIconSm;
    }

    [DllImport("user32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX Lpwcx);

    [DllImport("user32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint DwExStyle,
        string LpClassName,
        string LpWindowName,
        uint DwStyle,
        int X,
        int Y,
        int NWidth,
        int NHeight,
        IntPtr HWndParent,
        IntPtr HMenu,
        IntPtr HInstance,
        IntPtr LpParam);

    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr HWnd,uint Msg,IntPtr WParam,IntPtr LParam);

    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr HWnd);

    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string?LpModuleName);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr HWnd);
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr HWnd);

    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr HWnd,int NCmdShow);

    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(
        IntPtr HWnd,int X,int Y,int Width,int Height,
        [MarshalAs(UnmanagedType.Bool)]bool Repaint);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr HWnd);

    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int VirtualKey);
}
