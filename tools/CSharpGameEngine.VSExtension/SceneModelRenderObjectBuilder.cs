using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>ライブRoslyn SceneModelをScene Designerの描画オブジェクトへ変換する。</summary>
internal static class SceneModelRenderObjectBuilder {
    private const string PrimitiveBridgeProjectRelativePath=@"tools\CSharpGameEngine.Primitive.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);

    internal static IReadOnlyList<SceneRenderObject>Build(
        SceneModel Model,
        Func<string,SimpleMesh?>ResolvePrimitiveMesh,
        Func<string,SimpleMesh?>?ResolveStaticMesh=null){
        List<SceneRenderObject>Result=[];
        if(Model.Root is not{ }Root)return Result;

        Append(Root,Matrix4x4.Identity);
        return Result;

        // Model.EnumerateNodes() は親コンテキスト(深さも親も)を返さないため、ここは自前の pre-order
        // 再帰へ切り替える。列挙順はどちらも「自分 → 子を宣言順」なので NodePath の並びは不変。
        // SceneNode.Children は入れ子の ObjectCreationExpression から構築されるツリーであり、JSON の
        // ParentId のような任意参照が存在しないのでサイクルガードは不要。深さも解析器が
        // SceneDslWellKnownNames.MaxRecursionDepth で打ち切っている。
        void Append(SceneNode Node,Matrix4x4 ParentWorld){
            // world = local * parentWorld。SceneTransformMathはこのランタイム規約をEditor側の
            // 読み取り・preview・source write-backで一貫して使う純粋層である。
            Matrix4x4 World=(SceneTransformBinding.TryRead(Node,out SceneTransformValue Local)
                ?SceneTransformMath.ToMatrix(Local)
                :Matrix4x4.Identity)*ParentWorld;

            string?AssetPath=ExtractStaticMeshAssetPath(Node);
            SimpleMesh Mesh;
            if(AssetPath is not null&&ResolveStaticMesh is not null){
                Mesh=ResolveStaticMesh(AssetPath)??GizmoMeshFactory.Create();
            } else if(ExtractPrimitiveMeshEnumMemberName(Node)is{ }EnumMemberName){
                Mesh=ResolvePrimitiveMesh(EnumMemberName)??GizmoMeshFactory.Create();
            } else {
                Mesh=GizmoMeshFactory.Create();
            }

            Result.Add(new SceneRenderObject {
                Mesh=Mesh,
                World=World,
                NodeId=Node.Path,
                IncludeInBounds=!IsSkyDomeAsset(AssetPath),
            });

            foreach(SceneNode Child in Node.Children){
                Append(Child,World);
            }
        }
    }

    /// <summary>Sky dome は描画するが、編集対象ステージの自動フレーミング半径には含めない。</summary>
    private static bool IsSkyDomeAsset(string?AssetPath)=>
        AssetPath is not null&&AssetPath.IndexOf("SkySphere",StringComparison.OrdinalIgnoreCase)>=0;

    internal static SimpleMesh?ResolveViaBridge(string PrimitiveTypeEnumMemberName,string RepoRoot){
        string?BridgeKind=MapToBridgeKind(PrimitiveTypeEnumMemberName);
        if(BridgeKind is null)return null;
        string DirectoryPath=Path.Combine(RepoRoot,"UserEcsProject","Assets","Primitives");
        Directory.CreateDirectory(DirectoryPath);
        string GlbPath=Path.Combine(DirectoryPath,$"{BridgeKind}.glb");
        if(!File.Exists(GlbPath)){
            ExternalBridgeProcess.RunProducingFile(PrimitiveBridgeProjectRelativePath,BridgeKind,GlbPath,RepoRoot,BridgeTimeout,UseResidentServer:true);
        }
        return MeshLoader.Load(GlbPath,RepoRoot);
    }

    internal static SimpleMesh?ResolveStaticMeshViaBridge(string AssetPath,string RepoRoot){
        if(string.IsNullOrWhiteSpace(AssetPath))return null;
        string FullPath=Path.GetFullPath(Path.Combine(RepoRoot,"UserEcsProject",AssetPath));
        return File.Exists(FullPath)?MeshLoader.Load(FullPath,RepoRoot):null;
    }

    private static string?MapToBridgeKind(string Name)=>Name switch {
        "Plane"=>"Rectangle","Box"=>"Box","Sphere"=>"Sphere","Cylinder"=>"Cylinder",
        "Cone"=>"Cone","Torus"=>"Torus","Disk"=>"Disk","Capsule"=>"Capsule",_=>null,
    };

    private static string?ExtractPrimitiveMeshEnumMemberName(SceneNode Node)=>
        SceneComponentBindings.FindComponent(Node,SceneComponentRole.PrimitiveMesh)
            ?.FindMember("Type")?.Value.ReferencedSymbol?.Name;

    private static string?ExtractStaticMeshAssetPath(SceneNode Node)=>
        SceneComponentBindings.FindComponent(Node,SceneComponentRole.StaticMesh)
            ?.FindMember("AssetPath")?.Value.ConstantValue as string;
}
