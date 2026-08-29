using System;
using System.Collections.Generic;
using System.Globalization;
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
[ProvideToolWindow(typeof(InspectorToolWindowPane),
    Style=VsDockStyle.Linked,Window=ToolWindowGuids80.SolutionExplorer,
    Orientation=ToolWindowOrientation.Right,DockedWidth=300)]
// M0052: パレットはInspectorへ Tabbed で結合しない。ドラッグしている間に隠れてしまう
// ドロップ先は成立しないので、Linked で別枠として並べる。
[ProvideToolWindow(typeof(ComponentPaletteToolWindowPane),
    Style=VsDockStyle.Linked,Window=ToolWindowGuids80.SolutionExplorer,
    Orientation=ToolWindowOrientation.Bottom,DockedWidth=300)]
// Document Well is the primary Scene Designer host. This tool window remains registered only
// as a recovery path when Visual Studio cannot create the external virtual document frame.
[ProvideToolWindow(typeof(SceneDesignerToolWindowPane),
    Style=VsDockStyle.Linked,Window=ToolWindowGuids80.SolutionExplorer,
    Orientation=ToolWindowOrientation.Bottom,DockedWidth=500)]
[ProvideEditorFactory(typeof(GlbFbxPreviewEditorFactory),110,TrustLevel=__VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(GlbFbxPreviewEditorFactory),VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".glb",50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".gltf",50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".fbx",50)]
[ProvideEditorFactory(typeof(ShaderGraphEditorFactory),111,TrustLevel=__VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(ShaderGraphEditorFactory),VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(ShaderGraphEditorFactory),".cgeshadergraph",60)]
[ProvideEditorExtension(typeof(ShaderGraphEditorFactory),".cgesubgraph",60)]
// Tools>Options > C# Game Engine > Scene Editor。120/121 は VSPackage.resx のカテゴリ名/ページ名。
// GeneratePkgDefFile=true なので .pkgdef へ自動登録される(マニフェストの手編集は不要)。
[ProvideOptionPage(typeof(SceneEditorOptionsPage),"C# Game Engine","Scene Editor",120,121,
    supportsAutomation:true)]
public sealed partial class CSharpGameEngineViewerPackage:AsyncPackage {
    public const string PackageGuidString="aec17804-e2e4-46e7-b373-de11af816203";

    private ToggleFabWatchCommand?FabWatchCommand;
    private AssetMetaWatcher?AssetMetaWatcher;
    private SceneWorkspaceService?SceneWorkspace;
    private SceneEditorSessionService?SceneSession;
    private SceneDesignerDocumentManager?SceneDesignerDocuments;
    private ActiveDocumentTracker?DocumentTracker;
    private SceneSourceMarkerService?SourceMarkerService;
    private ExperimentalToolWindowState?ExperimentalToolWindowState;

    protected override async Task InitializeAsync(CancellationToken CancellationToken,IProgress<ServiceProgressData>Progress){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        VsixLocalization.Capture(CultureInfo.CurrentUICulture);
        ActivityLog.LogInformation(nameof(CSharpGameEngineViewerPackage),VsixBuildIdentity.ActivityLogText);

        await OpenViewerCommand.InitializeAsync(this);
        this.FabWatchCommand= await ToggleFabWatchCommand.InitializeAsync(this);
        await OpenFabCommand.InitializeAsync(this);
        await OpenGameCommand.InitializeAsync(this);
        await ShowHierarchyToolWindowCommand.InitializeAsync(this);
        await ApplyGameEngineLayoutCommand.InitializeAsync(this);
        await ShowInspectorToolWindowCommand.InitializeAsync(this);
        await ShowComponentPaletteToolWindowCommand.InitializeAsync(this);

        this.RegisterEditorFactory(new GlbFbxPreviewEditorFactory());
        this.RegisterEditorFactory(new ShaderGraphEditorFactory());

        // ADR-0040: the Scene editor reads scenes from live Roslyn. Both of these degrade to null
        // when the managed language service is unavailable, in which case the Hierarchy simply shows
        // its "open a scene file" message instead of failing package load.
        this.SceneWorkspace=SceneWorkspaceService.Initialize();
        SceneEditorSettings.ConfigureStoreProvider(
            ()=>new ShellSettingsManager(this).GetWritableSettingsStore(SettingsScope.UserSettings));
        SceneEditorSettings.MigrateNavigationSettingsIfNeeded();

        if(this.SceneWorkspace is not null){
            this.SceneSession=new SceneEditorSessionService(this.SceneWorkspace);
            this.SceneDesignerDocuments=new SceneDesignerDocumentManager(this);
            SceneDesignerToolWindowPane.ConfigureSessionProvider(()=>this.SceneSession);
            InspectorToolWindowPane.ConfigureSessionProvider(()=>this.SceneSession);
            ComponentPaletteToolWindowPane.ConfigureSessionProvider(()=>this.SceneSession);
            this.SourceMarkerService=SceneSourceMarkerService.Start();
            SceneEditorHub.HierarchySelectionChanged+=this.OnHierarchySelectionChanged;
            SceneEditorHub.HierarchyNavigationRequested+=this.OnHierarchyNavigationRequested;
            SceneEditorHub.HierarchyEditRequested+=this.OnHierarchyEditRequested;
            SceneEditorHub.ComponentAddRequested+=this.OnComponentAddRequested;
            SceneEditorHub.ComponentRemoveRequested+=this.OnComponentRemoveRequested;
            SceneEditorHub.DefinitionNavigationRequested+=this.OnDefinitionNavigationRequested;
            SceneEditorHub.SceneModelChanged+=this.OnSceneModelChangedForSourceMarkers;
            this.SceneSession.Selection.SelectionChanged+=this.OnSelectionChangedForHierarchy;
            this.SceneSession.PinnedSnapshotChanged+=this.OnPinnedSnapshotChangedForHub;
            this.SceneWorkspace.ModelChanged+=this.OnWorkspaceModelChangedForHub;
            this.DocumentTracker=ActiveDocumentTracker.Start(this.SceneWorkspace);
            if(this.DocumentTracker is not null){
                await OpenSceneDesignerCommand.InitializeAsync(this,this.SceneSession,this.DocumentTracker,this.SceneDesignerDocuments);
            }
        }

        this.AssetMetaWatcher=AssetMetaWatcher.StartIfProjectExists();
        this.ExperimentalToolWindowState=new ExperimentalToolWindowState(this);
        _=this.JoinableTaskFactory.RunAsync(async()=>{
            if(await this.WaitForSolutionOpenAsync()){
                await this.ExperimentalToolWindowState.RestoreAsync(this.DisposalToken);
            }
        });
        // Do not create this package's tool windows while AsyncPackage.InitializeAsync is still
        // running. FindToolWindowAsync can then wait on this package's own load operation and
        // prevent Visual Studio from completing startup. The existing Apply Game Engine Layout
        // command remains the explicit, safe way to create the default layout.
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
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(this.SceneSession is not { } Session||SceneEditorHub.ActiveSceneModel is not { } Model){
            return;
        }

        var Snapshot=this.EnsurePinned(Session,Model);
        if(Snapshot is null||Snapshot.Projection is not { } Projection){
            return;
        }

        var Roots=SelectHierarchyRoots(Request.Paths);
        List<SceneEditOperation>Operations=[];
        switch(Request.Kind){
            case HierarchyEditKind.AddChild:
                foreach(var Path in Roots){
                    if(Projection.TryGetId(Path,out var ParentId)){
                        Operations.Add(new AddNodeOperation(
                            Snapshot.Key,ParentId,Snapshot.DocumentVersion,"Node"));
                    }
                }
                break;
            case HierarchyEditKind.NormalizeComponents:
                foreach(var Path in Roots){
                    if(Projection.TryGetId(Path,out var NormalizeId)){
                        Operations.Add(new NormalizeComponentsOperation(
                            Snapshot.Key,NormalizeId,Snapshot.DocumentVersion));
                    }
                }
                break;
            case HierarchyEditKind.Rename:
                var RequestedName=Request.NewName;
                if(Request.Paths.FirstOrDefault() is { } RenamePath
                    &&Projection.TryGetId(RenamePath,out var RenameId)
                    &&!string.IsNullOrWhiteSpace(RequestedName)){
                    Operations.Add(new RenameNodeOperation(
                        Snapshot.Key,RenameId,Snapshot.DocumentVersion,RequestedName!.Trim()));
                }
                break;
            case HierarchyEditKind.Remove:
                foreach(var Path in Roots){
                    if(Projection.TryGetId(Path,out var RemoveId)){
                        Operations.Add(new RemoveNodeOperation(
                            Snapshot.Key,RemoveId,Snapshot.DocumentVersion));
                    }
                }
                break;
            case HierarchyEditKind.Reparent:
                if(Request.TargetPath is not { } TargetPath
                    ||!Projection.TryGetId(TargetPath,out var TargetId)
                    ||Roots.Any(Path=>Path.Equals(TargetPath)||TargetPath.IsDescendantOf(Path))){
                    return;
                }

                var ReparentIndex=Math.Max(0,Request.TargetIndex);
                foreach(var Path in Roots.OrderBy(static Path=>Path.ToDisplayString(),StringComparer.Ordinal)){
                    if(Projection.TryGetId(Path,out var MoveId)){
                        Operations.Add(new ReparentNodeOperation(
                            Snapshot.Key,MoveId,Snapshot.DocumentVersion,TargetId,ReparentIndex++));
                    }
                }
                break;
            case HierarchyEditKind.Reorder:
                if(Request.Paths.FirstOrDefault() is { } ReorderPath
                    &&Projection.TryGetId(ReorderPath,out var ReorderId)){
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
                HierarchyEditKind.NormalizeComponents=>"Normalize components",
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

    private void OnComponentAddRequested(ComponentAddRequest Request)=>
        _=this.JoinableTaskFactory.RunAsync(()=>this.HandleComponentAddAsync(Request));

    private void OnDefinitionNavigationRequested(DefinitionNavigationRequest Request)=>
        _=this.JoinableTaskFactory.RunAsync(()=>this.HandleDefinitionNavigationAsync(Request));

    private async Task HandleDefinitionNavigationAsync(DefinitionNavigationRequest Request){
        if(this.SceneSession is not { } Session){
            return;
        }

        var Resolved=await Session.ResolveComponentTypeAsync(
            Request.ComponentTypeFullyQualifiedName,this.DisposalToken);
        if(Resolved is not { } Result){
            ActivityLog.LogWarning(nameof(CSharpGameEngineViewerPackage),
                "Definition type was not resolved: "+Request.ComponentTypeFullyQualifiedName);
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            SetStatusBarText(VsixText.Format("StatusDefinitionTypeResolveFailed",Request.ComponentTypeFullyQualifiedName));
            return;
        }

        DefinitionNavigationTarget? Target=await DefinitionNavigationService.ResolveAsync(
            Result.Symbol,Result.Compilation,Request.MemberName,this.DisposalToken);
        if(Target is null){
            ActivityLog.LogWarning(nameof(CSharpGameEngineViewerPackage),
                "Definition source was not found: "+Request.ComponentTypeFullyQualifiedName
                +(Request.MemberName is { Length:>0 }Member?"."+Member:string.Empty));
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            SetStatusBarText(VsixText.Format("StatusDefinitionSourceOpenFailed",Request.ComponentTypeFullyQualifiedName));
            return;
        }

        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        bool Navigated=this.SourceMarkerService?.NavigateAndMark(Target)==true;
        if(!Navigated){
            ActivityLog.LogWarning(nameof(CSharpGameEngineViewerPackage),
                "Definition source was not found: "+Request.ComponentTypeFullyQualifiedName
                +(Request.MemberName is { Length:>0 }Member?"."+Member:string.Empty));
            SetStatusBarText(VsixText.Format("StatusDefinitionSourceOpenFailed",Request.ComponentTypeFullyQualifiedName));
        }
    }

    /// <summary>
    /// M0052: <paramref name="Model"/>が既にpin済みならそのSnapshotを、そうでなければ
    /// <see cref="SceneEditorSessionService.Pin"/>でpinしてから返す。
    ///
    /// <para>
    /// <b>解決する不具合</b>: 以前はpinされていない間、この判定の代わりに
    /// <see cref="SceneEditorSessionService.GetSnapshotForModel"/>で読み取り専用のSnapshotを作って
    /// いたが、それは<see cref="SceneEditorSessionService.Apply"/>が要求する
    /// <c>SceneEditDocument</c>を作らない。そのため<b>Hierarchyの構造編集・Componentの追加は
    /// シーン デザイナーを一度も開いていないと無言で失敗し続けていた</b>
    /// (<see cref="SceneEditorSessionService.Apply"/>は<c>Documents</c>辞書に無いキーへは何もしない)。
    /// Inspectorも同じ理由で選択が届かず、Hierarchyでは選べるのにInspectorだけ永久に空という
    /// 体験になっていた(実機報告)。
    /// </para>
    ///
    /// <para>
    /// pinは<see cref="SceneEditDocument"/>を(無ければ)新規作成するだけで、Viewport/シーン
    /// デザイナーのウィンドウを開いたり、Undo履歴やdirty状態を汚したりはしない
    /// (<see cref="SceneEditorSessionService.Pin"/>のEnsureDocumentAndPublish参照)。
    /// 既にそのSceneへpin済みの場合は再pinしない — 呼び出し頻度の高いHierarchy選択のたびに
    /// 無駄な再publishを起こさないため。
    /// </para>
    /// </summary>
    private SceneDocumentSnapshot? EnsurePinned(SceneEditorSessionService Session,SceneModel Model){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!Session.TryCreateSceneKey(Model,out var Key)){
            return null;
        }

        return Session.PinnedSceneKey is { } Pinned&&Pinned.Equals(Key)
            ?Session.PinnedSnapshot
            :Session.Pin(Model);
    }

    /// <summary>
    /// M0052: Component追加の唯一の適用点。Hierarchy(<see cref="NodePath"/>)・Inspector・パレット
    /// (<see cref="EditorNodeId"/>)で対象の指し方が違うのをここで吸収し、Undoラベルと
    /// 追加後のReveal発行も1か所に閉じ込める。
    /// </summary>
    private async Task HandleComponentAddAsync(ComponentAddRequest Request){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(this.SceneSession is not { } Session||SceneEditorHub.ActiveSceneModel is not { } Model){
            return;
        }

        var Snapshot=this.EnsurePinned(Session,Model);
        if(Snapshot is null||Snapshot.Projection is not { } Projection){
            return;
        }

        if(Request.CreateRootChild){
            await this.TryCreateRootChildForComponentAsync(Request,Session,Snapshot);
            return;
        }
        EditorNodeId?NodeId=Request.TargetNodeId;
        if(NodeId is null&&Request.TargetPath is { } Path&&Projection.TryGetId(Path,out var ResolvedId)){
            NodeId=ResolvedId;
        }

        NodeId??=Session.Selection.CurrentSelection?.PrimaryNodeId;
        if(NodeId is not { } TargetId){
            SetStatusBarText(VsixText.Get("StatusComponentTargetRequired"));
            return;
        }

        // 型シンボルを引いて、追加するComponentのメンバーを実体で組み立てる。これをやらないと
        // メモリ内Componentのメンバーが空になり、Roslynの再解析が着地するまでInspectorが
        // 「解析待ち…」のまま1項目も編集できない(実機報告)。解決できなければ従来どおり
        // パレットが持っている式だけで縮退する。
        var Resolved=await Session.ResolveComponentTypeAsync(
            Request.ComponentTypeFullyQualifiedName,this.DisposalToken);
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        var Plan=Resolved is { } Symbol
            ?SceneComponentInitializerPolicy.CreatePlan(
                Symbol.Symbol,Request.ComponentTypeFullyQualifiedName,Symbol.Compilation)
            :null;

        AddComponentOperation Operation=new(
            Snapshot.Key,TargetId,Snapshot.DocumentVersion,
            Request.ComponentTypeFullyQualifiedName,
            Plan?.Expression??Request.ComponentExpression,
            Plan?.Members??default,
            Resolved is { } ResolvedSymbol?SceneComponentOrdering.GetSortKey(
                ResolvedSymbol.Symbol,Request.ComponentTypeFullyQualifiedName):null);
        SceneEditCommitRequest CommitRequest=new(
            Snapshot.Key,Snapshot.DocumentVersion,VsixText.Get("UndoAddComponent"),
            Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,
            DirectOperations:[Operation]);
        if(Session.Apply(CommitRequest)){
            SceneEditorHub.RequestComponentReveal(
                new ComponentRevealRequest(TargetId,Request.ComponentTypeFullyQualifiedName));
            return;
        }

        // TryApplyAddComponentが拒否する主因は「同じ型を既に持っている」と
        // 「Components=[…]が書かれていないノード」。どちらもユーザーの操作で起きうるので、
        // ログではなくステータスバーで返す。
        SetStatusBarText(VsixText.Format(
            "StatusComponentAddFailed",Request.ComponentTypeFullyQualifiedName));
    }

    private void OnComponentRemoveRequested(ComponentRemoveRequest Request)=>
        _=this.JoinableTaskFactory.RunAsync(()=>this.HandleComponentRemoveAsync(Request));

    /// <summary>
    /// Inspector Expanderヘッダの削除(×)ボタンの唯一の適用点。<see cref="HandleComponentAddAsync"/>と
    /// 同じ理由でEnsurePinnedを必ず経由する。
    /// </summary>
    private async Task HandleComponentRemoveAsync(ComponentRemoveRequest Request){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(this.SceneSession is not { } Session||SceneEditorHub.ActiveSceneModel is not { } Model){
            return;
        }

        var Snapshot=this.EnsurePinned(Session,Model);
        if(Snapshot is null){
            return;
        }

        RemoveComponentOperation Operation=new(
            Snapshot.Key,Request.NodeId,Snapshot.DocumentVersion,Request.ComponentTypeFullyQualifiedName);
        SceneEditCommitRequest CommitRequest=new(
            Snapshot.Key,Snapshot.DocumentVersion,VsixText.Get("UndoRemoveComponent"),
            Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,
            DirectOperations:[Operation]);
        if(!Session.Apply(CommitRequest)){
            SetStatusBarText(VsixText.Format(
                "StatusComponentRemoveFailed",Request.ComponentTypeFullyQualifiedName));
        }
    }

    private static void SetStatusBarText(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }

    private static IReadOnlyList<NodePath> SelectHierarchyRoots(IReadOnlyList<NodePath>Paths){
        List<NodePath>Roots=[];
        foreach(var Path in Paths.OrderBy(static Value=>Value.Depth)){
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
        // M0052: HierarchyはPinに依存せずActiveSceneModelだけで表示できるため、シーン デザイナーを
        // 一度も開かずにノードを選ぶ経路が普通に起きる。従来はここで素通りしていたため、
        // Hierarchyでは選べるのにInspectorだけ永久に空という体験になっていた
        // (実機報告で発覚。Hierarchyの構造編集も同じ理由で無言失敗していた — EnsurePinned参照)。
        if(this.SceneSession is not { } Session||SceneEditorHub.ActiveSceneModel is not { } Model
            ||this.EnsurePinned(Session,Model) is not { } Snapshot){
            return;
        }

        List<EditorNodeId>Ids=[];
        foreach(var Path in Selection.Paths){
            if(Snapshot.Projection?.TryGetId(Path,out var Id)==true){
                Ids.Add(Id);
            }
        }

        EditorNodeId?Primary=Selection.PrimaryPath is { } PrimaryPath
            &&Snapshot.Projection?.TryGetId(PrimaryPath,out var PrimaryId)==true
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
        var Session=this.SceneSession;
        var HasUnsavedEdits=Session?.PinnedSceneKey is { }Key&&Session.IsDirty(Key);

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
            return;
        }

        // M0053: pinが外れた(ClearPin)。SceneHubPublicationPolicyはpin中の表示を守るため
        // アクティブ文書由来のnull/別Sceneを塞いでいるので、ここで発行しないとHierarchyが
        // 解除後も古い木を出し続ける。未pin時の表示元であるアクティブ文書のSceneへ明示的に戻す
        // (対象が無ければnullで、そのまま空表示になる)。
        SceneEditorHub.PublishSceneModel(this.SceneWorkspace?.CurrentModel);
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
        var Paths=this.SceneSession is { }Session&&Session.PinnedSnapshot is { }Snapshot
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
        var Paths=SceneSelectionPathResolver.Resolve(Selection,Snapshot.Key,Snapshot.Projection);
        NodePath?PrimaryPath=Selection.PrimaryNodeId is { }PrimaryId
            &&Snapshot.Projection?.TryGetNode(PrimaryId,out var PrimaryNode)==true
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

            // Do not create or show tool windows until a solution is actually open; the presentation itself uses
            // ShowNoActivate, so this wait is not a keyboard-focus workaround.
            if(!await this.WaitForSolutionOpenAsync()){
                return;
            }

            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            if(await this.GetServiceAsync(typeof(SDTE))is not DTE Dte){
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
        for(var Attempt=0;Attempt<80;Attempt++){
            if(await this.IsSolutionOpenAsync()){
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), this.DisposalToken);
        }

        return false;
    }

    private async Task<bool> IsSolutionOpenAsync(){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(await this.GetServiceAsync(typeof(SVsSolution))is not IVsSolution Solution){
            return false;
        }

        Solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen,out var IsOpen);
        return IsOpen is bool Value&&Value;
    }

    /// <summary>
    /// 開いている(または開きかけの)ソリューションファイルのパス。無ければnull。
    /// <see cref="__VSPROPID.VSPROPID_IsSolutionOpen"/>が立つ前でもパスは判ることがあるため、
    /// 自動オープンの判定では<see cref="IsSolutionOpenAsync"/>と併用する
    /// (<see cref="StartupAutoOpenPolicy.ShouldOpenUserEcsProject"/>参照)。
    /// </summary>
    private async Task<string?>GetSolutionPathAsync(){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(await this.GetServiceAsync(typeof(SVsSolution))is not IVsSolution Solution){
            return null;
        }

        return ErrorHandler.Succeeded(Solution.GetSolutionInfo(out _,out var SolutionFile,out _))
            ?SolutionFile
            :null;
    }

    protected override void Dispose(bool Disposing){
        if(Disposing){
            this.ExperimentalToolWindowState?.Capture();
            this.ExperimentalToolWindowState=null;
            this.FabWatchCommand?.Dispose();
            this.AssetMetaWatcher?.Dispose();
            this.DocumentTracker?.Dispose();

            SceneEditorHub.HierarchySelectionChanged-=this.OnHierarchySelectionChanged;
            SceneEditorHub.HierarchyNavigationRequested-=this.OnHierarchyNavigationRequested;
            SceneEditorHub.HierarchyEditRequested-=this.OnHierarchyEditRequested;
            SceneEditorHub.ComponentAddRequested-=this.OnComponentAddRequested;
            SceneEditorHub.ComponentRemoveRequested-=this.OnComponentRemoveRequested;
            SceneEditorHub.DefinitionNavigationRequested-=this.OnDefinitionNavigationRequested;
            SceneEditorHub.SceneModelChanged-=this.OnSceneModelChangedForSourceMarkers;
            this.SourceMarkerService?.Dispose();
            this.SourceMarkerService=null;
            if(this.SceneSession is not null){
                this.SceneSession.Selection.SelectionChanged-=this.OnSelectionChangedForHierarchy;
                this.SceneSession.PinnedSnapshotChanged-=this.OnPinnedSnapshotChangedForHub;
            }

            this.SceneSession?.Dispose();
            this.SceneSession=null;
            this.SceneDesignerDocuments?.Dispose();
            this.SceneDesignerDocuments=null;
            SceneDesignerToolWindowPane.ConfigureSessionProvider(null);
            InspectorToolWindowPane.ConfigureSessionProvider(null);
            ComponentPaletteToolWindowPane.ConfigureSessionProvider(null);
            SceneEditorSettings.ConfigureStoreProvider(null);

            if(this.SceneWorkspace is not null){
                this.SceneWorkspace.ModelChanged-=this.OnWorkspaceModelChangedForHub;
                this.SceneWorkspace.Dispose();
            }
        }

        base.Dispose(Disposing);
    }
}
