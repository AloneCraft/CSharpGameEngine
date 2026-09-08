using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GameSystemGeneratorEmissionTests {
    private const string TwoSystems="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal struct FirstSystem : ISystem
        {
            public bool Update(ref Position position, ref State state) => true;
        }

        internal struct SecondSystem : ISystem
        {
            public bool Update(ref Position position, ref Velocity velocity, ref State state) => true;
        }
        """;

    [Fact]
    public void ArchetypesAreNamedByDiscoveryOrder_NotByComponentIndexConcatenation(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        Assert.Empty(Result.Diagnostics);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");

        // ADR-0033: Archetype名は出現順の連番。Component集合を連結した名前(Archetype0001…)は、
        // 読めない上にComponentを1つ足すだけで全部の名前が変わってしまうため採用していない。
        // None = -1 は破棄されたEntityの墓石(ADR-0040)。必ず先頭に来る。
        Assert.Contains("public enum Archetype {\n    None = -1,\n    Archetype0,\n    Archetype1,\n}",Text);
        Assert.Contains("public sealed class Archetype0 : IArchetype",Text);
        Assert.Contains("public sealed class Archetype1 : IArchetype",Text);
    }

    [Fact]
    public void CreateFactory_ReturnsTheCreatedEntity(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");

        Assert.Contains("public static Entity Create(ref World World,",Text);
        Assert.Contains("        return Entity;",Text);
    }

    [Fact]
    public void WorldGetsCreateOverloads_SoCallersNeverNameAnArchetype(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.World.g.cs");

        Assert.Contains("public Entity Create(global::Sample.Position Position)",Text);
        Assert.Contains("public Entity Create(global::Sample.Position Position, global::Sample.Velocity Velocity)",Text);
        Assert.Contains("public Entity CreateDeferred(global::Sample.Position Position)",Text);
        Assert.Contains("public bool MoveArchetype(Entity Entity, global::Sample.Position Position, ref State State)",Text);
        Assert.Contains("public void PlaybackStructuralChanges(ref State State)",Text);
        Assert.Contains("public void Destroy(Entity Entity)",Text);
        Assert.Contains("this.Commands.Move(MoveIndex);",Text);
        Assert.Contains("StructuralCommand.Kind != EntityCommandKind.Move",Text);
        Assert.DoesNotContain("this.PlaybackSceneCommands();",Text);
    }

    [Fact]
    public void ArchetypeMove_PreservesSceneOwnership(){
        // タプルごと代入するとSceneSlotが消え、Archetype移動しただけでEntityがSceneから外れてしまう
        // (ADR-0040)。必ずフィールド単位で更新すること。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.World.g.cs");

        Assert.Contains("Updated.Archetype = Archetype.Archetype0;",Text);
        Assert.Contains("Updated.Index = DestinationIndex;",Text);
        Assert.DoesNotContain("= (Command.Entity, Archetype.Archetype0, DestinationIndex);",Text);
    }

    [Fact]
    public void DispatchOrderFollowsSourceDeclarationOrder(){
        // FirstSystem/SecondSystem は宣言順どおりに呼ばれなければならない(ADR-0033決定事項1:
        // Component引数が全てrefになった結果、Write→Readの自動順序決定は原理的に不可能)。
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        var First=Text.IndexOf("FirstSystem.Update(",StringComparison.Ordinal);
        var Second=Text.IndexOf("SecondSystem.Update(",StringComparison.Ordinal);

        Assert.True(First>=0&&Second>=0,"both systems must be dispatched");
        Assert.True(First<Second,"declaration order must be preserved");
        Assert.Contains("State.World.BeginSystemExecution();",Text);
        Assert.Contains("State.World.EndSystemExecution();",Text);
        Assert.Contains("} finally {",Text);
    }

    [Fact]
    public void SubsetDispatch_RunsASmallerOverloadAgainstASupersetArchetype(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        // Archetype1 は {Position, Velocity}。{Position} だけを要求する FirstSystem もここに一致する。
        var Archetype1Loop=Text.IndexOf("State.World.Archetype1.Count",StringComparison.Ordinal);
        var Archetype1Body=Text[Archetype1Loop..];

        Assert.Contains("FirstSystem.Update(ref State.World.Archetype1.GetPositionRefUnchecked(Index), ref State)",Archetype1Body);
        Assert.Contains("SecondSystem.Update(ref State.World.Archetype1.GetPositionRefUnchecked(Index), ref State.World.Archetype1.GetVelocityRefUnchecked(Index), ref State)",Archetype1Body);
    }

    [Fact]
    public void SpawnMethod_RegistersAnArchetypeButIsNeverDispatched(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;
                                                              using Sample;

                                                              namespace Game;

                                                              internal struct OnlySystem : ISystem
                                                              {
                                                                  public bool Update(ref Position position, ref State state) => true;
                                                              }

                                                              internal struct HealthSpawn : ISystem
                                                              {
                                                                  public void Spawn(ref Position position, ref Health health) { }
                                                              }
                                                              """);

        Assert.Empty(Result.Diagnostics);

        var Types=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        // Spawnが宣言した {Position, Health} のArchetypeとCreateは生成される。
        Assert.Contains("global::Sample.Health Health",Types);

        // しかしSpawn自体はディスパッチされない。
        Assert.DoesNotContain("HealthSpawn",Loop);
        Assert.DoesNotContain(".Spawn(",Loop);
    }

    [Fact]
    public void ContextParameters_AreThreadedThroughRunFrame(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;
                                                              using Sample;

                                                              namespace Game;

                                                              internal struct ContextSystem : ISystem
                                                              {
                                                                  public bool Update(ref Position position, in FrameContext frame, Log log, ref State state) => true;
                                                              }
                                                              """);

        Assert.Empty(Result.Diagnostics);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        // 構造体Contextは`in`、参照型Contextは値渡し(属性は一切使わず、ref-kindだけで判別される)。
        Assert.Contains("in global::Sample.FrameContext FrameContext",Text);
        Assert.Contains("global::Sample.Log Log",Text);
        Assert.Contains("ContextSystem.Update(ref State.World.Archetype0.GetPositionRefUnchecked(Index), in FrameContext, Log, ref State)",Text);
    }

    [Fact]
    public void PromotedSample_Compiles(){
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(
                                                                  GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        Assert.NotNull(Assembly.GetType("CSharpGameEngine.Generated.GeneratedGameLoop"));
    }

    [Fact]
    public void GeneratorIsDeterministic_AcrossTwoIndependentRuns(){
        var First=GameSystemGeneratorTestHelper.RunGenerator(
                                                             GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);
        var Second=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,TwoSystems);

        string[]FirstTexts=[.. First.Results[0].GeneratedSources.OrderBy(S=>S.HintName,StringComparer.Ordinal).Select(S=>S.HintName+"\n"+S.SourceText)];
        string[]SecondTexts=[.. Second.Results[0].GeneratedSources.OrderBy(S=>S.HintName,StringComparer.Ordinal).Select(S=>S.HintName+"\n"+S.SourceText)];

        Assert.Equal(FirstTexts,SecondTexts);
    }

    [Fact]
    public void SystemWithDiagnostics_IsExcluded_ButValidSystemsStillGenerate(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              GameSystemGeneratorTestHelper.CommonComponents,
                                                              """
                                                              using CSharpGameEngine.Entities;
                                                              using Sample;

                                                              namespace Game;

                                                              internal struct ValidSystem : ISystem
                                                              {
                                                                  public bool Update(ref Position position, ref State state) => true;
                                                              }

                                                              internal struct BrokenSystem : ISystem
                                                              {
                                                                  public bool Update(ref Velocity velocity) => true;
                                                              }
                                                              """);

        Assert.Contains(Result.Diagnostics,D=>D.Id=="SYSGEN013");

        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("ValidSystem",Loop);
        Assert.DoesNotContain("BrokenSystem",Loop);
    }
}

