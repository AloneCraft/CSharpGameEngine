using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class StaticArchetypeGeneratorEmissionTests
{
    [Fact]
    public void PromotedSample_GeneratesExpectedArchetypeTypesShape()
    {
        GeneratorDriverRunResult result = StaticArchetypeGeneratorTestHelper.RunGenerator(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            StaticArchetypeGeneratorTestHelper.PromotedSystemsSource);

        Assert.Empty(result.Diagnostics);

        string text = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "StaticArchetype.Types.g.cs").SourceText.ToString();

        // 出力順序は発見順ではなく「コンポーネント数昇順→インデックスタプル辞書式昇順」
        // (Archetype0,Archetype1,Archetype2が先、2コンポーネントのArchetype01が最後 - ADR-0032決定事項3)。
        Assert.Contains("internal enum Archetype\n{\n    Archetype0,\n    Archetype1,\n    Archetype2,\n    Archetype01,\n}", text);

        Assert.Contains("internal struct Archetype0:IArchetype{", text);
        Assert.Contains("public static void Create(ref World World, Component0 Component0){", text);
        Assert.Contains("internal struct Archetype01:IArchetype{", text);
        Assert.Contains("public Archetype01(Entity Entity, Component0 Component0, Component1 Component1){", text);
    }

    [Fact]
    public void PromotedSample_GeneratesExpectedWorldShape()
    {
        GeneratorDriverRunResult result = StaticArchetypeGeneratorTestHelper.RunGenerator(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            StaticArchetypeGeneratorTestHelper.PromotedSystemsSource);

        string text = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "StaticArchetype.World.g.cs").SourceText.ToString();

        Assert.Contains("internal readonly ChunkList<Archetype0> ListArchetype0 = new();", text);
        Assert.Contains("internal readonly ChunkList<Archetype1> ListArchetype1 = new();", text);
        Assert.Contains("internal readonly ChunkList<Archetype2> ListArchetype2 = new();", text);
        Assert.Contains("internal readonly ChunkList<Archetype01> ListArchetype01 = new();", text);
        Assert.Contains("public bool MoveArchetype(Entity Entity,Component0 Component0,ref State State){", text);
        Assert.Contains("public bool MoveArchetype(Entity Entity,Component0 Component0, Component1 Component1,ref State State){", text);
        Assert.Contains("private void Detach(Entity Entity){", text);
        Assert.Contains("public void PlaybackMoveArchetypes(ref State State){", text);
        Assert.Contains("case Archetype.Archetype2:{", text);
    }

    [Fact]
    public void PromotedSample_GeneratesLoopWiringBothSystems()
    {
        GeneratorDriverRunResult result = StaticArchetypeGeneratorTestHelper.RunGenerator(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            StaticArchetypeGeneratorTestHelper.PromotedSystemsSource);

        string text = Assert.Single(result.Results[0].GeneratedSources, s => s.HintName == "StaticArchetype.Loop.g.cs").SourceText.ToString();

        Assert.Contains("internal static void RunFrame(ref State State, ref System0 System0, ref System1 System1){", text);

        // サンプルの手書き.g.csはSystem0しか呼んでいないが、これは書き漏れとユーザー確認済み。
        // 実装するSGはISystemを実装する全structをループに含める(ADR-0032決定事項5)。
        Assert.Contains("System1.Update(ref Archetype.Component0,ref State)", text);
        Assert.Contains("System1.Update(ref Archetype.Component1,ref State)", text);
        Assert.Contains("System1.Update(ref Archetype.Component0,ref Archetype.Component1,ref State)", text);

        // Component2はSystem0の1オーバーロードだけが対象、System1にはComponent2を扱うオーバーロードが無いため呼ばれない。
        int archetype2LoopStart = text.IndexOf("ListArchetype2.Count-1", StringComparison.Ordinal);
        int archetype01LoopStart = text.IndexOf("ListArchetype01.Count-1", StringComparison.Ordinal);
        string archetype2LoopBody = text[archetype2LoopStart..archetype01LoopStart];

        Assert.Contains("System0.Update(Archetype.Entity,ref Archetype.Component2,ref State)", archetype2LoopBody);
        Assert.DoesNotContain("System1.Update", archetype2LoopBody);

        Assert.Contains("internal static void Run(ref State State){", text);
        Assert.Contains("State.World.PlaybackMoveArchetypes(ref State);", text);
    }

    [Fact]
    public void PromotedSample_Compiles()
    {
        Assembly assembly = StaticArchetypeGeneratorTestHelper.CompileAndLoad(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            StaticArchetypeGeneratorTestHelper.PromotedSystemsSource);

        Assert.NotNull(assembly.GetType("GeneratedGameLoop"));
        Assert.NotNull(assembly.GetType("Archetype01"));
    }

    [Fact]
    public void GeneratorIsDeterministic_AcrossTwoIndependentRuns()
    {
        GeneratorDriverRunResult first = StaticArchetypeGeneratorTestHelper.RunGenerator(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            StaticArchetypeGeneratorTestHelper.PromotedSystemsSource);
        GeneratorDriverRunResult second = StaticArchetypeGeneratorTestHelper.RunGenerator(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            StaticArchetypeGeneratorTestHelper.PromotedSystemsSource);

        string[] firstTexts = [.. first.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];
        string[] secondTexts = [.. second.Results[0].GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText)];

        Assert.Equal(firstTexts, secondTexts);
    }

    [Fact]
    public void DeclarationOrderOfSystems_DoesNotAffectIndexAssignment()
    {
        // Systems.csと同じ内容だが、System1をSystem0より先に宣言する(物理的なソース順序を反転)。
        // FQN順ソート(StaticArchetypeIndexAssigner)がソース宣言順ではなく型の完全修飾名で
        // 決定的に走査することの証明(ADR-0032決定事項2)。
        string reorderedSystems = """
            internal partial struct System1:ISystem{
                internal bool Update(ref Component0 Component0,ref State State){
                    return true;
                }
                internal bool Update(ref Component1 Component1,ref State State){
                    return true;
                }
                internal bool Update(ref Component0 Component0,ref Component1 Component1,ref State State){
                    return true;
                }
            }
            internal partial struct System0:ISystem{
                public bool Update(Entity Entity,ref Component0 Component0,ref State State){
                    var Component1=new Component1();
                    State.World.MoveArchetype(Entity, Component0, Component1,ref State);
                    return true;
                }
                public bool Update(Entity Entity,ref Component1 Component1,ref State State){
                    var Component0=new Component0();
                    State.World.MoveArchetype(Entity, Component0, Component1,ref State);
                    return true;
                }
                public bool Update(Entity Entity,ref Component0 Component0,ref Component1 Component1,ref State State){
                    State.World.MoveArchetype(Entity, Component0, Component1,ref State);
                    return true;
                }
                public bool Update(Entity Entity,ref Component2 Component2,ref State State){
                    return true;
                }
            }
            """;

        GeneratorDriverRunResult reordered = StaticArchetypeGeneratorTestHelper.RunGenerator(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            reorderedSystems);
        GeneratorDriverRunResult original = StaticArchetypeGeneratorTestHelper.RunGenerator(
            StaticArchetypeGeneratorTestHelper.PromotedComponentsSource,
            StaticArchetypeGeneratorTestHelper.PromotedSystemsSource);

        string reorderedTypes = Assert.Single(reordered.Results[0].GeneratedSources, s => s.HintName == "StaticArchetype.Types.g.cs").SourceText.ToString();
        string originalTypes = Assert.Single(original.Results[0].GeneratedSources, s => s.HintName == "StaticArchetype.Types.g.cs").SourceText.ToString();

        Assert.Equal(originalTypes, reorderedTypes);
    }
}
