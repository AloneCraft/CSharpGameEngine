using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// One drawable or light instance flattened out of a glTF scene graph. <see cref="WorldTransform"/>
/// is already fully resolved through the source node's parent chain (SharpGLTF's Node.WorldMatrix),
/// so consumers never need to walk hierarchy or compose transforms themselves. A source glTF node
/// with multiple mesh primitives is flattened into multiple <see cref="ImportedSceneNode"/> entries
/// that share the same <see cref="Name"/> and <see cref="WorldTransform"/> but differ in
/// <see cref="MeshIndex"/>/<see cref="MaterialIndex"/>, since each primitive is a separate drawable.
/// </summary>
public sealed class ImportedSceneNode {
    public required string Name { get; init;}

    public required Matrix4x4 WorldTransform { get; init;}

    /// <summary>Index into <see cref="ImportedScene.Meshes"/>. Null for light-only nodes.</summary>
    public int?MeshIndex { get; init;}

    /// <summary>Index into <see cref="ImportedScene.Materials"/>. Null exactly when <see cref="MeshIndex"/> is null.</summary>
    public int?MaterialIndex { get; init;}

    /// <summary>Index into <see cref="ImportedScene.Lights"/>. Null for mesh nodes.</summary>
    public int?LightIndex { get; init;}
}
