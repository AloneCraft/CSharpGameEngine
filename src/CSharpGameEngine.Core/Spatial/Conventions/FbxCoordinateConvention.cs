using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Fbx;

/// <summary>
/// AssimpNet's own default output convention (used for every format it imports, including FBX,
/// after Assimp's internal per-format axis normalization): right-handed, +Y up, +Z toward the
/// viewer (AssimpNet's <c>PostProcessSteps.MakeLeftHanded</c> XML doc: "By default the data is
/// returned in a right-handed coordinate space, where +X points to the right, +Z towards the
/// viewer, and +Y upwards"). "+Z toward the viewer" means Forward is -Z, mathematically identical
/// to <see cref="CSharpGameEngine.Spatial.Gltf.GltfCoordinateConvention"/> - not a coincidence
/// (glTF is itself right-handed/Y-up/-Z-forward by spec) but kept as its own distinct type rather
/// than reused directly, so <c>CSharpGameEngine.Import.Fbx</c> stays independent of
/// <c>CSharpGameEngine.Import.Gltf</c> and a future divergence (e.g. a source format Assimp does
/// NOT normalize the same way) would not require renaming a "Gltf"-named type. See
/// Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md.
/// </summary>
public readonly struct FbxCoordinateConvention : ICoordinateConvention
{
    public static CoordinateHandedness Handedness => CoordinateHandedness.RightHanded;

    public static XyzVector ToXyz(in RufVector Value) => new(Value.R, Value.U, -Value.F);

    public static RufVector FromXyz(in XyzVector Value) => new(Value.X, Value.Y, -Value.Z);

    public static XyzVector RightXyz => new(1f, 0f, 0f);

    public static XyzVector UpXyz => new(0f, 1f, 0f);

    public static XyzVector ForwardXyz => new(0f, 0f, -1f);
}
