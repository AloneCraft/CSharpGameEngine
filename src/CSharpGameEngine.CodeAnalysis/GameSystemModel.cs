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

/// <summary>1つの<c>Update</c>/<c>Spawn</c>オーバーロードの解析結果。</summary>
internal readonly struct GameSystemMethodModel(
        GameSystemMethodRole Role,
        bool HasLeadingEntityParameter,
        ImmutableArray<GameSystemComponentParameterModel>Components,
        ImmutableArray<GameSystemContextParameterModel>Contexts,
        ImmutableArray<GameSystemParameterSlot>CallOrder,
        int DeclarationPosition,
        bool ReturnsBool) {
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
        int?StartupOrder=null) {
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
internal readonly struct GameSystemArchetypeInfo(string Name,ImmutableArray<int>ComponentIndices,ulong MaskLow,ulong MaskHigh) {
    internal readonly string Name=Name;
    internal readonly ImmutableArray<int>ComponentIndices=ComponentIndices;
    internal readonly ulong MaskLow=MaskLow;
    internal readonly ulong MaskHigh=MaskHigh;

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
