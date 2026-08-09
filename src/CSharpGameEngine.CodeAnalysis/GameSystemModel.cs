using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する(既存Model群と同じ理由)。
/// </remarks>
internal enum GameSystemMethodRole
{
    /// <summary><c>Update</c>: Archetypeを登録し、毎フレームのディスパッチ対象になる。</summary>
    Dispatch,

    /// <summary><c>Spawn</c>: Archetype形状の登録のみ。ディスパッチ対象外(ADR-0033決定事項2)。</summary>
    SpawnOnly,
}

internal enum GameSystemParameterRole
{
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
internal readonly struct GameSystemParameterSlot
{
    internal readonly GameSystemParameterRole Role;
    internal readonly int Index;

    internal GameSystemParameterSlot(GameSystemParameterRole role, int index)
    {
        this.Role = role;
        this.Index = index;
    }
}

internal readonly struct GameSystemComponentParameterModel
{
    internal readonly string ParameterName;
    internal readonly string TypeFullyQualifiedName;

    internal GameSystemComponentParameterModel(string parameterName, string typeFullyQualifiedName)
    {
        this.ParameterName = parameterName;
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
    }
}

/// <summary>Context引数(Query無関係、ゲームループの引数としてそのまま転送される)。値型は<c>in</c>、参照型は値渡し。</summary>
internal readonly struct GameSystemContextParameterModel
{
    internal readonly string ParameterName;
    internal readonly string TypeFullyQualifiedName;
    internal readonly bool IsReferenceType;

    internal GameSystemContextParameterModel(string parameterName, string typeFullyQualifiedName, bool isReferenceType)
    {
        this.ParameterName = parameterName;
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
        this.IsReferenceType = isReferenceType;
    }
}

/// <summary>1つの<c>Update</c>/<c>Spawn</c>オーバーロードの解析結果。</summary>
internal readonly struct GameSystemMethodModel
{
    internal readonly GameSystemMethodRole Role;
    internal readonly bool HasLeadingEntityParameter;
    internal readonly ImmutableArray<GameSystemComponentParameterModel> Components;
    internal readonly ImmutableArray<GameSystemContextParameterModel> Contexts;
    internal readonly ImmutableArray<GameSystemParameterSlot> CallOrder;
    internal readonly int DeclarationPosition;

    internal GameSystemMethodModel(
        GameSystemMethodRole role,
        bool hasLeadingEntityParameter,
        ImmutableArray<GameSystemComponentParameterModel> components,
        ImmutableArray<GameSystemContextParameterModel> contexts,
        ImmutableArray<GameSystemParameterSlot> callOrder,
        int declarationPosition)
    {
        this.Role = role;
        this.HasLeadingEntityParameter = hasLeadingEntityParameter;
        this.Components = components;
        this.Contexts = contexts;
        this.CallOrder = callOrder;
        this.DeclarationPosition = declarationPosition;
    }
}

/// <summary>
/// 1つの<c>ISystem</c>実装structの解析結果。<see cref="Diagnostics"/>が1件でもあれば、このstruct分の
/// 出力を丸ごとスキップする(既存ジェネレータと同じ全か無かの方針)。
/// </summary>
internal readonly struct GameSystemResult
{
    internal readonly string? Namespace;
    internal readonly string ClassName;
    internal readonly ImmutableArray<GameSystemMethodModel> Methods;
    internal readonly ImmutableArray<Diagnostic> Diagnostics;

    /// <summary>
    /// 実行順序の決定に使う宣言位置。ADR-0033決定事項1により、実行順は「ソース上の記述順」。
    /// 同一ファイル内は<see cref="DeclarationPosition"/>(SourceSpan.Start)昇順、
    /// 複数ファイルにまたがる場合のみ<see cref="FilePath"/>のOrdinal順でタイブレークする。
    /// </summary>
    internal readonly string FilePath;
    internal readonly int DeclarationPosition;

    internal GameSystemResult(
        string? @namespace,
        string className,
        ImmutableArray<GameSystemMethodModel> methods,
        ImmutableArray<Diagnostic> diagnostics,
        string filePath,
        int declarationPosition)
    {
        this.Namespace = @namespace;
        this.ClassName = className;
        this.Methods = methods;
        this.Diagnostics = diagnostics;
        this.FilePath = filePath;
        this.DeclarationPosition = declarationPosition;
    }

    internal string ClassFullyQualifiedName => this.Namespace is null ? this.ClassName : $"{this.Namespace}.{this.ClassName}";

    internal string GlobalQualifiedName => this.Namespace is null ? $"global::{this.ClassName}" : $"global::{this.Namespace}.{this.ClassName}";
}

/// <summary>コンパイル全体で発見された1つのComponent型に、初出順で割り当てられたグローバルインデックス。</summary>
internal readonly struct GameSystemComponentInfo
{
    internal readonly int Index;
    internal readonly string TypeFullyQualifiedName;
    internal readonly string FieldName;

    internal GameSystemComponentInfo(int index, string typeFullyQualifiedName, string fieldName)
    {
        this.Index = index;
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
        this.FieldName = fieldName;
    }
}

/// <summary>
/// 1つの生成対象Archetype。<see cref="ComponentIndices"/>は<b>最初にこの構成を宣言した
/// <c>Update</c>/<c>Spawn</c>の引数順</b>であり、グローバルインデックス昇順ではない。生成される
/// <c>World.Create</c>/<c>MoveArchetype</c>のパラメータ順もこれに従うため、呼び出し側は自分が
/// 宣言したとおりの順序で引数を渡せる(内部インデックスの並びを知る必要がない)。
/// </summary>
internal readonly struct GameSystemArchetypeInfo
{
    internal readonly string Name;
    internal readonly ImmutableArray<int> ComponentIndices;
    internal readonly long Mask;

    internal GameSystemArchetypeInfo(string name, ImmutableArray<int> componentIndices, long mask)
    {
        this.Name = name;
        this.ComponentIndices = componentIndices;
        this.Mask = mask;
    }

    internal string ListFieldName => $"List{this.Name}";

    internal string MoveCommandTypeName => $"MoveTo{this.Name}Command";

    internal string MoveCommandsFieldName => $"MoveTo{this.Name}Commands";
}

/// <summary>
/// <see cref="GameSystemIndexAssigner.Assign"/>の出力。診断のあるSystemは<see cref="ValidSystems"/>
/// から除外済み、<see cref="Archetypes"/>は決定的な出力順序(コンポーネント数昇順→インデックスタプル
/// 辞書式昇順)で確定済み、<see cref="ValidSystems"/>は実行順序(記述順)で並んでいる。
/// </summary>
internal readonly struct GameSystemRegistry
{
    internal readonly ImmutableArray<GameSystemComponentInfo> ComponentsByIndex;
    internal readonly ImmutableArray<GameSystemArchetypeInfo> Archetypes;
    internal readonly ImmutableArray<GameSystemResult> ValidSystems;
    internal readonly ImmutableArray<Diagnostic> Diagnostics;

    internal GameSystemRegistry(
        ImmutableArray<GameSystemComponentInfo> componentsByIndex,
        ImmutableArray<GameSystemArchetypeInfo> archetypes,
        ImmutableArray<GameSystemResult> validSystems,
        ImmutableArray<Diagnostic> diagnostics)
    {
        this.ComponentsByIndex = componentsByIndex;
        this.Archetypes = archetypes;
        this.ValidSystems = validSystems;
        this.Diagnostics = diagnostics;
    }

    /// <summary>
    /// 指定したComponentインデックスの<b>集合</b>に一致するArchetypeを引く(順序は問わない -
    /// <see cref="GameSystemArchetypeInfo.ComponentIndices"/>は宣言順であり昇順とは限らないため、
    /// 両辺をソートして比較する)。SceneRuntimeEmitterが使う。
    /// </summary>
    internal GameSystemArchetypeInfo? FindArchetype(IReadOnlyList<int> componentIndices)
    {
        List<int> wanted = [.. componentIndices];
        wanted.Sort();

        foreach (GameSystemArchetypeInfo archetype in this.Archetypes)
        {
            if (archetype.ComponentIndices.Length != wanted.Count)
            {
                continue;
            }

            List<int> candidate = [.. archetype.ComponentIndices];
            candidate.Sort();

            bool same = true;

            for (int i = 0; i < wanted.Count; i++)
            {
                if (candidate[i] != wanted[i])
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return archetype;
            }
        }

        return null;
    }
}
