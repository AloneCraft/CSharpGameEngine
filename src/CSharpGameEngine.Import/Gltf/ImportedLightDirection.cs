using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// Derives an <see cref="ImportedLight"/>'s world-space travel direction from its owning node's
/// (already engine-space, per ADR-0027) <see cref="ImportedSceneNode.WorldTransform"/>. Centralizes
/// the "local Forward axis" knowledge in one place instead of re-implementing it at each caller: a
/// punctual light's world direction is its node's local RUF Forward, transformed by the node's world
/// matrix - under this engine's fixed <see cref="CSharpGameEngine.Spatial.Unity.UnityCoordinateConvention"/>,
/// local Forward is <c>+Vector3.UnitZ</c> (not glTF's own local <c>-Z</c>, which only applied before
/// <see cref="GltfAxisConversion.ConvertWorldTransform"/> ran).
/// </summary>
public static class ImportedLightDirection
{
    public static Vector3 WorldForward(Matrix4x4 EngineSpaceWorldTransform) =>
        Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, EngineSpaceWorldTransform));
}
