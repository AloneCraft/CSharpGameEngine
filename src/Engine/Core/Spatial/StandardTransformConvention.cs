using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// このマイルストーンで提供する唯一の<see cref="ITransformConvention"/>実装。Scale、Rotation、Translationの順に合成する行ベクトル規約
///（既にリポジトリ内で使われている<see cref="System.Numerics.Matrix4x4"/>の使用方法と一致。例：
/// <c>UserGameProject/Program.cs</c>の<c>Matrix4x4.CreateFromQuaternion(rotation) *
/// Matrix4x4.CreateTranslation(position)</c>）。親子の合成順序は<c>local * parentWorld</c>（子の変換後に親を適用）で、
/// 同じ行ベクトル規約に従う。有効な<see cref="ICoordinateConvention"/>プリセットからは独立している。
/// Transform ConventionとCoordinate Conventionは互いに直交する軸である（<see cref="ITransformConvention"/>を参照）。
/// </summary>
public readonly struct StandardTransformConvention:ITransformConvention {
    public static Matrix4x4 Compose(in RufVector Position,in RufQuaternion Rotation,in RufVector Scale){
        var ScaleMatrix=Matrix4x4.CreateScale(Scale.R,Scale.U,Scale.F);
        var RotationMatrix=Matrix4x4.CreateFromQuaternion(new Quaternion(Rotation.R,Rotation.U,Rotation.F,Rotation.W));
        var TranslationMatrix=Matrix4x4.CreateTranslation(Position.R,Position.U,Position.F);
        return ScaleMatrix*RotationMatrix*TranslationMatrix;
    }

    public static Matrix4x4 Combine(in Matrix4x4 ParentWorld,in Matrix4x4 Local)=>Local*ParentWorld;

    public static RufVector TransformPoint(in Matrix4x4 Transform,in RufVector Point){
        var Transformed=Vector3.Transform(new Vector3(Point.R,Point.U,Point.F),Transform);
        return new RufVector(Transformed.X,Transformed.Y,Transformed.Z);
    }

    /// <summary>線形部分（回転＋スケール）だけを適用する。平行移動は含めない。</summary>
    public static RufVector TransformDirection(in Matrix4x4 Transform,in RufVector Direction){
        var Transformed=Vector3.TransformNormal(new Vector3(Direction.R,Direction.U,Direction.F),Transform);
        return new RufVector(Transformed.X,Transformed.Y,Transformed.Z);
    }

    /// <summary>
    /// 線形部分の逆転置行列を適用し、非一様スケールによって法線が表面から傾くのを防ぐ（標準的な、グラフィックス上正しい法線変換）。<see cref="TransformDirection"/> と同じ処理経路にはしない (Docs/Specifications/Spatial-RUF.md の「コード品質」を参照。Point/Direction/Normal で同じ変換関数を共有してはならない)。
    /// </summary>
    public static RufVector TransformNormal(in Matrix4x4 Transform,in RufVector Normal){
        if(!Matrix4x4.Invert(Transform,out var Inverse)){
            throw new SpatialConfigurationException($"TransformNormal requires an invertible matrix, but {Transform} is singular.");
        }

        var InverseTranspose=Matrix4x4.Transpose(Inverse);
        var Transformed=Vector3.TransformNormal(new Vector3(Normal.R,Normal.U,Normal.F),InverseTranspose);
        if(Transformed==Vector3.Zero){
            return RufVector.Zero;
        }

        var Normalized=Vector3.Normalize(Transformed);
        return new RufVector(Normalized.X,Normalized.Y,Normalized.Z);
    }
}
