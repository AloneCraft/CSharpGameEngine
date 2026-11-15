using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ユーザーが表示しているドキュメントを <see cref="SceneWorkspaceService"/> に設定する (ADR-0040)。Hierarchy はアクティブなシーン JSON (<c>Scenes\**\*.json</c>、ADR-0070) のシーンを追うため、ソリューションではなくドキュメントフレームを追跡しなければならない。
///
/// <para>
/// DTE のウィンドウイベントではなく、<c>IVsMonitorSelection</c> のドキュメントフレーム要素を使う。ツールウィンドウへ一時的にフォーカスが移った場合は <c>IVsTextManager.GetActiveView(false)</c> を代替として使う。どちらも起動時に同期的に読み取れるため、Hierarchy はすでに開いているファイルを表示でき、Scene Designer や Solution Explorer へフォーカスを移しても表示を消さない。
/// </para>
/// </summary>
internal sealed class ActiveDocumentTracker : IVsSelectionEvents,IDisposable {
    private readonly IVsMonitorSelection Selection;
    private readonly IVsTextManager TextManager;
    private readonly IVsUIShell UIShell;
    private readonly SceneWorkspaceService Service;
    private uint Cookie;
    private bool Disposed;

    private ActiveDocumentTracker(
        IVsMonitorSelection Selection,IVsTextManager TextManager,IVsUIShell UIShell,SceneWorkspaceService Service){
        this.Selection=Selection;
        this.TextManager=TextManager;
        this.UIShell=UIShell;
        this.Service=Service;
    }

    internal static ActiveDocumentTracker?Start(SceneWorkspaceService Service){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Package.GetGlobalService(typeof(SVsShellMonitorSelection))is not IVsMonitorSelection Selection
            ||Package.GetGlobalService(typeof(SVsTextManager))is not IVsTextManager TextManager
            ||Package.GetGlobalService(typeof(SVsUIShell))is not IVsUIShell UIShell){
            return null;
        }

        ActiveDocumentTracker Tracker=new(Selection,TextManager,UIShell,Service);
        Selection.AdviseSelectionEvents(Tracker,out Tracker.Cookie);

        // すでに開いているドキュメントを取得する。既存のエディター上でツールウィンドウを開くときに、
        // 先にファイルを操作する必要をなくす。
        Tracker.RefreshCurrentDocument();

        return Tracker;
    }

    /// <summary>
    /// パッケージの起動後に、現在のフレーム/ビューを再読み込みする。コールドハイブでは、最初のテキストビューが読み取り可能になる前に Visual Studio がライブワークスペースを公開することがあるため、<see cref="Start"/> で行う同期読み取り 1 回だけでは不十分。
    /// </summary>
    internal bool RefreshCurrentDocument(){
        ThreadHelper.ThrowIfNotOnUIThread();

        IVsWindowFrame?Frame=null;
        if(this.Selection.GetCurrentElementValue(
            (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame,
            out var Current)==VSConstants.S_OK){
            Frame=Current as IVsWindowFrame;
        }
        this.UpdateFromFrame(Frame);
        return this.CurrentFilePath is not null;
    }

    public int OnElementValueChanged(uint ElementId,object OldValue,object NewValue){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(ElementId==(uint)VSConstants.VSSELELEMID.SEID_DocumentFrame){
            this.UpdateFromFrame(NewValue as IVsWindowFrame);
        }

        return VSConstants.S_OK;
    }

    /// <summary>Solution Explorer の選択はシーンを対象としない (ADR-0071：シーンはダブルクリックで開く)。</summary>
    public int OnSelectionChanged(
        IVsHierarchy OldHierarchy,
        uint OldItemId,
        IVsMultiItemSelect OldMultiItemSelect,
        ISelectionContainer OldSelectionContainer,
        IVsHierarchy NewHierarchy,
        uint NewItemId,
        IVsMultiItemSelect NewMultiItemSelect,
        ISelectionContainer NewSelectionContainer)=>VSConstants.S_OK;

    public int OnCmdUIContextChanged(uint CmdUICookie,int Active)=>VSConstants.S_OK;

    private void UpdateFromFrame(IVsWindowFrame?Frame){
        ThreadHelper.ThrowIfNotOnUIThread();

        // ツールウィンドウのアクティブ化時に、一時的に null またはドキュメントでないフレームが公開されることがある。GetActiveView は
        // fMustHaveFocus=false で呼ぶと、現在または直近にアクティブだったテキストビューを返す。そのため、
        // Scene Designer または Solution Explorer にフォーカスを移しても、最後に開いたコードドキュメントを維持できる。
        // Hierarchy をクリアして OpenSceneDesignerCommand と競合する問題を防ぐ。
        var FramePath=TryGetFrameDocumentPath(Frame,out var DocumentPath)?DocumentPath:null;
        var TextViewPath=this.TryGetActiveTextViewPath(out DocumentPath)?DocumentPath:null;
        var SelectedPath=ActiveDocumentPathSelector.Select(FramePath,TextViewPath, this.CurrentFilePath);
        if(SelectedPath is not null){
            this.SetDocumentPath(SelectedPath);
        }
        // どちらの情報源にもドキュメントがない場合は、未知のツールウィンドウへの切り替えである。現在のコードドキュメントを維持する。
        // ここでクリアしていたことが、Scene Designer を空にしていたフェーズ 8 の不具合だった。
    }

    private void SetDocumentPath(string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        this.CurrentFilePath=SceneJsonCommitPlanner.IsJsonScene(DocumentPath)
                                 ?DocumentPath
                                 :null;
        this.Service.SetActiveDocument(this.CurrentFilePath);
    }

    private static bool TryGetFrameDocumentPath(IVsWindowFrame?Frame,out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        DocumentPath=string.Empty;
        if(Frame is null
            ||Frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument,out var Value)!=VSConstants.S_OK
            ||Value is not string Path
            ||string.IsNullOrWhiteSpace(Path)){
            return false;
        }

        DocumentPath=Path;
        return true;
    }

    private bool TryGetActiveTextViewPath(out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        return this.TryGetActiveTextView(out _,out DocumentPath);
    }

    private bool TryGetActiveTextView(out IVsTextView View,out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        View=null!;
        DocumentPath=string.Empty;
        if(this.TextManager.GetActiveView(0,null,out View)!=VSConstants.S_OK
            ||View.GetBuffer(out var Buffer)!=VSConstants.S_OK
            ||Buffer is not IPersistFileFormat PersistFile
            ||PersistFile.GetCurFile(out var Path,out _)!=VSConstants.S_OK
            ||string.IsNullOrWhiteSpace(Path)){
            return false;
        }

        DocumentPath=Path;
        return true;
    }

    internal string?CurrentFilePath { get;private set;}

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;

        ThreadHelper.JoinableTaskFactory.Run(async ()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if(this.Cookie!=0){
                this.Selection.UnadviseSelectionEvents(this.Cookie);
                this.Cookie=0;
            }
        });
    }
}
