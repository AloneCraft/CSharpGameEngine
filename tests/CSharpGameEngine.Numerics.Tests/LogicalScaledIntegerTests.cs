using Logical128=CSharpGameEngine.Numerics.Logical128;
using Logical192=CSharpGameEngine.Numerics.Logical192;
using Logical256=CSharpGameEngine.Numerics.Logical256;

namespace CSharpGameEngine.Numerics.Tests;

public sealed class LogicalScaledIntegerTests {
    [Fact]
    public void Q32Point32RoundTrip_IsExactForEveryProfile(){
        const long Raw=5L<<30;
        Assert.True(Logical128.Scalar.TryFromScaledInt64(Raw,32,out Logical128.Scalar Value128));
        Assert.True(Logical192.Scalar.TryFromScaledInt64(Raw,32,out Logical192.Scalar Value192));
        Assert.True(Logical256.Scalar.TryFromScaledInt64(Raw,32,out Logical256.Scalar Value256));
        Assert.True(Value128.TryToScaledInt64(32,out long Result128));
        Assert.True(Value192.TryToScaledInt64(32,out long Result192));
        Assert.True(Value256.TryToScaledInt64(32,out long Result256));
        Assert.Equal(Raw,Result128);
        Assert.Equal(Raw,Result192);
        Assert.Equal(Raw,Result256);
    }

    [Fact]
    public void DownscaleAtHalf_UsesTiesToEvenForEveryProfile(){
        Verify128();
        Verify192();
        Verify256();
    }

    [Fact]
    public void Q32Point32Conversion_RejectsLogicalValueOutsideLocalDomain(){
        Logical256.Scalar Huge=Logical256.Scalar.ParseInvariant("1000000000000000000000000000000");
        Assert.False(Huge.TryToScaledInt64(32,out _));
    }

    private static void Verify128(){
        Assert.True(Logical128.Scalar.TryFromScaledInt64(5,33,out Logical128.Scalar EvenHalf));
        Assert.True(Logical128.Scalar.TryFromScaledInt64(7,33,out Logical128.Scalar OddHalf));
        Assert.True(EvenHalf.TryToScaledInt64(32,out long Even));
        Assert.True(OddHalf.TryToScaledInt64(32,out long Odd));
        Assert.Equal(2,Even);Assert.Equal(4,Odd);
    }
    private static void Verify192(){
        Assert.True(Logical192.Scalar.TryFromScaledInt64(5,33,out Logical192.Scalar EvenHalf));
        Assert.True(Logical192.Scalar.TryFromScaledInt64(7,33,out Logical192.Scalar OddHalf));
        Assert.True(EvenHalf.TryToScaledInt64(32,out long Even));
        Assert.True(OddHalf.TryToScaledInt64(32,out long Odd));
        Assert.Equal(2,Even);Assert.Equal(4,Odd);
    }
    private static void Verify256(){
        Assert.True(Logical256.Scalar.TryFromScaledInt64(5,33,out Logical256.Scalar EvenHalf));
        Assert.True(Logical256.Scalar.TryFromScaledInt64(7,33,out Logical256.Scalar OddHalf));
        Assert.True(EvenHalf.TryToScaledInt64(32,out long Even));
        Assert.True(OddHalf.TryToScaledInt64(32,out long Odd));
        Assert.Equal(2,Even);Assert.Equal(4,Odd);
    }
}
