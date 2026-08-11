using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Media;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process D3D12 embedding (same pattern as <see cref="AssetPreviewHwndHost"/>, ADR-0013) for the
/// "シーン" preview document window (ADR-0020, extension corrected to <c>*.scene</c> in ADR-0021):
/// loads a scene file via <see cref="SceneFile"/>, resolves each node's <c>MeshAssetPath</c> through
/// <see cref="MeshLoader"/> (out-of-process, ADR-0010/ADR-0019), and renders every mesh-bearing node
/// via <see cref="SceneRenderer"/>. Publishes the loaded scene to <see cref="SceneEditorHub"/> so the
/// always-visible Hierarchy tool window can display its node tree, subscribes to
/// <see cref="SceneEditorHub.SelectedNodeChanged"/> to highlight the corresponding object, and
/// subscribes to <see cref="SceneEditorHub.ReloadRequested"/> to re-read this same file after
/// <see cref="PrimitiveSpawner"/> (ADR-0021) appends a new node to it.
/// </summary>
internal sealed class SceneHwndHost:HwndHost {
    private string?RepoRoot;
    private string ScenePath;
    private RenderSurfaceWindow?Surface;
    private SceneRenderer?Renderer;
    private EventHandler?RenderingHandler;
    private Action<Guid?>?SelectionHandler;
    private Action?ReloadHandler;

    public SceneHwndHost(string ScenePath){
        this.ScenePath=ScenePath;
    }

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        Surface=new RenderSurfaceWindow(HwndParent.Handle,800,600);
        LoadIntoSurface();
        return new HandleRef(this,Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        TearDownRenderer();

        Surface?.Dispose();
        Surface=null;
    }

    /// <summary>Reloads the preview with a different scene file - used if VS reuses this pane's doc data for a renamed/reloaded document.</summary>
    public void SetScenePath(string ScenePath){
        this.ScenePath=ScenePath;
        if(Surface is not null){
            TearDownRenderer();
            LoadIntoSurface();
        }
    }

    private void LoadIntoSurface(){
        if(Surface is null){
            return;
        }

        try {
            this.RepoRoot??=RepoLocator.ResolveRepoRoot();
            if(this.RepoRoot is null){
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }
            var RepoRoot=this.RepoRoot;

            SceneFile Scene=SceneFile.Load(ScenePath);
            IReadOnlyDictionary<Guid,Matrix4x4>WorldTransforms=Scene.ComputeWorldTransforms();

            var SceneDirectory=Path.GetDirectoryName(ScenePath)
                ??throw new InvalidOperationException($"シーンファイルのディレクトリを解決できませんでした: {ScenePath}");

            List<(SceneFileNode Node,string AbsoluteMeshPath)>MeshNodes=Scene.Nodes
                .Where(Node=>Node.MeshAssetPath is not null)
                .Select(Node=>(Node,Path.GetFullPath(Path.Combine(SceneDirectory,Node.MeshAssetPath!))))
                .ToList();

            // Nodes with no mesh reference (every node authored via [SceneDefinition] C# code,
            // ADR-0031; also any hand-authored .scene node that simply has none) previously never
            // reached SceneRenderer at all - silently invisible, or a hard ArgumentException if the
            // whole scene had no mesh-bearing node. They now get a small placeholder gizmo cube
            // instead, so a scene is always representable regardless of what it references.
            List<SceneFileNode>GizmoNodes=[.. Scene.Nodes.Where(Node=>Node.MeshAssetPath is null)];

            // MeshLoader caches by path (process-lifetime), so warming distinct paths in parallel
            // here means a scene with several different meshes pays the bridge-process cost once,
            // concurrently, instead of once per node, serially - the dominant cost when opening the
            // シーン tab for the first time (see ADR-0010 addendum). BuildWindowCore is called from
            // deep inside WPF's own window-creation machinery, not a plain UI-thread event handler -
            // a Task-based wait here (even routed through ThreadHelper.JoinableTaskFactory.Run) risks
            // exactly the nested-JoinableTaskFactory deadlock class this project has hit more than
            // once before (see the "project-vsix-joinabletask-deadlock" memory / ADR-0021 追記), and
            // separately, JoinableTaskFactory was confirmed (ADR-0023) to misbehave outside a fully
            // running VS host. WarmMeshCache below uses raw Thread/Join instead - no Task, no
            // JoinableTaskFactory, nothing for either failure mode to attach to.
            var DistinctMeshPaths=MeshNodes
                .Select(N=>N.AbsoluteMeshPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            WarmMeshCache(DistinctMeshPaths,RepoRoot);

            List<SceneRenderObject>RenderObjects=[];
            foreach((SceneFileNode Node,var AbsoluteMeshPath)in MeshNodes){
                RenderObjects.Add(new SceneRenderObject {
                    Mesh=MeshLoader.Load(AbsoluteMeshPath,RepoRoot),
                    World=WorldTransforms[Node.Id],
                    NodeId=Node.Id,
                });
            }

            foreach(SceneFileNode node in GizmoNodes){
                RenderObjects.Add(new SceneRenderObject {
                    Mesh=GizmoMeshFactory.Create(),
                    World=WorldTransforms[node.Id],
                    NodeId=node.Id,
                });
            }

            Renderer=new SceneRenderer(Surface.Handle,800,600,RenderObjects);

            Surface.Resized+=(W,H)=>Renderer?.Resize(W,H);

            RenderingHandler=(_,_)=>Renderer?.RenderFrame();
            CompositionTarget.Rendering+=RenderingHandler;

            SelectionHandler=NodeId=>Renderer?.SetSelectedNode(NodeId);
            SceneEditorHub.SelectedNodeChanged+=SelectionHandler;

            // Fired after PrimitiveSpawner rewrites this same file on disk (ADR-0021) - reload from
            // the current path rather than the whole BuildWindowCore/surface-creation path.
            ReloadHandler=()=>SetScenePath(ScenePath);
            SceneEditorHub.ReloadRequested+=ReloadHandler;

            SceneEditorHub.PublishSceneOpened(Scene,ScenePath);
        } catch(Exception Ex){
            System.Windows.MessageBox.Show(
                $"シーンのプレビューに失敗しました: {Ex}",
                "C# Game Engine Scene",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Loads each of <paramref name="DistinctMeshPaths"/> via <see cref="MeshLoader"/> on its own
    /// background <see cref="Thread"/>, concurrently, then waits for all of them with a plain
    /// <see cref="Thread.Join()"/> - deliberately not <c>Task</c>/<c>async</c>/
    /// <c>JoinableTaskFactory</c> (see the call site's comment for why).
    /// </summary>
    private static void WarmMeshCache(IReadOnlyList<string>DistinctMeshPaths,string RepoRoot){
        if(DistinctMeshPaths.Count==0){
            return;
        }

        Thread[]Threads=new Thread[DistinctMeshPaths.Count];
        Exception?[]Errors=new Exception?[DistinctMeshPaths.Count];

        for(var i=0;i<DistinctMeshPaths.Count;i++){
            var index=i;
            Threads[index]=new Thread(()=>{
                try {
                    MeshLoader.Load(DistinctMeshPaths[index],RepoRoot);
                } catch(Exception Ex){
                    Errors[index]=Ex;
                }
            }){ IsBackground=true };
            Threads[index].Start();
        }

        foreach(Thread thread in Threads){
            thread.Join();
        }

        Exception?FirstError=Array.Find(Errors,Error=>Error is not null);
        if(FirstError is not null){
            ExceptionDispatchInfo.Capture(FirstError).Throw();
        }
    }

    private void TearDownRenderer(){
        if(RenderingHandler is not null){
            CompositionTarget.Rendering-=RenderingHandler;
            RenderingHandler=null;
        }

        if(SelectionHandler is not null){
            SceneEditorHub.SelectedNodeChanged-=SelectionHandler;
            SelectionHandler=null;
        }

        if(ReloadHandler is not null){
            SceneEditorHub.ReloadRequested-=ReloadHandler;
            ReloadHandler=null;
        }

        Renderer?.Dispose();
        Renderer=null;
    }
}
