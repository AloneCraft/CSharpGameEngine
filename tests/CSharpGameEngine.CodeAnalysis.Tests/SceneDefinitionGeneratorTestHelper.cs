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
internal static class SceneDefinitionGeneratorTestHelper {
    private const string ImplicitUsingsSource="global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\n";

    /// <summary>テストでよく使う共通コンポーネント一式。個々のテストのソースに連結して使う。</summary>
    internal const string CommonComponents="""
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

    internal static CSharpCompilation CreateCompilation(params string[]Sources)=>
        CreateCompilation(LanguageVersion.Latest,Sources);

    internal static CSharpCompilation CreateCompilation(LanguageVersion languageVersion,params string[]Sources){
        CSharpParseOptions parseOptions=new(languageVersion);
        List<string>AllSources=[
            ImplicitUsingsSource,
            GameSystemGeneratorTestHelper.RuntimeSource,
            // Runtime.csのWorldがInstantiateScene/UnloadSceneを持つため、Scene関連の型は必須になった。
            .. GameSystemGeneratorTestHelper.SceneRuntimeSources,
            .. GameSystemGeneratorTestHelper.SceneAuthoringSources,
            .. Sources,
        ];

        return CSharpCompilation.Create(
            "SceneDefinitionGeneratorTestAssembly",
            [.. AllSources.Select(Source=>CSharpSyntaxTree.ParseText(Source,parseOptions))],
            GameSystemGeneratorTestHelper.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation){
        GeneratorDriver Driver=CSharpGeneratorDriver.Create(new GameSystemGenerator());
        Driver=Driver.RunGenerators(compilation);
        return Driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunGenerator(params string[]Sources)=>
        RunGenerator(CreateCompilation(Sources));

    /// <summary>生成コードを合流させた最終コンパイルを実際にEmitしてメモリ上に読み込む。E2Eテストがリフレクションで実行結果を検証するために使う。</summary>
    internal static Assembly CompileAndLoad(params string[]Sources){
        CSharpCompilation compilation=CreateCompilation(Sources);
        GeneratorDriver Driver=CSharpGeneratorDriver.Create(new GameSystemGenerator());
        Driver=Driver.RunGeneratorsAndUpdateCompilation(compilation,out Compilation OutputCompilation,out ImmutableArray<Diagnostic>GeneratorDiagnostics);

        if(!GeneratorDiagnostics.IsEmpty){
            throw new InvalidOperationException("Generator diagnostics:\n"+string.Join("\n",GeneratorDiagnostics));
        }

        using MemoryStream stream=new();
        EmitResult emitResult=OutputCompilation.Emit(stream);

        if(!emitResult.Success){
            IEnumerable<Diagnostic>Errors=emitResult.Diagnostics.Where(D=>D.Severity==DiagnosticSeverity.Error);
            throw new InvalidOperationException("Compilation failed:\n"+string.Join("\n",Errors));
        }

        return Assembly.Load(stream.ToArray());
    }

    internal static object?Invoke(Assembly assembly,string TypeFullName,string MethodName,params object?[]?Args){
        Type type=assembly.GetType(TypeFullName)??throw new InvalidOperationException($"Type '{TypeFullName}' not found.");
        MethodInfo Method=type.GetMethod(MethodName,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
            ??throw new InvalidOperationException($"Method '{MethodName}' not found on '{TypeFullName}'.");
        return Method.Invoke(null,Args);
    }
}
