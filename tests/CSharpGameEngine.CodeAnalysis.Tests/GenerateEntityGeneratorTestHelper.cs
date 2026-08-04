using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpGameEngine.CodeAnalysis.Tests;

internal static class GenerateEntityGeneratorTestHelper
{
    /// <summary>
    /// ユーザー側のEntity定義でよく使う共通コンポーネント一式。個々のテストのソースに連結して使う。
    /// </summary>
    internal const string CommonComponents = """
        using CSharpGameEngine.Generation;

        namespace Sample;

        public struct LocalTransform { public float X; public float Y; public float Z; }
        public struct Velocity { public float X; public float Y; public float Z; }
        public struct Health { public int Value; }
        """;

    /// <summary>
    /// <c>EntityGenerationSettings.Kind</c>という、ユーザーが書く唯一の<c>const</c>切り替え点を、
    /// 指定した<see cref="CSharpGameEngine.Generation.EntityStorageKind"/>メンバー名で構築する。
    /// </summary>
    internal static string EntityGenerationSettings(string kindMemberName)
    {
        return $$"""


            public static class EntityGenerationSettings
            {
                public const CSharpGameEngine.Generation.EntityStorageKind Kind = CSharpGameEngine.Generation.EntityStorageKind.{{kindMemberName}};
            }
            """;
    }

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

        references.Add(MetadataReference.CreateFromFile(typeof(CSharpGameEngine.Ecs.World).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(CSharpGameEngine.Generation.GenerateEntityAttribute).Assembly.Location));

        CSharpParseOptions parseOptions = new(languageVersion);

        return CSharpCompilation.Create(
            "GenerateEntityGeneratorTestAssembly",
            [.. sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GenerateEntityGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[] sources)
    {
        return RunGenerator(CreateCompilation(sources));
    }

    /// <summary>
    /// ソースをGenerateEntityGeneratorに通し、生成コードを合流させた最終コンパイルを実際にEmitして、
    /// メモリ上に読み込む。Runtimeテストがリフレクションで実行結果を検証するために使う。
    /// </summary>
    internal static Assembly CompileAndLoad(params string[] sources)
    {
        CSharpCompilation compilation = CreateCompilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GenerateEntityGenerator());
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
