using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// StaticArchetypeGeneratorが発行する診断の定義。診断IDは<c>SARCH###</c>(3桁ゼロ埋め)、カテゴリは
/// <c>CSharpGameEngine.StaticArchetype.CodeGen</c>(GameSystemGenerator/SceneDefinitionGeneratorが使う
/// <c>CSharpGameEngine.Entities.CodeGen</c>とは無関係な新規カテゴリ - この設計はCSharpGameEngine.Entities
/// と一切関係が無いため、既存カテゴリを流用すると誤解を招く)。ルートのTreatWarningsAsErrors=trueにより
/// Severityに関わらずビルドは失敗するため、意図を明確にする目的ですべてErrorとして宣言する
/// (SYSGEN/ECSGEN/ENTGENと同じ既存規約)。
/// </summary>
internal static class StaticArchetypeDiagnostics
{
    private const string Category = "CSharpGameEngine.StaticArchetype.CodeGen";

    internal static readonly DiagnosticDescriptor SystemMustBeStruct = new(
        id: "SARCH001",
        title: "ISystem implementor must be a struct",
        messageFormat: "'{0}' implements ISystem but is not declared 'struct'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SystemMustNotBeGeneric = new(
        id: "SARCH002",
        title: "ISystem struct must not be generic",
        messageFormat: "'{0}' implements ISystem but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SystemMustNotBeNested = new(
        id: "SARCH003",
        title: "ISystem struct must not be nested",
        messageFormat: "'{0}' implements ISystem but is nested inside another type, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustBeInstance = new(
        id: "SARCH004",
        title: "Update method must be an instance method",
        messageFormat: "Method '{0}' is named 'Update' but is declared 'static'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustNotBePrivate = new(
        id: "SARCH005",
        title: "Update method must not be private",
        messageFormat: "Method '{0}' is named 'Update' but is 'private', so the generated main loop cannot call it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustReturnBool = new(
        id: "SARCH006",
        title: "Update method must return bool",
        messageFormat: "Method '{0}' is named 'Update' but returns '{1}' instead of bool",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustNotBeGeneric = new(
        id: "SARCH007",
        title: "Update method must not be generic",
        messageFormat: "Method '{0}' is named 'Update' but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustHaveAtLeastOneComponentParameter = new(
        id: "SARCH008",
        title: "Update method must have at least one component parameter",
        messageFormat: "Method '{0}' does not declare any 'ref' component parameter, so no Archetype can be inferred",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentParameterMustBeRef = new(
        id: "SARCH009",
        title: "Component parameter must be 'ref'",
        messageFormat: "Parameter '{0}' of method '{1}' must be passed by 'ref', not by value/'in'/'out'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentTypeMustBeNonNullableStruct = new(
        id: "SARCH010",
        title: "Component type must be a non-nullable struct",
        messageFormat: "Parameter '{0}' of method '{1}' has type '{2}', which is not a non-nullable struct type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateComponentParameter = new(
        id: "SARCH011",
        title: "The same component type must not appear twice in one Update overload",
        messageFormat: "Method '{0}' declares component type '{1}' more than once",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor LeadingEntityParameterMustBeByValueFirst = new(
        id: "SARCH012",
        title: "A leading Entity parameter must be by-value and the first parameter",
        messageFormat: "Method '{0}': the 'Entity' parameter must be passed by value and be the first parameter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustEndWithRefState = new(
        id: "SARCH013",
        title: "Update method must end with a trailing 'ref State' parameter",
        messageFormat: "Method '{0}' does not end with a 'ref State' parameter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor EntityOrStateMustNotBeUsedAsComponent = new(
        id: "SARCH014",
        title: "Entity/State must not be used as a component parameter",
        messageFormat: "Parameter '{0}' of method '{1}' uses '{2}', which is reserved and cannot be a component",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SystemHasNoRecognizedUpdateOverload = new(
        id: "SARCH015",
        title: "ISystem struct declares no Update method",
        messageFormat: "'{0}' implements ISystem but declares no method named 'Update', so it will never run",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>1つのSystem解析結果が持つ全診断を報告する。GameSystemExecutorEmitterと同じく、生成には関与しない診断専用の出力段。</summary>
    internal static void ReportAll(SourceProductionContext context, StaticArchetypeSystemResult result)
    {
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }
}
