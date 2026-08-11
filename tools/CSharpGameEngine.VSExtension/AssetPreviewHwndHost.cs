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
    private string? RepoRoot;
    private string ModelPath;
    private RenderSurfaceWindow? Surface;
    private VorticeRenderer? Renderer;
    private EventHandler? RenderingHandler;

    public AssetPreviewHwndHost(string ModelPath)
    {
        this.ModelPath = ModelPath;
    }

    protected override HandleRef BuildWindowCore(HandleRef HwndParent)
    {
        Surface = new RenderSurfaceWindow(HwndParent.Handle, 800, 600);
        LoadIntoSurface();
        return new HandleRef(this, Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd)
    {
        TearDownRenderer();

        Surface?.Dispose();
        Surface = null;
    }

    /// <summary>Reloads the preview with a different file - used if VS reuses this pane's doc data for a renamed/reloaded document.</summary>
    public void SetModelPath(string ModelPath)
    {
        this.ModelPath = ModelPath;
        if (Surface is not null)
        {
            TearDownRenderer();
            LoadIntoSurface();
        }
    }

    private void LoadIntoSurface()
    {
        if (Surface is null)
        {
            return;
        }

        try
        {
            RepoRoot ??= RepoLocator.ResolveRepoRoot();
            if (RepoRoot is null)
            {
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }

            SimpleMesh Mesh = MeshLoader.Load(ModelPath, RepoRoot);
            Renderer = new VorticeRenderer(Surface.Handle, 800, 600, Mesh);

            Surface.Resized += (W, H) => Renderer?.Resize(W, H);

            RenderingHandler = (_, _) => Renderer?.RenderFrame();
            CompositionTarget.Rendering += RenderingHandler;
        }
        catch (Exception Ex)
        {
            System.Windows.MessageBox.Show(
                $"アセットのプレビューに失敗しました: {Ex}",
                "C# Game Engine Asset Preview",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void TearDownRenderer()
    {
        if (RenderingHandler is not null)
        {
            CompositionTarget.Rendering -= RenderingHandler;
            RenderingHandler = null;
        }

        Renderer?.Dispose();
        Renderer = null;
    }
}
