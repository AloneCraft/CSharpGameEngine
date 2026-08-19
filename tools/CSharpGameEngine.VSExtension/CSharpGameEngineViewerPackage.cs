using System;
using System.IO;
using System.Collections.Generic;
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
[ProvideToolWindow(typeof(InspectorToolWindowPane),
    Style=VsDockStyle.Linked,Window=ToolWindowGuids80.SolutionExplorer,
    Orientation=ToolWindowOrientation.Right,DockedWidth=300)]
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
    private SceneSourceMarkerService?SourceMarkerService;

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
        await ShowInspectorToolWindowCommand.InitializeAsync(this);

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
            InspectorToolWindowPane.ConfigureSessionProvider(()=>this.SceneSession);
            SourceMarkerService=SceneSourceMarkerService.Start();
            SceneEditorHub.HierarchySelectionChanged+=this.OnHierarchySelectionChanged;
            SceneEditorHub.HierarchyNavigationRequested+=this.OnHierarchyNavigationRequested;
            SceneEditorHub.HierarchyEditRequested+=this.OnHierarchyEditRequested;
            SceneEditorHub.SceneModelChanged+=this.OnSceneModelChangedForSourceMarkers;
            SceneSession.Selection.SelectionChanged+=this.OnSelectionChangedForHierarchy;
            SceneSession.PinnedSnapshotChanged+=this.OnPinnedSnapshotChangedForHub;
            SceneWorkspace.ModelChanged+=this.OnWorkspaceModelChangedForHub;
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
    private void OnHierarchyEditRequested(HierarchyEditRequest Request)=>
        _=this.JoinableTaskFactory.RunAsync(()=>this.HandleHierarchyEditAsync(Request));

    /// <summary>
    /// M0047: NodeIdの解決とM0047までのC#書き戻しは、pin中のSceneがあれば
    /// <see cref="SceneEditorSessionService.PinnedSnapshot"/>(=Documentの現在の投影、未保存の
    /// 追加・改名・移動を含む)を基準にする。実解析だけを基準にすると、この一連のセッション内で
    /// 既に追加/改名済みのNodeをHierarchyから続けて操作できなくなる。
    /// </summary>
    private async Task HandleHierarchyEditAsync(HierarchyEditRequest Request){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if(this.SceneSession is not { } Session||SceneEditorHub.ActiveSceneModel is not { } Model){
            return;
        }

        SceneDocumentSnapshot?Snapshot=Session.TryCreateSceneKey(Model,out SceneKey Key)
            &&Session.PinnedSceneKey is { }Pinned&&Pinned.Equals(Key)
            ?Session.PinnedSnapshot
            :Session.GetSnapshotForModel(Model);
        if(Snapshot is null||Snapshot.Projection is not { } Projection){
            return;
        }

        IReadOnlyList<NodePath>Roots=SelectHierarchyRoots(Request.Paths);
        List<SceneEditOperation>Operations=[];
        switch(Request.Kind){
            case HierarchyEditKind.AddChild:
                foreach(NodePath Path in Roots){
                    if(Projection.TryGetId(Path,out EditorNodeId ParentId)){
                        Operations.Add(new AddNodeOperation(
                            Snapshot.Key,ParentId,Snapshot.DocumentVersion,"Node"));
                    }
                }
                break;
            case HierarchyEditKind.Rename:
                string?RequestedName=Request.NewName;
                if(Request.Paths.FirstOrDefault() is { } RenamePath
                    &&Projection.TryGetId(RenamePath,out EditorNodeId RenameId)
                    &&!string.IsNullOrWhiteSpace(RequestedName)){
                    Operations.Add(new RenameNodeOperation(
                        Snapshot.Key,RenameId,Snapshot.DocumentVersion,RequestedName!.Trim()));
                }
                break;
            case HierarchyEditKind.Remove:
                foreach(NodePath Path in Roots){
                    if(Projection.TryGetId(Path,out EditorNodeId RemoveId)){
                        Operations.Add(new RemoveNodeOperation(
                            Snapshot.Key,RemoveId,Snapshot.DocumentVersion));
                    }
                }
                break;
            case HierarchyEditKind.Reparent:
                if(Request.TargetPath is not { } TargetPath
                    ||!Projection.TryGetId(TargetPath,out EditorNodeId TargetId)
                    ||Roots.Any(Path=>Path.Equals(TargetPath)||TargetPath.IsDescendantOf(Path))){
                    return;
                }

                var ReparentIndex=Math.Max(0,Request.TargetIndex);
                foreach(NodePath Path in Roots.OrderBy(static Path=>Path.ToDisplayString(),StringComparer.Ordinal)){
                    if(Projection.TryGetId(Path,out EditorNodeId MoveId)){
                        Operations.Add(new ReparentNodeOperation(
                            Snapshot.Key,MoveId,Snapshot.DocumentVersion,TargetId,ReparentIndex++));
                    }
                }
                break;
            case HierarchyEditKind.Reorder:
                if(Request.Paths.FirstOrDefault() is { } ReorderPath
                    &&Projection.TryGetId(ReorderPath,out EditorNodeId ReorderId)){
                    Operations.Add(new ReorderNodeOperation(
                        Snapshot.Key,ReorderId,Snapshot.DocumentVersion,Math.Max(0,Request.TargetIndex)));
                }
                break;
        }

        if(Operations.Count==0){
            return;
        }

        SceneEditCommitRequest CommitRequest=new(
            Snapshot.Key,Snapshot.DocumentVersion,
            Request.Kind switch {
                HierarchyEditKind.AddChild=>"Add child",
                HierarchyEditKind.Rename=>"Rename node",
                HierarchyEditKind.Remove=>"Remove node",
                HierarchyEditKind.Reparent=>"Reparent node",
                HierarchyEditKind.Reorder=>"Reorder node",
                _=>"Edit scene hierarchy",
            },
            Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,
            DirectOperations:Operations);
        if(!Session.Apply(CommitRequest)){
            ActivityLog.LogError(nameof(CSharpGameEngineViewerPackage),"Scene hierarchy edit failed.");
        }
    }

    private static IReadOnlyList<NodePath> SelectHierarchyRoots(IReadOnlyList<NodePath>Paths){
        List<NodePath>Roots=[];
        foreach(NodePath Path in Paths.OrderBy(static Value=>Value.Depth)){
            if(Roots.Any(Root=>Path.Equals(Root)||Path.IsDescendantOf(Root))){
                continue;
            }

            Roots.Add(Path);
        }

        return Roots;
    }
    private void OnHierarchySelectionChanged(HierarchyNodeSelection Selection){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SourceMarkerService?.Update(
            SceneEditorHub.ActiveSceneModel,Selection.Paths,
            SceneSourceMarkerScrollBehavior.ScrollToSelection);
        if(this.SceneSession?.PinnedSnapshot is not { } Snapshot){
            return;
        }

        List<EditorNodeId>Ids=[];
        foreach(NodePath Path in Selection.Paths){
            if(Snapshot.Projection?.TryGetId(Path,out EditorNodeId Id)==true){
                Ids.Add(Id);
            }
        }

        EditorNodeId?Primary=Selection.PrimaryPath is { } PrimaryPath
            &&Snapshot.Projection?.TryGetId(PrimaryPath,out EditorNodeId PrimaryId)==true
            ?PrimaryId
            :null;
        this.SceneSession.Selection.Select(
            Snapshot.Key,Ids,Primary,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Hierarchy,Guid.NewGuid());
    }

    private void OnHierarchyNavigationRequested(NodePath Path){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SourceMarkerService?.Navigate(SceneEditorHub.ActiveSceneModel,Path);
    }

    /// <summary>
    /// M0048: <see cref="SceneEditorHub.SceneModelChanged"/>/<see cref="SceneEditorHub.ActiveSceneModel"/>は
    /// 元々<see cref="SceneWorkspaceService.ModelChanged"/>(実Roslyn再解析)だけで更新されていた。
    /// M0047でSceneDesignerが<see cref="SceneEditorSessionService.PinnedSnapshotChanged"/>
    /// (Documentの即時編集)を直接購読するよう切り替えた際、Hierarchy(と<c>OnSceneModelChangedFor
    /// SourceMarkers</c>)がこのHub経由のままだったため、編集のたびにDesignerのビューポートだけ
    /// 更新されHierarchyの表示が固まる実機回帰を起こした(M0048)。DocumentのPublishもHubへ
    /// 橋渡しし、Hierarchyがpin中Sceneの最新状態を常に見られるようにする。
    /// </summary>
    /// <summary>
    /// M0051 Step 1-4: 再解析(実C#由来)のモデルをHubへ流す前に選別する。
    ///
    /// <para>
    /// 以前は<c>SceneWorkspace.ModelChanged</c>を<see cref="SceneEditorHub.PublishSceneModel"/>へ
    /// 直結していたため、Hierarchyで編集した直後に保存等で再解析が走ると<b>未編集のC#由来モデル</b>で
    /// Hubが上書きされ、Hierarchyの木が古い内容から再構築されて編集が消えたように見えていた
    /// (実機報告の実体)。M0048でDocument→Hubの橋渡し
    /// (<see cref="OnPinnedSnapshotChangedForHub"/>)を足した際、この直結を残したことによる競合。
    /// </para>
    /// </summary>
    private void OnWorkspaceModelChangedForHub(SceneModel?Model){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneEditorSessionService?Session=this.SceneSession;
        bool HasUnsavedEdits=Session?.PinnedSceneKey is { }Key&&Session.IsDirty(Key);

        if(!SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            Model?.ClassFullyQualifiedName,
            Session?.PinnedSceneKey?.ClassFullyQualifiedName,
            HasUnsavedEdits)){
            return;
        }

        SceneEditorHub.PublishSceneModel(Model);
    }

    private void OnPinnedSnapshotChangedForHub(SceneDocumentSnapshot?Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Snapshot?.CurrentModel is { }Model){
            SceneEditorHub.PublishSceneModel(Model);
        }
    }

    /// <summary>
    /// M0040: 対象Sceneの再解析が着地するたびに発火する。以前はHierarchyパネル専用の
    /// <see cref="SceneEditorHub.HierarchySelection"/>を参照していたが、これはHierarchyパネルでの
    /// クリックでしか更新されない静的フィールドで、Viewport起点で選択・編集した場合は一切
    /// 更新されない。そのため編集を確定して再解析が着地するたびに、セッション中どこかで最後に
    /// Hierarchyパネルをクリックしたときの(今の選択とは無関係な)古い位置へ再スクロールして
    /// しまっていた(実機報告: 選択直後は正しい位置がマークされるが、編集確定後に無関係な行へ
    /// 飛ぶ)。統一選択サービス(<see cref="SceneEditorSessionService.Selection"/>)から今の選択を
    /// 都度解決することで、選択の起点(Hierarchy/Viewport/Inspector)によらず一貫させる。
    ///
    /// <para>
    /// M0043: この経路は<b>絶対にスクロールしない</b>(<see cref="SceneSourceMarkerScrollBehavior.PreserveScroll"/>)。
    /// ここはユーザーが選択を変えたのではなく、編集の書き戻し後にRoslynが再解析し終えただけなので、
    /// コードエディタの表示位置を動かす理由が無い。ギズモ編集のたびに無関係な位置へ飛んでいた
    /// 実機不具合の直接の修正点。
    /// </para>
    /// </summary>
    private void OnSceneModelChangedForSourceMarkers(SceneModel?Model){
        ThreadHelper.ThrowIfNotOnUIThread();
        IReadOnlyList<NodePath>Paths=this.SceneSession is { }Session&&Session.PinnedSnapshot is { }Snapshot
            ?SceneSelectionPathResolver.Resolve(Session.Selection.CurrentSelection,Snapshot.Key,Snapshot.Projection)
            :[];
        this.SourceMarkerService?.Update(Model,Paths,SceneSourceMarkerScrollBehavior.PreserveScroll);
    }

    /// <summary>
    /// M0020 Phase 2: ヒエラルキーは静的<see cref="SceneEditorHub.SelectedNodePath"/>を読む段階移行中
    /// (<see cref="HierarchyToolWindowControl"/>参照)のため、共通Selectionからの変更をここで橋渡しする。
    /// Origin=Hierarchy自身の変更はTreeViewの見た目と既に一致しているので素通りする(往復回避)。
    /// </summary>
    private void OnSelectionChangedForHierarchy(SceneSelection Selection){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Selection.Origin==SceneSelectionOrigin.Hierarchy){
            return;
        }

        if(this.SceneSession?.PinnedSnapshot is not { } Snapshot||!Snapshot.Key.Equals(Selection.Key)){
            this.SourceMarkerService?.Update(
                Model:null,Paths:[],SceneSourceMarkerScrollBehavior.PreserveScroll);
            return;
        }

        // Viewport/Inspector起点はHierarchyへprimaryだけを表示する。Hierarchy自身が発行する
        // 複数選択集合はOnHierarchySelectionChangedでそのままSceneSelectionServiceへ渡す。
        IReadOnlyList<NodePath>Paths=SceneSelectionPathResolver.Resolve(Selection,Snapshot.Key,Snapshot.Projection);
        NodePath?PrimaryPath=Selection.PrimaryNodeId is { }PrimaryId
            &&Snapshot.Projection?.TryGetNode(PrimaryId,out SceneProjectionNode PrimaryNode)==true
            ?PrimaryNode.Path
            :null;

        // M0032: Viewport(将来的にはInspectorも)起点の選択もHierarchyと同様にソースをマーク・
        // スクロールする。以前はHierarchy起点以外は明示的にマークを消していたが、実機フィードバック
        // で非対称と指摘され撤廃した。
        this.SourceMarkerService?.Update(
            SceneEditorHub.ActiveSceneModel,Paths,SceneSourceMarkerScrollBehavior.ScrollToSelection);
        SceneEditorHub.PublishSelectedNodePath(PrimaryPath);
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

            SceneEditorHub.HierarchySelectionChanged-=this.OnHierarchySelectionChanged;
            SceneEditorHub.HierarchyNavigationRequested-=this.OnHierarchyNavigationRequested;
            SceneEditorHub.HierarchyEditRequested-=this.OnHierarchyEditRequested;
            SceneEditorHub.SceneModelChanged-=this.OnSceneModelChangedForSourceMarkers;
            SourceMarkerService?.Dispose();
            SourceMarkerService=null;
            if(SceneSession is not null){
                SceneSession.Selection.SelectionChanged-=this.OnSelectionChangedForHierarchy;
                SceneSession.PinnedSnapshotChanged-=this.OnPinnedSnapshotChangedForHub;
            }

            SceneSession?.Dispose();
            SceneSession=null;
            SceneDesignerToolWindowPane.ConfigureSessionProvider(null);
            InspectorToolWindowPane.ConfigureSessionProvider(null);
            SceneEditorSettings.ConfigureStoreProvider(null);

            if(SceneWorkspace is not null){
                SceneWorkspace.ModelChanged-=this.OnWorkspaceModelChangedForHub;
                SceneWorkspace.Dispose();
            }
        }

        base.Dispose(Disposing);
    }
}
