using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>開いている浮動アセットエディターフレームを追跡し、2つ目以降を既存ウィンドウの別タブへドッキングできるようにする。</summary>
internal static class AssetEditorFloatingTracker {
    private static readonly List<WeakReference<IVsWindowFrame>> Frames=new();
    private static readonly object Gate=new();
    internal static void Register(IVsWindowFrame Frame){
        if(Frame is null){ return; }
        lock(Gate){
            Clean();
            for(var I=0;I<Frames.Count;I++){
                if(Frames[I].TryGetTarget(out var Existing)&&ReferenceEquals(Existing,Frame)){ return; }
            }
            Frames.Add(new WeakReference<IVsWindowFrame>(Frame));
        }
    }
    internal static void Unregister(IVsWindowFrame Frame){
        if(Frame is null){ return; }
        lock(Gate){
            Frames.RemoveAll(Wr=>!Wr.TryGetTarget(out var Target)||ReferenceEquals(Target,Frame));
        }
    }
    internal static IVsWindowFrame?GetActiveFloatingFrame(IServiceProvider Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        lock(Gate){
            Clean();
            for(var I=Frames.Count-1;I>=0;I--){
                if(Frames[I].TryGetTarget(out var Frame)&&IsFrameAliveAndFloating(Frame,Services)){
                    return Frame;
                }
            }
            return null;
        }
    }
    private static bool IsFrameAliveAndFloating(IVsWindowFrame Frame,IServiceProvider Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            var Hr=Frame.IsVisible();
            if(Hr!=VSConstants.S_OK){ return false; }
            if(Frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView,out var View)==VSConstants.S_OK
                &&View is System.Windows.Media.Visual Visual){
                var Hosting=AssetEditorFrameFloater.DescribeHosting(Visual,Services);
                return Hosting=="Floating";
            }
            return true;
        } catch(Exception Error) when(Error is System.Runtime.InteropServices.COMException or InvalidOperationException){
            return false;
        }
    }
    private static void Clean(){
        Frames.RemoveAll(static Wr=>!Wr.TryGetTarget(out _));
    }
    internal static void ResetForTesting(){
        lock(Gate){ Frames.Clear(); }
    }
}