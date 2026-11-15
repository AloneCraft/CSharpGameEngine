namespace CSharpGameEngine.Numerics;
public readonly record struct LogicalAffineTransform(
    LogicalVector3 RAxis,LogicalVector3 UAxis,LogicalVector3 FAxis,LogicalVector3 Translation) {
    public static LogicalAffineTransform Identity=>new(LogicalVector3.UnitR,LogicalVector3.UnitU,LogicalVector3.UnitF,LogicalVector3.Zero);
    public static bool TryCreateTrs(LogicalVector3 Position,LogicalQuaternion Rotation,LogicalVector3 Scale,out LogicalAffineTransform Result){
        Result=default;
        if(Scale.R.IsZero||Scale.U.IsZero||Scale.F.IsZero||!Rotation.TryNormalize(out var NormalizedRotation)
            ||!TryDoubleProduct(NormalizedRotation.R,NormalizedRotation.R,out var TwoRR)
            ||!TryDoubleProduct(NormalizedRotation.U,NormalizedRotation.U,out var TwoUU)
            ||!TryDoubleProduct(NormalizedRotation.F,NormalizedRotation.F,out var TwoFF)
            ||!TryDoubleProduct(NormalizedRotation.R,NormalizedRotation.U,out var TwoRU)
            ||!TryDoubleProduct(NormalizedRotation.R,NormalizedRotation.F,out var TwoRF)
            ||!TryDoubleProduct(NormalizedRotation.U,NormalizedRotation.F,out var TwoUF)
            ||!TryDoubleProduct(NormalizedRotation.R,NormalizedRotation.W,out var TwoRW)
            ||!TryDoubleProduct(NormalizedRotation.U,NormalizedRotation.W,out var TwoUW)
            ||!TryDoubleProduct(NormalizedRotation.F,NormalizedRotation.W,out var TwoFW)
            ||!LogicalScalar.TrySubtract(LogicalScalar.One,TwoUU,out var M11)
            ||!LogicalScalar.TrySubtract(M11,TwoFF,out M11)
            ||!LogicalScalar.TryAdd(TwoRU,TwoFW,out var M12)
            ||!LogicalScalar.TrySubtract(TwoRF,TwoUW,out var M13)
            ||!LogicalScalar.TrySubtract(TwoRU,TwoFW,out var M21)
            ||!LogicalScalar.TrySubtract(LogicalScalar.One,TwoRR,out var M22)
            ||!LogicalScalar.TrySubtract(M22,TwoFF,out M22)
            ||!LogicalScalar.TryAdd(TwoUF,TwoRW,out var M23)
            ||!LogicalScalar.TryAdd(TwoRF,TwoUW,out var M31)
            ||!LogicalScalar.TrySubtract(TwoUF,TwoRW,out var M32)
            ||!LogicalScalar.TrySubtract(LogicalScalar.One,TwoRR,out var M33)
            ||!LogicalScalar.TrySubtract(M33,TwoUU,out M33)
            ||!TryScale(new LogicalVector3(M11,M12,M13),Scale.R,out var RAxis)
            ||!TryScale(new LogicalVector3(M21,M22,M23),Scale.U,out var UAxis)
            ||!TryScale(new LogicalVector3(M31,M32,M33),Scale.F,out var FAxis)){ return false; }
        Result=new LogicalAffineTransform(RAxis,UAxis,FAxis,Position);
        return true;
    }
    public bool TryTransformPoint(LogicalVector3 Point,out LogicalVector3 Result){
        Result=default;
        return this.TryTransformVector(Point,out var Vector)&&TryAdd(Vector,this.Translation,out Result);
    }
    public bool TryTransformVector(LogicalVector3 Vector,out LogicalVector3 Result){
        Result=default;
        return TryLinearCombination(Vector.R,this.RAxis,Vector.U,this.UAxis,Vector.F,this.FAxis,out Result);
    }
    public static bool TryMultiply(LogicalAffineTransform Left,LogicalAffineTransform Right,out LogicalAffineTransform Result){
        Result=default;
        if(!Right.TryTransformVector(Left.RAxis,out var RAxis)
            ||!Right.TryTransformVector(Left.UAxis,out var UAxis)
            ||!Right.TryTransformVector(Left.FAxis,out var FAxis)
            ||!Right.TryTransformPoint(Left.Translation,out var Translation)){ return false; }
        Result=new LogicalAffineTransform(RAxis,UAxis,FAxis,Translation);
        return true;
    }
    private static bool TryLinearCombination(LogicalScalar R,LogicalVector3 RAxis,LogicalScalar U,LogicalVector3 UAxis,LogicalScalar F,LogicalVector3 FAxis,out LogicalVector3 Result){
        Result=default;
        return TryDot(R,RAxis.R,U,UAxis.R,F,FAxis.R,out var ResultR)
            &&TryDot(R,RAxis.U,U,UAxis.U,F,FAxis.U,out var ResultU)
            &&TryDot(R,RAxis.F,U,UAxis.F,F,FAxis.F,out var ResultF)
            &&((Result=new LogicalVector3(ResultR,ResultU,ResultF))==Result);
    }
    private static bool TryDot(LogicalScalar A,LogicalScalar X,LogicalScalar B,LogicalScalar Y,LogicalScalar C,LogicalScalar Z,out LogicalScalar Result){
        Result=default;
        return LogicalScalar.TryMultiply(A,X,out var AX)
            &&LogicalScalar.TryMultiply(B,Y,out var BY)
            &&LogicalScalar.TryMultiply(C,Z,out var CZ)
            &&LogicalScalar.TryAdd(AX,BY,out var Sum)
            &&LogicalScalar.TryAdd(Sum,CZ,out Result);
    }
    private static bool TryDoubleProduct(LogicalScalar Left,LogicalScalar Right,out LogicalScalar Result){
        Result=default;
        return LogicalScalar.TryMultiply(Left,Right,out var Product)&&LogicalScalar.TryAdd(Product,Product,out Result);
    }
    private static bool TryScale(LogicalVector3 Vector,LogicalScalar Scale,out LogicalVector3 Result){
        Result=default;
        return LogicalScalar.TryMultiply(Vector.R,Scale,out var R)
            &&LogicalScalar.TryMultiply(Vector.U,Scale,out var U)
            &&LogicalScalar.TryMultiply(Vector.F,Scale,out var F)
            &&((Result=new LogicalVector3(R,U,F))==Result);
    }
    private static bool TryAdd(LogicalVector3 Left,LogicalVector3 Right,out LogicalVector3 Result){
        Result=default;
        return LogicalScalar.TryAdd(Left.R,Right.R,out var R)
            &&LogicalScalar.TryAdd(Left.U,Right.U,out var U)
            &&LogicalScalar.TryAdd(Left.F,Right.F,out var F)
            &&((Result=new LogicalVector3(R,U,F))==Result);
    }
}
