using CSharpGameEngine.Runtime.Physics;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class FixedPhysicsMathTests {
    [Fact]
    public void MultiplyAndDivide_UseQ32Point32(){
        PhysicsScalar Left=PhysicsScalar.FromDouble(1.5);
        PhysicsScalar Right=PhysicsScalar.FromDouble(2.25);

        Assert.True(PhysicsScalar.TryMultiply(Left,Right,out PhysicsScalar Product));
        Assert.Equal(3.375,Product.ToDouble());
        Assert.True(PhysicsScalar.TryDivide(Product,Right,out PhysicsScalar Quotient));
        Assert.Equal(Left,Quotient);
    }

    [Fact]
    public void RoundingAtExactlyHalf_UsesTiesToEven(){
        Int128 EvenHalf=((Int128)2<<32)+(1L<<31);
        Int128 OddHalf=((Int128)3<<32)+(1L<<31);

        Assert.Equal(2L,PhysicsScalar.RoundShiftRightToEven(EvenHalf,32));
        Assert.Equal(4L,PhysicsScalar.RoundShiftRightToEven(OddHalf,32));
    }

    [Fact]
    public void QuaternionNormalization_ProducesUnitQuaternion(){
        PhysicsQuaternion Input=new(
            PhysicsScalar.FromDouble(0.2),
            PhysicsScalar.FromDouble(0.3),
            PhysicsScalar.FromDouble(0.4),
            PhysicsScalar.FromDouble(0.5));

        Assert.True(Input.TryNormalize(out PhysicsQuaternion Normalized));
        Assert.InRange(Normalized.LengthSquared.ToDouble(),0.999999,1.000001);
    }
}
