using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0052: 追加できるComponent型を並べるツールウィンドウ。Session の受け渡しは
/// <see cref="InspectorToolWindowPane"/> と同じで、pane自体は状態を持たず、VSが復元した pane を
/// 既存の編集セッションへ繋ぎ直すだけ。
/// </summary>
[Guid(PaneGuidString)]
public sealed class ComponentPaletteToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="0a5a7d16-9b0e-4a4a-9a4a-2c2f4e51a7b3";

    private static Func<SceneEditorSessionService?>?SessionProvider;

    public ComponentPaletteToolWindowPane():base(null){
        this.Caption="コンポーネント";
        this.Content=new ComponentPaletteToolWindowControl();
        if(SessionProvider?.Invoke() is { } Session&& this.Content is ComponentPaletteToolWindowControl Control){
            Control.SetSession(Session);
        }
    }

    internal static void ConfigureSessionProvider(Func<SceneEditorSessionService?>?Provider)=>SessionProvider=Provider;
}
