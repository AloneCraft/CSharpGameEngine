using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.EditorPreview.Host;
internal static class RenderPreviewModelBounds {
    internal static RenderPreviewAssetBounds Calculate(IReadOnlyList<StageMeshCreateInfo>Meshes,ReadOnlySpan<StageModelAssets.Placement>Placements,ReadOnlySpan<Matrix4x4>Palette,bool IncludeSkinPadding){
        var Min=new Vector3(float.PositiveInfinity);var Max=new Vector3(float.NegativeInfinity);var Magnitude=1f;var Found=false;var Referenced=new HashSet<int>();
        foreach(var Placement in Placements){
            var Mesh=Meshes[Placement.MeshIndex];Referenced.Add(Placement.MeshIndex);
            for(var Vertex=0;Vertex<Mesh.Positions.Count;Vertex++){
                var Position=Mesh.Positions[Vertex];
                if(Mesh.Skin is{}Skin){
                    var Accumulated=Vector3.Zero;
                    for(var Slot=0;Slot<8;Slot++){
                        var Index=Vertex*8+Slot;var Weight=Skin.Weights[Index];if(Weight<=0f){ continue; }
                        var Joint=Skin.JointIndices[Index];if(Joint>=Palette.Length){ throw new InvalidDataException("A skeletal vertex references an invalid joint."); }
                        var Part=Vector3.Transform(Position,Palette[Joint]);var WorldPart=Vector3.Transform(Part,Placement.LocalTransform);Validate(WorldPart);
                        Magnitude=Math.Max(Magnitude,Math.Max(Math.Abs(WorldPart.X),Math.Max(Math.Abs(WorldPart.Y),Math.Abs(WorldPart.Z))));Accumulated+=Part*Weight;
                    }
                    Position=Accumulated;
                }
                var World=Vector3.Transform(Position,Placement.LocalTransform);Validate(World);Min=Vector3.Min(Min,World);Max=Vector3.Max(Max,World);Found=true;
            }
        }
        if(!Found){ throw new InvalidDataException("The preview model has no vertices."); }
        if(IncludeSkinPadding){ var Padding=new Vector3(Magnitude*0.00025f);Min-=Padding;Max+=Padding; }
        var Vertices=0;var Triangles=0;
        foreach(var Index in Referenced){ Vertices=checked(Vertices+Meshes[Index].Positions.Count);Triangles=checked(Triangles+Meshes[Index].Indices.Count/3); }
        return new(){Min=[Min.X,Min.Y,Min.Z],Max=[Max.X,Max.Y,Max.Z],VertexCount=Vertices,TriangleCount=Triangles};
    }
    private static void Validate(Vector3 Value){
        if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)||!float.IsFinite(Value.Z)){ throw new InvalidDataException("The preview model contains a non-finite transformed vertex."); }
    }
}
