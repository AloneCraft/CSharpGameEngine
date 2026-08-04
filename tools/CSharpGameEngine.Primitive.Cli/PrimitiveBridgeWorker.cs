using System.Numerics;
using CSharpGameEngine.Geometry;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace CSharpGameEngine.Primitive.Cli;

/// <summary>
/// The actual generation work, extracted from <c>Program.cs</c> (ADR-0023) so both one-shot mode
/// and the resident server (<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>) share
/// identical logic. Each call builds its own local <see cref="MeshBuilder{TvG,TvM,TvS}"/>/
/// <see cref="SceneBuilder"/> with no shared/static state, so unlike
/// <c>Import.Fbx.Cli</c>'s <c>FbxBridgeWorker</c>, concurrent resident-mode requests need no
/// serialization here.
/// </summary>
internal static class PrimitiveBridgeWorker
{
    public static (int ExitCode, string Output) Run(string primitiveName, string outputPath)
    {
        // Fixed default dimensions - no size/segment-count configuration UI exists yet (ADR-0021); a
        // future Inspector-driven "add with parameters" flow can extend this switch. "Rectangle"
        // reuses the existing CreatePlane (already used for the ground plane elsewhere) rather than
        // a new method.
        ProceduralMeshData? mesh = primitiveName switch
        {
            "Box" => ProceduralMeshFactory.CreateBox(new Vector3(1f, 1f, 1f)),
            "Sphere" => ProceduralMeshFactory.CreateSphere(0.5f),
            "Cylinder" => ProceduralMeshFactory.CreateCylinder(0.5f, 1f),
            "Capsule" => ProceduralMeshFactory.CreateCapsule(0.5f, 2f),
            "Cone" => ProceduralMeshFactory.CreateCone(0.5f, 1f),
            "Torus" => ProceduralMeshFactory.CreateTorus(0.5f, 0.2f),
            "Rectangle" => ProceduralMeshFactory.CreatePlane(1f, 1f),
            "Disk" => ProceduralMeshFactory.CreateDisk(0.5f),
            _ => null,
        };

        if (mesh is null)
        {
            return (1, $"Unknown primitive name: {primitiveName}");
        }

        SaveAsGlb(mesh.Value, primitiveName, outputPath);
        return (0, string.Empty);
    }

    private static void SaveAsGlb(ProceduralMeshData mesh, string meshName, string outputPath)
    {
        MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty> meshBuilder = new(meshName);
        var primitive = meshBuilder.UsePrimitive(new MaterialBuilder(meshName).WithMetallicRoughness());

        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            uint ia = mesh.Indices[i];
            uint ib = mesh.Indices[i + 1];
            uint ic = mesh.Indices[i + 2];

            VertexPositionNormal va = new(mesh.Positions[ia], mesh.Normals[ia]);
            VertexPositionNormal vb = new(mesh.Positions[ib], mesh.Normals[ib]);
            VertexPositionNormal vc = new(mesh.Positions[ic], mesh.Normals[ic]);

            primitive.AddTriangle(va, vb, vc);
        }

        SceneBuilder sceneBuilder = new();
        sceneBuilder.AddRigidMesh(meshBuilder, Matrix4x4.Identity);
        sceneBuilder.ToGltf2().SaveGLB(outputPath);
    }
}
