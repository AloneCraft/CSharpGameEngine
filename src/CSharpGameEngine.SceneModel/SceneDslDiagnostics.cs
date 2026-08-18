using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// Scene DSLの診断定義(ADR-0040)。IDは<c>SCENE1##</c>、カテゴリは<c>CSharpGameEngine.Entities.CodeGen</c>
/// に統一する(既存規約)。ルートのTreatWarningsAsErrors=trueによりSeverityに関わらずビルドは失敗するため、
/// 意図を明確にする目的ですべてErrorとして宣言する。
///
/// <para>
/// <b>100番台から始めるのは意図的</b>: 旧<c>[SceneDefinition]</c>のSCENE001〜008は削除されており、
/// 番号の再利用は「古い診断が復活した」という誤読を生むため避ける。
/// </para>
///
/// <para>
/// これらを<b>報告するのは<c>SceneDslAnalyzer</c>(DiagnosticAnalyzer)だけ</b>である。
/// GameSystemGeneratorは同じ解析を走らせるが診断は報告せず、生成可否の判定にのみ使う
/// (二重スクイグルを避けるため)。リリース追跡は出荷側の
/// <c>src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md</c>で一元管理する。
/// </para>
/// </summary>
internal static class SceneDslDiagnostics {
    private const string Category="CSharpGameEngine.Entities.CodeGen";

    internal static readonly DiagnosticDescriptor SceneMustBeNonGenericPartialClass=new(
        id:"SCENE100",
        title:"An IScene implementation must be a non-generic partial class",
        messageFormat:"'{0}' implements IScene but is not declared as a non-generic 'partial class'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor RootPropertyMustBeExpressionBodied=new(
        id:"SCENE101",
        title:"IScene.Root must be an expression-bodied or single-return get-only property",
        messageFormat:"'{0}.Root' must be an expression-bodied property or a get accessor whose body is a single return statement",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SceneMustNotDeclareConstructor=new(
        id:"SCENE102",
        title:"A scene definition class must not declare an instance constructor",
        messageFormat:"'{0}' must not declare an instance constructor; the Scene DSL is never executed and the generator emits a private constructor to enforce that",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor RootMustBeNodeCreation=new(
        id:"SCENE103",
        title:"IScene.Root must be a Node object-creation expression",
        messageFormat:"'{0}.Root' must be a 'new Node' object-creation expression",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnknownNodeMember=new(
        id:"SCENE104",
        title:"A Node initializer may only assign Name, Components and Children",
        messageFormat:"'{0}' cannot be assigned in a Node initializer; only 'Name', 'Components' and 'Children' are supported",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor NodeNameMustBeConstantString=new(
        id:"SCENE105",
        title:"Node.Name must be a compile-time constant string",
        messageFormat:"'Node.Name' must be a compile-time constant string; '{0}' cannot be resolved at compile time",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor MemberMustBeCollectionExpression=new(
        id:"SCENE106",
        title:"Node.Components and Node.Children must be collection expressions",
        messageFormat:"'Node.{0}' must be a collection expression or an array initializer",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ComponentMustBeStructCreation=new(
        id:"SCENE107",
        title:"A Components element must create a non-nullable component struct",
        messageFormat:"Each 'Node.Components' element must be an object-creation expression of a non-nullable component struct; '{0}' is not",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor DuplicateComponentType=new(
        id:"SCENE108",
        title:"A node must not declare the same component type twice",
        messageFormat:"Node '{0}' declares component type '{1}' more than once",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ChildMustBeNodeCreation=new(
        id:"SCENE109",
        title:"A Children element must be a Node object-creation expression",
        messageFormat:"Each 'Node.Children' element must be a 'new Node' object-creation expression; '{0}' is not",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnsupportedExpression=new(
        id:"SCENE110",
        title:"Unsupported expression in the Scene DSL",
        messageFormat:"'{0}' cannot be used in the Scene DSL; only declarations whose value is statically determined are allowed (this is a '{1}')",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor MethodInvocationRequiresSceneFactory=new(
        id:"SCENE111",
        title:"Method invocations in the Scene DSL require [SceneFactory]",
        messageFormat:"Method invocation '{0}' cannot be used in the Scene DSL; annotate the target static method with [SceneFactory] to opt in",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor MemberAccessRequiresSceneAssets=new(
        id:"SCENE112",
        title:"Member references in the Scene DSL require [SceneAssets]",
        messageFormat:"'{0}' cannot be referenced from the Scene DSL; only members of a static class annotated with [SceneAssets] can be referenced",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor InvalidSceneFactoryTarget=new(
        id:"SCENE113",
        title:"A [SceneFactory] method must be static and take Scene DSL values",
        messageFormat:"[SceneFactory] method '{0}' must be static and every argument must itself be a valid Scene DSL value expression",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SceneDslTypeUsedOutsideDeclaration=new(
        id:"SCENE114",
        title:"Scene DSL types must not be referenced outside a scene declaration",
        messageFormat:"'{0}' cannot be referenced here; the Scene DSL is interpreted at compile time only and is never executed at runtime",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ReservedComponentType=new(
        id:"SCENE115",
        title:"Reserved component types must not be declared explicitly",
        messageFormat:"'{0}' is attached automatically when the scene is generated and must not be declared in 'Node.Components'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SceneMustNotBeNested=new(
        id:"SCENE116",
        title:"A scene definition class must not be nested",
        messageFormat:"'{0}' implements IScene but is a nested type; scene definition classes must be declared at namespace scope",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor StartupSceneMustImplementIScene=new(
        id:"SCENE117",
        title:"[StartupScene] can only be applied to an IScene implementation",
        messageFormat:"'{0}' is annotated with [StartupScene] but does not implement IScene",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// 唯一、コンパイル全体を見ないと判定できない規則。<c>CompilationEnd</c>タグが必要で(RS1037)、
    /// その代償としてIDEでは「ソリューション全体の解析」を有効にしないとスクイグルが出ない。
    /// ビルド時には常に検出される。
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateStartupSceneOrder=new(
        id:"SCENE118",
        title:"Startup scenes must not share the same Order",
        messageFormat:"Startup scene order '{0}' is declared by both '{1}' and '{2}'; startup order must be unambiguous",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        description:null,
        helpLinkUri:null,
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// M0049: <c>*.scene.json</c>用の診断。<c>200</c>番台から始めるのは意図的 —
    /// C#構文由来のSCENE1xxと出所(JSON vs C#)を番号だけで見分けられるようにする。
    /// これらは<see cref="SceneDslAnalyzer"/>ではなく<c>GameSystemGenerator</c>が
    /// <c>SourceProductionContext.ReportDiagnostic</c>で直接報告する(JSONにはIDEのライブ構文解析対象が
    /// 無く、二重報告の心配が無いため)。
    /// </summary>
    internal static readonly DiagnosticDescriptor InvalidSceneJson=new(
        id:"SCENE200",
        title:"A *.scene.json file is malformed",
        messageFormat:"'{0}'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnknownJsonMember=new(
        id:"SCENE201",
        title:"A scene.json component member does not exist on the target type",
        messageFormat:"'{0}' is not a public field or property of '{1}'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor JsonValueTypeMismatch=new(
        id:"SCENE202",
        title:"A scene.json value does not match the target member's type",
        messageFormat:"A value of type '{0}' expects a JSON {1}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnknownEnumMember=new(
        id:"SCENE203",
        title:"A scene.json enum member name does not exist",
        messageFormat:"'{0}' is not a member of enum '{1}'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnsupportedJsonMemberType=new(
        id:"SCENE204",
        title:"A scene.json component member has an unsupported type",
        messageFormat:"'{0}' cannot be assigned from scene.json; only string, bool, numeric and enum members are supported",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// <c>SceneDslAnalyzer.SupportedDiagnostics</c>が返す全ルール。ここに追加し忘れると
    /// 「報告したのにIDEへ出ない」という追跡困難な状態になるため、descriptorの定義と同じファイルに置く。
    /// SCENE2xx(scene.json)は<c>SceneDslAnalyzer</c>ではなくGameSystemGeneratorが報告するため、
    /// あえてこの配列には含めない(含めるとIDEのライブ構文解析対象に無い診断IDがRS1038等で警告される)。
    /// </summary>
    internal static readonly DiagnosticDescriptor[]All=[
        SceneMustBeNonGenericPartialClass,
        RootPropertyMustBeExpressionBodied,
        SceneMustNotDeclareConstructor,
        RootMustBeNodeCreation,
        UnknownNodeMember,
        NodeNameMustBeConstantString,
        MemberMustBeCollectionExpression,
        ComponentMustBeStructCreation,
        DuplicateComponentType,
        ChildMustBeNodeCreation,
        UnsupportedExpression,
        MethodInvocationRequiresSceneFactory,
        MemberAccessRequiresSceneAssets,
        InvalidSceneFactoryTarget,
        SceneDslTypeUsedOutsideDeclaration,
        ReservedComponentType,
        SceneMustNotBeNested,
        StartupSceneMustImplementIScene,
        DuplicateStartupSceneOrder,
    ];
}
