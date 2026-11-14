namespace CSharpGameEngine.Numerics;
internal static class FixedAngleMath<T> where T:struct,IFixedMathScalar<T> {
    public static readonly T Pi=T.ParseInvariant("3.1415926535897932384626433832795028841971693993751058209749445923078164062862089986280");
    public static readonly T HalfPi=T.ParseInvariant("1.5707963267948966192313216916397514420985846996875529104874722961539082031431044993140");
    public static readonly T TwoPi=T.ParseInvariant("6.2831853071795864769252867665590057683943387987502116419498891846156328125724179972561");
    private static readonly T NegativeOneHalf=-Reciprocal(2);
    private static readonly T Sin3=-Reciprocal(6);
    private static readonly T Sin5=Reciprocal(120);
    private static readonly T Sin7=-Reciprocal(5040);
    private static readonly T Sin9=Reciprocal(362880);
    private static readonly T Sin11=-Reciprocal(39916800);
    private static readonly T Sin13=Reciprocal(6227020800);
    private static readonly T Sin15=-Reciprocal(1307674368000);
    private static readonly T Sin17=Reciprocal(355687428096000);
    private static readonly T Cos4=Reciprocal(24);
    private static readonly T Cos6=-Reciprocal(720);
    private static readonly T Cos8=Reciprocal(40320);
    private static readonly T Cos10=-Reciprocal(3628800);
    private static readonly T Cos12=Reciprocal(479001600);
    private static readonly T Cos14=-Reciprocal(87178291200);
    private static readonly T Cos16=Reciprocal(20922789888000);
    public static T NormalizeRadians(T Angle){
        var Value=Angle%TwoPi;
        if(Value>Pi){ Value-=TwoPi; }
        else if(Value<-Pi){ Value+=TwoPi; }
        return Value;
    }
    public static bool TrySinCos(T Angle,out T Sin,out T Cos){
        Sin=default;
        Cos=default;
        var Reduced=NormalizeRadians(Angle);
        var CosineNegative=false;
        if(Reduced>HalfPi){
            if(!T.TrySubtract(Pi,Reduced,out Reduced)){ return false; }
            CosineNegative=true;
        } else if(Reduced<-HalfPi){
            if(!T.TrySubtract(-Pi,Reduced,out Reduced)){ return false; }
            CosineNegative=true;
        }
        if(T.FractionalPrecision>64){
            if(!TrySeriesSinCos(Reduced,out Sin,out Cos)){ return false; }
            if(CosineNegative){ Cos=-Cos; }
            return true;
        }
        if(!T.TryMultiply(Reduced,Reduced,out var Square)){ return false; }
        var SinPolynomial=Sin17;
        if(!TryMultiplyAdd(SinPolynomial,Square,Sin15,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin13,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin11,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin9,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin7,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin5,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin3,out SinPolynomial)
            ||!T.TryMultiply(Reduced,Square,out var Cube)
            ||!T.TryMultiply(Cube,SinPolynomial,out var SinCorrection)
            ||!T.TryAdd(Reduced,SinCorrection,out Sin)){ return false; }
        var CosPolynomial=Cos16;
        if(!TryMultiplyAdd(CosPolynomial,Square,Cos14,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos12,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos10,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos8,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos6,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos4,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,NegativeOneHalf,out CosPolynomial)
            ||!T.TryMultiply(Square,CosPolynomial,out var CosCorrection)
            ||!T.TryAdd(T.One,CosCorrection,out Cos)){ return false; }
        if(CosineNegative){ Cos=-Cos; }
        return true;
    }
    /// <summary>
    /// 点 (<paramref name="X"/>、<paramref name="Y"/>) の角度（ラジアン、-π～π）。Math.Atan2 と同じ（ADR-0078/M0109：タイヤのスリップ角
    /// とアッカーマン・リンケージ）。固定小数点の決定的な演算で、小さい方の絶対値を大きい方で割った値を2回半減する
    /// （atan t = 2 atan(t / (1 + √(1 + t²)))）。縮小後の交代級数の項数は小数精度に応じて選ぶ。(0, 0) の場合は0。
    /// </summary>
    public static bool TryAtan2(T Y,T X,out T Angle){
        Angle=default;
        if(X.IsZero&&Y.IsZero){ return true; }
        if(X==T.MinValue||Y==T.MinValue){ return false; }
        var AbsoluteX=X<T.Zero?-X:X;
        var AbsoluteY=Y<T.Zero?-Y:Y;
        var Steep=AbsoluteY>AbsoluteX;
        if(!T.TryDivide(Steep?AbsoluteX:AbsoluteY,Steep?AbsoluteY:AbsoluteX,out var Ratio)||!TryAtanUnit(Ratio,out var Base)){ return false; }
        if(Steep){ Base=HalfPi-Base; }
        if(X<T.Zero){ Base=Pi-Base; }
        Angle=Y<T.Zero?-Base:Base;
        return true;
    }
    /// <summary>0 ≤ value ≤ 1 における atan(<paramref name="Value"/>)。</summary>
    private static bool TryAtanUnit(T Value,out T Result){
        Result=default;
        var Reduced=Value;
        for(var Halving=0;Halving<2;Halving++){
            if(!T.TryMultiply(Reduced,Reduced,out var Square)||!T.TryAdd(T.One,Square,out var OnePlusSquare)
                ||!T.TrySquareRoot(OnePlusSquare,out var Root)||!T.TryAdd(T.One,Root,out var Denominator)
                ||!T.TryDivide(Reduced,Denominator,out Reduced)){ return false; }
        }
        if(!T.TryMultiply(Reduced,Reduced,out var ReducedSquare)){ return false; }
        var Series=AtanSeries[^1];
        for(var Index=AtanSeries.Length-2;Index>=0;Index--){
            if(!TryMultiplyAdd(Series,ReducedSquare,AtanSeries[Index],out Series)){ return false; }
        }
        if(!T.TryMultiply(Series,Reduced,out var Quarter)){ return false; }
        // 2回半減すると、縮小後の値の角度は求める角度の4分の1になる。
        return T.TryMultiplyInteger(Quarter,4,out Result);
    }
    /// <summary>k = 0 … 16 に対する (-1)^k / (2k + 1)。</summary>
    private static readonly T[]AtanSeries=BuildAtanSeries();
    private static T[]BuildAtanSeries(){
        var Terms=new T[T.FractionalPrecision<=64?17:(T.FractionalPrecision+15)/4];
        for(var Index=0;Index<Terms.Length;Index++){
            var Term=Reciprocal(2*Index+1);
            Terms[Index]=Index%2==0?Term:-Term;
        }
        return Terms;
    }
    public static bool TryAdvanceRadians(T Phase,T RadiansPerTick,ulong TickCount,out T Result){
        var Modulus=TwoPi;
        var PhaseResidue=PositiveResidue(Phase,Modulus);
        var Step=PositiveResidue(RadiansPerTick,Modulus);
        var Increment=T.Zero;
        while(TickCount!=0){
            if((TickCount&1)!=0){ Increment=AddModulo(Increment,Step,Modulus); }
            TickCount>>=1;
            if(TickCount!=0){ Step=AddModulo(Step,Step,Modulus); }
        }
        Result=AddModulo(PhaseResidue,Increment,Modulus);
        return true;
    }
    private static T Reciprocal(long Denominator)=>T.FromRatio(1,Denominator);
    private static bool TrySeriesSinCos(T Value,out T Sin,out T Cos){
        Sin=default;Cos=default;
        if(!T.TryMultiply(Value,Value,out var Square)){ return false; }
        var SineTerm=Value;
        var CosineTerm=T.One;
        var Sine=Value;
        var Cosine=T.One;
        for(var Index=1;Index<=T.FractionalPrecision+4;Index++){
            var Even=2L*Index;
            if(!T.TryMultiply(SineTerm,Square,out var SineProduct)||!T.TryDivideInteger(SineProduct,Even*(Even+1),out SineTerm)
                ||!T.TryMultiply(CosineTerm,Square,out var CosineProduct)||!T.TryDivideInteger(CosineProduct,(Even-1)*Even,out CosineTerm)){ return false; }
            SineTerm=-SineTerm;CosineTerm=-CosineTerm;
            if(!T.TryAdd(Sine,SineTerm,out Sine)||!T.TryAdd(Cosine,CosineTerm,out Cosine)){ return false; }
            if(SineTerm.IsZero&&CosineTerm.IsZero){ break; }
        }
        Sin=Sine;Cos=Cosine;return true;
    }
    private static bool TryMultiplyAdd(T Value,T Factor,T Addend,out T Result){
        Result=default;
        return T.TryMultiply(Value,Factor,out var Product)&&T.TryAdd(Product,Addend,out Result);
    }
    private static T PositiveResidue(T Value,T Modulus){
        var Residue=Value%Modulus;
        return Residue<T.Zero?Residue+Modulus:Residue;
    }
    private static T AddModulo(T Left,T Right,T Modulus)=>Left>=Modulus-Right?Left-(Modulus-Right):Left+Right;
}
