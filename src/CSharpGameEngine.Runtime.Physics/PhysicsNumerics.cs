namespace CSharpGameEngine.Runtime.Physics;

/// <summary>Signed Q32.32 local-solver scalar with checked, ties-to-even arithmetic.</summary>
public readonly struct PhysicsScalar(long RawValue):IComparable<PhysicsScalar>,IEquatable<PhysicsScalar> {
    public const int FractionalBits=32;
    public const long OneRaw=1L<<FractionalBits;
    public long RawValue { get; }=RawValue;
    public static PhysicsScalar Zero=>default;
    public static PhysicsScalar One=>new(OneRaw);
    public static PhysicsScalar MinValue=>new(long.MinValue);
    public static PhysicsScalar MaxValue=>new(long.MaxValue);
    public bool IsZero=>this.RawValue==0;

    public static PhysicsScalar FromRaw(long Raw)=>new(Raw);
    public static PhysicsScalar FromInt32(int Value)=>new((long)Value<<FractionalBits);
    public static PhysicsScalar FromDouble(double Value){
        if(!TryFromDouble(Value,out PhysicsScalar Result)){
            throw new OverflowException();
        }
        return Result;
    }
    public static bool TryFromDouble(double Value,out PhysicsScalar Result){
        Result=default;
        if(!double.IsFinite(Value)||Value<long.MinValue/(double)OneRaw||Value>long.MaxValue/(double)OneRaw){
            return false;
        }
        double Scaled=Value*OneRaw;
        double Rounded=Math.Round(Scaled,MidpointRounding.ToEven);
        if(Rounded<long.MinValue||Rounded>long.MaxValue){
            return false;
        }
        Result=new PhysicsScalar((long)Rounded);
        return true;
    }
    public static bool TryFromLogical(Logical.Scalar Value,out PhysicsScalar Result){
        Result=default;
        if(!Value.TryToScaledInt64(FractionalBits,out long Raw)){ return false; }
        Result=new PhysicsScalar(Raw);
        return true;
    }
    public bool TryToLogical(out Logical.Scalar Result)=>Logical.Scalar.TryFromScaledInt64(this.RawValue,FractionalBits,out Result);
    public double ToDouble()=>this.RawValue/(double)OneRaw;

    public static bool TryAdd(PhysicsScalar Left,PhysicsScalar Right,out PhysicsScalar Result){
        Int128 Raw=(Int128)Left.RawValue+Right.RawValue;
        return TryCreate(Raw,out Result);
    }
    public static bool TrySubtract(PhysicsScalar Left,PhysicsScalar Right,out PhysicsScalar Result){
        Int128 Raw=(Int128)Left.RawValue-Right.RawValue;
        return TryCreate(Raw,out Result);
    }
    public static bool TryMultiply(PhysicsScalar Left,PhysicsScalar Right,out PhysicsScalar Result){
        Int128 Product=(Int128)Left.RawValue*Right.RawValue;
        return TryCreate(RoundShiftRightToEven(Product,FractionalBits),out Result);
    }
    public static bool TryDivide(PhysicsScalar Left,PhysicsScalar Right,out PhysicsScalar Result){
        Result=default;
        if(Right.RawValue==0){
            return false;
        }
        Int128 Numerator=(Int128)Left.RawValue<<FractionalBits;
        Int128 Quotient=DivideRoundToEven(Numerator,Right.RawValue);
        return TryCreate(Quotient,out Result);
    }
    public static bool TrySquareRoot(PhysicsScalar Value,out PhysicsScalar Result){
        Result=default;
        if(Value.RawValue<0){
            return false;
        }
        UInt128 Radicand=(UInt128)(ulong)Value.RawValue<<FractionalBits;
        UInt128 Root=IntegerSquareRoot(Radicand);
        if(Root>(UInt128)long.MaxValue){
            return false;
        }
        Result=new PhysicsScalar((long)Root);
        return true;
    }

    public static long RoundShiftRightToEven(Int128 Value,int Bits){
        if(Bits is <=0 or >=127){
            throw new ArgumentOutOfRangeException(nameof(Bits));
        }
        Int128 Denominator=(Int128)1<<Bits;
        Int128 Rounded=DivideRoundToEven(Value,Denominator);
        return checked((long)Rounded);
    }

    private static Int128 DivideRoundToEven(Int128 Numerator,Int128 Denominator){
        Int128 Quotient=Numerator/Denominator;
        Int128 Remainder=Numerator%Denominator;
        Int128 TwiceRemainder=Int128.Abs(Remainder)*2;
        Int128 AbsoluteDenominator=Int128.Abs(Denominator);
        if(TwiceRemainder>AbsoluteDenominator
            ||(TwiceRemainder==AbsoluteDenominator&&(Quotient&1)!=0)){
            Quotient+=Numerator<0^Denominator<0?-1:1;
        }
        return Quotient;
    }

    private static UInt128 IntegerSquareRoot(UInt128 Value){
        if(Value==0){ return 0; }
        UInt128 Result=0;
        UInt128 Bit=(UInt128)1<<126;
        while(Bit>Value){ Bit>>=2; }
        while(Bit!=0){
            if(Value>=Result+Bit){
                Value-=Result+Bit;
                Result=(Result>>1)+Bit;
            }
            else {
                Result>>=1;
            }
            Bit>>=2;
        }
        return Result;
    }

    private static bool TryCreate(Int128 Raw,out PhysicsScalar Result){
        Result=default;
        if(Raw<long.MinValue||Raw>long.MaxValue){ return false; }
        Result=new PhysicsScalar((long)Raw);
        return true;
    }

    public int CompareTo(PhysicsScalar Other)=>this.RawValue.CompareTo(Other.RawValue);
    public bool Equals(PhysicsScalar Other)=>this.RawValue==Other.RawValue;
    public override bool Equals(object? Object)=>Object is PhysicsScalar Other&&this.Equals(Other);
    public override int GetHashCode()=>this.RawValue.GetHashCode();
    public override string ToString()=>this.ToDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static PhysicsScalar operator +(PhysicsScalar Left,PhysicsScalar Right)=>TryAdd(Left,Right,out PhysicsScalar Result)?Result:throw new OverflowException();
    public static PhysicsScalar operator -(PhysicsScalar Left,PhysicsScalar Right)=>TrySubtract(Left,Right,out PhysicsScalar Result)?Result:throw new OverflowException();
    public static PhysicsScalar operator -(PhysicsScalar Value)=>Value.RawValue==long.MinValue?throw new OverflowException():new PhysicsScalar(-Value.RawValue);
    public static PhysicsScalar operator *(PhysicsScalar Left,PhysicsScalar Right)=>TryMultiply(Left,Right,out PhysicsScalar Result)?Result:throw new OverflowException();
    public static PhysicsScalar operator /(PhysicsScalar Left,PhysicsScalar Right)=>TryDivide(Left,Right,out PhysicsScalar Result)?Result:Right.IsZero?throw new DivideByZeroException():throw new OverflowException();
    public static implicit operator PhysicsScalar(int Value)=>FromInt32(Value);
    public static implicit operator PhysicsScalar(float Value)=>FromDouble(Value);
    public static implicit operator PhysicsScalar(double Value)=>FromDouble(Value);
    public static bool operator ==(PhysicsScalar Left,PhysicsScalar Right)=>Left.Equals(Right);
    public static bool operator !=(PhysicsScalar Left,PhysicsScalar Right)=>!Left.Equals(Right);
    public static bool operator <(PhysicsScalar Left,PhysicsScalar Right)=>Left.RawValue<Right.RawValue;
    public static bool operator >(PhysicsScalar Left,PhysicsScalar Right)=>Left.RawValue>Right.RawValue;
    public static bool operator <=(PhysicsScalar Left,PhysicsScalar Right)=>Left.RawValue<=Right.RawValue;
    public static bool operator >=(PhysicsScalar Left,PhysicsScalar Right)=>Left.RawValue>=Right.RawValue;
}

public readonly record struct PhysicsVector3(PhysicsScalar R,PhysicsScalar U,PhysicsScalar F) {
    public PhysicsVector3(int R,int U,int F):this(PhysicsScalar.FromInt32(R),PhysicsScalar.FromInt32(U),PhysicsScalar.FromInt32(F)){}
    public PhysicsVector3(double R,double U,double F):this(PhysicsScalar.FromDouble(R),PhysicsScalar.FromDouble(U),PhysicsScalar.FromDouble(F)){}
    public static PhysicsVector3 Zero=>default;
    public static PhysicsVector3 One=>new(1,1,1);
    public static PhysicsVector3 UnitR=>new(1,0,0);
    public static PhysicsVector3 UnitU=>new(0,1,0);
    public static PhysicsVector3 UnitF=>new(0,0,1);
    public PhysicsScalar LengthSquared=>Dot(this,this);
    public static PhysicsScalar Dot(in PhysicsVector3 Left,in PhysicsVector3 Right)=>Left.R*Right.R+Left.U*Right.U+Left.F*Right.F;
    public static PhysicsVector3 operator +(PhysicsVector3 Left,PhysicsVector3 Right)=>new(Left.R+Right.R,Left.U+Right.U,Left.F+Right.F);
    public static PhysicsVector3 operator -(PhysicsVector3 Left,PhysicsVector3 Right)=>new(Left.R-Right.R,Left.U-Right.U,Left.F-Right.F);
    public static PhysicsVector3 operator -(PhysicsVector3 Value)=>new(-Value.R,-Value.U,-Value.F);
    public static PhysicsVector3 operator *(PhysicsVector3 Value,PhysicsScalar Factor)=>new(Value.R*Factor,Value.U*Factor,Value.F*Factor);
    public static PhysicsVector3 operator /(PhysicsVector3 Value,PhysicsScalar Divisor)=>new(Value.R/Divisor,Value.U/Divisor,Value.F/Divisor);
}

public readonly record struct PhysicsQuaternion(PhysicsScalar R,PhysicsScalar U,PhysicsScalar F,PhysicsScalar W) {
    public static PhysicsQuaternion Identity=>new(0,0,0,PhysicsScalar.One);
    public PhysicsScalar LengthSquared=>this.R*this.R+this.U*this.U+this.F*this.F+this.W*this.W;
    public bool TryNormalize(out PhysicsQuaternion Result){
        Result=default;
        if(!PhysicsScalar.TrySquareRoot(this.LengthSquared,out PhysicsScalar Length)||Length.IsZero
            ||!PhysicsScalar.TryDivide(this.R,Length,out PhysicsScalar R)
            ||!PhysicsScalar.TryDivide(this.U,Length,out PhysicsScalar U)
            ||!PhysicsScalar.TryDivide(this.F,Length,out PhysicsScalar F)
            ||!PhysicsScalar.TryDivide(this.W,Length,out PhysicsScalar W)){
            return false;
        }
        Result=new PhysicsQuaternion(R,U,F,W);
        return true;
    }
}
