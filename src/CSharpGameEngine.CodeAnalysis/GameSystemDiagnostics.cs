using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// GameSystemGeneratorが発行する診断の定義。診断IDは<c>SYSGEN###</c>(3桁ゼロ埋め)、カテゴリは
/// <c>CSharpGameEngine.Entities.CodeGen</c>に統一する。ルートのTreatWarningsAsErrors=trueにより
/// Severityに関わらずビルドは失敗するため、意図を明確にする目的ですべてErrorとして宣言する
/// (ECSGEN/ENTGENと同じ既存規約)。
/// </summary>
internal static class GameSystemDiagnostics
{
    private const string Category = "CSharpGameEngine.Entities.CodeGen";

    internal static readonly DiagnosticDescriptor GameSystemMustBeStaticClass = new(
        id: "SYSGEN001",
        title: "[GameSystem] requires a static class",
        messageFormat: "'{0}' is annotated with [GameSystem] but is not declared 'static class'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor GameSystemMustNotBeGeneric = new(
        id: "SYSGEN002",
        title: "[GameSystem] class must not be generic",
        messageFormat: "'{0}' is annotated with [GameSystem] but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor GameSystemMustNotBeNested = new(
        id: "SYSGEN003",
        title: "[GameSystem] class must not be nested",
        messageFormat: "'{0}' is annotated with [GameSystem] but is nested inside another type, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustBeStatic = new(
        id: "SYSGEN004",
        title: "[Update] method must be static",
        messageFormat: "Method '{0}' is annotated with [Update] but is not declared 'static'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustReturnVoid = new(
        id: "SYSGEN005",
        title: "[Update] method must return void",
        messageFormat: "Method '{0}' is annotated with [Update] but returns '{1}' instead of void",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustNotBeGeneric = new(
        id: "SYSGEN006",
        title: "[Update] method must not be generic",
        messageFormat: "Method '{0}' is annotated with [Update] but declares type parameters, which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustNotBePrivate = new(
        id: "SYSGEN007",
        title: "[Update] method must not be private",
        messageFormat: "Method '{0}' is annotated with [Update] but is 'private', so the generated executor (a sibling type) cannot call it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustHaveAtLeastOneComponentParameter = new(
        id: "SYSGEN008",
        title: "[Update] method must have at least one component parameter",
        messageFormat: "Method '{0}' does not declare any 'ref'/'in' component parameter, so no Query can be inferred",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentParameterMustBeRefOrIn = new(
        id: "SYSGEN009",
        title: "Component parameter must be 'ref' or 'in'",
        messageFormat: "Parameter '{0}' of method '{1}' must be passed by 'ref' (write) or 'in' (read), not by value/'out'/'ref readonly'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ComponentTypeMustBeNonNullableStruct = new(
        id: "SYSGEN010",
        title: "Component type must be a non-nullable struct",
        messageFormat: "Parameter '{0}' of method '{1}' has type '{2}', which is not a non-nullable struct type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateComponentParameter = new(
        id: "SYSGEN011",
        title: "The same component type must not appear twice in one [Update] method",
        messageFormat: "Method '{0}' declares component type '{1}' more than once",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TagComponentMustNotBeRef = new(
        id: "SYSGEN012",
        title: "A tag component (zero instance fields) must be received with 'in', not 'ref'",
        messageFormat: "Parameter '{0}' of method '{1}' is a tag component type ('{2}' has no instance fields) and must be 'in', not 'ref'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContextParameterMustBeInOrByValue = new(
        id: "SYSGEN013",
        title: "[Context] parameter must be 'in' (struct) or by value (reference type)",
        messageFormat: "Parameter '{0}' of method '{1}' is annotated with [Context] but is passed by 'ref'/'out'; use 'in' for a struct type or plain by-value for a reference type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CurrentEntityParameterMustBeEntityByValue = new(
        id: "SYSGEN014",
        title: "[CurrentEntity] parameter must be a by-value Entity",
        messageFormat: "Parameter '{0}' of method '{1}' is annotated with [CurrentEntity] but is not a by-value 'CSharpGameEngine.Entities.Entity'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CommandsParameterMustBeEntityCommandBufferByValue = new(
        id: "SYSGEN015",
        title: "[Commands] parameter must be a by-value EntityCommandBuffer",
        messageFormat: "Parameter '{0}' of method '{1}' is annotated with [Commands] but is not a by-value 'CSharpGameEngine.Entities.EntityCommandBuffer'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ParameterMustNotCombineMultipleRoleAttributes = new(
        id: "SYSGEN016",
        title: "A parameter must not combine [Context]/[CurrentEntity]/[Commands]",
        messageFormat: "Parameter '{0}' of method '{1}' combines more than one of [Context]/[CurrentEntity]/[Commands]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateSpecialParameter = new(
        id: "SYSGEN017",
        title: "[CurrentEntity]/[Commands]/a given [Context] type must appear at most once",
        messageFormat: "Method '{0}' declares more than one parameter for '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateUpdateMethodName = new(
        id: "SYSGEN018",
        title: "[Update] method names must be unique within a [GameSystem] class",
        messageFormat: "'{0}' declares more than one [Update] method named '{1}' (overloads are not supported)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CircularSystemDependency = new(
        id: "SYSGEN019",
        title: "Circular dependency between [Update] methods",
        messageFormat: "The following [Update] methods form a circular Write/Read dependency and cannot be ordered: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor WithoutArgumentInvalid = new(
        id: "SYSGEN020",
        title: "[Without] argument must be a component struct not already required",
        messageFormat: "Method '{0}': [Without(typeof({1}))] must name a non-nullable struct type that is not also used as a component parameter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
