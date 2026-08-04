using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

public enum ImportedLightType
{
    Directional,
    Point,
    Spot,
}

/// <summary>
/// A KHR_lights_punctual light definition. Direction is not stored here - it is derived from the
/// owning <see cref="ImportedSceneNode.WorldTransform"/> (a punctual light always points along its
/// node's local -Z axis, per the KHR_lights_punctual spec), so callers compute world-space
/// direction as <c>Vector3.TransformNormal(-Vector3.UnitZ, node.WorldTransform)</c>.
/// </summary>
public sealed class ImportedLight
{
    public required ImportedLightType LightType { get; init; }

    public required Vector3 Color { get; init; }

    public required float Intensity { get; init; }
}
