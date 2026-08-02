using System;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Minimal hand-written Win32 P/Invoke surface for the native window that hosts the in-process
/// D3D12 swap chain (ADR-0008). <c>TerraFX.Interop.Windows</c> (used elsewhere in this repo)
/// targets <c>net10.0</c> only and cannot be referenced from this <c>net472</c> project.
/// </summary>
internal static class NativeMethods
{
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;

    // Required for a DXGI flip-model swap chain (mandatory for D3D12) to actually composite when
    // targeting a WS_CHILD window instead of a normal top-level window - without this, WDM's
    // default redirection bitmap for the child window intercepts/hides the swap chain's own
    // presented surface (confirmed empirically - see ADR-0008).
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    public const uint WM_SIZE = 0x0005;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
