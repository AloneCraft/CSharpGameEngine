using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process D3D12 embedding (same pattern as <see cref="AssetPreviewHwndHost"/>, ADR-0013) for the
/// Scene Designer tool window (ADR-0040 Phase 9). Targets a specific <c>IScene</c>-implementing
/// class by <see cref="SetTarget"/>, resolves it out of
/// <see cref="SceneEditorHub.ActiveAllSceneModels"/> (published by <see cref="SceneWorkspaceService"/>
/// - live Roslyn, no build step), converts it via <see cref="SceneModelRenderObjectBuilder"/> and
/// renders every node via <see cref="SceneRenderer"/>. Replaces the earlier <c>.scene</c>-JSON-based
/// version of this class (ADR-0020/ADR-0021) - re-analyzed in place rather than duplicated, since the
/// old data source goes away entirely in this same phase (see ADR-0040 Phase 9's "known limitations":
/// drag-and-drop placement, which still writes to <c>.scene</c>, is temporarily non-functional until
/// Phase 10 moves it onto Roslyn-based source edits too).
/// </summary>
internal sealed class SceneHwndHost:HwndHost {
    private string?RepoRoot;
    private string?TargetFilePath;
    private string?TargetClassName;
    private SceneModel?TargetModel;
    private RenderSurfaceWindow?Surface;
    private SceneRenderer?Renderer;
    private LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>?BuildWorker;
    private EventHandler?RenderingHandler;
    private Action<IReadOnlyList<SceneModel>>?ModelsHandler;
    private Action<NodePath?>?SelectionHandler;
    private Action<int,int>?ResizeHandler;

    internal event Action<string>?StatusChanged;

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        // Rendererが最初のPresentを行えるまで子HWNDを隠す。NOREDIRECTIONBITMAPの空Surfaceを
        // 表示すると、ドッキング前に同じ領域にいたSolution Explorerが残像のように露出する。
        Surface=new RenderSurfaceWindow(HwndParent.Handle,800,600,InitiallyVisible:false);
        SynchronizationContext UiContext=SynchronizationContext.Current
            ??new DispatcherSynchronizationContext(this.Dispatcher);
#pragma warning disable VSTHRD001 // 生workerの完了を非同期Postするだけで、同期wait/message pumpは行わない。
        BuildWorker=new LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>(
            Work:BuildRenderObjects,
            Dispatch:Callback=>UiContext.Post(_=>Callback(),null),
            Completed:this.OnBuildCompleted);
#pragma warning restore VSTHRD001
        LoadIntoSurface();
        return new HandleRef(this,Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        BuildWorker?.Dispose();
        BuildWorker=null;
        DetachSubscriptions();
        DisposeRenderer();

        Surface?.Dispose();
        Surface=null;
    }

    /// <summary>Points this host at a specific class in a specific file - called once by <c>OpenSceneDesignerCommand</c> when the tool window is shown/retargeted.</summary>
    internal void SetTarget(SceneModel Model){
        this.TargetFilePath=Model.FilePath;
        this.TargetClassName=Model.ClassName;
        this.TargetModel=Model;

        if(Surface is not null){
            DisposeRenderer();
            Rebuild();
        }
    }

    private void LoadIntoSurface(){
        if(Surface is null){
            return;
        }

        if(ModelsHandler is null){
            ModelsHandler=Models=>{
                SceneModel?Updated=Models.FirstOrDefault(
                    M=>string.Equals(M.FilePath,TargetFilePath,StringComparison.OrdinalIgnoreCase)
                        &&string.Equals(M.ClassName,TargetClassName,StringComparison.Ordinal));
                if(Updated is not null){
                    TargetModel=Updated;
                    Rebuild();
                }
            };
            SceneEditorHub.AllSceneModelsChanged+=ModelsHandler;
        }

        if(SelectionHandler is null){
            SelectionHandler=Path=>Renderer?.SetSelectedNode(Path);
            SceneEditorHub.SelectedNodePathChanged+=SelectionHandler;
        }

        if(RenderingHandler is null){
            RenderingHandler=(_,_)=>Renderer?.RenderFrame();
            CompositionTarget.Rendering+=RenderingHandler;

            // Subscribed once here (not inside Rebuild, which runs on every re-analysis) - it always
            // resizes whatever Renderer currently holds via the field, so re-subscribing per rebuild
            // would just accumulate redundant duplicate calls per resize.
            ResizeHandler=(W,H)=>Renderer?.Resize(W,H);
            Surface.Resized+=ResizeHandler;
        }

        Rebuild();
    }

    /// <summary>(Re)builds <see cref="Renderer"/> from the current <see cref="SceneEditorHub.ActiveAllSceneModels"/> - called on first load and every live re-analysis.</summary>
    private void Rebuild(){
        if(Surface is null||TargetFilePath is null||TargetClassName is null){
            return;
        }

        if(TargetModel is not { } Model){
            // Not (yet) found - e.g. analysis hasn't caught up with a just-opened tool window, or
            // the class was renamed/deleted out from under us. Leave the last good frame on screen.
            SetStatus($"解析結果を待っています: {TargetClassName}");
            return;
        }

        if(Model.Root is null){
            DisposeRenderer();
            Microsoft.CodeAnalysis.Diagnostic?Diagnostic=Model.Diagnostics.FirstOrDefault();
            string Detail=Diagnostic is null
                ?"Rootを解析できませんでした。"
                :$"{Diagnostic.Id}: {Diagnostic.GetMessage()}";
            ActivityLog.LogError(nameof(SceneHwndHost),$"{TargetClassName}: {Detail}");
            SetStatus($"シーン解析エラー: {Detail}");
            return;
        }

        SetStatus($"読み込み中: {TargetClassName}");

        // 旧 .scene 実装の WarmMeshCache は、WPF の BuildWindowCore 内でJTF/Taskを同期wait
        // するデッドロックを避けるため、生ThreadへI/Oを隔離してからUI側でJoinしていた。
        // Scene DSL版ではさらに一歩進め、Primitive.Cli生成と後続のGLTF MeshLoaderを含む
        // Build全体をこの単一workerへ渡し、UIスレッドではJoin/Waitを一切行わない。完了後の
        // SceneRenderer生成だけをDispatcherへ戻す。live解析が連続してもworkerはlatest-winsで
        // 未処理要求を置換するため、ブリッジプロセスが無制限に増えることもない。
        BuildWorker?.Request(new SceneRenderBuildRequest(
            Model,
            TargetFilePath,
            TargetClassName,
            Surface.Handle));
    }

    private SceneRenderBuildResult BuildRenderObjects(SceneRenderBuildRequest Request){
        this.RepoRoot??=RepoLocator.ResolveRepoRoot();
        if(this.RepoRoot is null){
            throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
        }

        string RepoRoot=this.RepoRoot;
        IReadOnlyList<SceneRenderObject>RenderObjects=SceneModelRenderObjectBuilder.Build(
            Request.Model,
            EnumMemberName=>SceneModelRenderObjectBuilder.ResolveViaBridge(EnumMemberName,RepoRoot));
        return new SceneRenderBuildResult(Request,RenderObjects);
    }

    private void OnBuildCompleted(SceneRenderBuildRequest Request,SceneRenderBuildResult?Result,Exception?Error){
        if(Surface is null
            ||Surface.Handle!=Request.SurfaceHandle
            ||!string.Equals(TargetFilePath,Request.FilePath,StringComparison.OrdinalIgnoreCase)
            ||!string.Equals(TargetClassName,Request.ClassName,StringComparison.Ordinal)){
            return;
        }

        if(Error is not null){
            ActivityLog.LogError(nameof(SceneHwndHost),Error.ToString());
            if(Renderer is null){
                Surface.SetVisible(false);
            }
            SetStatus($"表示に失敗しました: {Error.Message}");
            return;
        }

        if(Result is null){
            return;
        }

        if(Result.RenderObjects.Count==0){
            DisposeRenderer();
            SetStatus($"描画可能なノードがありません: {Result.Request.ClassName}");
            return;
        }

        try {
            DisposeRenderer();
            Renderer=new SceneRenderer(Surface.Handle,800,600,Result.RenderObjects);
            Renderer.SetSelectedNode(SceneEditorHub.SelectedNodePath);
            Surface.SetVisible(true);
            SetStatus($"表示中: {Result.Request.ClassName}");
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneHwndHost),Ex.ToString());
            DisposeRenderer();
            SetStatus($"表示に失敗しました: {Ex.Message}");
        }
    }

    private void DisposeRenderer(){
        Renderer?.Dispose();
        Renderer=null;
        Surface?.SetVisible(false);
    }

    private void DetachSubscriptions(){
        if(RenderingHandler is not null){
            CompositionTarget.Rendering-=RenderingHandler;
            RenderingHandler=null;
        }

        if(ModelsHandler is not null){
            SceneEditorHub.AllSceneModelsChanged-=ModelsHandler;
            ModelsHandler=null;
        }

        if(SelectionHandler is not null){
            SceneEditorHub.SelectedNodePathChanged-=SelectionHandler;
            SelectionHandler=null;
        }

        if(ResizeHandler is not null&&Surface is not null){
            Surface.Resized-=ResizeHandler;
            ResizeHandler=null;
        }
    }

    private void SetStatus(string Status)=>StatusChanged?.Invoke(Status);

    private sealed record SceneRenderBuildRequest(
        SceneModel Model,
        string FilePath,
        string ClassName,
        IntPtr SurfaceHandle);

    private sealed record SceneRenderBuildResult(
        SceneRenderBuildRequest Request,
        IReadOnlyList<SceneRenderObject>RenderObjects);
}
