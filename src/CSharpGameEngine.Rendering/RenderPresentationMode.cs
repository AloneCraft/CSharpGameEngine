namespace CSharpGameEngine.Rendering;

/// <summary>
/// Provider-neutral presentation view of the same render-world snapshot. This is derived display
/// state, never authoritative simulation data.
/// </summary>
public enum RenderPresentationMode:byte {
    Lit,
    BaseColor,
    Normal,
    Wireframe,
}
