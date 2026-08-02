using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Embeds the glTF viewer inside a WPF tool window with a real in-process D3D12 renderer
/// (ADR-0008) - no external process, no window reparenting across process boundaries. The
/// renderer (<see cref="VorticeRenderer"/>) draws directly into the native window this class
/// creates and returns from <see cref="BuildWindowCore"/>.
/// </summary>
internal sealed class ViewerHwndHost : HwndHost
{
    private const string ViewerModelRelativePath = @"assets\TestFixtures\Gltf\Box.glb";

    private RenderSurfaceWindow? _surface;
    private VorticeRenderer? _renderer;
    private EventHandler? _renderingHandler;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _surface = new RenderSurfaceWindow(hwndParent.Handle, 800, 600);

        try
        {
            string? repoRoot = RepoLocator.ResolveRepoRoot();
            if (repoRoot is null)
            {
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }

            string modelPath = Path.Combine(repoRoot, ViewerModelRelativePath);
            SimpleMesh mesh = MeshLoader.Load(modelPath, repoRoot);
            _renderer = new VorticeRenderer(_surface.Handle, 800, 600, mesh);

            _surface.Resized += (w, h) => _renderer?.Resize(w, h);

            _renderingHandler = (_, _) => _renderer?.RenderFrame();
            CompositionTarget.Rendering += _renderingHandler;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"ビューアの初期化に失敗しました: {ex}",
                "C# Game Engine Viewer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }

        return new HandleRef(this, _surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_renderingHandler is not null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }

        _renderer?.Dispose();
        _renderer = null;

        _surface?.Dispose();
        _surface = null;
    }
}
