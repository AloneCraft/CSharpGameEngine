using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// GenerateEntityGeneratorが発行する診断の定義。診断IDは<c>ENTGEN###</c>(3桁ゼロ埋め)とし、
/// 既存の<c>ECSGEN###</c>(EcsArchetypeGenerator)とは衝突しない別プレフィックスを用いる。
/// カテゴリは<c>CSharpGameEngine.EntityGeneration.CodeGen</c>に統一する。
/// ルートのTreatWarningsAsErrors=trueによりSeverityに関わらずビルドは失敗するため、意図を明確にする
/// 目的ですべてErrorとして宣言する。
/// </summary>
internal static class GenerateEntityDiagnostics
{
    private const string Category = "CSharpGameEngine.EntityGeneration.CodeGen";

    internal static readonly DiagnosticDescriptor MustBePartialStruct = new(
        id: "ENTGEN001",
        title: "GenerateEntity attribute requires a partial struct",
        messageFormat: "'{0}' is annotated with [GenerateEntity] but is not declared 'partial'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MustNotBeGeneric = new(
        id: "ENTGEN002",
        title: "GenerateEntity struct must not be generic",
        messageFormat: "'{0}' is annotated with [GenerateEntity] but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        id: "ENTGEN003",
        title: "A nested GenerateEntity struct requires every containing type to be partial",
        messageFormat: "'{0}' is nested inside a type that is not declared 'partial'; every containing type of a nested [GenerateEntity] struct must be 'partial'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MustHaveAtLeastOneComponent = new(
        id: "ENTGEN004",
        title: "GenerateEntity struct must declare at least one component property",
        messageFormat: "'{0}' is annotated with [GenerateEntity] but does not declare any recognized component property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentPropertyMustBePartial = new(
        id: "ENTGEN005",
        title: "Component property must be partial",
        messageFormat: "'{0}.{1}' has 'get' and 'set' accessors but is not declared 'partial'; GenerateEntity only recognizes 'partial' properties as components",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentPropertyMustNotBeStatic = new(
        id: "ENTGEN006",
        title: "Component property must not be static",
        messageFormat: "'{0}.{1}' is a 'partial' property but is declared 'static', which is not supported as a component",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentPropertyMustNotBeIndexer = new(
        id: "ENTGEN007",
        title: "Component property must not be an indexer",
        messageFormat: "'{0}.{1}' is a 'partial' indexer, which is not supported as a component",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentPropertyMustHaveGetAndSet = new(
        id: "ENTGEN008",
        title: "Component property must declare both get and set",
        messageFormat: "'{0}.{1}' is a 'partial' property but does not declare both 'get' and 'set'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentPropertyMustNotBeInitOnly = new(
        id: "ENTGEN009",
        title: "Component property must not be init-only",
        messageFormat: "'{0}.{1}' has an 'init' setter, which is not supported as a component; use 'set' instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentTypeMustBeUnmanagedStruct = new(
        id: "ENTGEN010",
        title: "Component type must be a non-nullable unmanaged struct",
        messageFormat: "Property '{0}' returns '{1}', which is not a non-nullable unmanaged struct type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateComponentType = new(
        id: "ENTGEN011",
        title: "Entity must not use the same component type more than once",
        messageFormat: "'{0}' already declares a component property of type '{1}'; the same component type cannot be used twice on one entity",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor GeneratedMemberNameCollision = new(
        id: "ENTGEN012",
        title: "Member name collides with a generated member",
        messageFormat: "'{0}' declares a member named '{1}', which collides with a member GenerateEntity must generate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor GeneratedTypeNameCollision = new(
        id: "ENTGEN013",
        title: "Type name collides with a generated type",
        messageFormat: "'{0}' requires a generated type named '{1}', but a type with that name is already declared in the same scope",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor StorageKindMustBeResolvableConstant = new(
        id: "ENTGEN014",
        title: "GenerateEntity storage kind must resolve to a defined constant",
        messageFormat: "'{0}' is annotated with [GenerateEntity] but its storage kind argument does not resolve to a defined EntityStorageKind constant",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RequiresPartialPropertiesLanguageVersion = new(
        id: "ENTGEN015",
        title: "GenerateEntity requires a C# version that supports partial properties",
        messageFormat: "'{0}' is annotated with [GenerateEntity] but the compilation's C# language version does not support partial properties (requires C# 13 or later)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
