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
public static class GltfAxisConversion {
    /// <summary>Converts one glTF-space point or direction (both are plain linear maps here - no translation component to treat specially) into engine space. Under the two fixed Conventions involved, this reduces to negating Z.</summary>
    public static Vector3 ConvertVector(Vector3 GltfSpaceValue){
        RufVector Ruf=Spatial<GltfCoordinateConvention>.FromXyz((XyzVector)GltfSpaceValue);
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Ruf);
    }

    /// <summary>
    /// Converts a glTF tangent (xyz = tangent direction, w = bitangent sign) into engine space. The
    /// xyz part converts like any other direction; w must flip because a single-axis mirror has
    /// determinant -1, which inverts the handedness relationship <c>bitangent = cross(normal,
    /// tangent) * w</c> unless w is negated to compensate (verified algebraically in
    /// ADR-0027 - cross(mirror(a), mirror(b)) = -mirror(cross(a, b)) for any orthogonal mirror).
    /// </summary>
    public static Vector4 ConvertTangent(Vector4 GltfSpaceTangent){
        Vector3 Converted=ConvertVector(new Vector3(GltfSpaceTangent.X,GltfSpaceTangent.Y,GltfSpaceTangent.Z));
        return new Vector4(Converted,-GltfSpaceTangent.W);
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
    public static Matrix4x4 ConvertWorldTransform(Matrix4x4 GltfSpaceWorldTransform){
        if(!Matrix4x4.Decompose(GltfSpaceWorldTransform,out Vector3 Scale,out Quaternion Rotation,out Vector3 Translation)){
            // Non-decomposable (e.g. singular/degenerate) source matrices are passed through
            // unconverted rather than thrown away - a best-effort fallback for content this engine
            // does not otherwise validate at import time.
            return GltfSpaceWorldTransform;
        }

        Vector3 ConvertedTranslation=ConvertVector(Translation);
        Quaternion ConvertedRotation=MirrorRotationAroundZ(Rotation);

        RufVector TranslationRuf=new(ConvertedTranslation.X,ConvertedTranslation.Y,ConvertedTranslation.Z);
        RufQuaternion RotationRuf=new(ConvertedRotation.X,ConvertedRotation.Y,ConvertedRotation.Z,ConvertedRotation.W);
        RufVector ScaleRuf=new(Scale.X,Scale.Y,Scale.Z);

        return StandardTransformConvention.Compose(TranslationRuf,RotationRuf,ScaleRuf);
    }

    /// <summary>Converts a glTF-space rotation: the mirror <c>C * R * C</c> with <c>C = diag(1, 1, -1)</c> keeps the angle and maps the axis to (-x, -y, z).</summary>
    public static Quaternion ConvertRotation(Quaternion GltfSpaceRotation)=>new(-GltfSpaceRotation.X,-GltfSpaceRotation.Y,GltfSpaceRotation.Z,GltfSpaceRotation.W);

    /// <summary>Converts any glTF-space affine matrix (row-vector convention), including non-decomposable ones such as inverse bind matrices: <c>C * M * C</c>.</summary>
    public static Matrix4x4 ConvertMatrix(Matrix4x4 GltfSpaceMatrix){
        var C=Matrix4x4.CreateScale(1f,1f,-1f);
        return C*GltfSpaceMatrix*C;
    }

    /// <summary>Swaps two indices of each triangle so front-facing winding is preserved after a single-axis position mirror (which otherwise inverts every triangle's chirality). No visible effect today (every consumer disables backface culling), but keeps winding correct if that ever changes, and keeps triangle orientation consistent with the (already-flipped) tangent/normal data.</summary>
    public static void FlipTriangleWinding(uint[]Indices){
        for(var I=0;I+2<Indices.Length;I+=3){
            (Indices[I+1],Indices[I+2])=(Indices[I+2],Indices[I+1]);
        }
    }

    private static Quaternion MirrorRotationAroundZ(Quaternion Rotation){
        Matrix4x4 R=Matrix4x4.CreateFromQuaternion(Rotation);
        Matrix4x4 Mirrored=new(
            R.M11,R.M12,-R.M13,0f,
            R.M21,R.M22,-R.M23,0f,
            -R.M31,-R.M32,R.M33,0f,
            0f,0f,0f,1f);
        return Quaternion.CreateFromRotationMatrix(Mirrored);
    }
}
