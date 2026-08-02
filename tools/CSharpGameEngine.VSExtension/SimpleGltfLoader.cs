using System;
using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Schema2;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Minimal glTF loader for the VSIX in-process renderer (ADR-0008): first mesh/primitive only,
/// position + normal + indices + baseColorFactor, no UV/texture/PBR support, no missing-NORMAL
/// fallback synthesis (the bundled Box.glb sample always has NORMAL). Deliberately not a reuse of
/// <c>CSharpGameEngine.Import.Gltf</c> - that project is net10.0 and cannot be referenced from
/// this net472 project; this loader stays fully decoupled, referencing only
/// <c>SharpGLTF.Core</c>'s netstandard2.0 build directly.
/// </summary>
internal static class SimpleGltfLoader
{
    public static SimpleMesh Load(string filePath)
    {
        ModelRoot model = ModelRoot.Load(filePath);
        if (model.LogicalMeshes.Count == 0)
        {
            throw new InvalidOperationException("glTF file contains no meshes.");
        }

        Mesh mesh = model.LogicalMeshes[0];
        if (mesh.Primitives.Count == 0)
        {
            throw new InvalidOperationException("glTF mesh contains no primitives.");
        }

        MeshPrimitive primitive = mesh.Primitives[0];

        Accessor positionAccessor = primitive.GetVertexAccessor("POSITION")
            ?? throw new InvalidOperationException("glTF primitive has no POSITION attribute.");
        Vector3[] positions = [.. positionAccessor.AsVector3Array()];

        Accessor normalAccessor = primitive.GetVertexAccessor("NORMAL")
            ?? throw new InvalidOperationException("glTF primitive has no NORMAL attribute (not supported by this minimal loader).");
        Vector3[] normals = [.. normalAccessor.AsVector3Array()];
        if (normals.Length != positions.Length)
        {
            throw new InvalidOperationException("glTF NORMAL attribute count does not match POSITION count.");
        }

        Material? material = primitive.Material;
        MaterialChannel? baseColorChannel = material?.FindChannel("BaseColor");
        Vector4 baseColorFactor = baseColorChannel?.Color ?? new Vector4(1f, 1f, 1f, 1f);

        List<(int A, int B, int C)> triangles = [.. primitive.GetTriangleIndices()];
        if (triangles.Count == 0)
        {
            throw new InvalidOperationException("glTF primitive has no triangles.");
        }

        uint[] indices = new uint[triangles.Count * 3];
        for (int i = 0; i < triangles.Count; i++)
        {
            indices[(i * 3) + 0] = (uint)triangles[i].A;
            indices[(i * 3) + 1] = (uint)triangles[i].B;
            indices[(i * 3) + 2] = (uint)triangles[i].C;
        }

        (Vector3 center, float radius) = SimpleMesh.ComputeBoundingSphere(positions);

        return new SimpleMesh
        {
            Positions = positions,
            Normals = normals,
            Indices = indices,
            BaseColorFactor = baseColorFactor,
            BoundsCenter = center,
            BoundsRadius = radius,
        };
    }
}
