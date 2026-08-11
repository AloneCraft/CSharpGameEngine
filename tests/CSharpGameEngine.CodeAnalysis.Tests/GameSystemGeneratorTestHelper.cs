using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpGameEngine.CodeAnalysis.Tests;

internal static class GameSystemGeneratorTestHelper
{
    /// <summary>
    /// 実プロジェクトでは<c>Directory.Build.props</c>の<c>ImplicitUsings=enable</c>により
    /// <c>System</c>等が暗黙<c>using</c>されるが、このヘルパーは生の<see cref="CSharpCompilation"/>を
    /// 使うためMSBuildのSDK生成<c>GlobalUsings.g.cs</c>を経由しない。実プロジェクトと同じ前提を
    /// 明示的に補う。
    /// </summary>
    private const string ImplicitUsingsSource = "global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\n";

    /// <summary>正本の<c>Runtime.cs</c>(src/CSharpGameEngine.Entities)。テストと実体のドリフトを防ぐためファイルから読む。</summary>
    internal static readonly string RuntimeSource = ReadTestAsset("Runtime.cs");

    /// <summary>生成されたエントリポイントが作るシードEntityのComponent。全コンパイルに含めても、Systemが宣言しない限りArchetypeにはならない。</summary>
    internal static readonly string BootstrapSource = ReadTestAsset("Bootstrap.cs");

    /// <summary>ADR-0036の<c>[SystemOrder]</c>。全コンパイルに含めても、実際に付けなければ何も変わらない。</summary>
    internal static readonly string SystemOrderAttributeSource = ReadTestAsset("SystemOrderAttribute.cs");

    /// <summary>
    /// Scene Authoringの型一式(<c>[SceneDefinition]</c>を含むテストで必要)。各ファイルは
    /// file-scoped namespaceを持つため、1つの文字列へ連結せず個別の構文ツリーとして渡す。
    /// </summary>
    internal static readonly string[] SceneAuthoringSources =
    [
        ReadTestAsset("Parent.cs"),
        ReadTestAsset("SceneNodeTag.cs"),
        ReadTestAsset("SceneNodeId.cs"),
        ReadTestAsset("SceneNodeSymbol.cs"),
        ReadTestAsset("SceneBuilder.cs"),
        ReadTestAsset("SceneNodeBuilder.cs"),
        ReadTestAsset("SceneDefinitionAttribute.cs"),
    ];

    /// <summary>テストでよく使う共通コンポーネント一式。個々のテストのソースに連結して使う。</summary>
    internal const string CommonComponents = """
        namespace Sample;

        public struct Position { public float X; public float Y; public float Z; }
        public struct Velocity { public float X; public float Y; public float Z; }
        public struct Health { public int Value; }
        public struct PlayerTag { }
        public struct MovableTag { }
        public readonly struct FrameContext
        {
            public FrameContext(float deltaTime) { this.DeltaTime = deltaTime; }
            public float DeltaTime { get; }
        }
        public sealed class Log { public System.Collections.Generic.List<string> Entries = new(); }
        """;

    private static string ReadTestAsset(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Entities", fileName));

    /// <summary><see cref="RuntimeSource"/>を常に含めるコンパイルを作る(全コンパイルがWorld/State/ISystemを必要とするため)。</summary>
    internal static CSharpCompilation CreateCompilation(params string[] extraSources) =>
        CreateCompilation([.. extraSources.Select(static source => (Path: (string?)null, Source: source))]);

    /// <summary>
    /// ファイルパスを明示できるオーバーロード。ADR-0036の<c>[SystemOrder]</c>をfileマーカー経由で
    /// 使う場合は<c>SyntaxTree.FilePath</c>をキーにするため、「ファイル名のアルファベット順とは逆の
    /// 実行順序になる」ことを証明するテストには実在するかのようなパスの指定が要る(既定の
    /// <c>ParseText</c>はパス無し = 空文字列になり、複数ファイルを区別できない)。
    /// </summary>
    internal static CSharpCompilation CreateCompilation(IEnumerable<(string? Path, string Source)> extraSources)
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
        List<(string? Path, string Source)> sources =
        [
            (null, ImplicitUsingsSource),
            (null, RuntimeSource),
            (null, BootstrapSource),
            (null, SystemOrderAttributeSource),
            .. extraSources,
        ];

        return CSharpCompilation.Create(
            "GameSystemGeneratorTestAssembly",
            [.. sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, parseOptions, path: s.Path ?? string.Empty))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// 実行可能プロジェクトとしてのコンパイル。エントリポイント生成は<c>OutputKind</c>を見て決まるため、
    /// その分岐を踏むテストはこちらを使う。
    /// </summary>
    internal static CSharpCompilation CreateApplicationCompilation(params string[] extraSources) =>
        CreateCompilation(extraSources).WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication));

    internal static CSharpCompilation CreateApplicationCompilation(IEnumerable<(string? Path, string Source)> extraSources) =>
        CreateCompilation(extraSources).WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication));

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GameSystemGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[] extraSources) =>
        RunGenerator(CreateCompilation(extraSources));

    internal static GeneratorDriverRunResult RunGenerator(IEnumerable<(string? Path, string Source)> extraSources) =>
        RunGenerator(CreateCompilation(extraSources));

    /// <summary>生成コードを合流させた最終コンパイルを実際にEmitしてメモリ上に読み込む。E2Eテストがリフレクションで実行結果を検証するために使う。</summary>
    internal static Assembly CompileAndLoad(params string[] extraSources) =>
        CompileAndLoad(CreateCompilation(extraSources));

    internal static Assembly CompileAndLoad(CSharpCompilation compilation)
    {
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

    /// <summary>この設計の型は<c>internal</c>基調なので、Non-publicも検索対象に含める。</summary>
    internal static object? Invoke(Assembly assembly, string typeFullName, string methodName, params object?[]? args)
    {
        Type type = assembly.GetType(typeFullName) ?? throw new InvalidOperationException($"Type '{typeFullName}' not found.");
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found on '{typeFullName}'.");
        return method.Invoke(null, args);
    }

    internal static string GeneratedText(GeneratorDriverRunResult result, string hintName) =>
        result.Results[0].GeneratedSources.Single(s => s.HintName == hintName).SourceText.ToString();
}
