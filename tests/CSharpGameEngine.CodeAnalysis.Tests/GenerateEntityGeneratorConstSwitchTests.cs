using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// 同一のユーザーコードで<c>EntityGenerationSettings.Kind</c>だけを切り替え、選択したBackendだけが
/// 生成されること・生成コードにRuntime分岐が残らないこと・公開Character APIが両Backendで
/// 同じ形状になることを検証する。
/// </summary>
public sealed class GenerateEntityGeneratorConstSwitchTests
{
    private const string CharacterSource = """


        [GenerateEntity(EntityGenerationSettings.Kind)]
        public partial struct Character
        {
            public partial LocalTransform Transform { get; set; }
            public partial Velocity Velocity { get; set; }
            public partial Health Health { get; set; }
        }
        """;

    [Fact]
    public void AosKind_GeneratesOnlyAosHintFiles_NoSoaFiles()
    {
        GeneratorDriverRunResult result = RunForKind("ObjectOrientedAos");

        string[] hintNames = [.. result.Results[0].GeneratedSources.Select(s => s.HintName)];

        Assert.Contains(hintNames, n => n.EndsWith(".Common.g.cs", StringComparison.Ordinal));
        Assert.Contains(hintNames, n => n.EndsWith(".Aos.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(hintNames, n => n.EndsWith(".Soa.g.cs", StringComparison.Ordinal));
        Assert.Equal(2, hintNames.Length);
    }

    [Fact]
    public void SoaKind_GeneratesOnlySoaHintFiles_NoAosFiles()
    {
        GeneratorDriverRunResult result = RunForKind("EcsSoa");

        string[] hintNames = [.. result.Results[0].GeneratedSources.Select(s => s.HintName)];

        Assert.Contains(hintNames, n => n.EndsWith(".Common.g.cs", StringComparison.Ordinal));
        Assert.Contains(hintNames, n => n.EndsWith(".Soa.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(hintNames, n => n.EndsWith(".Aos.g.cs", StringComparison.Ordinal));
        Assert.Equal(2, hintNames.Length);
    }

    [Fact]
    public void AosOutput_DoesNotContainSoaChunkOrArchetypeTypes()
    {
        GeneratorDriverRunResult result = RunForKind("ObjectOrientedAos");
        string allText = string.Join("\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

        Assert.DoesNotContain("SoaGeneratedArchetype", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("IEcsArchetype", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("ArchetypeChunk", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void SoaOutput_DoesNotContainAosDataRowType()
    {
        GeneratorDriverRunResult result = RunForKind("EcsSoa");
        string allText = string.Join("\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

        Assert.DoesNotContain("AosGeneratedData", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("AosEntityStore", allText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ObjectOrientedAos")]
    [InlineData("EcsSoa")]
    public void GeneratedSource_ContainsNoRuntimeStorageKindBranch(string kindMemberName)
    {
        GeneratorDriverRunResult result = RunForKind(kindMemberName);
        string allText = string.Join("\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

        Assert.DoesNotContain("EntityStorageKind", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("switch", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicCharacterApi_IsIdenticalBetweenAosAndSoa()
    {
        Assembly aosAssembly = CompileForKind("ObjectOrientedAos");
        Assembly soaAssembly = CompileForKind("EcsSoa");

        Type aosCharacter = aosAssembly.GetType("Sample.Character")!;
        Type soaCharacter = soaAssembly.GetType("Sample.Character")!;

        Assert.Equal(PublicMemberSignatures(aosCharacter), PublicMemberSignatures(soaCharacter));

        Type aosCreateInfo = aosAssembly.GetType("Sample.CharacterCreateInfo")!;
        Type soaCreateInfo = soaAssembly.GetType("Sample.CharacterCreateInfo")!;

        Assert.Equal(PublicMemberSignatures(aosCreateInfo), PublicMemberSignatures(soaCreateInfo));
    }

    private static GeneratorDriverRunResult RunForKind(string kindMemberName)
    {
        string source = GenerateEntityGeneratorTestHelper.CommonComponents
            + GenerateEntityGeneratorTestHelper.EntityGenerationSettings(kindMemberName)
            + CharacterSource;

        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(source);
        Assert.Empty(result.Diagnostics);
        return result;
    }

    private static Assembly CompileForKind(string kindMemberName)
    {
        string source = GenerateEntityGeneratorTestHelper.CommonComponents
            + GenerateEntityGeneratorTestHelper.EntityGenerationSettings(kindMemberName)
            + CharacterSource;

        return GenerateEntityGeneratorTestHelper.CompileAndLoad(source);
    }

    private static string[] PublicMemberSignatures(Type type)
    {
        return [.. type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.ToString() ?? string.Empty)
            .OrderBy(s => s, StringComparer.Ordinal)];
    }
}
