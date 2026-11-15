using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>アセット作成時のL5プリミティブの種類から、L4のメッシュ／マテリアル／オブジェクト送信データを構築する。</summary>
public static class PrimitiveRoster {
    private static readonly Vector4 DefaultBaseColorFactor=new(0.6f,0.6f,0.6f,1f);
    /// <summary>
    /// 同じPrimitiveTypeは1つの共有StageMeshCreateInfoにまとめる（.glbのインポート経路がMeshとObjectを分離するのと同様）。
    /// そのため、BoxをN個作ってもGPUメッシュバッファは1つで済む。すべてのプリミティブで1つのフラットな既定マテリアルを共有する。
    /// Phase 6の範囲にプリミティブごとの色／マテリアルは含めない。
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
            // M0110：プリミティブはビルドではなくここで生成するため、距離場もここで（プロセスごとに一度）作る。
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
    /// <summary>
    /// 形状ごとの単位寸法（Transform.Scale=(1,1,1)でおよそ1単位に収まる）。UnityのGameObject.CreatePrimitive既定値に従う（例：Cylinder／Cone
    /// ／Capsuleの高さは2、半径は0.5f）。
    /// </summary>
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
    /// <summary>共有の非テクスチャマテリアルではUVを0のままにする。接線の生成は単一メッシュ構築処理と共有する。</summary>
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
