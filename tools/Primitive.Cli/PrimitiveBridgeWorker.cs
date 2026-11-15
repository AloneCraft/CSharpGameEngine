using System.Numerics;
using CSharpGameEngine.Geometry;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace CSharpGameEngine.Primitive.Cli;

/// <summary>
/// <c>Program.cs</c> から切り出した実際の生成処理 (ADR-0023)。1 回限りのモードと常駐サーバー（<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>）の両方で同じロジックを共有する。呼び出しごとに共有状態や静的状態を持たない独自の <see cref="MeshBuilder{TvG,TvM,TvS}"/>/<see cref="SceneBuilder"/> を構築する。そのため、<c>Import.Fbx.Cli</c> の <c>FbxBridgeWorker</c> と異なり、常駐モードの並行要求をここで直列化する必要はない。
/// </summary>
internal static class PrimitiveBridgeWorker {
    public static(int ExitCode,string Output)Run(string PrimitiveName,string OutputPath){
        // サイズやセグメント数を設定する UI はまだないため、寸法は固定の既定値 (ADR-0021)。
        // 将来、Inspector から「パラメーター付きで追加」する操作を行う場合は、この switch を拡張できる。「Rectangle」は
        // ほかの場所の地面にもすでに使われている既存の CreatePlane を再利用する。
        // 新しいメソッド。
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
        MeshBuilder<VertexPositionNormal,VertexEmpty,VertexEmpty>MeshBuilder=new(MeshName);
        var Primitive=MeshBuilder.UsePrimitive(new MaterialBuilder(MeshName).WithMetallicRoughness());

        for(var I=0;I+2<Mesh.Indices.Length;I+=3){
            var Ia=Mesh.Indices[I];
            var Ib=Mesh.Indices[I+1];
            var Ic=Mesh.Indices[I+2];

            VertexPositionNormal Va=new(Mesh.Positions[Ia],Mesh.Normals[Ia]);
            VertexPositionNormal Vb=new(Mesh.Positions[Ib],Mesh.Normals[Ib]);
            VertexPositionNormal Vc=new(Mesh.Positions[Ic],Mesh.Normals[Ic]);

            Primitive.AddTriangle(Va,Vb,Vc);
        }

        SceneBuilder SceneBuilder=new();
        SceneBuilder.AddRigidMesh(MeshBuilder,Matrix4x4.Identity);
        SceneBuilder.ToGltf2().SaveGLB(OutputPath);
    }
}
