using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// 正規化(コンポーネント型の完全修飾名によるOrdinalソート)後の、1コンポーネントプロパティ分の情報。
/// </summary>
/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する。
/// </remarks>
internal readonly struct EcsComponentPropertyModel
{
    internal readonly string PropertyName;
    internal readonly string ParameterName;
    internal readonly string ComponentTypeFullyQualifiedName;
    internal readonly bool IsReadOnly;

    internal EcsComponentPropertyModel(string propertyName, string parameterName, string componentTypeFullyQualifiedName, bool isReadOnly)
    {
        this.PropertyName = propertyName;
        this.ParameterName = parameterName;
        this.ComponentTypeFullyQualifiedName = componentTypeFullyQualifiedName;
        this.IsReadOnly = isReadOnly;
    }
}

/// <summary>
/// 1つの<c>[EcsArchetype]</c>構造体の解析結果。<see cref="Diagnostics"/>が1件でもあれば
/// EcsArchetypeEmitterはこのアーキタイプのソース生成をスキップする。
/// </summary>
/// <remarks>
/// <see cref="ImmutableArray{T}"/>の既定の等価性は要素比較ではなく参照比較であるため、
/// このモデルをIncrementalValuesProviderのキャッシュキーとして使う場合、実際には
/// 内容が同じでも「変化した」と判定されキャッシュが効かないことがある。これは生成結果の
/// 正しさには影響しない(単に再生成が余分に走るだけ)ため、v1では許容する既知の制約とする。
/// </remarks>
internal readonly struct EcsArchetypeAnalysisResult
{
    internal readonly string? Namespace;
    internal readonly string StructName;
    internal readonly ImmutableArray<EcsComponentPropertyModel> NormalizedProperties;
    internal readonly ImmutableArray<Diagnostic> Diagnostics;

    internal EcsArchetypeAnalysisResult(
        string? @namespace,
        string structName,
        ImmutableArray<EcsComponentPropertyModel> normalizedProperties,
        ImmutableArray<Diagnostic> diagnostics)
    {
        this.Namespace = @namespace;
        this.StructName = structName;
        this.NormalizedProperties = normalizedProperties;
        this.Diagnostics = diagnostics;
    }
}
