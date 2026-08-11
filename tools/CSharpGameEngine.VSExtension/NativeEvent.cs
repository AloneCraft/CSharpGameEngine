using System;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.VSExtension;

/// <summary>Thin Win32 event-object wrapper for <see cref="VorticeRenderer"/>'s fence sync - Vortice.Windows has no managed wrapper for this.</summary>
internal static class NativeEvent {
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern IntPtr CreateEvent(IntPtr LpEventAttributes,[MarshalAs(UnmanagedType.Bool)]bool BManualReset,[MarshalAs(UnmanagedType.Bool)]bool BInitialState,string?LpName);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr HHandle,uint DwMilliseconds);

    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr HObject);

    private const uint Infinite=0xFFFFFFFF;

    public static IntPtr CreateAutoResetEvent(){
        IntPtr Handle=CreateEvent(IntPtr.Zero,false,false,null);
        if(Handle==IntPtr.Zero){
            var Error=Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateEvent failed with Win32 error 0x{Error:X8}.");
        }

        return Handle;
    }

    public static void Wait(IntPtr EventHandle)=>WaitForSingleObject(EventHandle,Infinite);

    public static void Close(IntPtr EventHandle)=>CloseHandle(EventHandle);
}
