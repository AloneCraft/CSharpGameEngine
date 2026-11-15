using System.Numerics;

namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Scale/Rotation/Translation の合成、親/子の合成、Point/Direction/Normal への適用を定義する。意図的に <see cref="ICoordinateConvention"/> とは別の契約としている (Docs/Specifications/Spatial-RUF.md の「Transformとの分離」を参照)。RUF の R/U/F が物理 +X/+Y/+Z のどれに対応するかは、行ベクトルと列ベクトルの違い、行列の乗算順序、親のワールド行列とローカル行列をどう合成するかとは別の問題である。ここの <see cref="Matrix4x4"/> は 16 個の float を格納するだけであり、行/列ベクトルの実際の規約と乗算順序は型自体ではなく、各実装のメソッド本体だけで決まる。
/// </summary>
public interface ITransformConvention {
    static abstract Matrix4x4 Compose(in RufVector Position,in RufQuaternion Rotation,in RufVector Scale);

    static abstract Matrix4x4 Combine(in Matrix4x4 ParentWorld,in Matrix4x4 Local);

    static abstract RufVector TransformPoint(in Matrix4x4 Transform,in RufVector Point);

    static abstract RufVector TransformDirection(in Matrix4x4 Transform,in RufVector Direction);

    static abstract RufVector TransformNormal(in Matrix4x4 Transform,in RufVector Normal);
}
