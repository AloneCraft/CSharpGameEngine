using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>キーボードフォーカスを取得せずに ToolWindow のフレームを表示する。</summary>
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