using System.Numerics;

namespace UserGameProject.Entities;

// 注意: この名前空間は UserGameProject の子であり、親には Scene1.cs が宣言する別物の
// UserGameProject.Entity / UserGameProject.World が存在する。親名前空間のメンバーは
// compilation-unit levelのusingエイリアスより先に解決されるため、ECSの Entity/World は
// 必ず global:: 付きの完全修飾で書く。

/// <summary>
/// Particleのバッチ処理(System)。生成される<c>Particle*ChunkView</c>は実装詳細のため
/// <c>internal</c>だが、同一アセンブリのここからは使える。テストやベンチマークなど
/// 他アセンブリからはこの<c>public</c>なメソッド経由でバッチ経路を駆動する。
///
/// AoSとSoAで異なるのはメモリの舐め方(行配列 vs 列Span)だけで、各行に適用する計算は
/// どちらも同じ<see cref="ParticleMotion.Move"/>を呼ぶ。これによりベンチマークが
/// 「レイアウトの差」だけを測っていることを担保する。
/// </summary>
public static class ParticleSystems
{
    /// <summary>
    /// AoSバッチ: 1行=1エンティティの全コンポーネントが隣接した密配列を直接走査する。
    /// </summary>
    public static void MoveAllAos(ParticleAosStore store, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!store.TryGetChunk(out ParticleAosChunkView view))
        {
            return;
        }

        Span<ParticleAosGeneratedData> rows = view.Rows;

        for (int row = 0; row < rows.Length; row++)
        {
            ref ParticleAosGeneratedData current = ref rows[row];
            ParticleMotion.Move(ref current.Transform, ref current.Velocity, deltaTime);
        }
    }

    /// <summary>
    /// SoAバッチ: コンポーネント型ごとに独立した連続列(Span)を直接走査する。
    /// このループではエンティティごとのLocation Table参照もFacade構築も発生しない。
    /// </summary>
    public static void MoveAllSoa(ParticleSoaStore store, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!store.TryGetChunk(out ParticleSoaChunkView view))
        {
            return;
        }

        Span<LocalTransform> transforms = view.Transform;
        Span<LinearVelocity> velocities = view.Velocity;

        for (int row = 0; row < transforms.Length; row++)
        {
            ParticleMotion.Move(ref transforms[row], ref velocities[row], deltaTime);
        }
    }

    /// <summary>
    /// AoSバッチ(1コンポーネントのみ更新)。SoAとの差が最も出る形を測るための経路で、
    /// AoSでは使わないコンポーネントも同じキャッシュラインに載る。
    /// </summary>
    public static void ApplyWearAllAos(ParticleAosStore store, int amount)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!store.TryGetChunk(out ParticleAosChunkView view))
        {
            return;
        }

        Span<ParticleAosGeneratedData> rows = view.Rows;

        for (int row = 0; row < rows.Length; row++)
        {
            ParticleMotion.ApplyWear(ref rows[row].Durability, amount);
        }
    }

    /// <summary>
    /// SoAバッチ(1コンポーネントのみ更新)。Durability列だけを連続に舐める。
    /// </summary>
    public static void ApplyWearAllSoa(ParticleSoaStore store, int amount)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!store.TryGetChunk(out ParticleSoaChunkView view))
        {
            return;
        }

        Span<Durability> durabilities = view.Durability;

        for (int row = 0; row < durabilities.Length; row++)
        {
            ParticleMotion.ApplyWear(ref durabilities[row], amount);
        }
    }

    /// <summary>
    /// AoSバッチの結果を集計する読み取り専用パス(ベンチマークで最適化により
    /// ループごと消されるのを防ぐためにも使う)。
    /// </summary>
    public static Vector3 SumPositionsAos(ParticleAosStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!store.TryGetChunk(out ParticleAosChunkView view))
        {
            return Vector3.Zero;
        }

        Span<ParticleAosGeneratedData> rows = view.Rows;
        Vector3 sum = Vector3.Zero;

        for (int row = 0; row < rows.Length; row++)
        {
            sum += rows[row].Transform.Position;
        }

        return sum;
    }

    /// <summary>
    /// SoAバッチの結果を集計する読み取り専用パス。
    /// </summary>
    public static Vector3 SumPositionsSoa(ParticleSoaStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!store.TryGetChunk(out ParticleSoaChunkView view))
        {
            return Vector3.Zero;
        }

        Span<LocalTransform> transforms = view.Transform;
        Vector3 sum = Vector3.Zero;

        for (int row = 0; row < transforms.Length; row++)
        {
            sum += transforms[row].Position;
        }

        return sum;
    }

    /// <summary>
    /// SoA側はFacadeを持たないため、単体アクセスの読み取りはこのヘルパ経由で行う
    /// (AoS側は<c>particle.Transform</c>で同じことができる)。
    /// </summary>
    public static LocalTransform GetTransformSoa(ParticleSoaStore store, global::CSharpGameEngine.Ecs.Entity entity)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.GetTransformRef(entity);
    }

    /// <summary>
    /// SoA側の単体書き込み。Facadeの<c>particle.Move(dt)</c>と等価な処理を、
    /// Store経由の<c>ref</c>アクセサで行う(比較の対等性を保つため)。
    /// </summary>
    public static void MoveSingleSoa(ParticleSoaStore store, global::CSharpGameEngine.Ecs.Entity entity, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(store);
        ParticleMotion.Move(ref store.GetTransformRef(entity), ref store.GetVelocityRef(entity), deltaTime);
    }
}
