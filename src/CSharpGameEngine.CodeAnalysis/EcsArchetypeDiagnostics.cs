using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// EcsArchetypeGeneratorが発行する診断の定義。診断IDは<c>ECSGEN###</c>(3桁ゼロ埋め)という
/// このリポジトリで新規に確立する規約であり、カテゴリは<c>CSharpGameEngine.Ecs.CodeGen</c>に統一する。
/// ルートのTreatWarningsAsErrors=trueによりSeverityに関わらずビルドは失敗するため、意図を明確にする
/// 目的ですべてErrorとして宣言する。
/// </summary>
internal static class EcsArchetypeDiagnostics
{
    private const string Category = "CSharpGameEngine.Ecs.CodeGen";

    internal static readonly DiagnosticDescriptor ArchetypeMustBePartial = new(
        id: "ECSGEN001",
        title: "EcsArchetype attribute requires a partial struct",
        messageFormat: "'{0}' is annotated with [EcsArchetype] but is not declared 'partial'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ArchetypeMustNotBeGeneric = new(
        id: "ECSGEN002",
        title: "EcsArchetype struct must not be generic",
        messageFormat: "'{0}' is annotated with [EcsArchetype] but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ArchetypeMustNotBeNested = new(
        id: "ECSGEN003",
        title: "EcsArchetype struct must not be nested",
        messageFormat: "'{0}' is annotated with [EcsArchetype] but is nested inside another type, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ArchetypeMustHaveAtLeastOneComponent = new(
        id: "ECSGEN004",
        title: "EcsArchetype struct must declare at least one component property",
        messageFormat: "'{0}' is annotated with [EcsArchetype] but does not declare any partial component property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor PropertyMustReturnByRef = new(
        id: "ECSGEN005",
        title: "Archetype component property must return by ref or ref readonly",
        messageFormat: "Property '{0}' must return its component type by 'ref' or 'ref readonly'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentTypeMustBeNonNullableStruct = new(
        id: "ECSGEN006",
        title: "Archetype component type must be a non-nullable struct",
        messageFormat: "Property '{0}' returns '{1}', which is not a non-nullable struct type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateComponentType = new(
        id: "ECSGEN007",
        title: "Archetype must not use the same component type more than once",
        messageFormat: "'{0}' already declares a component property of type '{1}'; the same component type cannot be used twice in one archetype",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor EcsReadOnlyRequiresRefReadonly = new(
        id: "ECSGEN008",
        title: "[EcsReadOnly] requires a ref readonly property",
        messageFormat: "Property '{0}' is annotated with [EcsReadOnly] but does not return by 'ref readonly'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RefReadonlyRequiresEcsReadOnly = new(
        id: "ECSGEN009",
        title: "A ref readonly property requires [EcsReadOnly]",
        messageFormat: "Property '{0}' returns by 'ref readonly' but is not annotated with [EcsReadOnly]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
