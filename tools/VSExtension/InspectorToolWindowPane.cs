using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 現在選択中のシーンノードの編集可能なメンバーを表示する、ドッキング可能な VS tool window（M0031）。Package が所有する session provider は <see cref="SceneDesignerToolWindowPane"/> と同じ方式です。pane 自体は designer state を保持せず、Visual Studio が復元した pane を、すでに作成済みの editor session に接続するだけです。
/// </summary>
[Guid(PaneGuidString)]
public sealed class InspectorToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="7ca25df1-0dbf-4b49-9c12-fed543543e26";

    private static Func<SceneEditorSessionService?>?SessionProvider;
    private static WeakReference<InspectorToolWindowControl>?RestoredControl;
    private readonly InspectorToolWindowControl Control;
    private readonly AssetInspectorControl AssetControl;
    private bool ShowingAsset;

    public InspectorToolWindowPane():base(null){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Caption=VsixText.Get("PaneInspector");
        this.Control=new InspectorToolWindowControl();
        this.AssetControl=new AssetInspectorControl{Visibility=System.Windows.Visibility.Collapsed};
        var Root=new System.Windows.Controls.Grid();Root.Children.Add(this.Control);Root.Children.Add(this.AssetControl);
        this.AssetControl.ShowingChanged+=this.OnAssetShowingChanged;
        AssetWorkspaceDrag.Changed+=this.OnDragChanged;
        this.Content=Root;
        this.AssetControl.RefreshSelection();
        RestoredControl=new WeakReference<InspectorToolWindowControl>(this.Control);
        if(SessionProvider?.Invoke() is { }Session){
            this.Control.SetSession(Session);
        }
    }

    internal static void ConfigureSessionProvider(Func<SceneEditorSessionService?>Provider){
        if(Provider is null){ throw new ArgumentNullException(nameof(Provider)); }
        ThreadHelper.ThrowIfNotOnUIThread();
        SessionProvider=Provider;
        if(RestoredControl?.TryGetTarget(out var Control)==true
            &&SessionProvider?.Invoke() is { }Session){
            Control.SetSession(Session);
        }
    }

    internal static void ClearSessionProvider()=>SessionProvider=null;
    private void OnAssetShowingChanged(bool Showing){ this.ShowingAsset=Showing;this.UpdatePresentation(); }
    private void OnDragChanged()=>this.UpdatePresentation();
    /// <summary>素材の表示は、素材が選ばれていて、かつ Assets からのドラッグ中でないときだけ。ドラッグ中は落とし先の欄を見せる。</summary>
    private void UpdatePresentation(){
        var ShowAsset=this.ShowingAsset&&!AssetWorkspaceDrag.Active;
        this.Control.Visibility=ShowAsset?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;
        this.AssetControl.Visibility=ShowAsset?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
    }
    protected override bool PreProcessMessage(ref System.Windows.Forms.Message Message){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.AssetControl.HandleNativeShortcut(AssetToolWindowInput.Shortcut(ref Message))||base.PreProcessMessage(ref Message);
    }
    protected override void Dispose(bool Disposing){
        if(Disposing){
            AssetWorkspaceDrag.Changed-=this.OnDragChanged;
            this.AssetControl.ShowingChanged-=this.OnAssetShowingChanged;
            this.AssetControl.Dispose();this.Control.Dispose();
        }
        base.Dispose(Disposing);
    }
}
