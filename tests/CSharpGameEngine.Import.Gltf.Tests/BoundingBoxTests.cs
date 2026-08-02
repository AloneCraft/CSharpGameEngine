using System.Numerics;

namespace CSharpGameEngine.Import.Gltf.Tests;

public class BoundingBoxTests
{
    [Fact]
    public void FromPoints_ComputesCenterExtentsAndRadius()
    {
        Vector3[] points =
        [
            new Vector3(-1, -2, -3),
            new Vector3(1, 2, 3),
            new Vector3(0, 0, 0),
        ];

        BoundingBox box = BoundingBox.FromPoints(points);

        Assert.Equal(new Vector3(-1, -2, -3), box.Min);
        Assert.Equal(new Vector3(1, 2, 3), box.Max);
        Assert.Equal(Vector3.Zero, box.Center);
        Assert.Equal(new Vector3(1, 2, 3), box.Extents);
        Assert.Equal(new Vector3(1, 2, 3).Length(), box.BoundingSphereRadius, precision: 5);
    }

    [Fact]
    public void FromPoints_EmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() => BoundingBox.FromPoints([]));
    }

    [Fact]
    public void FromPoints_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BoundingBox.FromPoints(null!));
    }
}
