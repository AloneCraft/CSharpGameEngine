using CSharpGameEngine.Input;
namespace CSharpGameEngine.Core.Tests;
/// <summary>M0108: the value types an <c>[InputActions]</c> struct carries (<see cref="Axis"/>, <see cref="Button.HeldSeconds"/>).</summary>
public sealed class InputActionValueTypesTests {
    [Theory]
    [InlineData(0f,0f)]
    [InlineData(0.5f,0.5f)]
    [InlineData(-0.5f,-0.5f)]
    [InlineData(1f,1f)]
    [InlineData(-1f,-1f)]
    [InlineData(2f,1f)]
    [InlineData(-2f,-1f)]
    [InlineData(float.MaxValue,1f)]
    [InlineData(float.MinValue,-1f)]
    [InlineData(float.PositiveInfinity,0f)]
    [InlineData(float.NegativeInfinity,0f)]
    [InlineData(float.NaN,0f)]
    public void AnAxisBuiltFromASum_IsClampedToMinusOneToOneAndNonFiniteSumsReadZero(float Sum,float Expected){
        Assert.Equal(Expected,Axis.FromSum(Sum).Value);
    }
    [Fact]
    public void AnAxis_ConvertsToAFloatSoThrottleMinusBrakeReadsAsANumber(){
        Axis Throttle=Axis.FromSum(0.75f);
        Axis Brake=Axis.FromSum(0.25f);
        float Drive=Throttle-Brake;
        Assert.Equal(0.5f,Drive);
        Assert.Equal(0f,default(Axis).Value);
    }
    [Theory]
    [InlineData(0f,true)]
    [InlineData(0.4f,true)]
    [InlineData(0.49999997f,true)]
    [InlineData(0.5f,false)]
    [InlineData(0.5000001f,false)]
    [InlineData(4f,false)]
    [InlineData(float.PositiveInfinity,false)]
    public void ARelease_IsShortOnlyWhenTheHoldWasStrictlyUnderTheThreshold(float HeldSeconds,bool Expected){
        Assert.Equal(Expected,new Button(false,false,true,HeldSeconds).ReleasedBefore(0.5f));
    }
    [Fact]
    public void OnlyTheReleaseFrameCanBeAShortRelease_AndANonNumberThresholdIsNeverMet(){
        Assert.False(new Button(true,false,false,0.1f).ReleasedBefore(0.5f));
        Assert.False(new Button(true,true,false,0f).ReleasedBefore(0.5f));
        Assert.False(default(Button).ReleasedBefore(0.5f));
        Assert.False(new Button(false,false,true,0.1f).ReleasedBefore(float.NaN));
        Assert.False(new Button(false,false,true,0f).ReleasedBefore(0f));
        Assert.True(new Button(false,false,true,0f).ReleasedBefore(float.PositiveInfinity));
    }
    [Fact]
    public void AButton_CarriesItsHeldSecondsAndDefaultsToZero(){
        Assert.Equal(0f,new Button(true,true,false).HeldSeconds);
        var Released=new Button(false,false,true,0.4f);
        Assert.True(Released.Released);
        Assert.False(Released.Held);
        Assert.Equal(0.4f,Released.HeldSeconds);
        Assert.Equal(0f,default(Button).HeldSeconds);
    }
}
