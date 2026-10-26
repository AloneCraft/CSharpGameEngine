using System.Numerics;

namespace CSharpGameEngine.Import.Fbx.Tests;

/// <summary>
/// ADR-0027 の回帰ガード：手作業で作成した非対称 .fbx バイナリフィクスチャに依存せず、変換関数自体が正しいことを証明する（Box.fbx は対称なので、Import_BoxFbx_BoundingBox_IsPlausible だけでは軸の回帰を検出できない）。
/// </summary>
public class FbxAxisConversionTests {
    [Fact]
    public void ConvertVector_NegatesZ(){
        var Converted=FbxAxisConversion.ConvertVector(new Vector3(1f,2f,3f));

        Assert.Equal(new Vector3(1f,2f,-3f),Converted);
    }

    [Fact]
    public void ConvertVector_LeavesXAndYUnaffected_OnlyZChanges(){
        var Converted=FbxAxisConversion.ConvertVector(Vector3.UnitX);
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
