using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Runtime.Physics;
/// <summary>
/// <see cref="StaticMesh"/> ノードの描画三角形を使って衝突させる（ADR-0067）。レベル形状で Unreal の complex-as-simple 衝突を使うのと同様。
/// 三角形はアセットとワールド配置ごとに一度調理し、ノードのワールド変換全体（スケールを含む）を反映して、静的三角形メッシュのボディとして登録する。同じ Entity に
/// <see cref="Collider"/> を併用しないこと。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Spatial,22)]
public struct StaticMeshCollider() {
    public PhysicsLayer Layer;
    /// <summary>表面の摩擦と反発（UE のメッシュ物理マテリアル、ADR-0078/M0109）。車両のタイヤは走行中の面の摩擦を読む。</summary>
    public PhysicsMaterial Material=new();
}
/// <summary>StaticMesh の三角形を取り込み、ワールド配置済み三角形メッシュの形状アセットに調理する。</summary>
internal sealed class StaticMeshCollisionCooker {
    private readonly Dictionary<string,(Vector3[]Positions,int[]Indices)>Imported=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string>ShapeIdByPlacement=[];
    private readonly Dictionary<(string AssetPath,LogicalAffineTransform World),string>ShapeIdByTransform=[];
    /// <summary>
    /// <paramref name="World"/> に配置されたメッシュを保持する三角形メッシュアセットの ID を <paramref name="Shapes"/> から返す。初回使用時に調理する。
    /// アセットを読み込めない、または使用可能な三角形がない場合は false。
    /// </summary>
    internal bool TryEnsure(PhysicsShapeCatalog Shapes,string AssetPath,in LogicalAffineTransform World,out string Id,out string Error){
        // 毎ティック、すべての静的メッシュを再確認する。配置文字列の書式化では12個の
        // Q64.64 値を decimal に変換するため、まず値で配置を照合する。
        if(this.ShapeIdByTransform.TryGetValue((AssetPath,World),out Id!)){
            Error=string.Empty;
            return true;
        }
        if(!this.TryEnsureByPlacement(Shapes,AssetPath,World,out Id,out Error)){
            return false;
        }
        this.ShapeIdByTransform[(AssetPath,World)]=Id;
        return true;
    }
    private bool TryEnsureByPlacement(PhysicsShapeCatalog Shapes,string AssetPath,in LogicalAffineTransform World,out string Id,out string Error){
        var FullPath=CSharpGameEngine.Entities.ContentRoot.Resolve(AssetPath);
        var Placement=FullPath+"|"+World;
        Error=string.Empty;
        if(this.ShapeIdByPlacement.TryGetValue(Placement,out Id!)){
            return true;
        }
        if(!this.TryImport(FullPath,out var Positions,out var Indices,out Error)){
            return false;
        }
        LogicalVector3[]Vertices=new LogicalVector3[Positions.Length];
        for(var Index=0;Index<Positions.Length;Index++){
            var Local=Positions[Index];
            if(!LogicalScalar.TryFromSingle(Local.X,out var R)||!LogicalScalar.TryFromSingle(Local.Y,out var U)||!LogicalScalar.TryFromSingle(Local.Z,out var F)
                ||!World.TryTransformPoint(new LogicalVector3(R,U,F),out Vertices[Index])){
                Error=$"StaticMesh '{AssetPath}' does not fit the logical coordinate range at this placement.";
                return false;
            }
        }
        List<int>Kept=new(Indices.Length);
        for(var Index=0;Index<Indices.Length;Index+=3){
            var A=Vertices[Indices[Index]];
            if(!LogicalVector3.Cross(Vertices[Indices[Index+1]]-A,Vertices[Indices[Index+2]]-A).LengthSquared.IsZero){
                Kept.Add(Indices[Index]);
                Kept.Add(Indices[Index+1]);
                Kept.Add(Indices[Index+2]);
            }
        }
        if(Kept.Count==0){
            Error=$"StaticMesh '{AssetPath}' has no non-degenerate triangle.";
            return false;
        }
        Id="static-mesh:"+Path.GetFileName(FullPath)+":"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Placement)),0,8);
        if(!Shapes.TryGet(Id,out _)){
            Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Kept));
        }
        this.ShapeIdByPlacement[Placement]=Id;
        return true;
    }
    private bool TryImport(string FullPath,out Vector3[]Positions,out int[]Indices,out string Error){
        Error=string.Empty;
        if(this.Imported.TryGetValue(FullPath,out var Cached)){
            (Positions,Indices)=Cached;
            return true;
        }
        Positions=[];
        Indices=[];
        var Result=GltfImporter.ImportScene(FullPath);
        if(!Result.Success||Result.Scene is not { }Scene){
            Error=$"StaticMesh '{FullPath}' could not be imported: {string.Join("; ",Result.Diagnostics)}";
            return false;
        }
        List<Vector3>AllPositions=[];
        List<int>AllIndices=[];
        foreach(var Node in Scene.Nodes){
            if(Node.MeshIndex is not int MeshIndex){
                continue;
            }
            var Mesh=Scene.Meshes[MeshIndex];
            var Offset=AllPositions.Count;
            foreach(var Position in Mesh.Positions){
                AllPositions.Add(Vector3.Transform(Position,Node.WorldTransform));
            }
            foreach(var Index in Mesh.Indices){
                AllIndices.Add(Offset+(int)Index);
            }
        }
        Positions=[.. AllPositions];
        Indices=[.. AllIndices];
        this.Imported[FullPath]=(Positions,Indices);
        return true;
    }
}
[SystemPhase(SystemPhase.PhysicsSimulation)]
[ExecuteInEditor(Realtime=true)]
[RunsAfter(typeof(PhysicsBodySync))]
internal struct StaticMeshColliderSync:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref StaticMesh Mesh,ref StaticMeshCollider MeshCollider,IPhysicsHost Host,ref State State){
        PhysicsDomainRuntime Domain=Host.Physics.Active;
        if(Transform.Frame!=Host.Physics.Frame){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.ReferenceFrameMismatch);
            return;
        }
        if(State.World.TryGetComponent(Entity,out Collider _)){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.InvalidShape);
            return;
        }
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out FixedAffineTransform World)!=FixedTransformResolveStatus.Success){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.InvalidHierarchy);
            return;
        }
        if(!Domain.StaticMeshes.TryEnsure(Domain.MutableWorld.Shapes,Mesh.AssetPath,World.Logical,out string Id,out _)){
            Domain.ReportSyncFailure(Entity,PhysicsFaultType.InvalidShape);
            return;
        }
        Domain.ApplyBody(new PhysicsBodyDescription(
            Entity,PhysicsDomainRuntime.ResolveStableId(Entity,State.World),
            new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id,Layer=MeshCollider.Layer,Material=MeshCollider.Material },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));
    }
}
