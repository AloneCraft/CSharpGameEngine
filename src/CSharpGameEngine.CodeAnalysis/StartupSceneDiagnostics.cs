using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[StartupScene]</c>/<c>[SceneEntity]</c>(<see cref="StartupSceneAnalyzer"/>)が発行する診断の定義。
/// 診断IDは<c>STARTUP###</c>(3桁ゼロ埋め)、カテゴリは<see cref="GameSystemDiagnostics"/>/
/// <see cref="SceneDefinitionDiagnostics"/>と同じ<c>CSharpGameEngine.Entities.CodeGen</c>に統一する。
/// ルートのTreatWarningsAsErrors=trueによりSeverityに関わらずビルドは失敗するため、意図を明確にする
/// 目的ですべてErrorとして宣言する(既存規約)。
///
/// <para>
/// 以下は言語仕様またはAttributeUsageで既に到達不能なため、専用の診断を設けない:
/// <list type="bullet">
/// <item><c>[SceneEntity]</c>がプロパティ以外(フィールド/メソッド等)に付く — <c>AttributeTargets.Property</c>
/// によりコンパイラ自身がCS0592で弾く。</item>
/// <item>同じ<c>[StartupScene]</c>クラス内でEntity論理名(プロパティ名)が衝突する — C#が同名プロパティの
/// 重複宣言自体を許さない。</item>
/// <item>生成型名/Hint Nameの衝突 — 合成Systemの型名は元クラスの完全修飾名から1:1に導出するため、
/// 元クラスのFQNが一意である以上、構造的に衝突し得ない。</item>
/// </list>
/// </para>
/// </summary>
internal static class StartupSceneDiagnostics
{
    private const string Category = "CSharpGameEngine.Entities.CodeGen";

    internal static readonly DiagnosticDescriptor MustBeStaticPartialClass = new(
        id: "STARTUP001",
        title: "[StartupScene] requires a static partial class",
        messageFormat: "'{0}' is annotated with [StartupScene] but is not declared 'static partial class'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MustNotBeGeneric = new(
        id: "STARTUP002",
        title: "[StartupScene] class must not be generic",
        messageFormat: "'{0}' is annotated with [StartupScene] but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SceneEntityPropertyMustBeStatic = new(
        id: "STARTUP003",
        title: "[SceneEntity] property must be static",
        messageFormat: "Property '{0}' is annotated with [SceneEntity] but is not declared 'static'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SceneEntityMustNotBeIndexer = new(
        id: "STARTUP004",
        title: "[SceneEntity] must not be applied to an indexer",
        messageFormat: "'{0}' is an indexer annotated with [SceneEntity]; a Scene Entity must be a simple property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SceneEntityMustHaveGetter = new(
        id: "STARTUP005",
        title: "[SceneEntity] property must have a getter",
        messageFormat: "Property '{0}' is annotated with [SceneEntity] but has no 'get' accessor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SceneEntityMustNotReturnByRef = new(
        id: "STARTUP006",
        title: "[SceneEntity] property must not return by reference",
        messageFormat: "Property '{0}' is annotated with [SceneEntity] but returns by 'ref'/'ref readonly', which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SceneEntityReturnTypeInvalid = new(
        id: "STARTUP007",
        title: "[SceneEntity] property must return a non-nullable struct or a ValueTuple of non-nullable structs",
        messageFormat: "Property '{0}' has type '{1}', which is neither a non-nullable struct nor a ValueTuple of non-nullable structs",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TupleElementMustBeNonNullableStruct = new(
        id: "STARTUP008",
        title: "Every tuple element of a [SceneEntity] property must be a non-nullable struct",
        messageFormat: "Property '{0}': tuple element '{1}' has type '{2}', which is not a non-nullable struct type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateComponentTypeInTuple = new(
        id: "STARTUP009",
        title: "The same component type must not appear twice in one [SceneEntity] tuple",
        messageFormat: "Property '{0}' declares component type '{1}' more than once",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TupleElementMustBeNamed = new(
        id: "STARTUP010",
        title: "Every tuple element of a [SceneEntity] property must have an explicit name",
        messageFormat: "Property '{0}': tuple element {1} has no explicit name; name every element (for example 'ItemN') so the generated code can access it by name",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>1つのStartup Scene解析結果が持つ全診断を報告する。生成には関与しない診断専用の出力段。</summary>
    internal static void ReportAll(SourceProductionContext Context, StartupSceneClassResult Result)
    {
        foreach (Diagnostic diagnostic in Result.Diagnostics)
        {
            Context.ReportDiagnostic(diagnostic);
        }
    }
}
