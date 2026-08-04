using CSharpGameEngine.Generation;

namespace UserGameProject.Entities;

/// <summary>
/// エンドユーザーが書く唯一のストレージ切り替え点。この<c>const</c>を書き換えるだけで、
/// <c>[GenerateEntity(EntityGenerationSettings.Kind)]</c>を付けた全Entity型の物理ストレージが
/// 一括で切り替わる(切り替えはコンパイル時のみ。生成コードにRuntime分岐は残らない)。
/// </summary>
public static class EntityGenerationSettings
{
    /// <summary>
    /// AoSとSoAを同一プロセスで比較・計測するため、両方のフラグを立てている。
    /// 実際のゲームでは片方だけを指定する:
    ///   AoSのみ: EntityStorageKind.Aos
    ///   SoAのみ: EntityStorageKind.EcsSoa
    /// </summary>
    public const EntityStorageKind Kind =
        EntityStorageKind.Aos | EntityStorageKind.EcsSoa;
}

/// <summary>
/// エンドユーザーが書く、たった1つのEntity宣言。AoS/SoAは継承やヒープ上のオブジェクトグラフとは
/// 無関係な、純粋にメモリレイアウトの選択(1エンティティの全フィールドを隣接させるか、
/// フィールドごとに列として並べるか)であることに注意。ここには物理ストレージ・Location Table・
/// Chunk・Component列・Factoryのボイラープレートを一切書かない。両方のフラグが立っているため、
/// この1宣言から
/// <c>ParticleAosStore</c>(AoS)と<c>ParticleSoaStore</c>(SoA)の両方が生成される。
/// </summary>
/// <remarks>
/// Facade(この<c>Particle</c>型自身)はAoS側にのみ生成される。partialプロパティは実装宣言を
/// 1つしか持てず、Facadeの<c>_store</c>フィールドも型を1つしか取れないという言語制約による
/// (ADR-0024)。SoA側の単体アクセスは<c>ParticleSoaStore.Get*Ref</c>で行う。
/// </remarks>
[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Particle
{
    public partial LocalTransform Transform { get; set; }

    public partial LinearVelocity Velocity { get; set; }

    public partial Durability Durability { get; set; }

    /// <summary>
    /// 手書きのドメイン挙動。ストレージ方式が変わっても、このコードは1文字も変わらない。
    /// </summary>
    public void Move(float deltaTime)
    {
        ParticleMotion.Move(ref this.TransformRef, ref this.VelocityRef, deltaTime);
    }

    public void ApplyWear(int amount)
    {
        ParticleMotion.ApplyWear(ref this.DurabilityRef, amount);
    }

    public bool IsWornOut => this.Durability.Value <= 0;
}
