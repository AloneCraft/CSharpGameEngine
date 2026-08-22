using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する(既存Model群と同じ理由)。
/// </remarks>
internal enum GameSystemMethodRole {
    /// <summary><c>Update</c>: Archetypeを登録し、毎フレームのディスパッチ対象になる。</summary>
    Dispatch,

    /// <summary><c>Spawn</c>: Archetype形状の登録のみ。ディスパッチ対象外(ADR-0033決定事項2)。</summary>
    SpawnOnly,

    /// <summary>Shutdown lifecycle phase.</summary>
    Shutdown,
}

internal enum GameSystemParameterRole {
    Entity,
    Component,
    Context,
    State,
}

/// <summary>
/// 元のメソッド宣言でのパラメータ並び順を1つずつ表す。役割ごとにまとめられたコレクションだけでは
/// 並び順の情報を失うため、Emitterがユーザーメソッドを位置引数で正しく呼び出すにはこの順序情報が
/// 別途必要になる。
/// </summary>
internal readonly struct GameSystemParameterSlot(GameSystemParameterRole Role,int Index) {
    internal readonly GameSystemParameterRole Role=Role;
    internal readonly int Index=Index;
}

internal readonly struct GameSystemComponentParameterModel(string ParameterName,string TypeFullyQualifiedName) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string TypeFullyQualifiedName=TypeFullyQualifiedName;
}

/// <summary>Context引数(Query無関係、ゲームループの引数としてそのまま転送される)。値型は<c>in</c>、参照型は値渡し。</summary>
internal readonly struct GameSystemContextParameterModel(string ParameterName,string TypeFullyQualifiedName,bool IsReferenceType) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string TypeFullyQualifiedName=TypeFullyQualifiedName;
    internal readonly bool IsReferenceType=IsReferenceType;
}

/// <summary>1つの<c>Update</c>/<c>Spawn</c>オーバーロードの解析結果。</summary>
internal readonly struct GameSystemMethodModel(
        GameSystemMethodRole Role,
        bool HasLeadingEntityParameter,
        ImmutableArray<GameSystemComponentParameterModel>Components,
        ImmutableArray<GameSystemContextParameterModel>Contexts,
        ImmutableArray<GameSystemParameterSlot>CallOrder,
        int DeclarationPosition) {
    internal readonly GameSystemMethodRole Role=Role;
    internal readonly bool HasLeadingEntityParameter=HasLeadingEntityParameter;
    internal readonly ImmutableArray<GameSystemComponentParameterModel>Components=Components;
    internal readonly ImmutableArray<GameSystemContextParameterModel>Contexts=Contexts;
    internal readonly ImmutableArray<GameSystemParameterSlot>CallOrder=CallOrder;
    internal readonly int DeclarationPosition=DeclarationPosition;
}

/// <summary>
/// 1つの<c>ISystem</c>実装structの解析結果。<see cref="Diagnostics"/>が1件でもあれば、このstruct分の
/// 出力を丸ごとスキップする(既存ジェネレータと同じ全か無かの方針)。
/// </summary>
internal readonly struct GameSystemResult(
        string?Namespace,
        string ClassName,
        ImmutableArray<GameSystemMethodModel>Methods,
        ImmutableArray<Diagnostic>Diagnostics,
        string FilePath,
        int DeclarationPosition) {
    internal readonly string?Namespace=Namespace;
    internal readonly string ClassName=ClassName;
    internal readonly ImmutableArray<GameSystemMethodModel>Methods=Methods;
    internal readonly ImmutableArray<Diagnostic>Diagnostics=Diagnostics;

    /// <summary>
    /// 実行順序の決定に使う宣言位置。ADR-0033決定事項1により、実行順は「ソース上の記述順」。
    /// 同一ファイル内は<see cref="DeclarationPosition"/>(SourceSpan.Start)昇順、
    /// 複数ファイルにまたがる場合のみ<see cref="FilePath"/>のOrdinal順でタイブレークする。
    /// </summary>
    internal readonly string FilePath=FilePath;
    internal readonly int DeclarationPosition=DeclarationPosition;

    internal string ClassFullyQualifiedName=>this.Namespace is null?this.ClassName: $"{this.Namespace}.{this.ClassName}";

    internal string GlobalQualifiedName=>this.Namespace is null? $"global::{this.ClassName}" : $"global::{this.Namespace}.{this.ClassName}";
}

/// <summary>コンパイル全体で発見された1つのComponent型に、初出順で割り当てられたグローバルインデックス。</summary>
internal readonly struct GameSystemComponentInfo(int Index,string TypeFullyQualifiedName,string FieldName) {
    internal readonly int Index=Index;
    internal readonly string TypeFullyQualifiedName=TypeFullyQualifiedName;
    internal readonly string FieldName=FieldName;
}

/// <summary>
/// 1つの生成対象Archetype。<see cref="ComponentIndices"/>は<b>最初にこの構成を宣言した
/// <c>Update</c>/<c>Spawn</c>の引数順</b>であり、グローバルインデックス昇順ではない。生成される
/// <c>World.Create</c>/<c>MoveArchetype</c>のパラメータ順もこれに従うため、呼び出し側は自分が
/// 宣言したとおりの順序で引数を渡せる(内部インデックスの並びを知る必要がない)。
/// </summary>
internal readonly struct GameSystemArchetypeInfo(string Name,ImmutableArray<int>ComponentIndices,long Mask) {
    internal readonly string Name=Name;
    internal readonly ImmutableArray<int>ComponentIndices=ComponentIndices;
    internal readonly long Mask=Mask;

    internal string ListFieldName=> $"List{this.Name}";

    internal string MoveCommandTypeName=> $"MoveTo{this.Name}Command";

    internal string MoveCommandsFieldName=> $"MoveTo{this.Name}Commands";
}

/// <summary>
/// <c>[SystemOrder]</c>の付け先2種類(ADR-0036)。<c>System</c>はそのSystem1つだけの順序、
/// <c>FileMarker</c>はファイル内で自分の順序を持たない全Systemの既定順序になる。
/// </summary>
internal enum GameSystemOrderTargetKind {
    System,
    FileMarker,
}

/// <summary>
/// <c>[SystemOrder]</c>属性(<see cref="GameSystemOrderAnalyzer"/>)を1つ付けた対象の解析結果。
/// ファイル横断・Systemごとの実行順序は本来ADR-0033の「記述順」からは導出できない(構造的な
/// 手がかりが無い)ため、この属性だけが例外的に許される宣言的キーになる(ADR-0036)。
/// </summary>
internal readonly struct GameSystemOrderResult(
        GameSystemOrderTargetKind Kind,
        string FilePath,
        int Order,
        string TargetName,
        string?SystemFullyQualifiedName,
        Location Location,
        ImmutableArray<Diagnostic>Diagnostics) {
    internal readonly GameSystemOrderTargetKind Kind=Kind;
    internal readonly string FilePath=FilePath;
    internal readonly int Order=Order;

    /// <summary>診断メッセージ用の型名(Systemの場合もマーカーの場合も単純名)。</summary>
    internal readonly string TargetName=TargetName;

    /// <summary><see cref="GameSystemOrderTargetKind.System"/>のときだけ設定される、対象Systemの完全修飾名。<see cref="GameSystemResult.ClassFullyQualifiedName"/>と同じキー空間。</summary>
    internal readonly string?SystemFullyQualifiedName=SystemFullyQualifiedName;

    internal readonly Location Location=Location;
    internal readonly ImmutableArray<Diagnostic>Diagnostics=Diagnostics;
}

/// <summary>
/// <see cref="GameSystemExecutionOrder.Compare"/>が引く2種類のマップをまとめたもの。System単体の
/// 明示指定がファイル既定より優先される(<see cref="GameSystemExecutionOrder"/>参照)。
/// </summary>
internal readonly struct GameSystemOrderContext(IReadOnlyDictionary<string,int>SystemOrderByFqn,IReadOnlyDictionary<string,int>FileOrderByPath) {
    internal readonly IReadOnlyDictionary<string,int>SystemOrderByFqn=SystemOrderByFqn;
    internal readonly IReadOnlyDictionary<string,int>FileOrderByPath=FileOrderByPath;
}

/// <summary>
/// <see cref="GameSystemIndexAssigner.Assign"/>の出力。診断のあるSystemは<see cref="ValidSystems"/>
/// から除外済み、<see cref="Archetypes"/>は決定的な出力順序(コンポーネント数昇順→インデックスタプル
/// 辞書式昇順)で確定済み、<see cref="ValidSystems"/>は実行順序(記述順、<c>[SystemOrder]</c>があれば
/// それを優先キーとする)で並んでいる。
/// </summary>
internal readonly struct GameSystemRegistry(
        ImmutableArray<GameSystemComponentInfo>ComponentsByIndex,
        ImmutableArray<GameSystemArchetypeInfo>Archetypes,
        ImmutableArray<GameSystemResult>ValidSystems,
        ImmutableArray<Diagnostic>Diagnostics) {
    internal readonly ImmutableArray<GameSystemComponentInfo>ComponentsByIndex=ComponentsByIndex;
    internal readonly ImmutableArray<GameSystemArchetypeInfo>Archetypes=Archetypes;
    internal readonly ImmutableArray<GameSystemResult>ValidSystems=ValidSystems;
    internal readonly ImmutableArray<Diagnostic>Diagnostics=Diagnostics;

    /// <summary>
    /// 指定したComponentインデックスの<b>集合</b>に一致するArchetypeを引く(順序は問わない -
    /// <see cref="GameSystemArchetypeInfo.ComponentIndices"/>は宣言順であり昇順とは限らないため、
    /// 両辺をソートして比較する)。SceneRuntimeEmitterが使う。
    /// </summary>
    internal GameSystemArchetypeInfo?FindArchetype(IReadOnlyList<int>ComponentIndices){
        List<int>Wanted=[.. ComponentIndices];
        Wanted.Sort();

        foreach(GameSystemArchetypeInfo Archetype in this.Archetypes){
            if(Archetype.ComponentIndices.Length!=Wanted.Count){
                continue;
            }

            List<int>Candidate=[.. Archetype.ComponentIndices];
            Candidate.Sort();

            var Same=true;

            for(var I=0;I<Wanted.Count;I++){
                if(Candidate[I]!=Wanted[I]){
                    Same=false;
                    break;
                }
            }

            if(Same){
                return Archetype;
            }
        }

        return null;
    }
}
