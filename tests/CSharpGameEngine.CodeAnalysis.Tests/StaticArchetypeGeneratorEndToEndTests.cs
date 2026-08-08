using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// StaticArchetypeGeneratorが出力したコード(Archetype/World/GeneratedGameLoop)を実際にコンパイル・
/// Emit・ロードし、実World/Entityを使って実行する。「Archetypeは閉じた集合として列挙される」
/// 「Update引数の部分集合一致で複数System・複数オーバーロードが発火する」「構造変更はCommand Buffer
/// 経由でフレーム末尾に安全に適用される」という設計の中核契約が、Generatorが実際に出力するコードで
/// 機能することを証明する最重要テスト。
/// </summary>
public sealed class StaticArchetypeGeneratorEndToEndTests
{
    [Fact]
    public void PromotedSample_EntitiesMigrateBetweenArchetypesAcrossFrames()
    {
        string source = StaticArchetypeGeneratorTestHelper.PromotedComponentsSource
            + StaticArchetypeGeneratorTestHelper.PromotedSystemsSource
            + """

            internal static class TestEntry{
                internal static string RunScenario(){
                    var world = new World();
                    var state = new State(world){ Enabled = true };

                    Archetype0.Create(ref world, new Component0());
                    Archetype0.Create(ref world, new Component0());
                    Archetype2.Create(ref world, new Component2());

                    var system0 = new System0();
                    var system1 = new System1();

                    if (world.ListArchetype0.Count != 2) return "FAIL:PreconditionArchetype0Count";

                    GeneratedGameLoop.RunFrame(ref state, ref system0, ref system1);

                    if (world.ListArchetype0.Count != 0) return "FAIL:Archetype0ShouldBeEmptyAfterMove:" + world.ListArchetype0.Count;
                    if (world.ListArchetype01.Count != 2) return "FAIL:BothEntitiesShouldHaveMovedToArchetype01:" + world.ListArchetype01.Count;
                    if (world.ListArchetype2.Count != 1) return "FAIL:Archetype2ShouldBeUntouched:" + world.ListArchetype2.Count;

                    GeneratedGameLoop.RunFrame(ref state, ref system0, ref system1);

                    if (world.ListArchetype01.Count != 2) return "FAIL:Archetype01ShouldStayAt2AfterSecondFrame:" + world.ListArchetype01.Count;

                    return "OK";
                }
            }
            """;

        Assembly assembly = StaticArchetypeGeneratorTestHelper.CompileAndLoad(source);
        object? result = StaticArchetypeGeneratorTestHelper.Invoke(assembly, "TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }

    [Fact]
    public void MultiSystemDispatch_FiresAllMatchingOverloadsInDeterministicOrder()
    {
        string source = StaticArchetypeGeneratorTestHelper.PromotedComponentsSource
            + StaticArchetypeGeneratorTestHelper.PromotedSystemsSource
            + """

            internal struct CountingSystem:ISystem{
                public int Count0;
                public int Count1;
                public int Count01;
                public bool Update(ref Component0 Component0, ref State State){ this.Count0++; return true; }
                public bool Update(ref Component1 Component1, ref State State){ this.Count1++; return true; }
                public bool Update(ref Component0 Component0, ref Component1 Component1, ref State State){ this.Count01++; return true; }
            }

            internal static class TestEntry{
                internal static string RunScenario(){
                    var world = new World();
                    var state = new State(world){ Enabled = true };

                    Archetype0.Create(ref world, new Component0());
                    Archetype1.Create(ref world, new Component1());
                    Archetype01.Create(ref world, new Component0(), new Component1());

                    var counting = new CountingSystem();
                    var system0 = new System0();
                    var system1 = new System1();

                    GeneratedGameLoop.RunFrame(ref state, ref counting, ref system0, ref system1);

                    // Archetype0(1体)+Archetype01(1体)からCount0=2、Archetype1(1体)+Archetype01(1体)からCount1=2、
                    // Archetype01(1体)からCount01=1 - 部分集合一致による複数System・複数オーバーロード発火の証明。
                    if (counting.Count0 != 2) return "FAIL:Count0=" + counting.Count0;
                    if (counting.Count1 != 2) return "FAIL:Count1=" + counting.Count1;
                    if (counting.Count01 != 1) return "FAIL:Count01=" + counting.Count01;

                    return "OK";
                }
            }
            """;

        Assembly assembly = StaticArchetypeGeneratorTestHelper.CompileAndLoad(source);
        object? result = StaticArchetypeGeneratorTestHelper.Invoke(assembly, "TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }

    [Fact]
    public void Diagnostics_ExcludeOnlyTheOffendingSystemFromCombinedOutput()
    {
        string source = """
            internal struct Component0{}
            internal struct ValidSystem:ISystem{
                public bool Update(ref Component0 Component0, ref State State){ return true; }
            }
            internal struct BrokenSystem:ISystem{
                public bool Update(ref Component0 Component0){ return true; }
            }
            """;

        GeneratorDriverRunResult result = StaticArchetypeGeneratorTestHelper.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "SARCH013");

        string types = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "StaticArchetype.Types.g.cs").SourceText.ToString();
        Assert.Contains("internal struct Archetype0:IArchetype{", types);

        string loop = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "StaticArchetype.Loop.g.cs").SourceText.ToString();
        Assert.Contains("ValidSystem", loop);
        Assert.DoesNotContain("BrokenSystem", loop);
    }
}
