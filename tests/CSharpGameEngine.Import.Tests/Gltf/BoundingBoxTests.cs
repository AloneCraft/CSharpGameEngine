using System.Numerics;

namespace CSharpGameEngine.Import.Gltf.Tests;

public class BoundingBoxTests {
    [Fact]
    public void FromPoints_ComputesCenterExtentsAndRadius(){
        Vector3[]Points=[
            new Vector3(-1,-2,-3),
            new Vector3(1,2,3),
            new Vector3(0,0,0),
        ];

        var Box=BoundingBox.FromPoints(Points);

        Assert.Equal(new Vector3(-1,-2,-3),Box.Min);
        Assert.Equal(new Vector3(1,2,3),Box.Max);
        Assert.Equal(Vector3.Zero,Box.Center);
        Assert.Equal(new Vector3(1,2,3),Box.Extents);
        Assert.Equal(new Vector3(1,2,3).Length(),Box.BoundingSphereRadius,precision:5);
    }

    [Fact]
    public void FromPoints_EmptyList_Throws(){
        Assert.Throws<ArgumentException>(()=>BoundingBox.FromPoints([]));
    }

    [Fact]
    public void FromPoints_NullList_Throws(){
        Assert.Throws<ArgumentNullException>(()=>BoundingBox.FromPoints(null!));
    }
}
