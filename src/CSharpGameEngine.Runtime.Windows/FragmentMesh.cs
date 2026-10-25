using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// Draws one fragment of a cooked prefracture asset (ADR-0078/M0113): the part of one mesh that the fragment is, placed by its own Entity's
/// <see cref="Transform"/> (the fragment's centre and the rotation of the body it rides on). The destruction plugin makes one Entity per fragment and moves
/// it; the renderer adds and removes the objects as those Entities come and go (M0105). A fragment's triangles on the source's surface use
/// <see cref="OuterColor"/>, the ones on a Voronoi cut <see cref="InteriorColor"/> (UE's interior material slot).
/// </summary>
public struct FragmentMesh {
    /// <summary>The asset under the content root (for example <c>Assets/Destruction/Crate.cgefracture</c>).</summary>
    public string AssetPath;
    public int Fragment;
    /// <summary>Linear RGB of the outer surface.</summary>
    public Vector3 OuterColor;
    /// <summary>Linear RGB of the cut faces.</summary>
    public Vector3 InteriorColor;
}
/// <summary>ADR-0078/M0113: gives an Entity that has a <see cref="FragmentMesh"/> and is not drawn yet its renderer objects.</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
[RunsBefore(typeof(TransformSync))]
internal struct FragmentMeshBind:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref FragmentMesh Mesh,EngineHost Host,ref State State){
        Host.Roster?.BindFragmentMesh(Entity,in Transform,in Mesh,Host,ref State);
    }
}
/// <summary>The renderer meshes of a fragment: flat-shaded triangles of one material slot in the fragment's own frame.</summary>
internal static class FragmentMeshBuilder {
    /// <summary>
    /// The triangles of <paramref name="Slot"/>, each with its own three vertices (flat normals from the outward winding, a tangent along the first edge, and
    /// a box projection of the source-frame position for the UVs, so neighbouring fragments' textures meet); null when the slot has no triangle.
    /// </summary>
    internal static StageMeshCreateInfo?Build(PrefractureAsset Asset,int Fragment,byte Slot){
        var Mesh=Asset.Meshes[Fragment];
        var Center=Asset.Fragments[Fragment].LocalCenter;
        List<Vector3>Positions=[];
        List<Vector3>Normals=[];
        List<Vector4>Tangents=[];
        List<Vector2>Uvs=[];
        List<uint>Indices=[];
        for(var Triangle=0;Triangle<Mesh.Slots.Count;Triangle++){
            if(Mesh.Slots[Triangle]!=Slot){ continue; }
            var A=ToVector(Mesh.Vertices[Mesh.Indices[Triangle*3]]);
            var B=ToVector(Mesh.Vertices[Mesh.Indices[Triangle*3+1]]);
            var C=ToVector(Mesh.Vertices[Mesh.Indices[Triangle*3+2]]);
            var Normal=Vector3.Cross(B-A,C-A);
            if(Normal.LengthSquared()<=1e-20f){ continue; }
            Normal=Vector3.Normalize(Normal);
            var Edge=Vector3.Normalize(B-A);
            var Offset=ToVector(Center);
            foreach(var Point in (ReadOnlySpan<Vector3>)[A,B,C]){
                Indices.Add((uint)Positions.Count);
                Positions.Add(Point);
                Normals.Add(Normal);
                Tangents.Add(new Vector4(Edge,1f));
                Uvs.Add(BoxProjection(Point+Offset,Normal));
            }
        }
        if(Indices.Count==0){ return null; }
        return new StageMeshCreateInfo { Positions=Positions,Normals=Normals,Tangents=Tangents,Uvs=Uvs,Indices=Indices };
    }
    private static Vector3 ToVector(LogicalVector3 Value)=>Value.TryToSingle(out var Result)?Result:throw new OverflowException("A fragment vertex does not fit a float.");
    private static Vector2 BoxProjection(Vector3 Point,Vector3 Normal){
        var Absolute=Vector3.Abs(Normal);
        return Absolute.X>=Absolute.Y&&Absolute.X>=Absolute.Z?new Vector2(Point.Z,Point.Y):Absolute.Y>=Absolute.Z?new Vector2(Point.X,Point.Z):new Vector2(Point.X,Point.Y);
    }
}
