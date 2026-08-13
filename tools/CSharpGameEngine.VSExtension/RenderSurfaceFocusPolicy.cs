namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Win32/WPFの実フォーカスを描画Surfaceへ渡さないための判断を集約します。
/// Surfaceはマウス入力を受けるD3D12の描画先であり、キーボード入力を所有するUIではありません。
/// </summary>
internal static class RenderSurfaceFocusPolicy {
    internal static RenderSurfaceFocusAction GetAction(uint Message)=>Message switch {
        NativeMethods.WM_MOUSEACTIVATE=>RenderSurfaceFocusAction.MouseWithoutActivation,
        NativeMethods.WM_SETFOCUS=>RenderSurfaceFocusAction.ReturnFocus,
        _=>RenderSurfaceFocusAction.Default,
    };

    internal static System.IntPtr SelectReturnTarget(
        System.IntPtr PreviousFocus,
        bool PreviousIsWindow,
        System.IntPtr SurfaceHandle,
        System.IntPtr ParentHandle,
        bool ParentIsWindow){
        if(PreviousFocus!=System.IntPtr.Zero
            &&PreviousFocus!=SurfaceHandle
            &&PreviousIsWindow){
            return PreviousFocus;
        }

        return ParentHandle!=System.IntPtr.Zero
            &&ParentHandle!=SurfaceHandle
            &&ParentIsWindow
            ?ParentHandle
            :System.IntPtr.Zero;
    }

    internal static bool ShouldRestoreFocus(
        System.IntPtr CurrentFocus,
        System.IntPtr SurfaceHandle,
        System.IntPtr ReturnTarget)
        =>CurrentFocus==SurfaceHandle
            &&SurfaceHandle!=System.IntPtr.Zero
            &&ReturnTarget!=System.IntPtr.Zero
            &&ReturnTarget!=SurfaceHandle;

    internal static bool AllowTabInto=>false;
}

internal enum RenderSurfaceFocusAction {
    Default,
    MouseWithoutActivation,
    ReturnFocus,
}
