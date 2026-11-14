#if CGE_NUMERIC_FLOAT || CGE_NUMERIC_DOUBLE
using System.Buffers.Binary;
using System.Globalization;
#if CGE_NUMERIC_DOUBLE
using NativeScalar=System.Double;
#else
using NativeScalar=System.Single;
#endif
namespace CSharpGameEngine.Numerics;
public static class FloatingScalarExtensions {
    extension(NativeScalar Number) {
        public static NativeScalar Zero=>0;
        public static NativeScalar One=>1;
        public static int FractionalBits=>-1;
#if CGE_NUMERIC_DOUBLE
        public static int TotalBits=>64;
        public static int SerializedSize=>8;
#else
        public static int TotalBits=>32;
        public static int SerializedSize=>4;
#endif
        public bool IsZero=>Number==0;
        public static NativeScalar FromInt64(long Value)=>(NativeScalar)Value;
        public static NativeScalar FromSingle(float Value)=>float.IsFinite(Value)?Value:throw new OverflowException();
        public static bool TryFromSingle(float Value,out NativeScalar Result){ Result=default;if(!float.IsFinite(Value)){ return false; }Result=Value;return true; }
        public bool TryToSingle(out float Value){ Value=default;var Projected=(float)Number;if(!NativeScalar.IsFinite(Number)||!float.IsFinite(Projected)){ return false; }Value=Projected;return true; }
        public static NativeScalar FromRatio(long Numerator,long Denominator)=>Denominator==0?throw new DivideByZeroException():Require((NativeScalar)Numerator/(NativeScalar)Denominator);
        public static NativeScalar ParseInvariant(string Text)=>NativeScalar.TryParseInvariant(Text,out var Value)?Value:throw new FormatException();
        public static bool TryParseInvariant(string?Text,out NativeScalar Value){
            Value=default;
            return NativeScalar.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var Parsed)&&TryFinite(Parsed,out Value);
        }
        public string ToStringInvariant()=>NumericValueCodec.ToText(Number.ToCanonicalBytes());
        public byte[]ToCanonicalBytes(){
#if CGE_NUMERIC_DOUBLE
            return NumericValueCodec.WriteDouble(Number);
#else
            return NumericValueCodec.WriteSingle(Number);
#endif
        }
        public static bool TryReadCanonical(ReadOnlySpan<byte>Source,out NativeScalar Result){
#if CGE_NUMERIC_DOUBLE
            return NumericValueCodec.TryReadDouble(Source,out Result);
#else
            return NumericValueCodec.TryReadSingle(Source,out Result);
#endif
        }
        public void WriteLittleEndian(Span<byte>Destination){
            if(!NativeScalar.IsFinite(Number)){ throw new OverflowException(); }
#if CGE_NUMERIC_DOUBLE
            BinaryPrimitives.WriteUInt64LittleEndian(Destination,BitConverter.DoubleToUInt64Bits(Number));
#else
            BinaryPrimitives.WriteUInt32LittleEndian(Destination,BitConverter.SingleToUInt32Bits(Number));
#endif
        }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte>Source,out NativeScalar Result){
            Result=default;
            if(Source.Length!=NativeScalar.SerializedSize){ return false; }
#if CGE_NUMERIC_DOUBLE
            return TryFinite(BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReadUInt64LittleEndian(Source)),out Result);
#else
            return TryFinite(BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(Source)),out Result);
#endif
        }
        public bool TryToScaledInt64(int TargetFractionalBits,out long RawValue){
            RawValue=0;
            if(TargetFractionalBits is <0 or >191){ return false; }
            Span<ulong>Output=stackalloc ulong[1];
#if CGE_NUMERIC_DOUBLE
            if(!FixedArithmetic.TryFromDouble(Number,TargetFractionalBits,Output)){ return false; }
#else
            if(!FixedArithmetic.TryFromSingle(Number,TargetFractionalBits,Output)){ return false; }
#endif
            RawValue=unchecked((long)Output[0]);return true;
        }
        public static bool TryFromScaledInt64(long RawValue,int SourceFractionalBits,out NativeScalar Result){
            Result=default;
            if(SourceFractionalBits is <0 or >191){ return false; }
            Span<ulong>Source=stackalloc ulong[1]{unchecked((ulong)RawValue)};
#if CGE_NUMERIC_DOUBLE
            return FixedArithmetic.TryToDouble(Source,SourceFractionalBits,out Result);
#else
            return FixedArithmetic.TryToSingle(Source,SourceFractionalBits,out Result);
#endif
        }
        public static bool TryAdd(NativeScalar Left,NativeScalar Right,out NativeScalar Result)=>TryOperands(Left,Right,Left+Right,out Result);
        public static bool TrySubtract(NativeScalar Left,NativeScalar Right,out NativeScalar Result)=>TryOperands(Left,Right,Left-Right,out Result);
        public static bool TryMultiply(NativeScalar Left,NativeScalar Right,out NativeScalar Result)=>TryOperands(Left,Right,Left*Right,out Result);
        public static bool TryDivide(NativeScalar Left,NativeScalar Right,out NativeScalar Result)=>TryOperands(Left,Right,Left/Right,out Result);
        public static bool TryMultiplyInteger(NativeScalar Value,long Factor,out NativeScalar Result)=>TryOperands(Value,Factor,Value*Factor,out Result);
        public static bool TrySquareRoot(NativeScalar Value,out NativeScalar Result){
            Result=default;
            if(!NativeScalar.IsFinite(Value)||Value<0){ return false; }
            return TryFinite(NativeScalar.Sqrt(Value),out Result);
        }
    }
    private static NativeScalar Require(NativeScalar Value)=>NativeScalar.IsFinite(Value)?Value:throw new OverflowException();
    private static bool TryFinite(NativeScalar Value,out NativeScalar Result){ Result=default;if(!NativeScalar.IsFinite(Value)){ return false; }Result=Value;return true; }
    private static bool TryOperands(NativeScalar Left,NativeScalar Right,NativeScalar Value,out NativeScalar Result){ Result=default;return NativeScalar.IsFinite(Left)&&NativeScalar.IsFinite(Right)&&TryFinite(Value,out Result); }
}
#endif
