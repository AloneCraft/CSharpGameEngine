using System.Numerics;
namespace CSharpGameEngine.Numerics.Tests;
public sealed class NumericValueCodecTests {
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-0")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("1.20")]
    [InlineData("0.1")]
    [InlineData("1e-3")]
    [InlineData(" 1")]
    [InlineData("NaN")]
    public void StoredTextIsValidatedWithoutRoundingItToADestination(string?Text){
        Assert.False(NumericValueCodec.TryReadCanonicalText(Text,out var Value));Assert.Empty(Value);
    }
    [Fact]
    public void TextAndBinaryDescribeTheSameValueWithoutDoubleIntermediate(){
        foreach(var Bytes in new[]{8,16,24}){
            foreach(var Scale in new[]{0,Bytes*4,Bytes*8-2}){
                var Raw=SignedBytes(-((BigInteger.One<<(Bytes*8-1))-1),Bytes);
                var Text=NumericValueCodec.ToText(NumericValueCodec.WriteFixed(Raw,Scale));
                var Actual=new byte[Bytes];
                Assert.True(NumericValueCodec.TryReadFixedText(Text,Scale,Actual));
                Assert.Equal(Raw,Actual);
            }
        }
        var Output=new byte[24];
        Assert.True(NumericValueCodec.TryReadFixedText("9007199254740993.5",96,Output));
        Assert.Equal("9007199254740993.5",NumericValueCodec.ToText(NumericValueCodec.WriteFixed(Output,96)));
        Assert.Equal("0.100000001490116119384765625",NumericValueCodec.ToText(NumericValueCodec.WriteSingle(0.1f)));
        Assert.True(NumericValueCodec.TryReadFixedText("0.1",3,Output));
        Assert.Equal(BigInteger.One,new BigInteger(Output,false,false));
        foreach(var Invalid in new[]{"", ".", "-", "1.2.3", "NaN", "1,5", " 1", "1 "}){
            var Before=Output.ToArray();
            Assert.False(NumericValueCodec.TryReadFixedText(Invalid,96,Output));
            Assert.Equal(Before,Output);
        }
    }
    [Fact]
    public void ExistingLogicalScalarUsesTheSharedValueFormat(){
        foreach(var Raw in new[]{Int128.MinValue,Int128.MaxValue,(Int128)0,(Int128)1,(Int128)(-1)}){
            var Value=LogicalScalar.FromRaw(Raw);
            Assert.True(LogicalScalar.TryReadCanonical(Value.ToCanonicalBytes(),out var Actual));
            Assert.Equal(Value,Actual);
        }
        var Encoded=NumericValueCodec.WriteDouble(-123.25);
        Assert.True(LogicalScalar.TryReadCanonical(Encoded,out var Fixed));
        Assert.Equal(LogicalScalar.ParseInvariant("-123.25"),Fixed);
        Assert.Equal(Encoded,Fixed.ToCanonicalBytes());
        Assert.True(LogicalScalar.TryReadCanonical(NumericValueCodec.WriteDouble(double.Epsilon),out var Tiny));
        Assert.Equal(LogicalScalar.Zero,Tiny);
        Assert.False(LogicalScalar.TryReadCanonical(NumericValueCodec.WriteDouble(double.MaxValue),out _));
    }
    [Fact]
    public void CrossFixedPrecisionRoundingMatchesIntegerOracle(){
        var Random=new Random(149);
        foreach(var SourceBytes in new[]{8,16,24}){
            foreach(var TargetBytes in new[]{8,16,24}){
                foreach(var SourceScale in new[]{0,SourceBytes*4,SourceBytes*8-2}){
                    foreach(var TargetScale in new[]{0,TargetBytes*4,TargetBytes*8-2}){
                        for(var Index=0;Index<20;Index++){
                            var RawBytes=new byte[SourceBytes];Random.NextBytes(RawBytes);
                            var Signed=new BigInteger(RawBytes,false,false);
                            var Magnitude=BigInteger.Abs(Signed);
                            var Shift=SourceScale-TargetScale;
                            BigInteger Rounded;
                            if(Shift>0){
                                var Divisor=BigInteger.One<<Shift;
                                Rounded=BigInteger.DivRem(Magnitude,Divisor,out var Remainder);
                                if(Remainder*2>Divisor||Remainder*2==Divisor&&!Rounded.IsEven){ Rounded++; }
                            }else{ Rounded=Magnitude<< -Shift; }
                            if(Signed.Sign<0){ Rounded=-Rounded; }
                            var Limit=BigInteger.One<<(TargetBytes*8-1);
                            var ExpectedSuccess=Rounded>= -Limit&&Rounded<Limit;
                            var Target=Enumerable.Repeat((byte)0x5a,TargetBytes).ToArray();
                            var Before=Target.ToArray();
                            Assert.Equal(ExpectedSuccess,NumericValueCodec.TryReadFixed(NumericValueCodec.WriteFixed(RawBytes,SourceScale),TargetScale,Target));
                            if(ExpectedSuccess){ Assert.Equal(Rounded,new BigInteger(Target,false,false)); }
                            else{ Assert.Equal(Before,Target); }
                        }
                    }
                }
            }
        }
    }
    [Theory]
    [InlineData(8,32)]
    [InlineData(16,64)]
    [InlineData(24,96)]
    [InlineData(8,0)]
    [InlineData(24,190)]
    public void FixedFormatsRoundTripExactly(int Bytes,int FractionalBits){
        var Random=new Random(731);
        for(var Index=0;Index<200;Index++){
            var Input=new byte[Bytes];Random.NextBytes(Input);
            var Encoded=NumericValueCodec.WriteFixed(Input,FractionalBits);
            var Actual=new byte[Bytes];
            Assert.True(NumericValueCodec.TryReadFixed(Encoded,FractionalBits,Actual));
            Assert.Equal(Input,Actual);
        }
    }
    [Fact]
    public void SameValueHasSameEncodingInEverySourceFormat(){
        var Expected=NumericValueCodec.WriteSingle(-1.5f);
        Assert.Equal(Expected,NumericValueCodec.WriteDouble(-1.5));
        foreach(var Bytes in new[]{8,16,24}){
            foreach(var Scale in new[]{1,Bytes*4,Bytes*8-2}){
                var Raw=-(new BigInteger(3)<<(Scale-1));
                // At the largest fractional setting -1.5 is still representable.
                var Input=SignedBytes(Raw,Bytes);
                Assert.Equal(Expected,NumericValueCodec.WriteFixed(Input,Scale));
            }
        }
    }
    [Fact]
    public void IeeeRoundTripsIncludeSubnormalsAndExtremes(){
        var Random=new Random(930);
        foreach(var Value in new[]{double.Epsilon,double.MaxValue,double.MinValue,Math.ScaleB(1,-1022),0.1,-0.1,0.0}){ CheckDouble(Value); }
        foreach(var Value in new[]{float.Epsilon,float.MaxValue,float.MinValue,MathF.ScaleB(1,-126),0.1f,-0.1f,0f}){ CheckSingle(Value); }
        for(var Index=0;Index<1000;Index++){
            var Bits=new byte[8];Random.NextBytes(Bits);
            var Double=BitConverter.ToDouble(Bits);
            if(double.IsFinite(Double)&&Double!=0){ CheckDouble(Double); }
            var Single=BitConverter.ToSingle(Bits);
            if(float.IsFinite(Single)&&Single!=0){ CheckSingle(Single); }
        }
    }
    [Fact]
    public void CrossFormatRoundingMatchesIndependentIeeeCast(){
        var Random=new Random(521);
        for(var Index=0;Index<1000;Index++){
            var Value=Math.ScaleB(Random.NextDouble()*2-1,Random.Next(-160,128));
            Assert.True(NumericValueCodec.TryReadSingle(NumericValueCodec.WriteDouble(Value),out var Actual));
            Assert.Equal((float)Value,Actual);
        }
        Assert.True(NumericValueCodec.TryReadSingle(NumericValueCodec.WriteDouble(1.0+Math.ScaleB(1,-24)),out var Tie));
        Assert.Equal(1f,Tie);
    }
    [Theory]
    [InlineData(0.5,0)]
    [InlineData(1.5,2)]
    [InlineData(2.5,2)]
    [InlineData(-0.5,0)]
    [InlineData(-1.5,-2)]
    public void FixedRoundingUsesNearestEven(double Value,long Expected){
        var Destination=new byte[8];
        Assert.True(NumericValueCodec.TryReadFixed(NumericValueCodec.WriteDouble(Value),0,Destination));
        Assert.Equal(Expected,BitConverter.ToInt64(Destination));
    }
    [Fact]
    public void OverflowAndMalformedPayloadLeaveDestinationUnchanged(){
        var Destination=Enumerable.Repeat((byte)0x5a,8).ToArray();
        var Before=Destination.ToArray();
        Assert.False(NumericValueCodec.TryReadFixed(NumericValueCodec.WriteDouble(double.MaxValue),32,Destination));
        Assert.Equal(Before,Destination);
        Assert.False(NumericValueCodec.TryReadSingle(NumericValueCodec.WriteDouble(double.MaxValue),out _));
        var Valid=NumericValueCodec.WriteDouble(1.5);
        for(var Length=0;Length<Valid.Length;Length++){
            Assert.False(NumericValueCodec.TryReadFixed(Valid.AsSpan(0,Length),32,Destination));
            Assert.Equal(Before,Destination);
        }
        var Trailing=Valid.Concat(new byte[]{0}).ToArray();
        Assert.False(NumericValueCodec.TryReadDouble(Trailing,out _));
        var Padding=Valid.ToArray();Padding[^1]|=0x80;
        Assert.False(NumericValueCodec.TryReadDouble(Padding,out _));
        var NegativeZero=NumericValueCodec.WriteSingle(0);NegativeZero[4]=1;
        Assert.False(NumericValueCodec.TryReadSingle(NegativeZero,out _));
        Assert.Equal(NumericValueCodec.WriteSingle(0),NumericValueCodec.WriteSingle(-0f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>NumericValueCodec.WriteDouble(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(()=>NumericValueCodec.WriteSingle(float.PositiveInfinity));
    }
    private static byte[]SignedBytes(BigInteger Value,int Length){
        var Result=Enumerable.Repeat(Value.Sign<0?(byte)255:(byte)0,Length).ToArray();
        Assert.True(Value.TryWriteBytes(Result,out _,false,false));
        return Result;
    }
    private static void CheckDouble(double Value){
        var Bytes=NumericValueCodec.WriteDouble(Value);
        Assert.True(NumericValueCodec.TryReadDouble(Bytes,out var Actual));
        Assert.Equal(BitConverter.DoubleToInt64Bits(Value),BitConverter.DoubleToInt64Bits(Actual));
        Assert.True(NumericValueCodec.TryReadCanonicalText(NumericValueCodec.ToText(Bytes),out var FromText));Assert.Equal(Bytes,FromText);
    }
    private static void CheckSingle(float Value){
        var Bytes=NumericValueCodec.WriteSingle(Value);
        Assert.True(NumericValueCodec.TryReadSingle(Bytes,out var Actual));
        Assert.Equal(BitConverter.SingleToInt32Bits(Value),BitConverter.SingleToInt32Bits(Actual));
        Assert.True(NumericValueCodec.TryReadCanonicalText(NumericValueCodec.ToText(Bytes),out var FromText));Assert.Equal(Bytes,FromText);
    }
}
