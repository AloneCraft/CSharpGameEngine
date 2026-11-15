using System;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.Vsix.Automation;

internal sealed class OleMessageFilter:IOleMessageFilter,IDisposable {
    private const int RetryLater=2;
    private const int RetryDelayMilliseconds=100;
    private const int MaximumCallRetryMilliseconds=2000;
    private IOleMessageFilter?Previous;
    private bool IsRegistered;
    private OleMessageFilter() { }
    internal static OleMessageFilter Register() {
        var Filter=new OleMessageFilter();
        var Result=CoRegisterMessageFilter(Filter,out Filter.Previous);
        Marshal.ThrowExceptionForHR(Result);
        Filter.IsRegistered=true;
        return Filter;
    }
    internal static int GetRetryDelay(int RejectType,int ElapsedMilliseconds) {
        return RejectType==RetryLater
            &&ElapsedMilliseconds<MaximumCallRetryMilliseconds
            ?RetryDelayMilliseconds:-1;
    }
    public void Dispose() {
        if(!IsRegistered) { return; }
        var Result=CoRegisterMessageFilter(Previous,out _);
        IsRegistered=false;
        Previous=null;
        Marshal.ThrowExceptionForHR(Result);
    }
    int IOleMessageFilter.HandleInComingCall(
        int CallType,IntPtr TaskCaller,int TickCount,IntPtr InterfaceInfo) {
        return 0;
    }
    int IOleMessageFilter.RetryRejectedCall(
        IntPtr TaskCallee,int TickCount,int RejectType) {
        return GetRetryDelay(RejectType,TickCount);
    }
    int IOleMessageFilter.MessagePending(
        IntPtr TaskCallee,int TickCount,int PendingType) {
        return 2;
    }
    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(
        IOleMessageFilter?NewFilter,out IOleMessageFilter?OldFilter);
}

[ComVisible(true)]
[Guid("00000016-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter {
    [PreserveSig]
    int HandleInComingCall(
        int CallType,IntPtr TaskCaller,int TickCount,IntPtr InterfaceInfo);
    [PreserveSig]
    int RetryRejectedCall(IntPtr TaskCallee,int TickCount,int RejectType);
    [PreserveSig]
    int MessagePending(IntPtr TaskCallee,int TickCount,int PendingType);
}
