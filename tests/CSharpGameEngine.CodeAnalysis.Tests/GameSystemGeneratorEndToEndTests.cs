using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// GameSystemGeneratorが出力したコード(Archetype/World/GeneratedGameLoop)を実際に
/// コンパイル・Emit・ロードして実行する。「Archetypeは閉じた集合としてコンパイル時に確定する」
/// 「部分集合一致で複数System・複数オーバーロードが発火する」「構造変更はフレーム末尾にまとめて
/// 適用される」という設計の中核契約を、生成された実コードで証明する最重要テスト。
/// </summary>
public sealed class GameSystemGeneratorEndToEndTests {
    private const string Components="""
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
    public void SubsetDispatch_FiresEveryMatchingOverloadOncePerEntity(){
        var Source="""
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

        Assembly Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    /// <summary>
    /// ADR-0040 Phase 6のLevelLoadアキュムレータ(RuntimeSystem.cs)が依拠する核心契約: 同一struct内で
    /// 先に宣言したUpdateオーバーロードのArchetypeは、後に宣言した(互いに部分集合関係にない)
    /// オーバーロードのArchetypeより1フレーム内で必ず先に処理される。private instance fieldでの
    /// 状態共有(SubsetDispatch_FiresEveryMatchingOverloadOncePerEntityで実証済みの一般原理)と
    /// 組み合わせて、「先に全部集めてから後で使う」というパターンを成立させる根拠。
    /// </summary>
    [Fact]
    public void DeclarationOrderedOverloads_AccumulatorIsFullyPopulatedBeforeLaterOverloadRuns(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct AccumulatingSystem : ISystem
            {
                // 先に宣言: {Position} を集める。
                public int Collected;

                public bool Update(ref Position position, ref State state)
                {
                    Collected++;
                    return true;
                }

                // 後に宣言: {Position} とは互いに素な {Velocity} 単体を担当し、集計済みの値を読む。
                public int SnapshotAtConsumeTime;

                public bool Update(ref Velocity velocity, ref State state)
                {
                    SnapshotAtConsumeTime = Collected;
                    return true;
                }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };

                    world.Create(new Position { X = 1f });
                    world.Create(new Position { X = 2f });
                    world.Create(new Position { X = 3f });
                    world.Create(new Velocity { X = 0f });

                    AccumulatingSystem system = new();
                    GeneratedGameLoop.RunFrame(ref state, ref system);

                    if (system.SnapshotAtConsumeTime != 3) return "FAIL:SnapshotAtConsumeTime=" + system.SnapshotAtConsumeTime;

                    return "OK";
                }
            }
            """;

        Assembly Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    /// <summary>
    /// ADR-0040 Phase 6実装中に実際にハマった落とし穴の固定回帰テスト: Scene DSLのChildrenは自動で
    /// Parentが付与され、Systemがオーバーロードで宣言したのと同じComponent部分集合であっても
    /// (部分集合一致でUpdateは発火するが)実体のArchetypeは別物になる。Scene由来の追加Archetypeは
    /// 実Systemが宣言した全Archetypeより<b>後に</b>登録される(ADR-0038)ため、上のテストと同じ
    /// 「先に宣言したオーバーロードで集めてから後で読む」パターンは、対象がScene DSLの子ノード
    /// (Parent付き)だと機能しない — 親と同じ階層(Rootまたは兄弟)に直接置く必要がある。
    /// PrimitiveShowcaseScene.csがChildrenではなく独立したRootを使っているのはこのため。
    /// </summary>
    [Fact]
    public void SceneChildNodeArchetype_IsProcessedAfterAllSystemDeclaredArchetypes(){
        var ComponentsAndSystem="""
            namespace Sample;
            public struct Foo { }
            public struct Trigger { }
            """;
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct AccumulatingSystem : ISystem
            {
                // 先に宣言: {Position, Foo} を集める(LevelLoadの{Transform,PrimitiveMesh}に相当)。
                public int Collected;

                public bool Update(ref Position position, ref Foo foo, ref State state)
                {
                    Collected++;
                    return true;
                }

                // 後に宣言: {Trigger} 単体を担当し、集計済みの値を読む(LevelLoadの{AppHost,Level}に相当)。
                public int SnapshotAtConsumeTime;

                public bool Update(ref Trigger trigger, ref State state)
                {
                    SnapshotAtConsumeTime = Collected;
                    return true;
                }
            }

            public partial class MainScene : IScene
            {
                public Node Root => new() {
                    Components = [new Trigger()],
                    // ここがChildrenなので子ノードには自動でParentが付与される -> {Position,Foo,Parent}。
                    Children = [new Node { Components = [new Position(), new Foo()] }],
                };
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    world.InstantiateScene<MainScene>();
                    State state = new(world) { Enabled = true };

                    AccumulatingSystem system = new();
                    GeneratedGameLoop.RunFrame(ref state, ref system);

                    // {Position,Foo,Parent} はScene専用Archetypeとして{Trigger}より後ろに登録されるため、
                    // Triggerオーバーロードが読む時点ではまだ処理されておらず0のまま(意図通りの挙動)。
                    if (system.SnapshotAtConsumeTime != 0) return "FAIL:SnapshotAtConsumeTime=" + system.SnapshotAtConsumeTime;

                    return "OK";
                }
            }
            """;

        Assembly Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(
            [.. GameSystemGeneratorTestHelper.SceneDslSources,Components,ComponentsAndSystem,Source]);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    [Fact]
    public void MoveArchetype_AppliesAtEndOfFrame_AndSwapBackKeepsIndicesValid(){
        var Source="""
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

        Assembly Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    [Fact]
    public void ContextParameters_ReachTheSystemBody(){
        var Source="""
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

        Assembly Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    [Fact]
    public void SpawnDeclaredArchetype_IsCreatableThroughWorldCreate(){
        var Source="""
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

        Assembly Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

}

