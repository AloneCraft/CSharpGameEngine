namespace CSharpGameEngine.Numerics.Tests;
/// <summary>ADR-0078/M0109: 決定的な atan2(車のタイヤの滑りの角とアッカーマンの棒の幾何に使う)。</summary>
public sealed class LogicalMathAtan2Tests {
    [Theory]
    [InlineData(0.0,1.0)]
    [InlineData(1.0,1.0)]
    [InlineData(1.0,0.0)]
    [InlineData(1.0,-1.0)]
    [InlineData(0.0,-1.0)]
    [InlineData(-1.0,-1.0)]
    [InlineData(-1.0,0.0)]
    [InlineData(-1.0,1.0)]
    [InlineData(0.3,2.7)]
    [InlineData(-5.5,0.01)]
    [InlineData(1234.5,-0.25)]
    [InlineData(0.0001,0.0002)]
    public void MatchesTheDoubleAtan2InEveryQuadrant(double Y,double X){
        Assert.True(LogicalMath.TryAtan2(LogicalScalar.ParseInvariant(Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)),
            LogicalScalar.ParseInvariant(X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)),out var Angle));
        Assert.True(Angle.TryToSingle(out var Value));
        Assert.Equal(Math.Atan2(Y,X),Value,6);
    }
    [Fact]
    public void TheOriginIsZeroAndTheResultIsTheSameEveryTime(){
        Assert.True(LogicalMath.TryAtan2(LogicalScalar.Zero,LogicalScalar.Zero,out var Zero));
        Assert.Equal(LogicalScalar.Zero,Zero);
        Assert.True(LogicalMath.TryAtan2(LogicalScalar.FromSingle(0.7f),LogicalScalar.FromSingle(-0.2f),out var First));
        Assert.True(LogicalMath.TryAtan2(LogicalScalar.FromSingle(0.7f),LogicalScalar.FromSingle(-0.2f),out var Second));
        Assert.Equal(First.Raw,Second.Raw);
        Assert.False(LogicalMath.TryAtan2(LogicalScalar.MinValue,LogicalScalar.One,out _));
    }
    [Fact]
    public void FortyFiveDegreesIsAQuarterOfPiToTheLastBits(){
        Assert.True(LogicalMath.TryAtan2(LogicalScalar.One,LogicalScalar.One,out var Angle));
        var Error=Angle-LogicalMath.Pi/LogicalScalar.FromInt64(4);
        Assert.True(Error.Raw>-1000&&Error.Raw<1000,$"error {Error.Raw} in 2^-64 units");
    }
}
