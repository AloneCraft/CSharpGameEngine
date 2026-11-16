using System.Collections.Immutable;
using System.Numerics;
namespace CSharpGameEngine.Content.Models;
public readonly record struct ModelTriangleGeometry(ImmutableArray<Vector3>Positions,ImmutableArray<int>Indices);
public static class ModelGeometry {
    public static ModelTriangleGeometry FlattenStatic(ModelAssetBundle Bundle){
        var World=new Matrix4x4[Bundle.Model.Nodes.Length];
        var Positions=ImmutableArray.CreateBuilder<Vector3>();var Indices=ImmutableArray.CreateBuilder<int>();
        for(var NodeIndex=0;NodeIndex<World.Length;NodeIndex++){
            var Node=Bundle.Model.Nodes[NodeIndex];
            World[NodeIndex]=Node.Local.ToMatrix()*(Node.ParentIndex<0?Matrix4x4.Identity:World[Node.ParentIndex]);
            if(Node.MeshIndex<0){ continue; }
            var Mesh=Bundle.Meshes[Node.MeshIndex];var Offset=Positions.Count;
            foreach(var Position in Mesh.Positions){ Positions.Add(Vector3.Transform(Position,World[NodeIndex])); }
            foreach(var Index in Mesh.Indices){ Indices.Add(checked(Offset+(int)Index)); }
        }
        return new(Positions.ToImmutable(),Indices.ToImmutable());
    }
}
