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
internal sealed class AssetPreviewHwndHost:HwndHost {
    private string?RepoRoot;
    private string ModelPath;
    private RenderSurfaceWindow?Surface;
    private VorticeRenderer?Renderer;
    private EventHandler?RenderingHandler;

    public AssetPreviewHwndHost(string ModelPath){
        this.ModelPath=ModelPath;
    }

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        this.Surface=new RenderSurfaceWindow(HwndParent.Handle,800,600);
        this.LoadIntoSurface();
        return new HandleRef(this, this.Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        this.TearDownRenderer();

        this.Surface?.Dispose();
        this.Surface=null;
    }

    /// <summary>Reloads the preview with a different file - used if VS reuses this pane's doc data for a renamed/reloaded document.</summary>
    public void SetModelPath(string ModelPath){
        this.ModelPath=ModelPath;
        if(this.Surface is not null){
            this.TearDownRenderer();
            this.LoadIntoSurface();
        }
    }

    private void LoadIntoSurface(){
        if(this.Surface is null){
            return;
        }

        try {
            this.RepoRoot??=RepoLocator.ResolveRepoRoot();
            if(this.RepoRoot is null){
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }

            var Mesh=MeshLoader.Load(this.ModelPath, this.RepoRoot);
            this.Renderer=new VorticeRenderer(this.Surface.Handle,800,600,Mesh);

            this.Surface.Resized+=(W,H)=> this.Renderer?.Resize(W,H);

            this.RenderingHandler=(_,_)=> this.Renderer?.RenderFrame();
            CompositionTarget.Rendering+= this.RenderingHandler;
        } catch(Exception Ex){
            System.Windows.MessageBox.Show(
                $"アセットのプレビューに失敗しました: {Ex}",
                "C# Game Engine Asset Preview",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void TearDownRenderer(){
        if(this.RenderingHandler is not null){
            CompositionTarget.Rendering-= this.RenderingHandler;
            this.RenderingHandler=null;
        }

        this.Renderer?.Dispose();
        this.Renderer=null;
    }
}
