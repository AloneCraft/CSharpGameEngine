using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// GameSystemGeneratorが出力したコード(Executor+GeneratedGameLoop)を実際にコンパイル・Emit・
/// ロードし、実World/Entityを使って実行する。「引数からQueryとRead/Writeを推論する」「SoAループを
/// 生成する」「構造変更はCommand Buffer経由でループ末尾に安全に適用される」という新仕様の
/// 中核契約が、Generatorが実際に出力するコードで機能することを証明する最重要テスト。
/// </summary>
public sealed class GameSystemGeneratorEndToEndTests
{
    [Fact]
    public void Move_ProcessesEntitiesAcrossMultipleArchetypes_UsingSoaSpans()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class MovementSystems
            {
                [Update]
                public static void Move(ref Position position, in Velocity velocity)
                {
                    position.X += velocity.X;
                    position.Y += velocity.Y;
                    position.Z += velocity.Z;
                }
            }

            public static class TestEntry
            {
                public static string RunScenario()
                {
                    World world = new World();

                    EntityArchetype posVel = world.CreateArchetype<Position, Velocity>();
                    EntityArchetype posVelHealth = world.CreateArchetype<Position, Velocity, Health>();

                    Entity a = world.CreateEntity(posVel);
                    world.GetRef<Velocity>(a) = new Velocity { X = 1, Y = 0, Z = 0 };

                    Entity b = world.CreateEntity(posVelHealth);
                    world.GetRef<Velocity>(b) = new Velocity { X = 0, Y = 2, Z = 0 };
                    world.GetRef<Health>(b) = new Health { Value = 50 };

                    CSharpGameEngine.Generated.GeneratedGameLoop.Update(world);

                    if (world.GetRef<Position>(a).X != 1) return "FAIL:A.X";
                    if (world.GetRef<Position>(b).Y != 2) return "FAIL:B.Y";
                    if (world.GetRef<Health>(b).Value != 50) return "FAIL:B.HealthUntouched";

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(source);
        object? result = GameSystemGeneratorTestHelper.Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }

    [Fact]
    public void Without_ExcludesEntitiesThatHaveTheExcludedComponent()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class ActiveOnlySystems
            {
                [Update]
                [Without(typeof(DisabledTag))]
                public static void Move(ref Position position, in Velocity velocity)
                {
                    position.X += velocity.X;
                }
            }

            public static class TestEntry
            {
                public static string RunScenario()
                {
                    World world = new World();

                    EntityArchetype active = world.CreateArchetype<Position, Velocity>();
                    EntityArchetype disabled = world.CreateArchetype<Position, Velocity, DisabledTag>();

                    Entity a = world.CreateEntity(active);
                    world.GetRef<Velocity>(a) = new Velocity { X = 5 };

                    Entity b = world.CreateEntity(disabled);
                    world.GetRef<Velocity>(b) = new Velocity { X = 5 };

                    CSharpGameEngine.Generated.GeneratedGameLoop.Update(world);

                    if (world.GetRef<Position>(a).X != 5) return "FAIL:ActiveEntityNotMoved";
                    if (world.GetRef<Position>(b).X != 0) return "FAIL:DisabledEntityWasMoved";

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(source);
        object? result = GameSystemGeneratorTestHelper.Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }

    [Fact]
    public void Context_And_CurrentEntity_AreForwardedToEachExecuteCall()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            public struct FrameContext
            {
                public float DeltaTime;
            }

            public sealed class Log
            {
                public System.Collections.Generic.List<int> VisitedEntityIds = new();
            }

            [GameSystem]
            public static class MovementSystems
            {
                [Update]
                public static void Move(ref Position position, in Velocity velocity, [Context] in FrameContext frame, [Context] Log log, [CurrentEntity] Entity self)
                {
                    position.X += velocity.X * frame.DeltaTime;
                    log.VisitedEntityIds.Add(self.Id);
                }
            }

            public static class TestEntry
            {
                public static string RunScenario()
                {
                    World world = new World();
                    EntityArchetype posVel = world.CreateArchetype<Position, Velocity>();

                    Entity a = world.CreateEntity(posVel);
                    world.GetRef<Velocity>(a) = new Velocity { X = 10 };

                    FrameContext frame = new FrameContext { DeltaTime = 0.5f };
                    Log log = new Log();

                    CSharpGameEngine.Generated.GeneratedGameLoop.Update(world, frame, log);

                    if (world.GetRef<Position>(a).X != 5) return "FAIL:Position";
                    if (log.VisitedEntityIds.Count != 1 || log.VisitedEntityIds[0] != a.Id) return "FAIL:CurrentEntity";

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(source);
        object? result = GameSystemGeneratorTestHelper.Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }

    [Fact]
    public void Commands_StructuralChangesApplyOnlyAfterPlaybackAtEndOfGeneratedLoop()
    {
        string source = GameSystemGeneratorTestHelper.CommonComponents + """


            [GameSystem]
            public static class HealthSystems
            {
                [Update]
                public static void DisableDeadEntity(in Health health, [CurrentEntity] Entity self, [Commands] EntityCommandBuffer commands)
                {
                    if (health.Value <= 0)
                    {
                        commands.Add<DisabledTag>(self);
                    }
                }
            }

            public static class TestEntry
            {
                public static string RunScenario()
                {
                    World world = new World();
                    EntityArchetype withHealth = world.CreateArchetype<Health>();

                    Entity alive = world.CreateEntity(withHealth);
                    world.GetRef<Health>(alive) = new Health { Value = 10 };

                    Entity dead = world.CreateEntity(withHealth);
                    world.GetRef<Health>(dead) = new Health { Value = 0 };

                    if (world.Has<DisabledTag>(alive) || world.Has<DisabledTag>(dead)) return "FAIL:PreconditionAlreadyDisabled";

                    CSharpGameEngine.Generated.GeneratedGameLoop.Update(world);

                    if (world.Has<DisabledTag>(alive)) return "FAIL:AliveWasDisabled";
                    if (!world.Has<DisabledTag>(dead)) return "FAIL:DeadWasNotDisabled";

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(source);
        object? result = GameSystemGeneratorTestHelper.Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }
}
