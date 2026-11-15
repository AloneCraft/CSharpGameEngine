using System;
using System.Windows;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>種類ごとの <see cref="AssetEditorContent"/> を作る。種類が増えたらここと <see cref="AssetEditorRegistry"/> に追加する。</summary>
internal static class AssetEditorContentFactory {
    internal static AssetEditorContent Create(AssetEditorDescriptor Descriptor,string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        return Descriptor.Kind switch {
            AssetEditorKind.StaticMesh=>new StaticMeshEditorContent(FilePath),
            AssetEditorKind.Texture=>new TextureEditorContent(FilePath),
            AssetEditorKind.SoundWave=>new SoundEditorContent(FilePath),
            AssetEditorKind.Font=>new FontEditorContent(FilePath),
            AssetEditorKind.DataAsset=>new DataAssetEditorContent(FilePath),
            _=>new DetailsOnlyEditorContent(FilePath),
        };
    }
}
/// <summary>専用の中央表示を持たない種類の暫定表示。Details（Inspector）を全面に出す。</summary>
internal sealed class DetailsOnlyEditorContent:AssetEditorContent {
    private readonly AssetDetailsView DetailsView;
    internal DetailsOnlyEditorContent(string FilePath):base(FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView=new AssetDetailsView(FilePath);
    }
    internal override UIElement Center=>this.DetailsView;
    internal override bool HandleNativeShortcut(System.Windows.Input.Key Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.DetailsView.HandleNativeShortcut(Key);
    }
    protected override void OnReload(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView.Retarget(this.FilePath);
    }
    public override void Dispose()=>this.DetailsView.Dispose();
}
