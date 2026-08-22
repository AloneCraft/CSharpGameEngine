using System.Runtime.InteropServices;
using Logical128=CSharpGameEngine.Numerics.Logical128;
using Logical192=CSharpGameEngine.Numerics.Logical192;
using Logical256=CSharpGameEngine.Numerics.Logical256;

namespace CSharpGameEngine.Numerics.Tests;

public sealed class LogicalNumericsTests {
    [Fact]
    public void PublishedFormatsHaveFixedConstantsAndLayouts(){
        Assert.Equal(128,Logical128.Scalar.TotalBits);
        Assert.Equal(64,Logical128.Scalar.FractionalBits);
        Assert.Equal(192,Logical192.Scalar.TotalBits);
        Assert.Equal(96,Logical192.Scalar.FractionalBits);
        Assert.Equal(256,Logical256.Scalar.TotalBits);
        Assert.Equal(128,Logical256.Scalar.FractionalBits);
        Assert.Equal(16,Marshal.SizeOf<Logical128.Scalar>());
        Assert.Equal(24,Marshal.SizeOf<Logical192.Scalar>());
        Assert.Equal(32,Marshal.SizeOf<Logical256.Scalar>());
    }

    [Fact]
    public void Logical256ScalarPreservesLargeOriginAndFraction(){
        Logical256.Scalar Origin=Logical256.Scalar.ParseInvariant("1000000000000000000000000000000");
        Assert.True(Logical256.Scalar.TryAdd(Origin,Logical256.Scalar.ParseInvariant("0.125"),out Logical256.Scalar Nearby));
        Assert.Equal("1000000000000000000000000000000.125",Nearby.ToStringInvariant());
        Assert.Equal(0.125d,(double)(Nearby-Origin));
    }

    [Fact]
    public void LogicalVectorsUseRufComponentsAndNormalize(){
        Logical256.Vector3 Direction=new(3d,4d,0d);
        Logical256.Vector3 Unit=Direction.Normalize();

        Assert.Equal(3d,(double)Direction.R);
        Assert.Equal(4d,(double)Direction.U);
        Assert.Equal(0d,(double)Direction.F);
        Assert.Equal(1d,(double)Unit.LengthSquared);
        Assert.Equal(Logical256.Vector3.UnitR,new Logical256.Vector3(1d,0d,0d));
        Assert.Equal(Logical256.Vector3.UnitU,new Logical256.Vector3(0d,1d,0d));
        Assert.Equal(Logical256.Vector3.UnitF,new Logical256.Vector3(0d,0d,1d));
    }
}
