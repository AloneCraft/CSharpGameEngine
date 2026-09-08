namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GameSystemScheduleTests {
    [Fact]
    public void ReadOnlyComponentUsesInAndDoesNotStampAChangeVersion(){
        var Result=Run(
            """
            [ScheduledSystem]
            internal struct ReadSystem : ISystem
            {
                public bool Update([ReadOnlyComponent] in Position position, ref Velocity velocity, ref State state) => true;
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        Assert.Contains("ReadSystem.Update(in State.World.Archetype0.GetPositionRefUnchecked(Index), ref State.World.Archetype0.GetVelocityRefUnchecked(Index), ref State)",Loop);
        Assert.DoesNotContain("MarkPositionChangedUnchecked",Loop);
        Assert.Contains("MarkVelocityChangedUnchecked",Loop);
    }

    [Fact]
    public void OrderedConflictProducesAStableTwoWaveSchedule(){
        var Result=Run(
            """
            [ScheduledSystem]
            internal struct ReadSystem : ISystem
            {
                public bool Update([ReadOnlyComponent] in Position position, ref State state) => true;
            }

            [ScheduledSystem, RunsAfter(typeof(ReadSystem))]
            internal struct WriteSystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Schedule=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Schedule.g.cs");
        Assert.Contains("new(0, 1, 0, 0, 0,",Schedule);
        Assert.Contains("new(1, 1, 1, 0, 1,",Schedule);
        Assert.Contains("0 => \"global::Game.ReadSystem\"",Schedule);
        Assert.Contains("1 => \"global::Game.WriteSystem\"",Schedule);
        Assert.DoesNotContain("Dictionary",Schedule);
        Assert.DoesNotContain("Reflection",Schedule);
    }

    [Fact]
    public void ReadOnlySystemsMayShareTheSameWave(){
        var Result=Run(
            """
            [ScheduledSystem]
            internal struct FirstReadSystem : ISystem
            {
                public bool Update([ReadOnlyComponent] in Position position, ref State state) => true;
            }

            [ScheduledSystem]
            internal struct SecondReadSystem : ISystem
            {
                public bool Update([ReadOnlyComponent] in Position position, ref State state) => true;
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Schedule=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Schedule.g.cs");
        Assert.Equal(2,Schedule.Split("new(",StringSplitOptions.None).Length-1);
        Assert.DoesNotContain("SYSGEN032",Schedule);
    }

    [Fact]
    public void UnorderedWriteConflictIsACompileError(){
        var Result=Run(
            """
            [ScheduledSystem]
            internal struct FirstSystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }

            [ScheduledSystem]
            internal struct SecondSystem : ISystem
            {
                public bool Update([ReadOnlyComponent] in Position position, ref State state) => true;
            }
            """);

        var Diagnostic=Assert.Single(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="SYSGEN032");
        Assert.Contains("global::Game.FirstSystem",Diagnostic.GetMessage());
        Assert.Contains("global::Sample.Position",Diagnostic.GetMessage());
        Assert.DoesNotContain(Result.Results[0].GeneratedSources,static Source=>Source.HintName=="GameSystem.Schedule.g.cs");
    }

    [Fact]
    public void MissingScheduledDependencyIsACompileError(){
        var Result=Run(
            """
            internal struct UnscheduledSystem : ISystem
            {
                public bool Update(ref Velocity velocity, ref State state) => true;
            }

            [ScheduledSystem, RunsAfter(typeof(UnscheduledSystem))]
            internal struct ScheduledSystemA : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }
            """);

        Assert.Single(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="SYSGEN030");
    }

    [Fact]
    public void DependencyCycleIsACompileError(){
        var Result=Run(
            """
            [ScheduledSystem, RunsAfter(typeof(SecondSystem))]
            internal struct FirstSystem : ISystem
            {
                public bool Update(ref Position position, ref State state) => true;
            }

            [ScheduledSystem, RunsAfter(typeof(FirstSystem))]
            internal struct SecondSystem : ISystem
            {
                public bool Update(ref Velocity velocity, ref State state) => true;
            }
            """);

        var Diagnostic=Assert.Single(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="SYSGEN031");
        Assert.Contains("global::Game.FirstSystem",Diagnostic.GetMessage());
        Assert.Contains("global::Game.SecondSystem",Diagnostic.GetMessage());
    }

    [Fact]
    public void ReadOnlyAttributeRequiresAnInStructParameter(){
        var Result=Run(
            """
            internal struct BrokenSystem : ISystem
            {
                public bool Update([ReadOnlyComponent] ref Position position, ref State state) => true;
            }
            """);

        Assert.Single(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="SYSGEN029");
    }

    private static Microsoft.CodeAnalysis.GeneratorDriverRunResult Run(string Systems)=>
        GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            """+Environment.NewLine+Systems);
}
