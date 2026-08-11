using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// SceneDefinitionGeneratorが発行する診断の定義。診断IDは<c>SCENE###</c>(3桁ゼロ埋め)、カテゴリは
/// <c>CSharpGameEngine.Entities.CodeGen</c>に統一する(GameSystemDiagnosticsと同じ既存規約)。
/// ルートのTreatWarningsAsErrors=trueによりSeverityに関わらずビルドは失敗するため、意図を明確にする
/// 目的ですべてErrorとして宣言する。SCENE001〜008は仕様見本(シーン構築見本.cs)のSCENE001〜006/008/009に
/// 対応する(見本のSCENE007「循環検出」はCreateのネスト構文では真の循環が到達不能でSCENE005が構造的に
/// 包含するため実装せず、見本のSCENE010「削除時の被参照チェック」はIDE操作時のガードでPhase B以降の
/// 対象のため実装しない)。SCENE009は見本には無い、Phase A固有の追加診断(World.CreateArchetypeの
/// 型引数8個上限に対応するための静的検証)。
/// </summary>
internal static class SceneDefinitionDiagnostics {
    private const string Category="CSharpGameEngine.Entities.CodeGen";

    internal static readonly DiagnosticDescriptor MustBeStaticPartialClass=new(
        id:"SCENE001",
        title:"[SceneDefinition] requires a static partial class",
        messageFormat:"'{0}' is annotated with [SceneDefinition] but is not declared 'static partial class'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor NodePropertyMustBeStaticPartialGetOnly=new(
        id:"SCENE002",
        title:"A SceneNodeSymbol node declaration must be a static partial get-only property",
        messageFormat:"Property '{0}' has type 'SceneNodeSymbol' but is not declared 'private static partial SceneNodeSymbol {0} {{ get; }}'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor CreateArgumentAndLambdaParameterNameMismatch=new(
        id:"SCENE003",
        title:"Create's node argument and its lambda parameter must have the same name",
        messageFormat:"'Create({0}, {1} => ...)' names the lambda parameter '{1}', but it must match the node argument '{0}'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor DeclaredNodeNotCreated=new(
        id:"SCENE004",
        title:"A declared SceneNodeSymbol node must be created exactly once in Define",
        messageFormat:"Node '{0}' is declared but never created via 'Create' inside 'Define'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor DuplicateNodeCreation=new(
        id:"SCENE005",
        title:"The same node must not be created more than once",
        messageFormat:"Node '{0}' is created more than once inside 'Define'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor TopLevelCreateMustBeSingle=new(
        id:"SCENE006",
        title:"Define must contain exactly one top-level Create call",
        messageFormat:"'Define' contains {0} top-level 'Scene.Create' call(s), but exactly 1 is required so the scene has a single Root node",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnsupportedSyntaxInDefine=new(
        id:"SCENE007",
        title:"Define must only contain Create/Set calls",
        messageFormat:"'Define' contains a construct ('{0}') that cannot be statically analyzed; only nested 'Create'/'Set' calls are supported (no loops, conditionals, or arbitrary method calls)",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ComponentInitializerMustBeObjectCreationExpression=new(
        id:"SCENE008",
        title:"Set's argument must be a plain object-creation expression",
        messageFormat:"'Set' argument for node '{0}' must be a plain 'new TypeName(...)' object-creation expression that can be emitted verbatim; arbitrary expressions are not supported",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    // SCENE009(1ノードのComponent数が8を超えてはならない)はADR-0033で削除した。
    // この規則は旧`World.CreateArchetype<T1..T8>`のジェネリック型引数の上限に由来するものであり、
    // 閉集合Archetype方式では組み合わせごとに専用のstructを生成するため、そもそも上限が存在しない。
}
