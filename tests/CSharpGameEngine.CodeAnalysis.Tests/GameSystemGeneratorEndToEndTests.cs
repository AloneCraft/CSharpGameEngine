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
        public sealed class FrameContext
        {
            public FrameContext(float deltaTime) { this.DeltaTime = deltaTime; }
            public float DeltaTime { get; }
        }
        """;

    /// <summary>
    /// ADR-0070 B1：<c>TrySetComponent</c>はシステムの書き込みと同様に、値をその場で置き換える
    ///（変更バージョンは進むがアーキタイプは維持する）。対象コンポーネントを持たないEntityは拒否する。
    /// </summary>
    [Fact]
    public void TrySetComponent_ReplacesTheValueInPlaceAndMarksItChanged(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Mover : ISystem
            {
                public bool Update(ref Position position, ref Velocity velocity, ref State state) => true;
                public bool Update(ref Health health, ref State state) => true;
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    var moving = world.Create(new Position { X = 1f }, new Velocity { X = 2f });
                    var healthy = world.Create(new Health { Value = 3 });
                    world.TryGetComponentChangeVersion(moving, EcsComponentType.Position, out var before);
                    if (!world.TrySetComponent(moving, new Position { X = 5f })) return "FAIL:set";
                    world.TryGetComponent(moving, out Position position);
                    world.TryGetComponent(moving, out Velocity velocity);
                    world.TryGetComponentChangeVersion(moving, EcsComponentType.Position, out var after);
                    if (position.X != 5f || velocity.X != 2f) return "FAIL:values " + position.X + " " + velocity.X;
                    if (!(after.Value > before.Value)) return "FAIL:version";
                    if (world.TrySetComponent(healthy, new Position { X = 9f })) return "FAIL:missing component";
                    world.Destroy(moving);
                    if (world.TrySetComponent(moving, new Position { X = 9f })) return "FAIL:dead entity";
                    return "OK";
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }

    /// <summary>
    /// ADR-0072：フレームの経過時間はStateを通じてすべてのシステムに渡る。
    /// ループの固定ティックスケジューラーも使うカウンターなので、各システムが独自にStopwatchを持つ必要はない。
    /// </summary>
    [Fact]
    public void FrameDeltaSeconds_IsTheElapsedTimeTheLoopWasGiven(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct TimeProbe : ISystem
            {
                public float Seen;
                public float Fixed;
                public bool Update(ref Position position, ref State state)
                {
                    Seen = state.FrameDeltaSeconds;
                    Fixed = state.FixedDeltaSeconds;
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
                    TimeProbe probe = new();
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref probe, ref scheduler, 1_000, 60_000);
                    if (probe.Seen != 1_000f / 60_000f) return "FAIL:first " + probe.Seen;
                    GeneratedGameLoop.RunFrame(ref state, ref probe, ref scheduler, 3_000, 60_000);
                    if (probe.Seen != 3_000f / 60_000f) return "FAIL:second " + probe.Seen;
                    if (probe.Fixed != 1f / state.FixedTickRate) return "FAIL:fixed " + probe.Fixed;
                    return "OK";
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }

    /// <summary>
    /// ADR-0072：Updateの戻り値はvoid。Entityシステムとワールドシステムのどちらも毎フレーム実行され、
    /// bool Updateも同時にコンパイルできる。
    /// </summary>
    [Fact]
    public void VoidUpdate_RunsForEntityAndWorldSystems(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct Mover : ISystem
            {
                public void Update(ref Position position, ref State state) { position.X += 1f; }
            }

            internal struct FrameCounter : IWorldSystem
            {
                public int Frames;
                public void Update(ref State state) { Frames++; }
            }

            internal struct Legacy : ISystem
            {
                public int Calls;
                public bool Update(ref Velocity velocity, ref State state) { Calls++; return true; }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };
                    var moving = world.Create(new Position());
                    world.Create(new Velocity());
                    GeneratedSystemSet systems = default;
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref systems, ref scheduler, 1_000, 60_000);
                    GeneratedGameLoop.RunFrame(ref state, ref systems, ref scheduler, 1_000, 60_000);
                    world.TryGetComponent(moving, out Position position);
                    if (position.X != 2f) return "FAIL:entity " + position.X;
                    if (systems.FrameCounter.Frames != 2) return "FAIL:world " + systems.FrameCounter.Frames;
                    if (systems.Legacy.Calls != 2) return "FAIL:bool " + systems.Legacy.Calls;
                    return "OK";
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }

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
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref counting, ref scheduler, 1_000, 60_000);

                    // {Position} は2体(単独 + {Position,Velocity}の方にも部分集合一致)にヒットする。
                    if (counting.PositionOnly != 2) return "FAIL:PositionOnly=" + counting.PositionOnly;
                    if (counting.PositionAndVelocity != 1) return "FAIL:PositionAndVelocity=" + counting.PositionAndVelocity;

                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    [Fact]
    public void EnableFilteringAndSystemWritesAdvanceGeneratedChangeQueries(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal struct MoveSystem : ISystem
            {
                public int Calls;
                public bool Update(ref Position position, ref State state)
                {
                    Calls++;
                    position.X += 1f;
                    return true;
                }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };
                    Entity entityDisabled = world.Create(new Position());
                    Entity componentDisabled = world.Create(new Position());
                    world.SetEntityEnabled(entityDisabled, false);
                    world.SetComponentEnabled(componentDisabled, EcsComponentType.Position, false);

                    MoveSystem system = new();
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref system, ref scheduler, 1_000, 60_000);
                    if (system.Calls != 0) return "FAIL:disabledCalls=" + system.Calls;

                    world.SetComponentEnabled(componentDisabled, EcsComponentType.Position, true);
                    ChangeVersion beforeWrite = world.CaptureChangeVersion();
                    GeneratedGameLoop.RunFrame(ref state, ref system, ref scheduler, 1_000, 60_000);
                    if (system.Calls != 1) return "FAIL:enabledCalls=" + system.Calls;
                    if (!world.TryGetComponent(componentDisabled, out Position position) || position.X != 1f) return "FAIL:write";

                    Span<Entity> changed = stackalloc Entity[4];
                    int count = world.CollectChanged(EcsComponentType.Position, beforeWrite, changed);
                    if (count != 1 || changed[0] != componentDisabled) return "FAIL:changed=" + count;
                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
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
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref system, ref scheduler, 1_000, 60_000);

                    if (system.SnapshotAtConsumeTime != 3) return "FAIL:SnapshotAtConsumeTime=" + system.SnapshotAtConsumeTime;

                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    /// <summary>
    /// System-major契約の回帰テスト。Systemの先行Updateメソッドは、System/Sceneのどちらが宣言した
    /// Archetypeかに関係なく一致する全Archetypeを処理し、その後に次のUpdateメソッドへ進む。
    /// Scene Childrenへ自動付与されるParentやArchetype登録順で集計結果が変わってはならない。
    /// </summary>
    [Fact]
    public void SystemMajorDispatch_ProcessesSceneChildBeforeLaterSystemMethod(){
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
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref system, ref scheduler, 1_000, 60_000);

                    // System-major dispatchでは先のUpdateメソッドを一致する全Archetypeへ適用してから
                    // 次のUpdateメソッドへ進む。Scene専用Archetypeの登録順に依存せず1件が集計済みになる。
                    if (system.SnapshotAtConsumeTime != 1) return "FAIL:SnapshotAtConsumeTime=" + system.SnapshotAtConsumeTime;

                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(
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
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref system, ref scheduler, 1_000, 60_000);

                    if (world.Archetype0.Count != 0) return "FAIL:sourceNotEmptied=" + world.Archetype0.Count;
                    if (world.Archetype1.Count != 2) return "FAIL:destinationCount=" + world.Archetype1.Count;
                    if (world.Archetype1.GetVelocityRefUnchecked(0).X != 3f) return "FAIL:velocityNotApplied";

                    // 2フレーム目は移動元が空なので何も起きない。
                    GeneratedGameLoop.RunFrame(ref state, ref system, ref scheduler, 1_000, 60_000);

                    if (world.Archetype1.Count != 2) return "FAIL:secondFrameCount=" + world.Archetype1.Count;

                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
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
                public bool Update(ref Position position, FrameContext frame, ref State state)
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
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                    GeneratedGameLoop.RunFrame(ref state, ref system, ref scheduler, 1_000, 60_000, frame);

                    if (world.Archetype0.GetPositionRefUnchecked(0).X != 0.5f) return "FAIL:X=" + world.Archetype0.GetPositionRefUnchecked(0).X;

                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
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
            // ADR-0073: 同じPhaseで順序の無いSystemは完全修飾名順なので、BigSpawnのArchetypeが先(Archetype0)になる。
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

                    if (world.Archetype0.Count != 1) return "FAIL:count=" + world.Archetype0.Count;
                    if (world.Archetype0.GetHealthRefUnchecked(0).Value != 99) return "FAIL:health";
                    if (world.Archetype0.GetEntityRefUnchecked(0) != entity) return "FAIL:entity";

                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");

        Assert.Equal("OK",Result);
    }

    [Fact]
    public void FixedTickRunFrame_UsesHostFrameBoundariesAndPlaybackControls(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;

            namespace Game;

            public struct Anchor { }

            public sealed class Log
            {
                public System.Collections.Generic.List<string> Entries = new();
            }

            [SystemPhase(SystemPhase.FrameInput)]
            internal struct InputSystem : IWorldSystem
            {
                public bool Update(Log log, ref State state)
                {
                    log.Entries.Add("input");
                    return true;
                }
            }

            [SystemPhase(SystemPhase.FixedSimulation)]
            internal struct FixedSystem : IWorldSystem
            {
                public bool Update(Log log, ref State state)
                {
                    log.Entries.Add("fixed:" + state.FixedTick);
                    return true;
                }
            }

            [SystemPhase(SystemPhase.FixedSimulation)]
            internal struct AnchorSystem : ISystem
            {
                public bool Update(ref Anchor anchor, ref State state) => true;
            }

            [SystemPhase(SystemPhase.PhysicsSimulation)]
            internal struct PhysicsSystem : IWorldSystem
            {
                public bool Update(Log log, ref State state)
                {
                    log.Entries.Add("physics:" + state.FixedTick);
                    return true;
                }
            }

            [SystemPhase(SystemPhase.PostPhysics)]
            internal struct PostPhysicsSystem : IWorldSystem
            {
                public bool Update(Log log, ref State state)
                {
                    log.Entries.Add("post:" + state.FixedTick);
                    return true;
                }
            }

            [SystemPhase(SystemPhase.Presentation)]
            internal struct PresentationSystem : IWorldSystem
            {
                public bool Update(Log log, ref State state)
                {
                    log.Entries.Add("present:" + state.FixedTick);
                    return true;
                }
            }

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    world.Create(new Anchor());
                    State state = new(world) { Enabled = true };
                    InputSystem input = new();
                    FixedSystem fixedSystem = new();
                    AnchorSystem anchor = new();
                    PhysicsSystem physics = new();
                    PostPhysicsSystem post = new();
                    PresentationSystem presentation = new();
                    var log = new Log();
                    var scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();

                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 0, 60_000, log);
                    if (log.Entries.Count != 2 || log.Entries[0] != "input" || log.Entries[1] != "present:0") return "FAIL:zero=" + string.Join(",", log.Entries);

                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 1_000, 60_000, log);
                    var Expected = new[] { "input", "fixed:0", "physics:0", "post:0", "present:1" };
                    if (!log.Entries.SequenceEqual(Expected)) return "FAIL:one=" + string.Join(",", log.Entries);
                    if (state.FixedTick != 1UL || state.InterpolationAlpha != 0d) return "FAIL:tick=" + state.FixedTick + ":alpha=" + state.InterpolationAlpha;

                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 8_000, 60_000, log);
                    if (log.Entries.Count(static entry => entry.StartsWith("fixed:")) != 8 || log.Entries.Count(static entry => entry.StartsWith("physics:")) != 8 || log.Entries.Count(static entry => entry.StartsWith("post:")) != 8) return "FAIL:eight=" + string.Join(",", log.Entries);
                    if (log.Entries.Count(static entry => entry == "input") != 1 || log.Entries.Count(static entry => entry == "present:9") != 1) return "FAIL:host=" + string.Join(",", log.Entries);

                    log.Entries.Clear();
                    var ErrorWriter = new System.IO.StringWriter();
                    var PreviousError = System.Console.Error;
                    System.Console.SetError(ErrorWriter);
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 60_000, 60_000, log);
                    System.Console.SetError(PreviousError);
                    if (!ErrorWriter.ToString().Contains("CGE-FIXED-TICK-OVERRUN", StringComparison.Ordinal) || !ErrorWriter.ToString().Contains("attemptedSteps=60", StringComparison.Ordinal) || !ErrorWriter.ToString().Contains("maximumSteps=8", StringComparison.Ordinal) || !ErrorWriter.ToString().Contains("backlog=52", StringComparison.Ordinal) || !ErrorWriter.ToString().Contains("policy=RetainBacklog", StringComparison.Ordinal)) return "FAIL:overrun=" + ErrorWriter;
                    if (state.FixedTick != 17UL) return "FAIL:overrun-tick=" + state.FixedTick;

                    state.PauseSimulation();
                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 60_000, 60_000, log);
                    if (state.FixedTick != 17UL || log.Entries.Count(static entry => entry.StartsWith("fixed:")) != 0 || log.Entries.Count(static entry => entry == "input") != 1 || log.Entries.Count(static entry => entry == "present:17") != 1) return "FAIL:pause=" + string.Join(",", log.Entries);

                    state.StepSimulation();
                    state.StepSimulation();
                    state.StepSimulation();
                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 60_000, 60_000, log);
                    if (state.FixedTick != 18UL || log.Entries.Count(static entry => entry.StartsWith("fixed:")) != 1) return "FAIL:step=" + string.Join(",", log.Entries);
                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 60_000, 60_000, log);
                    if (state.FixedTick != 19UL || log.Entries.Count(static entry => entry.StartsWith("fixed:")) != 1) return "FAIL:second-step=" + string.Join(",", log.Entries);
                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 60_000, 60_000, log);
                    if (state.FixedTick != 20UL || log.Entries.Count(static entry => entry.StartsWith("fixed:")) != 1) return "FAIL:third-step=" + string.Join(",", log.Entries);
                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 60_000, 60_000, log);
                    if (state.FixedTick != 20UL || log.Entries.Count(static entry => entry.StartsWith("fixed:")) != 0) return "FAIL:fourth-step=" + string.Join(",", log.Entries);

                    state.ResumeSimulation();
                    log.Entries.Clear();
                    GeneratedGameLoop.RunFrame(ref state, ref input, ref anchor, ref fixedSystem, ref physics, ref post, ref presentation, ref scheduler, 0, 60_000, log);
                    if (state.FixedTick != 28UL || log.Entries.Count(static entry => entry.StartsWith("fixed:")) != 8) return "FAIL:resume=" + string.Join(",", log.Entries);

                    return "OK";
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Components,Source);
        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");
        Assert.Equal("OK",Result);
    }

}

