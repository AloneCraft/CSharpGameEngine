using Xunit;
namespace CSharpGameEngine.Numerics.Tests;
public sealed class LogicalNumericTests {
    [Theory]
    [InlineData("-1.25","0.5",-3,"0.25")]
    [InlineData("-1","0.5",-2,"0")]
    [InlineData("1.25","0.5",2,"0.25")]
    [InlineData("0","0.5",0,"0")]
    public void GridIndexUsesFloorAndPositiveRemainder(string Value,string Divisor,long Expected,string Remainder){
        Assert.True(LogicalNumeric.TryDivRemFloor(LogicalScalar.ParseInvariant(Value),LogicalScalar.ParseInvariant(Divisor),out var Index,out var Rest));
        Assert.Equal(Expected,Index);Assert.Equal(LogicalScalar.ParseInvariant(Remainder),Rest);
    }
    [Fact]
    public void IndexLimitsAreCheckedAndRatioScalingDoesNotOverflowBeforeDivision(){
        Assert.Equal(long.MinValue,LogicalNumeric.FloorToInt64(LogicalScalar.FromInt64(long.MinValue)));
        Assert.Equal(long.MaxValue,LogicalNumeric.FloorToInt64(LogicalScalar.FromInt64(long.MaxValue)));
        Assert.False(LogicalNumeric.TryScaledFloorQuotient(LogicalScalar.FromInt64(long.MaxValue),LogicalScalar.FromRatio(1,2),1,out var Index));Assert.Equal(0,Index);
        var Large=LogicalScalar.FromRaw(Int128.MaxValue);
        Assert.Equal(Large,LogicalNumeric.ScaleByRatio(Large,long.MaxValue,long.MaxValue));
        Assert.False(LogicalNumeric.TryScaleByRatio(Large,2,1,out var Result));Assert.Equal(default,Result);
        Assert.Equal(LogicalScalar.FromRaw(64),LogicalNumeric.Tolerance(58,8));
        Assert.Equal(LogicalScalar.FromRaw(1),LogicalNumeric.PrecisionStep);
        Assert.Equal("fixed-128-64",LogicalNumeric.ProfileId);Assert.Equal("LogicalScalar-Q64.64",LogicalNumeric.CacheProfileId);
    }
}
