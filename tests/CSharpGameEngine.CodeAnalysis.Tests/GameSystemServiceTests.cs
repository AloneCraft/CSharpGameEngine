using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0077 stage 5：EngineHostのプロパティを型とする参照型パラメーターはサービスとなり、ループが渡す。</summary>
public sealed class GameSystemServiceTests {
    private const string Host="""
        namespace CSharpGameEngine.Runtime.Windows;

        public sealed class Speaker { }
        public sealed class Other { }

        public sealed class EngineHost
        {
            public Speaker Sound => new();
        }
        """;

    private static GeneratorDriverRunResult Run(string System)=>
        GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CommonComponents,Host,System);

    [Fact]
    public void ASystemTakesAServiceByItsType_AndTheLoopPassesTheHostProperty(){
        var Result=Run("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            using Sample;

            namespace Game;

            internal struct Beep : ISystem
            {
                public void Update(ref Position position, Speaker speaker) { }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("Beep.Update(ref State.World.Archetype0.GetPositionRefUnchecked(Index), EngineHost.Sound)",Loop);
        // どのシステムもEngineHostを指定していなくても、このサービスの生成にはホストが必要。
        Assert.Contains("global::CSharpGameEngine.Runtime.Windows.EngineHost EngineHost",Loop);
    }

    [Fact]
    public void ATypeThatIsNotAHostProperty_StaysAContext(){
        var Result=Run("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            using Sample;

            namespace Game;

            internal struct Plain : ISystem
            {
                public void Update(ref Position position, Other other) { }
            }
            """);

        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("Plain.Update(ref State.World.Archetype0.GetPositionRefUnchecked(Index), Other)",Loop);
    }

    [Fact]
    public void TwoParametersOfTheSameServiceType_ReportADuplicate(){
        var Result=Run("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            using Sample;

            namespace Game;

            internal struct Twice : ISystem
            {
                public void Update(ref Position position, Speaker first, Speaker second) { }
            }
            """);

        Assert.Contains(Result.Diagnostics,D=>D.Id=="SYSGEN019");
    }
}
