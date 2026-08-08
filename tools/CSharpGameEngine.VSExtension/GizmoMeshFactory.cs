using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Procedurally builds a small placeholder cube <see cref="SimpleMesh"/> in memory (no disk I/O, no
/// bridge call) for <c>[SceneDefinition]</c>-authored nodes, which have no mesh-asset concept at all
/// (ADR-0031). Deliberately NOT routed through <c>CSharpGameEngine.Geometry.ProceduralMeshFactory
/// .CreateBox</c>: that method's vertex/normal math goes through the RUF spatial abstraction
/// (<c>CSharpGameEngine.Spatial.Abstractions</c>/<c>.Unity</c>, both net10.0), which this net472 VSIX
/// cannot reference in-process - the same TFM wall ADR-0031 already documents for
/// <c>CSharpGameEngine.Entities</c>. A fixed-size axis-aligned cube is constant geometry, not an
/// algorithm that can drift, so duplicating just this one shape locally (as plain
/// <see cref="System.Numerics.Vector3"/> math, not RUF) is the same "duplicate the trivial thing,
/// don't bridge it" call ADR-0023 already made for the wire-protocol DTOs. Face/vertex/index layout
/// mirrors <c>ProceduralMeshFactory.CreateBox</c> exactly (6 faces x 4 vertices, one normal per face),
/// just expressed directly in XYZ instead of RUF.
/// </summary>
internal static class GizmoMeshFactory
{
    private static readonly Vector4 DefaultTint = new(0.4f, 0.65f, 0.95f, 1f);

    internal static SimpleMesh Create(float size = 0.3f, Vector4? tint = null)
    {
        if (size <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "size must be positive.");
        }

        float half = size / 2f;

        (Vector3 Normal, Vector3 U, Vector3 V)[] faces =
        [
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),      // +Z
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),    // -Z
            (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),     // +X
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),     // -X
            (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),     // +Y
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),     // -Y
        ];

        Vector3[] positions = new Vector3[faces.Length * 4];
        Vector3[] normals = new Vector3[faces.Length * 4];
        uint[] indices = new uint[faces.Length * 6];

        for (int face = 0; face < faces.Length; face++)
        {
            (Vector3 normal, Vector3 u, Vector3 v) = faces[face];
            Vector3 center = normal * half;
            Vector3 uHalf = u * half;
            Vector3 vHalf = v * half;
            int vertexBase = face * 4;

            positions[vertexBase + 0] = center - uHalf - vHalf;
            positions[vertexBase + 1] = center + uHalf - vHalf;
            positions[vertexBase + 2] = center + uHalf + vHalf;
            positions[vertexBase + 3] = center - uHalf + vHalf;

            for (int i = 0; i < 4; i++)
            {
                normals[vertexBase + i] = normal;
            }

            int indexBase = face * 6;
            indices[indexBase + 0] = (uint)(vertexBase + 0);
            indices[indexBase + 1] = (uint)(vertexBase + 1);
            indices[indexBase + 2] = (uint)(vertexBase + 2);
            indices[indexBase + 3] = (uint)(vertexBase + 0);
            indices[indexBase + 4] = (uint)(vertexBase + 2);
            indices[indexBase + 5] = (uint)(vertexBase + 3);
        }

        (Vector3 boundsCenter, float boundsRadius) = SimpleMesh.ComputeBoundingSphere(positions);

        return new SimpleMesh
        {
            Positions = positions,
            Normals = normals,
            Indices = indices,
            BaseColorFactor = tint ?? DefaultTint,
            BoundsCenter = boundsCenter,
            BoundsRadius = boundsRadius,
        };
    }
}
