using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static CSharpGameEngine.SceneAnalysis.Json.SceneJsonFixedAngles;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>制作時の10進入力を選択した固定小数点の範囲で検査し、floatを介さない式にする。</summary>
internal static class SceneJsonFixedValue {

#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool TryAngleFromDegrees(ITypeSymbol Type,string Text,SceneAngleUnit Unit,out string Expression){
        Expression=string.Empty;
        if(Unit==SceneAngleUnit.Degrees){ return TryExpression(Type,Text,out Expression,out _); }
        if(!TryProfile(Type,out var Bits,out var Fraction)||!TryRational(Text,out var Numerator,out var Denominator)){ return false; }
        var Raw=Round(Numerator*PiNumerator*(BigInteger.One<<Fraction),Denominator*PiDenominator*180);
        return InRange(Raw,Bits)&&TryExpression(Type,FixedText(Raw,Fraction),out Expression,out _);
    }

#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool TryAngleFromTurns(ITypeSymbol Type,string Text,SceneAngleUnit Unit,out string Expression,out string Error){
        Expression=string.Empty;Error="the angle is beyond the selected fixed range";
        // 回転数からの変換は SceneJsonFixedAngles に置き、エディターのプレビューの読み手と同じ計算にする。
        return TryProfile(Type,out var Bits,out var Fraction)&&TryTurnsToFixedText(Text,Unit,Bits,Fraction,out var Fixed)&&TryExpression(Type,Fixed,out Expression,out Error);
    }

#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool TryAngleToTurns(ITypeSymbol Type,string Text,SceneAngleUnit Unit,out string Turns){
        Turns=string.Empty;
        if(!TryProfile(Type,out var Bits,out var Fraction)||!TryRational(Text,out var Numerator,out var Denominator)){ return false; }
        var Raw=Round(Numerator*(BigInteger.One<<Fraction),Denominator);
        if(!InRange(Raw,Bits)){ return false; }
        var ScaleNumerator=Unit==SceneAngleUnit.Radians?PiNumerator*2:new BigInteger(360);
        var ScaleDenominator=Unit==SceneAngleUnit.Radians?PiDenominator:BigInteger.One;
        var Divisor=(BigInteger.One<<Fraction)*ScaleNumerator;
        for(var Digits=0;Digits<=Fraction+5;Digits++){
            var Power=BigInteger.Pow(10,Digits);
            var Decimal=Round(Raw*ScaleDenominator*Power,Divisor);
            var Restored=Round(Decimal*ScaleNumerator*(BigInteger.One<<Fraction),Power*ScaleDenominator);
            if(Restored==Raw){ Turns=DecimalText(Decimal,Digits);return true; }
        }
        return false;
    }

#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string FormatAngleDegrees(ITypeSymbol Type,string Text,SceneAngleUnit Unit){
        if(Unit==SceneAngleUnit.Degrees||!TryProfile(Type,out var Bits,out var Fraction)
            ||!TryRational(Text,out var Numerator,out var Denominator)){ return Text; }
        var Raw=Round(Numerator*(BigInteger.One<<Fraction),Denominator);
        if(!InRange(Raw,Bits)){ return Text; }
        var Pi=PiNumerator;var PiScale=PiDenominator;
        var DegreeNumerator=Raw*180*PiScale;
        var DegreeDenominator=(BigInteger.One<<Fraction)*Pi;
        for(var Digits=0;Digits<=Fraction+5;Digits++){
            var Power=BigInteger.Pow(10,Digits);
            var Decimal=Round(DegreeNumerator*Power,DegreeDenominator);
            var Restored=Round(Decimal*Pi*(BigInteger.One<<Fraction),Power*PiScale*180);
            if(Restored==Raw){ return DecimalText(Decimal,Digits); }
        }
        return Text;
    }

    private static bool TryProfile(ITypeSymbol Type,out int Bits,out int Fraction){
        Bits=Type.GetMembers("TotalBits").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue is int B?B:0;
        Fraction=Type.GetMembers("FractionalBits").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue is int F?F:-1;
        return IsProfile(Bits,Fraction);
    }
    internal static bool TryExpression(ITypeSymbol Type,string Text,out string Expression,out string Error)=>TryExpression(Type,Text,out Expression,out Error,out _);
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool TryExpression(ITypeSymbol Type,string Text,out string Expression,out string Error,out string Normalized){
        Expression="default";Normalized=string.Empty;Error="the fixed numeric type must declare its total and fractional bits";
        if(Type.GetMembers("TotalBits").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue is not int Bits
            ||Type.GetMembers("FractionalBits").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue is not int Fraction
            ||Bits is not (64 or 128 or 192)||Fraction<0||Fraction>Bits-2){ return false; }
        Error="the number is too long or has an unsupported exponent";
        if(string.IsNullOrEmpty(Text)||Text.Length>4096){ return false; }
        var Negative=Text[0]=='-';var Magnitude=Negative?Text.Substring(1):Text;
        var ExponentIndex=Magnitude.IndexOfAny(['e','E']);var Exponent=0;
        if(ExponentIndex>=0){
            if(!int.TryParse(Magnitude.Substring(ExponentIndex+1),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out Exponent)){ return false; }
            Magnitude=Magnitude.Substring(0,ExponentIndex);
        }
        var Point=Magnitude.IndexOf('.');var Scale=Point<0?0:Magnitude.Length-Point-1;
        var Digits=Point<0?Magnitude:Magnitude.Remove(Point,1);
        if(Digits.Length==0||Digits.Any(static Digit=>Digit<'0'||Digit>'9')){ Error="expected a finite decimal number";return false; }
        var Coefficient=BigInteger.Parse(Digits,NumberStyles.None,CultureInfo.InvariantCulture);
        var Plain="0";
        if(!Coefficient.IsZero){
            if(Exponent is < -4096 or > 4096){ return false; }
            Scale-=Exponent;if(Scale is < -4096 or > 4096){ return false; }
            var Scaled=Coefficient<<Fraction;
            if(Scale<0){ Scaled*=BigInteger.Pow(10,-Scale); }
            else if(Scale>0){
                var Denominator=BigInteger.Pow(10,Scale);var Quotient=BigInteger.DivRem(Scaled,Denominator,out var Remainder);
                if(Remainder*2>Denominator||Remainder*2==Denominator&&!Quotient.IsEven){ Quotient++; }
                Scaled=Quotient;
            }
            if(Negative){ Scaled=-Scaled; }
            var Limit=BigInteger.One<<(Bits-1);
            if(Scaled < -Limit||Scaled>=Limit){ Error=$"the number is beyond the selected {Bits}-bit fixed range ({Fraction} fractional bits)";return false; }
            Plain=Coefficient.ToString(CultureInfo.InvariantCulture);
            if(Scale<0){ Plain+=new string('0',-Scale); }
            else if(Scale>0){
                if(Plain.Length<=Scale){ Plain=new string('0',Scale+1-Plain.Length)+Plain; }
                Plain=Plain.Insert(Plain.Length-Scale,".").TrimEnd('0').TrimEnd('.');
            }
            if(Negative){ Plain="-"+Plain; }
            if(Plain.Length>4096){ return false; }
        }
        Expression="global::CSharpGameEngine.Numerics.LogicalScalar.ParseInvariant("+SymbolDisplay.FormatLiteral(Plain,true)+")";
        Normalized=Plain;
        Error=string.Empty;return true;
    }
}
