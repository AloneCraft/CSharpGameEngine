using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// <c>[StartupScene]</c>/<c>[SceneEntity]</c>から生成されるコードそのものを検証する。合成Systemが
/// 既存の<c>GameSystemGenerator</c>パイプラインへ「実際に検出されたSystemと区別なく」合流すること
/// (Archetype順序の維持・ループへのディスパッチ・エントリポイント連携)が最重要の契約。
/// </summary>
public sealed class StartupSceneGeneratorEmissionTests {
    private const string Components="""
        namespace Sample;

        public struct AppHost { public int Width; }
        public struct Level { public string? Path; }
        public struct Position { public float X; }
        public struct Velocity { public float X; }
        """;

    [Fact]
    public void HiddenStartupSystem_ImplementsISystem_AndGuardsWithBootstrapDone(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position { X = 1f };
            }
            """;

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source]);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"Game.MainScene.StartupSystem.g.cs");

        Assert.Contains("internal struct MainScene__StartupSystem : global::CSharpGameEngine.Entities.ISystem",Text);
        Assert.Contains("if (Bootstrap.Done)",Text);
        Assert.Contains("Bootstrap.Done = true;",Text);

        var DoneGuardIndex=Text.IndexOf("if (Bootstrap.Done)",StringComparison.Ordinal);
        var CreateIndex=Text.IndexOf("State.World.Create(",StringComparison.Ordinal);
        Assert.True(DoneGuardIndex<CreateIndex,"Bootstrap.Doneのガードは、World.Create呼び出しより前に生成されなければならない。");
    }

    [Fact]
    public void SingleComponentEntity_PassesPropertyDirectlyWithoutIntermediateLocal(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position { X = 1f };
            }
            """;

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source]);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"Game.MainScene.StartupSystem.g.cs");

        Assert.Contains("State.World.Create(global::Game.MainScene.Origin);",Text);
        Assert.DoesNotContain("var Origin",Text);
    }

    [Fact]
    public void TupleEntity_EvaluatesPropertyOnceAndAccessesNamedElements(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (AppHost App, Level Level) Application => (new AppHost(), new Level());
            }
            """;

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source]);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"Game.MainScene.StartupSystem.g.cs");

        Assert.Contains("var Application = global::Game.MainScene.Application;",Text);
        Assert.Contains("State.World.Create(Application.App, Application.Level);",Text);

        // "global::Game.MainScene.Application"というプロパティ参照(=評価)は1回だけ出現するはず。
        var OccurrenceCount=0;
        var SearchIndex=0;

        while((SearchIndex=Text.IndexOf("global::Game.MainScene.Application",SearchIndex,StringComparison.Ordinal))>=0){
            OccurrenceCount++;
            SearchIndex+=1;
        }

        Assert.Equal(1,OccurrenceCount);
    }

    [Fact]
    public void MultipleSceneEntities_AreEmittedInSourceDeclarationOrder(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Velocity Second => new Velocity { X = 2f };

                [SceneEntity]
                internal static Position First => new Position { X = 1f };
            }
            """;

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source]);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"Game.MainScene.StartupSystem.g.cs");

        // Sourceの宣言順はSecondが先だが、両方ともプロパティ宣言位置で決定的に整列されるため、
        // 生成コード内でもSecond用のCreate呼び出しがFirstより先に出現するはず。
        var SecondIndex=Text.IndexOf("global::Game.MainScene.Second",StringComparison.Ordinal);
        var FirstIndex=Text.IndexOf("global::Game.MainScene.First",StringComparison.Ordinal);

        Assert.True(SecondIndex>=0&&FirstIndex>=0);
        Assert.True(SecondIndex<FirstIndex);
    }

    [Fact]
    public void SyntheticSystem_IsDispatchedFromGeneratedGameLoop(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position { X = 1f };
            }
            """;

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source]);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("MainScene__StartupSystem.Update(",Text);
    }

    /// <summary>
    /// StartupSceneが出力する{AppHost, Level}は、実System(Window/LevelLoad)が既に登録済みの同じ集合の
    /// 後ろへ二重登録されるだけで、Archetypeの出現順(=UserEcsProjectの実際の並び)を動かさないことを
    /// 検証する(要件セクション9)。
    /// </summary>
    [Fact]
    public void StartupSceneOutputArchetype_DoesNotDisturbExistingSystemDerivedOrder(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (AppHost App, Level Level) Application => (new AppHost(), new Level());
            }

            [SystemOrder(1)]
            file sealed class RuntimeOrder;

            internal struct Window : ISystem
            {
                public bool Update(ref AppHost app, ref State state) => true;
            }

            internal struct LevelLoad : ISystem
            {
                public bool Update(ref AppHost app, ref Level level, ref State state) => true;
            }
            """;

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            [.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source]);

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");

        // Archetype0 {Bootstrap} (合成Startup System自身), Archetype1 {AppHost} (Window),
        // Archetype2 {AppHost, Level} (LevelLoad = MainSceneのTuple出力と同一集合、二重登録されない)。
        Assert.Contains("public enum Archetype {\n    None = -1,\n    Archetype0,\n    Archetype1,\n    Archetype2,\n}",Text);
    }

    [Fact]
    public void Generator_ProducesEntryPoint_WhenStartupSceneIsTheOnlySystem(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static Position Origin => new Position { X = 1f };
            }
            """;

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CreateApplicationCompilation(
                [.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source]));

        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.EntryPoint.g.cs");

        Assert.Contains("World.Create(new global::CSharpGameEngine.Entities.Bootstrap());",Text);
        Assert.Contains("GeneratedGameLoop.Run(ref State);",Text);
    }

    [Fact]
    public void Generator_IsDeterministic_AcrossTwoIndependentRunsWithStartupScene(){
        var Source="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [StartupScene(Order = 0)]
            internal static partial class MainScene
            {
                [SceneEntity]
                internal static (AppHost App, Level Level) Application => (new AppHost(), new Level());
            }
            """;

        string[]AllSources=[.. GameSystemGeneratorTestHelper.StartupSceneSources,Components,Source];

        GeneratorDriverRunResult First=GameSystemGeneratorTestHelper.RunGenerator(AllSources);
        GeneratorDriverRunResult Second=GameSystemGeneratorTestHelper.RunGenerator(AllSources);

        string[]FirstTexts=[.. First.Results[0].GeneratedSources.OrderBy(S=>S.HintName,StringComparer.Ordinal).Select(S=>S.SourceText.ToString())];
        string[]SecondTexts=[.. Second.Results[0].GeneratedSources.OrderBy(S=>S.HintName,StringComparer.Ordinal).Select(S=>S.SourceText.ToString())];

        Assert.Equal(FirstTexts,SecondTexts);
    }
}
