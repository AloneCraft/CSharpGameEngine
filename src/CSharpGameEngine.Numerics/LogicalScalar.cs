using System.Buffers.Binary;
using System.Globalization;
using System.Text;
namespace CSharpGameEngine.Numerics;
public readonly struct LogicalScalar(Int128 Raw):IComparable<LogicalScalar>,IEquatable<LogicalScalar> {
    public const int TotalBits=128;
    public const int FractionalBits=64;
    public const int SerializedSize=16;
    private static readonly UInt128 SignMagnitude=(UInt128)1<<127;
    public Int128 Raw{get;}=Raw;
    public static LogicalScalar Zero=>default;
    public static LogicalScalar One=>new((Int128)1<<FractionalBits);
    public static LogicalScalar MinValue=>new(Int128.MinValue);
    public static LogicalScalar MaxValue=>new(Int128.MaxValue);
    public bool IsZero=>this.Raw==0;
    public static LogicalScalar FromRaw(Int128 Raw)=>new(Raw);
    public static LogicalScalar FromInt64(long Value)=>new((Int128)Value<<FractionalBits);
    public static LogicalScalar FromSingle(float Value)=>TryFromSingle(Value,out var Result)?Result:throw new OverflowException();
    public static bool TryFromSingle(float Value,out LogicalScalar Result){
        Result=default;
        var Bits=BitConverter.SingleToUInt32Bits(Value);
        var Exponent=(int)((Bits>>23)&0xff);
        var Fraction=Bits&0x7fffff;
        if(Exponent==0xff){ return false; }
        if(Exponent==0&&Fraction==0){ return true; }
        var Significand=(UInt128)(Exponent==0?Fraction:Fraction|0x800000);
        var Shift=Exponent==0?-85:Exponent-86;
        UInt128 Magnitude;
        if(Shift>=0){
            if(Shift>=128||Significand>(UInt128.MaxValue>>Shift)){ return false; }
            Magnitude=Significand<<Shift;
        } else {
            Magnitude=RoundRightToEven(Significand,-Shift);
        }
        return TryCreateSigned(Magnitude,(Bits&0x80000000)!=0,out Result);
    }
    public bool TryToSingle(out float Value){
        Value=MathF.ScaleB((float)this.Raw,-FractionalBits);
        return float.IsFinite(Value);
    }
    public static LogicalScalar ParseInvariant(string Text)=>TryParseInvariant(Text,out var Result)?Result:throw new FormatException();
    public static bool TryParseInvariant(string? Text,out LogicalScalar Result){
        Result=default;
        if(string.IsNullOrWhiteSpace(Text)){ return false; }
        var Source=Text.AsSpan().Trim();
        var Negative=false;
        if(Source[0] is '+' or '-'){
            Negative=Source[0]=='-';
            Source=Source[1..];
        }
        if(Source.IsEmpty){ return false; }
        var Dot=Source.IndexOf('.');
        if(Dot>=0&&Source[(Dot+1)..].IndexOf('.')>=0){ return false; }
        var IntegerPart=Dot<0?Source:Source[..Dot];
        var FractionPart=Dot<0?ReadOnlySpan<char>.Empty:Source[(Dot+1)..];
        if(IntegerPart.IsEmpty&&FractionPart.IsEmpty){ return false; }
        UInt128 IntegerMagnitude=0;
        var MaximumIntegerMagnitude=(UInt128)1<<63;
        foreach(var Character in IntegerPart){
            if(Character<'0'||Character>'9'){ return false; }
            var Digit=(uint)(Character-'0');
            if(IntegerMagnitude>(MaximumIntegerMagnitude-Digit)/10){ return false; }
            IntegerMagnitude=IntegerMagnitude*10+Digit;
        }
        if(FractionPart.Length>4096){ return false; }
        Span<byte>Digits=FractionPart.Length<=256?stackalloc byte[FractionPart.Length]:new byte[FractionPart.Length];
        for(var Index=0;Index<FractionPart.Length;Index++){
            var Character=FractionPart[Index];
            if(Character<'0'||Character>'9'){ return false; }
            Digits[Index]=(byte)(Character-'0');
        }
        ulong FractionRaw=0;
        for(var Bit=0;Bit<64;Bit++){
            var Carry=MultiplyDecimalByTwo(Digits);
            FractionRaw=(FractionRaw<<1)|(uint)Carry;
        }
        var RoundBit=MultiplyDecimalByTwo(Digits);
        var HasRemainder=false;
        foreach(var Digit in Digits){ HasRemainder|=Digit!=0; }
        UInt128 Magnitude=(IntegerMagnitude<<64)|FractionRaw;
        if(RoundBit!=0&&(HasRemainder||(Magnitude&1)!=0)){
            if(Magnitude==UInt128.MaxValue){ return false; }
            Magnitude++;
        }
        return TryCreateSigned(Magnitude,Negative,out Result);
    }
    public override string ToString()=>this.ToStringInvariant();
    public string ToStringInvariant(){
        var Negative=this.Raw<0;
        var Magnitude=GetMagnitude(this.Raw);
        var IntegerPart=(ulong)(Magnitude>>64);
        var Fraction=(ulong)Magnitude;
        if(Fraction==0){ return string.Concat(Negative?"-":string.Empty,IntegerPart.ToString(CultureInfo.InvariantCulture)); }
        Span<char>FractionDigits=stackalloc char[64];
        var Count=0;
        while(Fraction!=0){
            var Scaled=(UInt128)Fraction*10;
            FractionDigits[Count++]=(char)('0'+(uint)(Scaled>>64));
            Fraction=(ulong)Scaled;
        }
        var Builder=new StringBuilder((Negative?1:0)+20+1+Count);
        if(Negative){ Builder.Append('-'); }
        Builder.Append(IntegerPart.ToString(CultureInfo.InvariantCulture));
        Builder.Append('.');
        Builder.Append(FractionDigits[..Count]);
        return Builder.ToString();
    }
    public void WriteLittleEndian(Span<byte>Destination){
        if(Destination.Length<SerializedSize){ throw new ArgumentException("LogicalScalar requires sixteen bytes.",nameof(Destination)); }
        var Bits=unchecked((UInt128)this.Raw);
        BinaryPrimitives.WriteUInt64LittleEndian(Destination,(ulong)Bits);
        BinaryPrimitives.WriteUInt64LittleEndian(Destination[8..],(ulong)(Bits>>64));
    }
    public static bool TryReadLittleEndian(ReadOnlySpan<byte>Source,out LogicalScalar Result){
        Result=default;
        if(Source.Length!=SerializedSize){ return false; }
        var Bits=(UInt128)BinaryPrimitives.ReadUInt64LittleEndian(Source)|(UInt128)BinaryPrimitives.ReadUInt64LittleEndian(Source[8..])<<64;
        Result=new LogicalScalar(unchecked((Int128)Bits));
        return true;
    }
    public bool TryToScaledInt64(int TargetFractionalBits,out long RawValue){
        RawValue=0;
        if(TargetFractionalBits is <0 or >127){ return false; }
        var Negative=this.Raw<0;
        var Magnitude=GetMagnitude(this.Raw);
        if(TargetFractionalBits>=FractionalBits){
            var Shift=TargetFractionalBits-FractionalBits;
            if(Shift>=128||Magnitude>(UInt128.MaxValue>>Shift)){ return false; }
            Magnitude<<=Shift;
        } else {
            Magnitude=RoundRightToEven(Magnitude,FractionalBits-TargetFractionalBits);
        }
        var Maximum=Negative?(UInt128)long.MaxValue+1:(UInt128)long.MaxValue;
        if(Magnitude>Maximum){ return false; }
        RawValue=Negative?(Magnitude==(UInt128)long.MaxValue+1?long.MinValue:-(long)Magnitude):(long)Magnitude;
        return true;
    }
    public static bool TryFromScaledInt64(long RawValue,int SourceFractionalBits,out LogicalScalar Result){
        Result=default;
        if(SourceFractionalBits is <0 or >127){ return false; }
        var Negative=RawValue<0;
        var Magnitude=RawValue==long.MinValue?(UInt128)long.MaxValue+1:(UInt128)Math.Abs(RawValue);
        if(SourceFractionalBits<=FractionalBits){
            var Shift=FractionalBits-SourceFractionalBits;
            if(Shift>=128||Magnitude>(UInt128.MaxValue>>Shift)){ return false; }
            Magnitude<<=Shift;
        } else {
            Magnitude=RoundRightToEven(Magnitude,SourceFractionalBits-FractionalBits);
        }
        return TryCreateSigned(Magnitude,Negative,out Result);
    }
    public static bool TryAdd(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result){
        if((Right.Raw>0&&Left.Raw>Int128.MaxValue-Right.Raw)||(Right.Raw<0&&Left.Raw<Int128.MinValue-Right.Raw)){ Result=default;return false; }
        Result=new LogicalScalar(Left.Raw+Right.Raw);
        return true;
    }
    public static bool TrySubtract(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result){
        if((Right.Raw<0&&Left.Raw>Int128.MaxValue+Right.Raw)||(Right.Raw>0&&Left.Raw<Int128.MinValue+Right.Raw)){ Result=default;return false; }
        Result=new LogicalScalar(Left.Raw-Right.Raw);
        return true;
    }
    public static bool TryMultiply(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result){
        var Product=MultiplyUnsigned(GetMagnitude(Left.Raw),GetMagnitude(Right.Raw));
        if(Product.L3!=0){ Result=default;return false; }
        var Low=Product.L1;
        var High=Product.L2;
        if(Product.L0>0x8000000000000000UL||(Product.L0==0x8000000000000000UL&&(Low&1)!=0)){
            Low++;
            if(Low==0){ High++;if(High==0){ Result=default;return false; } }
        }
        var Magnitude=(UInt128)High<<64|Low;
        return TryCreateSigned(Magnitude,(Left.Raw<0)^(Right.Raw<0),out Result);
    }
    public static bool TryDivide(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result){
        Result=default;
        if(Right.Raw==0){ return false; }
        if(!TryDivideScaled(GetMagnitude(Left.Raw),GetMagnitude(Right.Raw),out var Quotient,out var Remainder)){ return false; }
        var Complement=GetMagnitude(Right.Raw)-Remainder;
        if(Remainder>Complement||(Remainder==Complement&&(Quotient&1)!=0)){
            if(Quotient==UInt128.MaxValue){ return false; }
            Quotient++;
        }
        return TryCreateSigned(Quotient,(Left.Raw<0)^(Right.Raw<0),out Result);
    }
    public static bool TrySquareRoot(LogicalScalar Value,out LogicalScalar Result){
        Result=default;
        if(Value.Raw<0){ return false; }
        if(Value.Raw==0){ return true; }
        var Magnitude=(UInt128)Value.Raw;
        var Radicand=new UInt256(0,(ulong)Magnitude,(ulong)(Magnitude>>64),0);
        UInt128 Low=0;
        UInt128 High=(UInt128)1<<96;
        while(Low+1<High){
            var Middle=Low+((High-Low)>>1);
            if(MultiplyUnsigned(Middle,Middle).CompareTo(Radicand)<=0){ Low=Middle; }else{ High=Middle; }
        }
        var Remainder256=UInt256.Subtract(Radicand,MultiplyUnsigned(Low,Low));
        if(Remainder256.L2!=0||Remainder256.L3!=0){ return false; }
        var Remainder=(UInt128)Remainder256.L1<<64|Remainder256.L0;
        var Gap=(Low<<1)+1;
        var Complement=Gap-Remainder;
        if(Remainder>Complement||(Remainder==Complement&&(Low&1)!=0)){ Low++; }
        return TryCreateSigned(Low,false,out Result);
    }
    public int CompareTo(LogicalScalar Other)=>this.Raw.CompareTo(Other.Raw);
    public bool Equals(LogicalScalar Other)=>this.Raw==Other.Raw;
    public override bool Equals(object? Object)=>Object is LogicalScalar Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Raw.GetHashCode();
    public static LogicalScalar operator +(LogicalScalar Left,LogicalScalar Right)=>TryAdd(Left,Right,out var Result)?Result:throw new OverflowException();
    public static LogicalScalar operator -(LogicalScalar Left,LogicalScalar Right)=>TrySubtract(Left,Right,out var Result)?Result:throw new OverflowException();
    public static LogicalScalar operator -(LogicalScalar Value)=>Value.Raw==Int128.MinValue?throw new OverflowException():new LogicalScalar(-Value.Raw);
    public static LogicalScalar operator *(LogicalScalar Left,LogicalScalar Right)=>TryMultiply(Left,Right,out var Result)?Result:throw new OverflowException();
    public static LogicalScalar operator /(LogicalScalar Left,LogicalScalar Right)=>TryDivide(Left,Right,out var Result)?Result:Right.IsZero?throw new DivideByZeroException():throw new OverflowException();
    public static implicit operator LogicalScalar(int Value)=>FromInt64(Value);
    public static implicit operator LogicalScalar(long Value)=>FromInt64(Value);
    public static implicit operator LogicalScalar(float Value)=>FromSingle(Value);
    public static bool operator ==(LogicalScalar Left,LogicalScalar Right)=>Left.Raw==Right.Raw;
    public static bool operator !=(LogicalScalar Left,LogicalScalar Right)=>Left.Raw!=Right.Raw;
    public static bool operator <(LogicalScalar Left,LogicalScalar Right)=>Left.Raw<Right.Raw;
    public static bool operator >(LogicalScalar Left,LogicalScalar Right)=>Left.Raw>Right.Raw;
    public static bool operator <=(LogicalScalar Left,LogicalScalar Right)=>Left.Raw<=Right.Raw;
    public static bool operator >=(LogicalScalar Left,LogicalScalar Right)=>Left.Raw>=Right.Raw;
    private static int MultiplyDecimalByTwo(Span<byte>Digits){
        var Carry=0;
        for(var Index=Digits.Length-1;Index>=0;Index--){
            var Value=Digits[Index]*2+Carry;
            Digits[Index]=(byte)(Value%10);
            Carry=Value/10;
        }
        return Carry;
    }
    private static UInt128 RoundRightToEven(UInt128 Value,int Shift){
        if(Shift<=0){ return Value; }
        if(Shift>128){ return 0; }
        if(Shift==128){
            var Half=(UInt128)1<<127;
            return Value>Half?1u:0u;
        }
        var Quotient=Value>>Shift;
        var Mask=((UInt128)1<<Shift)-1;
        var Remainder=Value&Mask;
        var HalfValue=(UInt128)1<<(Shift-1);
        if(Remainder>HalfValue||(Remainder==HalfValue&&(Quotient&1)!=0)){ Quotient++; }
        return Quotient;
    }
    private static UInt128 GetMagnitude(Int128 Value)=>Value<0?unchecked((UInt128)(-(Value+1)))+1:(UInt128)Value;
    private static bool TryCreateSigned(UInt128 Magnitude,bool Negative,out LogicalScalar Result){
        Result=default;
        if(Negative){
            if(Magnitude>SignMagnitude){ return false; }
            Result=Magnitude==SignMagnitude?new LogicalScalar(Int128.MinValue):new LogicalScalar(-(Int128)Magnitude);
            return true;
        }
        if(Magnitude>(UInt128)Int128.MaxValue){ return false; }
        Result=new LogicalScalar((Int128)Magnitude);
        return true;
    }
    private static bool TryDivideScaled(UInt128 NumeratorMagnitude,UInt128 Denominator,out UInt128 Quotient,out UInt128 Remainder){
        Quotient=0;
        Remainder=0;
        var Overflow=false;
        for(var Bit=191;Bit>=0;Bit--){
            var High=(Remainder&SignMagnitude)!=0;
            Remainder=(Remainder<<1)|NumeratorBit(NumeratorMagnitude,Bit);
            if(!High&&Remainder<Denominator){ continue; }
            Remainder=unchecked(Remainder-Denominator);
            if(Bit>=128){ Overflow=true; }
            else{ Quotient|=(UInt128)1<<Bit; }
        }
        return !Overflow;
    }
    private static UInt128 NumeratorBit(UInt128 Magnitude,int Bit){
        var SourceBit=Bit-64;
        return SourceBit is >=0 and <128?(Magnitude>>SourceBit)&1:0;
    }
    private static UInt256 MultiplyUnsigned(UInt128 Left,UInt128 Right){
        Span<ulong>Limbs=stackalloc ulong[4];
        var LeftLow=(ulong)Left;
        var LeftHigh=(ulong)(Left>>64);
        var RightLow=(ulong)Right;
        var RightHigh=(ulong)(Right>>64);
        AddProduct(Limbs,0,LeftLow,RightLow);
        AddProduct(Limbs,1,LeftLow,RightHigh);
        AddProduct(Limbs,1,LeftHigh,RightLow);
        AddProduct(Limbs,2,LeftHigh,RightHigh);
        return new UInt256(Limbs[0],Limbs[1],Limbs[2],Limbs[3]);
    }
    private static void AddProduct(Span<ulong>Limbs,int Index,ulong Left,ulong Right){
        var Product=(UInt128)Left*Right;
        var Sum=(UInt128)Limbs[Index]+(ulong)Product;
        Limbs[Index]=(ulong)Sum;
        var Carry=(Sum>>64)+(Product>>64);
        Index++;
        while(Carry!=0&&Index<Limbs.Length){
            Sum=(UInt128)Limbs[Index]+(ulong)Carry;
            Limbs[Index]=(ulong)Sum;
            Carry=(Carry>>64)+(Sum>>64);
            Index++;
        }
    }
    private readonly record struct UInt256(ulong L0,ulong L1,ulong L2,ulong L3):IComparable<UInt256> {
        public int CompareTo(UInt256 Other){
            var Comparison=this.L3.CompareTo(Other.L3);
            if(Comparison!=0){ return Comparison; }
            Comparison=this.L2.CompareTo(Other.L2);
            if(Comparison!=0){ return Comparison; }
            Comparison=this.L1.CompareTo(Other.L1);
            return Comparison!=0?Comparison:this.L0.CompareTo(Other.L0);
        }
        public static UInt256 Subtract(UInt256 Left,UInt256 Right){
            var Borrow=0u;
            var L0=SubtractLimb(Left.L0,Right.L0,ref Borrow);
            var L1=SubtractLimb(Left.L1,Right.L1,ref Borrow);
            var L2=SubtractLimb(Left.L2,Right.L2,ref Borrow);
            var L3=SubtractLimb(Left.L3,Right.L3,ref Borrow);
            return new UInt256(L0,L1,L2,L3);
        }
        private static ulong SubtractLimb(ulong Left,ulong Right,ref uint Borrow){
            var Subtrahend=(UInt128)Right+Borrow;
            Borrow=(uint)((UInt128)Left<Subtrahend?1:0);
            return unchecked((ulong)((UInt128)Left-Subtrahend));
        }
    }
}
