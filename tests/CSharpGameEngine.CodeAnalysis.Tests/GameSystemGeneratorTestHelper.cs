using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpGameEngine.CodeAnalysis.Tests;

internal static class GameSystemGeneratorTestHelper
{
    /// <summary>
    /// テストでよく使う共通コンポーネント一式。個々のテストのソースに連結して使う。
    /// </summary>
    internal const string CommonComponents = """
        using CSharpGameEngine.Entities;

        namespace Sample;

        public struct Position { public float X; public float Y; public float Z; }
        public struct Velocity { public float X; public float Y; public float Z; }
        public struct Health { public int Value; }
        public readonly struct DisabledTag { }
        public readonly struct MovableTag { }
        """;

    internal static CSharpCompilation CreateCompilation(params string[] sources)
    {
        return CreateCompilation(LanguageVersion.Latest, sources);
    }

    internal static CSharpCompilation CreateCompilation(LanguageVersion languageVersion, params string[] sources)
    {
        List<MetadataReference> references = [];

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(CSharpGameEngine.Entities.World).Assembly.Location));

        CSharpParseOptions parseOptions = new(languageVersion);

        return CSharpCompilation.Create(
            "GameSystemGeneratorTestAssembly",
            [.. sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GameSystemGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[] sources)
    {
        return RunGenerator(CreateCompilation(sources));
    }

    /// <summary>
    /// ソースをGameSystemGeneratorに通し、生成コードを合流させた最終コンパイルを実際にEmitして、
    /// メモリ上に読み込む。E2Eテストがリフレクションで実行結果を検証するために使う。
    /// </summary>
    internal static Assembly CompileAndLoad(params string[] sources)
    {
        CSharpCompilation compilation = CreateCompilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GameSystemGenerator());
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

    internal static object? Invoke(Assembly assembly, string typeFullName, string methodName, params object?[]? args)
    {
        Type type = assembly.GetType(typeFullName) ?? throw new InvalidOperationException($"Type '{typeFullName}' not found.");
        MethodInfo method = type.GetMethod(methodName) ?? throw new InvalidOperationException($"Method '{methodName}' not found on '{typeFullName}'.");
        return method.Invoke(null, args);
    }
}
