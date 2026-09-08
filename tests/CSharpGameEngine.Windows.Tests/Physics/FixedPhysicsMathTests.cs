using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class FixedPhysicsMathTests {
    [Fact]
    public void MultiplyAndDivide_UseCanonicalQ64Point64(){
        LogicalScalar Left=LogicalScalar.FromSingle(1.5f);
        LogicalScalar Right=LogicalScalar.FromSingle(2.25f);

        Assert.True(LogicalScalar.TryMultiply(Left,Right,out LogicalScalar Product));
        Assert.True(Product.TryToSingle(out var ProductSingle));
        Assert.Equal(3.375f,ProductSingle);
        Assert.True(LogicalScalar.TryDivide(Product,Right,out LogicalScalar Quotient));
        Assert.Equal(Left,Quotient);
    }

    [Fact]
    public void RoundingAtExactlyHalf_UsesTiesToEven(){
        Assert.True(LogicalScalar.FromSingle(2.5f).TryToScaledInt64(0,out var Even));
        Assert.True(LogicalScalar.FromSingle(3.5f).TryToScaledInt64(0,out var Odd));
        Assert.Equal(2L,Even);
        Assert.Equal(4L,Odd);
    }

    [Fact]
    public void QuaternionNormalization_ProducesUnitQuaternion(){
        LogicalQuaternion Input=new(
            LogicalScalar.FromSingle(0.2f),
            LogicalScalar.FromSingle(0.3f),
            LogicalScalar.FromSingle(0.4f),
            LogicalScalar.FromSingle(0.5f));

        Assert.True(Input.TryNormalize(out LogicalQuaternion Normalized));
        Assert.True(Normalized.LengthSquared.TryToSingle(out var LengthSquared));
        Assert.InRange(LengthSquared,0.999999f,1.000001f);
    }
}
