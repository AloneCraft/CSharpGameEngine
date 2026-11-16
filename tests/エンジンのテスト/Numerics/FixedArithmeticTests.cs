using Xunit;
using System.Numerics;
using System.Runtime.InteropServices;
namespace CSharpGameEngine.Numerics.Tests;
public sealed class FixedArithmeticTests {
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    public void FloorDivisionMatchesIntegerOracleAndLeavesOutputUntouchedOnOverflow(int Width){
        var Minimum=-(BigInteger.One<<(Width-1));var Maximum=-Minimum-1;
        var Values=new[]{Minimum,Minimum+1,-Maximum,-7,-1,BigInteger.Zero,BigInteger.One,7,Maximum};
        foreach(var Left in Values){
            foreach(var Right in new[]{BigInteger.One,2,3,Maximum}){
                var Expected=BigInteger.DivRem(Left,Right,out var Rest);if(Rest<0){ Expected--;Rest+=Right; }
                var Output=Enumerable.Repeat(0x12345678UL,Width/64).ToArray();var Before=Output.ToArray();
                var Success=FixedArithmetic.TryFloorDivRemToInt64(Digits(Left,Width),Digits(Right,Width),out var Index,Output);
                Assert.Equal(Expected>=long.MinValue&&Expected<=long.MaxValue,Success);
                if(Success){ Assert.Equal((long)Expected,Index);Assert.Equal(Rest,Number(Output)); }else{ Assert.Equal(0,Index);Assert.Equal(Before,Output); }
            }
        }
    }
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    public void ArithmeticMatchesIndependentIntegerOracleAcrossFractionPositions(int Width){
        var Random=new Random(2501+Width);
        var Minimum=-(BigInteger.One<<(Width-1));
        var Maximum=-Minimum-1;
        var Positions=new[]{0,1,Width/2,Width-2,Width-1};
        var Cases=new List<BigInteger>{Minimum,Minimum+1,-3,-1,0,1,3,Maximum-1,Maximum};
        for(var Index=0;Index<80;Index++){
            var Bytes=new byte[Width/8];Random.NextBytes(Bytes);Cases.Add(new BigInteger(Bytes));
        }
        foreach(var FractionalBits in Positions){
            foreach(var Left in Cases){
                foreach(var Right in Cases.Take(9).Append(Cases[Random.Next(Cases.Count)])){
                    Check(Left,Right,Width,FractionalBits,0,Left+Right);
                    Check(Left,Right,Width,FractionalBits,1,Left-Right);
                    Check(Left,Right,Width,FractionalBits,2,Round(Left*Right,BigInteger.One<<FractionalBits));
                    if(Right!=0){ Check(Left,Right,Width,FractionalBits,3,Round(Left<<FractionalBits,Right)); }
                    if(Right!=0){ Check(Left,Right,Width,FractionalBits,4,Left%Right); }
                }
                if(Left>=0){
                    var Radicand=Left<<FractionalBits;
                    var Root=SquareRoot(Radicand);
                    var Expected=Radicand-Root*Root>Root?Root+1:Root;
                    var A=Digits(Left,Width);
                    var Output=Enumerable.Repeat(0x987654321UL,Width/64).ToArray();
                    var Before=Output.ToArray();
                    var Success=FixedArithmetic.TrySquareRoot(A,FractionalBits,Output);
                    Assert.Equal(Expected<=Maximum,Success);
                    if(Success){ Assert.Equal(Expected,Number(Output)); }else{ Assert.Equal(Before,Output); }
                }
            }
        }
    }
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    public void FailureAndAliasedDestinationsPreserveTheContract(int Width){
        var Left=Digits(-(BigInteger.One<<(Width-1)),Width);
        var Zero=Digits(0,Width);
        var Destination=Digits(123,Width);
        var Before=Destination.ToArray();
        Assert.False(FixedArithmetic.TryDivide(Left,Zero,Width/2,Destination));
        Assert.Equal(Before,Destination);
        Assert.False(FixedArithmetic.TrySquareRoot(Left,Width/2,Destination));
        Assert.Equal(Before,Destination);
        var A=Digits(3*(BigInteger.One<<(Width/2)),Width);
        var B=Digits(2*(BigInteger.One<<(Width/2)),Width);
        Assert.True(FixedArithmetic.TryMultiply(A,B,Width/2,A));
        Assert.Equal(6*(BigInteger.One<<(Width/2)),Number(A));
        Assert.True(FixedArithmetic.TryDivide(A,B,Width/2,B));
        Assert.Equal(3*(BigInteger.One<<(Width/2)),Number(B));
        Assert.True(FixedArithmetic.TrySquareRoot(A,Width/2,A));
        Assert.True(Number(A)>0);
    }
    [Fact]
    public void ArithmeticUsesNoManagedScratchAllocations(){
        Span<ulong>A=stackalloc ulong[3]{0,1,0};
        Span<ulong>B=stackalloc ulong[3]{0,2,0};
        Span<ulong>Output=stackalloc ulong[3];
        for(var Index=0;Index<100;Index++){ FixedArithmetic.TryDivide(A,B,96,Output);FixedArithmetic.TryMultiply(A,B,96,Output);FixedArithmetic.TrySquareRoot(A,96,Output); }
        var Before=GC.GetAllocatedBytesForCurrentThread();
        ulong Checksum=0;
        for(var Index=0;Index<1000;Index++){
            FixedArithmetic.TryDivide(A,B,96,Output);Checksum^=Output[0];
            FixedArithmetic.TryMultiply(A,B,96,Output);Checksum^=Output[0];
            FixedArithmetic.TrySquareRoot(A,96,Output);Checksum^=Output[0];
        }
        var Allocated=GC.GetAllocatedBytesForCurrentThread()-Before;
        GC.KeepAlive(Checksum);
        Assert.Equal(0,Allocated);
    }
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    public void FixedAndIeeeConversionsMatchTheIndependentPersistenceBoundary(int Width){
        var Random=new Random(1181+Width);
        var Positions=new[]{0,1,Width/2,Width-2};
        foreach(var FractionalBits in Positions){
            for(var Index=0;Index<100;Index++){
                var Bytes=new byte[Width/8];Random.NextBytes(Bytes);
                var Seed=Index switch{0=>BigInteger.Zero,1=>BigInteger.One,2=>-BigInteger.One,3=>(BigInteger.One<<23)+1,4=>-(BigInteger.One<<23)-1,5=>(BigInteger.One<<(Width-1))-1,6=>-(BigInteger.One<<(Width-1)),_=>new BigInteger(Bytes)};
                var Digits=FixedArithmeticTests.Digits(Seed,Width);
                Bytes=MemoryMarshal.AsBytes(Digits.AsSpan()).ToArray();
                var Encoded=NumericValueCodec.WriteFixed(Bytes,FractionalBits);
                var ExpectedSuccess=NumericValueCodec.TryReadSingle(Encoded,out var Expected);
                var Success=FixedArithmetic.TryToSingle(Digits,FractionalBits,out var Value);
                Assert.Equal(ExpectedSuccess,Success);
                if(Success){ Assert.Equal(BitConverter.SingleToUInt32Bits(Expected),BitConverter.SingleToUInt32Bits(Value)); }
                var DoubleSuccess=NumericValueCodec.TryReadDouble(Encoded,out var ExpectedDouble);
                Assert.Equal(DoubleSuccess,FixedArithmetic.TryToDouble(Digits,FractionalBits,out var DoubleValue));
                if(DoubleSuccess){ Assert.Equal(BitConverter.DoubleToUInt64Bits(ExpectedDouble),BitConverter.DoubleToUInt64Bits(DoubleValue)); }
                var DestinationWidth=Width==192?64:Width+64;
                var TargetFractionalBits=DestinationWidth/2;
                var ExpectedBytes=Enumerable.Repeat((byte)0x55,DestinationWidth/8).ToArray();
                ExpectedSuccess=NumericValueCodec.TryReadFixed(Encoded,TargetFractionalBits,ExpectedBytes);
                var Output=MemoryMarshal.Cast<byte,ulong>(Enumerable.Repeat((byte)0x55,DestinationWidth/8).ToArray()).ToArray();
                Success=FixedArithmetic.TryRescale(Digits,FractionalBits,TargetFractionalBits,Output);
                Assert.Equal(ExpectedSuccess,Success);
                Assert.Equal(ExpectedBytes,MemoryMarshal.AsBytes(Output.AsSpan()).ToArray());
            }
            foreach(var Bits in new uint[]{0,0x80000000,1,0x80000001,0x7fffff,0x800000,0x3f000000,0x3f800000,0xbf800000,0x7f7fffff,0xff7fffff,0x7f800000,0xff800000,0x7fc00000}){
                var Single=BitConverter.UInt32BitsToSingle(Bits);
                var Expected=Enumerable.Repeat((byte)0x55,Width/8).ToArray();
                var ExpectedSuccess=float.IsFinite(Single)&&NumericValueCodec.TryReadFixed(NumericValueCodec.WriteSingle(Single),FractionalBits,Expected);
                var Output=MemoryMarshal.Cast<byte,ulong>(Enumerable.Repeat((byte)0x55,Width/8).ToArray()).ToArray();
                var Success=FixedArithmetic.TryFromSingle(Single,FractionalBits,Output);
                Assert.Equal(ExpectedSuccess,Success);
                Assert.Equal(Expected,MemoryMarshal.AsBytes(Output.AsSpan()).ToArray());
            }
            foreach(var Bits in new ulong[]{0,0x8000000000000000,1,0x8000000000000001,0x000fffffffffffff,0x0010000000000000,0x3fe0000000000000,0x3ff0000000000000,0xbff0000000000000,0x7fefffffffffffff,0xffefffffffffffff,0x7ff0000000000000,0xfff0000000000000,0x7ff8000000000000}){
                var Double=BitConverter.UInt64BitsToDouble(Bits);
                var Expected=Enumerable.Repeat((byte)0x55,Width/8).ToArray();
                var ExpectedSuccess=double.IsFinite(Double)&&NumericValueCodec.TryReadFixed(NumericValueCodec.WriteDouble(Double),FractionalBits,Expected);
                var Output=MemoryMarshal.Cast<byte,ulong>(Enumerable.Repeat((byte)0x55,Width/8).ToArray()).ToArray();
                Assert.Equal(ExpectedSuccess,FixedArithmetic.TryFromDouble(Double,FractionalBits,Output));
                Assert.Equal(Expected,MemoryMarshal.AsBytes(Output.AsSpan()).ToArray());
            }
        }
    }
    private static void Check(BigInteger Left,BigInteger Right,int Width,int FractionalBits,int Operation,BigInteger Expected){
        var A=Digits(Left,Width);
        var B=Digits(Right,Width);
        var Output=Enumerable.Repeat(0x13579BDFUL,Width/64).ToArray();
        var Before=Output.ToArray();
        var Success=Operation switch{
            0=>FixedArithmetic.TryAdd(A,B,Output),
            1=>FixedArithmetic.TrySubtract(A,B,Output),
            2=>FixedArithmetic.TryMultiply(A,B,FractionalBits,Output),
            3=>FixedArithmetic.TryDivide(A,B,FractionalBits,Output),
            _=>FixedArithmetic.TryRemainder(A,B,Output),
        };
        var Limit=BigInteger.One<<(Width-1);
        Assert.Equal(Expected>=-Limit&&Expected<Limit,Success);
        if(Success){ Assert.Equal(Expected,Number(Output)); }else{ Assert.Equal(Before,Output); }
    }
    private static ulong[]Digits(BigInteger Value,int Width){
        var Bytes=new byte[Width/8];
        Array.Fill(Bytes,Value.Sign<0?(byte)255:(byte)0);
        var Source=Value.ToByteArray();Source.CopyTo(Bytes,0);
        return MemoryMarshal.Cast<byte,ulong>(Bytes).ToArray();
    }
    private static BigInteger Number(ulong[]Digits)=>new(MemoryMarshal.AsBytes(Digits.AsSpan()));
    private static BigInteger Round(BigInteger Numerator,BigInteger Denominator){
        var Negative=Numerator.Sign*Denominator.Sign<0;
        var Quotient=BigInteger.DivRem(BigInteger.Abs(Numerator),BigInteger.Abs(Denominator),out var Remainder);
        if(2*Remainder>BigInteger.Abs(Denominator)||(2*Remainder==BigInteger.Abs(Denominator)&&!Quotient.IsEven)){ Quotient++; }
        return Negative?-Quotient:Quotient;
    }
    private static BigInteger SquareRoot(BigInteger Value){
        if(Value.IsZero){ return 0; }
        var Root=BigInteger.One<<(int)((Value.GetBitLength()+1)/2);
        while(true){ var Next=(Root+Value/Root)/2;if(Next>=Root){ return Root; }Root=Next; }
    }
}
