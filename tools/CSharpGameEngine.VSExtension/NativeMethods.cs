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
}
