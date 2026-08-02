using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Bare vertex/index data for the VSIX in-process renderer (ADR-0008) - no UVs/texture. Shared by
/// <see cref="SimpleGltfLoader"/> (glTF, parsed directly in-process via SharpGLTF.Core) and
/// <see cref="ExternalFbxLoader"/> (FBX, parsed out-of-process via ADR-0010's bridge process) so
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
        Vector3 min = positions[0];
        Vector3 max = positions[0];
        foreach (Vector3 p in positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 center = (min + max) * 0.5f;
        float radius = 0f;
        foreach (Vector3 p in positions)
        {
            radius = Math.Max(radius, Vector3.Distance(center, p));
        }

        return (center, radius);
    }
}
