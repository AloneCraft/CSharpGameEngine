using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process D3D12 embedding (same pattern as <see cref="AssetPreviewHwndHost"/>, ADR-0013) for the
/// "シーン" preview document window (ADR-0020): loads a <c>*.scene.json</c> file via
/// <see cref="SceneFile"/>, resolves each node's <c>MeshAssetPath</c> through <see cref="MeshLoader"/>
/// (out-of-process, ADR-0010/ADR-0019), and renders every mesh-bearing node via
/// <see cref="SceneRenderer"/>. Publishes the loaded scene to <see cref="SceneEditorHub"/> so the
/// always-visible Hierarchy tool window can display its node tree, and subscribes to
/// <see cref="SceneEditorHub.SelectedNodeChanged"/> to highlight the corresponding object.
/// </summary>
internal sealed class SceneHwndHost : HwndHost
{
    private string? _repoRoot;
    private string _scenePath;
    private RenderSurfaceWindow? _surface;
    private SceneRenderer? _renderer;
    private EventHandler? _renderingHandler;
    private Action<Guid?>? _selectionHandler;

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

            SceneFile scene = SceneFile.Load(_scenePath);
            IReadOnlyDictionary<Guid, Matrix4x4> worldTransforms = scene.ComputeWorldTransforms();

            string sceneDirectory = Path.GetDirectoryName(_scenePath)
                ?? throw new InvalidOperationException($"シーンファイルのディレクトリを解決できませんでした: {_scenePath}");

            Dictionary<string, SimpleMesh> meshCache = new(StringComparer.OrdinalIgnoreCase);
            List<SceneRenderObject> renderObjects = [];
            foreach (SceneFileNode node in scene.Nodes)
            {
                if (node.MeshAssetPath is null)
                {
                    continue;
                }

                string absoluteMeshPath = Path.GetFullPath(Path.Combine(sceneDirectory, node.MeshAssetPath));
                if (!meshCache.TryGetValue(absoluteMeshPath, out SimpleMesh? mesh))
                {
                    mesh = MeshLoader.Load(absoluteMeshPath, _repoRoot);
                    meshCache[absoluteMeshPath] = mesh;
                }

                renderObjects.Add(new SceneRenderObject
                {
                    Mesh = mesh,
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

        _renderer?.Dispose();
        _renderer = null;
    }
}
