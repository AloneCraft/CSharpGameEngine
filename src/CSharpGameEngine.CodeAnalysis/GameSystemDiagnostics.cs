using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// GameSystemGeneratorが発行する診断の定義。診断IDは<c>SYSGEN###</c>(3桁ゼロ埋め)、カテゴリは
/// <c>CSharpGameEngine.Entities.CodeGen</c>。ルートのTreatWarningsAsErrors=trueによりSeverityに
/// 関わらずビルドは失敗するため、意図を明確にする目的ですべてErrorとして宣言する(既存規約)。
/// ADR-0033の全面移行に伴い、属性ベースだった旧SYSGEN001〜020は破棄し、構造的推論ベースの
/// 新しい規則へ全面的に振り直した。
/// </summary>
internal static class GameSystemDiagnostics {
    private const string Category="CSharpGameEngine.Entities.CodeGen";

    internal static readonly DiagnosticDescriptor SystemMustBeStruct=new(
        id:"SYSGEN001",
        title:"ISystem implementor must be a struct",
        messageFormat:"'{0}' implements ISystem but is not declared 'struct'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SystemMustNotBeGeneric=new(
        id:"SYSGEN002",
        title:"ISystem struct must not be generic",
        messageFormat:"'{0}' implements ISystem but declares type parameters, which is not supported",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SystemMustNotBeNested=new(
        id:"SYSGEN003",
        title:"ISystem struct must not be nested",
        messageFormat:"'{0}' implements ISystem but is nested inside another type, which is not supported",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor MethodMustBeInstance=new(
        id:"SYSGEN004",
        title:"Update/Spawn method must be an instance method",
        messageFormat:"Method '{0}' is declared 'static'; Update/Spawn methods must be instance methods",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor MethodMustNotBePrivate=new(
        id:"SYSGEN005",
        title:"Update/Spawn method must not be private",
        messageFormat:"Method '{0}' is 'private', so the generated game loop cannot call it",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UpdateMethodMustReturnBool=new(
        id:"SYSGEN006",
        title:"Update method must return void or bool",
        messageFormat:"Method '{0}' returns '{1}'; an Update returns void (ADR-0072), or bool where false ends a world system's frame",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor MethodMustNotBeGeneric=new(
        id:"SYSGEN007",
        title:"Update/Spawn method must not be generic",
        messageFormat:"Method '{0}' declares type parameters, which is not supported",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor MethodMustHaveAtLeastOneComponentParameter=new(
        id:"SYSGEN008",
        title:"Update/Spawn method must have at least one component parameter",
        messageFormat:"Method '{0}' does not declare any 'ref' component parameter, so no Archetype can be inferred",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    // SYSGEN009("Componentパラメータはrefでなければならない")は実装時に到達不能と判明したため欠番。
    // 構造的推論(ADR-0033決定事項3)では「refならComponent、ref以外ならContext」と定義したので、
    // refでないパラメータは壊れたComponentではなく正当なContextになる。渡し方自体が不正な場合
    // (`out`など)はSYSGEN018が捕捉する。ADR-0015でも到達不能と分かった診断を同様に削除している。

    internal static readonly DiagnosticDescriptor ComponentTypeMustBeNonNullableStruct=new(
        id:"SYSGEN010",
        title:"Component type must be a non-nullable struct",
        messageFormat:"Parameter '{0}' of method '{1}' has type '{2}', which is not a non-nullable struct type",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor DuplicateComponentParameter=new(
        id:"SYSGEN011",
        title:"The same component type must not appear twice in one method",
        messageFormat:"Method '{0}' declares component type '{1}' more than once",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor LeadingEntityParameterMustBeByValueFirst=new(
        id:"SYSGEN012",
        title:"A leading Entity parameter must be by-value and the first parameter",
        messageFormat:"Method '{0}': the 'Entity' parameter must be passed by value and be the first parameter",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor EntityOrStateMustNotBeUsedAsComponent=new(
        id:"SYSGEN014",
        title:"Entity/State must not be used as a component parameter",
        messageFormat:"Parameter '{0}' of method '{1}' uses '{2}', which is reserved and cannot be a component",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SystemHasNoRecognizedMethod=new(
        id:"SYSGEN015",
        title:"ISystem struct declares no recognized lifecycle method",
        messageFormat:"'{0}' implements ISystem but declares no method named 'Update', 'Spawn', or 'Shutdown', so it will never run",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);


    internal static readonly DiagnosticDescriptor ShutdownMethodMustReturnVoid=new(
        id:"SYSGEN028",
        title:"Shutdown method must return void",
        messageFormat:"Method '{0}' is named 'Shutdown' but returns '{1}'; Shutdown must return void",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SpawnMethodMustReturnVoid=new(
        id:"SYSGEN016",
        title:"Spawn method must return void",
        messageFormat:"Method '{0}' is named 'Spawn' but returns '{1}'; Spawn only declares an Archetype shape and must return void",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor SpawnMethodMustNotDeclareEntityOrState=new(
        id:"SYSGEN017",
        title:"Spawn method must not declare an Entity or State parameter",
        messageFormat:"Method '{0}' is named 'Spawn' but declares an 'Entity'/'State' parameter; Spawn is never dispatched and only takes component parameters",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ContextParameterMustBeInOrByValue=new(
        id:"SYSGEN018",
        title:"Context parameter must be 'in' (struct) or by value (reference type)",
        messageFormat:"Parameter '{0}' of method '{1}' is passed by '{2}'; a Context parameter must be 'in' for a struct type or plain by-value for a reference type",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor DuplicateContextParameter=new(
        id:"SYSGEN019",
        title:"The same Context type must not appear twice in one method",
        messageFormat:"Method '{0}' declares Context type '{1}' more than once",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ComponentSimpleNameCollision=new(
        id:"SYSGEN020",
        title:"Two component types must not share a simple name",
        messageFormat:"Component types '{0}' and '{1}' share the simple name '{2}'; generated Archetype fields would collide",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// エントリポイントを生成すべき状況(Exe・ユーザーのMain無し・System有り)なのにContext引数が
    /// あるため生成できないケース。何もしないとCS5001「エントリポイントが見つかりません」だけが出て
    /// 原因が分からないため、理由を名指しする。
    /// </summary>
    internal static readonly DiagnosticDescriptor EntryPointCannotSupplyContext=new(
        id:"SYSGEN021",
        title:"Cannot generate an entry point while a Context parameter is declared",
        messageFormat:"'{0}' declares the Context parameter '{1}', which the generated entry point cannot construct; hold it in a component and create it from a System, or write Program.cs by hand",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// Archetypeの一致判定はSource Generator内の二語の<c>ulong</c>マスクで行う。
    /// 1–64型はlow wordだけ、65–128型はlow/high wordを使い、生成Runtimeへmask照合を残さない。
    /// 129型目は表現できないため、ビットの折り返しで部分集合判定が壊れる前に明示的に拒否する。
    /// </summary>
    internal static readonly DiagnosticDescriptor TooManyComponentTypes=new(
        id:"SYSGEN026",
        title:"A compilation must not declare more than 128 distinct component types",
        messageFormat:"This compilation declares {0} distinct component types, but the archetype mask is 128 bits wide so at most {1} are supported",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// <c>IRuntimeComponent&lt;TAuthored&gt;</c>は1つの作者Component型につき1つでなければならない。
    /// 2つ以上が同じ<c>TAuthored</c>を主張すると、Scene DSLがどちらを随伴付与するかが非決定になり、
    /// Archetypeの構成がビルドごとに変わりうる。曖昧なまま片方を選ぶより明示的に止める。
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateRuntimeComponent=new(
        id:"SYSGEN027",
        title:"An authored component must have at most one runtime companion component",
        messageFormat:"'{0}' is claimed by more than one IRuntimeComponent<T> implementation ('{1}' and '{2}'); keep exactly one",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>1つのSystem解析結果が持つ全診断を報告する。生成には関与しない診断専用の出力段。</summary>
    internal static readonly DiagnosticDescriptor ReadOnlyComponentMustBeInStruct=new(
        id:"SYSGEN029",
        title:"A read-only component must be an in struct parameter",
        messageFormat:"Parameter '{0}' on '{1}' uses ReadOnlyComponent but is not an in non-nullable struct parameter",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);


    internal static readonly DiagnosticDescriptor AmbiguousScheduledAccessConflict=new(
        id:"SYSGEN032",
        title:"Conflicting scheduled component access must be explicitly ordered",
        messageFormat:"Scheduled systems '{0}' and '{1}' have an unordered write conflict on '{2}'; add RunsAfter or RunsBefore to establish one order",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>ADR-0073: <c>[RunsAfter]</c>/<c>[RunsBefore]</c>の対象は、このコンパイルのSystemでなければならない。</summary>
    internal static readonly DiagnosticDescriptor SystemOrderTargetMustBeSystem=new(
        id:"SYSGEN033",
        title:"A RunsAfter or RunsBefore target must be a system in this compilation",
        messageFormat:"'{0}' declares [{1}] on '{2}', which is not a system in this compilation",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>ADR-0073: 同じPhaseの中の<c>[RunsAfter]</c>/<c>[RunsBefore]</c>は循環してはならない。</summary>
    internal static readonly DiagnosticDescriptor SystemOrderCycle=new(
        id:"SYSGEN034",
        title:"System order declarations must not form a cycle",
        messageFormat:"System order cycle: {0}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// ADR-0073: Phaseの順は固定なので、後のPhaseのSystemの後・前のPhaseのSystemの前には置けない
    /// (逆向きの宣言は常に満たされるので何もしない)。
    /// </summary>
    internal static readonly DiagnosticDescriptor SystemOrderAgainstPhaseOrder=new(
        id:"SYSGEN035",
        title:"A system cannot be ordered against the phase order",
        messageFormat:"'{0}' declares [{1}] on '{2}', but '{0}' runs in the {3} phase and '{2}' in the {4} phase, so the phase order already decides the opposite",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ParentRelationParameterInvalid=new(
        id:"SYSGEN036",
        title:"A Parent<T> parameter must be an 'in' parameter of an Update or Shutdown method, with T a non-nullable struct",
        messageFormat:"Parameter '{0}' of method '{1}' is a Parent<T> relation that is not an 'in' parameter, or T is not a non-nullable struct, or the method is a Spawn",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor ParentRelationConflictsWithComponent=new(
        id:"SYSGEN037",
        title:"A method cannot read a component from the parent and also take or repeat it",
        messageFormat:"Method '{0}' takes '{1}' as a Parent<T> and also as a component or a second Parent<T>; the entity and its parent are different entities, so split the method",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor InputActionsMustBePassedByIn=new(
        id:"SYSGEN038",
        title:"An [InputActions] struct must be an 'in' parameter of an Update or Shutdown method",
        messageFormat:"Parameter '{0}' of method '{1}' is an [InputActions] struct that is not an 'in' parameter, or the method is a Spawn",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor EventParameterInvalid=new(
        id:"SYSGEN039",
        title:"An event parameter must be 'in TEvent' or 'in EventWriter<TEvent>' of an Update method, with TEvent a struct implementing IEvent",
        messageFormat:"Parameter '{0}' of method '{1}' is an event parameter that is not an 'in' parameter, whose type is not a struct implementing IEvent, that belongs to a Spawn or Shutdown, or is a second event of the method",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor EventConsumerCannotTakeEntities=new(
        id:"SYSGEN040",
        title:"A method that receives an event cannot also take components, an Entity or a Parent<T>",
        messageFormat:"Method '{0}' receives an event and also iterates entities; the event has no entity of its own, so split the method (carry the entity in the event)",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor EventReceivedBeforeItIsSent=new(
        id:"SYSGEN041",
        title:"An event is received in an earlier phase than the one that sends it",
        messageFormat:"'{0}' receives {1} in the {2} phase, but '{3}' sends it in the later {4} phase, so the event would be dropped at the next frame",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor EventNeverSent=new(
        id:"SYSGEN042",
        title:"An event is received but no system sends it",
        messageFormat:"'{0}' receives {1}, but no system takes an EventWriter<{1}>, so it never runs",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Warning,
        isEnabledByDefault:true);

    internal static void ReportAll(SourceProductionContext Context,GameSystemResult Result){
        foreach(var Diagnostic in Result.Diagnostics){
            Context.ReportDiagnostic(Diagnostic);
        }
    }
}
