using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;

namespace SoaSystemsDemo;

/// <summary>
/// Query一致・SoAループ・[Context]/[CurrentEntity]/[Commands]・構造変更・Archetype動的生成・
/// Queryキャッシュの差分更新を1本のシナリオで同時に検証し、決定的な結果文字列を返す。
/// </summary>
public static class DemoScenario
{
    private const float DeltaTime = 1f / 60f;
    private const float Epsilon = 0.0001f;

    public static string Run()
    {
        World world = new World();
        EntityArchetype movable = world.CreateArchetype<LocalTransform, Velocity, Health, MovableTag>();

        Entity a = world.CreateEntity(movable);
        world.GetRef<LocalTransform>(a) = new LocalTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };
        world.GetRef<Velocity>(a) = new Velocity { Value = new Vector3(1f, 0f, 0f) };
        world.GetRef<Health>(a) = new Health { Value = 100f };

        // Healthが負のまま生まれるエンティティ。frame1のRegenerateでも回復しきらず、
        // DisableDeadEntityがCommand Bufferで無効化タグへの構造変更を積む。
        Entity b = world.CreateEntity(movable);
        world.GetRef<LocalTransform>(b) = new LocalTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };
        world.GetRef<Velocity>(b) = new Velocity { Value = new Vector3(1f, 0f, 0f) };
        world.GetRef<Health>(b) = new Health { Value = -5f };

        FrameContext frame1 = new FrameContext(DeltaTime, 1);
        GeneratedGameLoop.Update(world, frame1);

        if (!world.Has<MovableTag>(a) || world.Has<DisabledTag>(a))
        {
            return "FAIL:A should remain movable after frame1";
        }

        if (!NearlyEqual(world.GetRef<LocalTransform>(a).Position.X, DeltaTime))
        {
            return "FAIL:A.Position.X after frame1";
        }

        if (!NearlyEqual(world.GetRef<Health>(a).Value, 100f + (2f * DeltaTime)))
        {
            return "FAIL:A.Health after frame1";
        }

        if (world.Has<MovableTag>(b) || !world.Has<DisabledTag>(b))
        {
            return "FAIL:B should be disabled after frame1 (structural change via EntityCommandBuffer)";
        }

        // Moveはframe1中、Bがまだ無効化される前に一度だけ実行されている(構造変更はPlaybackで
        // ループ末尾に安全にまとめて適用されるため)。
        if (!NearlyEqual(world.GetRef<LocalTransform>(b).Position.X, DeltaTime))
        {
            return "FAIL:B.Position.X after frame1";
        }

        float healthOfBAfterFrame1 = -5f + (2f * DeltaTime);

        if (!NearlyEqual(world.GetRef<Health>(b).Value, healthOfBAfterFrame1))
        {
            return "FAIL:B.Health after frame1";
        }

        // frame1とframe2の間に、Move/RegenerateのQueryがまだ一度も見ていない新規Archetypeを
        // 生成する。両Queryとも既にframe1で1回Update済み(静的にキャッシュされている)なので、
        // これはQueryキャッシュが「新しいArchetypeが生成されたときだけ」正しく差分更新される
        // ことの検証になる。
        EntityArchetype boosted = world.CreateArchetype<LocalTransform, Velocity, Health, MovableTag, BoostTag>();
        Entity d = world.CreateEntity(boosted);
        world.GetRef<LocalTransform>(d) = new LocalTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };
        world.GetRef<Velocity>(d) = new Velocity { Value = new Vector3(2f, 0f, 0f) };
        world.GetRef<Health>(d) = new Health { Value = 10f };

        FrameContext frame2 = new FrameContext(DeltaTime, 2);
        GeneratedGameLoop.Update(world, frame2);

        if (!NearlyEqual(world.GetRef<LocalTransform>(a).Position.X, 2f * DeltaTime))
        {
            return "FAIL:A.Position.X after frame2";
        }

        if (!NearlyEqual(world.GetRef<Health>(a).Value, 100f + (4f * DeltaTime)))
        {
            return "FAIL:A.Health after frame2";
        }

        // BはMovableTagを失ったのでMoveのQueryにはもう一致せず、位置はframe1のまま凍結される。
        if (!NearlyEqual(world.GetRef<LocalTransform>(b).Position.X, DeltaTime))
        {
            return "FAIL:B.Position.X should stay frozen after losing MovableTag";
        }

        // RegenerateはMovableTagを要求しない(Healthのみ)ので、無効化後もHealthは増え続ける。
        if (!NearlyEqual(world.GetRef<Health>(b).Value, healthOfBAfterFrame1 + (2f * DeltaTime)))
        {
            return "FAIL:B.Health should keep regenerating while disabled";
        }

        // Dはframe1とframe2の間に生成された新規Archetypeであり、frame1の時点では存在すら
        // していなかった。それでもframe2でMove/Regenerateの対象になっていれば、Queryの
        // 差分スキャンが新規Archetypeを正しく検出したことになる。
        if (!NearlyEqual(world.GetRef<LocalTransform>(d).Position.X, 2f * DeltaTime))
        {
            return "FAIL:D.Position.X after frame2 (newly created archetype was not picked up by the cached Query)";
        }

        if (!NearlyEqual(world.GetRef<Health>(d).Value, 10f + (2f * DeltaTime)))
        {
            return "FAIL:D.Health after frame2";
        }

        return "OK";
    }

    private static bool NearlyEqual(float actual, float expected)
    {
        return MathF.Abs(actual - expected) < Epsilon;
    }
}
