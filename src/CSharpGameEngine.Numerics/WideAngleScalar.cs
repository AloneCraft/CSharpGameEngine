#if !CGE_NUMERIC_FLOAT && !CGE_NUMERIC_DOUBLE
using System.Buffers.Binary;
namespace CSharpGameEngine.Numerics;
/// <summary>数学関数の一時値。保存する数値型を変えず、整数部と粗い設定の作業精度を確保する。</summary>
internal readonly struct WideAngleScalar(ulong Low,ulong Middle1,ulong Middle2,long High):IFixedMathScalar<WideAngleScalar> {
    private readonly ulong Low=Low;
    private readonly ulong Middle1=Middle1;
    private readonly ulong Middle2=Middle2;
    private readonly long High=High;
    private const int Precision=LogicalScalar.FractionalBits<32?32:LogicalScalar.FractionalBits;
    public static int FractionalPrecision=>Precision;
    public bool IsZero=>(this.Low|this.Middle1|this.Middle2|unchecked((ulong)this.High))==0;
    public static WideAngleScalar Zero=>default;
    public static WideAngleScalar One{
        get{
            Span<ulong>Value=stackalloc ulong[4];Value.Clear();Value[Precision/64]=1UL<<(Precision%64);
            return FromDigits(Value);
        }
    }
    public static WideAngleScalar MinValue=>new(0,0,0,long.MinValue);
    public static WideAngleScalar ParseInvariant(string Text){
        Span<byte>Bytes=stackalloc byte[32];
        if(!NumericValueCodec.TryReadWideFixedText(Text,Precision,Bytes)){ throw new FormatException(); }
        return new(BinaryPrimitives.ReadUInt64LittleEndian(Bytes),BinaryPrimitives.ReadUInt64LittleEndian(Bytes[8..]),BinaryPrimitives.ReadUInt64LittleEndian(Bytes[16..]),BinaryPrimitives.ReadInt64LittleEndian(Bytes[24..]));
    }
    public static WideAngleScalar FromRatio(long Numerator,long Denominator){
        Span<ulong>A=stackalloc ulong[4];Integer(Numerator,A);
        Span<ulong>B=stackalloc ulong[4];Integer(Denominator,B);
        Span<ulong>Output=stackalloc ulong[4];
        return FixedArithmetic.TryDivide(A,B,Precision,Output)?FromDigits(Output):throw new OverflowException();
    }
    internal static WideAngleScalar FromLogical(LogicalScalar Value){
        Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];Value.WriteLittleEndian(Bytes);
        Span<ulong>Input=stackalloc ulong[LogicalScalar.SerializedSize/8];
        for(var Index=0;Index<Input.Length;Index++){ Input[Index]=BinaryPrimitives.ReadUInt64LittleEndian(Bytes[(Index*8)..]); }
        Span<ulong>Output=stackalloc ulong[4];
        if(!FixedArithmetic.TryRescale(Input,LogicalScalar.FractionalBits,Precision,Output)){ throw new OverflowException(); }
        return FromDigits(Output);
    }
    internal bool TryToLogical(out LogicalScalar Value){
        Value=default;
        Span<ulong>Input=stackalloc ulong[4];this.CopyDigits(Input);
        Span<ulong>Output=stackalloc ulong[LogicalScalar.SerializedSize/8];
        if(!FixedArithmetic.TryRescale(Input,Precision,LogicalScalar.FractionalBits,Output)){ return false; }
        Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];
        for(var Index=0;Index<Output.Length;Index++){ BinaryPrimitives.WriteUInt64LittleEndian(Bytes[(Index*8)..],Output[Index]); }
        return LogicalScalar.TryReadLittleEndian(Bytes,out Value);
    }
    public static bool TryAdd(WideAngleScalar Left,WideAngleScalar Right,out WideAngleScalar Result)=>TryBinary(Left,Right,0,out Result);
    public static bool TrySubtract(WideAngleScalar Left,WideAngleScalar Right,out WideAngleScalar Result)=>TryBinary(Left,Right,1,out Result);
    public static bool TryMultiply(WideAngleScalar Left,WideAngleScalar Right,out WideAngleScalar Result)=>TryBinary(Left,Right,2,out Result);
    public static bool TryDivide(WideAngleScalar Left,WideAngleScalar Right,out WideAngleScalar Result)=>TryBinary(Left,Right,3,out Result);
    public static bool TrySquareRoot(WideAngleScalar Value,out WideAngleScalar Result){
        Result=default;
        Span<ulong>Input=stackalloc ulong[4];Value.CopyDigits(Input);
        Span<ulong>Output=stackalloc ulong[4];
        if(!FixedArithmetic.TrySquareRoot(Input,Precision,Output)){ return false; }
        Result=FromDigits(Output);return true;
    }
    public static bool TryMultiplyInteger(WideAngleScalar Value,long Factor,out WideAngleScalar Result)=>TryInteger(Value,Factor,false,out Result);
    public static bool TryDivideInteger(WideAngleScalar Value,long Divisor,out WideAngleScalar Result)=>TryInteger(Value,Divisor,true,out Result);
    private static bool TryInteger(WideAngleScalar Value,long IntegerValue,bool Divide,out WideAngleScalar Result){
        Result=default;
        Span<ulong>Input=stackalloc ulong[4];Value.CopyDigits(Input);
        Span<ulong>Operand=stackalloc ulong[4];Integer(IntegerValue,Operand);
        Span<ulong>Output=stackalloc ulong[4];
        if(!(Divide?FixedArithmetic.TryDivide(Input,Operand,0,Output):FixedArithmetic.TryMultiply(Input,Operand,0,Output))){ return false; }
        Result=FromDigits(Output);return true;
    }
    private static bool TryBinary(WideAngleScalar Left,WideAngleScalar Right,int Operation,out WideAngleScalar Result){
        Result=default;
        Span<ulong>A=stackalloc ulong[4];Left.CopyDigits(A);
        Span<ulong>B=stackalloc ulong[4];Right.CopyDigits(B);
        Span<ulong>Output=stackalloc ulong[4];
        var Success=Operation switch{
            0=>FixedArithmetic.TryAdd(A,B,Output),1=>FixedArithmetic.TrySubtract(A,B,Output),
            2=>FixedArithmetic.TryMultiply(A,B,Precision,Output),3=>FixedArithmetic.TryDivide(A,B,Precision,Output),
            _=>FixedArithmetic.TryRemainder(A,B,Output),
        };
        if(!Success){ return false; }
        Result=FromDigits(Output);return true;
    }
    public static WideAngleScalar operator +(WideAngleScalar Left,WideAngleScalar Right)=>TryAdd(Left,Right,out var Result)?Result:throw new OverflowException();
    public static WideAngleScalar operator -(WideAngleScalar Left,WideAngleScalar Right)=>TrySubtract(Left,Right,out var Result)?Result:throw new OverflowException();
    public static WideAngleScalar operator -(WideAngleScalar Value)=>Zero-Value;
    public static WideAngleScalar operator *(WideAngleScalar Left,WideAngleScalar Right)=>TryMultiply(Left,Right,out var Result)?Result:throw new OverflowException();
    public static WideAngleScalar operator /(WideAngleScalar Left,WideAngleScalar Right)=>TryDivide(Left,Right,out var Result)?Result:throw new OverflowException();
    public static WideAngleScalar operator %(WideAngleScalar Left,WideAngleScalar Right)=>TryBinary(Left,Right,4,out var Result)?Result:throw new DivideByZeroException();
    public static bool operator ==(WideAngleScalar Left,WideAngleScalar Right)=>Left.Low==Right.Low&&Left.Middle1==Right.Middle1&&Left.Middle2==Right.Middle2&&Left.High==Right.High;
    public static bool operator !=(WideAngleScalar Left,WideAngleScalar Right)=>!(Left==Right);
    public static bool operator <(WideAngleScalar Left,WideAngleScalar Right)=>Left.CompareTo(Right)<0;
    public static bool operator >(WideAngleScalar Left,WideAngleScalar Right)=>Left.CompareTo(Right)>0;
    public static bool operator <=(WideAngleScalar Left,WideAngleScalar Right)=>Left.CompareTo(Right)<=0;
    public static bool operator >=(WideAngleScalar Left,WideAngleScalar Right)=>Left.CompareTo(Right)>=0;
    private int CompareTo(WideAngleScalar Other){
        if(this.High!=Other.High){ return this.High.CompareTo(Other.High); }
        if(this.Middle2!=Other.Middle2){ return this.Middle2.CompareTo(Other.Middle2); }
        if(this.Middle1!=Other.Middle1){ return this.Middle1.CompareTo(Other.Middle1); }
        return this.Low.CompareTo(Other.Low);
    }
    public override bool Equals(object?Value)=>Value is WideAngleScalar Other&&this==Other;
    public override int GetHashCode()=>HashCode.Combine(this.Low,this.Middle1,this.Middle2,this.High);
    private void CopyDigits(Span<ulong>Value){ Value[0]=this.Low;Value[1]=this.Middle1;Value[2]=this.Middle2;Value[3]=unchecked((ulong)this.High); }
    private static WideAngleScalar FromDigits(ReadOnlySpan<ulong>Value)=>new(Value[0],Value[1],Value[2],unchecked((long)Value[3]));
    private static void Integer(long Value,Span<ulong>Digits){ Digits.Fill(Value<0?ulong.MaxValue:0);Digits[0]=unchecked((ulong)Value); }
}
#endif
