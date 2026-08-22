using System.Numerics;

namespace CSharpGameEngine.Numerics.Logical192;

/// <summary>Logical coordinates: R (right, +X), U (up, +Y), F (forward, +Z).</summary>
    public readonly struct Vector3(Scalar R,Scalar U,Scalar F):IEquatable<Vector3> {
        public Scalar R { get; }=R;
        public Scalar U { get; }=U;
        public Scalar F { get; }=F;public static Vector3 Zero=>default; public static Vector3 One=>new(Scalar.One,Scalar.One,Scalar.One);
        public static Vector3 UnitR=>new(Scalar.One,Scalar.Zero,Scalar.Zero); public static Vector3 UnitU=>new(Scalar.Zero,Scalar.One,Scalar.Zero); public static Vector3 UnitF=>new(Scalar.Zero,Scalar.Zero,Scalar.One);
        public Scalar LengthSquared=>this.R*this.R+this.U*this.U+this.F*this.F;
        public bool TryNormalize(out Vector3 Result){ if(this.LengthSquared.IsZero||!Scalar.TrySquareRoot(this.LengthSquared,out Scalar Length)||Length.IsZero||!Scalar.TryDivide(this.R,Length,out Scalar R)||!Scalar.TryDivide(this.U,Length,out Scalar U)||!Scalar.TryDivide(this.F,Length,out Scalar F)){ Result=default; return false; } Result=new(R,U,F); return true; }
        public Vector3 Normalize()=>this.TryNormalize(out Vector3 Result)?Result:throw new InvalidOperationException();
        public static Scalar Dot(Vector3 Left,Vector3 Right)=>Left.R*Right.R+Left.U*Right.U+Left.F*Right.F;
        public static Vector3 Cross(Vector3 Left,Vector3 Right)=>new(Left.U*Right.F-Left.F*Right.U,Left.F*Right.R-Left.R*Right.F,Left.R*Right.U-Left.U*Right.R);
        public static Vector3 operator +(Vector3 Left,Vector3 Right)=>new(Left.R+Right.R,Left.U+Right.U,Left.F+Right.F); public static Vector3 operator -(Vector3 Left,Vector3 Right)=>new(Left.R-Right.R,Left.U-Right.U,Left.F-Right.F); public static Vector3 operator -(Vector3 Value)=>new(-Value.R,-Value.U,-Value.F);
        public static Vector3 operator *(Vector3 Value,Scalar Factor)=>new(Value.R*Factor,Value.U*Factor,Value.F*Factor); public static Vector3 operator *(Scalar Factor,Vector3 Value)=>Value*Factor; public static Vector3 operator /(Vector3 Value,Scalar Factor)=>new(Value.R/Factor,Value.U/Factor,Value.F/Factor);
        public bool Equals(Vector3 Other)=>this.R==Other.R&&this.U==Other.U&&this.F==Other.F; public override bool Equals(object? Object)=>Object is Vector3 Other&&this.Equals(Other); public override int GetHashCode()=>HashCode.Combine(this.R,this.U,this.F); public static bool operator ==(Vector3 Left,Vector3 Right)=>Left.Equals(Right); public static bool operator !=(Vector3 Left,Vector3 Right)=>!Left.Equals(Right); public System.Numerics.Vector3 ToSingle()=>new((float)this.R,(float)this.U,(float)this.F);
    }
