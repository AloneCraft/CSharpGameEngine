using System;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="AssetPreviewWindowPane"/> (ADR-0013) - hosts one <see cref="AssetPreviewHwndHost"/> pointed at the opened file.</summary>
public partial class AssetPreviewControl:UserControl,IDisposable {
    private readonly AssetPreviewHwndHost Host;

    public AssetPreviewControl(string ModelPath){
        this.InitializeComponent();
        this.Host=new AssetPreviewHwndHost(ModelPath);
        this.RootGrid.Children.Add(this.Host);
    }

    public void SetModelPath(string ModelPath)=> this.Host.SetModelPath(ModelPath);

    // WindowPane only disposes Content when it implements IDisposable - without this, the native
    // window inside AssetPreviewHwndHost never runs DestroyWindowCore when the preview tab closes,
    // so the focus-release fix there (see RenderSurfaceWindow.Dispose) never fires either.
    public void Dispose()=> this.Host.Dispose();
}
