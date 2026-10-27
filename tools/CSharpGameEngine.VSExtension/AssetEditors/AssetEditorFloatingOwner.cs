using System;
using System.Runtime.InteropServices;
namespace CSharpGameEngine.VSExtension;
/// <summary>浮動ドキュメントをVS本体の所有ウィンドウにし、所有者より上に保つ。全アプリ最前面やアクティブ化は使わない。</summary>
internal static class AssetEditorFloatingOwner {
    private const int OwnerIndex=-8;
    internal static bool Apply(IntPtr ContentWindow,IntPtr MainWindow){
        var Root=GetAncestor(ContentWindow,2);
        var Main=GetAncestor(MainWindow,2);
        if(Root==IntPtr.Zero||Main==IntPtr.Zero||Root==Main){ return false; }
        GetWindowThreadProcessId(Root,out var Process);
        GetWindowThreadProcessId(Main,out var MainProcess);
        if(Process!=GetCurrentProcessId()||MainProcess!=Process){ return false; }
        if(GetWindow(Root,4)==Main){ return true; }
        SetLastError(0);
        var Previous=IntPtr.Size==8?SetWindowLongPtr(Root,OwnerIndex,Main):(IntPtr)SetWindowLong(Root,OwnerIndex,Main.ToInt32());
        var Error=Marshal.GetLastWin32Error();
        if(Previous==IntPtr.Zero&&Error!=0){ throw new System.ComponentModel.Win32Exception(Error); }
        return GetWindow(Root,4)==Main;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr Window,uint Flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr Window,uint Command);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr Window,out uint Process);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint Error);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr Window,int Index,IntPtr Value);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)]
    private static extern int SetWindowLong(IntPtr Window,int Index,int Value);
}
