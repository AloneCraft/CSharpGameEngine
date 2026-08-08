namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX-side mirror of <c>CSharpGameEngine.SceneAuthoring.Cli</c>'s <c>ScenePreviewRequest</c>
/// (ADR-0031) - same "duplicate the wire shape, not the assembly" convention as
/// <see cref="BridgeDocument"/>. Serialized to a temp JSON file and that file's path passed as the
/// bridge's single "input path", since the shared bridge transport
/// (<see cref="ExternalBridgeProcess"/>) is fixed to exactly one input + one output path.
/// </summary>
internal sealed class ScenePreviewRequest
{
    public string SourceFilePath { get; set; } = "";

    public string TargetAssemblyPath { get; set; } = "";
}
