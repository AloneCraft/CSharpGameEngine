using System.Numerics;

namespace UserGameProject.Entities;

// エンドユーザーが自分で書くコンポーネント。GenerateEntityGeneratorが認識できるのは
// 非nullableなunmanaged構造体のみ(ENTGEN010)。
//
// 名前は意図的に UserGameProject 直下の Position/Velocity/Health とは重複させていない。
// UserGameProject.Entities は UserGameProject の子名前空間なので親の型も参照範囲に入るため、
// 同名にすると読み手にとって紛らわしくなる(コンパイルは通るがシャドウイングになる)。

public struct LocalTransform
{
    public Vector3 Position;
}

public struct LinearVelocity
{
    public Vector3 Value;
}

public struct Durability
{
    public int Value;
}
