using System;
using System.Globalization;
using System.Numerics;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Decimal text for binary floating-point values, the same on .NET Framework (the VSIX) and .NET (the tools). Digits
/// are rounded once from the exact binary value with ties to even, and a text is read back through a correctly rounded
/// parse, as .NET does both. .NET Framework rounds to 15 or 17 digits before it rounds to fewer, rounds ties away from
/// zero and does not round its double parse correctly, so it wrote other text for some values (the float 312985.125
/// as 312985.13 where .NET writes 312985.12) and some of its double texts read back as another double.
/// </summary>
/// <remarks>Every method works on <see cref="BigInteger"/> or double: the numeric policy's (NUM001) external boundary.</remarks>
internal static class SceneJsonDecimal {
    /// <summary>
    /// The first text of <paramref name="FirstDigits"/> to <paramref name="LastDigits"/> significant digits (laid out
    /// like "G" with that many digits, or with <paramref name="NotationDigits"/> when it is set) whose parse
    /// <paramref name="ReadsBack"/> accepts, skipping exponent texts when <paramref name="PositionalOnly"/>; null when
    /// none is accepted. Zero is "0" or "-0".
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string?Shortest(double Value,int FirstDigits,int LastDigits,bool PositionalOnly,Func<double,bool>ReadsBack,int NotationDigits=0){
        var Bits=BitConverter.DoubleToInt64Bits(Value);
        var Negative=Bits<0;
        if(Value==0d){ return ReadsBack(Value)?(Negative?"-0":"0"):null; }
        Decompose(Bits,out var Mantissa,out var Exponent);
        var Magnitude=DecimalExponent(Mantissa,Exponent);
        for(var Digits=FirstDigits;Digits<=LastDigits;Digits++){
            Round(Mantissa,Exponent,Magnitude,Digits,out var Significand,out var TextExponent);
            var Text=Layout(Negative,Significand,TextExponent,NotationDigits>0?NotationDigits:Digits);
            if(PositionalOnly&&Text.IndexOf('E')>=0){ continue; }
            if(ReadsBack(Parse(Negative,Significand,TextExponent-(Digits-1)))){ return Text; }
        }
        return null;
    }
    /// <summary>The value rounded to <paramref name="Digits"/> significant digits, as .NET's "G" + digits writes it.</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string Format(double Value,int Digits)=>Shortest(Value,Digits,Digits,false,static Parsed=>true)!;
    /// <summary>A finite nonzero double's magnitude as <paramref name="Mantissa"/> × 2^<paramref name="Exponent"/>.</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static void Decompose(long Bits,out BigInteger Mantissa,out int Exponent){
        var Biased=(int)((Bits>>52)&0x7FF);
        var Fraction=Bits&0xFFFFFFFFFFFFFL;
        Mantissa=Biased==0?Fraction:Fraction|(1L<<52);
        Exponent=Biased==0?-1074:Biased-1075;
    }
    /// <summary>K with 10^K &lt;= <paramref name="Mantissa"/> × 2^<paramref name="Exponent"/> &lt; 10^(K+1).</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int DecimalExponent(BigInteger Mantissa,int Exponent){
        var Numerator=Exponent>=0?Mantissa<<Exponent:Mantissa;
        var Denominator=Exponent>=0?BigInteger.One:BigInteger.One<<-Exponent;
        var Magnitude=Numerator.ToString(CultureInfo.InvariantCulture).Length-Denominator.ToString(CultureInfo.InvariantCulture).Length;
        while(ComparePowerOfTen(Numerator,Denominator,Magnitude)<0){ Magnitude--; }
        while(ComparePowerOfTen(Numerator,Denominator,Magnitude+1)>=0){ Magnitude++; }
        return Magnitude;
    }
    /// <summary>Numerator / Denominator compared with 10^Power.</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int ComparePowerOfTen(BigInteger Numerator,BigInteger Denominator,int Power)=>
        Power>=0?Numerator.CompareTo(Denominator*BigInteger.Pow(10,Power)):(Numerator*BigInteger.Pow(10,-Power)).CompareTo(Denominator);
    /// <summary>
    /// The value rounded to <paramref name="Digits"/> significant digits, ties to even: a <paramref name="Digits"/>-digit
    /// <paramref name="Significand"/> whose first digit has the decimal exponent <paramref name="TextExponent"/>.
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static void Round(BigInteger Mantissa,int Exponent,int Magnitude,int Digits,out BigInteger Significand,out int TextExponent){
        var Numerator=Exponent>=0?Mantissa<<Exponent:Mantissa;
        var Denominator=Exponent>=0?BigInteger.One:BigInteger.One<<-Exponent;
        var Scale=Digits-1-Magnitude;
        if(Scale>=0){ Numerator*=BigInteger.Pow(10,Scale); }else{ Denominator*=BigInteger.Pow(10,-Scale); }
        Significand=BigInteger.DivRem(Numerator,Denominator,out var Remainder);
        var Half=(Remainder<<1).CompareTo(Denominator);
        if(Half>0||Half==0&&!Significand.IsEven){ Significand+=BigInteger.One; }
        TextExponent=Magnitude;
        if(Significand==BigInteger.Pow(10,Digits)){ Significand/=10;TextExponent++; }
    }
    /// <summary>.NET's "G" layout: positional when -5 &lt; exponent &lt; <paramref name="NotationDigits"/>, no trailing zeros.</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static string Layout(bool Negative,BigInteger Significand,int TextExponent,int NotationDigits){
        var Digits=Significand.ToString(CultureInfo.InvariantCulture).TrimEnd('0');
        var Sign=Negative?"-":string.Empty;
        if(TextExponent>-5&&TextExponent<NotationDigits){
            if(TextExponent<0){ return Sign+"0."+new string('0',-TextExponent-1)+Digits; }
            if(Digits.Length<=TextExponent+1){ return Sign+Digits+new string('0',TextExponent+1-Digits.Length); }
            return Sign+Digits.Substring(0,TextExponent+1)+"."+Digits.Substring(TextExponent+1);
        }
        var Fraction=Digits.Length>1?"."+Digits.Substring(1):string.Empty;
        return Sign+Digits.Substring(0,1)+Fraction+"E"+(TextExponent<0?"-":"+")+Math.Abs(TextExponent).ToString("00",CultureInfo.InvariantCulture);
    }
    /// <summary>The double nearest to <paramref name="Significand"/> × 10^<paramref name="Power"/> (ties to even), or an infinity.</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static double Parse(bool Negative,BigInteger Significand,int Power){
        var Numerator=Power>=0?Significand*BigInteger.Pow(10,Power):Significand;
        var Denominator=Power>=0?BigInteger.One:BigInteger.Pow(10,-Power);
        // Binary exponent with 2^52 <= value / 2^Exponent < 2^53, or the subnormal exponent.
        var Exponent=BitLength(Numerator)-BitLength(Denominator)-53;
        while(ComparePowerOfTwo(Numerator,Denominator,Exponent+52)<0){ Exponent--; }
        while(ComparePowerOfTwo(Numerator,Denominator,Exponent+53)>=0){ Exponent++; }
        if(Exponent<-1074){ Exponent=-1074; }
        if(Exponent<0){ Numerator<<=-Exponent; }else{ Denominator<<=Exponent; }
        var Mantissa=BigInteger.DivRem(Numerator,Denominator,out var Remainder);
        var Half=(Remainder<<1).CompareTo(Denominator);
        if(Half>0||Half==0&&!Mantissa.IsEven){ Mantissa+=BigInteger.One; }
        if(Mantissa==BigInteger.One<<53){ Mantissa>>=1;Exponent++; }
        if(Exponent>971){ return Negative?double.NegativeInfinity:double.PositiveInfinity; }
        var Bits=Mantissa<BigInteger.One<<52?(long)Mantissa:((long)(Exponent+1075)<<52)|((long)Mantissa&0xFFFFFFFFFFFFFL);
        return BitConverter.Int64BitsToDouble(Negative?Bits|long.MinValue:Bits);
    }
    /// <summary>Numerator / Denominator compared with 2^Power.</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int ComparePowerOfTwo(BigInteger Numerator,BigInteger Denominator,int Power)=>
        Power>=0?Numerator.CompareTo(Denominator<<Power):(Numerator<<-Power).CompareTo(Denominator);
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int BitLength(BigInteger Value){
        var Bytes=Value.ToByteArray();
        var Top=Bytes.Length-1;
        while(Top>0&&Bytes[Top]==0){ Top--; }
        var Length=Top*8;
        for(int Byte=Bytes[Top];Byte!=0;Byte>>=1){ Length++; }
        return Length;
    }
}
