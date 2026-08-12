using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

public readonly record struct BoundingBox(Vector3 Min,Vector3 Max){
    public Vector3 Center=>(Min+Max)*0.5f;

    public Vector3 Extents=>(Max-Min)*0.5f;

    public float BoundingSphereRadius=>Extents.Length();

    public static BoundingBox FromPoints(IReadOnlyList<Vector3>Points){
        ArgumentNullException.ThrowIfNull(Points);
        if(Points.Count==0){
            throw new ArgumentException("Cannot compute a bounding box from an empty point list.",nameof(Points));
        }

        Vector3 Min=Points[0];
        Vector3 Max=Points[0];

        for(var I=1;I<Points.Count;I++){
            Min=Vector3.Min(Min,Points[I]);
            Max=Vector3.Max(Max,Points[I]);
        }

        return new BoundingBox(Min,Max);
    }
}
