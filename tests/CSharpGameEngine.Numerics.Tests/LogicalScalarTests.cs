using System.Numerics;
using System.Runtime.InteropServices;
namespace CSharpGameEngine.Numerics.Tests;
public sealed class LogicalScalarTests {
    [Fact]
    public void CanonicalLayoutAndSingleConversionAreQ64Point64(){
        Assert.Equal(128,LogicalScalar.TotalBits);
        Assert.Equal(64,LogicalScalar.FractionalBits);
        Assert.Equal(16,Marshal.SizeOf<LogicalScalar>());
        Assert.Equal((Int128)1<<63,LogicalScalar.FromSingle(0.5f).Raw);
        Assert.Equal((Int128)3<<63,LogicalScalar.FromSingle(1.5f).Raw);
        Assert.True(LogicalScalar.FromSingle(-123.25f).TryToSingle(out var Value));
        Assert.Equal(-123.25f,Value);
        Assert.False(LogicalScalar.TryFromSingle(float.NaN,out _));
        Assert.False(LogicalScalar.TryFromSingle(float.PositiveInfinity,out _));
    }
    [Fact]
    public void CheckedArithmeticUsesTiesToEvenAndReportsFaults(){
        var Half=LogicalScalar.FromRaw((Int128)1<<63);
        Assert.True(LogicalScalar.TryMultiply(LogicalScalar.FromRaw(1),Half,out var EvenProduct));
        Assert.Equal(0,EvenProduct.Raw);
        Assert.True(LogicalScalar.TryMultiply(LogicalScalar.FromRaw(3),Half,out var OddProduct));
        Assert.Equal(2,OddProduct.Raw);
        Assert.True(LogicalScalar.TryDivide(LogicalScalar.FromRaw(1),LogicalScalar.FromRaw((Int128)1<<65),out var EvenQuotient));
        Assert.Equal(0,EvenQuotient.Raw);
        Assert.True(LogicalScalar.TryDivide(LogicalScalar.FromRaw(3),LogicalScalar.FromRaw((Int128)1<<65),out var OddQuotient));
        Assert.Equal(2,OddQuotient.Raw);
        Assert.False(LogicalScalar.TryAdd(LogicalScalar.MaxValue,LogicalScalar.One,out _));
        Assert.False(LogicalScalar.TrySubtract(LogicalScalar.MinValue,LogicalScalar.One,out _));
        Assert.False(LogicalScalar.TryMultiply(LogicalScalar.MaxValue,LogicalScalar.FromInt64(2),out _));
        Assert.False(LogicalScalar.TryDivide(LogicalScalar.One,LogicalScalar.Zero,out _));
        Assert.False(LogicalScalar.TrySquareRoot(-LogicalScalar.One,out _));
    }
    [Fact]
    public void SquareRootAndVectorMathRemainInCanonicalPrecision(){
        Assert.True(LogicalScalar.TrySquareRoot(LogicalScalar.FromSingle(2.25f),out var Root));
        Assert.Equal(LogicalScalar.FromSingle(1.5f),Root);
        var Direction=new LogicalVector3(3,4,0);
        Assert.True(Direction.TryNormalize(out var Unit));
        Assert.InRange(Int128.Abs(Unit.LengthSquared.Raw-LogicalScalar.One.Raw),0,1);
        Assert.Equal(new LogicalVector3(-3,6,-3),LogicalVector3.Cross(new(1,2,3),new(4,5,6)));
    }
    [Fact]
    public void TryNormalizeAcceptsTheReceiverAsItsOwnOutput(){
        var Direction=new LogicalVector3(3,0,4);
        Assert.True(Direction.TryNormalize(out Direction));
        Assert.Equal(LogicalScalar.FromInt64(3)/LogicalScalar.FromInt64(5),Direction.R);
        var Rotation=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.FromInt64(3),LogicalScalar.Zero,LogicalScalar.FromInt64(4));
        Assert.True(Rotation.TryNormalize(out Rotation));
        Assert.Equal(LogicalScalar.FromInt64(4)/LogicalScalar.FromInt64(5),Rotation.W);
    }
    [Fact]
    public void EulerAuthoringConvertsOnceToANormalizedLogicalQuaternion(){
        var Rotation=LogicalQuaternion.FromEulerRuf(new LogicalVector3(0,MathF.PI/2f,0));

        Assert.InRange(Int128.Abs(Rotation.LengthSquared.Raw-LogicalScalar.One.Raw),0,2);
        Assert.True(Rotation.TryRotate(LogicalVector3.UnitF,out var Forward));
        Assert.True(Forward.TryToSingle(out var ForwardSingle));
        Assert.InRange(ForwardSingle.X,0.99999f,1.00001f);
        Assert.InRange(MathF.Abs(ForwardSingle.Y),0,0.00001f);
        Assert.InRange(MathF.Abs(ForwardSingle.Z),0,0.00001f);
    }
    [Fact]
    public void FixedPointSinCosAgreesWithIndependentOracleAcrossLargeReducedAngles(){
        var Random=new Random(9053);
        for(var Index=0;Index<2000;Index++){
            var Angle=(Random.NextSingle()-0.5f)*2_000_000f;
            var LogicalAngle=LogicalScalar.FromSingle(Angle);
            Assert.True(LogicalMath.TrySinCos(LogicalAngle,out var Sin,out var Cos));
            var OracleAngle=ToDouble(LogicalAngle);
            Assert.InRange(Math.Abs(ToDouble(Sin)-Math.Sin(OracleAngle)),0,8e-13);
            Assert.InRange(Math.Abs(ToDouble(Cos)-Math.Cos(OracleAngle)),0,8e-13);
            Assert.InRange(Math.Abs(ToDouble(Sin*Sin+Cos*Cos)-1),0,2e-12);
        }
    }
    [Fact]
    public void PeriodicAdvanceHandlesFullUlongTickRangeWithoutIntermediateOverflow(){
        var Phase=LogicalScalar.ParseInvariant("1.25");
        var Step=LogicalScalar.ParseInvariant("0.000000000000000001");
        Assert.True(LogicalMath.TryAdvanceRadians(Phase,Step,ulong.MaxValue,out var First));
        Assert.True(LogicalMath.TryAdvanceRadians(Phase,Step,ulong.MaxValue,out var Second));
        Assert.Equal(First,Second);
        Assert.True(First>=LogicalScalar.Zero&&First<LogicalMath.TwoPi);
    }
    [Fact]
    public void SerializationAndInvariantTextRoundTripTheRawValue(){
        var Values=new[]{LogicalScalar.Zero,LogicalScalar.One,LogicalScalar.FromSingle(-123.25f),LogicalScalar.FromRaw(Int128.MaxValue),LogicalScalar.FromRaw(Int128.MinValue)};
        Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];
        foreach(var Value in Values){
            Value.WriteLittleEndian(Bytes);
            Assert.True(LogicalScalar.TryReadLittleEndian(Bytes,out var BinaryRoundTrip));
            Assert.Equal(Value,BinaryRoundTrip);
            Assert.True(LogicalScalar.TryParseInvariant(Value.ToStringInvariant(),out var TextRoundTrip));
            Assert.Equal(Value,TextRoundTrip);
        }
        Assert.False(LogicalScalar.TryReadLittleEndian(Bytes[..^1],out _));
        Assert.False(LogicalScalar.TryParseInvariant("9223372036854775808",out _));
        Assert.False(LogicalScalar.TryParseInvariant("18446744073709551616",out _));
        Assert.False(LogicalScalar.TryParseInvariant("340282366920938463463374607431768211455",out _));
        Assert.False(LogicalScalar.TryParseInvariant("-18446744073709551616",out _));
    }
    [Fact]
    public void MultiplyAndDivideAgreeWithIndependentBigIntegerOracle(){
        var Random=new Random(5489);
        var Scale=BigInteger.One<<LogicalScalar.FractionalBits;
        for(var Index=0;Index<2000;Index++){
            var LeftRaw=(Int128)Random.NextInt64(-4_000_000_000,4_000_000_000);
            var RightRaw=(Int128)Random.NextInt64(-4_000_000_000,4_000_000_000);
            if(RightRaw==0){ RightRaw=1; }
            var Left=LogicalScalar.FromRaw(LeftRaw);
            var Right=LogicalScalar.FromRaw(RightRaw);
            Assert.True(LogicalScalar.TryMultiply(Left,Right,out var Product));
            Assert.Equal((Int128)DivideRoundToEven((BigInteger)LeftRaw*RightRaw,Scale),Product.Raw);
            Assert.True(LogicalScalar.TryDivide(Left,Right,out var Quotient));
            Assert.Equal((Int128)DivideRoundToEven((BigInteger)LeftRaw*Scale,RightRaw),Quotient.Raw);
        }
    }
    [Fact]
    public void FullWidthArithmeticAndSquareRootAgreeWithIndependentOracle(){
        var Random=new Random(8741);
        var Scale=BigInteger.One<<LogicalScalar.FractionalBits;
        var Minimum=(BigInteger)Int128.MinValue;
        var Maximum=(BigInteger)Int128.MaxValue;
        for(var Index=0;Index<1000;Index++){
            var LeftRaw=NextInt128(Random);
            var RightRaw=NextInt128(Random);
            if(RightRaw==0){ RightRaw=1; }
            var Left=LogicalScalar.FromRaw(LeftRaw);
            var Right=LogicalScalar.FromRaw(RightRaw);
            var ExpectedProduct=DivideRoundToEven((BigInteger)LeftRaw*RightRaw,Scale);
            var ProductSucceeded=LogicalScalar.TryMultiply(Left,Right,out var Product);
            Assert.Equal(ExpectedProduct>=Minimum&&ExpectedProduct<=Maximum,ProductSucceeded);
            if(ProductSucceeded){ Assert.Equal((Int128)ExpectedProduct,Product.Raw); }
            var ExpectedQuotient=DivideRoundToEven((BigInteger)LeftRaw*Scale,RightRaw);
            var DivideSucceeded=LogicalScalar.TryDivide(Left,Right,out var Quotient);
            Assert.Equal(ExpectedQuotient>=Minimum&&ExpectedQuotient<=Maximum,DivideSucceeded);
            if(DivideSucceeded){ Assert.Equal((Int128)ExpectedQuotient,Quotient.Raw); }
            var PositiveRaw=(Int128)((UInt128)LeftRaw&((UInt128)Int128.MaxValue));
            Assert.True(LogicalScalar.TrySquareRoot(LogicalScalar.FromRaw(PositiveRaw),out var Root));
            Assert.Equal((Int128)SquareRootRoundToEven((BigInteger)PositiveRaw*Scale),Root.Raw);
        }
    }
    [Fact]
    public void ArithmeticHotPathDoesNotAllocate(){
        var Left=LogicalScalar.FromSingle(1.25f);
        var Right=LogicalScalar.FromSingle(0.75f);
        LogicalScalar Result=default;
        for(var Index=0;Index<1000;Index++){ LogicalScalar.TryMultiply(Left,Right,out Result);LogicalScalar.TryDivide(Left,Right,out Result); }
        var Before=GC.GetAllocatedBytesForCurrentThread();
        for(var Index=0;Index<100000;Index++){ LogicalScalar.TryMultiply(Left,Right,out Result);LogicalScalar.TryDivide(Left,Right,out Result); }
        var After=GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(Before,After);
        GC.KeepAlive(Result);
    }
    private static BigInteger DivideRoundToEven(BigInteger Numerator,BigInteger Denominator){
        var Quotient=BigInteger.DivRem(Numerator,Denominator,out var Remainder);
        var TwiceRemainder=BigInteger.Abs(Remainder)*2;
        var AbsoluteDenominator=BigInteger.Abs(Denominator);
        if(TwiceRemainder>AbsoluteDenominator||(TwiceRemainder==AbsoluteDenominator&&!Quotient.IsEven)){ Quotient+=Numerator.Sign==Denominator.Sign?BigInteger.One:-BigInteger.One; }
        return Quotient;
    }
    private static Int128 NextInt128(Random Random){
        Span<byte>Bytes=stackalloc byte[16];
        Random.NextBytes(Bytes);
        return (Int128)BitConverter.ToUInt64(Bytes)|(Int128)BitConverter.ToUInt64(Bytes[8..])<<64;
    }
    private static BigInteger SquareRootRoundToEven(BigInteger Value){
        var Low=BigInteger.Zero;
        var High=BigInteger.One<<96;
        while(Low+1<High){
            var Middle=(Low+High)>>1;
            if(Middle*Middle<=Value){ Low=Middle; }else{ High=Middle; }
        }
        var Remainder=Value-Low*Low;
        var Gap=2*Low+1;
        if(2*Remainder>Gap||(2*Remainder==Gap&&!Low.IsEven)){ Low++; }
        return Low;
    }
    private static double ToDouble(LogicalScalar Value)=>double.CreateChecked(Value.Raw)/18446744073709551616d;
    [Fact]
    public void ProductAndSquareRoot_MatchTheirReferencesExactly(){
        var Random=new Random(5489);
        UInt128 Next(){
            var Bits=Random.Next(0,129);
            var Value=(UInt128)(ulong)Random.NextInt64()<<64|(ulong)Random.NextInt64()|(UInt128)(ulong)Random.Next(0,2)<<127;
            return Bits==128?Value:Value&(((UInt128)1<<Bits)-1);
        }
        UInt128[]Edges=[0,1,2,ulong.MaxValue,(UInt128)ulong.MaxValue+1,(UInt128)1<<127,((UInt128)1<<127)-1,UInt128.MaxValue];
        foreach(var Left in Edges){
            foreach(var Right in Edges){ Assert.Equal(LogicalScalar.MultiplyUnsignedLimbwiseLimbs(Left,Right),LogicalScalar.MultiplyUnsignedLimbs(Left,Right)); }
        }
        for(var Index=0;Index<200000;Index++){
            var Left=Next();
            var Right=Next();
            Assert.Equal(LogicalScalar.MultiplyUnsignedLimbwiseLimbs(Left,Right),LogicalScalar.MultiplyUnsignedLimbs(Left,Right));
        }
        void CheckRoot(Int128 Raw){
            var Value=LogicalScalar.FromRaw(Raw);
            var Expected=LogicalScalar.TrySquareRootBinarySearch(Value,out var ExpectedRoot);
            Assert.Equal(Expected,LogicalScalar.TrySquareRoot(Value,out var ActualRoot));
            Assert.Equal(ExpectedRoot,ActualRoot);
        }
        foreach(var Raw in new Int128[]{-1,0,1,2,3,4,(Int128)1<<64,((Int128)1<<64)-1,((Int128)1<<64)+1,Int128.MaxValue,Int128.MaxValue-1,(Int128)1<<126}){ CheckRoot(Raw); }
        for(var Index=0;Index<30000;Index++){
            CheckRoot((Int128)(Next()>>1));
            var Whole=(Int128)Random.NextInt64(1,1L<<40);
            CheckRoot(Whole*Whole<<64);
            CheckRoot((Whole*Whole<<64)-1);
            CheckRoot((Whole*Whole<<64)+1);
        }
    }
    [Fact]
    public void ScaledDivision_MatchesTheShiftSubtractReferenceExactly(){
        // Divisions sit on the deterministic simulation path, so the limb division must reproduce the
        // bitwise reference's quotient, remainder and overflow verdict for every input class.
        var Random=new Random(20260930);
        UInt128 Next(){
            var Bits=Random.Next(0,129);
            var Value=((UInt128)(ulong)Random.NextInt64()<<64|(ulong)Random.NextInt64())^((UInt128)Random.Next()<<127);
            return Bits==128?Value:Value&(((UInt128)1<<Bits)-1);
        }
        UInt128[]Edges=[0,1,2,3,ulong.MaxValue-1,ulong.MaxValue,(UInt128)ulong.MaxValue+1,(UInt128)1<<63,(UInt128)1<<64|1,(UInt128)1<<127,((UInt128)1<<127)-1,UInt128.MaxValue,UInt128.MaxValue-1];
        void Check(UInt128 Numerator,UInt128 Denominator){
            if(Denominator==0){ return; }
            var Expected=LogicalScalar.TryDivideScaledBitwise(Numerator,Denominator,out var ExpectedQuotient,out var ExpectedRemainder);
            var Actual=LogicalScalar.TryDivideScaled(Numerator,Denominator,out var ActualQuotient,out var ActualRemainder);
            Assert.Equal(Expected,Actual);
            if(Expected){
                Assert.Equal(ExpectedQuotient,ActualQuotient);
                Assert.Equal(ExpectedRemainder,ActualRemainder);
            }
        }
        foreach(var Numerator in Edges){
            foreach(var Denominator in Edges){ Check(Numerator,Denominator); }
        }
        for(var Index=0;Index<200000;Index++){
            Check(Next(),Next());
        }
        for(var Index=0;Index<50000;Index++){
            // Divisors near a limb boundary and numerators just above them exercise the estimate correction and add-back.
            var Denominator=((UInt128)(ulong)Random.NextInt64()<<64)|(ulong)(Random.Next(0,4)==0?0:Random.NextInt64());
            Check(Denominator*(UInt128)(ulong)Random.Next(1,1<<20)+(UInt128)(ulong)Random.Next(),Denominator|(UInt128)1<<64);
        }
    }
}
