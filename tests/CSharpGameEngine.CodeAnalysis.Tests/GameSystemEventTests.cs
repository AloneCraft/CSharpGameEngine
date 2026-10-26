using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0077 stage 6：イベント。システムは<c>in EventWriter&lt;T&gt;</c>で送信し、<c>in T</c>で1件ずつ受信する。</summary>
public sealed class GameSystemEventTests {
    private static readonly string Events=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestAssets","Entities","Events.cs"));

    private const string Components="""
        namespace Sample;

        public struct Position { public float X; }
        """;

    private const string Hit="""
        using CSharpGameEngine.Entities;

        namespace Sample;

        public struct Hit : IEvent { public int Amount; }
        """;

    private static GeneratorDriverRunResult Run(params string[] Sources)=>
        GameSystemGeneratorTestHelper.RunGenerator([Events,Components,Hit,.. Sources]);

    private static int PhaseOf(string Loop,string Marker){
        var At=Loop.IndexOf(Marker,StringComparison.Ordinal);
        Assert.True(At>=0,"missing "+Marker);
        var Condition=Loop.LastIndexOf("if (Phase == (global::CSharpGameEngine.Entities.SystemPhase)",At,StringComparison.Ordinal);
        Assert.True(Condition>=0);
        var Start=Condition+"if (Phase == (global::CSharpGameEngine.Entities.SystemPhase)".Length;
        return int.Parse(Loop.Substring(Start,Loop.IndexOf(')',Start)-Start)[..1]);
    }

    private const string Sender="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        [SystemPhase(SystemPhase.PhysicsSimulation)]
        internal struct Sender : ISystem
        {
            public void Update(ref Position position, in EventWriter<Hit> hits) { if (position.X > 0) hits.Send(new Hit { Amount = (int)position.X }); }
        }
        """;

    private const string Receiver="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal struct Receiver : ISystem
        {
            public int Total;
            public void Update(in Hit hit) { Total += hit.Amount; }
        }
        """;

    [Fact]
    public void AReceiverTakesThePhaseOfItsSender_AndRunsAfterIt(){
        var Result=Run(Sender,Receiver);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        // 受信側に[SystemPhase]の指定はない。送信側に続いてPhysicsSimulation（2）で実行する。
        Assert.Equal(2,PhaseOf(Loop,"Systems.Receiver.Update("));
        Assert.True(Loop.IndexOf("Systems.Sender.Update(",StringComparison.Ordinal)<Loop.IndexOf("Systems.Receiver.Update(",StringComparison.Ordinal));
        Assert.Contains("Systems.Events0 ??= new global::System.Collections.Generic.List<global::Sample.Hit>();",Loop);
        Assert.Contains("new global::CSharpGameEngine.Entities.EventWriter<global::Sample.Hit>(Systems.Events0!)",Loop);
        Assert.Contains("Systems.Receiver.Update(in hitValue)",Loop);
        Assert.Contains("internal static void ClearEvents(ref GeneratedSystemSet Systems)",Loop);
    }

    [Fact]
    public void EventsAreSentAndReceivedOneByOneWithinTheFrame_AndDroppedAtTheNext(){
        var Source="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;

            namespace Game;

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };
                    world.Create(new Position { X = 2f });
                    world.Create(new Position { X = 3f });
                    world.Create(new Position { X = 0f });
                    GeneratedSystemSet systems = default;
                    GeneratedGameLoop.RunBakeTick(ref state, ref systems);
                    if (systems.Receiver.Total != 5) return "FAIL:first " + systems.Receiver.Total;
                    GeneratedGameLoop.RunBakeTick(ref state, ref systems);
                    if (systems.Receiver.Total != 10) return "FAIL:second " + systems.Receiver.Total;
                    return "OK";
                }
            }
            """;

        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Events,Components,Hit,Sender,Receiver,Source);

        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }

    [Fact]
    public void AReceiverInAnEarlierExplicitPhaseThanItsSender_ReportsSysgen041(){
        const string Early="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [SystemPhase(SystemPhase.FixedSimulation)]
            internal struct Early : ISystem
            {
                public void Update(in Hit hit) { }
            }
            """;

        Assert.Contains(Run(Sender,Early).Diagnostics,D=>D.Id=="SYSGEN041");
    }

    [Fact]
    public void AReceiverNobodySendsTo_WarnsSysgen042(){
        var Diagnostics=Run(Receiver).Diagnostics;

        Assert.Contains(Diagnostics,D=>D.Id=="SYSGEN042"&&D.Severity==DiagnosticSeverity.Warning);
    }

    [Fact]
    public void AReceiverThatAlsoIteratesEntities_ReportsSysgen040(){
        const string Mixed="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Mixed : ISystem
            {
                public void Update(ref Position position, in Hit hit) { }
            }
            """;

        Assert.Contains(Run(Sender,Mixed).Diagnostics,D=>D.Id=="SYSGEN040");
    }

    [Fact]
    public void AnEventParameterThatIsNotInOrNotAnEvent_ReportsSysgen039(){
        const string Bad="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Bad : ISystem
            {
                public void Update(ref Position position, EventWriter<Hit> hits) { }
            }

            internal struct Plain { }

            internal struct BadWriter : ISystem
            {
                public void Update(ref Position position, in EventWriter<Plain> hits) { }
            }
            """;
        var Reports=Run(Sender,Bad).Diagnostics.Where(static D=>D.Id=="SYSGEN039"||D.Id=="SYSGEN018").ToArray();

        Assert.NotEmpty(Reports);
    }
}
