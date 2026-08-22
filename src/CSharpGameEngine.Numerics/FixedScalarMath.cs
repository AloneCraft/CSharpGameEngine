using System.Globalization;
using System.Numerics;

namespace CSharpGameEngine.Numerics;

internal static class FixedScalarMath {
    internal static readonly BigInteger UInt64Mask=(BigInteger.One<<64)-BigInteger.One;

    internal static BigInteger ToRaw(int TotalBits,ulong Limb0,ulong Limb1){
        BigInteger Raw=new BigInteger(Limb0)|(new BigInteger(Limb1)<<64);
        return Raw>MaximumRaw(TotalBits)?Raw-Modulus(TotalBits):Raw;
    }

    internal static BigInteger ToRaw(int TotalBits,ulong Limb0,ulong Limb1,ulong Limb2){
        BigInteger Raw=new BigInteger(Limb0)|(new BigInteger(Limb1)<<64)|(new BigInteger(Limb2)<<128);
        return Raw>MaximumRaw(TotalBits)?Raw-Modulus(TotalBits):Raw;
    }

    internal static BigInteger ToRaw(int TotalBits,ulong Limb0,ulong Limb1,ulong Limb2,ulong Limb3){
        BigInteger Raw=new BigInteger(Limb0)|(new BigInteger(Limb1)<<64)|(new BigInteger(Limb2)<<128)|(new BigInteger(Limb3)<<192);
        return Raw>MaximumRaw(TotalBits)?Raw-Modulus(TotalBits):Raw;
    }

    internal static bool TryFromRaw(BigInteger Raw,int TotalBits,out ulong Limb0,out ulong Limb1){
        Limb0=Limb1=0;
        if(Raw<MinimumRaw(TotalBits)||Raw>MaximumRaw(TotalBits)){ return false; }
        BigInteger Unsigned=Raw.Sign<0?Raw+Modulus(TotalBits):Raw;
        Limb0=(ulong)(Unsigned&UInt64Mask);
        Limb1=(ulong)((Unsigned>>64)&UInt64Mask);
        return true;
    }

    internal static bool TryFromRaw(BigInteger Raw,int TotalBits,out ulong Limb0,out ulong Limb1,out ulong Limb2){
        Limb0=Limb1=Limb2=0;
        if(Raw<MinimumRaw(TotalBits)||Raw>MaximumRaw(TotalBits)){ return false; }
        BigInteger Unsigned=Raw.Sign<0?Raw+Modulus(TotalBits):Raw;
        Limb0=(ulong)(Unsigned&UInt64Mask);
        Limb1=(ulong)((Unsigned>>64)&UInt64Mask);
        Limb2=(ulong)((Unsigned>>128)&UInt64Mask);
        return true;
    }

    internal static bool TryFromRaw(BigInteger Raw,int TotalBits,out ulong Limb0,out ulong Limb1,out ulong Limb2,out ulong Limb3){
        Limb0=Limb1=Limb2=Limb3=0;
        if(Raw<MinimumRaw(TotalBits)||Raw>MaximumRaw(TotalBits)){ return false; }
        BigInteger Unsigned=Raw.Sign<0?Raw+Modulus(TotalBits):Raw;
        Limb0=(ulong)(Unsigned&UInt64Mask);
        Limb1=(ulong)((Unsigned>>64)&UInt64Mask);
        Limb2=(ulong)((Unsigned>>128)&UInt64Mask);
        Limb3=(ulong)((Unsigned>>192)&UInt64Mask);
        return true;
    }

    internal static BigInteger Modulus(int TotalBits)=>BigInteger.One<<TotalBits;
    internal static BigInteger MinimumRaw(int TotalBits)=>-(BigInteger.One<<(TotalBits-1));
    internal static BigInteger MaximumRaw(int TotalBits)=>(BigInteger.One<<(TotalBits-1))-BigInteger.One;

    internal static bool TryFromDouble(double Value,int TotalBits,int FractionalBits,out BigInteger Raw){
        Raw=BigInteger.Zero;
        if(!double.IsFinite(Value)){ return false; }
        if(Value==0d){ return true; }
        ulong Bits=(ulong)BitConverter.DoubleToInt64Bits(Value);
        bool Negative=(Bits&(1UL<<63))!=0;
        int Exponent=(int)((Bits>>52)&0x7ffUL);
        ulong Significand=Bits&0x000f_ffff_ffff_ffffUL;
        int BinaryExponent;
        if(Exponent==0){ BinaryExponent=-1074; }
        else { Significand|=1UL<<52; BinaryExponent=Exponent-1023-52; }
        BigInteger Magnitude=new(Significand);
        int Shift=BinaryExponent+FractionalBits;
        Magnitude=Shift>=0?Magnitude<<Shift:DivideRoundToNearest(Magnitude,BigInteger.One<<-Shift);
        Raw=Negative?-Magnitude:Magnitude;
        return Raw>=MinimumRaw(TotalBits)&&Raw<=MaximumRaw(TotalBits);
    }

    internal static bool TryParseInvariant(string? Text,int TotalBits,int FractionalBits,out BigInteger Raw){
        Raw=BigInteger.Zero;
        if(string.IsNullOrWhiteSpace(Text)){ return false; }
        string Value=Text.Trim();
        char Last=Value[^1];
        if(Last is 'f' or 'F' or 'd' or 'D' or 'm' or 'M'){ Value=Value[..^1]; }
        bool Negative=false;
        if(Value.StartsWith("+",StringComparison.Ordinal)){ Value=Value[1..]; }
        else if(Value.StartsWith("-",StringComparison.Ordinal)){ Negative=true; Value=Value[1..]; }
        int ExponentMarker=Value.IndexOfAny(new[]{'e','E'});
        int Exponent=0;
        if(ExponentMarker>=0){
            if(Value.IndexOfAny(new[]{'e','E'},ExponentMarker+1)>=0||!int.TryParse(Value[(ExponentMarker+1)..],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out Exponent)){ return false; }
            Value=Value[..ExponentMarker];
        }
        int Point=Value.IndexOf('.');
        if(Point>=0&&Value.IndexOf('.',Point+1)>=0){ return false; }
        string Whole=Point>=0?Value[..Point]:Value;
        string Fraction=Point>=0?Value[(Point+1)..]:string.Empty;
        if(Whole.Length==0){ Whole="0"; }
        string Digits=Whole+Fraction;
        if(Digits.Length==0){ return false; }
        for(int Index=0;Index<Digits.Length;Index++){ if(Digits[Index]<'0'||Digits[Index]>'9'){ return false; } }
        if(Exponent>1024){ return false; }
        if(Exponent<-1024){ return true; }
        if(!BigInteger.TryParse(Digits,NumberStyles.None,CultureInfo.InvariantCulture,out BigInteger Significand)){ return false; }
        int DecimalExponent=Exponent-Fraction.Length;
        Raw=DecimalExponent>=0?(Significand*BigInteger.Pow(10,DecimalExponent))<<FractionalBits:DivideRoundToNearest(Significand<<FractionalBits,BigInteger.Pow(10,-DecimalExponent));
        if(Negative){ Raw=-Raw; }
        return Raw>=MinimumRaw(TotalBits)&&Raw<=MaximumRaw(TotalBits);
    }
    internal static string ToStringInvariant(BigInteger Raw,int FractionalBits){
        if(Raw.IsZero){ return "0"; }
        bool Negative=Raw.Sign<0;
        if(Negative){ Raw=BigInteger.Abs(Raw); }
        BigInteger Scale=BigInteger.One<<FractionalBits;
        BigInteger IntegerPart=Raw>>FractionalBits;
        BigInteger FractionPart=Raw&(Scale-BigInteger.One);
        if(FractionPart.IsZero){ return (Negative?"-":"")+IntegerPart.ToString(CultureInfo.InvariantCulture); }
        string Fraction=(FractionPart*BigInteger.Pow(5,FractionalBits))
            .ToString(CultureInfo.InvariantCulture).PadLeft(FractionalBits,'0').TrimEnd('0');
        return (Negative?"-":"")+IntegerPart.ToString(CultureInfo.InvariantCulture)+"."+Fraction;
    }

    internal static BigInteger DivideRoundToNearest(BigInteger Numerator,BigInteger Denominator){
        BigInteger Quotient=BigInteger.DivRem(Numerator,Denominator,out BigInteger Remainder);
        if(BigInteger.Abs(Remainder)*2>=BigInteger.Abs(Denominator)){
            Quotient+=Numerator.Sign==Denominator.Sign?BigInteger.One:-BigInteger.One;
        }
        return Quotient;
    }

    internal static bool TryAdd(BigInteger Left,BigInteger Right,int TotalBits,out BigInteger Result){
        Result=Left+Right; return Result>=MinimumRaw(TotalBits)&&Result<=MaximumRaw(TotalBits);
    }
    internal static bool TrySubtract(BigInteger Left,BigInteger Right,int TotalBits,out BigInteger Result){
        Result=Left-Right; return Result>=MinimumRaw(TotalBits)&&Result<=MaximumRaw(TotalBits);
    }
    internal static bool TryMultiply(BigInteger Left,BigInteger Right,int TotalBits,int FractionalBits,out BigInteger Result){
        Result=DivideRoundToNearest(Left*Right,BigInteger.One<<FractionalBits);
        return Result>=MinimumRaw(TotalBits)&&Result<=MaximumRaw(TotalBits);
    }
    internal static bool TryDivide(BigInteger Left,BigInteger Right,int TotalBits,int FractionalBits,out BigInteger Result){
        Result=BigInteger.Zero;
        if(Right.IsZero){ return false; }
        Result=DivideRoundToNearest(Left<<FractionalBits,Right);
        return Result>=MinimumRaw(TotalBits)&&Result<=MaximumRaw(TotalBits);
    }
    internal static bool TrySquareRoot(BigInteger Value,int TotalBits,int FractionalBits,out BigInteger Result){
        Result=BigInteger.Zero;
        if(Value.Sign<0){ return false; }
        Result=IntegerSquareRoot(Value<<FractionalBits);
        return Result>=MinimumRaw(TotalBits)&&Result<=MaximumRaw(TotalBits);
    }
    internal static BigInteger IntegerSquareRoot(BigInteger Value){
        if(Value.IsZero){ return BigInteger.Zero; }
        BigInteger Current=BigInteger.One<<((int)((Value.GetBitLength()+1)/2));
        while(true){
            BigInteger Next=(Current+Value/Current)>>1;
            if(Next>=Current){ return Current; }
            Current=Next;
        }
    }
}

