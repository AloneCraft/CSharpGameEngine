using System.Numerics;

namespace CSharpGameEngine.Numerics.Logical256;

public readonly struct Scalar(ulong Limb0,ulong Limb1,ulong Limb2,ulong Limb3):IComparable<Scalar>,IEquatable<Scalar> {
        private readonly ulong Limb0=Limb0;
        private readonly ulong Limb1=Limb1;
        private readonly ulong Limb2=Limb2;
        private readonly ulong Limb3=Limb3;
        internal const int TotalBits=256;
        internal const int FractionalBits=128;private BigInteger Raw=>FixedScalarMath.ToRaw(TotalBits,Limb0,Limb1,Limb2,Limb3);
        public static Scalar Zero=>default;
        public static Scalar One=>FromRawChecked(BigInteger.One<<FractionalBits);
        public bool IsZero=>this.Raw.IsZero;
        public static Scalar FromInt64(long Value)=>FromRawChecked((BigInteger)Value<<FractionalBits);
        public static Scalar FromDouble(double Value){ if(!TryFromDouble(Value,out Scalar Result)){ throw new OverflowException(); } return Result; }
        public static bool TryFromDouble(double Value,out Scalar Result)=>TryRaw(FixedScalarMath.TryFromDouble(Value,TotalBits,FractionalBits,out BigInteger Raw)?Raw:null,out Result);
        public static Scalar ParseInvariant(string Text){ if(!TryParseInvariant(Text,out Scalar Result)){ throw new FormatException(); } return Result; }
        public static bool TryParseInvariant(string? Text,out Scalar Result)=>TryRaw(FixedScalarMath.TryParseInvariant(Text,TotalBits,FractionalBits,out BigInteger Raw)?Raw:null,out Result);
        public bool TryToScaledInt64(int TargetFractionalBits,out long RawValue)=>FixedScalarMath.TryToScaledInt64(this.Raw,FractionalBits,TargetFractionalBits,out RawValue);
        public static bool TryFromScaledInt64(long RawValue,int SourceFractionalBits,out Scalar Result)=>TryRaw(FixedScalarMath.TryFromScaledInt64(RawValue,SourceFractionalBits,FractionalBits,TotalBits,out BigInteger Raw)?Raw:null,out Result);
        public double ToDouble()=>(double)this.Raw/(double)(BigInteger.One<<FractionalBits);
        public bool TryToSingle(out float Value){ double D=this.ToDouble(); if(!double.IsFinite(D)||D>float.MaxValue||D<float.MinValue){ Value=0f; return false; } Value=(float)D; return true; }
        public override string ToString()=>FixedScalarMath.ToStringInvariant(this.Raw,FractionalBits);
        public string ToStringInvariant()=>this.ToString();
        public static bool TryAdd(Scalar Left,Scalar Right,out Scalar Result)=>TryRaw(FixedScalarMath.TryAdd(Left.Raw,Right.Raw,TotalBits,out BigInteger Raw)?Raw:null,out Result);
        public static bool TrySubtract(Scalar Left,Scalar Right,out Scalar Result)=>TryRaw(FixedScalarMath.TrySubtract(Left.Raw,Right.Raw,TotalBits,out BigInteger Raw)?Raw:null,out Result);
        public static bool TryMultiply(Scalar Left,Scalar Right,out Scalar Result)=>TryRaw(FixedScalarMath.TryMultiply(Left.Raw,Right.Raw,TotalBits,FractionalBits,out BigInteger Raw)?Raw:null,out Result);
        public static bool TryDivide(Scalar Left,Scalar Right,out Scalar Result)=>TryRaw(FixedScalarMath.TryDivide(Left.Raw,Right.Raw,TotalBits,FractionalBits,out BigInteger Raw)?Raw:null,out Result);
        public static bool TrySquareRoot(Scalar Value,out Scalar Result)=>TryRaw(FixedScalarMath.TrySquareRoot(Value.Raw,TotalBits,FractionalBits,out BigInteger Raw)?Raw:null,out Result);
        public static Scalar operator +(Scalar Left,Scalar Right)=>TryAdd(Left,Right,out Scalar Result)?Result:throw new OverflowException();
        public static Scalar operator -(Scalar Left,Scalar Right)=>TrySubtract(Left,Right,out Scalar Result)?Result:throw new OverflowException();
        public static Scalar operator -(Scalar Value)=>FromRawChecked(-Value.Raw);
        public static Scalar operator *(Scalar Left,Scalar Right)=>TryMultiply(Left,Right,out Scalar Result)?Result:throw new OverflowException();
        public static Scalar operator /(Scalar Left,Scalar Right)=>TryDivide(Left,Right,out Scalar Result)?Result:Right.IsZero?throw new DivideByZeroException():throw new OverflowException();
        public static implicit operator Scalar(int Value)=>FromInt64(Value);
        public static implicit operator Scalar(long Value)=>FromInt64(Value);
        public static implicit operator Scalar(float Value)=>FromDouble(Value);
        public static implicit operator Scalar(double Value)=>FromDouble(Value);
        public static implicit operator double(Scalar Value)=>Value.ToDouble();
        public static implicit operator float(Scalar Value)=>(float)Value.ToDouble();
        public int CompareTo(Scalar Other)=>this.Raw.CompareTo(Other.Raw);
        public bool Equals(Scalar Other)=>this.Raw==Other.Raw;
        public override bool Equals(object? Object)=>Object is Scalar Other&&this.Equals(Other);
        public override int GetHashCode()=>this.Raw.GetHashCode();
        public static bool operator ==(Scalar Left,Scalar Right)=>Left.Equals(Right);
        public static bool operator !=(Scalar Left,Scalar Right)=>!Left.Equals(Right);
        public static bool operator <(Scalar Left,Scalar Right)=>Left.Raw<Right.Raw;
        public static bool operator >(Scalar Left,Scalar Right)=>Left.Raw>Right.Raw;
        public static bool operator <=(Scalar Left,Scalar Right)=>Left.Raw<=Right.Raw;
        public static bool operator >=(Scalar Left,Scalar Right)=>Left.Raw>=Right.Raw;
        private static Scalar FromRawChecked(BigInteger Raw)=>TryRaw(Raw,out Scalar Result)?Result:throw new OverflowException();
        private static bool TryRaw(BigInteger? Raw,out Scalar Result){ Result=default; return Raw.HasValue&&FixedScalarMath.TryFromRaw(Raw.Value,TotalBits,out ulong Limb0,out ulong Limb1,out ulong Limb2,out ulong Limb3)&&((Result=new Scalar(Limb0,Limb1,Limb2,Limb3))==Result); }
    }
