namespace CSharpGameEngine.Numerics;
/// <summary>64/128/192bitの符号付き固定小数点演算。桁は下位から並べ、成功時だけ出力を更新する。</summary>
internal static class FixedArithmetic {
    internal static bool TryAdd(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,Span<ulong>Destination)=>TryAddSubtract(Left,Right,Destination,false);
    internal static bool TrySubtract(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,Span<ulong>Destination)=>TryAddSubtract(Left,Right,Destination,true);
    private static bool TryAddSubtract(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,Span<ulong>Destination,bool Subtract){
        Validate(Left,Right,Destination,0);
        Span<ulong>Value=stackalloc ulong[4];
        Value=Value[..Left.Length];
        ulong Carry=0;
        for(var Index=0;Index<Value.Length;Index++){
            if(Subtract){
                var Subtrahend=(UInt128)Right[Index]+Carry;
                Value[Index]=unchecked(Left[Index]-(ulong)Subtrahend);
                Carry=Subtrahend>Left[Index]?1UL:0UL;
            }else{
                var Sum=(UInt128)Left[Index]+Right[Index]+Carry;
                Value[Index]=(ulong)Sum;
                Carry=(ulong)(Sum>>64);
            }
        }
        var LeftNegative=IsNegative(Left);
        var RightNegative=IsNegative(Right);
        if((Subtract?LeftNegative!=RightNegative:LeftNegative==RightNegative)&&IsNegative(Value)!=LeftNegative){ return false; }
        Value.CopyTo(Destination);
        return true;
    }
    internal static bool TryMultiply(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,int FractionalBits,Span<ulong>Destination){
        Validate(Left,Right,Destination,FractionalBits);
        Span<ulong>A=stackalloc ulong[4];
        Span<ulong>B=stackalloc ulong[4];
        Magnitude(Left,A[..Left.Length]);
        Magnitude(Right,B[..Right.Length]);
        Span<ulong>Product=stackalloc ulong[8];
        Product.Clear();
        for(var I=0;I<Left.Length;I++){
            ulong Carry=0;
            for(var J=0;J<Right.Length;J++){
                var Sum=(UInt128)A[I]*B[J]+Product[I+J]+Carry;
                Product[I+J]=(ulong)Sum;
                Carry=(ulong)(Sum>>64);
            }
            Product[I+Right.Length]=Carry;
        }
        Span<ulong>Rounded=stackalloc ulong[8];
        ShiftRight(Product,FractionalBits,Rounded);
        if(FractionalBits>0&&Bit(Product,FractionalBits-1)&&(AnyBelow(Product,FractionalBits-1)||(Rounded[0]&1)!=0)&&!Increment(Rounded)){ return false; }
        return TryCommit(Rounded,IsNegative(Left)^IsNegative(Right),Destination);
    }
    internal static bool TryDivide(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,int FractionalBits,Span<ulong>Destination){
        Validate(Left,Right,Destination,FractionalBits);
        Span<ulong>A=stackalloc ulong[8];
        Span<ulong>B=stackalloc ulong[5];
        A.Clear();B.Clear();
        Magnitude(Left,A[..Left.Length]);
        Magnitude(Right,B[..Right.Length]);
        if(IsZero(B)){ return false; }
        Span<ulong>Dividend=stackalloc ulong[8];
        ShiftLeft(A,FractionalBits,Dividend);
        Span<ulong>Quotient=stackalloc ulong[8];
        Span<ulong>Remainder=stackalloc ulong[5];
        Quotient.Clear();Remainder.Clear();
        for(var Index=BitLength(Dividend)-1;Index>=0;Index--){
            ShiftSmall(Remainder,1);
            if(Bit(Dividend,Index)){ Remainder[0]|=1; }
            if(Compare(Remainder,B)>=0){
                SubtractUnsigned(Remainder,B);
                Quotient[Index/64]|=1UL<<(Index%64);
            }
        }
        Span<ulong>Complement=stackalloc ulong[5];
        B.CopyTo(Complement);
        SubtractUnsigned(Complement,Remainder);
        var Comparison=Compare(Remainder,Complement);
        if((Comparison>0||(Comparison==0&&(Quotient[0]&1)!=0))&&!Increment(Quotient)){ return false; }
        return TryCommit(Quotient,IsNegative(Left)^IsNegative(Right),Destination);
    }
    internal static bool TryRemainder(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,Span<ulong>Destination){
        Validate(Left,Right,Destination,0);
        Span<ulong>A=stackalloc ulong[4];
        Span<ulong>B=stackalloc ulong[5];
        Span<ulong>Remainder=stackalloc ulong[5];
        B.Clear();Remainder.Clear();
        Magnitude(Left,A[..Left.Length]);
        Magnitude(Right,B[..Right.Length]);
        if(IsZero(B)){ return false; }
        for(var Index=BitLength(A[..Left.Length])-1;Index>=0;Index--){
            ShiftSmall(Remainder,1);
            if(Bit(A,Index)){ Remainder[0]|=1; }
            if(Compare(Remainder,B)>=0){ SubtractUnsigned(Remainder,B); }
        }
        return TryCommit(Remainder,IsNegative(Left),Destination);
    }
    internal static bool TryFloorDivRemToInt64(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,out long Quotient,Span<ulong>Destination){
        Quotient=0;Validate(Left,Right,Destination,0);
        if(IsNegative(Right)||IsZero(Right)){ return false; }
        Span<ulong>A=stackalloc ulong[4];Magnitude(Left,A[..Left.Length]);
        Span<ulong>B=stackalloc ulong[5];B.Clear();Right.CopyTo(B);
        Span<ulong>Q=stackalloc ulong[8];Q.Clear();
        Span<ulong>R=stackalloc ulong[5];R.Clear();
        for(var Index=BitLength(A[..Left.Length])-1;Index>=0;Index--){
            ShiftSmall(R,1);if(Bit(A,Index)){ R[0]|=1; }
            if(Compare(R,B)>=0){ SubtractUnsigned(R,B);Q[Index/64]|=1UL<<(Index%64); }
        }
        var Negative=IsNegative(Left);
        if(Negative&&!IsZero(R)){
            if(!Increment(Q)){ return false; }
            Span<ulong>Complement=stackalloc ulong[5];B.CopyTo(Complement);SubtractUnsigned(Complement,R);Complement.CopyTo(R);
        }
        Span<ulong>Integer=stackalloc ulong[1];
        if(!TryCommit(Q,Negative,Integer)){ return false; }
        Quotient=unchecked((long)Integer[0]);R[..Destination.Length].CopyTo(Destination);return true;
    }
    internal static bool TryFloorIeeeRatio(ulong Numerator,ulong Denominator,int MantissaBits,int ExponentBits,int Bias,long Scale,out long Result){
        Result=0;
        var ExponentMask=(1UL<<ExponentBits)-1;
        var SignMask=1UL<<(MantissaBits+ExponentBits);
        var NE=(int)((Numerator>>MantissaBits)&ExponentMask);
        var DE=(int)((Denominator>>MantissaBits)&ExponentMask);
        var NS=(Numerator&((1UL<<MantissaBits)-1))|(NE==0?0:1UL<<MantissaBits);
        var DS=(Denominator&((1UL<<MantissaBits)-1))|(DE==0?0:1UL<<MantissaBits);
        if((ulong)NE==ExponentMask||(ulong)DE==ExponentMask||DS==0||(Denominator&SignMask)!=0){ return false; }
        if(NS==0||Scale==0){ return true; }
        var Negative=((Numerator&SignMask)!=0)^(Scale<0);
        var Factor=Scale<0?unchecked((ulong)(-(Scale+1)))+1:(ulong)Scale;
        UInt128 A=(UInt128)NS*Factor;
        UInt128 B=DS;
        var Shift=(NE==0?1-Bias-MantissaBits:NE-Bias-MantissaBits)-(DE==0?1-Bias-MantissaBits:DE-Bias-MantissaBits);
        var ABits=128-(int)UInt128.LeadingZeroCount(A);
        var BBits=64-System.Numerics.BitOperations.LeadingZeroCount(DS);
        UInt128 Q;
        bool HasRemainder;
        if(Shift>=0){
            if(ABits+Shift-BBits>64||ABits+Shift>128){ return false; }
            A<<=Shift;Q=A/B;HasRemainder=A%B!=0;
        }else if(BBits-Shift>ABits){ Q=0;HasRemainder=true; }else{
            B<<= -Shift;Q=A/B;HasRemainder=A%B!=0;
        }
        if(Negative&&HasRemainder){ Q++; }
        var Limit=Negative?(UInt128)long.MaxValue+1:(UInt128)long.MaxValue;
        if(Q>Limit){ return false; }
        Result=Negative?(Q==(UInt128)long.MaxValue+1?long.MinValue:-(long)Q):(long)Q;return true;
    }
    internal static bool TryRescale(ReadOnlySpan<ulong>Source,int SourceFractionalBits,int TargetFractionalBits,Span<ulong>Destination){
        if(Source.Length is <1 or >4||Destination.Length is <1 or >4){ throw new ArgumentException("Fixed storage must be 64 through 256 bits."); }
        if(SourceFractionalBits is <0 or >255||TargetFractionalBits is <0 or >255){ throw new ArgumentOutOfRangeException(nameof(SourceFractionalBits)); }
        Span<ulong>MagnitudeValue=stackalloc ulong[8];
        MagnitudeValue.Clear();
        Magnitude(Source,MagnitudeValue[..Source.Length]);
        Span<ulong>Scaled=stackalloc ulong[8];
        var Shift=TargetFractionalBits-SourceFractionalBits;
        if(Shift>=0){ ShiftLeft(MagnitudeValue,Shift,Scaled); }else{
            ShiftRight(MagnitudeValue,-Shift,Scaled);
            if(Bit(MagnitudeValue,-Shift-1)&&(AnyBelow(MagnitudeValue,-Shift-1)||(Scaled[0]&1)!=0)&&!Increment(Scaled)){ return false; }
        }
        return TryCommit(Scaled,IsNegative(Source),Destination);
    }
    internal static bool TryFromSingle(float Value,int FractionalBits,Span<ulong>Destination)=>TryFromIeee(BitConverter.SingleToUInt32Bits(Value),23,8,127,FractionalBits,Destination);
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
    internal static bool TryFromDouble(double Value,int FractionalBits,Span<ulong>Destination)=>TryFromIeee(BitConverter.DoubleToUInt64Bits(Value),52,11,1023,FractionalBits,Destination);
    private static bool TryFromIeee(ulong Bits,int MantissaBits,int ExponentBits,int Bias,int FractionalBits,Span<ulong>Destination){
        if(Destination.Length is <1 or >4||FractionalBits is <0 or >255){ throw new ArgumentOutOfRangeException(nameof(FractionalBits)); }
        var ExponentMask=(1UL<<ExponentBits)-1;
        var Exponent=(int)((Bits>>MantissaBits)&ExponentMask);
        if((ulong)Exponent==ExponentMask){ return false; }
        var SignificandValue=(Bits&((1UL<<MantissaBits)-1))|(Exponent==0?0UL:1UL<<MantissaBits);
        if(SignificandValue==0){ Destination.Clear();return true; }
        var Shift=FractionalBits+(Exponent==0?1-Bias-MantissaBits:Exponent-Bias-MantissaBits);
        if(Shift>=Destination.Length*64){ return false; }
        if(Shift< -64){ Destination.Clear();return true; }
        Span<ulong>Significand=stackalloc ulong[8];Significand.Clear();Significand[0]=SignificandValue;
        Span<ulong>Scaled=stackalloc ulong[8];
        if(Shift>=0){ ShiftLeft(Significand,Shift,Scaled); }else{
            ShiftRight(Significand,-Shift,Scaled);
            if(Bit(Significand,-Shift-1)&&(AnyBelow(Significand,-Shift-1)||(Scaled[0]&1)!=0)){ Increment(Scaled); }
        }
        return TryCommit(Scaled,(Bits&(1UL<<(MantissaBits+ExponentBits)))!=0,Destination);
    }
    internal static bool TryToSingle(ReadOnlySpan<ulong>Source,int FractionalBits,out float Value){
        Value=default;
        if(!TryToIeee(Source,FractionalBits,23,8,127,out var Bits)){ return false; }
        Value=BitConverter.UInt32BitsToSingle((uint)Bits);return true;
    }
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
    internal static bool TryToDouble(ReadOnlySpan<ulong>Source,int FractionalBits,out double Value){
        Value=default;
        if(!TryToIeee(Source,FractionalBits,52,11,1023,out var Bits)){ return false; }
        Value=BitConverter.UInt64BitsToDouble(Bits);return true;
    }
    private static bool TryToIeee(ReadOnlySpan<ulong>Source,int FractionalBits,int MantissaBits,int ExponentBits,int Bias,out ulong Bits){
        Bits=0;
        if(Source.Length is <1 or >4||FractionalBits is <0 or >255){ throw new ArgumentOutOfRangeException(nameof(FractionalBits)); }
        Span<ulong>MagnitudeValue=stackalloc ulong[4];
        Magnitude(Source,MagnitudeValue[..Source.Length]);MagnitudeValue=MagnitudeValue[..Source.Length];
        var Highest=BitLength(MagnitudeValue)-1;
        if(Highest<0){ return true; }
        var Exponent=Highest-FractionalBits;
        var Shift=Exponent<1-Bias?FractionalBits+1-Bias-MantissaBits:Highest-MantissaBits;
        Span<ulong>Rounded=stackalloc ulong[8];
        if(Shift>=0){
            ShiftRight(MagnitudeValue,Shift,Rounded);
            if(Shift>0&&Bit(MagnitudeValue,Shift-1)&&(AnyBelow(MagnitudeValue,Shift-1)||(Rounded[0]&1)!=0)){ Increment(Rounded); }
        }else{ ShiftLeft(MagnitudeValue,-Shift,Rounded); }
        var Significand=Rounded[0];
        if(Significand==0){ return true; }
        if(Exponent<1-Bias){ Bits=Significand; }else{
            if(Significand==(1UL<<(MantissaBits+1))){ Significand>>=1;Exponent++; }
            if(Exponent>Bias){ return false; }
            Bits=(ulong)(Exponent+Bias)<<MantissaBits|(Significand&((1UL<<MantissaBits)-1));
        }
        if(IsNegative(Source)){ Bits|=1UL<<(MantissaBits+ExponentBits); }
        return true;
    }
    internal static bool TrySquareRoot(ReadOnlySpan<ulong>Value,int FractionalBits,Span<ulong>Destination){
        Validate(Value,Value,Destination,FractionalBits);
        if(IsNegative(Value)){ return false; }
        Span<ulong>Input=stackalloc ulong[8];
        Input.Clear();Value.CopyTo(Input);
        Span<ulong>Radicand=stackalloc ulong[8];
        ShiftLeft(Input,FractionalBits,Radicand);
        Span<ulong>Root=stackalloc ulong[5];
        Span<ulong>Remainder=stackalloc ulong[5];
        Span<ulong>Trial=stackalloc ulong[5];
        Root.Clear();Remainder.Clear();
        for(var Pair=(BitLength(Radicand)+1)/2-1;Pair>=0;Pair--){
            ShiftSmall(Remainder,2);
            if(Bit(Radicand,Pair*2)){ Remainder[0]|=1; }
            if(Bit(Radicand,Pair*2+1)){ Remainder[0]|=2; }
            ShiftSmall(Root,1);
            Root.CopyTo(Trial);
            ShiftSmall(Trial,1);
            Trial[0]|=1;
            if(Compare(Remainder,Trial)>=0){
                SubtractUnsigned(Remainder,Trial);
                Increment(Root);
            }
        }
        Root.CopyTo(Trial);
        ShiftSmall(Trial,1);
        Increment(Trial);
        SubtractUnsigned(Trial,Remainder);
        var Comparison=Compare(Remainder,Trial);
        if((Comparison>0||(Comparison==0&&(Root[0]&1)!=0))&&!Increment(Root)){ return false; }
        return TryCommit(Root,false,Destination);
    }
    private static bool TryCommit(ReadOnlySpan<ulong>Magnitude,bool Negative,Span<ulong>Destination){
        for(var Index=Destination.Length;Index<Magnitude.Length;Index++){ if(Magnitude[Index]!=0){ return false; } }
        var Top=Magnitude[Destination.Length-1];
        if(Top>0x8000000000000000UL||(!Negative&&Top==0x8000000000000000UL)){ return false; }
        if(Top==0x8000000000000000UL){
            for(var Index=0;Index<Destination.Length-1;Index++){ if(Magnitude[Index]!=0){ return false; } }
        }
        Magnitude[..Destination.Length].CopyTo(Destination);
        if(Negative){ Negate(Destination); }
        return true;
    }
    private static void Validate(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right,Span<ulong>Destination,int FractionalBits){
        if(Left.Length is <1 or >4||Right.Length!=Left.Length||Destination.Length!=Left.Length){ throw new ArgumentException("Fixed values require matching 64 through 256-bit storage."); }
        if(FractionalBits<0||FractionalBits>=Left.Length*64){ throw new ArgumentOutOfRangeException(nameof(FractionalBits)); }
    }
    private static bool IsNegative(ReadOnlySpan<ulong>Value)=>(Value[^1]&0x8000000000000000UL)!=0;
    private static bool IsZero(ReadOnlySpan<ulong>Value){ foreach(var Digit in Value){ if(Digit!=0){ return false; } }return true; }
    private static void Magnitude(ReadOnlySpan<ulong>Value,Span<ulong>Destination){ Value.CopyTo(Destination);if(IsNegative(Value)){ Negate(Destination); } }
    private static void Negate(Span<ulong>Value){
        ulong Carry=1;
        for(var Index=0;Index<Value.Length;Index++){
            var Sum=(UInt128)(~Value[Index])+Carry;
            Value[Index]=(ulong)Sum;
            Carry=(ulong)(Sum>>64);
        }
    }
    private static bool Increment(Span<ulong>Value){
        for(var Index=0;Index<Value.Length;Index++){ Value[Index]++;if(Value[Index]!=0){ return true; } }
        return false;
    }
    private static bool Bit(ReadOnlySpan<ulong>Value,int Index)=>Index>=0&&Index<Value.Length*64&&(Value[Index/64]&(1UL<<(Index%64)))!=0;
    private static bool AnyBelow(ReadOnlySpan<ulong>Value,int Bits){
        for(var Index=0;Index<Bits/64;Index++){ if(Value[Index]!=0){ return true; } }
        return Bits%64!=0&&(Value[Bits/64]&((1UL<<(Bits%64))-1))!=0;
    }
    private static int BitLength(ReadOnlySpan<ulong>Value){
        for(var Index=Value.Length-1;Index>=0;Index--){ if(Value[Index]!=0){ return Index*64+64-System.Numerics.BitOperations.LeadingZeroCount(Value[Index]); } }
        return 0;
    }
    private static int Compare(ReadOnlySpan<ulong>Left,ReadOnlySpan<ulong>Right){
        for(var Index=Left.Length-1;Index>=0;Index--){ var Result=Left[Index].CompareTo(Right[Index]);if(Result!=0){ return Result; } }
        return 0;
    }
    private static void SubtractUnsigned(Span<ulong>Left,ReadOnlySpan<ulong>Right){
        ulong Borrow=0;
        for(var Index=0;Index<Left.Length;Index++){
            var Subtrahend=(UInt128)Right[Index]+Borrow;
            var Digit=Left[Index];
            Left[Index]=unchecked(Digit-(ulong)Subtrahend);
            Borrow=Subtrahend>Digit?1UL:0UL;
        }
    }
    private static void ShiftSmall(Span<ulong>Value,int Shift){
        ulong Carry=0;
        for(var Index=0;Index<Value.Length;Index++){
            var Digit=Value[Index];
            Value[Index]=(Digit<<Shift)|Carry;
            Carry=Digit>>(64-Shift);
        }
    }
    private static void ShiftLeft(ReadOnlySpan<ulong>Value,int Shift,Span<ulong>Destination){
        Destination.Clear();
        var Words=Shift/64;
        var Bits=Shift%64;
        for(var Index=0;Index<Value.Length&&Index+Words<Destination.Length;Index++){
            Destination[Index+Words]|=Value[Index]<<Bits;
            if(Bits!=0&&Index+Words+1<Destination.Length){ Destination[Index+Words+1]|=Value[Index]>>(64-Bits); }
        }
    }
    private static void ShiftRight(ReadOnlySpan<ulong>Value,int Shift,Span<ulong>Destination){
        Destination.Clear();
        var Words=Shift/64;
        var Bits=Shift%64;
        for(var Index=0;Index<Destination.Length&&Index+Words<Value.Length;Index++){
            Destination[Index]=Value[Index+Words]>>Bits;
            if(Bits!=0&&Index+Words+1<Value.Length){ Destination[Index]|=Value[Index+Words+1]<<(64-Bits); }
        }
    }
}
