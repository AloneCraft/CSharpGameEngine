using System;
using System.Numerics;
namespace CSharpGameEngine.VSExtension;
/// <summary>メッシュの統計（Static Mesh Editor の Details に表示する）。</summary>
internal sealed class AssetMeshStatistics {
    internal int VertexCount { get;init;}
    internal int TriangleCount { get;init;}
    internal Vector3 BoundsMin { get;init;}
    internal Vector3 BoundsMax { get;init;}
    internal Vector3 Size=>this.BoundsMax-this.BoundsMin;
    internal Vector4 BaseColor { get;init;}
    internal static AssetMeshStatistics From(SimpleMesh Mesh){
        if(Mesh is null){ throw new ArgumentNullException(nameof(Mesh)); }
        var Min=Vector3.Zero;
        var Max=Vector3.Zero;
        if(Mesh.Positions.Length>0){
            Min=Mesh.Positions[0];
            Max=Mesh.Positions[0];
            foreach(var Position in Mesh.Positions){
                Min=Vector3.Min(Min,Position);
                Max=Vector3.Max(Max,Position);
            }
        }
        return new AssetMeshStatistics {
            VertexCount=Mesh.Positions.Length,
            TriangleCount=Mesh.Indices.Length/3,
            BoundsMin=Min,
            BoundsMax=Max,
            BaseColor=Mesh.BaseColorFactor,
        };
    }
}
