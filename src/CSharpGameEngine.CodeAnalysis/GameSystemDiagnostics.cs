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
        title:"Update method must return bool",
        messageFormat:"Method '{0}' returns '{1}' instead of bool",
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

    internal static readonly DiagnosticDescriptor UpdateMethodMustEndWithRefState=new(
        id:"SYSGEN013",
        title:"Update method must end with a trailing 'ref State' parameter",
        messageFormat:"Method '{0}' does not end with a 'ref State' parameter",
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
        title:"ISystem struct declares no Update or Spawn method",
        messageFormat:"'{0}' implements ISystem but declares no method named 'Update' or 'Spawn', so it will never run",
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
    /// <c>[SystemOrder]</c>(ADR-0036)をファイル既定値として使う場合、`file`スコープのマーカークラス
    /// 専用に限定している。通常のクラスに付けると、そのクラス名がファイルの外へ漏れる
    /// (=マーカーのつもりが公開APIの一部になる)ため、意図を強制する。ISystem実装struct自身に
    /// 直接付ける場合(そのSystem1つだけの順序)はこの制約を受けない。
    /// </summary>
    internal static readonly DiagnosticDescriptor SystemOrderMarkerMustBeFileScoped=new(
        id:"SYSGEN022",
        title:"A [SystemOrder] file marker must be a file-scoped ('file') class",
        messageFormat:"'{0}' is decorated with [SystemOrder] as a file-default marker but is not declared with the 'file' modifier; add 'file' so the marker does not leak outside its file",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>1ファイルの既定順序は一意でなければならない。2つ以上のマーカーがあると、そのファイルの既定値が定まらない。</summary>
    internal static readonly DiagnosticDescriptor DuplicateFileOrderInOneFile=new(
        id:"SYSGEN023",
        title:"A file must declare at most one [SystemOrder] file marker",
        messageFormat:"File '{0}' declares a [SystemOrder] file marker on more than one class ('{1}' and '{2}'); only one marker is allowed per file",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// 2つの異なるファイルが同じ明示的なファイル既定値を宣言すると、その2ファイル間の相対順序が
    /// 未定義に戻ってしまう(この属性を導入した目的そのものが達成できなくなる)。System単体に直接
    /// 付けた明示値同士が重複するのは「意図的な同着」でありうるため対象外(ファイル既定値だけの規則)。
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateFileOrderValue=new(
        id:"SYSGEN024",
        title:"Two files must not declare the same explicit [SystemOrder] file-default value",
        messageFormat:"Files '{0}' and '{1}' both declare a [SystemOrder({2})] file marker; give them different values so their relative order is unambiguous",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// <c>[SystemOrder]</c>をstructに付けたが<c>ISystem</c>を実装していない場合、その値はどこからも
    /// 参照されず静かに無視される。付け間違いに気づけるよう報告する。
    /// </summary>
    internal static readonly DiagnosticDescriptor SystemOrderOnNonSystemStruct=new(
        id:"SYSGEN025",
        title:"[SystemOrder] on a struct has no effect unless the struct implements ISystem",
        messageFormat:"'{0}' is decorated with [SystemOrder] but does not implement ISystem, so this value is never used",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    /// <summary>
    /// Archetypeの一致判定は<c>long</c>のビットマスク(<c>1L &lt;&lt; ComponentIndex</c>)で行うため、
    /// Component型が65個目に達した時点で<b>無診断のまま静かに壊れる</b>(65個目以降のビットが溢れ、
    /// 部分集合判定が誤った結果を返す)。Scene DSLとエンジン標準Component群の導入で64は現実的に
    /// 到達しうるため、破綻する前に明示的に止める(ADR-0040 リスクO-12)。
    /// マスクの拡幅(4×<c>ulong</c>)が必要になったら、影響箇所は<c>GameSystemArchetypeInfo.Mask</c>・
    /// <c>GameSystemIndexAssigner.Assign</c>・<c>GameSystemLoopEmitter</c>の部分集合判定の3つだけである。
    /// </summary>
    internal static readonly DiagnosticDescriptor TooManyComponentTypes=new(
        id:"SYSGEN026",
        title:"A compilation must not declare more than 64 distinct component types",
        messageFormat:"This compilation declares {0} distinct component types, but the archetype mask is 64 bits wide so at most {1} are supported",
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
    internal static void ReportAll(SourceProductionContext Context,GameSystemResult Result){
        foreach(var Diagnostic in Result.Diagnostics){
            Context.ReportDiagnostic(Diagnostic);
        }
    }
}
