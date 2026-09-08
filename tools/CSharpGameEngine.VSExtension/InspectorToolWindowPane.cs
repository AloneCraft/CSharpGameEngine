using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window showing the editable members of the currently-selected scene node
/// (M0031). The Package-owned session provider mirrors <see cref="SceneDesignerToolWindowPane"/>:
/// the pane does not hold designer state itself, it only lets a pane restored by Visual Studio
/// attach to the one already-created editor session.
/// </summary>
[Guid(PaneGuidString)]
public sealed class InspectorToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="7ca25df1-0dbf-4b49-9c12-fed543543e26";

    private static Func<SceneEditorSessionService?>?SessionProvider;
    private static WeakReference<InspectorToolWindowControl>?RestoredControl;
    private readonly InspectorToolWindowControl Control;

    public InspectorToolWindowPane():base(null){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Caption=VsixText.Get("PaneInspector");
        this.Control=new InspectorToolWindowControl();
        this.Content=this.Control;
        RestoredControl=new WeakReference<InspectorToolWindowControl>(this.Control);
        if(SessionProvider?.Invoke() is { }Session){
            this.Control.SetSession(Session);
        }
    }

    internal static void ConfigureSessionProvider(Func<SceneEditorSessionService?>Provider){
        if(Provider is null){ throw new ArgumentNullException(nameof(Provider)); }
        ThreadHelper.ThrowIfNotOnUIThread();
        SessionProvider=Provider;
        if(RestoredControl?.TryGetTarget(out var Control)==true
            &&SessionProvider?.Invoke() is { }Session){
            Control.SetSession(Session);
        }
    }

    internal static void ClearSessionProvider()=>SessionProvider=null;
}
