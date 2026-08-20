using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// <see cref="SceneDeclarationAnalyzer"/>を純粋関数として叩くためのコンパイル構築ヘルパー。
///
/// <para>
/// Scene DSLの型(<c>IScene</c>/<c>Node</c>/<c>[SceneFactory]</c>/<c>[SceneAssets]</c>)は<b>実際に出荷される
/// ソースファイル</b>をテキストとして読み込む(<c>TestAssets</c>)。宣言をテスト内へ書き写すと、
/// 出荷物との間に静かなドリフトが生まれるため。
/// </para>
///
/// <para>
/// このファイルは<c>Shared</c>配下に置き、Phase 8で<c>CSharpGameEngine.VSExtension.Tests</c>(net472)からも
/// リンクする。<b>同一の入力から同一のSceneModelが得られること</b>を2つのホストで検証するのが、
/// 共有ソース方式の最大の見返りである。
/// </para>
/// </summary>
internal static class SceneAnalysisTestHelper {
    /// <summary>
    /// 実プロジェクトでは<c>ImplicitUsings=enable</c>でSDKが<c>GlobalUsings.g.cs</c>を生成するが、
    /// 生の<see cref="CSharpCompilation"/>はそれを経由しないため明示的に補う。
    /// </summary>
    private const string ImplicitUsingsSource="global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\n";

    /// <summary>
    /// <c>init</c>アクセサーが要求する型。netstandard2.0/net472のBCLには存在しないため、
    /// コンパイルに含まれていない場合だけ補う(net10.0では既存の型と衝突するので足さない)。
    /// </summary>
    private const string IsExternalInitSource="namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }";

    /// <summary>
    /// <c>Parent.cs</c>がフィールドとして参照する<c>Entity</c>の最小スタブ。ここでの関心事は
    /// 「<c>Parent</c>がstructとして解決できること」(SCENE115の検証)だけで、Scene解析はEntityの
    /// 中身を一切見ないため、Runtime.cs全体(World/Archetypeの生成が要る)を持ち込まない。
    /// </summary>
    private const string EntityStubSource="namespace CSharpGameEngine.Entities;\npublic struct Entity { public int Id; }\n";

    internal static readonly string SceneInterfaceSource=ReadTestAsset("Entities","IScene.cs");

    internal static readonly string NodeSource=ReadTestAsset("Entities","Node.cs");

    internal static readonly string ParentSource=ReadTestAsset("Entities","Parent.cs");

    internal static readonly string SceneNodeTagSource=ReadTestAsset("Entities","SceneNodeTag.cs");

    internal static readonly string RuntimeComponentSource=ReadTestAsset("Entities","IRuntimeComponent.cs");

    internal static readonly string StartupSceneAttributeSource=ReadTestAsset("Entities","StartupSceneAttribute.cs");

    internal static readonly string SceneFactoryAttributeSource=ReadTestAsset("Core","SceneFactoryAttribute.cs");

    internal static readonly string SceneAssetsAttributeSource=ReadTestAsset("Core","SceneAssetsAttribute.cs");

    /// <summary>テストでよく使うComponent一式。Scene DSLの検証対象はComponentの中身ではなく宣言の形なので、素朴な値型で足りる。</summary>
    internal const string CommonComponents="""
        namespace Sample;
        using System.Numerics;
        public struct Transform { public Vector3 Position; public Vector3 Scale; }
        public struct PrimitiveMesh { public PrimitiveType Type; }
        public struct MaterialRef { public MaterialAsset Material; }
        public struct DirectionalLight { public float Intensity; }
        public struct SpotLight { public float Intensity; public float InnerConeAngleRadians; }
        public struct Label { public string? Text; }
        public struct Flags { public bool[]? Values; }
        public enum PrimitiveType { Cube, Sphere, Plane }
        public readonly struct MaterialAsset { public MaterialAsset(string path) { this.Path = path; } public string Path { get; } }
        public sealed class NotAComponent { }
        """;

    private static string ReadTestAsset(string Area,string FileName)=>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestAssets",Area,FileName));

    /// <summary>
    /// メタデータ参照はプロセス内で不変なので一度だけ作る。<see cref="CreateCompilation"/>のたびに
    /// <c>CreateFromFile</c>し直すと数百アセンブリ分のメタデータをディスクから読み直すことになり、
    /// 1テストで数百回コンパイルするresilience fuzzがスイート全体の実行時間を支配してしまう。
    /// </summary>
    private static readonly MetadataReference[]References=[
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(static Assembly=>!Assembly.IsDynamic&&!string.IsNullOrEmpty(Assembly.Location))
            .Select(static Assembly=>MetadataReference.CreateFromFile(Assembly.Location)),
    ];

    private static readonly CSharpParseOptions ParseOptions=new(LanguageVersion.Latest);

    /// <summary>Scene DSLの土台となる型群。毎回同じ文字列をパースし直さないよう構文ツリーを使い回す。</summary>
    private static readonly SyntaxTree[]FixtureTrees=[
        .. new[]{
            ImplicitUsingsSource,
            EntityStubSource,
            SceneInterfaceSource,
            NodeSource,
            ParentSource,
            SceneNodeTagSource,
            RuntimeComponentSource,
            StartupSceneAttributeSource,
            SceneFactoryAttributeSource,
            SceneAssetsAttributeSource,
        }.Select(static Source=>CSharpSyntaxTree.ParseText(Source,ParseOptions)),
    ];

    internal static CSharpCompilation CreateCompilation(params string[]ExtraSources){
        var Compilation=CSharpCompilation.Create(
                                                 "SceneModelTestAssembly",
                                                 [.. FixtureTrees,.. ExtraSources.Select(static Source=>CSharpSyntaxTree.ParseText(Source,ParseOptions))],
                                                 References,
                                                 new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // net472ホストではBCLがIsExternalInitを提供しないため、Node.csのinitアクセサーが解決できない。
        if(Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.IsExternalInit") is null){
            Compilation=Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(IsExternalInitSource,ParseOptions));
        }

        return Compilation;
    }

    /// <summary>ユーザーソース1本を解析する。テストの大半はこれだけで足りる。</summary>
    internal static SceneModel Analyze(string SceneSource,string?SceneClassName=null){
        return Analyze([CommonComponents,SceneSource],SceneClassName);
    }

    internal static SceneModel Analyze(string[]Sources,string?SceneClassName=null){
        var Compilation=CreateCompilation(Sources);
        var SceneClass=FindSceneClass(Compilation,SceneClassName);

        Assert.True(SceneClass is not null, $"No IScene implementation named '{SceneClassName??"(any)"}' was found in the test compilation.");

        return SceneDeclarationAnalyzer.Analyze(SceneClass!,Compilation,CancellationToken.None);
    }

    /// <summary>
    /// <c>IScene</c>を実装する型を探す。名前を省略した場合は最初に見つかった1件を返す。
    /// SCENE116(入れ子)の検証があるため、入れ子型も走査対象に含める。
    /// </summary>
    internal static INamedTypeSymbol?FindSceneClass(CSharpCompilation Compilation,string?SceneClassName){
        foreach(var Type in EnumerateTypes(Compilation.GlobalNamespace)){
            if(SceneClassName is not null&&Type.Name!=SceneClassName){
                continue;
            }

            if(SceneTypePredicates.ImplementsSceneInterface(Type)){
                return Type;
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateTypes(INamespaceOrTypeSymbol Container){
        foreach(var Member in Container.GetMembers()){
            switch(Member){
                case INamespaceSymbol Namespace:
                    foreach(var Nested in EnumerateTypes(Namespace)){
                        yield return Nested;
                    }

                    break;
                case INamedTypeSymbol Type:
                    yield return Type;

                    foreach(var Nested in EnumerateTypes(Type)){
                        yield return Nested;
                    }

                    break;
            }
        }
    }

    internal static string[]DiagnosticIds(SceneModel Model)=>[.. Model.Diagnostics.Select(static D=>D.Id).OrderBy(static Id=>Id,StringComparer.Ordinal)];

    internal static void AssertNoDiagnostics(SceneModel Model){
        Assert.True(
            Model.Diagnostics.IsEmpty,
            "Expected no Scene DSL diagnostics but got:\n"+string.Join("\n",Model.Diagnostics.Select(static D=>D.ToString())));
    }

    internal static void AssertSingleDiagnostic(SceneModel Model,string ExpectedId){
        Assert.True(
            Model.Diagnostics.Length==1&&Model.Diagnostics[0].Id==ExpectedId,
            $"Expected exactly one '{ExpectedId}' but got:\n"+string.Join("\n",Model.Diagnostics.Select(static D=>D.ToString())));
    }

    internal static void AssertContainsDiagnostic(SceneModel Model,string ExpectedId){
        Assert.True(
            Model.Diagnostics.Any(D=>D.Id==ExpectedId),
            $"Expected a '{ExpectedId}' but got:\n"+string.Join("\n",Model.Diagnostics.Select(static D=>D.ToString())));
    }

    /// <summary>コンパイル自体のエラー(テストソースの書き間違い)を検出する。診断テストの前提を守るために使う。</summary>
    internal static ImmutableArray<Diagnostic>GetCompilationErrors(params string[]ExtraSources){
        return [.. CreateCompilation(ExtraSources).GetDiagnostics().Where(static D=>D.Severity==DiagnosticSeverity.Error)];
    }
}
