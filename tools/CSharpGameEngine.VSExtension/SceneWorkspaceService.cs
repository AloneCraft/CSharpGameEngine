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
/// M0051 Step 1-5: <see cref="SceneWorkspaceService.ApplyTextStepsAsync"/>が積む1つのVS Undo単位。
/// <paramref name="Before"/>は<b>そのステップを適用する直前</b>のテキスト
/// (<c>SceneTextBufferApplyGuard</c>がバッファ内容と一致するか確認する基準)であり、
/// 保存全体の開始時点のテキストではない — ステップごとに繰り上がる。
/// <paramref name="UndoDescription"/>は<see cref="SceneUndoTransactionNaming.Format"/>で
/// 接頭辞を付けた形にしておくこと。委譲Undoの所有判定がこれを見る。
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
    /// M0051: 書き戻し結果の往復検証(<see cref="SceneSaveVerification"/>)が、編集後の構文木を
    /// 差し替えて再解析するために使う。解決時点と同じ世代のCompilationであることが重要。
    /// </summary>
    internal Compilation Compilation { get; }
}

internal readonly struct SceneLiveBufferWriteResult {
    internal SceneLiveBufferWriteResult(
        bool Succeeded,string?ErrorMessage,bool LiveTextChanged=false,
        int AppliedUnitCount=0,int VersionBeforeWrite=-1,int VersionAfterWrite=-1){
        this.Succeeded=Succeeded;
        this.ErrorMessage=ErrorMessage;
        this.LiveTextChanged=LiveTextChanged;
        this.AppliedUnitCount=AppliedUnitCount;
        this.VersionBeforeWrite=VersionBeforeWrite;
        this.VersionAfterWrite=VersionAfterWrite;
    }

    internal bool Succeeded { get; }

    internal string?ErrorMessage { get; }

    internal bool LiveTextChanged { get; }

    /// <summary>
    /// M0051 Step 1-5: 実際にテキストUndo履歴へ積まれたトランザクションの数。差分が空のステップは
    /// トランザクションを作らないため、<c>Steps.Count</c>とは一致しないことがある。
    /// <see cref="SceneTextUndoLedgerPolicy"/>がこの数だけ委譲を許す。
    /// </summary>
    internal int AppliedUnitCount { get; }

    /// <summary>書き込み開始直前のバッファ版番号(前回記録との照合に使う)。</summary>
    internal int VersionBeforeWrite { get; }

    /// <summary>全ステップ書き込み後のバッファ版番号。</summary>
    internal int VersionAfterWrite { get; }
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
    private SceneDocumentMenuSnapshot ActiveMenuSnapshot=new(SceneDesignerTargetState.Unavailable,[],[]);

    // ソリューションエクスプローラーで選択中のファイル用、アクティブ文書とは独立した解析スロット
    // (ADR-0041 / M0020: SEの右クリックはアクティブなコードエディタ文書と一致するとは限らないため、
    // Hierarchy/ModelChanged が依存するアクティブ文書側の状態には一切触れない別系統を持つ)。
    private DocumentId?SolutionExplorerDocumentId;
    private string?SolutionExplorerDocumentPath;
    private SceneDocumentMenuSnapshot SolutionExplorerMenuSnapshot=new(SceneDesignerTargetState.Unavailable,[],[]);
    private readonly SceneAnalysisGenerationGate<DocumentId>SolutionExplorerAnalysisGeneration=new();
    private CancellationTokenSource?PendingSolutionExplorerAnalysis;
    private JoinableTask?RunningSolutionExplorerAnalysis;

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
    /// several (e.g. PrimitiveShowcaseScene.cs). <see cref="CurrentModel"/>/<see cref="ModelChanged"/>
    /// stay unchanged (still "the first one found") so Hierarchy's existing behavior is untouched;
    /// this is computed in the same analysis pass, not a second one.
    /// </summary>
    internal IReadOnlyList<SceneModel>AllModels { get;private set;}=[];

    internal event Action<IReadOnlyList<SceneModel>>?AllModelsChanged;
    /// <summary>
    /// UIコマンド状態判定専用の解析済みスナップショット。Roslyn処理を開始・待機しない。
    /// </summary>
    internal SceneDocumentMenuSnapshot GetMenuSnapshot(string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        lock(this.Gate){
            if(string.Equals(this.ActiveDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)){
                return this.ActiveMenuSnapshot;
            }

            return string.Equals(this.SolutionExplorerDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)
                ?this.SolutionExplorerMenuSnapshot
                :new SceneDocumentMenuSnapshot(SceneDesignerTargetState.Unavailable,[],[]);
        }
    }

    /// <summary>
    /// ソリューションエクスプローラーの選択が変わるたびに<see cref="ActiveDocumentTracker"/>から
    /// 呼ばれる。アクティブ文書(<see cref="SetActiveDocument"/>)とは完全に独立しており、
    /// <see cref="CurrentModel"/>/<see cref="AllModels"/>やそのイベントには触れない
    /// (Hierarchy等の既存の消費者に一切影響を与えないため)。
    /// </summary>
    internal void SetSolutionExplorerSelection(string?FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();

        DocumentId?DocumentId=FilePath is null?null:this.FindDocument(FilePath);
        ScheduledSolutionExplorerAnalysis?Scheduled=null;
        lock(this.Gate){
            if(string.Equals(this.SolutionExplorerDocumentPath,FilePath,StringComparison.OrdinalIgnoreCase)
                &&Equals(this.SolutionExplorerDocumentId,DocumentId)){
                return;
            }

            this.SolutionExplorerDocumentPath=FilePath;
            this.SolutionExplorerDocumentId=DocumentId;
            SceneAnalysisRequest<DocumentId>Request=this.SolutionExplorerAnalysisGeneration.ActivateDocument(DocumentId);
            this.CancelPendingSolutionExplorerAnalysisNoLock();
            this.SolutionExplorerMenuSnapshot=new(
                DocumentId is null?SceneDesignerTargetState.Unavailable:SceneDesignerTargetState.Analyzing,[],[]);

            if(DocumentId is not null){
                CancellationTokenSource Source=new();
                this.PendingSolutionExplorerAnalysis=Source;
                Scheduled=new ScheduledSolutionExplorerAnalysis(this,Request,Source);
            }
        }

        // アクティブ文書側のScheduleAnalysisはタイピングの連打を吸収するために250msデバウンスするが、
        // ソリューションエクスプローラーの選択変更は「選択→即座に右クリックでメニュー表示」という
        // 単発操作であり、デバウンスするとBeforeQueryStatusが呼ばれる時点で解析がまだ始まって
        // すらいない(実機確認で、Scene実装クラスが複数あるファイルでも汎用的な単一項目しか
        // 出ない不具合として顕在化した)。連打耐性は上のAnalysisGeneration+CancellationTokenSource
        // のキャンセル機構が担うため、遅延を挟まず選択変更を検知した直後に解析を開始する。
        if(Scheduled is not null){
            this.StartSolutionExplorerAnalysis(Scheduled);
        }
    }

    private void StartSolutionExplorerAnalysis(ScheduledSolutionExplorerAnalysis Scheduled){
        this.RunningSolutionExplorerAnalysis=this.TaskFactory.RunAsync(async ()=>{
            try {
                SceneDocumentAnalysisResult Result=await this.AnalyzeAllAsync(
                    Scheduled.Request.Document,
                    Scheduled.Token).ConfigureAwait(false);

                if(Scheduled.Token.IsCancellationRequested){
                    return;
                }

                await this.TaskFactory.SwitchToMainThreadAsync(Scheduled.Token);
                lock(this.Gate){
                    if(this.Disposed
                        ||Scheduled.Token.IsCancellationRequested
                        ||!this.SolutionExplorerAnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.SolutionExplorerMenuSnapshot=Result.MenuSnapshot;
                }
            } catch(OperationCanceledException){
                // Superseded by a newer selection.
            } catch(Exception Ex){
                ActivityLog.LogError(nameof(SceneWorkspaceService),Ex.ToString());
                await this.TaskFactory.SwitchToMainThreadAsync();
                lock(this.Gate){
                    if(this.Disposed||!this.SolutionExplorerAnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.SolutionExplorerMenuSnapshot=new(SceneDesignerTargetState.Failed,[],[]);
                }
            }
        });
    }

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
            this.ActiveMenuSnapshot=new(this.ActiveDocumentPath is null?SceneDesignerTargetState.Unavailable:SceneDesignerTargetState.Analyzing,[],[]);
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
        Document?Document=DocumentId is { } Id
            ?this.Workspace.CurrentSolution.GetDocument(Id)
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
            throw new InvalidOperationException($"Roslyn workspace に対象ファイルがありません: {FilePath}");
        }

        SceneDocumentAnalysisResult Result=await this.AnalyzeAllAsync(DocumentId,CancellationToken).ConfigureAwait(false);
        return Result.Models;
    }

    /// <summary>
    /// 現在（または ActiveDocumentTracker が保持している最後）の C# 文書と同じ Project だけから
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
            ?this.Workspace.CurrentSolution.GetDocument(Id)?.Project.Id.Id.ToString("N")
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

        Compilation?Compilation=await Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null){
            return null;
        }

        string ProjectKey=Project.Id.Id.ToString("N");
        List<ProjectSceneCatalogEntry>Entries=[];
        foreach(INamedTypeSymbol Type in EnumerateTypes(Compilation.Assembly.GlobalNamespace)){
            CancellationToken.ThrowIfCancellationRequested();
            if(!SceneTypePredicates.ImplementsSceneInterface(Type)){
                continue;
            }

            TypeDeclarationSyntax?Declaration=Type.DeclaringSyntaxReferences
                .Select(Reference=>Reference.GetSyntax(CancellationToken) as TypeDeclarationSyntax)
                .Where(static Candidate=>Candidate is not null)
                .Cast<TypeDeclarationSyntax>()
                .Where(Candidate=>!IsGeneratedSceneFile(Candidate.SyntaxTree,CancellationToken))
                .OrderBy(Candidate=>Candidate.SyntaxTree.FilePath,StringComparer.OrdinalIgnoreCase)
                .ThenBy(Candidate=>Candidate.SpanStart)
                .FirstOrDefault();
            if(Declaration is null||string.IsNullOrWhiteSpace(Declaration.SyntaxTree.FilePath)){
                continue;
            }

            Entries.Add(new ProjectSceneCatalogEntry(
                new SceneKey(ProjectKey,SceneDeclarationAnalyzer.ComputeClassFullyQualifiedName(Type)),
                Type.Name,
                Declaration.SyntaxTree.FilePath,
                Declaration.SpanStart));
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
            ?this.Workspace.CurrentSolution.GetDocument(Id)?.Project.Id.Id.ToString("N")
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
            ?this.Workspace.CurrentSolution.GetDocument(Id)?.Project.Id.Id.ToString("N")
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

        INamedTypeSymbol?Type=EnumerateTypes(Compilation.Assembly.GlobalNamespace).FirstOrDefault(Candidate=>
            string.Equals(SceneDeclarationAnalyzer.ComputeClassFullyQualifiedName(Candidate),Key.ClassFullyQualifiedName,StringComparison.Ordinal));
        return Type is null||!SceneTypePredicates.ImplementsSceneInterface(Type)
            ?null
            :SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
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
        INamedTypeSymbol?Type=EnumerateTypes(Compilation.Assembly.GlobalNamespace).FirstOrDefault(Candidate=>
            string.Equals(
                SceneDeclarationAnalyzer.ComputeClassFullyQualifiedName(Candidate),
                Key.ClassFullyQualifiedName,
                StringComparison.Ordinal));
        if(Type is null||!SceneTypePredicates.ImplementsSceneInterface(Type)){
            return null;
        }

        TypeDeclarationSyntax?Declaration=Type.DeclaringSyntaxReferences
            .Select(Reference=>Reference.GetSyntax(CancellationToken)as TypeDeclarationSyntax)
            .Where(static Candidate=>Candidate is not null)
            .Cast<TypeDeclarationSyntax>()
            .Where(Candidate=>!IsGeneratedSceneFile(Candidate.SyntaxTree,CancellationToken))
            .OrderBy(Candidate=>Candidate.SyntaxTree.FilePath,StringComparer.OrdinalIgnoreCase)
            .ThenBy(static Candidate=>Candidate.SpanStart)
            .FirstOrDefault();
        if(Declaration is null||string.IsNullOrWhiteSpace(Declaration.SyntaxTree.FilePath)){
            return null;
        }

        Document?Document=this.Workspace.CurrentSolution.GetDocument(Declaration.SyntaxTree);
        if(Document is null){
            return null;
        }
        SyntaxNode Root=await Declaration.SyntaxTree.GetRootAsync(CancellationToken).ConfigureAwait(false);
        SourceText Text=await Declaration.SyntaxTree.GetTextAsync(CancellationToken).ConfigureAwait(false);
        SceneModel Model=SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
        return new SceneWorkspaceEditContext(Document.Id,Declaration.SyntaxTree.FilePath,Model,Root,Text,Compilation);
    }

    /// <summary>対象文書をbackgroundでopenし、RDTのlive bufferへ1 Undoで反映する。</summary>
    internal Task<SceneLiveBufferWriteResult>ApplyTextAsync(
        SceneWorkspaceEditContext Context,IReadOnlyList<TextChange>Changes,string UndoDescription,
        CancellationToken CancellationToken)=>
        this.ApplyTextStepsAsync(
            Context,[new SceneTextWriteStep(Context.Text,Changes,UndoDescription)],CancellationToken);

    /// <summary>
    /// M0051 Step 1-5: 書き戻しを<b>ステップごとに独立したVS Undoトランザクション</b>として適用する。
    ///
    /// <para>
    /// <b>なぜ必要か</b>: 以前は保存1回=Undo1単位だったため、3回ギズモを動かしてから保存すると
    /// 保存後のCtrl+Zが3回分まとめて戻ってしまった。ユーザーの確定仕様は「1操作=1 Undo単位」。
    /// <see cref="SceneSourceEditor.ApplyForSaveSteps"/>が履歴エントリごとの中間構文木を返すので、
    /// その隣接差分を1ステップとして順に積む。
    /// </para>
    ///
    /// <para>
    /// 既存の<see cref="ApplyTextAsync"/>を単純にループで呼べないのは、あちらが
    /// <see cref="SceneWorkspaceEditContext.Text"/>を常に「適用前テキスト」として使うため。
    /// 2回目以降はバッファが既に変わっており、<c>SceneTextBufferApplyGuard</c>が必ず不一致を返して
    /// 「編集中にC#ソースが変化したため上書きを中止しました」で失敗する。ステップごとに
    /// <see cref="SceneTextWriteStep.Before"/>を繰り上げる必要がある。
    /// </para>
    ///
    /// <para>
    /// 全ステップはUIスレッド上で同期的に連続適用されるため、途中に外部編集が割り込む余地は無い。
    /// 呼び出し側(<see cref="SceneEditorSessionService.SaveAsync"/>)が各ステップの差分を
    /// <b>書き込み前に</b>オフラインで検算しており、ここで途中失敗するのは想定外の状態のみ。
    /// </para>
    /// </summary>
    internal async Task<SceneLiveBufferWriteResult>ApplyTextStepsAsync(
        SceneWorkspaceEditContext Context,IReadOnlyList<SceneTextWriteStep>Steps,
        CancellationToken CancellationToken){
        await this.TaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(this.EditorAdapters is null||this.TextBufferWriter is null){
            return new SceneLiveBufferWriteResult(false,"Visual StudioのEditor/Undoサービスを取得できません。");
        }
        if(!this.Workspace.IsDocumentOpen(Context.DocumentId)){
            if(!this.Workspace.CanOpenDocuments){
                return new SceneLiveBufferWriteResult(false,"対象SceneのC#文書をlive bufferとして開けません。");
            }
            this.Workspace.OpenDocument(Context.DocumentId,activate:false);
        }
        if(!this.TryGetLiveBuffer(
                                  Context.FilePath,out ITextBuffer?Buffer,
                                  out Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer?VsBuffer)
            ||Buffer is null||VsBuffer is null){
            return new SceneLiveBufferWriteResult(false,"対象SceneのVisual Studio text bufferを取得できません。");
        }

        // ギズモ/インスペクター編集はユーザーがコードエディタで別の箇所を読んでいる間にも起こる。
        // Buffer.Replaceは最小差分なのでcaretは通常tracking経由で追従するはずだが、対象文書が
        // 開いていてもアクティブでない場合に備え、編集前後でcaret位置を明示的に保存・復元する
        // (M0041: ビューポート先頭行の保存・復元はSceneSourceMarkerServiceと競合するため撤去した。
        // CaptureViewPositions参照)。
        // caretの保存・復元は全ステップを1回で囲む(ステップ間で復元すると、まだ途中の状態へ
        // caretを戻したうえで次のステップがまた動かすことになり、無駄なうえに揺れる)。
        List<ViewPositionSnapshot>Restorations= this.CaptureViewPositions(Buffer,VsBuffer);
        int VersionBeforeWrite=SceneTextBufferWriter.GetVersionNumber(Buffer);
        int AppliedUnits=0;
        for(int Index=0;Index<Steps.Count;Index++){
            SceneTextWriteStep Step=Steps[Index];
            if(this.TextBufferWriter.TryApply(Buffer,Step.Before,Step.Changes,Step.UndoDescription)){
                // 差分が空のステップはトランザクションを作らないので、Undo単位としては数えない。
                if(Step.Changes.Count>0){
                    AppliedUnits++;
                }

                continue;
            }

            // Index==0は通常の競合(ユーザーが編集中にC#を触った)。Index>0はオフライン検算を
            // 通ったはずの中間ステップが実バッファと食い違ったということで、想定外の状態。
            // 既に前段までが書き込まれているため、その事実を明示する。
            return Index==0
                ?new SceneLiveBufferWriteResult(
                    false,"編集中にC#ソースが変化したため上書きを中止しました。",LiveTextChanged:true)
                :new SceneLiveBufferWriteResult(
                    false,
                    $"保存の途中({Index+1}/{Steps.Count}段目)でC#ソースの状態が想定と食い違ったため中断しました。"
                    +"直前までの変更は適用済みです。Ctrl+Zで戻せます。",
                    LiveTextChanged:true);
        }

        RestoreViewPositions(Restorations);
        return new SceneLiveBufferWriteResult(
            true,ErrorMessage:null,LiveTextChanged:false,
            AppliedUnits,VersionBeforeWrite,SceneTextBufferWriter.GetVersionNumber(Buffer));
    }

    /// <summary>
    /// M0051 Step 1-5(実機修正): 対象<c>.cs</c>のlive bufferの版番号。
    /// <see cref="SceneTextUndoLedgerPolicy"/>が「自分の最後の操作以降だれもバッファを触っていないか」を
    /// これで確かめる。バッファが解決できない場合はfalse(=委譲しない)。
    /// </summary>
    internal bool TryGetBufferVersion(string FilePath,out int Version){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.EnsureDocumentOpen(FilePath);
        if(this.TryGetLiveBuffer(FilePath,out ITextBuffer?Buffer,out _)&&Buffer is not null){
            Version=SceneTextBufferWriter.GetVersionNumber(Buffer);
            return true;
        }

        Version=-1;
        return false;
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

        this.Workspace.OpenDocument(DocumentId,activate:false);
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
    /// バッファ内容は変わらないため、これによる再解析が来ても
    /// <see cref="SceneExternalChangePolicy"/>は<c>RealContentActuallyChanged==false</c>で
    /// <c>Ignore</c>を返す(M0048の不変条件をそのまま利用する)。
    /// </para>
    /// </summary>
    internal bool TrySaveDocumentToDisk(string FilePath,out string?ErrorMessage){
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorMessage=null;
        if(Package.GetGlobalService(typeof(SVsRunningDocumentTable))is not IVsRunningDocumentTable Rdt){
            ErrorMessage="Visual StudioのRunning Document Tableを取得できません。";
            return false;
        }

        IntPtr DocDataPointer=IntPtr.Zero;
        try {
            int FindResult=Rdt.FindAndLockDocument(
                (uint)_VSRDTFLAGS.RDT_NoLock,FilePath,
                out IVsHierarchy?Hierarchy,out uint ItemId,out DocDataPointer,out uint DocCookie);
            if(ErrorHandler.Failed(FindResult)||DocCookie==0){
                ErrorMessage="対象Sceneのドキュメントが開かれていないため保存できません。";
                return false;
            }

            int SaveResult=Rdt.SaveDocuments(
                (uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty,Hierarchy,ItemId,DocCookie);
            if(ErrorHandler.Failed(SaveResult)){
                ErrorMessage=$"対象Sceneのファイル保存に失敗しました(HRESULT 0x{SaveResult:X8})。";
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
            this.ActiveMenuSnapshot=new(
                SceneAnalysisDebouncePolicy.PendingMenuState(this.ActiveDocumentPath),[],[]);
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
                SceneDocumentAnalysisResult Result=await this.AnalyzeAllAsync(
                    Scheduled.Request.Document,
                    Scheduled.Token).ConfigureAwait(false);

                // A cancelled analysis must not overwrite the newer one that cancelled it.
                if(Scheduled.Token.IsCancellationRequested){
                    return;
                }

                await this.TaskFactory.SwitchToMainThreadAsync(Scheduled.Token);
                SceneModel?First=Result.Models.Count>0?Result.Models[0]:null;
                lock(this.Gate){
                    if(this.Disposed
                        ||Scheduled.Token.IsCancellationRequested
                        ||!this.AnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.CurrentModel=First;
                    this.AllModels=Result.Models;
                    this.ActiveMenuSnapshot=Result.MenuSnapshot;
                }

                this.ModelChanged?.Invoke(First);
                this.AllModelsChanged?.Invoke(Result.Models);
            } catch(OperationCanceledException){
                // Superseded by a newer analysis.
            } catch(Exception Ex){
                ActivityLog.LogError(nameof(SceneWorkspaceService),Ex.ToString());
                await this.TaskFactory.SwitchToMainThreadAsync();
                lock(this.Gate){
                    if(this.Disposed||!this.AnalysisGeneration.IsCurrent(Scheduled.Request)){
                        return;
                    }

                    this.ActiveMenuSnapshot=new(SceneDesignerTargetState.Failed,[],[]);
                }
            }
        });
    }

    /// <summary>
    /// アクティブ文書に宣言された全<c>IScene</c>実装を宣言順で解析する。並行して保持する型範囲は
    /// コードウィンドウの右クリック専用のVSIX内部メタデータであり、Source Generatorと共有する
    /// <c>SceneModel</c>契約は変更しない。
    /// </summary>
    private async Task<SceneDocumentAnalysisResult>AnalyzeAllAsync(DocumentId?DocumentId,CancellationToken CancellationToken){
        if(DocumentId is null){
            return new([],new SceneDocumentMenuSnapshot(SceneDesignerTargetState.Unavailable,[],[]));
        }

        Document?Document=this.Workspace.CurrentSolution.GetDocument(DocumentId);
        if(Document is null){
            throw new InvalidOperationException($"Roslyn workspace に対象Documentがありません: {DocumentId}");
        }

        Compilation?Compilation=await Document.Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        SyntaxNode?Root=await Document.GetSyntaxRootAsync(CancellationToken).ConfigureAwait(false);
        SemanticModel?SemanticModel=await Document.GetSemanticModelAsync(CancellationToken).ConfigureAwait(false);
        if(Compilation is null||Root is null||SemanticModel is null){
            throw new InvalidOperationException($"Roslyn解析に必要な情報を取得できませんでした: {Document.FilePath}");
        }

        List<SceneModel>Models=[];
        List<SceneDesignerMenuItem>Items=[];
        List<SceneDocumentTypeRange>TypeRanges=[];
        foreach(TypeDeclarationSyntax Declaration in Root.DescendantNodes().OfType<TypeDeclarationSyntax>().OrderBy(Node=>Node.SpanStart)){
            CancellationToken.ThrowIfCancellationRequested();
            bool IsScene=false;
            string?FullyQualifiedName=null;
            if(SceneDeclarationAnalyzer.IsSceneCandidate(Declaration)
                &&SemanticModel.GetDeclaredSymbol(Declaration,CancellationToken)is INamedTypeSymbol Type
                &&SceneTypePredicates.ImplementsSceneInterface(Type)){
                SceneModel Model=SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
                Models.Add(Model);
                Items.Add(new SceneDesignerMenuItem(Model.ClassName,Model.ClassFullyQualifiedName));
                IsScene=true;
                FullyQualifiedName=Model.ClassFullyQualifiedName;
            }

            TypeRanges.Add(new SceneDocumentTypeRange(
                Declaration.SpanStart,
                Declaration.Span.End,
                IsScene,
                FullyQualifiedName));
        }

        return new(
            Models,
            new SceneDocumentMenuSnapshot(SceneDesignerTargetState.SceneType,TypeRanges,Items));
    }
    public void Dispose(){
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.Disposed=true;
            this.CancelPendingAnalysisNoLock();
            this.CancelPendingSolutionExplorerAnalysisNoLock();
        }

        this.Workspace.WorkspaceChanged-=this.OnWorkspaceChanged;
        this.ProjectSceneCatalogs.Clear();
        this.ProjectComponentCatalogs.Clear();
        Instance=null;
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateTypes(INamespaceSymbol Namespace){
        foreach(INamedTypeSymbol Type in Namespace.GetTypeMembers()){
            yield return Type;
            foreach(INamedTypeSymbol Nested in EnumerateNestedTypes(Type)){
                yield return Nested;
            }
        }

        foreach(INamespaceSymbol Child in Namespace.GetNamespaceMembers()){
            foreach(INamedTypeSymbol Type in EnumerateTypes(Child)){
                yield return Type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateNestedTypes(INamedTypeSymbol Type){
        foreach(INamedTypeSymbol Nested in Type.GetTypeMembers()){
            yield return Nested;
            foreach(INamedTypeSymbol Descendant in EnumerateNestedTypes(Nested)){
                yield return Descendant;
            }
        }
    }

    private static bool IsGeneratedSceneFile(SyntaxTree SyntaxTree,CancellationToken CancellationToken){
        string FilePath=SyntaxTree.FilePath??string.Empty;
        if(FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase)
            ||FilePath.EndsWith(".generated.cs",StringComparison.OrdinalIgnoreCase)){
            return true;
        }

        Microsoft.CodeAnalysis.Text.SourceText Text=SyntaxTree.GetText(CancellationToken);
        int PrefixLength=Math.Min(Text.Length,256);
        return Text.ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(0,PrefixLength))
            .IndexOf("<auto-generated",StringComparison.OrdinalIgnoreCase)>=0;
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

    private void CancelPendingSolutionExplorerAnalysisNoLock(){
        this.PendingSolutionExplorerAnalysis?.Cancel();
        this.PendingSolutionExplorerAnalysis?.Dispose();
        this.PendingSolutionExplorerAnalysis=null;
    }

    private sealed class ScheduledSolutionExplorerAnalysis {
        internal ScheduledSolutionExplorerAnalysis(
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
