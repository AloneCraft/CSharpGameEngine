using System.Numerics;
namespace CSharpGameEngine.Spatial.Abstractions;
/// <summary>行ベクトル規約での基底変換。単位変換は別のインポート処理で行う。</summary>
public static class ConventionConversion<TSource,TTarget> where TSource:struct,ICoordinateConvention where TTarget:struct,ICoordinateConvention {
    public static Matrix4x4 Basis{get;}=BuildBasis();
    public static bool IsMirror{get;}=Basis.GetDeterminant()<0f;
    public static Vector3 Vector(Vector3 Value)=>Vector3.TransformNormal(Value,Basis);
    public static Matrix4x4 Matrix(Matrix4x4 Value)=>Matrix4x4.Transpose(Basis)*Value*Basis;
    public static Quaternion Rotation(Quaternion Value){
        var Axis=Vector(new(Value.X,Value.Y,Value.Z))*(IsMirror?-1f:1f);
        return new(Axis,Value.W);
    }
    public static Vector3 Scale(Vector3 Value){
        var B=Basis;
        return new(Value.X*MathF.Abs(B.M11)+Value.Y*MathF.Abs(B.M21)+Value.Z*MathF.Abs(B.M31),
            Value.X*MathF.Abs(B.M12)+Value.Y*MathF.Abs(B.M22)+Value.Z*MathF.Abs(B.M32),
            Value.X*MathF.Abs(B.M13)+Value.Y*MathF.Abs(B.M23)+Value.Z*MathF.Abs(B.M33));
    }
    public static Vector4 Tangent(Vector4 Value)=>new(Vector(new(Value.X,Value.Y,Value.Z)),IsMirror?-Value.W:Value.W);
    private static Matrix4x4 BuildBasis(){
        CoordinateBasisValidator.Validate<TSource>();
        CoordinateBasisValidator.Validate<TTarget>();
        var X=Convert(new XyzVector(1f,0f,0f));
        var Y=Convert(new XyzVector(0f,1f,0f));
        var Z=Convert(new XyzVector(0f,0f,1f));
        var Result=new Matrix4x4(X.X,X.Y,X.Z,0f,Y.X,Y.Y,Y.Z,0f,Z.X,Z.Y,Z.Z,0f,0f,0f,0f,1f);
        if((Result.GetDeterminant()<0f)!=(TSource.Handedness!=TTarget.Handedness)){
            throw new SpatialConfigurationException("Declared handedness disagrees with the coordinate basis.");
        }
        if(!IsSignedAxis(X)||!IsSignedAxis(Y)||!IsSignedAxis(Z)){
            throw new SpatialConfigurationException("Import conventions require a signed axis permutation.");
        }
        return Result;
    }
    private static XyzVector Convert(XyzVector Value){
        var Semantic=TSource.FromXyz(Value);
        return TTarget.ToXyz(Semantic);
    }
    private static bool IsSignedAxis(XyzVector Value)=>MathF.Abs(Value.X)+MathF.Abs(Value.Y)+MathF.Abs(Value.Z)==1f
        &&(MathF.Abs(Value.X)==1f||MathF.Abs(Value.Y)==1f||MathF.Abs(Value.Z)==1f);
}
