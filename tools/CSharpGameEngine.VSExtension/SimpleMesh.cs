using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Bare vertex/index data for the VSIX in-process renderer (ADR-0008) - no UVs/texture. Shared by
/// <see cref="ExternalGltfLoader"/> (glTF, ADR-0019) and <see cref="ExternalFbxLoader"/> (FBX,
/// ADR-0010) - both parsed out-of-process via <see cref="ExternalBridgeProcess"/> - so
/// <see cref="VorticeRenderer"/> stays format-agnostic.
/// </summary>
internal sealed class SimpleMesh
{
    public required Vector3[] Positions { get; init; }

    public required Vector3[] Normals { get; init; }

    public required uint[] Indices { get; init; }

    public required Vector4 BaseColorFactor { get; init; }

    public required Vector3 BoundsCenter { get; init; }

    public required float BoundsRadius { get; init; }

    public static (Vector3 Center, float Radius) ComputeBoundingSphere(Vector3[] positions)
    {
        Vector3 Min = positions[0];
        Vector3 Max = positions[0];
        foreach (Vector3 P in positions)
        {
            Min = Vector3.Min(Min, P);
            Max = Vector3.Max(Max, P);
        }

        Vector3 Center = (Min + Max) * 0.5f;
        float Radius = 0f;
        foreach (Vector3 p in positions)
        {
            Radius = Math.Max(Radius, Vector3.Distance(Center, p));
        }

        return (Center, Radius);
    }
}
