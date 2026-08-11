using System.Numerics;

namespace CSharpGameEngine.Import.Fbx.Tests;

/// <summary>
/// ADR-0027 regression guard: proves the conversion function itself is correct without depending on
/// a hand-authored asymmetric .fbx binary fixture (Box.fbx is symmetric, so
/// Import_BoxFbx_BoundingBox_IsPlausible cannot detect an axis regression on its own).
/// </summary>
public class FbxAxisConversionTests {
    [Fact]
    public void ConvertVector_NegatesZ(){
        Vector3 Converted=FbxAxisConversion.ConvertVector(new Vector3(1f,2f,3f));

        Assert.Equal(new Vector3(1f,2f,-3f),Converted);
    }

    [Fact]
    public void ConvertVector_LeavesXAndYUnaffected_OnlyZChanges(){
        Vector3 Converted=FbxAxisConversion.ConvertVector(Vector3.UnitX);
        Assert.Equal(Vector3.UnitX,Converted);

        Converted=FbxAxisConversion.ConvertVector(Vector3.UnitY);
        Assert.Equal(Vector3.UnitY,Converted);

        Converted=FbxAxisConversion.ConvertVector(Vector3.UnitZ);
        Assert.Equal(-Vector3.UnitZ,Converted);
    }

    [Fact]
    public void FlipTriangleWinding_SwapsSecondAndThirdIndexOfEachTriangle(){
        List<uint>Indices=[0,1,2,3,4,5];

        FbxAxisConversion.FlipTriangleWinding(Indices);

        Assert.Equal<IEnumerable<uint>>([0,2,1,3,5,4],Indices);
    }
}
