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

        Vector3 min=Points[0];
        Vector3 max=Points[0];

        for(var i=1;i<Points.Count;i++){
            min=Vector3.Min(min,Points[i]);
            max=Vector3.Max(max,Points[i]);
        }

        return new BoundingBox(min,max);
    }
}
