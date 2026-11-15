#if CGE_NUMERIC_CUSTOM_FIXED
using System.Buffers.Binary;
#if CGE_FIXED_64
using RawStorage=System.Int64;
#elif CGE_FIXED_192
using RawStorage=CSharpGameEngine.Numerics.Int192;
#else
using RawStorage=System.Int128;
#endif
namespace CSharpGameEngine.Numerics;
/// <summary>ビルドで選んだ幅・小数位置の固定小数点。演算に管理ヒープの作業領域を使わない。</summary>
public readonly struct LogicalScalar(RawStorage Raw):IComparable<LogicalScalar>,IEquatable<LogicalScalar>,IFixedMathScalar<LogicalScalar> {
    public const int TotalBits=NumericBuildProfile.TotalBits;
    public const int FractionalBits=NumericBuildProfile.FractionalBits;
    public const int SerializedSize=TotalBits/8;
    private const int Digits=TotalBits/64;
    public RawStorage Raw{get;}=Raw;
    public static LogicalScalar Zero=>default;
    public static LogicalScalar One=>FromInt64(1);
    public static LogicalScalar MinValue=>Limit(true);
    public static LogicalScalar MaxValue=>Limit(false);
    public bool IsZero=>this.Equals(default);
    static int IFixedMathScalar<LogicalScalar>.FractionalPrecision=>FractionalBits;
    static bool IFixedMathScalar<LogicalScalar>.TryDivideInteger(LogicalScalar Value,long Divisor,out LogicalScalar Result){
        Result=default;
        Span<ulong>Source=stackalloc ulong[Digits];Value.CopyDigits(Source);
        Span<ulong>Denominator=stackalloc ulong[Digits];SignedInteger(Divisor,Denominator);
        Span<ulong>Output=stackalloc ulong[Digits];
        if(!FixedArithmetic.TryDivide(Source,Denominator,0,Output)){ return false; }
        Result=FromDigits(Output);return true;
    }
    public static LogicalScalar FromRaw(RawStorage Raw)=>new(Raw);
    public static LogicalScalar FromInt64(long Value)=>TryFromScaledInt64(Value,0,out var Result)?Result:throw new OverflowException();
    public static LogicalScalar FromRatio(long Numerator,long Denominator){
        Span<ulong>A=stackalloc ulong[Digits];
        Span<ulong>B=stackalloc ulong[Digits];
        Span<ulong>Output=stackalloc ulong[Digits];
        SignedInteger(Numerator,A);SignedInteger(Denominator,B);
        return FixedArithmetic.TryDivide(A,B,FractionalBits,Output)?FromDigits(Output):Denominator==0?throw new DivideByZeroException():throw new OverflowException();
    }
    public static LogicalScalar FromSingle(float Value)=>TryFromSingle(Value,out var Result)?Result:throw new OverflowException();
    public static bool TryFromSingle(float Value,out LogicalScalar Result){
        Result=default;
        Span<ulong>Output=stackalloc ulong[Digits];
        if(!FixedArithmetic.TryFromSingle(Value,FractionalBits,Output)){ return false; }
        Result=FromDigits(Output);return true;
    }
    public bool TryToSingle(out float Value){ Span<ulong>Source=stackalloc ulong[Digits];this.CopyDigits(Source);return FixedArithmetic.TryToSingle(Source,FractionalBits,out Value); }
    public static LogicalScalar ParseInvariant(string Text)=>TryParseInvariant(Text,out var Result)?Result:throw new FormatException();
    public static bool TryParseInvariant(string?Text,out LogicalScalar Result){
        Result=default;
        Span<byte>Bytes=stackalloc byte[SerializedSize];
        return NumericValueCodec.TryReadFixedText(Text?.Trim(),FractionalBits,Bytes)&&TryReadLittleEndian(Bytes,out Result);
    }
    public string ToStringInvariant()=>NumericValueCodec.ToText(this.ToCanonicalBytes());
    public override string ToString()=>this.ToStringInvariant();
    public byte[]ToCanonicalBytes(){ Span<byte>Bytes=stackalloc byte[SerializedSize];this.WriteLittleEndian(Bytes);return NumericValueCodec.WriteFixed(Bytes,FractionalBits); }
    public static bool TryReadCanonical(ReadOnlySpan<byte>Source,out LogicalScalar Result){
        Result=default;
        Span<byte>Bytes=stackalloc byte[SerializedSize];
        return NumericValueCodec.TryReadFixed(Source,FractionalBits,Bytes)&&TryReadLittleEndian(Bytes,out Result);
    }
    public void WriteLittleEndian(Span<byte>Destination){
        if(Destination.Length<SerializedSize){ throw new ArgumentException("The destination is smaller than the selected fixed format.",nameof(Destination)); }
        Span<ulong>Source=stackalloc ulong[Digits];this.CopyDigits(Source);
        for(var Index=0;Index<Digits;Index++){ BinaryPrimitives.WriteUInt64LittleEndian(Destination[(Index*8)..],Source[Index]); }
    }
    public static bool TryReadLittleEndian(ReadOnlySpan<byte>Source,out LogicalScalar Result){
        Result=default;
        if(Source.Length!=SerializedSize){ return false; }
        Span<ulong>Output=stackalloc ulong[Digits];
        for(var Index=0;Index<Digits;Index++){ Output[Index]=BinaryPrimitives.ReadUInt64LittleEndian(Source[(Index*8)..]); }
        Result=FromDigits(Output);return true;
    }
    public bool TryToScaledInt64(int TargetFractionalBits,out long RawValue){
        RawValue=0;
        if(TargetFractionalBits is <0 or >191){ return false; }
        Span<ulong>Source=stackalloc ulong[Digits];this.CopyDigits(Source);
        Span<ulong>Output=stackalloc ulong[1];
        if(!FixedArithmetic.TryRescale(Source,FractionalBits,TargetFractionalBits,Output)){ return false; }
        RawValue=unchecked((long)Output[0]);return true;
    }
    public static bool TryFromScaledInt64(long RawValue,int SourceFractionalBits,out LogicalScalar Result){
        Result=default;
        if(SourceFractionalBits is <0 or >191){ return false; }
        Span<ulong>Source=stackalloc ulong[1]{unchecked((ulong)RawValue)};
        Span<ulong>Output=stackalloc ulong[Digits];
        if(!FixedArithmetic.TryRescale(Source,SourceFractionalBits,FractionalBits,Output)){ return false; }
        Result=FromDigits(Output);return true;
    }
    public static bool TryAdd(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result)=>TryBinary(Left,Right,0,out Result);
    public static bool TrySubtract(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result)=>TryBinary(Left,Right,1,out Result);
    public static bool TryMultiply(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result)=>TryBinary(Left,Right,2,out Result);
    public static bool TryDivide(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result)=>TryBinary(Left,Right,3,out Result);
    public static bool TrySquareRoot(LogicalScalar Value,out LogicalScalar Result){
        Result=default;
        Span<ulong>Source=stackalloc ulong[Digits];Value.CopyDigits(Source);
        Span<ulong>Output=stackalloc ulong[Digits];
        if(!FixedArithmetic.TrySquareRoot(Source,FractionalBits,Output)){ return false; }
        Result=FromDigits(Output);return true;
    }
    public static bool TryMultiplyInteger(LogicalScalar Value,long Factor,out LogicalScalar Result){
        Result=default;
        Span<ulong>Source=stackalloc ulong[Digits];Value.CopyDigits(Source);
        Span<ulong>Multiplier=stackalloc ulong[Digits];SignedInteger(Factor,Multiplier);
        Span<ulong>Output=stackalloc ulong[Digits];
        if(!FixedArithmetic.TryMultiply(Source,Multiplier,0,Output)){ return false; }
        Result=FromDigits(Output);return true;
    }
    private static bool TryBinary(LogicalScalar Left,LogicalScalar Right,int Operation,out LogicalScalar Result){
        Result=default;
        Span<ulong>A=stackalloc ulong[Digits];Left.CopyDigits(A);
        Span<ulong>B=stackalloc ulong[Digits];Right.CopyDigits(B);
        Span<ulong>Output=stackalloc ulong[Digits];
        var Success=Operation switch{
            0=>FixedArithmetic.TryAdd(A,B,Output),1=>FixedArithmetic.TrySubtract(A,B,Output),
            2=>FixedArithmetic.TryMultiply(A,B,FractionalBits,Output),3=>FixedArithmetic.TryDivide(A,B,FractionalBits,Output),
            _=>FixedArithmetic.TryRemainder(A,B,Output),
        };
        if(!Success){ return false; }
        Result=FromDigits(Output);return true;
    }
    public int CompareTo(LogicalScalar Other){
        Span<ulong>A=stackalloc ulong[Digits];this.CopyDigits(A);
        Span<ulong>B=stackalloc ulong[Digits];Other.CopyDigits(B);
        var Sign=unchecked((long)A[^1]).CompareTo(unchecked((long)B[^1]));
        if(Sign!=0){ return Sign; }
        for(var Index=Digits-2;Index>=0;Index--){ var Comparison=A[Index].CompareTo(B[Index]);if(Comparison!=0){ return Comparison; } }
        return 0;
    }
    public bool Equals(LogicalScalar Other)=>this.Raw.Equals(Other.Raw);
    public override bool Equals(object?Value)=>Value is LogicalScalar Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Raw.GetHashCode();
    public static LogicalScalar operator +(LogicalScalar Left,LogicalScalar Right)=>TryAdd(Left,Right,out var Result)?Result:throw new OverflowException();
    public static LogicalScalar operator -(LogicalScalar Left,LogicalScalar Right)=>TrySubtract(Left,Right,out var Result)?Result:throw new OverflowException();
    public static LogicalScalar operator -(LogicalScalar Value)=>Zero-Value;
    public static LogicalScalar operator *(LogicalScalar Left,LogicalScalar Right)=>TryMultiply(Left,Right,out var Result)?Result:throw new OverflowException();
    public static LogicalScalar operator /(LogicalScalar Left,LogicalScalar Right)=>TryDivide(Left,Right,out var Result)?Result:Right.IsZero?throw new DivideByZeroException():throw new OverflowException();
    public static LogicalScalar operator %(LogicalScalar Left,LogicalScalar Right)=>TryBinary(Left,Right,4,out var Result)?Result:throw new DivideByZeroException();
    public static implicit operator LogicalScalar(int Value)=>FromInt64(Value);
    public static implicit operator LogicalScalar(long Value)=>FromInt64(Value);
    public static implicit operator LogicalScalar(float Value)=>FromSingle(Value);
    public static bool operator ==(LogicalScalar Left,LogicalScalar Right)=>Left.Equals(Right);
    public static bool operator !=(LogicalScalar Left,LogicalScalar Right)=>!Left.Equals(Right);
    public static bool operator <(LogicalScalar Left,LogicalScalar Right)=>Left.CompareTo(Right)<0;
    public static bool operator >(LogicalScalar Left,LogicalScalar Right)=>Left.CompareTo(Right)>0;
    public static bool operator <=(LogicalScalar Left,LogicalScalar Right)=>Left.CompareTo(Right)<=0;
    public static bool operator >=(LogicalScalar Left,LogicalScalar Right)=>Left.CompareTo(Right)>=0;
    private static LogicalScalar Limit(bool Minimum){
        Span<ulong>Value=stackalloc ulong[Digits];
        Value.Fill(Minimum?0:ulong.MaxValue);Value[^1]=Minimum?0x8000000000000000UL:0x7fffffffffffffffUL;
        return FromDigits(Value);
    }
    private static void SignedInteger(long Value,Span<ulong>Output){ Output.Fill(Value<0?ulong.MaxValue:0);Output[0]=unchecked((ulong)Value); }
    private void CopyDigits(Span<ulong>Output){
#if CGE_FIXED_64
        Output[0]=unchecked((ulong)this.Raw);
#elif CGE_FIXED_192
        Output[0]=this.Raw.Low;Output[1]=this.Raw.Middle;Output[2]=unchecked((ulong)this.Raw.High);
#else
        Output[0]=unchecked((ulong)this.Raw);Output[1]=unchecked((ulong)(this.Raw>>64));
#endif
    }
    private static LogicalScalar FromDigits(ReadOnlySpan<ulong>Value){
#if CGE_FIXED_64
        return new(unchecked((long)Value[0]));
#elif CGE_FIXED_192
        return new(new Int192(Value[0],Value[1],unchecked((long)Value[2])));
#else
        return new(unchecked((Int128)((UInt128)Value[1]<<64|Value[0])));
#endif
    }
}
#endif
