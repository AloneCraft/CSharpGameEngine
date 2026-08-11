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
internal static class PrimitiveBridgeWorker {
    public static(int ExitCode,string Output)Run(string PrimitiveName,string OutputPath){
        // Fixed default dimensions - no size/segment-count configuration UI exists yet (ADR-0021); a
        // future Inspector-driven "add with parameters" flow can extend this switch. "Rectangle"
        // reuses the existing CreatePlane (already used for the ground plane elsewhere) rather than
        // a new method.
        ProceduralMeshData?Mesh=PrimitiveName switch {
            "Box"=>ProceduralMeshFactory.CreateBox(new Vector3(1f,1f,1f)),
            "Sphere"=>ProceduralMeshFactory.CreateSphere(0.5f),
            "Cylinder"=>ProceduralMeshFactory.CreateCylinder(0.5f,1f),
            "Capsule"=>ProceduralMeshFactory.CreateCapsule(0.5f,2f),
            "Cone"=>ProceduralMeshFactory.CreateCone(0.5f,1f),
            "Torus"=>ProceduralMeshFactory.CreateTorus(0.5f,0.2f),
            "Rectangle"=>ProceduralMeshFactory.CreatePlane(1f,1f),
            "Disk"=>ProceduralMeshFactory.CreateDisk(0.5f),
            _ =>null,
        };

        if(Mesh is null){
            return(1, $"Unknown primitive name: {PrimitiveName}");
        }

        SaveAsGlb(Mesh.Value,PrimitiveName,OutputPath);
        return(0,string.Empty);
    }

    private static void SaveAsGlb(ProceduralMeshData Mesh,string MeshName,string OutputPath){
        MeshBuilder<VertexPositionNormal,VertexEmpty,VertexEmpty>meshBuilder=new(MeshName);
        var primitive=meshBuilder.UsePrimitive(new MaterialBuilder(MeshName).WithMetallicRoughness());

        for(var i=0;i+2<Mesh.Indices.Length;i+=3){
            var Ia=Mesh.Indices[i];
            var Ib=Mesh.Indices[i+1];
            var Ic=Mesh.Indices[i+2];

            VertexPositionNormal Va=new(Mesh.Positions[Ia],Mesh.Normals[Ia]);
            VertexPositionNormal Vb=new(Mesh.Positions[Ib],Mesh.Normals[Ib]);
            VertexPositionNormal Vc=new(Mesh.Positions[Ic],Mesh.Normals[Ic]);

            primitive.AddTriangle(Va,Vb,Vc);
        }

        SceneBuilder sceneBuilder=new();
        sceneBuilder.AddRigidMesh(meshBuilder,Matrix4x4.Identity);
        sceneBuilder.ToGltf2().SaveGLB(OutputPath);
    }
}
