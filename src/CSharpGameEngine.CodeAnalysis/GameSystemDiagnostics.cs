using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// GameSystemGeneratorが発行する診断の定義。診断IDは<c>SYSGEN###</c>(3桁ゼロ埋め)、カテゴリは
/// <c>CSharpGameEngine.Entities.CodeGen</c>。ルートのTreatWarningsAsErrors=trueによりSeverityに
/// 関わらずビルドは失敗するため、意図を明確にする目的ですべてErrorとして宣言する(既存規約)。
/// ADR-0033の全面移行に伴い、属性ベースだった旧SYSGEN001〜020は破棄し、構造的推論ベースの
/// 新しい規則へ全面的に振り直した。
/// </summary>
internal static class GameSystemDiagnostics
{
    private const string Category = "CSharpGameEngine.Entities.CodeGen";

    internal static readonly DiagnosticDescriptor SystemMustBeStruct = new(
        id: "SYSGEN001",
        title: "ISystem implementor must be a struct",
        messageFormat: "'{0}' implements ISystem but is not declared 'struct'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SystemMustNotBeGeneric = new(
        id: "SYSGEN002",
        title: "ISystem struct must not be generic",
        messageFormat: "'{0}' implements ISystem but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SystemMustNotBeNested = new(
        id: "SYSGEN003",
        title: "ISystem struct must not be nested",
        messageFormat: "'{0}' implements ISystem but is nested inside another type, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MethodMustBeInstance = new(
        id: "SYSGEN004",
        title: "Update/Spawn method must be an instance method",
        messageFormat: "Method '{0}' is declared 'static'; Update/Spawn methods must be instance methods",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MethodMustNotBePrivate = new(
        id: "SYSGEN005",
        title: "Update/Spawn method must not be private",
        messageFormat: "Method '{0}' is 'private', so the generated game loop cannot call it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustReturnBool = new(
        id: "SYSGEN006",
        title: "Update method must return bool",
        messageFormat: "Method '{0}' returns '{1}' instead of bool",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MethodMustNotBeGeneric = new(
        id: "SYSGEN007",
        title: "Update/Spawn method must not be generic",
        messageFormat: "Method '{0}' declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MethodMustHaveAtLeastOneComponentParameter = new(
        id: "SYSGEN008",
        title: "Update/Spawn method must have at least one component parameter",
        messageFormat: "Method '{0}' does not declare any 'ref' component parameter, so no Archetype can be inferred",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // SYSGEN009("Componentパラメータはrefでなければならない")は実装時に到達不能と判明したため欠番。
    // 構造的推論(ADR-0033決定事項3)では「refならComponent、ref以外ならContext」と定義したので、
    // refでないパラメータは壊れたComponentではなく正当なContextになる。渡し方自体が不正な場合
    // (`out`など)はSYSGEN018が捕捉する。ADR-0015でも到達不能と分かった診断を同様に削除している。

    internal static readonly DiagnosticDescriptor ComponentTypeMustBeNonNullableStruct = new(
        id: "SYSGEN010",
        title: "Component type must be a non-nullable struct",
        messageFormat: "Parameter '{0}' of method '{1}' has type '{2}', which is not a non-nullable struct type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateComponentParameter = new(
        id: "SYSGEN011",
        title: "The same component type must not appear twice in one method",
        messageFormat: "Method '{0}' declares component type '{1}' more than once",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor LeadingEntityParameterMustBeByValueFirst = new(
        id: "SYSGEN012",
        title: "A leading Entity parameter must be by-value and the first parameter",
        messageFormat: "Method '{0}': the 'Entity' parameter must be passed by value and be the first parameter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustEndWithRefState = new(
        id: "SYSGEN013",
        title: "Update method must end with a trailing 'ref State' parameter",
        messageFormat: "Method '{0}' does not end with a 'ref State' parameter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor EntityOrStateMustNotBeUsedAsComponent = new(
        id: "SYSGEN014",
        title: "Entity/State must not be used as a component parameter",
        messageFormat: "Parameter '{0}' of method '{1}' uses '{2}', which is reserved and cannot be a component",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SystemHasNoRecognizedMethod = new(
        id: "SYSGEN015",
        title: "ISystem struct declares no Update or Spawn method",
        messageFormat: "'{0}' implements ISystem but declares no method named 'Update' or 'Spawn', so it will never run",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SpawnMethodMustReturnVoid = new(
        id: "SYSGEN016",
        title: "Spawn method must return void",
        messageFormat: "Method '{0}' is named 'Spawn' but returns '{1}'; Spawn only declares an Archetype shape and must return void",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SpawnMethodMustNotDeclareEntityOrState = new(
        id: "SYSGEN017",
        title: "Spawn method must not declare an Entity or State parameter",
        messageFormat: "Method '{0}' is named 'Spawn' but declares an 'Entity'/'State' parameter; Spawn is never dispatched and only takes component parameters",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContextParameterMustBeInOrByValue = new(
        id: "SYSGEN018",
        title: "Context parameter must be 'in' (struct) or by value (reference type)",
        messageFormat: "Parameter '{0}' of method '{1}' is passed by '{2}'; a Context parameter must be 'in' for a struct type or plain by-value for a reference type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateContextParameter = new(
        id: "SYSGEN019",
        title: "The same Context type must not appear twice in one method",
        messageFormat: "Method '{0}' declares Context type '{1}' more than once",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentSimpleNameCollision = new(
        id: "SYSGEN020",
        title: "Two component types must not share a simple name",
        messageFormat: "Component types '{0}' and '{1}' share the simple name '{2}'; generated Archetype fields would collide",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>1つのSystem解析結果が持つ全診断を報告する。生成には関与しない診断専用の出力段。</summary>
    internal static void ReportAll(SourceProductionContext context, GameSystemResult result)
    {
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }
}
