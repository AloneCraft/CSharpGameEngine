using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class EcsArchetypeGeneratorEmissionTests
{
    [Fact]
    public void Player_NormalizesDeclarationOrderToFullyQualifiedNameOrder()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct Player
            {
                // わざと正規化順とは異なる順番で書いている
                public partial ref PlayerTag PlayerTag { get; }
                public partial ref Velocity Velocity { get; }
                public partial ref Health Health { get; }
                public partial ref Rotation Rotation { get; }
                public partial ref Position Position { get; }
            }
            """;

        GeneratorDriverRunResult result = EcsArchetypeGeneratorTestHelper.RunGenerator(source);

        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources);
        Assert.Equal("Sample.Player.g.cs", generated.HintName);
        Assert.Empty(result.Diagnostics);

        string text = generated.SourceText.ToString();
        AssertOrdered(text, "typeof(global::Sample.Health)", "typeof(global::Sample.PlayerTag)", "typeof(global::Sample.Position)", "typeof(global::Sample.Rotation)", "typeof(global::Sample.Velocity)");
    }

    [Fact]
    public void TwoArchetypesWithSameComponentSetDifferentOrder_NormalizeIdenticallyAndCompileWithoutDiagnostics()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct MovingObjectA
            {
                public partial ref Velocity Velocity { get; }
                public partial ref Position Position { get; }
            }

            [EcsArchetype]
            public partial struct MovingObjectB
            {
                public partial ref Position Position { get; }
                public partial ref Velocity Velocity { get; }
            }
            """;

        GeneratorDriverRunResult result = EcsArchetypeGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Results[0].GeneratedSources.Length);

        string textA = result.Results[0].GeneratedSources.Single(s => s.HintName == "Sample.MovingObjectA.g.cs").SourceText.ToString();
        string textB = result.Results[0].GeneratedSources.Single(s => s.HintName == "Sample.MovingObjectB.g.cs").SourceText.ToString();

        AssertOrdered(textA, "typeof(global::Sample.Position)", "typeof(global::Sample.Velocity)");
        AssertOrdered(textB, "typeof(global::Sample.Position)", "typeof(global::Sample.Velocity)");
    }

    [Fact]
    public void RenderableObject_RefReadonlyProperty_GeneratesMatchingRefReadonlyGetter()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct RenderableObject
            {
                [EcsReadOnly]
                public partial ref readonly Mesh Mesh { get; }

                [EcsReadOnly]
                public partial ref readonly Material Material { get; }

                public partial ref Position Position { get; }
            }
            """;

        GeneratorDriverRunResult result = EcsArchetypeGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources);
        string text = generated.SourceText.ToString();

        Assert.Contains("public partial ref readonly global::Sample.Mesh Mesh", text);
        Assert.Contains("public partial ref readonly global::Sample.Material Material", text);
        Assert.Contains("public partial ref global::Sample.Position Position", text);
        Assert.DoesNotContain("ref readonly global::Sample.Position", text);
    }

    [Fact]
    public void RunGenerators_ProducesIdenticalOutputAcrossIndependentRuns()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct Bullet
            {
                public partial ref Position Position { get; }
                public partial ref Velocity Velocity { get; }
                public partial ref Damage Damage { get; }
            }
            """;

        GeneratorDriverRunResult first = EcsArchetypeGeneratorTestHelper.RunGenerator(source);
        GeneratorDriverRunResult second = EcsArchetypeGeneratorTestHelper.RunGenerator(source);

        string firstText = Assert.Single(first.Results[0].GeneratedSources).SourceText.ToString();
        string secondText = Assert.Single(second.Results[0].GeneratedSources).SourceText.ToString();

        Assert.Equal(firstText, secondText);
    }

    [Fact]
    public void GeneratedSource_ParsesWithoutSyntaxDiagnostics()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct Bullet
            {
                public partial ref Position Position { get; }
                public partial ref Velocity Velocity { get; }
                public partial ref Damage Damage { get; }
            }
            """;

        GeneratorDriverRunResult result = EcsArchetypeGeneratorTestHelper.RunGenerator(source);
        GeneratedSourceResult generated = Assert.Single(result.Results[0].GeneratedSources);

        SyntaxTree reparsed = CSharpSyntaxTree.ParseText(generated.SourceText.ToString(), new CSharpParseOptions(LanguageVersion.Latest));

        Assert.Empty(reparsed.GetDiagnostics());
    }

    private static void AssertOrdered(string text, params string[] substringsInExpectedOrder)
    {
        int previousIndex = -1;

        foreach (string substring in substringsInExpectedOrder)
        {
            int index = text.IndexOf(substring, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected to find '{substring}' in generated source.");
            Assert.True(index > previousIndex, $"Expected '{substring}' to appear after the previous entry.");
            previousIndex = index;
        }
    }
}
