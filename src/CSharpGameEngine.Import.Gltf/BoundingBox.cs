using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;

    public Vector3 Extents => (Max - Min) * 0.5f;

    public float BoundingSphereRadius => Extents.Length();

    public static BoundingBox FromPoints(IReadOnlyList<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("Cannot compute a bounding box from an empty point list.", nameof(points));
        }

        Vector3 min = points[0];
        Vector3 max = points[0];

        for (int i = 1; i < points.Count; i++)
        {
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }

        return new BoundingBox(min, max);
    }
}
