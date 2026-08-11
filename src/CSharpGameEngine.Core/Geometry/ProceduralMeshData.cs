using System.Numerics;

namespace CSharpGameEngine.Geometry;

/// <summary>Plain positions/normals/indices, ready to feed directly into a renderer - no importer needed for these primitive shapes.</summary>
public readonly struct ProceduralMeshData {
    public required Vector3[]Positions { get; init;}

    public required Vector3[]Normals { get; init;}

    public required uint[]Indices { get; init;}
}
