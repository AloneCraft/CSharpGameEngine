using System.Numerics;

namespace CSharpGameEngine.Import.Fbx.Tests;

/// <summary>
/// ADR-0027 regression guard: proves the conversion function itself is correct without depending on
/// a hand-authored asymmetric .fbx binary fixture (Box.fbx is symmetric, so
/// Import_BoxFbx_BoundingBox_IsPlausible cannot detect an axis regression on its own).
/// </summary>
public class FbxAxisConversionTests
{
    [Fact]
    public void ConvertVector_NegatesZ()
    {
        Vector3 converted = FbxAxisConversion.ConvertVector(new Vector3(1f, 2f, 3f));

        Assert.Equal(new Vector3(1f, 2f, -3f), converted);
    }

    [Fact]
    public void ConvertVector_LeavesXAndYUnaffected_OnlyZChanges()
    {
        Vector3 converted = FbxAxisConversion.ConvertVector(Vector3.UnitX);
        Assert.Equal(Vector3.UnitX, converted);

        converted = FbxAxisConversion.ConvertVector(Vector3.UnitY);
        Assert.Equal(Vector3.UnitY, converted);

        converted = FbxAxisConversion.ConvertVector(Vector3.UnitZ);
        Assert.Equal(-Vector3.UnitZ, converted);
    }

    [Fact]
    public void FlipTriangleWinding_SwapsSecondAndThirdIndexOfEachTriangle()
    {
        List<uint> indices = [0, 1, 2, 3, 4, 5];

        FbxAxisConversion.FlipTriangleWinding(indices);

        Assert.Equal<IEnumerable<uint>>([0, 2, 1, 3, 5, 4], indices);
    }
}
