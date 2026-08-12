using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>ライブRoslyn SceneModelをScene Designerの描画オブジェクトへ変換する。</summary>
internal static class SceneModelRenderObjectBuilder {
    private const string TransformTypeFullyQualifiedName="global::UserEcsProject.Transform";
    private const string PrimitiveMeshTypeFullyQualifiedName="global::CSharpGameEngine.Runtime.Windows.PrimitiveMesh";
    private const string StaticMeshTypeFullyQualifiedName="global::CSharpGameEngine.Runtime.Windows.StaticMesh";
    private const string PrimitiveBridgeProjectRelativePath=@"tools\CSharpGameEngine.Primitive.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);

    internal static IReadOnlyList<SceneRenderObject>Build(
        SceneModel Model,
        Func<string,SimpleMesh?>ResolvePrimitiveMesh,
        Func<string,SimpleMesh?>?ResolveStaticMesh=null){
        List<SceneRenderObject>Result=[];
        if(Model.Root is null)return Result;

        foreach(SceneNode Node in Model.EnumerateNodes()){
            Matrix4x4 World=ExtractWorldTransform(Node);
            SimpleMesh Mesh;
            if(ExtractStaticMeshAssetPath(Node)is{ }AssetPath&&ResolveStaticMesh is not null){
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
                IncludeInBounds=ExtractStaticMeshAssetPath(Node) is not{ }BoundsAssetPath
                    ||BoundsAssetPath.IndexOf("SkySphere",StringComparison.OrdinalIgnoreCase)<0,
            });
        }
        return Result;
    }

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

    private static Matrix4x4 ExtractWorldTransform(SceneNode Node){
        SceneComponent?Transform=Node.FindComponent(TransformTypeFullyQualifiedName);
        if(Transform is null)return Matrix4x4.Identity;
        Vector3 Position=new(ReadFloat(Transform,"PositionX"),ReadFloat(Transform,"PositionY"),ReadFloat(Transform,"PositionZ"));
        Quaternion Rotation=new(ReadFloat(Transform,"RotationX"),ReadFloat(Transform,"RotationY"),ReadFloat(Transform,"RotationZ"),ReadFloat(Transform,"RotationW"));
        if(Rotation==default)Rotation=Quaternion.Identity;
        Vector3 Scale=new(ReadFloat(Transform,"ScaleX",1f),ReadFloat(Transform,"ScaleY",1f),ReadFloat(Transform,"ScaleZ",1f));
        return Matrix4x4.CreateScale(Scale)*Matrix4x4.CreateFromQuaternion(Rotation)*Matrix4x4.CreateTranslation(Position);
    }

    private static float ReadFloat(SceneComponent Component,string MemberName,float Default=0f)=>
        Component.FindMember(MemberName)?.Value.ConstantValue switch {
            float F=>F,double D=>(float)D,int I=>I,_=>Default,
        };

    private static string?ExtractPrimitiveMeshEnumMemberName(SceneNode Node)=>
        Node.FindComponent(PrimitiveMeshTypeFullyQualifiedName)?.FindMember("Type")?.Value.ReferencedSymbol?.Name;

    private static string?ExtractStaticMeshAssetPath(SceneNode Node)=>
        Node.FindComponent(StaticMeshTypeFullyQualifiedName)?.FindMember("AssetPath")?.Value.ConstantValue as string;
}
