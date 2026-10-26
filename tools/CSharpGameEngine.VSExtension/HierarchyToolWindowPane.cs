using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 現在開いているシーンのノードツリーを表示する、ドッキング可能な VS tool window（ADR-0020）。Unity の Hierarchy 形式です。シーンのロード処理は持たず、<see cref="HierarchyToolWindowControl"/> が <see cref="SceneEditorHub"/> のイベントに応答します。
/// </summary>
[Guid(PaneGuidString)]
public sealed class HierarchyToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="21310c74-a151-49af-a47b-67c543d80eb9";

    public HierarchyToolWindowPane():base(null){
        this.Caption=VsixText.Get("PaneHierarchy");
        this.Content=new HierarchyToolWindowControl();
    }

    protected override bool PreProcessMessage(
        ref System.Windows.Forms.Message Message){
        ThreadHelper.ThrowIfNotOnUIThread();
        // ToolWindowPane.TranslateAccelerator は Visual Studio が所有する最初のキーボード境界です。
        // このペインがアクセラレーターを所有しているとき、WPF が KeyEvent を発生させる前に、
        // シェルのコマンドルーティングで Escape が消費されることがあるため、ここで処理します。
        if(SceneEditorEscapeRoutingPolicy.IsVisualStudioEscapeKeyDown(
                Message.Msg,Message.WParam.ToInt64())
            &&this.Content is HierarchyToolWindowControl Hierarchy){
            Hierarchy.HandleVisualStudioEscape();
            return true;
        }
        return base.PreProcessMessage(ref Message);
    }
}
