using System;
using System.Collections.Generic;

namespace CSharpGameEngine.VSExtension;

/// <summary>Tool-window kinds that can participate in the first-run layout.</summary>
internal enum InitialLayoutToolWindowKind {
    Hierarchy,
    SceneDesigner,
}

/// <summary>Presentation policy that must not take the Visual Studio keyboard focus.</summary>
internal enum ToolWindowPresentation {
    NonActivating,
}

/// <summary>One immutable tool-window entry in the first-run layout.</summary>
internal sealed class InitialLayoutToolWindowSpec {
    internal InitialLayoutToolWindowSpec(
        InitialLayoutToolWindowKind Kind,
        int Width,
        ToolWindowPresentation Presentation){
        this.Kind=Kind;
        this.Width=Width;
        this.Presentation=Presentation;
    }

    internal InitialLayoutToolWindowKind Kind { get; }

    internal int Width { get; }

    internal ToolWindowPresentation Presentation { get; }
}

/// <summary>
/// First-run layout plan. Scene Designer is intentionally absent because it requires an explicit
/// IScene target selected from the code editor.
/// </summary>
internal static class InitialLayoutToolWindowPlan {
    private static readonly IReadOnlyList<InitialLayoutToolWindowSpec> ToolWindowsValue=
        Array.AsReadOnly(new[]{
            new InitialLayoutToolWindowSpec(
                InitialLayoutToolWindowKind.Hierarchy,
                Width:300,
                ToolWindowPresentation.NonActivating),
        });

    internal static IReadOnlyList<InitialLayoutToolWindowSpec> ToolWindows => ToolWindowsValue;
}