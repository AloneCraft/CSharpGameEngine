using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

public enum ImportedLightType {
    Directional,
    Point,
    Spot,
}

/// <summary>
/// A KHR_lights_punctual light definition. Direction is not stored here - it is derived from the
/// owning <see cref="ImportedSceneNode.WorldTransform"/> via <see cref="ImportedLightDirection.WorldForward"/>.
/// A punctual light points along its node's local -Z axis per the KHR_lights_punctual spec, but
/// <see cref="ImportedSceneNode.WorldTransform"/> is already converted into this engine's own
/// (Unity-compatible) space by the time it leaves <see cref="GltfImporter"/> (ADR-0027), where local
/// Forward is <c>+Z</c>, not glTF's own local <c>-Z</c> - do not apply <c>-Vector3.UnitZ</c> directly.
/// </summary>
public sealed class ImportedLight {
    public required ImportedLightType LightType { get; init;}

    public required Vector3 Color { get; init;}

    public required float Intensity { get; init;}
}
