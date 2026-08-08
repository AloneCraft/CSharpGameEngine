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
internal sealed class SceneHwndHost : HwndHost
{
    private string? _repoRoot;
    private string _scenePath;
    private RenderSurfaceWindow? _surface;
    private SceneRenderer? _renderer;
    private EventHandler? _renderingHandler;
    private Action<Guid?>? _selectionHandler;
    private Action? _reloadHandler;

    public SceneHwndHost(string scenePath)
    {
        _scenePath = scenePath;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _surface = new RenderSurfaceWindow(hwndParent.Handle, 800, 600);
        LoadIntoSurface();
        return new HandleRef(this, _surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        TearDownRenderer();

        _surface?.Dispose();
        _surface = null;
    }

    /// <summary>Reloads the preview with a different scene file - used if VS reuses this pane's doc data for a renamed/reloaded document.</summary>
    public void SetScenePath(string scenePath)
    {
        _scenePath = scenePath;
        if (_surface is not null)
        {
            TearDownRenderer();
            LoadIntoSurface();
        }
    }

    private void LoadIntoSurface()
    {
        if (_surface is null)
        {
            return;
        }

        try
        {
            _repoRoot ??= RepoLocator.ResolveRepoRoot();
            if (_repoRoot is null)
            {
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }
            string repoRoot = _repoRoot;

            SceneFile scene = SceneFile.Load(_scenePath);
            IReadOnlyDictionary<Guid, Matrix4x4> worldTransforms = scene.ComputeWorldTransforms();

            string sceneDirectory = Path.GetDirectoryName(_scenePath)
                ?? throw new InvalidOperationException($"シーンファイルのディレクトリを解決できませんでした: {_scenePath}");

            List<(SceneFileNode Node, string AbsoluteMeshPath)> meshNodes = scene.Nodes
                .Where(node => node.MeshAssetPath is not null)
                .Select(node => (node, Path.GetFullPath(Path.Combine(sceneDirectory, node.MeshAssetPath!))))
                .ToList();

            // Nodes with no mesh reference (every node authored via [SceneDefinition] C# code,
            // ADR-0031; also any hand-authored .scene node that simply has none) previously never
            // reached SceneRenderer at all - silently invisible, or a hard ArgumentException if the
            // whole scene had no mesh-bearing node. They now get a small placeholder gizmo cube
            // instead, so a scene is always representable regardless of what it references.
            List<SceneFileNode> gizmoNodes = [.. scene.Nodes.Where(node => node.MeshAssetPath is null)];

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
            string[] distinctMeshPaths = meshNodes
                .Select(n => n.AbsoluteMeshPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            WarmMeshCache(distinctMeshPaths, repoRoot);

            List<SceneRenderObject> renderObjects = [];
            foreach ((SceneFileNode node, string absoluteMeshPath) in meshNodes)
            {
                renderObjects.Add(new SceneRenderObject
                {
                    Mesh = MeshLoader.Load(absoluteMeshPath, repoRoot),
                    World = worldTransforms[node.Id],
                    NodeId = node.Id,
                });
            }

            foreach (SceneFileNode node in gizmoNodes)
            {
                renderObjects.Add(new SceneRenderObject
                {
                    Mesh = GizmoMeshFactory.Create(),
                    World = worldTransforms[node.Id],
                    NodeId = node.Id,
                });
            }

            _renderer = new SceneRenderer(_surface.Handle, 800, 600, renderObjects);

            _surface.Resized += (w, h) => _renderer?.Resize(w, h);

            _renderingHandler = (_, _) => _renderer?.RenderFrame();
            CompositionTarget.Rendering += _renderingHandler;

            _selectionHandler = nodeId => _renderer?.SetSelectedNode(nodeId);
            SceneEditorHub.SelectedNodeChanged += _selectionHandler;

            // Fired after PrimitiveSpawner rewrites this same file on disk (ADR-0021) - reload from
            // the current path rather than the whole BuildWindowCore/surface-creation path.
            _reloadHandler = () => SetScenePath(_scenePath);
            SceneEditorHub.ReloadRequested += _reloadHandler;

            SceneEditorHub.PublishSceneOpened(scene, _scenePath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"シーンのプレビューに失敗しました: {ex}",
                "C# Game Engine Scene",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Loads each of <paramref name="distinctMeshPaths"/> via <see cref="MeshLoader"/> on its own
    /// background <see cref="Thread"/>, concurrently, then waits for all of them with a plain
    /// <see cref="Thread.Join()"/> - deliberately not <c>Task</c>/<c>async</c>/
    /// <c>JoinableTaskFactory</c> (see the call site's comment for why).
    /// </summary>
    private static void WarmMeshCache(IReadOnlyList<string> distinctMeshPaths, string repoRoot)
    {
        if (distinctMeshPaths.Count == 0)
        {
            return;
        }

        Thread[] threads = new Thread[distinctMeshPaths.Count];
        Exception?[] errors = new Exception?[distinctMeshPaths.Count];

        for (int i = 0; i < distinctMeshPaths.Count; i++)
        {
            int index = i;
            threads[index] = new Thread(() =>
            {
                try
                {
                    MeshLoader.Load(distinctMeshPaths[index], repoRoot);
                }
                catch (Exception ex)
                {
                    errors[index] = ex;
                }
            })
            { IsBackground = true };
            threads[index].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Exception? firstError = Array.Find(errors, error => error is not null);
        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    private void TearDownRenderer()
    {
        if (_renderingHandler is not null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }

        if (_selectionHandler is not null)
        {
            SceneEditorHub.SelectedNodeChanged -= _selectionHandler;
            _selectionHandler = null;
        }

        if (_reloadHandler is not null)
        {
            SceneEditorHub.ReloadRequested -= _reloadHandler;
            _reloadHandler = null;
        }

        _renderer?.Dispose();
        _renderer = null;
    }
}
