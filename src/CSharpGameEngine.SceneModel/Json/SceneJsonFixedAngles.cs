using System;
using System.Globalization;
using System.Numerics;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// 固定小数点の値を 10 進の入力から正確に作る計算（Roslyn に依存しない）。生成器（<see cref="SceneJsonFixedValue"/>）と、エディターの
/// プレビューの実行時の読み手（<c>SceneJsonRuntimeValues</c>）が同じこのコードを使うので、回転数の角度から作る値が両者で一致する
/// （シーン JSON の読み手は一致しなければならない）。
/// </summary>
internal static class SceneJsonFixedAngles {
    private const string PiDigits="31415926535897932384626433832795028841971693993751058209749445923078164062862089986280";
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static BigInteger PiNumerator=>BigInteger.Parse(PiDigits,CultureInfo.InvariantCulture);
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static BigInteger PiDenominator=>BigInteger.Pow(10,PiDigits.Length-1);
    /// <summary>総ビット数と小数部のビット数が、固定小数点の数値設定として有効かどうか。</summary>
    internal static bool IsProfile(int Bits,int Fraction)=>Bits is 64 or 128 or 192&&Fraction>=0&&Fraction<=Bits-2;
    /// <summary>
    /// 回転数で書いた角度 <paramref name="Text"/> を、メンバーの単位の固定小数点の値（総 <paramref name="Bits"/> ビット、小数部 <paramref name="Fraction"/> ビット）に
    /// 最も近い値へ偶数丸めし、その値を正確に表す 10 進表記にする。範囲外や数値でない入力は false。
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool TryTurnsToFixedText(string Text,SceneAngleUnit Unit,int Bits,int Fraction,out string FixedTextValue){
        FixedTextValue=string.Empty;
        if(!IsProfile(Bits,Fraction)||!TryRational(Text,out var Numerator,out var Denominator)){ return false; }
        var ScaleNumerator=Unit==SceneAngleUnit.Radians?PiNumerator*2:new BigInteger(360);
        var ScaleDenominator=Unit==SceneAngleUnit.Radians?PiDenominator:BigInteger.One;
        var Raw=Round(Numerator*ScaleNumerator*(BigInteger.One<<Fraction),Denominator*ScaleDenominator);
        if(!InRange(Raw,Bits)){ return false; }
        FixedTextValue=FixedText(Raw,Fraction);
        return true;
    }
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool TryRational(string Text,out BigInteger Numerator,out BigInteger Denominator){
        Numerator=BigInteger.Zero;Denominator=BigInteger.One;
        Text=Text.Trim();
        if(Text.StartsWith("+",StringComparison.Ordinal)){ Text=Text.Substring(1); }
        if(Text.Length>4096||!SceneJsonReader.IsNumber(Text)){ return false; }
        var Negative=Text.StartsWith("-",StringComparison.Ordinal);
        var Magnitude=Negative?Text.Substring(1):Text;
        var E=Magnitude.IndexOfAny(['e','E']);var Exponent=0;
        if(E>=0){
            if(!int.TryParse(Magnitude.Substring(E+1),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out Exponent)||Exponent is < -4096 or > 4096){ return false; }
            Magnitude=Magnitude.Substring(0,E);
        }
        var Point=Magnitude.IndexOf('.');
        var Scale=(Point<0?0:Magnitude.Length-Point-1)-Exponent;
        if(Scale is < -4096 or > 4096){ return false; }
        Numerator=BigInteger.Parse(Point<0?Magnitude:Magnitude.Remove(Point,1),CultureInfo.InvariantCulture);
        if(Negative){ Numerator=-Numerator; }
        if(Scale>0){ Denominator=BigInteger.Pow(10,Scale); }
        else if(Scale<0){ Numerator*=BigInteger.Pow(10,-Scale); }
        return true;
    }
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static BigInteger Round(BigInteger Numerator,BigInteger Denominator){
        var Negative=Numerator.Sign<0;
        var Quotient=BigInteger.DivRem(BigInteger.Abs(Numerator),Denominator,out var Remainder);
        if(Remainder*2>Denominator||Remainder*2==Denominator&&!Quotient.IsEven){ Quotient++; }
        return Negative?-Quotient:Quotient;
    }
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool InRange(BigInteger Raw,int Bits){
        var Limit=BigInteger.One<<(Bits-1);
        return Raw>=-Limit&&Raw<Limit;
    }
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string FixedText(BigInteger Raw,int Fraction)=>DecimalText(Raw*BigInteger.Pow(5,Fraction),Fraction);
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string DecimalText(BigInteger Value,int Scale){
        if(Value.IsZero){ return "0"; }
        var Negative=Value.Sign<0;var Text=BigInteger.Abs(Value).ToString(CultureInfo.InvariantCulture);
        if(Scale>0){
            if(Text.Length<=Scale){ Text=new string('0',Scale+1-Text.Length)+Text; }
            Text=Text.Insert(Text.Length-Scale,".").TrimEnd('0').TrimEnd('.');
        }
        return Negative?"-"+Text:Text;
    }
}
