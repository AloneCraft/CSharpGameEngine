namespace CSharpGameEngine.Numerics;
public readonly record struct LogicalVector3(LogicalScalar R,LogicalScalar U,LogicalScalar F) {
    public static LogicalVector3 Zero=>default;
    public static LogicalVector3 One=>new(LogicalScalar.One,LogicalScalar.One,LogicalScalar.One);
    public static LogicalVector3 UnitR=>new(LogicalScalar.One,LogicalScalar.Zero,LogicalScalar.Zero);
    public static LogicalVector3 UnitU=>new(LogicalScalar.Zero,LogicalScalar.One,LogicalScalar.Zero);
    public static LogicalVector3 UnitF=>new(LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.One);
    public LogicalScalar LengthSquared=>Dot(this,this);
    /// <summary>Unit vector; <c>V.TryNormalize(out V)</c> is allowed because the receiver is read before <paramref name="Result"/> is written.</summary>
    public bool TryNormalize(out LogicalVector3 Result){
        if(LogicalScalar.TrySquareRoot(this.LengthSquared,out var Length)&&!Length.IsZero
            &&LogicalScalar.TryDivide(this.R,Length,out var R)
            &&LogicalScalar.TryDivide(this.U,Length,out var U)
            &&LogicalScalar.TryDivide(this.F,Length,out var F)){
            Result=new LogicalVector3(R,U,F);
            return true;
        }
        Result=default;
        return false;
    }
    public LogicalVector3 Normalize()=>this.TryNormalize(out var Result)?Result:throw new InvalidOperationException("The vector cannot be normalized.");
    public bool TryToSingle(out System.Numerics.Vector3 Result){
        Result=default;
        if(!this.R.TryToSingle(out var R)||!this.U.TryToSingle(out var U)||!this.F.TryToSingle(out var F)){ return false; }
        Result=new System.Numerics.Vector3(R,U,F);
        return true;
    }
    public static LogicalScalar Dot(LogicalVector3 Left,LogicalVector3 Right)=>Left.R*Right.R+Left.U*Right.U+Left.F*Right.F;
    public static LogicalVector3 Cross(LogicalVector3 Left,LogicalVector3 Right)=>new(Left.U*Right.F-Left.F*Right.U,Left.F*Right.R-Left.R*Right.F,Left.R*Right.U-Left.U*Right.R);
    public static LogicalVector3 operator +(LogicalVector3 Left,LogicalVector3 Right)=>new(Left.R+Right.R,Left.U+Right.U,Left.F+Right.F);
    public static LogicalVector3 operator -(LogicalVector3 Left,LogicalVector3 Right)=>new(Left.R-Right.R,Left.U-Right.U,Left.F-Right.F);
    public static LogicalVector3 operator -(LogicalVector3 Value)=>new(-Value.R,-Value.U,-Value.F);
    public static LogicalVector3 operator *(LogicalVector3 Value,LogicalScalar Factor)=>new(Value.R*Factor,Value.U*Factor,Value.F*Factor);
    public static LogicalVector3 operator *(LogicalScalar Factor,LogicalVector3 Value)=>Value*Factor;
    public static LogicalVector3 operator /(LogicalVector3 Value,LogicalScalar Factor)=>new(Value.R/Factor,Value.U/Factor,Value.F/Factor);
}
public readonly record struct LogicalQuaternion(LogicalScalar R,LogicalScalar U,LogicalScalar F,LogicalScalar W) {
    public LogicalQuaternion(LogicalVector3 EulerRuf):this(FromEulerRuf(EulerRuf),0){ }
    private LogicalQuaternion(LogicalQuaternion Value,byte _):this(Value.R,Value.U,Value.F,Value.W){ }
    public static LogicalQuaternion Identity=>new(LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.One);
    public LogicalScalar LengthSquared=>this.R*this.R+this.U*this.U+this.F*this.F+this.W*this.W;
    /// <summary>Unit quaternion; <c>Q.TryNormalize(out Q)</c> is allowed because the receiver is read before <paramref name="Result"/> is written.</summary>
    public bool TryNormalize(out LogicalQuaternion Result){
        if(LogicalScalar.TrySquareRoot(this.LengthSquared,out var Length)&&!Length.IsZero
            &&LogicalScalar.TryDivide(this.R,Length,out var R)
            &&LogicalScalar.TryDivide(this.U,Length,out var U)
            &&LogicalScalar.TryDivide(this.F,Length,out var F)
            &&LogicalScalar.TryDivide(this.W,Length,out var W)){
            Result=new LogicalQuaternion(R,U,F,W);
            return true;
        }
        Result=default;
        return false;
    }
    public LogicalQuaternion Conjugate()=>new(-this.R,-this.U,-this.F,this.W);
    public bool TryToSingle(out System.Numerics.Quaternion Result){
        Result=default;
        if(!this.R.TryToSingle(out var R)||!this.U.TryToSingle(out var U)
            ||!this.F.TryToSingle(out var F)||!this.W.TryToSingle(out var W)){ return false; }
        Result=new System.Numerics.Quaternion(R,U,F,W);
        return float.IsFinite(R)&&float.IsFinite(U)&&float.IsFinite(F)&&float.IsFinite(W);
    }
    public static bool TryFromEulerRuf(LogicalVector3 Euler,out LogicalQuaternion Result){
        Result=default;
        if(!TryCreateAxisAngle(LogicalVector3.UnitU,Euler.U,out var Yaw)
            ||!TryCreateAxisAngle(LogicalVector3.UnitR,Euler.R,out var Pitch)
            ||!TryCreateAxisAngle(LogicalVector3.UnitF,Euler.F,out var Roll)
            ||!TryMultiply(Yaw,Pitch,out var YawPitch)
            ||!TryMultiply(YawPitch,Roll,out var Candidate)){ return false; }
        return Candidate.TryNormalize(out Result);
    }
    public static LogicalQuaternion FromEulerRuf(LogicalVector3 Euler)=>
        TryFromEulerRuf(Euler,out var Result)?Result:throw new OverflowException("Euler rotation cannot be represented as a normalized LogicalQuaternion.");
    public static bool TryMultiply(LogicalQuaternion Left,LogicalQuaternion Right,out LogicalQuaternion Result){
        Result=default;
        if(!LogicalScalar.TryMultiply(Left.W,Right.R,out var Wr)||!LogicalScalar.TryMultiply(Left.R,Right.W,out var Rw)
            ||!LogicalScalar.TryMultiply(Left.U,Right.F,out var Uf)||!LogicalScalar.TryMultiply(Left.F,Right.U,out var Fu)
            ||!TryNegate(Fu,out Fu)||!TrySum(Wr,Rw,Uf,Fu,out var R)
            ||!LogicalScalar.TryMultiply(Left.W,Right.U,out var Wu)||!LogicalScalar.TryMultiply(Left.R,Right.F,out var Rf)
            ||!LogicalScalar.TryMultiply(Left.U,Right.W,out var Uw)||!LogicalScalar.TryMultiply(Left.F,Right.R,out var Fr)
            ||!TryNegate(Rf,out Rf)||!TrySum(Wu,Rf,Uw,Fr,out var U)
            ||!LogicalScalar.TryMultiply(Left.W,Right.F,out var Wf)||!LogicalScalar.TryMultiply(Left.R,Right.U,out var Ru)
            ||!LogicalScalar.TryMultiply(Left.U,Right.R,out var Ur)||!LogicalScalar.TryMultiply(Left.F,Right.W,out var Fw)
            ||!TryNegate(Ur,out Ur)||!TrySum(Wf,Ru,Ur,Fw,out var F)
            ||!LogicalScalar.TryMultiply(Left.W,Right.W,out var Ww)||!LogicalScalar.TryMultiply(Left.R,Right.R,out var Rr)
            ||!LogicalScalar.TryMultiply(Left.U,Right.U,out var Uu)||!LogicalScalar.TryMultiply(Left.F,Right.F,out var Ff)
            ||!TryNegate(Rr,out Rr)||!TryNegate(Uu,out Uu)||!TryNegate(Ff,out Ff)||!TrySum(Ww,Rr,Uu,Ff,out var W)){ return false; }
        Result=new LogicalQuaternion(R,U,F,W);
        return true;
    }
    public static bool TryCreateAxisAngle(LogicalVector3 Axis,LogicalScalar Angle,out LogicalQuaternion Result){
        Result=default;
        if(!Axis.TryNormalize(out var Unit)
            ||!LogicalScalar.TryMultiply(Angle,LogicalScalar.FromRaw((Int128)1<<63),out var HalfAngle)
            ||!LogicalMath.TrySinCos(HalfAngle,out var Sin,out var Cos)
            ||!LogicalScalar.TryMultiply(Unit.R,Sin,out var R)
            ||!LogicalScalar.TryMultiply(Unit.U,Sin,out var U)
            ||!LogicalScalar.TryMultiply(Unit.F,Sin,out var F)){ return false; }
        return new LogicalQuaternion(R,U,F,Cos).TryNormalize(out Result);
    }
    public bool TryRotate(LogicalVector3 Value,out LogicalVector3 Result){
        Result=default;
        var Point=new LogicalQuaternion(Value.R,Value.U,Value.F,LogicalScalar.Zero);
        if(!TryMultiply(this,Point,out var Rotated)||!TryMultiply(Rotated,this.Conjugate(),out Rotated)){ return false; }
        Result=new LogicalVector3(Rotated.R,Rotated.U,Rotated.F);
        return true;
    }
    private static bool TrySum(LogicalScalar First,LogicalScalar Second,LogicalScalar Third,LogicalScalar Fourth,out LogicalScalar Result){
        Result=default;
        return LogicalScalar.TryAdd(First,Second,out var FirstPair)&&LogicalScalar.TryAdd(Third,Fourth,out var SecondPair)&&LogicalScalar.TryAdd(FirstPair,SecondPair,out Result);
    }
    private static bool TryNegate(LogicalScalar Value,out LogicalScalar Result)=>LogicalScalar.TrySubtract(LogicalScalar.Zero,Value,out Result);
}
