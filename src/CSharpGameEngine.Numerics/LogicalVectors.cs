using System.Numerics;

namespace CSharpGameEngine.Numerics.Logical128 {
    public readonly struct Vector3:IEquatable<Vector3> {
        public Scalar X { get; }
        public Scalar Y { get; }
        public Scalar Z { get; }
        public Vector3(Scalar X,Scalar Y,Scalar Z){ this.X=X; this.Y=Y; this.Z=Z; }
        public static Vector3 Zero=>default;
        public static Vector3 One=>new(Scalar.One,Scalar.One,Scalar.One);
        public static Vector3 UnitX=>new(Scalar.One,Scalar.Zero,Scalar.Zero);
        public static Vector3 UnitY=>new(Scalar.Zero,Scalar.One,Scalar.Zero);
        public static Vector3 UnitZ=>new(Scalar.Zero,Scalar.Zero,Scalar.One);
        public Scalar LengthSquared=>this.X*this.X+this.Y*this.Y+this.Z*this.Z;
        public bool TryNormalize(out Vector3 Result){ if(this.LengthSquared.IsZero||!Scalar.TrySquareRoot(this.LengthSquared,out Scalar Length)||Length.IsZero||!Scalar.TryDivide(this.X,Length,out Scalar X)||!Scalar.TryDivide(this.Y,Length,out Scalar Y)||!Scalar.TryDivide(this.Z,Length,out Scalar Z)){ Result=default; return false; } Result=new(X,Y,Z); return true; }
        public Vector3 Normalize()=>this.TryNormalize(out Vector3 Result)?Result:throw new InvalidOperationException("The vector cannot be normalized.");
        public static Scalar Dot(Vector3 Left,Vector3 Right)=>Left.X*Right.X+Left.Y*Right.Y+Left.Z*Right.Z;
        public static Vector3 Cross(Vector3 Left,Vector3 Right)=>new(Left.Y*Right.Z-Left.Z*Right.Y,Left.Z*Right.X-Left.X*Right.Z,Left.X*Right.Y-Left.Y*Right.X);
        public static Vector3 operator +(Vector3 Left,Vector3 Right)=>new(Left.X+Right.X,Left.Y+Right.Y,Left.Z+Right.Z);
        public static Vector3 operator -(Vector3 Left,Vector3 Right)=>new(Left.X-Right.X,Left.Y-Right.Y,Left.Z-Right.Z);
        public static Vector3 operator -(Vector3 Value)=>new(-Value.X,-Value.Y,-Value.Z);
        public static Vector3 operator *(Vector3 Value,Scalar Factor)=>new(Value.X*Factor,Value.Y*Factor,Value.Z*Factor);
        public static Vector3 operator *(Scalar Factor,Vector3 Value)=>Value*Factor;
        public static Vector3 operator /(Vector3 Value,Scalar Factor)=>new(Value.X/Factor,Value.Y/Factor,Value.Z/Factor);
        public bool Equals(Vector3 Other)=>this.X==Other.X&&this.Y==Other.Y&&this.Z==Other.Z;
        public override bool Equals(object? Object)=>Object is Vector3 Other&&this.Equals(Other);
        public override int GetHashCode()=>HashCode.Combine(this.X,this.Y,this.Z);
        public static bool operator ==(Vector3 Left,Vector3 Right)=>Left.Equals(Right);
        public static bool operator !=(Vector3 Left,Vector3 Right)=>!Left.Equals(Right);
        public System.Numerics.Vector3 ToSingle()=>new((float)this.X,(float)this.Y,(float)this.Z);
    }

    public readonly struct Quaternion:IEquatable<Quaternion> {
        public Scalar X { get; }
        public Scalar Y { get; }
        public Scalar Z { get; }
        public Scalar W { get; }
        public Quaternion(Scalar X,Scalar Y,Scalar Z,Scalar W){ this.X=X; this.Y=Y; this.Z=Z; this.W=W; }
        public static Quaternion Identity=>new(Scalar.Zero,Scalar.Zero,Scalar.Zero,Scalar.One);
        public Scalar LengthSquared=>this.X*this.X+this.Y*this.Y+this.Z*this.Z+this.W*this.W;
        public bool TryNormalize(out Quaternion Result){ if(this.LengthSquared.IsZero||!Scalar.TrySquareRoot(this.LengthSquared,out Scalar Length)||Length.IsZero||!Scalar.TryDivide(this.X,Length,out Scalar X)||!Scalar.TryDivide(this.Y,Length,out Scalar Y)||!Scalar.TryDivide(this.Z,Length,out Scalar Z)||!Scalar.TryDivide(this.W,Length,out Scalar W)){ Result=default; return false; } Result=new(X,Y,Z,W); return true; }
        public Quaternion Normalize()=>this.TryNormalize(out Quaternion Result)?Result:throw new InvalidOperationException("The quaternion cannot be normalized.");
        public Quaternion Conjugate()=>new(-this.X,-this.Y,-this.Z,this.W);
        public static Quaternion operator *(Quaternion Left,Quaternion Right)=>new(
            Left.W*Right.X+Left.X*Right.W+Left.Y*Right.Z-Left.Z*Right.Y,
            Left.W*Right.Y-Left.X*Right.Z+Left.Y*Right.W+Left.Z*Right.X,
            Left.W*Right.Z+Left.X*Right.Y-Left.Y*Right.X+Left.Z*Right.W,
            Left.W*Right.W-Left.X*Right.X-Left.Y*Right.Y-Left.Z*Right.Z);
        public bool Equals(Quaternion Other)=>this.X==Other.X&&this.Y==Other.Y&&this.Z==Other.Z&&this.W==Other.W;
        public override bool Equals(object? Object)=>Object is Quaternion Other&&this.Equals(Other);
        public override int GetHashCode()=>HashCode.Combine(this.X,this.Y,this.Z,this.W);
        public static bool operator ==(Quaternion Left,Quaternion Right)=>Left.Equals(Right);
        public static bool operator !=(Quaternion Left,Quaternion Right)=>!Left.Equals(Right);
        public System.Numerics.Quaternion ToSingle()=>new((float)this.X,(float)this.Y,(float)this.Z,(float)this.W);
    }
}

namespace CSharpGameEngine.Numerics.Logical192 {
    public readonly struct Vector3:IEquatable<Vector3> {
        public Scalar X { get; } public Scalar Y { get; } public Scalar Z { get; }
        public Vector3(Scalar X,Scalar Y,Scalar Z){ this.X=X; this.Y=Y; this.Z=Z; }
        public static Vector3 Zero=>default; public static Vector3 One=>new(Scalar.One,Scalar.One,Scalar.One);
        public static Vector3 UnitX=>new(Scalar.One,Scalar.Zero,Scalar.Zero); public static Vector3 UnitY=>new(Scalar.Zero,Scalar.One,Scalar.Zero); public static Vector3 UnitZ=>new(Scalar.Zero,Scalar.Zero,Scalar.One);
        public Scalar LengthSquared=>this.X*this.X+this.Y*this.Y+this.Z*this.Z;
        public bool TryNormalize(out Vector3 Result){ if(this.LengthSquared.IsZero||!Scalar.TrySquareRoot(this.LengthSquared,out Scalar Length)||Length.IsZero||!Scalar.TryDivide(this.X,Length,out Scalar X)||!Scalar.TryDivide(this.Y,Length,out Scalar Y)||!Scalar.TryDivide(this.Z,Length,out Scalar Z)){ Result=default; return false; } Result=new(X,Y,Z); return true; }
        public Vector3 Normalize()=>this.TryNormalize(out Vector3 Result)?Result:throw new InvalidOperationException();
        public static Scalar Dot(Vector3 Left,Vector3 Right)=>Left.X*Right.X+Left.Y*Right.Y+Left.Z*Right.Z;
        public static Vector3 Cross(Vector3 Left,Vector3 Right)=>new(Left.Y*Right.Z-Left.Z*Right.Y,Left.Z*Right.X-Left.X*Right.Z,Left.X*Right.Y-Left.Y*Right.X);
        public static Vector3 operator +(Vector3 Left,Vector3 Right)=>new(Left.X+Right.X,Left.Y+Right.Y,Left.Z+Right.Z); public static Vector3 operator -(Vector3 Left,Vector3 Right)=>new(Left.X-Right.X,Left.Y-Right.Y,Left.Z-Right.Z); public static Vector3 operator -(Vector3 Value)=>new(-Value.X,-Value.Y,-Value.Z);
        public static Vector3 operator *(Vector3 Value,Scalar Factor)=>new(Value.X*Factor,Value.Y*Factor,Value.Z*Factor); public static Vector3 operator *(Scalar Factor,Vector3 Value)=>Value*Factor; public static Vector3 operator /(Vector3 Value,Scalar Factor)=>new(Value.X/Factor,Value.Y/Factor,Value.Z/Factor);
        public bool Equals(Vector3 Other)=>this.X==Other.X&&this.Y==Other.Y&&this.Z==Other.Z; public override bool Equals(object? Object)=>Object is Vector3 Other&&this.Equals(Other); public override int GetHashCode()=>HashCode.Combine(this.X,this.Y,this.Z); public static bool operator ==(Vector3 Left,Vector3 Right)=>Left.Equals(Right); public static bool operator !=(Vector3 Left,Vector3 Right)=>!Left.Equals(Right); public System.Numerics.Vector3 ToSingle()=>new((float)this.X,(float)this.Y,(float)this.Z);
    }
    public readonly struct Quaternion:IEquatable<Quaternion> {
        public Scalar X { get; } public Scalar Y { get; } public Scalar Z { get; } public Scalar W { get; }
        public Quaternion(Scalar X,Scalar Y,Scalar Z,Scalar W){ this.X=X; this.Y=Y; this.Z=Z; this.W=W; } public static Quaternion Identity=>new(Scalar.Zero,Scalar.Zero,Scalar.Zero,Scalar.One); public Scalar LengthSquared=>this.X*this.X+this.Y*this.Y+this.Z*this.Z+this.W*this.W;
        public bool TryNormalize(out Quaternion Result){ if(this.LengthSquared.IsZero||!Scalar.TrySquareRoot(this.LengthSquared,out Scalar Length)||Length.IsZero||!Scalar.TryDivide(this.X,Length,out Scalar X)||!Scalar.TryDivide(this.Y,Length,out Scalar Y)||!Scalar.TryDivide(this.Z,Length,out Scalar Z)||!Scalar.TryDivide(this.W,Length,out Scalar W)){ Result=default; return false; } Result=new(X,Y,Z,W); return true; } public Quaternion Normalize()=>this.TryNormalize(out Quaternion Result)?Result:throw new InvalidOperationException(); public Quaternion Conjugate()=>new(-this.X,-this.Y,-this.Z,this.W);
        public static Quaternion operator *(Quaternion Left,Quaternion Right)=>new(Left.W*Right.X+Left.X*Right.W+Left.Y*Right.Z-Left.Z*Right.Y,Left.W*Right.Y-Left.X*Right.Z+Left.Y*Right.W+Left.Z*Right.X,Left.W*Right.Z+Left.X*Right.Y-Left.Y*Right.X+Left.Z*Right.W,Left.W*Right.W-Left.X*Right.X-Left.Y*Right.Y-Left.Z*Right.Z); public bool Equals(Quaternion Other)=>this.X==Other.X&&this.Y==Other.Y&&this.Z==Other.Z&&this.W==Other.W; public override bool Equals(object? Object)=>Object is Quaternion Other&&this.Equals(Other); public override int GetHashCode()=>HashCode.Combine(this.X,this.Y,this.Z,this.W); public static bool operator ==(Quaternion Left,Quaternion Right)=>Left.Equals(Right); public static bool operator !=(Quaternion Left,Quaternion Right)=>!Left.Equals(Right); public System.Numerics.Quaternion ToSingle()=>new((float)this.X,(float)this.Y,(float)this.Z,(float)this.W);
    }
}

namespace CSharpGameEngine.Numerics.Logical256 {
    public readonly struct Vector3:IEquatable<Vector3> {
        public Scalar X { get; } public Scalar Y { get; } public Scalar Z { get; }
        public Vector3(Scalar X,Scalar Y,Scalar Z){ this.X=X; this.Y=Y; this.Z=Z; }
        public static Vector3 Zero=>default; public static Vector3 One=>new(Scalar.One,Scalar.One,Scalar.One); public static Vector3 UnitX=>new(Scalar.One,Scalar.Zero,Scalar.Zero); public static Vector3 UnitY=>new(Scalar.Zero,Scalar.One,Scalar.Zero); public static Vector3 UnitZ=>new(Scalar.Zero,Scalar.Zero,Scalar.One);
        public Scalar LengthSquared=>this.X*this.X+this.Y*this.Y+this.Z*this.Z;
        public bool TryNormalize(out Vector3 Result){ if(this.LengthSquared.IsZero||!Scalar.TrySquareRoot(this.LengthSquared,out Scalar Length)||Length.IsZero||!Scalar.TryDivide(this.X,Length,out Scalar X)||!Scalar.TryDivide(this.Y,Length,out Scalar Y)||!Scalar.TryDivide(this.Z,Length,out Scalar Z)){ Result=default; return false; } Result=new(X,Y,Z); return true; } public Vector3 Normalize()=>this.TryNormalize(out Vector3 Result)?Result:throw new InvalidOperationException(); public static Scalar Dot(Vector3 Left,Vector3 Right)=>Left.X*Right.X+Left.Y*Right.Y+Left.Z*Right.Z; public static Vector3 Cross(Vector3 Left,Vector3 Right)=>new(Left.Y*Right.Z-Left.Z*Right.Y,Left.Z*Right.X-Left.X*Right.Z,Left.X*Right.Y-Left.Y*Right.X);
        public static Vector3 operator +(Vector3 Left,Vector3 Right)=>new(Left.X+Right.X,Left.Y+Right.Y,Left.Z+Right.Z); public static Vector3 operator -(Vector3 Left,Vector3 Right)=>new(Left.X-Right.X,Left.Y-Right.Y,Left.Z-Right.Z); public static Vector3 operator -(Vector3 Value)=>new(-Value.X,-Value.Y,-Value.Z); public static Vector3 operator *(Vector3 Value,Scalar Factor)=>new(Value.X*Factor,Value.Y*Factor,Value.Z*Factor); public static Vector3 operator *(Scalar Factor,Vector3 Value)=>Value*Factor; public static Vector3 operator /(Vector3 Value,Scalar Factor)=>new(Value.X/Factor,Value.Y/Factor,Value.Z/Factor);
        public bool Equals(Vector3 Other)=>this.X==Other.X&&this.Y==Other.Y&&this.Z==Other.Z; public override bool Equals(object? Object)=>Object is Vector3 Other&&this.Equals(Other); public override int GetHashCode()=>HashCode.Combine(this.X,this.Y,this.Z); public static bool operator ==(Vector3 Left,Vector3 Right)=>Left.Equals(Right); public static bool operator !=(Vector3 Left,Vector3 Right)=>!Left.Equals(Right); public System.Numerics.Vector3 ToSingle()=>new((float)this.X,(float)this.Y,(float)this.Z);
    }
    public readonly struct Quaternion:IEquatable<Quaternion> {
        public Scalar X { get; } public Scalar Y { get; } public Scalar Z { get; } public Scalar W { get; }
        public Quaternion(Scalar X,Scalar Y,Scalar Z,Scalar W){ this.X=X; this.Y=Y; this.Z=Z; this.W=W; } public static Quaternion Identity=>new(Scalar.Zero,Scalar.Zero,Scalar.Zero,Scalar.One); public Scalar LengthSquared=>this.X*this.X+this.Y*this.Y+this.Z*this.Z+this.W*this.W;
        public bool TryNormalize(out Quaternion Result){ if(this.LengthSquared.IsZero||!Scalar.TrySquareRoot(this.LengthSquared,out Scalar Length)||Length.IsZero||!Scalar.TryDivide(this.X,Length,out Scalar X)||!Scalar.TryDivide(this.Y,Length,out Scalar Y)||!Scalar.TryDivide(this.Z,Length,out Scalar Z)||!Scalar.TryDivide(this.W,Length,out Scalar W)){ Result=default; return false; } Result=new(X,Y,Z,W); return true; } public Quaternion Normalize()=>this.TryNormalize(out Quaternion Result)?Result:throw new InvalidOperationException(); public Quaternion Conjugate()=>new(-this.X,-this.Y,-this.Z,this.W);
        public static Quaternion operator *(Quaternion Left,Quaternion Right)=>new(Left.W*Right.X+Left.X*Right.W+Left.Y*Right.Z-Left.Z*Right.Y,Left.W*Right.Y-Left.X*Right.Z+Left.Y*Right.W+Left.Z*Right.X,Left.W*Right.Z+Left.X*Right.Y-Left.Y*Right.X+Left.Z*Right.W,Left.W*Right.W-Left.X*Right.X-Left.Y*Right.Y-Left.Z*Right.Z); public bool Equals(Quaternion Other)=>this.X==Other.X&&this.Y==Other.Y&&this.Z==Other.Z&&this.W==Other.W; public override bool Equals(object? Object)=>Object is Quaternion Other&&this.Equals(Other); public override int GetHashCode()=>HashCode.Combine(this.X,this.Y,this.Z,this.W); public static bool operator ==(Quaternion Left,Quaternion Right)=>Left.Equals(Right); public static bool operator !=(Quaternion Left,Quaternion Right)=>!Left.Equals(Right); public System.Numerics.Quaternion ToSingle()=>new((float)this.X,(float)this.Y,(float)this.Z,(float)this.W);
    }
}
