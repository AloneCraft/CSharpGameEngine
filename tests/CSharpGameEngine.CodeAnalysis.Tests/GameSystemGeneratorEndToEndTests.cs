using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// GameSystemGeneratorが出力したコード(Archetype/World/GeneratedGameLoop/WorldSnapshot)を実際に
/// コンパイル・Emit・ロードして実行する。「Archetypeは閉じた集合としてコンパイル時に確定する」
/// 「部分集合一致で複数System・複数オーバーロードが発火する」「構造変更はフレーム末尾にまとめて
/// 適用される」という設計の中核契約を、生成された実コードで証明する最重要テスト。
/// </summary>
public sealed class GameSystemGeneratorEndToEndTests
{
    private const string Components = """
        namespace Sample;

        public struct Position { public float X; }
        public struct Velocity { public float X; }
        public struct Health { public int Value; }
        public struct MovableTag { }
        public readonly struct FrameContext
        {
            public FrameContext(float deltaTime) { this.DeltaTime = deltaTime; }
            public float DeltaTime { get; }
        }
        """;

    [Fact]
    public void SubsetDispatch_FiresEveryMatchingOverloadOncePerEntity()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct CountingSystem : ISystem
            {
                public int PositionOnly;
                public int PositionAndVelocity;

                public bool Update(ref Position position, ref State state)
                {
                    this.PositionOnly++;
                    return true;
                }

                public bool Update(ref Position position, ref Velocity velocity, ref State state)
                {
                    this.PositionAndVelocity++;
                    return true;
                }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };

                    world.Create(new Position());
                    world.Create(new Position(), new Velocity());

                    CountingSystem counting = new();
                    GeneratedGameLoop.RunFrame(ref state, ref counting);

                    // {Position} は2体(単独 + {Position,Velocity}の方にも部分集合一致)にヒットする。
                    if (counting.PositionOnly != 2) return "FAIL:PositionOnly=" + counting.PositionOnly;
                    if (counting.PositionAndVelocity != 1) return "FAIL:PositionAndVelocity=" + counting.PositionAndVelocity;

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(Components, Source);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }

    [Fact]
    public void MoveArchetype_AppliesAtEndOfFrame_AndSwapBackKeepsIndicesValid()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct PromoteSystem : ISystem
            {
                public bool Update(Entity entity, ref Position position, ref State state)
                {
                    state.World.MoveArchetype(entity, position, new Velocity { X = 3f }, ref state);
                    return true;
                }

                public bool Update(ref Position position, ref Velocity velocity, ref State state) => true;
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };

                    world.Create(new Position { X = 1f });
                    world.Create(new Position { X = 2f });

                    PromoteSystem system = new();
                    GeneratedGameLoop.RunFrame(ref state, ref system);

                    if (world.Archetype0.Count != 0) return "FAIL:sourceNotEmptied=" + world.Archetype0.Count;
                    if (world.Archetype1.Count != 2) return "FAIL:destinationCount=" + world.Archetype1.Count;
                    if (world.Archetype1.GetVelocityRefUnchecked(0).X != 3f) return "FAIL:velocityNotApplied";

                    // 2フレーム目は移動元が空なので何も起きない。
                    GeneratedGameLoop.RunFrame(ref state, ref system);

                    if (world.Archetype1.Count != 2) return "FAIL:secondFrameCount=" + world.Archetype1.Count;

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(Components, Source);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }

    [Fact]
    public void ContextParameters_ReachTheSystemBody()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct MoveSystem : ISystem
            {
                public bool Update(ref Position position, in FrameContext frame, ref State state)
                {
                    position.X += frame.DeltaTime;
                    return true;
                }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };

                    world.Create(new Position { X = 0f });

                    MoveSystem system = new();
                    FrameContext frame = new(0.5f);
                    GeneratedGameLoop.RunFrame(ref state, ref system, in frame);

                    if (world.Archetype0.GetPositionRefUnchecked(0).X != 0.5f) return "FAIL:X=" + world.Archetype0.GetPositionRefUnchecked(0).X;

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(Components, Source);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }

    [Fact]
    public void SpawnDeclaredArchetype_IsCreatableThroughWorldCreate()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct OnlySystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }

            // Healthを読むSystemが1つも無いため、この宣言が無いと{Position,Health}のArchetypeは生成されない。
            internal struct BigSpawn : ISystem
            {
                public void Spawn(ref Position position, ref Health health) { }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();

                    Entity entity = world.Create(new Position { X = 4f }, new Health { Value = 99 });

                    if (world.Archetype1.Count != 1) return "FAIL:count=" + world.Archetype1.Count;
                    if (world.Archetype1.GetHealthRefUnchecked(0).Value != 99) return "FAIL:health";
                    if (world.Archetype1.GetEntityRefUnchecked(0) != entity) return "FAIL:entity";

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(Components, Source);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }

    [Fact]
    public void WorldSnapshot_CapturesLiveEntitiesForTheOutOfProcessBridge()
    {
        string Source = """
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct OnlySystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    world.Create(new Position { X = 1f });
                    world.Create(new Position { X = 2f });

                    ArchetypeSnapshotEntry[] entries = WorldSnapshot.Capture(world);

                    if (entries.Length != 2) return "FAIL:length=" + entries.Length;
                    if (entries[0].EntityId != 0) return "FAIL:entityId";
                    if (entries[0].HasParent) return "FAIL:unexpectedParent";
                    if (entries[0].ComponentValues.Length != 1) return "FAIL:componentCount";
                    if (((Position)entries[1].ComponentValues[0]).X != 2f) return "FAIL:componentValue";

                    return "OK";
                }
            }
            """;

        Assembly assembly = GameSystemGeneratorTestHelper.CompileAndLoad(Components, Source);
        object? Result = GameSystemGeneratorTestHelper.Invoke(assembly, "Game.TestEntry", "RunScenario");

        Assert.Equal("OK", Result);
    }
}

