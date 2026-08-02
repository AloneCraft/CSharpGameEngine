using System;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.VSExtension;

/// <summary>Thin Win32 event-object wrapper for <see cref="VorticeRenderer"/>'s fence sync - Vortice.Windows has no managed wrapper for this.</summary>
internal static class NativeEvent
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, [MarshalAs(UnmanagedType.Bool)] bool bManualReset, [MarshalAs(UnmanagedType.Bool)] bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint Infinite = 0xFFFFFFFF;

    public static IntPtr CreateAutoResetEvent()
    {
        IntPtr handle = CreateEvent(IntPtr.Zero, false, false, null);
        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateEvent failed with Win32 error 0x{error:X8}.");
        }

        return handle;
    }

    public static void Wait(IntPtr eventHandle) => WaitForSingleObject(eventHandle, Infinite);

    public static void Close(IntPtr eventHandle) => CloseHandle(eventHandle);
}
