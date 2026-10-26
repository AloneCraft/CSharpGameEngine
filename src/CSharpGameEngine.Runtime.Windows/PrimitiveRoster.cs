using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>Builds L4 mesh/material/object submissions from authored L5 primitive vocabulary.</summary>
public static class PrimitiveRoster {
    private static readonly Vector4 DefaultBaseColorFactor=new(0.6f,0.6f,0.6f,1f);
    /// <summary>
    /// Same PrimitiveType deduplicates to one shared StageMeshCreateInfo (mirroring how the .glb
    /// import path already separates Meshes from Objects), so N boxes cost one GPU mesh buffer.
    /// Every primitive shares one flat default material - per-primitive color/material is out of
    /// scope for Phase 6.
    /// </summary>
    public static (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)Build(
        IReadOnlyList<PrimitiveType>Types,int MeshIndexOffset,int MaterialIndexOffset){
        var DistinctTypes=new List<PrimitiveType>();
        var MeshIndexByType=new Dictionary<PrimitiveType,int>();
        foreach(var Type in Types){
            if(!MeshIndexByType.ContainsKey(Type)){
                MeshIndexByType[Type]=DistinctTypes.Count;
                DistinctTypes.Add(Type);
            }
        }
        var Meshes=new StageMeshCreateInfo[DistinctTypes.Count];
        for(var Index=0;Index<DistinctTypes.Count;Index++){
            var Mesh=ToStageMesh(CreateProceduralMesh(DistinctTypes[Index]));
            // M0110: a primitive is made here, not by the build, so its distance field is made here too (once per process).
            Meshes[Index]=Mesh with { DistanceField=PrimitiveDistanceFields.GetOrAdd(DistinctTypes[Index],_=>MeshDistanceFieldBuilder.Build(Mesh.Positions,Mesh.Indices)) };
        }
        StageMaterialCreateInfo[]Materials=[
            new StageMaterialCreateInfo { BaseColorFactor=DefaultBaseColorFactor,RoughnessFactor=1f,MetallicFactor=0f },
        ];
        var Objects=new StageObjectCreateInfo[Types.Count];
        for(var Index=0;Index<Types.Count;Index++){
            Objects[Index]=new StageObjectCreateInfo {
                MeshIndex=MeshIndexOffset+MeshIndexByType[Types[Index]],
                MaterialIndex=MaterialIndexOffset,
            };
        }
        return(Meshes,Materials,Objects);
    }
    /// <summary>Unit-size dimensions per shape (Transform.Scale=(1,1,1) fits roughly one unit), following Unity's GameObject.CreatePrimitive defaults (e.g. Cylinder/Cone/Capsule height=2, radius=0.5f).</summary>
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
    /// <summary>UVs stay zero for the shared untextured material; tangent synthesis is shared with single-mesh assembly.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<PrimitiveType,MeshDistanceField?>PrimitiveDistanceFields=new();
    private static StageMeshCreateInfo ToStageMesh(ProceduralMeshData Mesh){
        var VertexCount=Mesh.Positions.Length;
        var Uvs=new Vector2[VertexCount];
        var Tangents=new Vector4[VertexCount];
        for(var Index=0;Index<VertexCount;Index++){
            Tangents[Index]=StageMeshGeometry.ArbitraryTangentFor(Mesh.Normals[Index]);
        }
        return new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Mesh.Indices };
    }
}
