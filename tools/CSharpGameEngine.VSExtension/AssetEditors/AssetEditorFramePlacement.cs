using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// アセットエディターのフレームを、表示後に一度だけ設定に従って浮かせる（既定はフローティング、<see cref="AssetEditorOpenLocation"/>）。
/// 成功した時点で配置済みとし、ユーザーがドッキングした後の再表示・再アクティブ化では浮かせ直さない。
/// 表示前（<c>OnCreate</c>）の <c>SetProperty(FrameMode)</c> は <c>E_UNEXPECTED</c> になるので、
/// <c>IVsWindowFrameNotify3.OnShow</c> のタイミングで <see cref="OnShow"/> を呼ぶ（実機確認、<see cref="AssetEditorFrameFloater"/>）。
/// </summary>
internal sealed class AssetEditorFramePlacement(System.IServiceProvider Services,Func<string> Describe,Func<System.Windows.Media.Visual?> Content):IDisposable {
    private const int MaximumPlacementAttempts=3;
    /// <summary>以前の不完全描画の観測に対する暫定遅延。最初の描画完了を保証する値ではない。</summary>
    private const int BoundsDelay=1200;
    private JoinableTask?BoundsTask;
    private JoinableTask?OwnerTask;
    private System.Windows.FrameworkElement?Element;
    private bool Disposed;
    private IVsWindowFrame?Frame;
    private bool PlacementDone;
    private int PlacementAttempts;
    internal bool IsDone=>this.PlacementDone;
    internal void Attach(IVsWindowFrame Frame){
        this.Frame=Frame;
        if(this.Element is null&&Content() is System.Windows.FrameworkElement Element){
            this.Element=Element;
            Element.Loaded+=this.OnLoaded;
            System.Windows.PresentationSource.AddSourceChangedHandler(Element,this.OnSourceChanged);
        }
    }
    private void OnLoaded(object Sender,System.Windows.RoutedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.OnHostingChanged();
    }
    private void OnSourceChanged(object Sender,System.Windows.SourceChangedEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.OnHostingChanged();
    }
    internal void OnHostingChanged(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){ return; }
        this.UpdateOwner();
        // ドッキングの移動中は新しいHwndSourceがまだ接続されていない場合がある。
        if(this.OwnerTask is { IsCompleted:false }){ return; }
        this.OwnerTask=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
            await Task.Yield();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!this.Disposed){ this.UpdateOwner(); }
        });
    }
    private void UpdateOwner(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Content() is not{ }Visual||System.Windows.PresentationSource.FromVisual(Visual) is not System.Windows.Interop.HwndSource Source
            ||Services.GetService(typeof(Microsoft.VisualStudio.Shell.Interop.SDTE)) is not EnvDTE.DTE Dte){ return; }
        try { AssetEditorFloatingOwner.Apply(Source.Handle,(IntPtr)Dte.MainWindow.HWnd); }
        catch(System.ComponentModel.Win32Exception Error){ ActivityLog.LogError(nameof(AssetEditorFramePlacement),$"Asset editor owner '{Describe()}': {Error.Message}"); }
    }
    /// <summary>
    /// VS が保存済みのレイアウトから復元した文書、または「プログラムから開く」で特定のエディターが選ばれた文書は、配置を触らない（ユーザーが保存した位置・ドッキングを尊重する）。
    /// 判定は <see cref="AssetEditorFramePlacement.IsShellRestoreOrSpecificEditor"/>。
    /// </summary>
    internal void RespectSavedPlacement(){
        this.PlacementDone=true;
        ActivityLog.LogInformation(nameof(AssetEditorFramePlacement),$"Asset editor placement skipped for '{Describe()}': restored or opened with a specific editor; the saved window placement is kept.");
    }
    /// <summary>
    /// 文書の作成フラグが「復元／特定エディターで開く」か。実機（VS 18）の観測: 通常のオープン（DTE の OpenFile、ダブルクリック相当）は <c>CEF_OPENFILE</c>（0x2）だけ、
    /// 起動時のレイアウト復元は <c>CEF_OPENSPECIFIC</c>（0x10）が加わる（0x12）。
    /// </summary>
    internal static bool IsShellRestoreOrSpecificEditor(uint CreateFlags)=>(CreateFlags&(uint)__VSCREATEEDITORFLAGS2.CEF_OPENSPECIFIC)!=0;
    internal void OnShow(int Show){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Show is (int)__FRAMESHOW.FRAMESHOW_WinShown or (int)__FRAMESHOW.FRAMESHOW_TabActivated){ this.Place();this.OnHostingChanged(); }
    }
    private void Place(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.PlacementDone||this.Frame is not{ }Target){ return; }
        var Hosting=AssetEditorFrameFloater.DescribeHosting(Content(),Services);
        ActivityLog.LogInformation(nameof(AssetEditorFramePlacement),$"Asset editor placement check '{Describe()}': hosting={Hosting}.");
        var Setting=SceneEditorSettings.ReadAssetEditorOpenLocation();
        if(!AssetEditorOpenLocationPolicy.ShouldFloat(Setting,AlreadyPlaced:false)){
            this.PlacementDone=true;
            return;
        }
        this.PlacementAttempts++;
        var Result=AssetEditorFrameFloater.TryFloat(Target,Services);
        ActivityLog.LogInformation(nameof(AssetEditorFramePlacement),$"Asset editor float '{Describe()}': floating={Result.Floating}, route={Result.Route}, detail={Result.Detail}, attempt={this.PlacementAttempts}.");
        if(Result.Floating){ this.ScheduleBounds(Target); }
        if(Result.Floating||this.PlacementAttempts>=MaximumPlacementAttempts){ this.PlacementDone=true; }
    }
    /// <summary>
    /// 位置とサイズは、以前の観測に基づき遅延して指定する。最終ビルドの全面描画を保証する対策ではない（M0127）。
    /// 待機中にウィンドウが閉じられた場合は何もしない。
    /// </summary>
    private void ScheduleBounds(IVsWindowFrame Target){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.BoundsTask=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
            await Task.Delay(BoundsDelay);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(this.Disposed||Content() is null){ return; }
            var Detail=AssetEditorFrameFloater.Place(Target,Services);
            ActivityLog.LogInformation(nameof(AssetEditorFramePlacement),$"Asset editor bounds '{Describe()}': {Detail}.");
        });
    }
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        if(this.Element is { }Element){
            Element.Loaded-=this.OnLoaded;
            System.Windows.PresentationSource.RemoveSourceChangedHandler(Element,this.OnSourceChanged);
            this.Element=null;
        }
        this.Frame=null;
    }
}
