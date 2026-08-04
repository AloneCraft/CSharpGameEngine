namespace UserGameProject.Entities;

/// <summary>
/// Particleのドメインロジック本体。ストレージ方式(AoS/SoA)に一切依存せず、
/// コンポーネントへの<c>ref</c>だけを受け取る純粋な静的メソッドとして1回だけ記述する。
/// これにより、Facade経由の単体更新パスとChunkView経由のバッチ更新パスが
/// 「同じ計算を実行している」ことがコード上で保証され、ベンチマークの比較が公平になる。
/// 既存の<see cref="CharacterMotion"/>(PlayerController.cs)と同じ設計方針。
/// </summary>
public static class ParticleMotion
{
    public static void Move(ref LocalTransform transform, ref LinearVelocity velocity, float deltaTime)
    {
        transform.Position += velocity.Value * deltaTime;
    }

    public static void ApplyWear(ref Durability durability, int amount)
    {
        durability.Value = Math.Max(0, durability.Value - Math.Max(0, amount));
    }
}
