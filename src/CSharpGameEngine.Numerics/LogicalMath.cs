namespace CSharpGameEngine.Numerics;
public static class LogicalMath {
    public static readonly LogicalScalar Pi=LogicalScalar.ParseInvariant("3.1415926535897932384626433832795028841971693993751");
    public static readonly LogicalScalar HalfPi=LogicalScalar.ParseInvariant("1.5707963267948966192313216916397514420985846996876");
    public static readonly LogicalScalar TwoPi=LogicalScalar.ParseInvariant("6.2831853071795864769252867665590057683943387987502");
    private static readonly LogicalScalar NegativeOneHalf=-Reciprocal(2);
    private static readonly LogicalScalar Sin3=-Reciprocal(6);
    private static readonly LogicalScalar Sin5=Reciprocal(120);
    private static readonly LogicalScalar Sin7=-Reciprocal(5040);
    private static readonly LogicalScalar Sin9=Reciprocal(362880);
    private static readonly LogicalScalar Sin11=-Reciprocal(39916800);
    private static readonly LogicalScalar Sin13=Reciprocal(6227020800);
    private static readonly LogicalScalar Sin15=-Reciprocal(1307674368000);
    private static readonly LogicalScalar Sin17=Reciprocal(355687428096000);
    private static readonly LogicalScalar Cos4=Reciprocal(24);
    private static readonly LogicalScalar Cos6=-Reciprocal(720);
    private static readonly LogicalScalar Cos8=Reciprocal(40320);
    private static readonly LogicalScalar Cos10=-Reciprocal(3628800);
    private static readonly LogicalScalar Cos12=Reciprocal(479001600);
    private static readonly LogicalScalar Cos14=-Reciprocal(87178291200);
    private static readonly LogicalScalar Cos16=Reciprocal(20922789888000);
    public static LogicalScalar NormalizeRadians(LogicalScalar Angle){
        var Raw=Angle.Raw%TwoPi.Raw;
        if(Raw>Pi.Raw){ Raw-=TwoPi.Raw; }
        else if(Raw<-Pi.Raw){ Raw+=TwoPi.Raw; }
        return LogicalScalar.FromRaw(Raw);
    }
    public static bool TrySinCos(LogicalScalar Angle,out LogicalScalar Sin,out LogicalScalar Cos){
        Sin=default;
        Cos=default;
        var Reduced=NormalizeRadians(Angle);
        var CosineNegative=false;
        if(Reduced>HalfPi){
            if(!LogicalScalar.TrySubtract(Pi,Reduced,out Reduced)){ return false; }
            CosineNegative=true;
        } else if(Reduced<-HalfPi){
            if(!LogicalScalar.TrySubtract(-Pi,Reduced,out Reduced)){ return false; }
            CosineNegative=true;
        }
        if(!LogicalScalar.TryMultiply(Reduced,Reduced,out var Square)){ return false; }
        var SinPolynomial=Sin17;
        if(!TryMultiplyAdd(SinPolynomial,Square,Sin15,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin13,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin11,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin9,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin7,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin5,out SinPolynomial)
            ||!TryMultiplyAdd(SinPolynomial,Square,Sin3,out SinPolynomial)
            ||!LogicalScalar.TryMultiply(Reduced,Square,out var Cube)
            ||!LogicalScalar.TryMultiply(Cube,SinPolynomial,out var SinCorrection)
            ||!LogicalScalar.TryAdd(Reduced,SinCorrection,out Sin)){ return false; }
        var CosPolynomial=Cos16;
        if(!TryMultiplyAdd(CosPolynomial,Square,Cos14,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos12,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos10,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos8,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos6,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,Cos4,out CosPolynomial)
            ||!TryMultiplyAdd(CosPolynomial,Square,NegativeOneHalf,out CosPolynomial)
            ||!LogicalScalar.TryMultiply(Square,CosPolynomial,out var CosCorrection)
            ||!LogicalScalar.TryAdd(LogicalScalar.One,CosCorrection,out Cos)){ return false; }
        if(CosineNegative){ Cos=-Cos; }
        return true;
    }
    /// <summary>
    /// The angle (radians, -π to π) of the point (<paramref name="X"/>, <paramref name="Y"/>), as Math.Atan2 (ADR-0078/M0109: tyre slip angles and the
    /// Ackermann linkage). Exact Q64.64 arithmetic only, so every machine gets the same bits: the smaller of the two magnitudes over the larger is
    /// halved twice (atan t = 2 atan(t / (1 + √(1 + t²)))) to at most tan(π/16), where the alternating series to the 33rd power is below the last bit.
    /// (0, 0) gives 0.
    /// </summary>
    public static bool TryAtan2(LogicalScalar Y,LogicalScalar X,out LogicalScalar Angle){
        Angle=default;
        if(X.IsZero&&Y.IsZero){ return true; }
        if(X.Raw==Int128.MinValue||Y.Raw==Int128.MinValue){ return false; }
        var AbsoluteX=X<LogicalScalar.Zero?-X:X;
        var AbsoluteY=Y<LogicalScalar.Zero?-Y:Y;
        var Steep=AbsoluteY>AbsoluteX;
        if(!LogicalScalar.TryDivide(Steep?AbsoluteX:AbsoluteY,Steep?AbsoluteY:AbsoluteX,out var Ratio)||!TryAtanUnit(Ratio,out var Base)){ return false; }
        if(Steep){ Base=HalfPi-Base; }
        if(X<LogicalScalar.Zero){ Base=Pi-Base; }
        Angle=Y<LogicalScalar.Zero?-Base:Base;
        return true;
    }
    /// <summary>atan(<paramref name="Value"/>) for 0 ≤ value ≤ 1.</summary>
    private static bool TryAtanUnit(LogicalScalar Value,out LogicalScalar Result){
        Result=default;
        var Reduced=Value;
        for(var Halving=0;Halving<2;Halving++){
            if(!LogicalScalar.TryMultiply(Reduced,Reduced,out var Square)||!LogicalScalar.TryAdd(LogicalScalar.One,Square,out var OnePlusSquare)
                ||!LogicalScalar.TrySquareRoot(OnePlusSquare,out var Root)||!LogicalScalar.TryAdd(LogicalScalar.One,Root,out var Denominator)
                ||!LogicalScalar.TryDivide(Reduced,Denominator,out Reduced)){ return false; }
        }
        if(!LogicalScalar.TryMultiply(Reduced,Reduced,out var ReducedSquare)){ return false; }
        var Series=AtanSeries[^1];
        for(var Index=AtanSeries.Length-2;Index>=0;Index--){
            if(!TryMultiplyAdd(Series,ReducedSquare,AtanSeries[Index],out Series)){ return false; }
        }
        if(!LogicalScalar.TryMultiply(Series,Reduced,out var Quarter)){ return false; }
        // Two halvings: the angle of the reduced value is a quarter of the wanted one.
        Result=LogicalScalar.FromRaw(Quarter.Raw*4);
        return true;
    }
    /// <summary>(-1)^k / (2k + 1) for k = 0 … 16.</summary>
    private static readonly LogicalScalar[]AtanSeries=BuildAtanSeries();
    private static LogicalScalar[]BuildAtanSeries(){
        var Terms=new LogicalScalar[17];
        for(var Index=0;Index<Terms.Length;Index++){
            var Term=Reciprocal(2*Index+1);
            Terms[Index]=Index%2==0?Term:-Term;
        }
        return Terms;
    }
    public static bool TryAdvanceRadians(LogicalScalar Phase,LogicalScalar RadiansPerTick,ulong TickCount,out LogicalScalar Result){
        var Modulus=(UInt128)TwoPi.Raw;
        var PhaseResidue=PositiveResidue(Phase.Raw,TwoPi.Raw);
        var Step=PositiveResidue(RadiansPerTick.Raw,TwoPi.Raw);
        UInt128 Increment=0;
        while(TickCount!=0){
            if((TickCount&1)!=0){ Increment=AddModulo(Increment,Step,Modulus); }
            TickCount>>=1;
            if(TickCount!=0){ Step=AddModulo(Step,Step,Modulus); }
        }
        Result=LogicalScalar.FromRaw((Int128)AddModulo(PhaseResidue,Increment,Modulus));
        return true;
    }
    private static LogicalScalar Reciprocal(long Denominator)=>LogicalScalar.One/LogicalScalar.FromInt64(Denominator);
    private static bool TryMultiplyAdd(LogicalScalar Value,LogicalScalar Factor,LogicalScalar Addend,out LogicalScalar Result){
        Result=default;
        return LogicalScalar.TryMultiply(Value,Factor,out var Product)&&LogicalScalar.TryAdd(Product,Addend,out Result);
    }
    private static UInt128 PositiveResidue(Int128 Value,Int128 Modulus){
        var Residue=Value%Modulus;
        return (UInt128)(Residue<0?Residue+Modulus:Residue);
    }
    private static UInt128 AddModulo(UInt128 Left,UInt128 Right,UInt128 Modulus)=>Left>=Modulus-Right?Left-(Modulus-Right):Left+Right;
}
