using System.Buffers.Binary;
using System.Numerics;
using System.Globalization;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Numerics;
/// <summary>有限の数値を内部の数値型に依存しない符号・整数部・2進小数部として保存する。</summary>
public static class NumericValueCodec {
    private const int HeaderSize=9;
    private const int MaximumIntegerBytes=128;
    private const int MaximumFractionBits=1074;
    /// <summary>保存値を丸めず、地域設定に依存しない有限小数で書き出す。</summary>
    [ExternalNumericAdapter]
    public static string ToText(ReadOnlySpan<byte>Source){
        if(!TryDecode(Source,out var Signed,out var Scale)){ throw new ArgumentException("Invalid canonical number.",nameof(Source)); }
        var Digits=(BigInteger.Abs(Signed)*BigInteger.Pow(5,Scale)).ToString(CultureInfo.InvariantCulture);
        if(Scale!=0){
            Digits=Digits.PadLeft(Scale+1,'0');
            Digits=Digits.Insert(Digits.Length-Scale,".").TrimEnd('0').TrimEnd('.');
        }
        return Signed.Sign<0?"-"+Digits:Digits;
    }
    /// <summary>保存用の正規10進表現を、丸めずに共通数値へ戻す。読み先の数値型には依存しない。</summary>
    [ExternalNumericAdapter]
    public static bool TryReadCanonicalText(string?Text,out byte[]Value){
        Value=[];
        if(string.IsNullOrEmpty(Text)||Text.Length>4096){ return false; }
        var Digits=Text.AsSpan();var Negative=Digits[0]=='-';
        if(Negative){ Digits=Digits[1..]; }
        if(Digits.IsEmpty){ return false; }
        var Numerator=BigInteger.Zero;var Scale=0;var SeenDot=false;var Count=0;
        foreach(var Character in Digits){
            if(Character=='.'&&!SeenDot){ SeenDot=true;continue; }
            if(Character<'0'||Character>'9'){ return false; }
            Numerator=Numerator*10+(Character-'0');Count++;
            if(SeenDot&&++Scale>MaximumFractionBits){ return false; }
        }
        if(Count==0){ return false; }
        var Signed=BigInteger.DivRem(Numerator,BigInteger.Pow(5,Scale),out var Remainder);
        if(!Remainder.IsZero){ return false; }
        if(Negative){ Signed=-Signed; }
        byte[]Encoded;
        try{ Encoded=Encode(Signed,Scale); }catch(ArgumentOutOfRangeException){ return false; }
        if(!string.Equals(Text,ToText(Encoded),StringComparison.Ordinal)){ return false; }
        Value=Encoded;return true;
    }
    /// <summary>10進文字列から直接丸める。double を中間形式にせず、失敗時は出力を変更しない。</summary>
    [ExternalNumericAdapter]
    public static bool TryReadFixedText(string?Text,int FractionalBits,Span<byte>Destination){
        if(Destination.Length is not (8 or 16 or 24)){ return false; }
        return TryReadFixedTextCore(Text,FractionalBits,Destination);
    }
    [ExternalNumericAdapter]
    internal static bool TryReadWideFixedText(string?Text,int FractionalBits,Span<byte>Destination)=>Destination.Length==32&&TryReadFixedTextCore(Text,FractionalBits,Destination);
    [ExternalNumericAdapter]
    private static bool TryReadFixedTextCore(string?Text,int FractionalBits,Span<byte>Destination){
        if(string.IsNullOrEmpty(Text)||Text.Length>4096
            ||FractionalBits<0||FractionalBits>Destination.Length*8-2){ return false; }
        var Digits=Text.AsSpan();
        var Negative=Digits[0]=='-';
        if(Digits[0] is '-' or '+'){ Digits=Digits[1..]; }
        if(Digits.IsEmpty){ return false; }
        var Numerator=BigInteger.Zero;
        var DecimalPlaces=0;
        var SeenDot=false;
        var DigitCount=0;
        foreach(var Character in Digits){
            if(Character=='.'&&!SeenDot){ SeenDot=true;continue; }
            if(Character<'0'||Character>'9'){ return false; }
            Numerator=Numerator*10+(Character-'0');
            DigitCount++;
            if(SeenDot){ DecimalPlaces++; }
        }
        if(DigitCount==0){ return false; }
        var Divisor=BigInteger.Pow(10,DecimalPlaces);
        var Raw=BigInteger.DivRem(Numerator<<FractionalBits,Divisor,out var Remainder);
        if(Remainder*2>Divisor||Remainder*2==Divisor&&!Raw.IsEven){ Raw++; }
        if(Negative){ Raw=-Raw; }
        var Limit=BigInteger.One<<(Destination.Length*8-1);
        if(Raw< -Limit||Raw>=Limit){ return false; }
        Span<byte>Buffer=stackalloc byte[32];
        Buffer.Fill(Raw.Sign<0?(byte)255:(byte)0);
        if(!Raw.TryWriteBytes(Buffer,out _,false,false)){ return false; }
        Buffer[..Destination.Length].CopyTo(Destination);
        return true;
    }
    public static byte[]WriteFixed(ReadOnlySpan<byte>SignedLittleEndian,int FractionalBits){
        if(SignedLittleEndian.Length is not (8 or 16 or 24)||FractionalBits<0||FractionalBits>SignedLittleEndian.Length*8-2){ throw new ArgumentOutOfRangeException(nameof(FractionalBits)); }
        return WriteFixedCore(SignedLittleEndian,FractionalBits);
    }
    [ExternalNumericAdapter]
    private static byte[]WriteFixedCore(ReadOnlySpan<byte>Source,int FractionalBits)=>Encode(new BigInteger(Source,false,false),FractionalBits);
    public static byte[]WriteSingle(float Value){
        if(!float.IsFinite(Value)){ throw new ArgumentOutOfRangeException(nameof(Value)); }
        var Bits=BitConverter.SingleToUInt32Bits(Value);
        return WriteIeee(Bits,23,8,127);
    }
    [ExternalNumericAdapter]
    public static byte[]WriteDouble(double Value){
        if(!double.IsFinite(Value)){ throw new ArgumentOutOfRangeException(nameof(Value)); }
        return WriteIeee(BitConverter.DoubleToUInt64Bits(Value),52,11,1023);
    }
    [ExternalNumericAdapter]
    private static byte[]WriteIeee(ulong Bits,int MantissaBits,int ExponentBits,int Bias){
        var Exponent=(int)((Bits>>MantissaBits)&((1UL<<ExponentBits)-1));
        var Magnitude=new BigInteger(Bits&((1UL<<MantissaBits)-1));
        if(Exponent!=0){ Magnitude|=BigInteger.One<<MantissaBits; }
        var Power=(Exponent==0?1-Bias:Exponent-Bias)-MantissaBits;
        if((Bits>>(MantissaBits+ExponentBits))!=0){ Magnitude=-Magnitude; }
        return Power>0?Encode(Magnitude<<Power,0):Encode(Magnitude,-Power);
    }
    /// <summary>失敗時は Destination を変更しない。丸めは最近接・偶数。</summary>
    [ExternalNumericAdapter]
    public static bool TryReadFixed(ReadOnlySpan<byte>Source,int FractionalBits,Span<byte>Destination){
        if(Destination.Length is not (8 or 16 or 24)||FractionalBits<0||FractionalBits>Destination.Length*8-2){ return false; }
        if(!TryDecode(Source,out var Signed,out var Scale)){ return false; }
        var Negative=Signed.Sign<0;
        var Magnitude=BigInteger.Abs(Signed);
        var Raw=Scale>FractionalBits?RoundRight(Magnitude,Scale-FractionalBits):Magnitude<<(FractionalBits-Scale);
        if(Negative){ Raw=-Raw; }
        var Limit=BigInteger.One<<(Destination.Length*8-1);
        if(Raw< -Limit||Raw>=Limit){ return false; }
        Span<byte>Buffer=stackalloc byte[24];
        Buffer.Fill(Raw.Sign<0?(byte)255:(byte)0);
        if(!Raw.TryWriteBytes(Buffer,out _,false,false)){ return false; }
        Buffer[..Destination.Length].CopyTo(Destination);
        return true;
    }
    public static bool TryReadSingle(ReadOnlySpan<byte>Source,out float Value){
        Value=default;
        if(!TryReadIeee(Source,23,8,127,out var Bits)){ return false; }
        Value=BitConverter.UInt32BitsToSingle((uint)Bits);
        return true;
    }
    [ExternalNumericAdapter]
    public static bool TryReadDouble(ReadOnlySpan<byte>Source,out double Value){
        Value=default;
        if(!TryReadIeee(Source,52,11,1023,out var Bits)){ return false; }
        Value=BitConverter.UInt64BitsToDouble(Bits);
        return true;
    }
    [ExternalNumericAdapter]
    private static bool TryReadIeee(ReadOnlySpan<byte>Source,int MantissaBits,int ExponentBits,int Bias,out ulong Bits){
        Bits=0;
        if(!TryDecode(Source,out var Signed,out var Scale)){ return false; }
        if(Signed.IsZero){ return true; }
        var Magnitude=BigInteger.Abs(Signed);
        var Highest=(int)Magnitude.GetBitLength()-1;
        var Exponent=Highest-Scale;
        // Subnormals have a fixed quantum; normal values keep MantissaBits+1 significant bits.
        var Quantum=Math.Max(Exponent-MantissaBits,1-Bias-MantissaBits);
        var Shift=Scale+Quantum;
        var Rounded=Shift>0?RoundRight(Magnitude,Shift):Magnitude<< -Shift;
        if(Rounded.IsZero){ return true; }
        var RoundedHighest=(int)Rounded.GetBitLength()-1;
        Exponent=RoundedHighest+Quantum;
        if(Exponent>Bias){ return false; }
        if(Signed.Sign<0){ Bits=1UL<<(MantissaBits+ExponentBits); }
        if(Exponent<1-Bias){ Bits|=(ulong)Rounded;return true; }
        var Significand=RoundedHighest> MantissaBits?Rounded>>(RoundedHighest-MantissaBits):Rounded<<(MantissaBits-RoundedHighest);
        Bits|=(ulong)(Exponent+Bias)<<MantissaBits;
        Bits|=(ulong)(Significand- (BigInteger.One<<MantissaBits));
        return true;
    }
    [ExternalNumericAdapter]
    private static BigInteger RoundRight(BigInteger Value,int Shift){
        var Quotient=Value>>Shift;
        var Remainder=Value-(Quotient<<Shift);
        var Half=BigInteger.One<<(Shift-1);
        return Remainder>Half||Remainder==Half&&!Quotient.IsEven?Quotient+1:Quotient;
    }
    [ExternalNumericAdapter]
    private static byte[]Encode(BigInteger Signed,int Scale){
        var Negative=Signed.Sign<0;
        var Magnitude=BigInteger.Abs(Signed);
        while(Scale>0&&Magnitude.IsEven&&!Magnitude.IsZero){ Magnitude>>=1;Scale--; }
        if(Magnitude.IsZero){ Scale=0; }
        var Integer=Magnitude>>Scale;
        var Fraction=Magnitude-(Integer<<Scale);
        var IntegerBytes=Integer.IsZero?Array.Empty<byte>():Integer.ToByteArray(true,false);
        var FractionLength=(Scale+7)/8;
        if(IntegerBytes.Length>MaximumIntegerBytes||Scale>MaximumFractionBits){ throw new ArgumentOutOfRangeException(nameof(Signed)); }
        var Result=new byte[HeaderSize+IntegerBytes.Length+FractionLength];
        "CGN1"u8.CopyTo(Result);
        Result[4]=Negative?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(Result.AsSpan(5),(ushort)Scale);
        BinaryPrimitives.WriteUInt16LittleEndian(Result.AsSpan(7),(ushort)IntegerBytes.Length);
        IntegerBytes.CopyTo(Result,HeaderSize);
        if(FractionLength!=0){ Fraction.TryWriteBytes(Result.AsSpan(HeaderSize+IntegerBytes.Length),out _,true,false); }
        return Result;
    }
    [ExternalNumericAdapter]
    private static bool TryDecode(ReadOnlySpan<byte>Source,out BigInteger Signed,out int Scale){
        Signed=default;Scale=0;
        if(Source.Length<HeaderSize||!Source[..4].SequenceEqual("CGN1"u8)||Source[4]>1){ return false; }
        Scale=BinaryPrimitives.ReadUInt16LittleEndian(Source[5..]);
        var IntegerLength=BinaryPrimitives.ReadUInt16LittleEndian(Source[7..]);
        var FractionLength=(Scale+7)/8;
        if(IntegerLength>MaximumIntegerBytes||Scale>MaximumFractionBits||Source.Length!=HeaderSize+IntegerLength+FractionLength){ return false; }
        var IntegerBytes=Source.Slice(HeaderSize,IntegerLength);
        var FractionBytes=Source[(HeaderSize+IntegerLength)..];
        if(IntegerLength!=0&&IntegerBytes[^1]==0){ return false; }
        var Integer=new BigInteger(IntegerBytes,true,false);
        var Fraction=new BigInteger(FractionBytes,true,false);
        if(Scale!=0&&(Fraction.IsEven||Fraction.GetBitLength()>Scale)){ return false; }
        Signed=(Integer<<Scale)|Fraction;
        if(Source[4]!=0){
            if(Signed.IsZero){ return false; }
            Signed=-Signed;
        }
        return true;
    }
}
