using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md の「Import Tests」。各ソースツールの生 XYZ 値を <c>FromXyz</c>
///  で RUF に変換する。軸変換と単位変換は順序を入れ替えても結果が同じ。Docs/Specifications/Spatial-RUF.md の「Import/Export」にある「External
///  axis conversion -&gt; Unit conversion -&gt; Semantic RUF normalization」の順序を確認する。このマイルストーンの範囲外である
/// ファイル全体のインポーターではなく、変換関数自体だけをテストする。
/// </summary>
public class ImportAxisConversionTests {
    [Fact]
    public void Unity_ImportedForwardAxis_BecomesRufForward()
        =>Assert.Equal(RufVector.Forward,UnityCoordinateConvention.FromXyz(new XyzVector(0f,0f,1f)));

    [Fact]
    public void Unreal_ImportedForwardAxis_BecomesRufForward()
        =>Assert.Equal(RufVector.Forward,UnrealCoordinateConvention.FromXyz(new XyzVector(1f,0f,0f)));

    [Fact]
    public void Blender_ImportedForwardAxis_BecomesRufForward()
        =>Assert.Equal(RufVector.Forward,BlenderWorldConvention.FromXyz(new XyzVector(0f,1f,0f)));

    [Fact]
    public void Unity_AxisConversionAndUnitConversion_AreOrderIndependent()=>AssertOrderIndependent<UnityCoordinateConvention>();

    [Fact]
    public void Unreal_AxisConversionAndUnitConversion_AreOrderIndependent()=>AssertOrderIndependent<UnrealCoordinateConvention>();

    [Fact]
    public void Blender_AxisConversionAndUnitConversion_AreOrderIndependent()=>AssertOrderIndependent<BlenderWorldConvention>();

    private static void AssertOrderIndependent<TConvention>()
        where TConvention:struct,ICoordinateConvention {
        XyzVector RawCentimeters=new(300f,-150f,75f);
        var MetersPerCentimeter=UnitConversion.MetersFromCentimeters(1f);

        var AxisFirstThenUnit=TConvention.FromXyz(RawCentimeters)*MetersPerCentimeter;

        XyzVector UnitFirst=new(
            UnitConversion.MetersFromCentimeters(RawCentimeters.X),
            UnitConversion.MetersFromCentimeters(RawCentimeters.Y),
            UnitConversion.MetersFromCentimeters(RawCentimeters.Z));
        var UnitFirstThenAxis=TConvention.FromXyz(UnitFirst);

        Assert.Equal(AxisFirstThenUnit.R,UnitFirstThenAxis.R,precision:4);
        Assert.Equal(AxisFirstThenUnit.U,UnitFirstThenAxis.U,precision:4);
        Assert.Equal(AxisFirstThenUnit.F,UnitFirstThenAxis.F,precision:4);
    }
}
