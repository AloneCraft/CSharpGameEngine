using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Tools&gt;Options の「C# Game Engine / Asset Editors」ページ。アセットをダブルクリックしたときの専用エディターを最初に開く場所
/// （フローティング／ドキュメントタブ）を選ぶ。保存先は <see cref="SceneEditorSettings"/> の同じコレクションで、DialogPage 標準の永続化は使わない。
/// </summary>
[Guid("c1a7e3d2-5b84-4f0a-8e69-2d3f7a1b9c40")]
public sealed class AssetEditorOptionsPage:UIElementDialogPage {
    private ComboBox?OpenLocationBox;
    protected override UIElement Child{
        get{
            ThreadHelper.ThrowIfNotOnUIThread();
            return this.EnsureControl();
        }
    }
    private ComboBox EnsureControl(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.OpenLocationBox is not{ }Existing){
            Existing=new ComboBox { MinWidth=240,HorizontalAlignment=HorizontalAlignment.Left };
            Existing.Items.Add(new ComboBoxItem { Content=VsixText.Get("AssetEditor_OpenLocationFloating"),Tag=AssetEditorOpenLocation.Floating });
            Existing.Items.Add(new ComboBoxItem { Content=VsixText.Get("AssetEditor_OpenLocationTab"),Tag=AssetEditorOpenLocation.DocumentTab });
            this.OpenLocationBox=Existing;
        }
        return Existing;
    }
    protected override void OnActivate(CancelEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnActivate(Args);
        this.Show(SceneEditorSettings.ReadAssetEditorOpenLocation());
    }
    protected override void OnApply(PageApplyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.ApplyBehavior==ApplyKind.Apply&&this.OpenLocationBox?.SelectedItem is ComboBoxItem{ Tag:AssetEditorOpenLocation Selected }){
            SceneEditorSettings.WriteAssetEditorOpenLocation(Selected);
        }
        base.OnApply(Args);
    }
    private void Show(AssetEditorOpenLocation Location){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Box=this.EnsureControl();
        Box.SelectedIndex=Location==AssetEditorOpenLocation.DocumentTab?1:0;
    }
}
