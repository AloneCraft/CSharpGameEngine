using System;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window showing the 3D preview of one <c>IScene</c>-implementing class.
/// The Package-owned session provider does not hold designer state; it only lets a pane restored by
/// Visual Studio attach to the one already-created editor session and populate its project ComboBox.
/// </summary>
[Guid(PaneGuidString)]
public sealed class SceneDesignerToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="6f2c6a0e-6e8a-4a7a-9a3a-3b6f2b8d7c4a";

    private static Func<SceneEditorSessionService?>?SessionProvider;
    private readonly SceneDesignerToolWindowControl Control;

    public SceneDesignerToolWindowPane():base(null){
        Caption="シーン デザイナー";
        Control=new SceneDesignerToolWindowControl();
        Content=Control;
        if(SessionProvider?.Invoke() is { } Session){
            Control.SetSession(Session);
        }
    }

    internal static void ConfigureSessionProvider(Func<SceneEditorSessionService?>?Provider)=>SessionProvider=Provider;

    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot)=>Control.SetTarget(Session,Snapshot);
}