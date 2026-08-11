namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX-side deserialization target for <c>CSharpGameEngine.SceneAuthoring.Cli</c>'s
/// <c>ScenePreviewBridgeDocument</c> (ADR-0031) - same "duplicate the wire shape, not the assembly"
/// convention as <see cref="BridgeDocument"/>. <see cref="ScenePreviewSceneFileAdapter"/> converts
/// this into a <see cref="SceneFile"/> for the existing シーン preview pipeline.
/// </summary>
internal sealed class ScenePreviewBridgeDocument {
    public bool Success { get; set;}

    public string?ErrorMessage { get; set;}

    public string?SceneClassFullName { get; set;}

    public ScenePreviewNodeDto[]Nodes { get; set;}=[];
}

internal sealed class ScenePreviewNodeDto {
    public int EntityId { get; set;}

    public int?ParentEntityId { get; set;}

    public string?Name { get; set;}

    public float[]Position { get; set;}=[0f,0f,0f];

    public float[]Rotation { get; set;}=[0f,0f,0f,1f];

    public float[]Scale { get; set;}=[1f,1f,1f];

    public string[]ComponentTypeNames { get; set;}=[];
}
