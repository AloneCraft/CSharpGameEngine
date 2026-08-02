using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Same in-process D3D12 embedding approach as <see cref="ViewerHwndHost"/> (ADR-0007/0008), but
/// for the Solution Explorer asset preview (ADR-0013) instead of the manual "show viewer" tool
/// window: the model path is supplied by the caller (the file VS opened) instead of being a fixed
/// sample. <see cref="SetModelPath"/> supports being told a new path after the surface already
/// exists, so the same pane instance can be reused if VS reloads the same doc data.
/// </summary>
internal sealed class AssetPreviewHwndHost : HwndHost
{
    private string? _repoRoot;
    private string _modelPath;
    private RenderSurfaceWindow? _surface;
    private VorticeRenderer? _renderer;
    private EventHandler? _renderingHandler;

    public AssetPreviewHwndHost(string modelPath)
    {
        _modelPath = modelPath;
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

    /// <summary>Reloads the preview with a different file - used if VS reuses this pane's doc data for a renamed/reloaded document.</summary>
    public void SetModelPath(string modelPath)
    {
        _modelPath = modelPath;
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

            SimpleMesh mesh = MeshLoader.Load(_modelPath, _repoRoot);
            _renderer = new VorticeRenderer(_surface.Handle, 800, 600, mesh);

            _surface.Resized += (w, h) => _renderer?.Resize(w, h);

            _renderingHandler = (_, _) => _renderer?.RenderFrame();
            CompositionTarget.Rendering += _renderingHandler;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"アセットのプレビューに失敗しました: {ex}",
                "C# Game Engine Asset Preview",
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

        _renderer?.Dispose();
        _renderer = null;
    }
}
