using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpGameEngine.CodeAnalysis.Tests;

internal static class EcsArchetypeGeneratorTestHelper
{
    /// <summary>
    /// ユーザー側のアーキタイプ定義でよく使う共通コンポーネント一式。個々のテストのソースに連結して使う。
    /// </summary>
    internal const string CommonComponents = """
        using CSharpGameEngine.Ecs;

        namespace Sample;

        public struct Position { public float X; public float Y; public float Z; }
        public struct Rotation { public float X; public float Y; public float Z; public float W; }
        public struct Velocity { public float X; public float Y; public float Z; }
        public struct Health { public int Value; }
        public struct Damage { public int Value; }
        public struct PlayerTag { }
        public struct Mesh { public int Value; }
        public struct Material { public int Value; }
        """;

    internal static CSharpCompilation CreateCompilation(params string[] sources)
    {
        List<MetadataReference> references = [];

        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(CSharpGameEngine.Ecs.World).Assembly.Location));

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        return CSharpCompilation.Create(
            "EcsArchetypeGeneratorTestAssembly",
            [.. sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new EcsArchetypeGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[] sources)
    {
        return RunGenerator(CreateCompilation(sources));
    }

    /// <summary>
    /// ソースをEcsArchetypeGeneratorに通し、生成コードを合流させた最終コンパイルを実際にEmitして、
    /// メモリ上に読み込む。end-to-endテストがリフレクションで実行結果を検証するために使う。
    /// </summary>
    internal static Assembly CompileAndLoad(params string[] sources)
    {
        CSharpCompilation compilation = CreateCompilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new EcsArchetypeGenerator());
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
}
