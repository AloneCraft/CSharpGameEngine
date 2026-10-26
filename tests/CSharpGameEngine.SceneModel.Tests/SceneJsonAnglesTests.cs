using System.Globalization;
using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.SceneModel.Tests;
/// <summary>ADR-0074：角度は回転数で書き、度数で表示する。どの表記から読み戻しても同じ float 値になる。</summary>
public sealed class SceneJsonAnglesTests {
    [Theory]
    [InlineData(1.5707964f,false,"0.25")]
    [InlineData(-3.1415927f,false,"-0.5")]
    [InlineData(90f,true,"0.25")]
    [InlineData(500f,true,"1.3888889")]
    [InlineData(0f,false,"0")]
    [InlineData(-0f,true,"0")]
    public void Turns_UseTheShortestTextThatReadsBack(float Value,bool InDegrees,string Expected){
        var Unit=InDegrees?SceneAngleUnit.Degrees:SceneAngleUnit.Radians;
        Assert.Equal(Expected,SceneJsonAngles.FormatTurns(Value,Unit));
        Assert.Equal(Value==0f?0f:Value,SceneJsonAngles.FromTurns(Expected,Unit));
    }
    [Theory]
    [InlineData(1.5707964f,false,"90")]
    [InlineData(0.7853982f,false,"45")]
    [InlineData(44.765083f,true,"44.765083")]
    public void Degrees_UseTheShortestTextThatReadsBack(float Value,bool InDegrees,string Expected){
        var Unit=InDegrees?SceneAngleUnit.Degrees:SceneAngleUnit.Radians;
        Assert.Equal(Expected,SceneJsonAngles.FormatDegrees(Value,Unit));
        Assert.Equal(Value,SceneJsonAngles.FromDegrees(Expected,Unit));
    }
    /// <summary>どの float 値も回転数と度数の両方の表記から、正確に読み戻せる。</summary>
    [Fact]
    public void EverySpelling_RoundTripsRandomFloats(){
        var Random=new Random(20261001);
        for(var Index=0;Index<20000;Index++){
            var Value=(float)(Random.NextDouble()*2000d-1000d)*(Index%2==0?1f:0.001f);
            foreach(var Unit in new[]{SceneAngleUnit.Radians,SceneAngleUnit.Degrees}){
                var Turns=SceneJsonAngles.FormatTurns(Value,Unit);
                Assert.Equal(Value,SceneJsonAngles.FromTurns(Turns,Unit));
                Assert.True(SceneJsonAngles.IsNumber(Turns),Turns);
                var Degrees=SceneJsonAngles.FormatDegrees(Value,Unit);
                Assert.Equal(Value,SceneJsonAngles.FromDegrees(Degrees,Unit));
            }
        }
    }
    [Fact]
    public void UnitFromArgument_ReadsTheEnumValue(){
        Assert.Equal(SceneAngleUnit.Radians,SceneJsonAngles.UnitFromArgument((byte)0));
        Assert.Equal(SceneAngleUnit.Degrees,SceneJsonAngles.UnitFromArgument((byte)1));
        Assert.Null(SceneJsonAngles.UnitFromArgument(null));
        Assert.Equal("1.5707964",SceneJsonAngles.FromTurns("0.25",SceneAngleUnit.Radians).ToString("R",CultureInfo.InvariantCulture));
    }
}
