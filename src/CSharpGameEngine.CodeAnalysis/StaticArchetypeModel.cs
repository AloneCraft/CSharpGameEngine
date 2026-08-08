using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する(既存Model群と同じ理由)。
/// </remarks>
internal readonly struct StaticArchetypeComponentParameterModel
{
    internal readonly string ParameterName;
    internal readonly string TypeFullyQualifiedName;

    internal StaticArchetypeComponentParameterModel(string parameterName, string typeFullyQualifiedName)
    {
        this.ParameterName = parameterName;
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
    }
}

/// <summary>1つの<c>Update</c>オーバーロードの解析結果。パラメータ順(宣言順)を保った<see cref="Components"/>が、そのオーバーロードが対象とするComponent集合そのものになる。</summary>
internal readonly struct StaticArchetypeUpdateOverloadModel
{
    internal readonly bool HasLeadingEntityParameter;
    internal readonly string EntityParameterName;
    internal readonly ImmutableArray<StaticArchetypeComponentParameterModel> Components;
    internal readonly int MemberIndex;

    internal StaticArchetypeUpdateOverloadModel(
        bool hasLeadingEntityParameter,
        string entityParameterName,
        ImmutableArray<StaticArchetypeComponentParameterModel> components,
        int memberIndex)
    {
        this.HasLeadingEntityParameter = hasLeadingEntityParameter;
        this.EntityParameterName = entityParameterName;
        this.Components = components;
        this.MemberIndex = memberIndex;
    }
}

/// <summary>1つの<c>ISystem</c>実装structの解析結果。<see cref="Diagnostics"/>が1件でもあれば、このstruct分の出力を丸ごとスキップする(既存GameSystemGeneratorと同じ全か無かの方針)。</summary>
internal readonly struct StaticArchetypeSystemResult
{
    internal readonly string? Namespace;
    internal readonly string ClassName;
    internal readonly ImmutableArray<StaticArchetypeUpdateOverloadModel> Overloads;
    internal readonly ImmutableArray<Diagnostic> Diagnostics;

    internal StaticArchetypeSystemResult(
        string? @namespace,
        string className,
        ImmutableArray<StaticArchetypeUpdateOverloadModel> overloads,
        ImmutableArray<Diagnostic> diagnostics)
    {
        this.Namespace = @namespace;
        this.ClassName = className;
        this.Overloads = overloads;
        this.Diagnostics = diagnostics;
    }

    internal string ClassFullyQualifiedName => this.Namespace is null ? this.ClassName : $"{this.Namespace}.{this.ClassName}";
}

/// <summary>コンパイル全体で発見された1つのComponent型に、初出順で割り当てられたグローバルインデックス。</summary>
internal readonly struct StaticArchetypeComponentInfo
{
    internal readonly int Index;
    internal readonly string TypeFullyQualifiedName;

    internal StaticArchetypeComponentInfo(int index, string typeFullyQualifiedName)
    {
        this.Index = index;
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
    }
}

/// <summary>1つの生成対象Archetype。<see cref="ComponentIndices"/>はグローバルインデックスの昇順。</summary>
internal readonly struct StaticArchetypeArchetypeInfo
{
    internal readonly string Name;
    internal readonly ImmutableArray<int> ComponentIndices;
    internal readonly long Mask;

    internal StaticArchetypeArchetypeInfo(string name, ImmutableArray<int> componentIndices, long mask)
    {
        this.Name = name;
        this.ComponentIndices = componentIndices;
        this.Mask = mask;
    }

    internal string ListFieldName => $"List{this.Name}";
}

/// <summary>
/// <see cref="StaticArchetypeIndexAssigner.Assign"/>の出力。診断のあるSystemは<see cref="ValidSystems"/>
/// から除外済み、<see cref="Archetypes"/>は決定的な出力順序(コンポーネント数昇順→インデックスタプル
/// 辞書式昇順)で確定済み。
/// </summary>
internal readonly struct StaticArchetypeRegistry
{
    internal readonly ImmutableArray<StaticArchetypeComponentInfo> ComponentsByIndex;
    internal readonly ImmutableArray<StaticArchetypeArchetypeInfo> Archetypes;
    internal readonly ImmutableArray<StaticArchetypeSystemResult> ValidSystems;

    internal StaticArchetypeRegistry(
        ImmutableArray<StaticArchetypeComponentInfo> componentsByIndex,
        ImmutableArray<StaticArchetypeArchetypeInfo> archetypes,
        ImmutableArray<StaticArchetypeSystemResult> validSystems)
    {
        this.ComponentsByIndex = componentsByIndex;
        this.Archetypes = archetypes;
        this.ValidSystems = validSystems;
    }
}
