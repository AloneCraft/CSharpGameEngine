namespace CSharpGameEngine.VSExtension;
internal static class SceneEditorEscapeRoutingPolicy {
    internal static bool ShouldConsume(bool OwnerHasKeyboardFocus,bool IsEscape)=>
        OwnerHasKeyboardFocus&&IsEscape;
    internal static bool ShouldInvoke(bool EscapeAlreadyPressed)=>!EscapeAlreadyPressed;
    internal static bool IsVisualStudioEscapeKeyDown(int Message,long VirtualKey)=>
        Message==NativeMethods.WM_KEYDOWN&&VirtualKey==NativeMethods.VK_ESCAPE;
}
