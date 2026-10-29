using System;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>フレームを浮かせた結果。実機検証でどの経路が効いたかを ActivityLog に残すために経路名を持つ。</summary>
internal readonly struct AssetEditorFloatResult(bool Floating,string Route,string Detail) {
    internal bool Floating { get;}=Floating;
    internal string Route { get;}=Route;
    internal string Detail { get;}=Detail;
}
/// <summary>
/// ドキュメントフレームを前面のフローティングウィンドウにする（UE5 のアセットエディター相当）。
/// 実機（VS 18、ドキュメントフレーム、M0127 のスパイク）で確認した事実:
/// 1) <c>VSFPROPID_FrameMode=VSFM_Float</c> はフレーム表示前（<c>OnCreate</c>）では <c>E_UNEXPECTED</c>、表示後（<c>OnShow</c>）に S_OK で浮く。
/// 2) 浮いた後も、<c>GetProperty(VSFPROPID_FrameMode)</c> と EnvDTE <c>Window.IsFloating</c> は文書フレームでは「浮いていない」を返し続ける。
///    したがって成功の判定は読み戻しではなく <c>SetProperty</c> の HRESULT で行い、読み戻し値は診断として記録するだけにする。
/// 位置・サイズは <c>SetFramePos</c>、効かなければ EnvDTE の Left/Top/Width/Height を使う。どれも失敗した場合は VS 既定の
/// 大きさを受け入れる（M0123: 新しい Assets フレームでは <c>SetFramePos</c> が <c>E_NOTIMPL</c> を返した前例がある）。
/// </summary>
internal static class AssetEditorFrameFloater {
    internal static string ReadMode(IVsWindowFrame Frame){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Hr=Frame.GetProperty((int)__VSFPROPID.VSFPROPID_FrameMode,out var Mode);
        return ErrorHandler.Succeeded(Hr)?Convert.ToString(Mode,System.Globalization.CultureInfo.InvariantCulture)??"null":"hr=0x"+Hr.ToString("X8");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr Handle,uint Flags);
    private const uint GaRoot=2;
    /// <summary>
    /// このコンテンツがどこにホストされているか。<c>Main</c> は VS メインウィンドウ内（ドキュメントタブ）、<c>Floating</c> は独立した最上位ウィンドウ（浮動）、
    /// <c>Unknown</c> はまだ表示されていない。文書フレームは浮いても <c>FrameMode</c>／<c>IsFloating</c> が変わらないので、ウィンドウの親子関係で判定する。
    /// </summary>
    internal static string DescribeHosting(System.Windows.Media.Visual?Content,IServiceProvider Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Content is null||System.Windows.PresentationSource.FromVisual(Content) is not System.Windows.Interop.HwndSource Source){ return "Unknown"; }
        if(Services.GetService(typeof(SDTE)) is not DTE Dte){ return "Unknown"; }
        var Root=GetAncestor(Source.Handle,GaRoot);
        return Root==(IntPtr)Dte.MainWindow.HWnd?"Main":"Floating";
    }
    internal static AssetEditorFloatResult TryFloat(IVsWindowFrame Frame,IServiceProvider Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Before=ReadMode(Frame);
        var Hr=Frame.SetProperty((int)__VSFPROPID.VSFPROPID_FrameMode,(int)VSFRAMEMODE.VSFM_Float);
        if(ErrorHandler.Failed(Hr)){ return new(false,"None","FrameMode hr=0x"+Hr.ToString("X8")+" before="+Before); }
        return new(true,"FrameMode","before="+Before+" after="+ReadMode(Frame));
    }
    /// <summary>既存の浮動フレームと同じタブグループ（コンテナ）へ別タブとしてドッキングする。</summary>
    internal static AssetEditorFloatResult TryDockToExistingFloating(IVsWindowFrame Target,IVsWindowFrame Existing,System.Windows.Media.Visual? TargetContent,IServiceProvider Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            var TargetView=ResolveViewElement(Target,TargetContent);
            var ExistingView=ResolveViewElement(Existing,null);
            if(TargetView is not null&&ExistingView is not null){
                var ViewElementType=ExistingView.GetType();
                var DockOpsType=ViewElementType.Assembly.GetType("Microsoft.VisualStudio.PlatformUI.Shell.DockOperations")
                    ??System.Type.GetType("Microsoft.VisualStudio.PlatformUI.Shell.DockOperations, Microsoft.VisualStudio.Shell.ViewManager");
                if(DockOpsType is not null){
                    var DockMethod=System.Linq.Enumerable.FirstOrDefault(
                        DockOpsType.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static),
                        M=>M.Name=="Dock"&&M.GetParameters().Length==3);
                    if(DockMethod is not null){
                        // Dock(targetView, dockingView, dockDirection: 0=Fill)
                        DockMethod.Invoke(null,[ExistingView,TargetView,0]);
                        Target.Show();
                        return new(true,"DockOperations.Dock","docked as tab into existing floating group");
                    }
                }
                var TargetParentProp=TargetView.GetType().GetProperty("Parent");
                var ExistingParentProp=ExistingView.GetType().GetProperty("Parent");
                if(TargetParentProp?.GetValue(TargetView) is{ }TargetGroup&&ExistingParentProp?.GetValue(ExistingView) is{ }ExistingGroup){
                    var TargetChildrenProp=TargetGroup.GetType().GetProperty("Children");
                    var ExistingChildrenProp=ExistingGroup.GetType().GetProperty("Children");
                    if(TargetChildrenProp?.GetValue(TargetGroup) is System.Collections.IList TargetList
                        &&ExistingChildrenProp?.GetValue(ExistingGroup) is System.Collections.IList ExistingList){
                        TargetList.Remove(TargetView);
                        ExistingList.Add(TargetView);
                        var SelectedProp=ExistingGroup.GetType().GetProperty("SelectedElement");
                        SelectedProp?.SetValue(ExistingGroup,TargetView);
                        Target.Show();
                        return new(true,"DocumentGroup.Children","added to existing floating document group");
                    }
                }
            }
        } catch(Exception Error){
            ActivityLog.LogWarning(nameof(AssetEditorFrameFloater),$"Native dock attempt failed: {Error.GetType().Name} {Error.Message}");
        }
        const int VsfpropidMdiContainerId=-5010;
        var ContainerHr=Existing.GetProperty(VsfpropidMdiContainerId,out var ContainerId);
        if(ErrorHandler.Succeeded(ContainerHr)&&ContainerId is not null){
            var SetHr=Target.SetProperty(VsfpropidMdiContainerId,ContainerId);
            if(ErrorHandler.Succeeded(SetHr)){
                Target.Show();
                return new(true,"MDIContainerID","containerId="+ContainerId);
            }
        }
        try {
            var ExWinHr=Existing.GetProperty((int)__VSFPROPID.VSFPROPID_ExtWindowObject,out var ExWinObj);
            var TgWinHr=Target.GetProperty((int)__VSFPROPID.VSFPROPID_ExtWindowObject,out var TgWinObj);
            if(ErrorHandler.Succeeded(ExWinHr)&&ErrorHandler.Succeeded(TgWinHr)
                &&ExWinObj is Window ExWin&&TgWinObj is Window TgWin){
                var LinkedFrame=ExWin.LinkedWindowFrame;
                if(LinkedFrame is not null){
                    LinkedFrame.LinkedWindows.Add(TgWin);
                    Target.Show();
                    return new(true,"LinkedWindows.Add","added to existing linked frame");
                }
                var Created=ExWin.DTE.Windows.CreateLinkedWindowFrame(ExWin,TgWin,vsLinkedWindowType.vsLinkedWindowTypeTabbed);
                if(Created is not null){
                    Target.Show();
                    return new(true,"CreateLinkedWindowFrame","tabbed linked frame created");
                }
            }
        } catch(Exception Error) when(Error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException){
            ActivityLog.LogWarning(nameof(AssetEditorFrameFloater),$"DTE linked frame fallback failed: {Error.Message}");
        }
        return new(false,"None","docking failed (containerHr=0x"+ContainerHr.ToString("X8")+")");
    }
    private static object? ResolveViewElement(IVsWindowFrame Frame,System.Windows.Media.Visual? Content){
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            var Prop=Frame.GetType().GetProperty("FrameView",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            if(Prop?.GetValue(Frame) is{ }View){ return View; }
            var Field=Frame.GetType().GetField("innerFrame",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
                ??Frame.GetType().GetField("_frame",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            if(Field?.GetValue(Frame) is{ }Inner){
                var InnerProp=Inner.GetType().GetProperty("FrameView",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                if(InnerProp?.GetValue(Inner) is{ }InnerView){ return InnerView; }
            }
        } catch {}
        if(Content is not null){
            try {
                var Current=Content as System.Windows.DependencyObject;
                while(Current is not null){
                    if(Current is System.Windows.FrameworkElement Fe){
                        var Dc=Fe.DataContext;
                        if(Dc is not null&&Dc.GetType().FullName=="Microsoft.VisualStudio.Platform.WindowManagement.WMView"){
                            return Dc;
                        }
                    }
                    Current=System.Windows.Media.VisualTreeHelper.GetParent(Current);
                }
            } catch {}
        }
        return null;
    }
    /// <summary>
    /// 浮いたフレームを中央・希望サイズに置く。浮かせた直後（WPF が最初の描画を終える前）にサイズを変えると、フローティングウィンドウの WPF 部分が
    /// 描画されない状態を以前に観測したため、呼び出し側（<see cref="AssetEditorFramePlacement"/>）が暫定的に遅延して呼ぶ。全面描画の最終受入は未完了。
    /// </summary>
    internal static string Place(IVsWindowFrame Frame,IServiceProvider Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        Place(Frame,Services,out var Detail);
        return Detail;
    }
    private static void Place(IVsWindowFrame Frame,IServiceProvider Services,out string Detail){
        ThreadHelper.ThrowIfNotOnUIThread();
        Detail=string.Empty;
        try {
            if(Services.GetService(typeof(SDTE)) is not DTE Dte){ Detail="DTE unavailable"; return; }
            var Main=Dte.MainWindow;
            var Bounds=AssetEditorOpenLocationPolicy.CenteredBounds(Main.Left,Main.Top,Main.Width,Main.Height);
            var Relative=Guid.Empty;
            var Hr=Frame.SetFramePos(VSSETFRAMEPOS.SFP_fMove|VSSETFRAMEPOS.SFP_fSize,ref Relative,Bounds.Left,Bounds.Top,Bounds.Width,Bounds.Height);
            if(ErrorHandler.Succeeded(Hr)){ Detail="SetFramePos ok"; return; }
            Detail="SetFramePos hr=0x"+Hr.ToString("X8");
            if(VsShellUtilities.GetWindowObject(Frame) is Window Window){
                Window.Left=Bounds.Left;
                Window.Top=Bounds.Top;
                Window.Width=Bounds.Width;
                Window.Height=Bounds.Height;
                Detail+="; DTE bounds set";
            }
        } catch(Exception Error) when(Error is System.Runtime.InteropServices.COMException or InvalidOperationException or NotImplementedException){
            Detail+="; place "+Error.GetType().Name+": "+Error.Message;
        }
    }
}
