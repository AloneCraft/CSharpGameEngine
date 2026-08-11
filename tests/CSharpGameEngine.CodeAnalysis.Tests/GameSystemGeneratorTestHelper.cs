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

    /// <summary><c>[StartupScene]</c>/<c>[SceneEntity]</c>(<see cref="StartupSceneAnalyzer"/>)を使うテストが追加で必要とする型一式。</summary>
    internal static readonly string[] StartupSceneSources =
    [
        ReadTestAsset("StartupSceneAttribute.cs"),
        ReadTestAsset("SceneEntityAttribute.cs"),
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

    private static string ReadTestAsset(string FileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Entities", FileName));

    /// <summary><see cref="RuntimeSource"/>を常に含めるコンパイルを作る(全コンパイルがWorld/State/ISystemを必要とするため)。</summary>
    internal static CSharpCompilation CreateCompilation(params string[] ExtraSources) =>
        CreateCompilation([.. ExtraSources.Select(static Source => (Path: (string?)null, Source: Source))]);

    /// <summary>
    /// ファイルパスを明示できるオーバーロード。ADR-0036の<c>[SystemOrder]</c>をfileマーカー経由で
    /// 使う場合は<c>SyntaxTree.FilePath</c>をキーにするため、「ファイル名のアルファベット順とは逆の
    /// 実行順序になる」ことを証明するテストには実在するかのようなパスの指定が要る(既定の
    /// <c>ParseText</c>はパス無し = 空文字列になり、複数ファイルを区別できない)。
    /// </summary>
    internal static CSharpCompilation CreateCompilation(IEnumerable<(string? Path, string Source)> ExtraSources)
    {
        List<MetadataReference> References = [];

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            {
                References.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        List<(string? Path, string Source)> Sources =
        [
            (null, ImplicitUsingsSource),
            (null, RuntimeSource),
            (null, BootstrapSource),
            (null, SystemOrderAttributeSource),
            .. ExtraSources,
        ];

        return CSharpCompilation.Create(
            "GameSystemGeneratorTestAssembly",
            [.. Sources.Select(S => CSharpSyntaxTree.ParseText(S.Source, parseOptions, path: S.Path ?? string.Empty))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// 実行可能プロジェクトとしてのコンパイル。エントリポイント生成は<c>OutputKind</c>を見て決まるため、
    /// その分岐を踏むテストはこちらを使う。
    /// </summary>
    internal static CSharpCompilation CreateApplicationCompilation(params string[] ExtraSources) =>
        CreateCompilation(ExtraSources).WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication));

    internal static CSharpCompilation CreateApplicationCompilation(IEnumerable<(string? Path, string Source)> ExtraSources) =>
        CreateCompilation(ExtraSources).WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication));

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver Driver = CSharpGeneratorDriver.Create(new GameSystemGenerator());
        Driver = Driver.RunGenerators(compilation);
        return Driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[] ExtraSources) =>
        RunGenerator(CreateCompilation(ExtraSources));

    internal static GeneratorDriverRunResult RunGenerator(IEnumerable<(string? Path, string Source)> ExtraSources) =>
        RunGenerator(CreateCompilation(ExtraSources));

    /// <summary>生成コードを合流させた最終コンパイルを実際にEmitしてメモリ上に読み込む。E2Eテストがリフレクションで実行結果を検証するために使う。</summary>
    internal static Assembly CompileAndLoad(params string[] ExtraSources) =>
        CompileAndLoad(CreateCompilation(ExtraSources));

    internal static Assembly CompileAndLoad(CSharpCompilation compilation)
    {
        GeneratorDriver Driver = CSharpGeneratorDriver.Create(new GameSystemGenerator());
        Driver = Driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation OutputCompilation, out ImmutableArray<Diagnostic> GeneratorDiagnostics);

        if (!GeneratorDiagnostics.IsEmpty)
        {
            throw new InvalidOperationException("Generator diagnostics:\n" + string.Join("\n", GeneratorDiagnostics));
        }

        using MemoryStream stream = new();
        EmitResult emitResult = OutputCompilation.Emit(stream);

        if (!emitResult.Success)
        {
            IEnumerable<Diagnostic> Errors = emitResult.Diagnostics.Where(D => D.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException("Compilation failed:\n" + string.Join("\n", Errors));
        }

        return Assembly.Load(stream.ToArray());
    }

    /// <summary>この設計の型は<c>internal</c>基調なので、Non-publicも検索対象に含める。</summary>
    internal static object? Invoke(Assembly assembly, string TypeFullName, string MethodName, params object?[]? Args)
    {
        Type type = assembly.GetType(TypeFullName) ?? throw new InvalidOperationException($"Type '{TypeFullName}' not found.");
        MethodInfo Method = type.GetMethod(MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{MethodName}' not found on '{TypeFullName}'.");
        return Method.Invoke(null, Args);
    }

    internal static string GeneratedText(GeneratorDriverRunResult Result, string HintName) =>
        Result.Results[0].GeneratedSources.Single(S => S.HintName == HintName).SourceText.ToString();
}
