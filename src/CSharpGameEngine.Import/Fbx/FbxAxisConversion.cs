using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Fbx;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Import.Fbx;

/// <summary>
/// Converts AssimpNet's own default output space (<see cref="FbxCoordinateConvention"/>:
/// right-handed, Y-up, Forward=-Z - see its doc comment) into this engine's fixed
/// <see cref="UnityCoordinateConvention"/> (left-handed, Y-up) at the Import boundary, per
/// Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md. <see cref="FbxImporter"/> has no
/// scene graph or per-node world transform (single mesh only, mirroring
/// <c>CSharpGameEngine.Import.Gltf.GltfImporter.Import(string)</c>'s scope) so only mesh-local
/// positions/normals and triangle winding need converting - no rotation-matrix conjugation like
/// <c>GltfAxisConversion.ConvertWorldTransform</c> is needed here.
/// </summary>
internal static class FbxAxisConversion
{
    /// <summary>Converts one AssimpNet-space point or direction into engine space. Under the two fixed Conventions involved, this reduces to negating Z.</summary>
    public static Vector3 ConvertVector(Vector3 AssimpSpaceValue)
    {
        RufVector Ruf = Spatial<FbxCoordinateConvention>.FromXyz((XyzVector)AssimpSpaceValue);
        return (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Ruf);
    }

    /// <summary>Swaps two indices of each triangle so front-facing winding is preserved after a single-axis position mirror - see GltfAxisConversion.FlipTriangleWinding (same reasoning, no visible effect today since every consumer disables backface culling).</summary>
    public static void FlipTriangleWinding(List<uint> Indices)
    {
        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            (Indices[i + 1], Indices[i + 2]) = (Indices[i + 2], Indices[i + 1]);
        }
    }
}
