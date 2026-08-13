using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>Shows a ToolWindow frame without making it the keyboard focus owner.</summary>
internal static class ToolWindowFramePresenter {
    internal static void ShowWithoutActivation(
        Action ShowNoActivate,
        Action?RestoreFocus){
        if(ShowNoActivate is null){
            throw new ArgumentNullException(nameof(ShowNoActivate));
        }

        ShowNoActivate();
        RestoreFocus?.Invoke();
    }
}