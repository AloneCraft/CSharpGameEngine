using System.Buffers.Binary;
namespace CSharpGameEngine.Numerics;
/// <summary>選択した数値型から離散インデックス・比率・許容誤差へ接続する。</summary>
public static class LogicalNumeric {
    public static LogicalScalar ParseInvariant(string Text)=>LogicalScalar.ParseInvariant(Text);
    public static string ProfileId{
        get{
#if CGE_NUMERIC_FLOAT
            return "float32";
#elif CGE_NUMERIC_DOUBLE
            return "float64";
#else
            return "fixed-"+LogicalScalar.TotalBits.ToString(System.Globalization.CultureInfo.InvariantCulture)+"-"+LogicalScalar.FractionalBits.ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
        }
    }
    public static string CacheProfileId=>ProfileId=="fixed-128-64"?"LogicalScalar-Q64.64":ProfileId;
    public static LogicalScalar PrecisionStep{
        get{
#if CGE_NUMERIC_FLOAT || CGE_NUMERIC_DOUBLE
            return LogicalScalar.BitIncrement(LogicalScalar.One)-LogicalScalar.One;
#else
            return LogicalScalar.TryFromScaledInt64(1,LogicalScalar.FractionalBits,out var Value)?Value:throw new InvalidOperationException();
#endif
        }
    }
    public static LogicalScalar Tolerance(int MinimumFractionalBits,long Steps){
        LogicalScalar.TryFromScaledInt64(1,MinimumFractionalBits,out var Minimum);
        var Scaled=ScaleByRatio(PrecisionStep,Steps,1);
        var Value=Minimum>Scaled?Minimum:Scaled;
        var Maximum=LogicalScalar.FromRatio(1,8);
        return Value>Maximum?Maximum:Value;
    }
    public static long FloorToInt64(LogicalScalar Value)=>TryScaledFloorQuotient(Value,LogicalScalar.One,1,out var Result)?Result:throw new OverflowException();
    public static long FloorQuotient(LogicalScalar Value,LogicalScalar Divisor,long Scale=1)=>TryScaledFloorQuotient(Value,Divisor,Scale,out var Result)?Result:throw new OverflowException();
    public static long FloorQuotientClamped(LogicalScalar Value,LogicalScalar Divisor,long Scale=1){
        if(!(Divisor>LogicalScalar.Zero)){ throw new ArgumentOutOfRangeException(nameof(Divisor)); }
#if CGE_NUMERIC_FLOAT || CGE_NUMERIC_DOUBLE
        if(!LogicalScalar.IsFinite(Value)||!LogicalScalar.IsFinite(Divisor)){ throw new ArgumentOutOfRangeException(nameof(Value)); }
#endif
        return TryScaledFloorQuotient(Value,Divisor,Scale,out var Result)?Result:(Value<LogicalScalar.Zero)^(Scale<0)?long.MinValue:long.MaxValue;
    }
    public static bool TryScaledFloorQuotient(LogicalScalar Value,LogicalScalar Divisor,long Scale,out long Result){
        Result=0;
#if CGE_NUMERIC_FLOAT
        return FixedArithmetic.TryFloorIeeeRatio(BitConverter.SingleToUInt32Bits(Value),BitConverter.SingleToUInt32Bits(Divisor),23,8,127,Scale,out Result);
#elif CGE_NUMERIC_DOUBLE
        return FixedArithmetic.TryFloorIeeeRatio(BitConverter.DoubleToUInt64Bits(Value),BitConverter.DoubleToUInt64Bits(Divisor),52,11,1023,Scale,out Result);
#else
        Span<ulong>A=stackalloc ulong[4];CopyDigits(Value,A);
        Span<ulong>B=stackalloc ulong[4];CopyDigits(Divisor,B);
        Span<ulong>Factor=stackalloc ulong[4];Integer(Scale,Factor);
        Span<ulong>Product=stackalloc ulong[4];
        Span<ulong>Remainder=stackalloc ulong[4];
        return FixedArithmetic.TryMultiply(A,Factor,0,Product)&&FixedArithmetic.TryFloorDivRemToInt64(Product,B,out Result,Remainder);
#endif
    }
    public static bool TryDivRemFloor(LogicalScalar Value,LogicalScalar Divisor,out long Quotient,out LogicalScalar Remainder){
        Remainder=default;
        if(!TryScaledFloorQuotient(Value,Divisor,1,out Quotient)){ return false; }
        var Rest=Value%Divisor;if(Rest<LogicalScalar.Zero){ Rest+=Divisor; }
        Remainder=Rest;return true;
    }
    public static LogicalScalar ScaleByRatio(LogicalScalar Value,long Numerator,long Denominator)=>TryScaleByRatio(Value,Numerator,Denominator,out var Result)?Result:throw new OverflowException();
    public static bool TryScaleByRatio(LogicalScalar Value,long Numerator,long Denominator,out LogicalScalar Result){
        Result=default;if(Denominator==0){ return false; }
#if CGE_NUMERIC_FLOAT || CGE_NUMERIC_DOUBLE
        return LogicalScalar.TryMultiply(Value,(LogicalScalar)Numerator/(LogicalScalar)Denominator,out Result);
#else
        Span<ulong>A=stackalloc ulong[4];CopyDigits(Value,A);
        Span<ulong>N=stackalloc ulong[4];Integer(Numerator,N);
        Span<ulong>D=stackalloc ulong[4];Integer(Denominator,D);
        Span<ulong>Product=stackalloc ulong[4];
        Span<ulong>Quotient=stackalloc ulong[4];
        Span<ulong>Output=stackalloc ulong[LogicalScalar.SerializedSize/8];
        if(!FixedArithmetic.TryMultiply(A,N,0,Product)||!FixedArithmetic.TryDivide(Product,D,0,Quotient)||!FixedArithmetic.TryRescale(Quotient,LogicalScalar.FractionalBits,LogicalScalar.FractionalBits,Output)){ return false; }
        Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];
        for(var Index=0;Index<Output.Length;Index++){ BinaryPrimitives.WriteUInt64LittleEndian(Bytes[(Index*8)..],Output[Index]); }
        return LogicalScalar.TryReadLittleEndian(Bytes,out Result);
#endif
    }
#if !CGE_NUMERIC_FLOAT && !CGE_NUMERIC_DOUBLE
    private static void CopyDigits(LogicalScalar Value,Span<ulong>Output){
        Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];Value.WriteLittleEndian(Bytes);
        Output.Fill((Bytes[^1]&0x80)!=0?ulong.MaxValue:0);
        for(var Index=0;Index<Bytes.Length/8;Index++){ Output[Index]=BinaryPrimitives.ReadUInt64LittleEndian(Bytes[(Index*8)..]); }
    }
    private static void Integer(long Value,Span<ulong>Output){ Output.Fill(Value<0?ulong.MaxValue:0);Output[0]=unchecked((ulong)Value); }
#endif
}
