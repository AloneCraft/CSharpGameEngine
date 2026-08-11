namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// Flat, JSON-friendly response shape (mirrors <c>GltfBridgeDocument</c>'s convention: no custom
/// types, so the net472 VSIX side never needs to reference this net10.0 assembly). Parent/child is
/// expressed via <see cref="ScenePreviewNode.ParentEntityId"/>, the same shape
/// <c>SceneFile</c>'s own <c>ParentId</c> already uses, so converting into a <c>SceneFile</c> on the
/// VSIX side is a direct field mapping.
/// </summary>
public sealed record ScenePreviewBridgeDocument {
    public bool Success { get; init;}

    public string?ErrorMessage { get; init;}

    public string?SceneClassFullName { get; init;}

    public ScenePreviewNode[]Nodes { get; init;}=[];

    public static ScenePreviewBridgeDocument ForFailure(string errorMessage)=>new(){ Success=false,ErrorMessage=errorMessage };
}

/// <summary>
/// One entity from the executed <c>World</c> (<see cref="WorldInspector"/>), with its declaration-order
/// <see cref="Name"/> (if recoverable) merged back in by <c>ScenePreviewBridgeWorker</c> from
/// <c>SceneDefinitionSourceParser</c>'s separate syntax-only pass over the source file.
/// </summary>
public sealed record ScenePreviewNode {
    public required int EntityId { get; init;}

    public int?ParentEntityId { get; init;}

    public string?Name { get; init;}

    public float[]Position { get; init;}=[0f,0f,0f];

    public float[]Rotation { get; init;}=[0f,0f,0f,1f];

    public float[]Scale { get; init;}=[1f,1f,1f];

    public string[]ComponentTypeNames { get; init;}=[];
}
