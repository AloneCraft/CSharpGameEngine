namespace CSharpGameEngine.VSExtension;
internal static class SceneEditorEscapeRoutingPolicy {
    internal static bool ShouldConsume(bool OwnerHasKeyboardFocus,bool IsEscape)=>
        OwnerHasKeyboardFocus&&IsEscape;
    internal static bool ShouldInvoke(bool EscapeAlreadyPressed)=>!EscapeAlreadyPressed;
}
