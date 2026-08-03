using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class EcsArchetypeGeneratorDiagnosticsTests
{
    [Fact]
    public void NonPartialStruct_ReportsEcsgen001()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public struct NonPartialArchetype
            {
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN001");
    }

    [Fact]
    public void GenericArchetypeStruct_ReportsEcsgen002()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct GenericArchetype<T>
            {
                public partial ref Position Position { get; }
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN002");
    }

    [Fact]
    public void NestedArchetypeStruct_ReportsEcsgen003()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            public partial struct Container
            {
                [EcsArchetype]
                public partial struct NestedArchetype
                {
                    public partial ref Position Position { get; }
                }
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN003");
    }

    [Fact]
    public void EmptyArchetype_ReportsEcsgen004()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct EmptyArchetype
            {
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN004");
    }

    [Fact]
    public void NonRefReturningPartialProperty_ReportsEcsgen005()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct NonRefArchetype
            {
                public partial Position Position { get; }
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN005");
    }

    [Fact]
    public void NonStructComponentType_ReportsEcsgen006()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct BadComponentTypeArchetype
            {
                public partial ref string Name { get; }
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN006");
    }

    [Fact]
    public void DuplicateComponentType_ReportsEcsgen007_ForInvalidObjectSample()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct InvalidObject
            {
                public partial ref Position CurrentPosition { get; }

                // 同じPosition型が重複するためSGエラー
                public partial ref Position PreviousPosition { get; }
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN007");
    }

    [Fact]
    public void EcsReadOnlyOnMutableRefProperty_ReportsEcsgen008()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct BadReadOnlyArchetype
            {
                [EcsReadOnly]
                public partial ref Position Position { get; }
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN008");
    }

    [Fact]
    public void RefReadonlyPropertyMissingAttribute_ReportsEcsgen009()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct MissingAttributeArchetype
            {
                public partial ref readonly Position Position { get; }
            }
            """;

        AssertHasDiagnostic(source, "ECSGEN009");
    }

    [Fact]
    public void ArchetypeWithDiagnostics_DoesNotGenerateSource()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct EmptyArchetype
            {
            }
            """;

        GeneratorDriverRunResult result = EcsArchetypeGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Results[0].GeneratedSources);
    }

    private static void AssertHasDiagnostic(string source, string diagnosticId)
    {
        GeneratorDriverRunResult result = EcsArchetypeGeneratorTestHelper.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == diagnosticId);
    }
}
