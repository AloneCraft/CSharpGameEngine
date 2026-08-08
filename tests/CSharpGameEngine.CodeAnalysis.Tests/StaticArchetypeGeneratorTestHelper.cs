using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpGameEngine.CodeAnalysis.Tests;

internal static class StaticArchetypeGeneratorTestHelper
{
    /// <summary>正本の<c>Runtime.cs</c>(src/CSharpGameEngine.StaticArchetype)。実際に使われている内容とテストのドリフトを防ぐため、埋め込み文字列ではなくファイルから読む。</summary>
    internal static readonly string RuntimeSource = ReadTestAsset("Runtime.cs");

    /// <summary>昇格済みサンプルの<c>Components.cs</c>(samples/StaticArchetypeSystemsDemo)。</summary>
    internal static readonly string PromotedComponentsSource = ReadTestAsset("Components.cs");

    /// <summary>昇格済みサンプルの<c>Systems.cs</c>(samples/StaticArchetypeSystemsDemo)。</summary>
    internal static readonly string PromotedSystemsSource = ReadTestAsset("Systems.cs");

    private static string ReadTestAsset(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "StaticArchetype", fileName);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 実プロジェクトでは<c>Directory.Build.props</c>の<c>ImplicitUsings=enable</c>により
    /// <c>System</c>等が暗黙<c>using</c>されるが(<c>Runtime.cs</c>の<c>ArgumentOutOfRangeException</c>は
    /// これに依存している)、このテストヘルパーは生の<see cref="CSharpCompilation"/>を使うため
    /// MSBuildのSDK生成<c>GlobalUsings.g.cs</c>を経由しない。実プロジェクトと同じ暗黙usingを
    /// 明示的に補うことで、実際の消費側と同じ前提でコンパイルを再現する。
    /// </summary>
    private const string ImplicitUsingsSource = "global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\n";

    /// <summary><see cref="RuntimeSource"/>を常に先頭に含めるコンパイルを作る(全コンパイルがRuntime.csの<c>World</c>/<c>State</c>を必要とするため)。</summary>
    internal static CSharpCompilation CreateCompilation(params string[] extraSources)
    {
        List<MetadataReference> references = [];

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        List<string> sources = [ImplicitUsingsSource, RuntimeSource, .. extraSources];

        return CSharpCompilation.Create(
            "StaticArchetypeGeneratorTestAssembly",
            [.. sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new StaticArchetypeGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[] extraSources)
    {
        return RunGenerator(CreateCompilation(extraSources));
    }

    /// <summary>ソースをStaticArchetypeGeneratorに通し、生成コードを合流させた最終コンパイルを実際にEmitして、メモリ上に読み込む。E2Eテストがリフレクションで実行結果を検証するために使う。</summary>
    internal static Assembly CompileAndLoad(params string[] extraSources)
    {
        CSharpCompilation compilation = CreateCompilation(extraSources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new StaticArchetypeGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        if (!generatorDiagnostics.IsEmpty)
        {
            throw new InvalidOperationException("Generator diagnostics:\n" + string.Join("\n", generatorDiagnostics));
        }

        using MemoryStream stream = new();
        EmitResult emitResult = outputCompilation.Emit(stream);

        if (!emitResult.Success)
        {
            IEnumerable<Diagnostic> errors = emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException("Compilation failed:\n" + string.Join("\n", errors));
        }

        return Assembly.Load(stream.ToArray());
    }

    /// <summary>
    /// このジェネレーターの設計はグローバル名前空間・<c>internal</c>基調(ADR-0032)のため、テスト
    /// シナリオの<c>TestEntry</c>/<c>RunScenario</c>も<c>internal</c>で書く。既定の<c>GetMethod</c>は
    /// publicのみを検索するため、Non-publicも対象に含める。
    /// </summary>
    internal static object? Invoke(Assembly assembly, string typeFullName, string methodName, params object?[]? args)
    {
        Type type = assembly.GetType(typeFullName) ?? throw new InvalidOperationException($"Type '{typeFullName}' not found.");
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found on '{typeFullName}'.");
        return method.Invoke(null, args);
    }
}
