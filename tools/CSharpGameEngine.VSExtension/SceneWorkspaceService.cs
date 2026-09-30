using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// One verified C# source edit and its single native Visual Studio Undo transaction.
/// <paramref name="Before"/> is the Roslyn text used to detect a concurrent live-buffer edit.
/// </summary>
internal sealed record SceneTextWriteStep(
    SourceText Before,IReadOnlyList<TextChange>Changes,string UndoDescription);

internal sealed class SceneWorkspaceEditContext {
    internal SceneWorkspaceEditContext(
        DocumentId DocumentId,string FilePath,SceneModel Model,SyntaxNode Root,SourceText Text,
        Compilation Compilation){
        this.DocumentId=DocumentId;
        this.FilePath=FilePath;
        this.Model=Model;
        this.Root=Root;
        this.Text=Text;
        this.Compilation=Compilation;
    }

    internal DocumentId DocumentId { get; }

    internal string FilePath { get; }

    internal SceneModel Model { get; }

    internal SyntaxNode Root { get; }

    internal SourceText Text { get; }

    /// <summary>
    /// 書き戻し結果の往復検証(<see cref="SceneJsonCommitPlanner"/>)が、書き戻したJSONの設計ビューを
    /// 差し替えて再解析するために使う。解決時点と同じ世代のCompilationであることが重要。
    /// </summary>
    internal Compilation Compilation { get; }
}

internal readonly struct SceneLiveBufferWriteResult {
    internal SceneLiveBufferWriteResult(bool Succeeded,string?ErrorMessage){
        this.Succeeded=Succeeded;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool Succeeded { get; }

    internal string?ErrorMessage { get; }

}

/// <summary>
/// Supplies the editor with a <see cref="SceneModel"/> built from <b>live Roslyn</b> (ADR-0040).
///
/// <para>
/// This replaces the previous execution-based path, which had to build the target project, run the
/// generated CreateRuntime in a separate process and read a WorldSnapshot reflectively. Reading the
/// declaration directly means the scene is visible while the user is still typing it, and there is
/// nothing to keep in sync with the Source Generator: both run the same shared
/// <see cref="SceneDeclarationAnalyzer"/> source.
/// </para>
///
/// <para>
/// Threading, in order of what has already gone wrong in this repo: Roslyn work never runs on the
/// UI thread; the result is marshaled back with SwitchToMainThreadAsync before anything touches WPF;
/// and nothing here is awaited from inside AsyncPackage.InitializeAsync's own chain. Analysis is
/// debounced because a keystroke-rate rebuild of every scene would make typing feel heavy, and the
/// previous CancellationTokenSource is cancelled so a slow analysis cannot overwrite a newer one.
/// </para>
/// </summary>
internal sealed class SceneWorkspaceService : IDisposable {
    private readonly JoinableTaskFactory TaskFactory;
    private readonly VisualStudioWorkspace Workspace;
    private readonly IVsEditorAdaptersFactoryService?EditorAdapters;
    private readonly SceneTextBufferWriter?TextBufferWriter;
    private readonly object Gate=new();
    private readonly SceneAnalysisGenerationGate<DocumentId>AnalysisGeneration=new();
    private System.Threading.Timer?DebounceTimer;
    private CancellationTokenSource?PendingAnalysis;

    /// <summary>
    /// M0051 Step 1-8: デバウンスの保留窓(<see cref="SceneAnalysisDebouncePolicy"/>)。
    /// 状態遷移は必ずポリシー経由で行うこと — 直接いじると
    /// 「タイマが武装中 ⟺ PendingSince が非null」の不変条件が壊れ、実機で
    /// 「シーンが開けなくなる」ライブロックへ直結する。
    /// </summary>
    private SceneAnalysisDebounceState DebounceState=SceneAnalysisDebouncePolicy.Idle;

    /// <summary>
    /// 壁時計ではなく単調時計を使う。NTP/DST補正で経過が負になると上限判定が壊れるため。
    /// </summary>
    private readonly System.Diagnostics.Stopwatch AnalysisClock=System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<ProjectId,ProjectSceneCatalogCache>ProjectSceneCatalogs=[];
    private readonly Dictionary<ProjectId,ProjectComponentCatalogCache>ProjectComponentCatalogs=[];

    /// <summary>
    /// The in-flight analysis. Held rather than discarded so the fire-and-forget stays observable
    /// (VSTHRD110) and so <see cref="Dispose"/> can join it instead of letting a callback run
    /// against a disposed service.
    /// </summary>
    private JoinableTask?RunningAnalysis;

    private DocumentId?ActiveDocumentId;
    private string?ActiveDocumentPath;

    private bool Disposed;

    private SceneWorkspaceService(
        VisualStudioWorkspace Workspace,JoinableTaskFactory TaskFactory,
        IVsEditorAdaptersFactoryService?EditorAdapters,ITextUndoHistoryRegistry?UndoHistoryRegistry){
        this.Workspace=Workspace;
        this.TaskFactory=TaskFactory;
        this.EditorAdapters=EditorAdapters;
        this.TextBufferWriter=UndoHistoryRegistry is null?null:new SceneTextBufferWriter(UndoHistoryRegistry);
        this.Workspace.WorkspaceChanged+=this.OnWorkspaceChanged;
    }

    internal static SceneWorkspaceService?Instance { get;private set;}

    /// <summary>The most recent successful analysis. Kept so a transient failure never blanks the UI.</summary>
    internal SceneModel?CurrentModel { get;private set;}

    internal event Action<SceneModel?>?ModelChanged;

    /// <summary>
    /// Every <c>IScene</c> implementation found in the active document (ADR-0040 Phase 9), not just
    /// the first - Scene Designer needs to target one specific class by name when a file declares
    /// several. <see cref="CurrentModel"/>/<see cref="ModelChanged"/>
    /// stay unchanged (still "the first one found") so Hierarchy's existing behavior is untouched;
    /// this is computed in the same analysis pass, not a second one.
    /// </summary>
    internal IReadOnlyList<SceneModel>AllModels { get;private set;}=[];

    internal event Action<IReadOnlyList<SceneModel>>?AllModelsChanged;
    /// <summary>
    /// Acquires the workspace through <c>SComponentModel</c> rather than exporting a MEF part - this
    /// VSIX has no MEF components and adding one just to pull a service would be a larger change
    /// than the service is worth. Returns null (rather than throwing) when the workspace is not
    /// available, so a VS instance without the managed language service simply has no Scene editor.
    /// </summary>
    internal static SceneWorkspaceService?Initialize(){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Instance is not null){
            return Instance;
        }

        if(Package.GetGlobalService(typeof(SComponentModel))is not IComponentModel ComponentModel){
            return null;
        }

        VisualStudioWorkspace?Workspace=ComponentModel.GetService<VisualStudioWorkspace>();

        if(Workspace is null){
            return null;
        }

        Instance=new SceneWorkspaceService(
            Workspace,
            ThreadHelper.JoinableTaskFactory,
            ComponentModel.GetService<IVsEditorAdaptersFactoryService>(),
            ComponentModel.GetService<ITextUndoHistoryRegistry>());
        return Instance;
    }

    /// <summary>Points the service at a document; passing null clears the model.</summary>
    internal void SetActiveDocument(string?FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();

        DocumentId?DocumentId=FilePath is null?null:this.FindDocument(FilePath);
        lock(this.Gate){
            if(string.Equals(this.ActiveDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)
                &&Equals(this.ActiveDocumentId,DocumentId)){
                return;
            }

            this.ActiveDocumentPath=FilePath;
            this.ActiveDocumentId=DocumentId;
            this.AnalysisGeneration.ActivateDocument(DocumentId);
            this.CancelPendingAnalysisNoLock();
        }

        // A document switch invalidates the old tree immediately. The command no longer consumes
        // this transient cache; it analyses its captured file directly, so this cannot recreate the
        // old BeforeQueryStatus/Execute race.
        this.CurrentModel=null;
        this.AllModels=[];

        this.ModelChanged?.Invoke(null);
        this.AllModelsChanged?.Invoke(this.AllModels);
        this.ScheduleAnalysis();
    }

    /// <summary>Resolves a file path to a document in the current solution. Null when the file is not part of it.</summary>
    internal DocumentId?FindDocument(string FilePath){
        return this.Workspace.CurrentSolution.GetDocumentIdsWithFilePath(FilePath).FirstOrDefault();
    }
    /// <summary>
    /// Roslyn の実 ProjectId と完全修飾 Scene 名から、Designer session 用の不変キーを作成します。
    /// ファイルパスや単純クラス名をキーにすると、同名 partial/別プロジェクトの Scene を誤って結合する
    /// ためです。ソリューション外の Document は session に載せません。
    /// </summary>
    internal SceneKey?TryCreateSceneKey(SceneModel Model){
        DocumentId?DocumentId=this.FindDocument(Model.FilePath);
        TextDocument?Document=DocumentId is { } Id
            ?GetTextDocument(this.Workspace.CurrentSolution,Id)
            :null;
        if(Document is null){
            return null;
        }

        return new SceneKey(Document.Project.Id.Id.ToString("N"),Model.ClassFullyQualifiedName);
    }

    internal string?TryGetProjectAssemblyName(SceneKey Key)=>
        SceneRuntimeProcessNameResolver.Resolve(this.Workspace.CurrentSolution.Projects.Select(Project=>
            new SceneRuntimeProjectAssembly(Project.Id.Id.ToString("N"),Project.AssemblyName)),Key);
    /// <summary>
    /// Analyses the exact file captured by a command invocation instead of reading the debounced,
    /// mutable active-document cache. This makes the command's 0/1/N decision independent of focus
    /// changes while its asynchronous work is in flight.
    /// </summary>
    internal async Task<IReadOnlyList<SceneModel>>AnalyzeFileNowAsync(string FilePath,CancellationToken CancellationToken){
        DocumentId?DocumentId=this.FindDocument(FilePath);
        if(DocumentId is null){
            throw new InvalidOperationException(VsixText.Format("WorkspaceFileMissing",FilePath));
        }

        return await this.AnalyzeAllAsync(DocumentId,CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 現在（または ActiveDocumentTracker が保持している最後）の文書と同じ Project だけから
    /// Scene を集めます。ここは Roslyn のみを背景で読むため、ComboBox の展開が UI thread を
    /// ブロックしたり、描画用 bridge process を起動したりしません。
    /// </summary>
    internal Task<ProjectSceneCatalog?>GetActiveProjectSceneCatalogAsync(CancellationToken CancellationToken)=>
        this.GetProjectSceneCatalogAsync(PreferredSceneKey:null,CancellationToken);

    /// <summary>Pin済みSceneを優先し、そのSceneを所有するProjectの選択肢を取得します。</summary>
    internal async Task<ProjectSceneCatalog?>GetProjectSceneCatalogAsync(
        SceneKey?PreferredSceneKey,CancellationToken CancellationToken){
        DocumentId?ActiveId;
        lock(this.Gate){
            ActiveId=this.ActiveDocumentId;
        }

        string?ActiveProjectId=ActiveId is { } Id
            ?GetTextDocument(this.Workspace.CurrentSolution,Id)?.Project.Id.Id.ToString("N")
            :null;
        string?ProjectId=SceneCatalogTargetPolicy.SelectProjectId(PreferredSceneKey,ActiveProjectId);
        Project?Project=ProjectId is null?null:this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return null;
        }

        lock(this.Gate){
            if(this.ProjectSceneCatalogs.TryGetValue(Project.Id,out ProjectSceneCatalogCache?Cached)
                &&Cached.Version.Equals(Project.Version)){
                return Cached.Catalog;
            }
        }

        // ADR-0070: scenes are the project's Scenes\**\*.json documents; their headers name the classes.
        string ProjectKey=Project.Id.Id.ToString("N");
        List<ProjectSceneCatalogEntry>Entries=[];
        foreach(TextDocument JsonScene in SceneJsonWorkspaceSource.JsonScenes(Project)){
            SourceText JsonText=await JsonScene.GetTextAsync(CancellationToken).ConfigureAwait(false);
            if(SceneJsonWorkspaceSource.TryReadClassFullyQualifiedName(JsonText.ToString())is not { }ClassName
                ||string.IsNullOrWhiteSpace(JsonScene.FilePath)){
                continue;
            }
            Entries.Add(new ProjectSceneCatalogEntry(
                new SceneKey(ProjectKey,ClassName),
                ClassName.Substring(ClassName.LastIndexOf('.')+1),
                JsonScene.FilePath!,
                0));
        }

        ProjectSceneCatalog Catalog=new(ProjectKey,ProjectSceneCatalogPresentation.Create(ProjectKey,Entries));
        lock(this.Gate){
            if(!this.Disposed){
                this.ProjectSceneCatalogs[Project.Id]=new(Project.Version,Catalog);
            }
        }

        return Catalog;
    }

    /// <summary>
    /// M0052: Componentパレットに並べる候補を、対象Projectの<see cref="Compilation"/>から組み立てます。
    /// 対象Projectの決め方と<see cref="Project.Version"/>によるキャッシュは
    /// <see cref="GetProjectSceneCatalogAsync"/>と同じ流儀に揃えています。
    ///
    /// <para>
    /// キャッシュするのは<see cref="SceneComponentCatalogEntry"/>ではなく
    /// <see cref="ComponentPaletteEntry"/>です。前者は<c>INamedTypeSymbol</c>を保持するため、
    /// UI側が持ち続けると<see cref="Compilation"/>全体をピン留めしてしまいます。
    /// </para>
    /// </summary>
    internal async Task<IReadOnlyList<ComponentPaletteEntry>>GetProjectComponentCatalogAsync(
        SceneKey?PreferredSceneKey,CancellationToken CancellationToken){
        DocumentId?ActiveId;
        lock(this.Gate){
            ActiveId=this.ActiveDocumentId;
        }

        string?ActiveProjectId=ActiveId is { } Id
            ?GetTextDocument(this.Workspace.CurrentSolution,Id)?.Project.Id.Id.ToString("N")
            :null;
        string?ProjectId=SceneCatalogTargetPolicy.SelectProjectId(PreferredSceneKey,ActiveProjectId);
        Project?Project=ProjectId is null?null:this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return [];
        }

        lock(this.Gate){
            if(this.ProjectComponentCatalogs.TryGetValue(Project.Id,out ProjectComponentCatalogCache?Cached)
                &&Cached.Version.Equals(Project.Version)){
                return Cached.Entries;
            }
        }

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return [];
        }

        string?ProjectDirectory=string.IsNullOrWhiteSpace(Project.FilePath)
            ?null
            :System.IO.Path.GetDirectoryName(Project.FilePath);
        List<ComponentPaletteEntry>Entries=[];
        foreach(SceneComponentCatalogEntry Candidate in SceneComponentCatalog.Build(Compilation)){
            CancellationToken.ThrowIfCancellationRequested();
            Entries.Add(new ComponentPaletteEntry(
                Candidate.FullyQualifiedName,
                Candidate.DisplayName,
                Candidate.Symbol.ContainingNamespace is { IsGlobalNamespace:false }Namespace
                    ?Namespace.ToDisplayString()
                    :string.Empty,
                Candidate.CreationExpression,
                SceneComponentIconResolver.Resolve(Candidate.Symbol,ProjectDirectory)));
        }

        lock(this.Gate){
            if(!this.Disposed){
                this.ProjectComponentCatalogs[Project.Id]=new(Project.Version,Entries);
            }
        }

        return Entries;
    }

    /// <summary>
    /// M0053: 追加するComponent型のシンボルを、対象Projectの現在のCompilationから引きます。
    ///
    /// <para>
    /// メモリ内Componentへ<b>実体のメンバー</b>を載せるために要ります。型が分からないと
    /// <see cref="SceneValue.Type"/>がnullになり、Inspectorは全項目を読み取り専用へ落として
    /// しまうため(<c>SceneInspectorModelBuilder.DetermineEditorKind</c>)。
    /// </para>
    ///
    /// <para>
    /// 探索に<see cref="SceneComponentCatalog.Build"/>を使うのは、閉じたgeneric instantiation
    /// (<c>StatusEffect&lt;PoisonKind&gt;</c>)がCompilationに型として存在せずカタログが
    /// <c>Construct</c>で組み立てたものだからです。<c>GetTypeByMetadataName</c>では引けません。
    /// </para>
    /// </summary>
    internal async Task<(INamedTypeSymbol Symbol,Compilation Compilation)?>ResolveComponentTypeAsync(
        SceneKey?PreferredSceneKey,string FullyQualifiedName,CancellationToken CancellationToken){
        DocumentId?ActiveId;
        lock(this.Gate){
            ActiveId=this.ActiveDocumentId;
        }

        string?ActiveProjectId=ActiveId is { } Id
            ?GetTextDocument(this.Workspace.CurrentSolution,Id)?.Project.Id.Id.ToString("N")
            :null;
        string?ProjectId=SceneCatalogTargetPolicy.SelectProjectId(PreferredSceneKey,ActiveProjectId);
        Project?Project=ProjectId is null?null:this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return null;
        }

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return null;
        }

        CancellationToken.ThrowIfCancellationRequested();
        INamedTypeSymbol?Symbol=ComponentTypeResolver.Resolve(Compilation,FullyQualifiedName);
        return Symbol is null?null:(Symbol,Compilation);
    }

    /// <summary>ComboBox が保持する <see cref="SceneKey"/> を、現在の Project で再解決します。</summary>
    internal async Task<SceneModel?>ResolveSceneAsync(SceneKey Key,CancellationToken CancellationToken){
        Project?Project=this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),Key.ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return null;
        }

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return null;
        }
        return await SceneJsonWorkspaceSource.FindAsync(Project,Key.ClassFullyQualifiedName,CancellationToken).ConfigureAwait(false)is { }JsonScene
            ?(await ResolveJsonSceneAsync(Project,Compilation,JsonScene,Key.ClassFullyQualifiedName,CancellationToken).ConfigureAwait(false))?.Model
            :null;
    }

    /// <summary>commit直前のCompilationからModel・Root・SourceTextを同じ世代で取得する。</summary>
    internal async Task<SceneWorkspaceEditContext?>ResolveSceneEditContextAsync(
        SceneKey Key,CancellationToken CancellationToken){
        Project?Project=this.Workspace.CurrentSolution.Projects.FirstOrDefault(Candidate=>
            string.Equals(Candidate.Id.Id.ToString("N"),Key.ProjectId,StringComparison.Ordinal));
        if(Project is null){
            return null;
        }

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return null;
        }
        if(await SceneJsonWorkspaceSource.FindAsync(Project,Key.ClassFullyQualifiedName,CancellationToken).ConfigureAwait(false)is not { }JsonScene
            ||await ResolveJsonSceneAsync(Project,Compilation,JsonScene,Key.ClassFullyQualifiedName,CancellationToken).ConfigureAwait(false)
                is not { }Resolved){
            return null;
        }
        SourceText JsonText=await JsonScene.GetTextAsync(CancellationToken).ConfigureAwait(false);
        return new SceneWorkspaceEditContext(
            JsonScene.Id,JsonScene.FilePath!,Resolved.Model,
            await Resolved.Tree.GetRootAsync(CancellationToken).ConfigureAwait(false),JsonText,Resolved.Compilation);
    }

    /// <summary>
    /// ADR-0070: analyzes a JSON scene through its designer tree. <c>null</c> when the live JSON is not a
    /// valid scene or does not declare <paramref name="ClassFullyQualifiedName"/>.
    /// </summary>
    private static async Task<(SceneModel Model,SyntaxTree Tree,Compilation Compilation)?>ResolveJsonSceneAsync(
        Project Project,Compilation Compilation,TextDocument JsonScene,string ClassFullyQualifiedName,
        CancellationToken CancellationToken){
        if(await SceneJsonWorkspaceSource.AddDesignerTreeAsync(Project,Compilation,JsonScene,CancellationToken).ConfigureAwait(false)
            is not { }Added){
            return null;
        }
        INamedTypeSymbol?Type=Added.Compilation.GetTypeByMetadataName(ClassFullyQualifiedName);
        if(Type is null||!SceneTypePredicates.ImplementsSceneInterface(Type)){
            return null;
        }
        return (SceneDeclarationAnalyzer.Analyze(Type,Added.Compilation,CancellationToken),Added.Tree,Added.Compilation);
    }

    /// <summary>
    /// Applies one verified gesture to the live scene document buffer as one native VS Undo unit.
    /// <see cref="SceneWorkspaceEditContext.Text"/> is the optimistic-concurrency precondition.
    /// </summary>
    internal async Task<SceneLiveBufferWriteResult>ApplyTextAsync(
        SceneWorkspaceEditContext Context,IReadOnlyList<TextChange>Changes,string UndoDescription,
        CancellationToken CancellationToken){
        await this.TaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(this.EditorAdapters is null||this.TextBufferWriter is null){
            return new SceneLiveBufferWriteResult(false,VsixText.Get("WorkspaceEditorUndoServicesUnavailable"));
        }
        if(!this.Workspace.IsDocumentOpen(Context.DocumentId)){
            if(!this.Workspace.CanOpenDocuments){
                return new SceneLiveBufferWriteResult(false,VsixText.Get("WorkspaceLiveDocumentOpenUnavailable"));
            }
            this.OpenWithoutActivating(Context.DocumentId);
        }
        if(!this.TryGetLiveBuffer(
                                  Context.FilePath,out ITextBuffer?Buffer,
                                  out Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer?VsBuffer)
            ||Buffer is null||VsBuffer is null){
            return new SceneLiveBufferWriteResult(false,VsixText.Get("WorkspaceTextBufferUnavailable"));
        }

        // ギズモ/インスペクター編集はユーザーがコードエディタで別の箇所を読んでいる間にも起こる。
        // Buffer.Replaceは最小差分なのでcaretは通常tracking経由で追従するはずだが、対象文書が
        // 開いていてもアクティブでない場合に備え、編集前後でcaret位置を明示的に保存・復元する
        // (M0041: ビューポート先頭行の保存・復元はSceneSourceMarkerServiceと競合するため撤去した。
        // CaptureViewPositions参照)。
        List<ViewPositionSnapshot>Restorations= this.CaptureViewPositions(Buffer,VsBuffer);
        try {
            if(!this.TextBufferWriter.TryApply(Buffer,Context.Text,Changes,UndoDescription)){
                return new SceneLiveBufferWriteResult(
                    false,VsixText.Get("WorkspaceSourceChangedDuringEdit"));
            }

            return new SceneLiveBufferWriteResult(true,ErrorMessage:null);
        }
        finally {
            RestoreViewPositions(Restorations);
        }
    }

    /// <summary>
    /// M0036: シーンビューのCtrl+Z/Yから、対象.csファイルの実際のVS Undo/Redoを1回実行する。
    /// Undo/Redoの唯一の実体は引き続きVSのテキストUndoであり(ADR-0040)、ここは
    /// <see cref="TryGetLiveBuffer"/>でバッファを解決して<see cref="SceneTextBufferWriter"/>へ
    /// 委譲するだけの薄いラッパー。M0037: 対象.csファイルがまだ一度もlive bufferとして開かれて
    /// いない(RDTに無い)場合、以前は無言で失敗していた — <see cref="EnsureDocumentOpen"/>で
    /// <see cref="ApplyTextAsync"/>と同じopen手順を先に踏む。
    /// </summary>
    internal bool TryUndo(string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.EnsureDocumentOpen(FilePath);
        return this.TextBufferWriter is not null
            && this.TryGetLiveBuffer(FilePath,out ITextBuffer?Buffer,out _)&&Buffer is not null
            &&this.TextBufferWriter.TryUndo(Buffer);
    }

    internal bool TryRedo(string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.EnsureDocumentOpen(FilePath);
        return this.TextBufferWriter is not null
            && this.TryGetLiveBuffer(FilePath,out ITextBuffer?Buffer,out _)&&Buffer is not null
            &&this.TextBufferWriter.TryRedo(Buffer);
    }

    /// <summary>
    /// <see cref="ApplyTextAsync"/>(477-489行)の開く手順を<see cref="TryUndo"/>/<see cref="TryRedo"/>
    /// から再利用するための切り出し。対象がRoslynの解に見つからない、または既に開いている場合は
    /// 何もしない(ベストエフォート — 開けなくても後続の<see cref="TryGetLiveBuffer"/>が素直に
    /// falseを返す)。
    /// </summary>
    private void EnsureDocumentOpen(string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        DocumentId?DocumentId=this.Workspace.CurrentSolution.GetDocumentIdsWithFilePath(FilePath).FirstOrDefault();
        if(DocumentId is null||this.Workspace.IsDocumentOpen(DocumentId)||!this.Workspace.CanOpenDocuments){
            return;
        }

        this.OpenWithoutActivating(DocumentId);
    }

    /// <summary>The live text of a solution document (a scene JSON's unsaved edits included); null when it is not loaded.</summary>
    internal string?TryGetLiveText(string FilePath){
        DocumentId?Id=this.FindDocument(FilePath);
        return Id is not null&&GetTextDocument(this.Workspace.CurrentSolution,Id) is { }Document&&Document.TryGetText(out SourceText?Text)
            ?Text.ToString()
            :null;
    }

    /// <summary>A C# document or, for a JSON scene (ADR-0070), an additional document.</summary>
    private static TextDocument?GetTextDocument(Solution Solution,DocumentId Id)=>
        (TextDocument?)Solution.GetDocument(Id)??Solution.GetAdditionalDocument(Id);

    /// <summary>A JSON scene is an additional document, which Roslyn opens through its own entry point.</summary>
    private void OpenWithoutActivating(DocumentId DocumentId){
        if(this.Workspace.CurrentSolution.GetAdditionalDocument(DocumentId)is not null){
            this.Workspace.OpenAdditionalDocument(DocumentId,activate:false);
        } else {
            this.Workspace.OpenDocument(DocumentId,activate:false);
        }
    }

    /// <summary>
    /// RDT経由でファイルパスからmanaged/nativeのlive text bufferを解決する
    /// (<see cref="ApplyTextAsync"/>と<see cref="TryUndo"/>/<see cref="TryRedo"/>で共有)。
    /// 対象文書が開いていない場合はfalseを返す(呼び出し元でopenは行わない — Undo/Redoは
    /// 既に開いている文書に対してのみ意味を持つため)。
    /// </summary>
    /// <summary>
    /// M0051 Step 1-4: 対象Sceneの<c>.cs</c>を<b>ディスクまで保存する</b>。
    ///
    /// <para>
    /// M0047は「保存トリガーはビューポートのCtrl+Sのみ」と決め、書き戻しは
    /// <see cref="ApplyTextAsync"/>によるlive bufferへの反映で終わっていた。そのためCtrl+Sを押しても
    /// タブに<c>*</c>が残るだけでファイルは保存されず、ユーザーからもう一度VSのCtrl+Sを押す必要が
    /// あった(実機報告「メモリ上で反映して*になるがそうではなくさらにそれを保存して」)。
    /// </para>
    ///
    /// <para>
    /// 保存するのは<b>対象Sceneの1ファイルだけ</b>で、Save All相当のことはしない
    /// (ユーザーが意図していない他のドキュメントまで保存してしまわないため)。
    /// <c>RDTSAVEOPT_SaveIfDirty</c>なので、既に保存済みなら何も起きない。
    /// バッファ内容は変わらないため、これによる再解析は同じsource snapshotへ収束する。
    /// </para>
    /// </summary>
    internal bool TrySaveDocumentToDisk(
        string FilePath,out string?ErrorMessage,bool MissingDocumentIsSuccess=false){
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorMessage=null;
        if(Package.GetGlobalService(typeof(SVsRunningDocumentTable))is not IVsRunningDocumentTable Rdt){
            ErrorMessage=VsixText.Get("WorkspaceRunningDocumentTableUnavailable");
            return false;
        }

        IntPtr DocDataPointer=IntPtr.Zero;
        try {
            int FindResult=Rdt.FindAndLockDocument(
                (uint)_VSRDTFLAGS.RDT_NoLock,FilePath,
                out IVsHierarchy?Hierarchy,out uint ItemId,out DocDataPointer,out uint DocCookie);
            if(ErrorHandler.Failed(FindResult)){
                ErrorMessage=VsixText.Format("WorkspaceSaveFailed",$"0x{FindResult:X8}");
                return false;
            }
            if(DocCookie==0){
                if(MissingDocumentIsSuccess){ return true; }
                ErrorMessage=VsixText.Get("WorkspaceSaveDocumentNotOpen");
                return false;
            }

            int SaveResult=Rdt.SaveDocuments(
                (uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty,Hierarchy,ItemId,DocCookie);
            if(ErrorHandler.Failed(SaveResult)){
                ErrorMessage=VsixText.Format("WorkspaceSaveFailed",$"0x{SaveResult:X8}");
                return false;
            }

            return true;
        } finally {
            if(DocDataPointer!=IntPtr.Zero){
                Marshal.Release(DocDataPointer);
            }
        }
    }

    private bool TryGetLiveBuffer(
        string FilePath,out ITextBuffer?Buffer,
        out Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer?NativeBuffer){
        ThreadHelper.ThrowIfNotOnUIThread();
        Buffer=null;
        NativeBuffer=null;
        if(this.EditorAdapters is null
            ||Package.GetGlobalService(typeof(SVsRunningDocumentTable))is not IVsRunningDocumentTable Rdt){
            return false;
        }

        IntPtr DocDataPointer=IntPtr.Zero;
        try {
            int HResult=Rdt.FindAndLockDocument(
                (uint)_VSRDTFLAGS.RDT_NoLock,FilePath,
                out _,out _,out DocDataPointer,out _);
            if(ErrorHandler.Failed(HResult)||DocDataPointer==IntPtr.Zero){
                return false;
            }

            object DocData=Marshal.GetObjectForIUnknown(DocDataPointer);
            Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer?VsBuffer=
                DocData as Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer;
            if(VsBuffer is null&&DocData is IVsTextBufferProvider Provider
                &&ErrorHandler.Succeeded(Provider.GetTextBuffer(out Microsoft.VisualStudio.TextManager.Interop.IVsTextLines Lines))){
                VsBuffer=Lines as Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer;
            }
            if(VsBuffer is null){
                return false;
            }

            NativeBuffer=VsBuffer;
            Buffer=this.EditorAdapters.GetDocumentBuffer(VsBuffer);
            return Buffer is not null;
        } finally {
            if(DocDataPointer!=IntPtr.Zero){
                Marshal.Release(DocDataPointer);
            }
        }
    }

    private readonly struct ViewPositionSnapshot(ITextView View,ITrackingPoint Caret){
        internal ITextView View { get; }=View;

        internal ITrackingPoint Caret { get; }=Caret;
    }

    /// <summary>
    /// <paramref name="VsBuffer"/>を表示中の全ビュー(分割ウィンドウ含む)のcaret位置を、編集前
    /// スナップショット基準の<see cref="ITrackingPoint"/>として退避する。ドキュメントをアクティブ
    /// 化しない(<c>IVsTextManager.EnumViews</c>は既存ビューの列挙だけで済む)。
    ///
    /// <para>
    /// M0041: 以前はビューポート先頭行(Top)も同様に保存・復元していたが、これは
    /// <see cref="SceneSourceMarkerService"/>(選択が実際に変わったときだけスクロールする)と
    /// スクロール位置の決定権を奪い合っていた。M0038でこの書き戻しを非同期化・背後直列化した
    /// ことで、Topの捕獲(<see cref="CaptureViewPositions"/>)がコミットした瞬間ではなく
    /// 「書き戻しチェーンが自分の番に到達した瞬間」まで遅れるようになり、捕獲時点のスクロール位置が
    /// 選択直後の正しい位置からズレていた場合にそのズレをそのまま復元してしまい、ギズモ編集を
    /// 確定するたびに無関係な位置へ再スクロールする不具合として実機観測された。スクロール位置は
    /// <see cref="SceneSourceMarkerService"/>だけに一本化し、ここではタイピング中のcaret位置
    /// (無関係なバックグラウンド書き込みで動いてはならない)だけを保護する。
    /// </para>
    /// </summary>
    private List<ViewPositionSnapshot>CaptureViewPositions(ITextBuffer Buffer,IVsTextBuffer VsBuffer){
        List<ViewPositionSnapshot>Result=[];
        if(this.EditorAdapters is null
            ||Package.GetGlobalService(typeof(SVsTextManager))is not IVsTextManager TextManager
            ||TextManager.EnumViews(VsBuffer,out IVsEnumTextViews?ViewEnum)!=VSConstants.S_OK||ViewEnum is null){
            return Result;
        }

        IVsTextView[]Fetched=new IVsTextView[1];
        uint FetchedCount=0;
        while(ViewEnum.Next(1,Fetched,ref FetchedCount)==VSConstants.S_OK&&FetchedCount==1){
            if(this.EditorAdapters.GetWpfTextView(Fetched[0])is not { }WpfView){
                continue;
            }

            ITextSnapshot Snapshot=Buffer.CurrentSnapshot;
            SnapshotPoint CaretPoint=WpfView.Caret.Position.BufferPosition;
            Result.Add(new ViewPositionSnapshot(
                WpfView,Snapshot.CreateTrackingPoint(CaretPoint.Position,PointTrackingMode.Positive)));
        }

        return Result;
    }

    private static void RestoreViewPositions(List<ViewPositionSnapshot>Restorations){
        foreach(ViewPositionSnapshot Restoration in Restorations){
            ITextSnapshot Snapshot=Restoration.View.TextBuffer.CurrentSnapshot;
            Restoration.View.Caret.MoveTo(Restoration.Caret.GetPoint(Snapshot));
        }
    }

    private void OnWorkspaceChanged(object Sender,WorkspaceChangeEventArgs Args){
        // Only the kinds that can change a scene declaration. Reacting to everything would rebuild
        // on unrelated solution churn.
        switch(Args.Kind){
            case WorkspaceChangeKind.DocumentChanged:
            case WorkspaceChangeKind.DocumentAdded:
            case WorkspaceChangeKind.DocumentRemoved:
            case WorkspaceChangeKind.DocumentReloaded:
            case WorkspaceChangeKind.AdditionalDocumentChanged:
            case WorkspaceChangeKind.AdditionalDocumentAdded:
            case WorkspaceChangeKind.AdditionalDocumentRemoved:
            case WorkspaceChangeKind.AdditionalDocumentReloaded:
            case WorkspaceChangeKind.ProjectChanged:
            case WorkspaceChangeKind.ProjectReloaded:
            case WorkspaceChangeKind.SolutionReloaded:
            case WorkspaceChangeKind.SolutionChanged:
                this.ScheduleAnalysis();
                break;
        }
    }

    /// <summary>
    /// M0051 Step 1-6: Scene Designer自身がテキストを書き換えた直後(保存の書き戻し、Undo/Redoの同期)に
    /// 呼ぶ。デバウンスを挟まず解析を始めるので、リネージ検証と表示の追従が最短で届く。
    /// </summary>
    internal void ScheduleAnalysisNow()=>this.ScheduleAnalysis(Immediate:true);

    private void ScheduleAnalysis(bool Immediate=false){
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            // M0051 Step 1-8: 張り直しのたびに待ち時間が丸ごとリセットされると、無関係な
            // workspace変更が続く間いつまでも解析が始まらない。保留窓の開始時刻を保ち、
            // そこからの上限で頭打ちにする。窓を閉じるのは StartAnalysis の OnStarted だけ。
            SceneAnalysisScheduleOutcome Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                this.DebounceState,this.AnalysisClock.Elapsed,Immediate);
            this.DebounceState=Outcome.State;
            TimeSpan Delay=Outcome.Delay;

            this.CancelPendingAnalysisNoLock();
            DocumentId?ResolvedDocumentId=this.ActiveDocumentPath is null
                ?null
                :this.FindDocument(this.ActiveDocumentPath);
            SceneAnalysisRequest<DocumentId>Request;
            if(!Equals(this.ActiveDocumentId,ResolvedDocumentId)){
                this.ActiveDocumentId=ResolvedDocumentId;
                Request=this.AnalysisGeneration.ActivateDocument(ResolvedDocumentId);
            } else {
                Request=this.AnalysisGeneration.Refresh();
            }

            // 古い型Spanで別クラスを有効化しない。BeforeQueryStatusはこの状態を見るだけで、
            // ここからRoslynを待機しない。
            // (M0051 Step 1-8: ここにあった無条件の Analyzing 再代入は、上で決めた Unavailable を
            //  潰していたため削除した。)
            CancellationTokenSource Source=new();
            this.PendingAnalysis=Source;
            this.DebounceTimer=new System.Threading.Timer(
                static State=>((ScheduledAnalysis)State).Service.StartAnalysis((ScheduledAnalysis)State),
                new ScheduledAnalysis(this,Request,Source),
                Delay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void StartAnalysis(ScheduledAnalysis Scheduled){
        // M0051 Step 1-8: 保留窓を閉じるのはここ。<b>解析の完走ではなく開始</b>が条件であることが要点で、
        // 完走を条件にすると追い越され続けたときに窓が永久に閉じず、以後ずっと待ち時間0になって
        // 「即発火 → 即キャンセル」を繰り返す(実機で「シーンが開けなくなる」に直結した)。
        // RunAsyncのラムダ内ではなく同期部分で行う — ラムダに入れるとスケジューリング遅延の分だけ
        // クリアが遅れ、その間のScheduleAnalysisが誤って「保留中」と判断する。
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.DebounceState=SceneAnalysisDebouncePolicy.OnStarted(this.DebounceState);
        }

        // Started from a timer callback, so there is nothing to await it here. RunAsync keeps the
        // work joinable, and the handle is stored so VS can still block on it during shutdown.
        this.RunningAnalysis=this.TaskFactory.RunAsync(async ()=>{
            try {
                IReadOnlyList<SceneModel>Models=await this.AnalyzeAllAsync(
                    Scheduled.Request.Document,
                    Scheduled.Token).ConfigureAwait(false);

                // A cancelled analysis must not overwrite the newer one that cancelled it.
                if(Scheduled.Token.IsCancellationRequested){
                    return;
                }

                await this.TaskFactory.SwitchToMainThreadAsync(Scheduled.Token);
                SceneModel?First=Models.Count>0?Models[0]:null;
                lock(this.Gate){
                    if(this.Disposed
                        ||Scheduled.Token.IsCancellationRequested
                        ||!this.AnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.CurrentModel=First;
                    this.AllModels=Models;
                }

                this.ModelChanged?.Invoke(First);
                this.AllModelsChanged?.Invoke(Models);
            } catch(OperationCanceledException){
                // Superseded by a newer analysis.
            } catch(Exception Ex){
                // The last successful model stays; a transient failure never blanks the UI.
                ActivityLog.LogError(nameof(SceneWorkspaceService),Ex.ToString());
            }
        });
    }

    /// <summary>
    /// The scene an active document declares. Only scene JSON documents (<c>Scenes\**\*.json</c>) declare scenes (ADR-0070);
    /// every other document has none.
    /// </summary>
    private async Task<IReadOnlyList<SceneModel>>AnalyzeAllAsync(DocumentId?DocumentId,CancellationToken CancellationToken){
        if(DocumentId is not null&&this.Workspace.CurrentSolution.GetAdditionalDocument(DocumentId)is { }JsonScene
            &&SceneJsonCommitPlanner.IsJsonScene(JsonScene.FilePath,JsonScene.Project.FilePath)){
            return await AnalyzeJsonSceneAsync(JsonScene,CancellationToken).ConfigureAwait(false);
        }
        return [];
    }

    /// <summary>ADR-0070: a scene JSON document declares at most one scene.</summary>
    private static async Task<IReadOnlyList<SceneModel>>AnalyzeJsonSceneAsync(
        TextDocument JsonScene,CancellationToken CancellationToken){
        SourceText Text=await JsonScene.GetTextAsync(CancellationToken).ConfigureAwait(false);
        Compilation?Compilation=await JsonScene.Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null
            ||SceneJsonWorkspaceSource.TryReadClassFullyQualifiedName(Text.ToString())is not { }ClassName
            ||await ResolveJsonSceneAsync(JsonScene.Project,Compilation,JsonScene,ClassName,CancellationToken).ConfigureAwait(false)
                is not { }Resolved){
            return [];
        }
        return [Resolved.Model];
    }
    public void Dispose(){
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.Disposed=true;
            this.CancelPendingAnalysisNoLock();
        }

        this.Workspace.WorkspaceChanged-=this.OnWorkspaceChanged;
        this.ProjectSceneCatalogs.Clear();
        this.ProjectComponentCatalogs.Clear();
        Instance=null;
    }

    private sealed class ProjectSceneCatalogCache {
        internal ProjectSceneCatalogCache(VersionStamp Version,ProjectSceneCatalog Catalog){
            this.Version=Version;
            this.Catalog=Catalog;
        }

        internal VersionStamp Version { get; }
        internal ProjectSceneCatalog Catalog { get; }
    }

    private sealed class ProjectComponentCatalogCache {
        internal ProjectComponentCatalogCache(VersionStamp Version,IReadOnlyList<ComponentPaletteEntry>Entries){
            this.Version=Version;
            this.Entries=Entries;
        }

        internal VersionStamp Version { get; }
        internal IReadOnlyList<ComponentPaletteEntry>Entries { get; }
    }

    private void CancelPendingAnalysisNoLock(){
        this.DebounceTimer?.Dispose();
        this.DebounceTimer=null;
        this.PendingAnalysis?.Cancel();
        this.PendingAnalysis?.Dispose();
        this.PendingAnalysis=null;
    }

    private sealed class ScheduledAnalysis {
        internal ScheduledAnalysis(
            SceneWorkspaceService Service,
            SceneAnalysisRequest<DocumentId>Request,
            CancellationTokenSource Source){
            this.Service=Service;
            this.Request=Request;
            this.Token=Source.Token;
        }

        internal SceneWorkspaceService Service { get;}

        internal SceneAnalysisRequest<DocumentId>Request { get;}

        internal CancellationToken Token { get;}
    }
}
