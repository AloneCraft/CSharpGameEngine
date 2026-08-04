using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// <c>EntityStorageKind.Aos | EntityStorageKind.EcsSoa</c>を1つの宣言へ指定した
/// 比較・計測モードの検証。1宣言から両バックエンドが生成され、型名が衝突せず、
/// Facadeは(partialプロパティの実装宣言が1つしか持てない言語制約により)AoS側にのみ生成される。
/// </summary>
public sealed class GenerateEntityCombinedKindTests
{
    private const string CombinedSettings = """


        public static class EntityGenerationSettings
        {
            public const CSharpGameEngine.Generation.EntityStorageKind Kind =
                CSharpGameEngine.Generation.EntityStorageKind.Aos
                | CSharpGameEngine.Generation.EntityStorageKind.EcsSoa;
        }
        """;

    private const string ParticleSource = """


        [GenerateEntity(EntityGenerationSettings.Kind)]
        public partial struct Particle
        {
            public partial LocalTransform Transform { get; set; }
            public partial Velocity Velocity { get; set; }
            public partial Health Health { get; set; }
        }
        """;

    private static string Source =>
        GenerateEntityGeneratorTestHelper.CommonComponents + CombinedSettings + ParticleSource;

    [Fact]
    public void CombinedKind_EmitsAllThreeHintFiles_FromOneDeclaration()
    {
        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(Source);

        Assert.Empty(result.Diagnostics);
        string[] hintNames = [.. result.Results[0].GeneratedSources.Select(s => s.HintName)];

        Assert.Contains(hintNames, n => n.EndsWith(".Common.g.cs", StringComparison.Ordinal));
        Assert.Contains(hintNames, n => n.EndsWith(".Aos.g.cs", StringComparison.Ordinal));
        Assert.Contains(hintNames, n => n.EndsWith(".Soa.g.cs", StringComparison.Ordinal));
        Assert.Equal(3, hintNames.Length);
    }

    [Fact]
    public void CombinedKind_CompilesWithoutErrors()
    {
        CSharpCompilation compilation = GenerateEntityGeneratorTestHelper.CreateCompilation(Source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GenerateEntityGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out System.Collections.Immutable.ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void CombinedKind_GeneratesBothStoresWithDistinctNames()
    {
        Assembly assembly = GenerateEntityGeneratorTestHelper.CompileAndLoad(Source);

        Assert.NotNull(assembly.GetType("Sample.ParticleAosStore"));
        Assert.NotNull(assembly.GetType("Sample.ParticleSoaStore"));

        // 単一指定時の型名は生成されない(両指定時は必ず接尾辞が付く)。
        Assert.Null(assembly.GetType("Sample.ParticleStore"));
    }

    [Fact]
    public void CombinedKind_GeneratesBothBackendPhysicalTypes()
    {
        Assembly assembly = GenerateEntityGeneratorTestHelper.CompileAndLoad(Source);

        Assert.NotNull(assembly.GetType("Sample.ParticleAosGeneratedData"));
        Assert.NotNull(assembly.GetType("Sample.ParticleSoaGeneratedArchetype"));
    }

    [Fact]
    public void CombinedKind_EmitsFacadeOnlyOnce_BoundToAosStore()
    {
        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(Source);

        string commonText = result.Results[0].GeneratedSources
            .Single(s => s.HintName.EndsWith(".Common.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();

        // Facadeは1回だけ、かつAoS側のStoreに束縛される。
        Assert.Contains("private readonly ParticleAosStore? _store;", commonText, StringComparison.Ordinal);
        Assert.DoesNotContain("ParticleSoaStore", commonText, StringComparison.Ordinal);

        string[] hintNames = [.. result.Results[0].GeneratedSources.Select(s => s.HintName)];
        Assert.Single(hintNames, n => n.EndsWith(".Common.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void CombinedKind_SoaCreateReturnsEntityBecauseItHasNoFacade()
    {
        Assembly assembly = GenerateEntityGeneratorTestHelper.CompileAndLoad(Source);

        Type aosStore = assembly.GetType("Sample.ParticleAosStore")!;
        Type soaStore = assembly.GetType("Sample.ParticleSoaStore")!;
        Type particle = assembly.GetType("Sample.Particle")!;
        Type entity = typeof(CSharpGameEngine.Ecs.Entity);

        Assert.Equal(particle, aosStore.GetMethod("Create")!.ReturnType);
        Assert.Equal(entity, soaStore.GetMethod("Create")!.ReturnType);
    }

    [Fact]
    public void CombinedKind_BothStoresExposePublicRefAccessorsForSingleEntityAccess()
    {
        Assembly assembly = GenerateEntityGeneratorTestHelper.CompileAndLoad(Source);

        Type aosStore = assembly.GetType("Sample.ParticleAosStore")!;
        Type soaStore = assembly.GetType("Sample.ParticleSoaStore")!;

        foreach (string accessor in new[] { "GetTransformRef", "GetVelocityRef", "GetHealthRef" })
        {
            MethodInfo? aosMethod = aosStore.GetMethod(accessor, BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? soaMethod = soaStore.GetMethod(accessor, BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(aosMethod);
            Assert.NotNull(soaMethod);
            Assert.True(aosMethod!.ReturnType.IsByRef, $"{accessor} must return by ref (AoS).");
            Assert.True(soaMethod!.ReturnType.IsByRef, $"{accessor} must return by ref (SoA).");
        }
    }

    [Fact]
    public void CombinedKind_GeneratedSourceContainsNoRuntimeStorageKindBranch()
    {
        GeneratorDriverRunResult result = GenerateEntityGeneratorTestHelper.RunGenerator(Source);
        string allText = string.Join("\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

        Assert.DoesNotContain("EntityStorageKind", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("switch", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleKind_StillUsesUnsuffixedTypeNames()
    {
        string aosOnly = GenerateEntityGeneratorTestHelper.CommonComponents
            + GenerateEntityGeneratorTestHelper.EntityGenerationSettings("Aos")
            + ParticleSource;

        Assembly assembly = GenerateEntityGeneratorTestHelper.CompileAndLoad(aosOnly);

        // 単一指定時の型名は従来どおり接尾辞なし(後方互換)。
        Assert.NotNull(assembly.GetType("Sample.ParticleStore"));
        Assert.Null(assembly.GetType("Sample.ParticleAosStore"));
        Assert.Null(assembly.GetType("Sample.ParticleSoaStore"));
    }
}
