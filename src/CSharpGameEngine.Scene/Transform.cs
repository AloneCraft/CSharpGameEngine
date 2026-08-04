using System.Numerics;

namespace CSharpGameEngine.Scene;

/// <summary>Local position/rotation/scale of a <see cref="SceneNode"/> (Docs/EditorSpec.md §3).</summary>
public readonly record struct Transform(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static Transform Identity { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
}
