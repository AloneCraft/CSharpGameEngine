using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// Converts glTF file-space data (<see cref="GltfCoordinateConvention"/>: right-handed, Y-up) into
/// this engine's fixed <see cref="UnityCoordinateConvention"/> (left-handed, Y-up) at the Import
/// boundary, per Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md. Everything
/// <see cref="GltfImporter.ImportScene"/> returns (mesh-local vertex data and per-node world
/// transforms) is already in engine space by the time it leaves this project - most consumers
/// (<c>StageRenderer</c>, <c>ModelRenderer</c>) never need to know glTF's own convention exists.
/// Public (not internal) because <c>UserGameProject.SceneLoader</c> is a second legitimate caller:
/// <c>scene_meta.json</c>'s PlayerStart is written in glTF space by
/// <c>tools/UnrealExport/export_lvl_firstperson.py</c>'s own UE-&gt;glTF conversion, so it needs the
/// same glTF-&gt;engine conversion applied here, even though it never passes through
/// <see cref="GltfImporter"/> itself. Not used by the older single-primitive
/// <see cref="GltfImporter.Import(string)"/> preview path (out of scope - see ADR-0027 "対象外").
/// </summary>
public static class GltfAxisConversion
{
    /// <summary>Converts one glTF-space point or direction (both are plain linear maps here - no translation component to treat specially) into engine space. Under the two fixed Conventions involved, this reduces to negating Z.</summary>
    public static Vector3 ConvertVector(Vector3 gltfSpaceValue)
    {
        RufVector ruf = Spatial<GltfCoordinateConvention>.FromXyz((XyzVector)gltfSpaceValue);
        return (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(ruf);
    }

    /// <summary>
    /// Converts a glTF tangent (xyz = tangent direction, w = bitangent sign) into engine space. The
    /// xyz part converts like any other direction; w must flip because a single-axis mirror has
    /// determinant -1, which inverts the handedness relationship <c>bitangent = cross(normal,
    /// tangent) * w</c> unless w is negated to compensate (verified algebraically in
    /// ADR-0027 - cross(mirror(a), mirror(b)) = -mirror(cross(a, b)) for any orthogonal mirror).
    /// </summary>
    public static Vector4 ConvertTangent(Vector4 gltfSpaceTangent)
    {
        Vector3 converted = ConvertVector(new Vector3(gltfSpaceTangent.X, gltfSpaceTangent.Y, gltfSpaceTangent.Z));
        return new Vector4(converted, -gltfSpaceTangent.W);
    }

    /// <summary>
    /// Converts a glTF-space world transform (translation/rotation/scale) into engine space.
    /// Translation is a plain point conversion (<see cref="ConvertVector"/>). Scale magnitude is
    /// unaffected by a single-axis mirror, so it passes through unchanged. Rotation requires the
    /// standard similarity-transform treatment for a reflection (<c>R' = C * R * C</c> where
    /// <c>C = diag(1, 1, -1)</c>) - naively negating the rotation quaternion's Z component would
    /// only be correct for a proper rotation (determinant +1), not a reflection; recomposing
    /// afterward via <see cref="StandardTransformConvention.Compose"/> keeps this using the same
    /// Scale-then-Rotation-then-Translation convention as the rest of the engine (ADR-0025).
    /// </summary>
    public static Matrix4x4 ConvertWorldTransform(Matrix4x4 gltfSpaceWorldTransform)
    {
        if (!Matrix4x4.Decompose(gltfSpaceWorldTransform, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            // Non-decomposable (e.g. singular/degenerate) source matrices are passed through
            // unconverted rather than thrown away - a best-effort fallback for content this engine
            // does not otherwise validate at import time.
            return gltfSpaceWorldTransform;
        }

        Vector3 convertedTranslation = ConvertVector(translation);
        Quaternion convertedRotation = MirrorRotationAroundZ(rotation);

        RufVector translationRuf = new(convertedTranslation.X, convertedTranslation.Y, convertedTranslation.Z);
        RufQuaternion rotationRuf = new(convertedRotation.X, convertedRotation.Y, convertedRotation.Z, convertedRotation.W);
        RufVector scaleRuf = new(scale.X, scale.Y, scale.Z);

        return StandardTransformConvention.Compose(translationRuf, rotationRuf, scaleRuf);
    }

    /// <summary>Swaps two indices of each triangle so front-facing winding is preserved after a single-axis position mirror (which otherwise inverts every triangle's chirality). No visible effect today (every consumer disables backface culling), but keeps winding correct if that ever changes, and keeps triangle orientation consistent with the (already-flipped) tangent/normal data.</summary>
    public static void FlipTriangleWinding(uint[] indices)
    {
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }
    }

    private static Quaternion MirrorRotationAroundZ(Quaternion rotation)
    {
        Matrix4x4 r = Matrix4x4.CreateFromQuaternion(rotation);
        Matrix4x4 mirrored = new(
            r.M11, r.M12, -r.M13, 0f,
            r.M21, r.M22, -r.M23, 0f,
            -r.M31, -r.M32, r.M33, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.CreateFromRotationMatrix(mirrored);
    }
}
