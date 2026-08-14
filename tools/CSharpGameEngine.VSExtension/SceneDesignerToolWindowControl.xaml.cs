using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designer の WPF content。Scene が未選択の間は <see cref="SceneHwndHost"/> を作らず、
/// WPF の空状態だけを表示します。HwndHost を非表示のまま残すと Win32 airspace が前フレームを
/// 覆い、空状態でもごみ画像が残るため、Host の attach / dispose を選択状態と一対一にします。
/// </summary>
public partial class SceneDesignerToolWindowControl:UserControl,IDisposable {
    private SceneHwndHost?Host;
    private SceneEditorSessionService?Session;
    private CancellationTokenSource?CatalogCancellation;
    private long CatalogGeneration;
    private bool UpdatingSelector;
    private bool Disposed;

    public SceneDesignerToolWindowControl(){
        InitializeComponent();
        // M0020 Phase 1(ADR-0041 決定事項2): ナビゲーション(Wheel/WASDQE)を受けるにはWPFの
        // キーボードフォーカスが要る。IsTabStopは変えない — Tab巡回でここへ止まらせず、
        // ビューポート内をクリックしたときだけ(OnViewportActivated)フォーカスを与える。
        RenderHostGrid.Focusable=true;
        KeyboardNavigation.SetIsTabStop(RenderHostGrid,false);
        this.Loaded+=this.OnLoaded;
        RenderHostGrid.PreviewMouseWheel+=this.OnViewportPreviewMouseWheel;
        RenderHostGrid.KeyDown+=this.OnViewportKeyDown;
        RenderHostGrid.KeyUp+=this.OnViewportKeyUp;
        RenderHostGrid.LostKeyboardFocus+=this.OnViewportLostKeyboardFocus;
    }

    internal void SetSession(SceneEditorSessionService Session){
        if(this.Disposed||ReferenceEquals(this.Session,Session)){
            return;
        }

        this.DetachSession();
        this.Session=Session;
        Session.PinnedSnapshotChanged+=this.OnPinnedSnapshotChanged;
        Session.Selection.SelectionChanged+=this.OnSelectionChanged;
        Session.ActiveDocumentModelsChanged+=this.OnActiveDocumentModelsChanged;

        if(Session.PinnedSnapshot is { } Snapshot){
            this.ShowSnapshot(Snapshot);
        } else {
            this.ShowEmpty("シーンを選択してください。");
        }

        this.RefreshSceneCatalog();
    }

    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        this.SetSession(Session);
        this.ShowSnapshot(Snapshot);
    }

    private void OnLoaded(object Sender,RoutedEventArgs Args){
        if(this.Session is not null){
            this.RefreshSceneCatalog();
        }
    }

    private void OnPinnedSnapshotChanged(SceneDocumentSnapshot?Snapshot){
        this.DispatchToUi(()=>{
            if(Snapshot is null){
                this.ShowEmpty("選択中のシーンは解決できなくなりました。");
            } else {
                this.ShowSnapshot(Snapshot);
            }
        });
    }

    private void OnActiveDocumentModelsChanged(System.Collections.Generic.IReadOnlyList<SceneModel> Models)=>
        this.DispatchToUi(this.RefreshSceneCatalog);

    private void OnSelectionChanged(SceneSelection Selection){
        this.DispatchToUi(()=>{
            if(this.Session?.PinnedSnapshot is { } Snapshot&&Snapshot.Key.Equals(Selection.Key)){
                this.Host?.SetSelection(Snapshot,Selection);
            }
        });
    }

    private void ShowSnapshot(SceneDocumentSnapshot Snapshot){
        SceneModel?Model=Snapshot.LastKnownGoodModel??Snapshot.CurrentModel;
        if(Model?.Root is null){
            this.ShowEmpty(Snapshot.HasBlockingErrors
                ?"シーンの解析に失敗しました。C# のエラーを修正してください。"
                :"描画できるシーンがありません。");
            return;
        }

        SceneHwndHost Host=this.EnsureHost();
        Host.SetTarget(Snapshot);
        if(this.Session?.Selection.CurrentSelection is { } Selection){
            Host.SetSelection(Snapshot,Selection);
        }
    }

    private SceneHwndHost EnsureHost(){
        if(this.Host is not null){
            return this.Host;
        }

        // SceneReady のときだけ HwndHost を attach する。未選択状態で BuildWindowCore を呼ばない。
        if(!SceneDesignerHostAttachmentPolicy.ShouldAttach(SceneDesignerHostState.SceneReady)){
            throw new InvalidOperationException("Scene Designer host の attach 状態が不正です。");
        }

        SceneHwndHost Host=new();
        Host.StatusChanged+=this.OnStatusChanged;
        Host.ViewportActivated+=this.OnViewportActivated;
        this.Host=Host;
        EmptyStateText.Visibility=Visibility.Collapsed;
        RenderHostGrid.Children.Add(Host);
        return Host;
    }

    private void ShowEmpty(string Message){
        this.DetachHost();
        StatusText.Text=Message;
        EmptyStateText.Text=Message;
        EmptyStateText.Visibility=Visibility.Visible;
    }

    private void DetachHost(){
        SceneHwndHost?Host=this.Host;
        if(Host is null){
            return;
        }

        this.Host=null;
        RenderHostGrid.Children.Remove(Host);
        Host.StatusChanged-=this.OnStatusChanged;
        Host.ViewportActivated-=this.OnViewportActivated;
        // Dispose triggers DestroyWindowCore, which cancels the latest-wins worker and disposes the
        // native child HWND. Its stale callback is additionally rejected by SceneHwndHost's surface
        // and generation checks, so it cannot repaint this WPF-only empty state.
        Host.Dispose();
    }

    private void RefreshSceneCatalog(){
        SceneEditorSessionService?Session=this.Session;
        if(this.Disposed||Session is null){
            return;
        }

        this.CatalogCancellation?.Cancel();
        this.CatalogCancellation?.Dispose();
        CancellationTokenSource Cancellation=new();
        this.CatalogCancellation=Cancellation;
        long Generation=++this.CatalogGeneration;
        SceneSelector.IsEnabled=false;
        _=this.RefreshSceneCatalogAsync(Session,Generation,Cancellation.Token);
    }

#pragma warning disable VSTHRD001 // WPF Dispatcher is the owning UI dispatcher of this control; no AsyncPackage/JTF chain is available here.
    private async Task RefreshSceneCatalogAsync(SceneEditorSessionService Session,long Generation,CancellationToken CancellationToken){
        try {
            ProjectSceneCatalog?Catalog=await Session.GetActiveProjectSceneCatalogAsync(CancellationToken).ConfigureAwait(false);
            if(CancellationToken.IsCancellationRequested){
                return;
            }

            await this.Dispatcher.InvokeAsync(()=>{
                if(this.Disposed||!ReferenceEquals(this.Session,Session)||Generation!=this.CatalogGeneration){
                    return;
                }

                this.UpdatingSelector=true;
                try {
                    SceneSelector.ItemsSource=Catalog?.Entries;
                    SceneSelector.IsEnabled=Catalog is { Entries.Count:>0 };
                    SceneKey?Pinned=Session.PinnedSceneKey;
                    SceneSelector.SelectedItem=Catalog?.Entries.FirstOrDefault(Entry=>
                        Pinned is { } Key&&Entry.Key.Equals(Key));

                    if(Catalog is null){
                        this.ShowEmpty("シーンを選択してください。");
                    } else if(Pinned is { } Key
                        &&string.Equals(Key.ProjectId,Catalog.ProjectId,StringComparison.Ordinal)
                        &&SceneSelector.SelectedItem is null){
                        Session.ClearPin();
                    }
                } finally {
                    this.UpdatingSelector=false;
                }
            });
        } catch(OperationCanceledException){
            // A newer active document/project request superseded this catalog.
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            this.DispatchToUi(()=>{
                if(!this.Disposed&&Generation==this.CatalogGeneration){
                    this.ShowEmpty("シーン一覧の取得に失敗しました。ActivityLog.xml を確認してください。");
                }
            });
        }
    }

#pragma warning restore VSTHRD001

    private void OnSceneSelectionChanged(object Sender,SelectionChangedEventArgs Args){
        if(this.UpdatingSelector||this.Disposed||this.Session is not { } Session
            ||SceneSelector.SelectedItem is not ProjectSceneCatalogEntry Entry){
            return;
        }

        _=this.SelectSceneAsync(Session,Entry.Key);
    }

    private async Task SelectSceneAsync(SceneEditorSessionService Session,SceneKey Key){
        try {
            StatusText.Text="シーンを解析中...";
            SceneModel?Model=await Session.ResolveSceneAsync(Key,CancellationToken.None).ConfigureAwait(true);
            if(this.Disposed||!ReferenceEquals(this.Session,Session)){
                return;
            }

            if(Model?.Root is null){
                Session.ClearPin();
                this.ShowEmpty("選択したシーンは解決できないか、解析エラーがあります。");
                return;
            }

            // Pin is Designer 内だけの切替であり、DTE/TextView を操作しない。コードエディタの
            // キャレット、タブ、キーボードフォーカスはこの選択では移動しない。
            this.ShowSnapshot(Session.Pin(Model));
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneDesignerToolWindowControl),Ex.ToString());
            if(!this.Disposed){
                Session.ClearPin();
                this.ShowEmpty("シーンの選択に失敗しました。ActivityLog.xml を確認してください。");
            }
        }
    }

    private void DetachSession(){
        if(this.Session is null){
            return;
        }

        this.Session.PinnedSnapshotChanged-=this.OnPinnedSnapshotChanged;
        this.Session.Selection.SelectionChanged-=this.OnSelectionChanged;
        this.Session.ActiveDocumentModelsChanged-=this.OnActiveDocumentModelsChanged;
        this.Session=null;
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.CatalogCancellation?.Cancel();
        this.CatalogCancellation?.Dispose();
        this.CatalogCancellation=null;
        this.Loaded-=this.OnLoaded;
        this.DetachSession();
        this.DetachHost();
    }

    private void OnStatusChanged(string Status){
        StatusText.Text=Status;
    }

    /// <summary>
    /// <see cref="SceneHwndHost.ViewportActivated"/>の購読先。ビューポート内をクリックした
    /// 瞬間だけキーボードフォーカスを与える(ADR-0041 決定事項2) — <see cref="OnLoaded"/>や
    /// <see cref="ShowSnapshot"/>/<see cref="SelectSceneAsync"/>では絶対に呼ばない。VS標準の
    /// ツールウィンドウフォーカスモデルを踏襲し、開いただけ・シーンを切り替えただけでは
    /// コードエディタからフォーカスを奪わない。
    /// </summary>
    private void OnViewportActivated(){
        if(!RenderHostGrid.IsKeyboardFocusWithin){
            Keyboard.Focus(RenderHostGrid);
        }
    }

    private void OnViewportPreviewMouseWheel(object Sender,MouseWheelEventArgs Args)=>
        this.Host?.HandleWheel(Args.Delta);

    private void OnViewportKeyDown(object Sender,KeyEventArgs Args){
        if(TryMapNavigationKey(Args.Key,out NavigationKey NavKey)){
            this.Host?.HandleKeyDown(NavKey);
        }
    }

    private void OnViewportKeyUp(object Sender,KeyEventArgs Args){
        if(TryMapNavigationKey(Args.Key,out NavigationKey NavKey)){
            this.Host?.HandleKeyUp(NavKey);
        }
    }

    /// <summary>Alt+Tab等でVS自体からフォーカスが抜けた場合もここが発火し、Held Keyの張り付きを防ぐ。</summary>
    private void OnViewportLostKeyboardFocus(object Sender,KeyboardFocusChangedEventArgs Args)=>
        this.Host?.ClearHeldFlyKeys();

    private static bool TryMapNavigationKey(Key WpfKey,out NavigationKey NavKey){
        switch(WpfKey){
            case Key.W: NavKey=NavigationKey.W; return true;
            case Key.A: NavKey=NavigationKey.A; return true;
            case Key.S: NavKey=NavigationKey.S; return true;
            case Key.D: NavKey=NavigationKey.D; return true;
            case Key.Q: NavKey=NavigationKey.Q; return true;
            case Key.E: NavKey=NavigationKey.E; return true;
            default: NavKey=default; return false;
        }
    }

#pragma warning disable VSTHRD001 // WPF Dispatcher is the owning UI dispatcher of this control; no AsyncPackage/JTF chain is available here.
    private void DispatchToUi(Action Action){
        if(this.Disposed){
            return;
        }

        if(this.Dispatcher.CheckAccess()){
            Action();
        } else {
            _=this.Dispatcher.BeginInvoke(Action);
        }
    }
#pragma warning restore VSTHRD001
}