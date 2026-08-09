using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// Scene AuthoringはADR-0033で<see cref="GameSystemGenerator"/>へ統合された(Sceneのノード形状と
/// SystemのUpdate形状が同一のArchetype空間に入る必要があるため)。よってこのヘルパーも
/// <c>SceneDefinitionGenerator</c>ではなく<see cref="GameSystemGenerator"/>を駆動する。
/// 生成が走るには<c>ISystem</c>型がコンパイル内に実在する必要があるため、
/// <see cref="CommonComponents"/>にダミーのSystemを1つ含めてある。
/// </summary>
internal static class SceneDefinitionGeneratorTestHelper
{
    private const string ImplicitUsingsSource = "global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\n";

    /// <summary>テストでよく使う共通コンポーネント一式。個々のテストのソースに連結して使う。</summary>
    internal const string CommonComponents = """
        using CSharpGameEngine.Entities;

        namespace Sample;

        public struct Position { public float X; public float Y; public float Z; }
        public struct Rotation { public float X; public float Y; public float Z; public float W; }
        public struct Scale { public float X; public float Y; public float Z; }
        public struct Camera { public float FieldOfView; }
        public struct PlayerTag { }
        public struct Health { public float Value; }

        // GameSystemGeneratorは`ISystem`型がコンパイル内に無いと何も出力しない(無関係なプロジェクトを
        // 汚染しないためのゲート、ADR-0033)。Scene単体のテストでもこのダミーが必要になる。
        internal struct KeepGeneratorAlive : ISystem
        {
            public bool Update(ref Position position, ref State state) => true;
        }
        """;

    internal static CSharpCompilation CreateCompilation(params string[] sources) =>
        CreateCompilation(LanguageVersion.Latest, sources);

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

        CSharpParseOptions parseOptions = new(languageVersion);
        List<string> allSources =
        [
            ImplicitUsingsSource,
            GameSystemGeneratorTestHelper.RuntimeSource,
            .. GameSystemGeneratorTestHelper.SceneAuthoringSources,
            .. sources,
        ];

        return CSharpCompilation.Create(
            "SceneDefinitionGeneratorTestAssembly",
            [.. allSources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GameSystemGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[] sources) =>
        RunGenerator(CreateCompilation(sources));

    /// <summary>生成コードを合流させた最終コンパイルを実際にEmitしてメモリ上に読み込む。E2Eテストがリフレクションで実行結果を検証するために使う。</summary>
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
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found on '{typeFullName}'.");
        return method.Invoke(null, args);
    }
}
