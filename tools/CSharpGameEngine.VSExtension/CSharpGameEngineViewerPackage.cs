using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX shell for the engine commands, asset preview, and the ECS project startup flow.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly=true,AllowsBackgroundLoading=true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu",1)]
[ProvideToolWindow(typeof(HierarchyToolWindowPane),
    Style=VsDockStyle.Linked,Window=ToolWindowGuids80.SolutionExplorer,
    Orientation=ToolWindowOrientation.Left,DockedWidth=300)]
[ProvideToolWindow(typeof(SceneDesignerToolWindowPane),
    Style=VsDockStyle.Tabbed,Window=ToolWindowGuids80.SolutionExplorer)]
// Direct .slnx launch is preferred, but a VS debug launch can still arrive at Start Window.
// Keep NoSolution as a fallback so that route opens UserEcsProject without taking editor focus.
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string,PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(StartupAutoOpenPolicy.NoSolutionContext,PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string,PackageAutoLoadFlags.BackgroundLoad)]
[ProvideEditorFactory(typeof(GlbFbxPreviewEditorFactory),110,TrustLevel=__VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(GlbFbxPreviewEditorFactory),VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".glb",50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".gltf",50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".fbx",50)]
// Tools>Options > C# Game Engine > Scene Editor。120/121 は VSPackage.resx のカテゴリ名/ページ名。
// GeneratePkgDefFile=true なので .pkgdef へ自動登録される(マニフェストの手編集は不要)。
[ProvideOptionPage(typeof(SceneEditorOptionsPage),"C# Game Engine","Scene Editor",120,121,
    supportsAutomation:true)]
public sealed class CSharpGameEngineViewerPackage:AsyncPackage {
    public const string PackageGuidString="aec17804-e2e4-46e7-b373-de11af816203";

    private ToggleFabWatchCommand?FabWatchCommand;
    private AssetMetaWatcher?AssetMetaWatcher;
    private SceneWorkspaceService?SceneWorkspace;
    private SceneEditorSessionService?SceneSession;
    private ActiveDocumentTracker?DocumentTracker;

    protected override async Task InitializeAsync(CancellationToken CancellationToken,IProgress<ServiceProgressData>Progress){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);

        // ソリューションを開く処理はRoslyn/ToolWindow初期化より先に開始する。Start Windowでは
        // 後段のサービスが利用できないことがあり、末尾まで待つと自動起動自体が失われる。
        _=JoinableTaskFactory.RunAsync(TryAutoOpenUserEcsProjectAsync);

        await OpenViewerCommand.InitializeAsync(this);
        FabWatchCommand= await ToggleFabWatchCommand.InitializeAsync(this);
        await OpenFabCommand.InitializeAsync(this);
        await OpenGameCommand.InitializeAsync(this);
        await ShowHierarchyToolWindowCommand.InitializeAsync(this);
        await ApplyGameEngineLayoutCommand.InitializeAsync(this);

        RegisterEditorFactory(new GlbFbxPreviewEditorFactory());

        // ADR-0040: the Scene editor reads scenes from live Roslyn. Both of these degrade to null
        // when the managed language service is unavailable, in which case the Hierarchy simply shows
        // its "open a scene file" message instead of failing package load.
        SceneWorkspace=SceneWorkspaceService.Initialize();
        SceneEditorSettings.ConfigureStoreProvider(
            ()=>new ShellSettingsManager(this).GetWritableSettingsStore(SettingsScope.UserSettings));

        if(SceneWorkspace is not null){
            SceneSession=new SceneEditorSessionService(SceneWorkspace);
            SceneDesignerToolWindowPane.ConfigureSessionProvider(()=>this.SceneSession);
            SceneEditorHub.SelectedNodePathChanged+=this.OnLegacyHierarchySelectionChanged;
            SceneSession.Selection.SelectionChanged+=this.OnSelectionChangedForHierarchy;
            SceneWorkspace.ModelChanged+=SceneEditorHub.PublishSceneModel;
            DocumentTracker=ActiveDocumentTracker.Start(SceneWorkspace);
            if(DocumentTracker is not null){
                await OpenSceneDesignerCommand.InitializeAsync(this,SceneSession,DocumentTracker);
            }
        }

        AssetMetaWatcher=AssetMetaWatcher.StartIfProjectExists();
        _=JoinableTaskFactory.RunAsync(TryApplyInitialLayoutAsync);
    }

    /// <summary>
    /// Start Window から起動した場合だけ UserEcsProject を開く互換経路です。
    /// これは SolutionExistsAndFullyLoaded の通常起動を置き換えません。DTE の MainWindow.Activate は
    /// Tab などのキーボードフォーカスをコードエディタから奪うため、ここでは絶対に呼びません。
    /// </summary>
    private async Task TryAutoOpenUserEcsProjectAsync(){
        // devenv自身のコマンドラインが既にソリューションを指定している場合(このリポジトリのF5設定が
        // 常にそう)、ネイティブな起動シーケンスが確実に処理する。この判定はプロセス起動時点の
        // 事実だけを見るためタイミングに一切依存せず、下のポーリング系の判定(IsSolutionOpenAsync等)
        // が抱える「確認するタイミング自体がネイティブ読み込みの完了より早いと同じ結果を返し続ける」
        // というレースを構造的に回避できる(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution
        // のコメント参照)。
        if(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution(Environment.GetCommandLineArgs())){
            return;
        }

        if(!StartupAutoOpenPolicy.ShouldOpenUserEcsProject(await IsSolutionOpenAsync(),await GetSolutionPathAsync())){
            return;
        }

        string?RepoRoot=RepoLocator.ResolveRepoRoot();
        if(RepoRoot is null){
            return;
        }

        string SolutionPath=Path.Combine(RepoRoot,"UserEcsProject.slnx");
        if(!File.Exists(SolutionPath)){
            return;
        }

        try {
            // Start Window の初期化と競合しないよう少し待つ。待機後にもう一度確認することで、
            // ユーザーがその間に別のソリューションを開いた場合は何もしない。
            await Task.Delay(TimeSpan.FromSeconds(2),DisposalToken);
            if(!StartupAutoOpenPolicy.ShouldOpenUserEcsProject(await IsSolutionOpenAsync(),await GetSolutionPathAsync())){
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if(await GetServiceAsync(typeof(SDTE))is not DTE Dte){
                return;
            }

            // UIスレッドを取れるまで待っている間にソリューションが開き終わっていることがある
            // (UIスレッドはコマンドラインで渡されたソリューションの読み込みで塞がっているため、
            // コールドスタートではこれが通常)。上の判定は最大でも2秒前の情報なので、実行の直前に
            // もう一度見ないと「開いたばかりのソリューションを閉じて開き直す」ことになる。
            if(!StartupAutoOpenPolicy.ShouldOpenUserEcsProject(await IsSolutionOpenAsync(),Dte.Solution?.FullName)){
                return;
            }

            Dte.ExecuteCommand("File.OpenProject",$"\"{SolutionPath}\"");
        } catch(COMException){
            // VS startup must remain usable even if the shell rejects the early open request.
        } catch(OperationCanceledException){
            // Package disposal while Start Window is still loading is benign.
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(CSharpGameEngineViewerPackage),Ex.ToString());
        }
    }

    /// <summary>
    /// Hierarchy は段階移行中のため NodePath を発行しているので、Package境界で一度だけ session ID に
    /// 解決します。Viewport/Inspectorはこの後の共通Selectionだけを購読し、static Hubを読みません。
    /// </summary>
    private void OnLegacyHierarchySelectionChanged(NodePath?Path){
        if(this.SceneSession?.PinnedSnapshot is not { } Snapshot){
            return;
        }

        EditorNodeId?Id=Path is { } Selected&&Snapshot.Projection?.TryGetId(Selected,out EditorNodeId Value)==true
            ?Value
            :null;
        this.SceneSession.Selection.Select(
            Snapshot.Key,
            Id is { } NodeId?[NodeId]:[],
            Id,
            ComponentType:null,
            MemberName:null,
            SceneSelectionOrigin.Hierarchy,
            Guid.NewGuid());
    }

    /// <summary>
    /// M0020 Phase 2: ヒエラルキーは静的<see cref="SceneEditorHub.SelectedNodePath"/>を読む段階移行中
    /// (<see cref="HierarchyToolWindowControl"/>参照)のため、共通Selectionからの変更をここで橋渡しする。
    /// Origin=Hierarchy自身の変更はTreeViewの見た目と既に一致しているので素通りする(往復回避)。
    /// </summary>
    private void OnSelectionChangedForHierarchy(SceneSelection Selection){
        if(Selection.Origin==SceneSelectionOrigin.Hierarchy){
            return;
        }

        if(this.SceneSession?.PinnedSnapshot is not { } Snapshot||!Snapshot.Key.Equals(Selection.Key)){
            return;
        }

        NodePath?Path=Selection.PrimaryNodeId is { } Id&&Snapshot.Projection?.TryGetNode(Id,out SceneProjectionNode Node)==true
            ?Node.Path
            :null;
        SceneEditorHub.PublishSelectedNodePath(Path);
    }

    private async Task TryApplyInitialLayoutAsync(){
        try {
            if(await GameEngineLayout.WasInitialLayoutAppliedAsync(this)){
                return;
            }

            // NoSolution fallback may have issued File.OpenProject only moments ago. Do not create
            // or show tool windows until a solution is actually open; the presentation itself uses
            // ShowNoActivate, so this wait is not a keyboard-focus workaround.
            if(!await WaitForSolutionOpenAsync()){
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if(await GetServiceAsync(typeof(SDTE))is not DTE Dte){
                return;
            }

            await GameEngineLayout.ApplyAsync(this,Dte);
            await GameEngineLayout.MarkInitialLayoutAppliedAsync(this);
        } catch(COMException){
            // Layout is optional and must not prevent VS from starting.
        } catch(OperationCanceledException){
            // Package disposal while waiting for a solution is benign.
        }
    }

    private async Task<bool> WaitForSolutionOpenAsync(){
        for(int Attempt=0;Attempt<80;Attempt++){
            if(await IsSolutionOpenAsync()){
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250),DisposalToken);
        }

        return false;
    }

    private async Task<bool> IsSolutionOpenAsync(){
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if(await GetServiceAsync(typeof(SVsSolution))is not IVsSolution Solution){
            return false;
        }

        Solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen,out object IsOpen);
        return IsOpen is bool Value&&Value;
    }

    /// <summary>
    /// 開いている(または開きかけの)ソリューションファイルのパス。無ければnull。
    /// <see cref="__VSPROPID.VSPROPID_IsSolutionOpen"/>が立つ前でもパスは判ることがあるため、
    /// 自動オープンの判定では<see cref="IsSolutionOpenAsync"/>と併用する
    /// (<see cref="StartupAutoOpenPolicy.ShouldOpenUserEcsProject"/>参照)。
    /// </summary>
    private async Task<string?>GetSolutionPathAsync(){
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if(await GetServiceAsync(typeof(SVsSolution))is not IVsSolution Solution){
            return null;
        }

        return ErrorHandler.Succeeded(Solution.GetSolutionInfo(out _,out string?SolutionFile,out _))
            ?SolutionFile
            :null;
    }

    protected override void Dispose(bool Disposing){
        if(Disposing){
            FabWatchCommand?.Dispose();
            AssetMetaWatcher?.Dispose();
            DocumentTracker?.Dispose();

            SceneEditorHub.SelectedNodePathChanged-=this.OnLegacyHierarchySelectionChanged;
            if(SceneSession is not null){
                SceneSession.Selection.SelectionChanged-=this.OnSelectionChangedForHierarchy;
            }

            SceneSession?.Dispose();
            SceneSession=null;
            SceneDesignerToolWindowPane.ConfigureSessionProvider(null);
            SceneEditorSettings.ConfigureStoreProvider(null);

            if(SceneWorkspace is not null){
                SceneWorkspace.ModelChanged-=SceneEditorHub.PublishSceneModel;
                SceneWorkspace.Dispose();
            }
        }

        base.Dispose(Disposing);
    }
}
