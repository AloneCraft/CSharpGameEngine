using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GenerateEntityGeneratorDiagnosticsTests
{
    [Fact]
    public void NonPartialStruct_ReportsEntgen001()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public struct NonPartialCharacter
            {
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN001");
    }

    [Fact]
    public void GenericStruct_ReportsEntgen002()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct GenericCharacter<T>
            {
                public partial LocalTransform Transform { get; set; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN002");
    }

    [Fact]
    public void NestedInNonPartialContainingType_ReportsEntgen003()
    {
        string source = Preamble + """


            public class Container
            {
                [GenerateEntity(EntityGenerationSettings.Kind)]
                public partial struct Character
                {
                    public partial LocalTransform Transform { get; set; }
                }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN003");
    }

    [Fact]
    public void NestedInPartialContainingType_DoesNotReportEntgen003()
    {
        string source = Preamble + """


            public partial class Container
            {
                [GenerateEntity(EntityGenerationSettings.Kind)]
                public partial struct Character
                {
                    public partial LocalTransform Transform { get; set; }
                }
            }
            """;

        AssertDoesNotHaveDiagnostic(source, "ENTGEN003");
    }

    [Fact]
    public void EmptyStruct_ReportsEntgen004()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN004");
    }

    [Fact]
    public void NonPartialGetSetProperty_ReportsEntgen005()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public LocalTransform Transform { get; set; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN005");
    }

    [Fact]
    public void StaticPartialProperty_ReportsEntgen006()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public static partial LocalTransform Transform { get; set; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN006");
    }

    [Fact]
    public void PartialIndexer_ReportsEntgen007()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform this[int index] { get; set; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN007");
    }

    [Fact]
    public void PartialPropertyMissingSet_ReportsEntgen008()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform Transform { get; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN008");
    }

    [Fact]
    public void InitOnlyPartialProperty_ReportsEntgen009()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform Transform { get; init; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN009");
    }

    [Fact]
    public void ManagedComponentType_ReportsEntgen010()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial string Name { get; set; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN010");
    }

    [Fact]
    public void DuplicateComponentType_ReportsEntgen011()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform CurrentTransform { get; set; }
                public partial LocalTransform PreviousTransform { get; set; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN011");
    }

    [Fact]
    public void UserDefinedDestroyMethod_ReportsEntgen012()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform Transform { get; set; }

                public void Destroy()
                {
                }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN012");
    }

    [Fact]
    public void ConflictingCharacterStoreType_ReportsEntgen013()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform Transform { get; set; }
            }

            public class CharacterStore
            {
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN013");
    }

    [Fact]
    public void OutOfRangeStorageKind_ReportsEntgen014()
    {
        string source = GenerateEntityGeneratorTestHelper.CommonComponents + """


            [GenerateEntity((CSharpGameEngine.Generation.EntityStorageKind)99)]
            public partial struct Character
            {
                public partial LocalTransform Transform { get; set; }
            }
            """;

        AssertHasDiagnostic(source, "ENTGEN014");
    }

    [Fact]
    public void BelowCSharp13LanguageVersion_ReportsEntgen015()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform Transform { get; set; }
            }
            """;

        Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation =
            GenerateEntityGeneratorTestHelper.CreateCompilation(LanguageVersion.CSharp12, source);

        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(compilation);

        Assert.Contains(result.Diagnostics, d => d.Id == "ENTGEN015");
    }

    [Fact]
    public void ValidCharacterSource_ProducesZeroDiagnostics()
    {
        string source = Preamble + """


            [GenerateEntity(EntityGenerationSettings.Kind)]
            public partial struct Character
            {
                public partial LocalTransform Transform { get; set; }
                public partial Velocity Velocity { get; set; }
                public partial Health Health { get; set; }

                public void Move(float deltaTime)
                {
                }

                public static Character Spawn(CharacterStore store, in CharacterCreateInfo info)
                {
                    return store.Create(in info);
                }
            }
            """;

        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// 共通コンポーネント + EntityGenerationSettings(EcsSoa)を連結した、各テストの前提部分。
    /// </summary>
    private static string Preamble =>
        GenerateEntityGeneratorTestHelper.CommonComponents + GenerateEntityGeneratorTestHelper.EntityGenerationSettings("EcsSoa");

    private static void AssertHasDiagnostic(string source, string diagnosticId)
    {
        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == diagnosticId);
    }

    private static void AssertDoesNotHaveDiagnostic(string source, string diagnosticId)
    {
        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == diagnosticId);
    }
}
