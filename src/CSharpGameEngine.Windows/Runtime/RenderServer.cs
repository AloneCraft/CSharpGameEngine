using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Graphics.D3D12;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Converts declared <see cref="PrimitiveMesh"/> values into the StageMeshCreateInfo/
/// StageMaterialCreateInfo/StageObjectCreateInfo triples <see cref="StageRenderer"/>'s multi-object
/// Create overload needs, appended onto an existing (e.g. .glb-derived) roster via the
/// MeshIndexOffset/MaterialIndexOffset parameters. A pure function, not a stateful service,
/// matching this repo's universal "the object roster is fixed once at Create time" renderer
/// pattern (ADR-0040 Phase 6) - it has no ECS dependency, the caller owns all Entity/Transform
/// concerns.
/// </summary>
public static class RenderServer {
    private static readonly Vector4 DefaultBaseColorFactor=new(0.6f,0.6f,0.6f,1f);

    /// <summary>
    /// Same PrimitiveType deduplicates to one shared StageMeshCreateInfo (mirroring how the .glb
    /// import path already separates Meshes from Objects), so N boxes cost one GPU mesh buffer.
    /// Every primitive shares one flat default material - per-primitive color/material is out of
    /// scope for Phase 6.
    /// </summary>
    public static (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)BuildPrimitiveRoster(
        IReadOnlyList<PrimitiveType>Types,int MeshIndexOffset,int MaterialIndexOffset){
        List<PrimitiveType>DistinctTypes=[];
        Dictionary<PrimitiveType,int>MeshIndexByType=[];
        foreach(PrimitiveType Type in Types){
            if(!MeshIndexByType.ContainsKey(Type)){
                MeshIndexByType[Type]=DistinctTypes.Count;
                DistinctTypes.Add(Type);
            }
        }

        StageMeshCreateInfo[]Meshes=new StageMeshCreateInfo[DistinctTypes.Count];
        for(var Index=0;Index<DistinctTypes.Count;Index++){
            Meshes[Index]=ToStageMesh(CreateProceduralMesh(DistinctTypes[Index]));
        }

        StageMaterialCreateInfo[]Materials=[
            new StageMaterialCreateInfo { BaseColorFactor=DefaultBaseColorFactor,RoughnessFactor=1f,MetallicFactor=0f },
        ];

        StageObjectCreateInfo[]Objects=new StageObjectCreateInfo[Types.Count];
        for(var Index=0;Index<Types.Count;Index++){
            Objects[Index]=new StageObjectCreateInfo {
                MeshIndex=MeshIndexOffset+MeshIndexByType[Types[Index]],
                MaterialIndex=MaterialIndexOffset,
            };
        }

        return(Meshes,Materials,Objects);
    }

    /// <summary>Unit-size dimensions per shape (Transform.Scale=(1,1,1) fits roughly one unit), following Unity's GameObject.CreatePrimitive defaults (e.g. Cylinder/Cone/Capsule height=2, radius=0.5).</summary>
    private static ProceduralMeshData CreateProceduralMesh(PrimitiveType Type)=>Type switch {
        PrimitiveType.Plane=>ProceduralMeshFactory.CreatePlane(1f,1f),
        PrimitiveType.Box=>ProceduralMeshFactory.CreateBox(Vector3.One),
        PrimitiveType.Sphere=>ProceduralMeshFactory.CreateSphere(0.5f),
        PrimitiveType.Cylinder=>ProceduralMeshFactory.CreateCylinder(0.5f,2f),
        PrimitiveType.Cone=>ProceduralMeshFactory.CreateCone(0.5f,2f),
        PrimitiveType.Torus=>ProceduralMeshFactory.CreateTorus(0.35f,0.15f),
        PrimitiveType.Disk=>ProceduralMeshFactory.CreateDisk(0.5f),
        PrimitiveType.Capsule=>ProceduralMeshFactory.CreateCapsule(0.5f,2f),
        _=>throw new ArgumentOutOfRangeException(nameof(Type),Type,"Unknown PrimitiveType."),
    };

    /// <summary>Synthesizes Uvs (unused - every primitive uses the flat default material, never a real texture) and Tangents via StageRenderer's own untextured-mesh adapter logic, so this stays byte-for-byte consistent with the legacy MeshCreateInfo path instead of a second, divergent implementation.</summary>
    private static StageMeshCreateInfo ToStageMesh(ProceduralMeshData Mesh){
        var VertexCount=Mesh.Positions.Length;
        Vector2[]Uvs=new Vector2[VertexCount];
        Vector4[]Tangents=new Vector4[VertexCount];
        for(var Index=0;Index<VertexCount;Index++){
            Tangents[Index]=StageRenderer.ArbitraryTangentFor(Mesh.Normals[Index]);
        }

        return new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Mesh.Indices };
    }
}
