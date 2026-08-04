using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// GenerateEntityGeneratorがAoSバックエンドで実際に出力したコードを、コンパイル・Emit・ロードして
/// リフレクションで実行し、Create/参照アクセサ/ユーザー定義メソッド/Destroy/stale handle拒否/
/// swap-back/ChunkViewバッチアクセスがすべて実際のRuntimeで機能することを証明する。
/// </summary>
public sealed class GenerateEntityAosRuntimeTests
{
    private static readonly Lazy<Assembly> CompiledAssembly = new(() => GenerateEntityGeneratorTestHelper.CompileAndLoad(
        GenerateEntityGeneratorTestHelper.CommonComponents
        + GenerateEntityGeneratorTestHelper.EntityGenerationSettings("ObjectOrientedAos")
        + CharacterAndTestEntrySource));

    [Fact]
    public void Create_GetSet_RefAccessor_UserMethods_AllWorkTogether()
    {
        Assert.Equal("OK", RunTestEntryMethod("RunCreateAndAccessors"));
    }

    [Fact]
    public void CopiedHandle_PointsToSameEntity()
    {
        Assert.Equal("OK", RunTestEntryMethod("RunCopiedHandleSharesSameEntity"));
    }

    [Fact]
    public void Destroy_ThenStaleHandleAccess_IsRejected()
    {
        Assert.Equal("OK", RunTestEntryMethod("RunDestroyAndStaleHandleRejection"));
    }

    [Fact]
    public void Destroy_MiddleEntity_SwapBackOnlyUpdatesMovedEntitysLocation()
    {
        Assert.Equal("OK", RunTestEntryMethod("RunSwapBackUpdatesOnlyMovedEntity"));
    }

    [Fact]
    public void ChunkView_DrivesBatchLoopWithoutPerRowStoreCalls()
    {
        Assert.Equal("OK", RunTestEntryMethod("RunBatchChunkViewLoop"));
    }

    [Fact]
    public void RowArray_StaysDenseAfterCreateDestroyChurn()
    {
        Assert.Equal("OK", RunTestEntryMethod("RunDenseArrayAfterChurn"));
    }

    private static object? RunTestEntryMethod(string methodName)
    {
        Type type = CompiledAssembly.Value.GetType("Sample.TestEntry") ?? throw new InvalidOperationException("Sample.TestEntry not found.");
        MethodInfo method = type.GetMethod(methodName) ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return method.Invoke(null, null);
    }

    private const string CharacterAndTestEntrySource = """


        [GenerateEntity(EntityGenerationSettings.Kind)]
        public partial struct Character
        {
            public partial LocalTransform Transform { get; set; }
            public partial Velocity Velocity { get; set; }
            public partial Health Health { get; set; }

            public void Move(float deltaTime)
            {
                ref LocalTransform transform = ref this.TransformRef;
                ref Velocity velocity = ref this.VelocityRef;
                transform.X += velocity.X * deltaTime;
            }

            public static Character Spawn(CharacterStore store, float velocityX, int initialHealth)
            {
                return store.Create(new CharacterCreateInfo
                {
                    Transform = new LocalTransform { X = 0, Y = 0, Z = 0 },
                    Velocity = new Velocity { X = velocityX, Y = 0, Z = 0 },
                    Health = new Health { Value = initialHealth }
                });
            }

            public static void ApplyDamage(Character target, int damage)
            {
                ref Health health = ref target.HealthRef;
                health.Value = System.Math.Max(0, health.Value - System.Math.Max(0, damage));
            }

            public static bool IsDead(Character target)
            {
                return target.Health.Value <= 0;
            }
        }

        public static class TestEntry
        {
            public static string RunCreateAndAccessors()
            {
                CharacterStore store = new CharacterStore();
                Character character = Character.Spawn(store, velocityX: 2f, initialHealth: 100);

                if (character.Transform.X != 0) return "FAIL:InitialTransform";
                if (character.Velocity.X != 2f) return "FAIL:InitialVelocity";
                if (character.Health.Value != 100) return "FAIL:InitialHealth";

                character.Move(1f);
                if (character.Transform.X != 2f) return "FAIL:MoveDidNotApply";

                Character.ApplyDamage(character, 30);
                if (character.Health.Value != 70) return "FAIL:ApplyDamage";

                if (Character.IsDead(character)) return "FAIL:ShouldNotBeDead";

                Character.ApplyDamage(character, 1000);
                if (!Character.IsDead(character)) return "FAIL:ShouldBeDead";

                return "OK";
            }

            public static string RunCopiedHandleSharesSameEntity()
            {
                CharacterStore store = new CharacterStore();
                Character original = Character.Spawn(store, velocityX: 0, initialHealth: 50);
                Character copy = original;

                copy.HealthRef.Value = 10;
                if (copy.Health.Value != 10) return "FAIL:CopySetDidNotApply";

                if (original.Entity != copy.Entity) return "FAIL:DifferentEntity";
                if (original.Health.Value != copy.Health.Value) return "FAIL:ValuesDiverged";

                return "OK";
            }

            public static string RunDestroyAndStaleHandleRejection()
            {
                CharacterStore store = new CharacterStore();
                Character character = Character.Spawn(store, velocityX: 0, initialHealth: 1);

                if (!character.IsAlive) return "FAIL:ShouldBeAliveAfterCreate";

                character.Destroy();
                if (character.IsAlive) return "FAIL:ShouldNotBeAliveAfterDestroy";

                bool threw = false;
                try
                {
                    _ = character.Health;
                }
                catch (System.InvalidOperationException)
                {
                    threw = true;
                }

                if (!threw) return "FAIL:StaleHandleShouldThrow";

                // 破棄されたIdが再利用されても、古いハンドルは新しいGenerationと一致しない。
                Character reused = Character.Spawn(store, velocityX: 0, initialHealth: 1);
                if (character.Entity == reused.Entity) return "FAIL:GenerationShouldHaveChanged";

                return "OK";
            }

            public static string RunSwapBackUpdatesOnlyMovedEntity()
            {
                CharacterStore store = new CharacterStore();
                Character first = Character.Spawn(store, velocityX: 0, initialHealth: 1);
                Character middle = Character.Spawn(store, velocityX: 0, initialHealth: 2);
                Character last = Character.Spawn(store, velocityX: 0, initialHealth: 3);

                middle.Destroy();

                if (!first.IsAlive) return "FAIL:FirstShouldStayAlive";
                if (middle.IsAlive) return "FAIL:MiddleShouldBeDead";
                if (!last.IsAlive) return "FAIL:LastShouldStayAlive";
                if (first.Health.Value != 1) return "FAIL:FirstValueCorrupted";
                if (last.Health.Value != 3) return "FAIL:LastValueCorrupted";

                return "OK";
            }

            public static string RunBatchChunkViewLoop()
            {
                CharacterStore store = new CharacterStore();
                Character a = Character.Spawn(store, velocityX: 1f, initialHealth: 1);
                Character b = Character.Spawn(store, velocityX: 2f, initialHealth: 1);

                if (!store.TryGetChunk(out CharacterChunkView view)) return "FAIL:NoChunk";
                if (view.Count != 2) return "FAIL:WrongCount";

                for (int row = 0; row < view.Count; row++)
                {
                    view.Rows[row].Transform.X += view.Rows[row].Velocity.X * 1f;
                }

                if (a.Transform.X != 1f) return "FAIL:AWasNotUpdated";
                if (b.Transform.X != 2f) return "FAIL:BWasNotUpdated";

                return "OK";
            }

            public static string RunDenseArrayAfterChurn()
            {
                CharacterStore store = new CharacterStore();
                Character first = Character.Spawn(store, velocityX: 0, initialHealth: 1);
                Character second = Character.Spawn(store, velocityX: 0, initialHealth: 2);
                Character third = Character.Spawn(store, velocityX: 0, initialHealth: 3);

                first.Destroy();

                if (!store.TryGetChunk(out CharacterChunkView view)) return "FAIL:NoChunk";
                if (view.Count != 2) return "FAIL:NotDense";

                bool sawSecond = false;
                bool sawThird = false;

                for (int row = 0; row < view.Count; row++)
                {
                    if (view.Entities[row] == second.Entity) sawSecond = true;
                    if (view.Entities[row] == third.Entity) sawThird = true;
                }

                if (!sawSecond || !sawThird) return "FAIL:MissingSurvivor";

                return "OK";
            }
        }
        """;
}
