using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Points <see cref="SceneWorkspaceService"/> at whichever document the user is looking at
/// (ADR-0040). The Scene editor shows the scene declared by the active <c>.cs</c> file, so it has to
/// follow the document frame rather than the solution.
///
/// <para>
/// Uses <c>IVsMonitorSelection</c>'s document-frame element instead of DTE window events, with
/// <c>IVsTextManager.GetActiveView(false)</c> as the fallback for transient tool-window focus.
/// Both are synchronously readable at startup, so Hierarchy can populate for an already-open file
/// without clearing it when focus moves to Scene Designer or Solution Explorer.
/// </para>
/// </summary>
internal sealed class ActiveDocumentTracker : IVsSelectionEvents,IDisposable {
    private readonly IVsMonitorSelection Selection;
    private readonly IVsTextManager TextManager;
    private readonly IVsUIShell UIShell;
    private readonly SceneWorkspaceService Service;
    private uint Cookie;
    private bool Disposed;
    private IVsWindowFrame?SolutionExplorerFrame;

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

        // Pick up the document that is already open, so opening the tool window on an existing
        // editor does not require touching the file first.
        if(Selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame,out var Current)==VSConstants.S_OK){
            Tracker.UpdateFromFrame(Current as IVsWindowFrame);
        }

        return Tracker;
    }

    public int OnElementValueChanged(uint ElementId,object OldValue,object NewValue){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(ElementId==(uint)VSConstants.VSSELELEMID.SEID_DocumentFrame){
            this.UpdateFromFrame(NewValue as IVsWindowFrame);
        }

        return VSConstants.S_OK;
    }

    /// <summary>
    /// ソリューションエクスプローラー等、階層ベースの選択が変わるたびに発火する。単一選択のみ
    /// 対応(実際に複数選択されている場合のみ無視、今回のスコープ外)。ここで解決した
    /// パスは<see cref="CurrentFilePath"/>(アクティブなコードエディタ文書)とは別に保持し、
    /// <see cref="SceneWorkspaceService.SetSolutionExplorerSelection"/>へ転送するだけで、
    /// コードエディタ側の状態には一切触れない。
    ///
    /// <para>
    /// 実機不具合(2026-08): <paramref name="NewMultiItemSelect"/>を「非nullなら複数選択」と
    /// 誤判定していたため、ソリューションエクスプローラーで別ファイルを右クリックしても
    /// <see cref="SolutionExplorerFilePath"/>が更新されず、直前に選択していたファイルの
    /// メニューが表示され続けるバグがあった。<c>IVsMultiItemSelect</c>は複数選択に対応する
    /// 階層では**単一選択時にも非nullで渡されることがある**(VS SDKの既知の仕様)ため、
    /// 参照のnullチェックではなく<c>GetSelectionInfo</c>の実際の選択件数で判定する。
    /// </para>
    /// </summary>
    public int OnSelectionChanged(
        IVsHierarchy OldHierarchy,
        uint OldItemId,
        IVsMultiItemSelect OldMultiItemSelect,
        ISelectionContainer OldSelectionContainer,
        IVsHierarchy NewHierarchy,
        uint NewItemId,
        IVsMultiItemSelect NewMultiItemSelect,
        ISelectionContainer NewSelectionContainer){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(NewMultiItemSelect is not null
            &&NewMultiItemSelect.GetSelectionInfo(out uint ItemCount,out _)==VSConstants.S_OK
            &&ItemCount>1){
            return VSConstants.S_OK;
        }

        this.SolutionExplorerFilePath=NewHierarchy is not null
            &&NewHierarchy.GetCanonicalName(NewItemId,out string CanonicalName)==VSConstants.S_OK
            &&CanonicalName.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)
            ?CanonicalName
            :null;
        this.Service.SetSolutionExplorerSelection(this.SolutionExplorerFilePath);
        return VSConstants.S_OK;
    }

    public int OnCmdUIContextChanged(uint CmdUICookie,int Active)=>VSConstants.S_OK;

    private void UpdateFromFrame(IVsWindowFrame?Frame){
        ThreadHelper.ThrowIfNotOnUIThread();

        // Tool-window activation can transiently publish a null/non-document frame. GetActiveView
        // with fMustHaveFocus=false returns the current or most recently active text view, so moving
        // focus to Scene Designer/Solution Explorer keeps the last code document instead of
        // clearing Hierarchy and racing OpenSceneDesignerCommand.
        string?FramePath=TryGetFrameDocumentPath(Frame,out var DocumentPath)?DocumentPath:null;
        string?TextViewPath=this.TryGetActiveTextViewPath(out DocumentPath)?DocumentPath:null;
        string?SelectedPath=ActiveDocumentPathSelector.Select(FramePath,TextViewPath,CurrentFilePath);
        if(SelectedPath is not null){
            this.SetDocumentPath(SelectedPath);
        }
        // If neither source has a document, this is an unknown tool-window transition. Preserve the
        // current code document; clearing here was the Phase 8 bug that blanked Scene Designer.
    }

    private void SetDocumentPath(string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        CurrentFilePath=DocumentPath.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)
            ?DocumentPath
            :null;
        this.Service.SetActiveDocument(CurrentFilePath);
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

    /// <summary>
    /// コマンド起動元のテキストビューを固定します。ToolWindow表示後にGetActiveViewを呼ぶと、
    /// 既にVSシェルへフォーカスが移った後なので、右クリック元を確実に復元できません。
    /// </summary>
    internal IVsTextView? CaptureActiveTextView(string ExpectedFilePath){
        ThreadHelper.ThrowIfNotOnUIThread();

        return this.TryGetActiveTextView(out IVsTextView View,out string DocumentPath)
            &&string.Equals(DocumentPath,ExpectedFilePath,StringComparison.OrdinalIgnoreCase)
            ?View
            :null;
    }

    /// <summary>
    /// 右クリックコマンドの型境界にはキャレット位置を使う。既にアクティブなテキストバッファだけを
    /// 同期的に読み、ここからRoslyn解析を開始してはならない。
    /// </summary>
    internal bool TryCaptureActiveCaretPosition(string ExpectedFilePath,out int Position){
        ThreadHelper.ThrowIfNotOnUIThread();

        Position=0;
        if(!this.TryGetActiveTextView(out IVsTextView View,out string DocumentPath)
            ||!string.Equals(DocumentPath,ExpectedFilePath,StringComparison.OrdinalIgnoreCase)
            ||View.GetCaretPos(out int Line,out int Column)!=VSConstants.S_OK
            ||View.GetBuffer(out IVsTextLines Buffer)!=VSConstants.S_OK){
            return false;
        }

        return Buffer.GetPositionOfLineIndex(Line,Column,out Position)==VSConstants.S_OK;
    }
    private bool TryGetActiveTextView(out IVsTextView View,out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        View=null!;
        DocumentPath=string.Empty;
        if(this.TextManager.GetActiveView(0,null,out View)!=VSConstants.S_OK
            ||View.GetBuffer(out IVsTextLines Buffer)!=VSConstants.S_OK
            ||Buffer is not IPersistFileFormat PersistFile
            ||PersistFile.GetCurFile(out var Path,out _)!=VSConstants.S_OK
            ||string.IsNullOrWhiteSpace(Path)){
            return false;
        }

        DocumentPath=Path;
        return true;
    }

    internal string?CurrentFilePath { get;private set;}

    /// <summary>直近の<see cref="OnSelectionChanged"/>が解決した、ソリューションエクスプローラー等の
    /// 階層選択上のファイルパス(.cs以外やハンドル不能な選択はnull)。</summary>
    internal string?SolutionExplorerFilePath { get;private set;}

    /// <summary>
    /// 現在フォーカスしているウィンドウフレームがソリューションエクスプローラーかどうかで、今回の
    /// メニュー要求がSE起点かを判定する。パスの一致だけで判定すると、コードエディタで別ファイルを
    /// 編集中でもSEが以前選んでいたファイルの残骸を誤って使ってしまう恐れがあるため、フォーカス先で
    /// 判定する。真を返すときの<paramref name="FilePath"/>は.cs以外や未解決ならnull
    /// (呼び出し元はnullを「このファイルは対象外」として扱う)。
    ///
    /// <para>
    /// 実機不具合(2026-08): 当初は<c>Frame.GetProperty(VSFPROPID_GuidPersistenceSlot)</c>で
    /// フレームの識別GUIDを読んで比較していたが、実機ログで**この呼び出しが常に
    /// `DISP_E_MEMBERNOTFOUND`(0x80020003)で失敗し、一度も成功していなかった**ことが判明した
    /// (この関数は常にfalseを返し、呼び出し元は常にコードエディタ経路にフォールバックしていた —
    /// 「たまたまアクティブタブ=右クリック対象」の時だけ正しく見えていた)。
    /// <c>GetProperty</c>によるプロパティ照会をやめ、<c>IVsUIShell.FindToolWindow</c>で
    /// ソリューションエクスプローラー自身の<see cref="IVsWindowFrame"/>を一度取得してキャッシュし、
    /// 現在アクティブなフレームとの参照同一性で比較する方式へ作り直した(in-process拡張機能では
    /// 同一のネイティブCOMオブジェクトは同一のRCWインスタンスに解決されるため、
    /// <c>ReferenceEquals</c>で確実に判定できる)。
    /// </para>
    /// </summary>
    internal bool TryGetSolutionExplorerInvocationFilePath(out string?FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();

        FilePath=null;
        if(this.Selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame,out object FrameObj)!=VSConstants.S_OK
            ||FrameObj is not IVsWindowFrame CurrentFrame
            ||!this.TryGetSolutionExplorerFrame(out IVsWindowFrame?SeFrame)
            ||SeFrame is null
            ||!ReferenceEquals(CurrentFrame,SeFrame)){
            return false;
        }

        FilePath=this.SolutionExplorerFilePath;
        return true;
    }

    private bool TryGetSolutionExplorerFrame(out IVsWindowFrame?Frame){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(this.SolutionExplorerFrame is not null){
            Frame=this.SolutionExplorerFrame;
            return true;
        }

        Guid SolutionExplorerGuid=new(ToolWindowGuids80.SolutionExplorer);
        if(this.UIShell.FindToolWindow(0,ref SolutionExplorerGuid,out IVsWindowFrame?Found)==VSConstants.S_OK
            &&Found is not null){
            this.SolutionExplorerFrame=Found;
            Frame=Found;
            return true;
        }

        Frame=null;
        return false;
    }

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
