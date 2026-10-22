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
    /// <summary><c>in Parent&lt;T&gt;</c>(ADR-0077)。親Entityの<c>T</c>を引いて渡す。</summary>
    ParentRelation,
    /// <summary><c>in T</c>で受けた<c>[InputActions]</c>のstruct(ADR-0077)。毎フレームの入力値をシングルトンとして渡す。</summary>
    Resource,
    /// <summary>EngineHostのプロパティの型の参照型引数(ADR-0077)。<c>Audio</c>など。呼び出しでは<c>Host.プロパティ</c>を渡す。</summary>
    Service,
    /// <summary><c>in T</c>(<c>T</c>は<c>IEvent</c>)。送られたイベント1件ごとに呼ぶ(ADR-0077)。</summary>
    EventRead,
    /// <summary><c>in EventWriter&lt;T&gt;</c>。イベントを送る口を渡す。</summary>
    EventWrite,
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

internal readonly struct GameSystemComponentParameterModel(string ParameterName,string TypeFullyQualifiedName,bool IsReadOnly) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string TypeFullyQualifiedName=TypeFullyQualifiedName;
    internal readonly bool IsReadOnly=IsReadOnly;
}

/// <summary>Context引数(Query無関係、ゲームループの引数としてそのまま転送される)。値型は<c>in</c>、参照型は値渡し。</summary>
internal readonly struct GameSystemContextParameterModel(string ParameterName,string TypeFullyQualifiedName,bool IsReferenceType) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string TypeFullyQualifiedName=TypeFullyQualifiedName;
    internal readonly bool IsReferenceType=IsReferenceType;
}

/// <summary><c>in Parent&lt;T&gt;</c>引数(ADR-0077)。<c>T</c>を親Entityから読む。</summary>
internal readonly struct GameSystemRelationParameterModel(string ParameterName,string ComponentTypeFullyQualifiedName) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string ComponentTypeFullyQualifiedName=ComponentTypeFullyQualifiedName;
}

/// <summary><c>in T</c>(<c>T</c>は<c>[InputActions]</c>)引数(ADR-0077)。</summary>
internal readonly struct GameSystemResourceParameterModel(string ParameterName,string TypeFullyQualifiedName) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string TypeFullyQualifiedName=TypeFullyQualifiedName;
}

/// <summary>サービスの作り方。</summary>
internal enum GameSystemServiceSource {
    /// <summary>EngineHostのプロパティ(ADR-0077)。<c>Audio</c>など。</summary>
    HostProperty,
    /// <summary><c>[EngineService]</c>のクラスで、引数なしのコンストラクターで作る(ADR-0079)。</summary>
    Parameterless,
    /// <summary><c>[EngineService]</c>のクラスで、EngineHostを1つ取るコンストラクターで作る(ADR-0079)。</summary>
    TakesHost,
}

/// <summary>Systemの引数のサービス。<see cref="PropertyName"/>は<see cref="GameSystemServiceSource.HostProperty"/>のときだけ。</summary>
internal readonly struct GameSystemServiceParameterModel(string ParameterName,string TypeFullyQualifiedName,string PropertyName,GameSystemServiceSource Source=GameSystemServiceSource.HostProperty) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string TypeFullyQualifiedName=TypeFullyQualifiedName;
    internal readonly string PropertyName=PropertyName;
    internal readonly GameSystemServiceSource Source=Source;
    internal bool NeedsHost=>this.Source!=GameSystemServiceSource.Parameterless;
}

/// <summary>イベントの引数(受け手の<c>in T</c>、送り手の<c>in EventWriter&lt;T&gt;</c>)。</summary>
internal readonly struct GameSystemEventParameterModel(string ParameterName,string EventTypeFullyQualifiedName) {
    internal readonly string ParameterName=ParameterName;
    internal readonly string EventTypeFullyQualifiedName=EventTypeFullyQualifiedName;
}

/// <summary>1つの<c>Update</c>/<c>Spawn</c>オーバーロードの解析結果。</summary>
internal readonly struct GameSystemMethodModel(
        GameSystemMethodRole Role,
        bool HasLeadingEntityParameter,
        ImmutableArray<GameSystemComponentParameterModel>Components,
        ImmutableArray<GameSystemContextParameterModel>Contexts,
        ImmutableArray<GameSystemParameterSlot>CallOrder,
        int DeclarationPosition,
        bool ReturnsBool,
        ImmutableArray<GameSystemRelationParameterModel>Relations=default,
        ImmutableArray<GameSystemResourceParameterModel>Resources=default,
        ImmutableArray<GameSystemServiceParameterModel>Services=default,
        ImmutableArray<GameSystemEventParameterModel>EventReads=default,
        ImmutableArray<GameSystemEventParameterModel>EventWrites=default) {
    internal readonly GameSystemMethodRole Role=Role;
    internal readonly bool HasLeadingEntityParameter=HasLeadingEntityParameter;
    internal readonly ImmutableArray<GameSystemComponentParameterModel>Components=Components;
    internal readonly ImmutableArray<GameSystemContextParameterModel>Contexts=Contexts;
    internal readonly ImmutableArray<GameSystemParameterSlot>CallOrder=CallOrder;
    internal readonly int DeclarationPosition=DeclarationPosition;
    /// <summary>
    /// ADR-0072: an Update returns void (the standard) or bool. Only a world system's false has an
    /// effect (it ends the frame's remaining systems); an entity system's false never did anything.
    /// </summary>
    internal readonly bool ReturnsBool=ReturnsBool;
    /// <summary>親から読むComponent(<c>in Parent&lt;T&gt;</c>)。子のArchetypeには<c>Parent</c>が暗黙に含まれる。</summary>
    internal readonly ImmutableArray<GameSystemRelationParameterModel>Relations=Relations.IsDefault?ImmutableArray<GameSystemRelationParameterModel>.Empty:Relations;
    /// <summary><c>in T</c>で受ける<c>[InputActions]</c>のstruct。Entityには依存しない。</summary>
    internal readonly ImmutableArray<GameSystemResourceParameterModel>Resources=Resources.IsDefault?ImmutableArray<GameSystemResourceParameterModel>.Empty:Resources;
    /// <summary>サービスとして渡す参照型引数(EngineHostのプロパティと<c>[EngineService]</c>のクラス)。</summary>
    internal readonly ImmutableArray<GameSystemServiceParameterModel>Services=Services.IsDefault?ImmutableArray<GameSystemServiceParameterModel>.Empty:Services;
    /// <summary>このメソッドが受けるイベント(0または1種類)。あれば、Entityを回さずイベントごとに呼ぶ。</summary>
    internal readonly ImmutableArray<GameSystemEventParameterModel>EventReads=EventReads.IsDefault?ImmutableArray<GameSystemEventParameterModel>.Empty:EventReads;
    /// <summary>このメソッドが送るイベントの種類。</summary>
    internal readonly ImmutableArray<GameSystemEventParameterModel>EventWrites=EventWrites.IsDefault?ImmutableArray<GameSystemEventParameterModel>.Empty:EventWrites;
    /// <summary>呼び出しにEngineHost(Context)が要るか。入力とホストのサービスは、どのSystemもEngineHostを名指ししなくても要求する。</summary>
    internal bool NeedsHost=>this.Resources.Length>0||this.Services.Any(static Service=>Service.NeedsHost);
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
        bool ExecuteInEditor,
        bool RealtimeInEditor,
        int Phase,
        bool IsWorldSystem,
        bool IsScheduled,
        ImmutableArray<string>RunsAfterSystemFullyQualifiedNames,
        ImmutableArray<string>RunsBeforeSystemFullyQualifiedNames,
        Location DeclarationLocation,
        string FilePath,
        int DeclarationPosition,
        int?StartupOrder=null,
        bool PhaseIsExplicit=false) {
    internal readonly string?Namespace=Namespace;
    internal readonly string ClassName=ClassName;
    internal readonly ImmutableArray<GameSystemMethodModel>Methods=Methods;
    internal readonly ImmutableArray<Diagnostic>Diagnostics=Diagnostics;
    internal readonly bool ExecuteInEditor=ExecuteInEditor;
    internal readonly bool RealtimeInEditor=RealtimeInEditor;
    internal readonly int Phase=Phase;
    internal readonly bool IsWorldSystem=IsWorldSystem;
    internal readonly bool IsScheduled=IsScheduled;
    internal readonly ImmutableArray<string>RunsAfterSystemFullyQualifiedNames=RunsAfterSystemFullyQualifiedNames;
    internal readonly ImmutableArray<string>RunsBeforeSystemFullyQualifiedNames=RunsBeforeSystemFullyQualifiedNames;
    internal readonly Location DeclarationLocation=DeclarationLocation;

    /// <summary>
    /// 起動シーンの合成Systemだけが持つ<c>startupOrder</c>。FrameInputの他のSystemより前に、この値→完全修飾名の
    /// 順で動く(ADR-0073決定事項4)。手書きのSystemはnull。
    /// </summary>
    internal readonly int?StartupOrder=StartupOrder;

    /// <summary>[SystemPhase]を書いたか。書いていなければ、イベントの受け手の既定フェーズは送り手から決まる(ADR-0077)。</summary>
    internal readonly bool PhaseIsExplicit=PhaseIsExplicit;

    /// <summary>フェーズと「後に動く」対象だけを変えた写し(イベントの配線用)。</summary>
    internal GameSystemResult With(int Phase,ImmutableArray<string>RunsAfter)=>new(this.Namespace,this.ClassName,this.Methods,this.Diagnostics,this.ExecuteInEditor,this.RealtimeInEditor,Phase,this.IsWorldSystem,this.IsScheduled,RunsAfter,this.RunsBeforeSystemFullyQualifiedNames,this.DeclarationLocation,this.FilePath,this.DeclarationPosition,this.StartupOrder,this.PhaseIsExplicit);

    /// <summary>診断を足した写し。診断があるSystemは出力から丸ごと外れる。</summary>
    internal GameSystemResult WithDiagnostics(ImmutableArray<Diagnostic>Added)=>new(this.Namespace,this.ClassName,this.Methods,this.Diagnostics.AddRange(Added),this.ExecuteInEditor,this.RealtimeInEditor,this.Phase,this.IsWorldSystem,this.IsScheduled,this.RunsAfterSystemFullyQualifiedNames,this.RunsBeforeSystemFullyQualifiedNames,this.DeclarationLocation,this.FilePath,this.DeclarationPosition,this.StartupOrder,this.PhaseIsExplicit);

    /// <summary>診断の位置と、起動シーンの合成Systemの出所を示すソース上の位置。実行順序には使わない(ADR-0073)。</summary>
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
internal readonly struct GameSystemArchetypeInfo(string Name,ImmutableArray<int>ComponentIndices,ImmutableArray<ulong>Mask) {
    internal readonly string Name=Name;
    internal readonly ImmutableArray<int>ComponentIndices=ComponentIndices;
    /// <summary>このArchetypeが持つComponentのビット。語数はレジストリの全Archetypeで同じ(<see cref="ComponentMask"/>)。</summary>
    internal readonly ImmutableArray<ulong>Mask=Mask;

    internal string ListFieldName=> $"List{this.Name}";

    internal string MoveCommandTypeName=> $"MoveTo{this.Name}Command";

    internal string MoveCommandsFieldName=> $"MoveTo{this.Name}Commands";
}

/// <summary>
/// <see cref="GameSystemIndexAssigner.Assign"/>の出力。診断のあるSystemは<see cref="ValidSystems"/>
/// から除外済み、<see cref="Archetypes"/>は決定的な出力順序(コンポーネント数昇順→インデックスタプル
/// 辞書式昇順)で確定済み、<see cref="ValidSystems"/>は実行順序(<see cref="GameSystemExecutionOrder"/>、
/// Phase順→Phase内の依存グラフ→完全修飾名)で並んでいる。
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
    /// <summary>Componentの有効マスクの語数(1語=64種類)。</summary>
    internal int MaskWordCount=>ComponentMask.WordCount(this.ComponentsByIndex.Length);

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
