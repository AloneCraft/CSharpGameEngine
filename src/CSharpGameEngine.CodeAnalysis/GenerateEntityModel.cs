using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// GenerateEntityGeneratorが選択する物理ストレージ方式のGenerator側ミラー。
/// <c>CSharpGameEngine.Generation.Abstractions</c>の<c>EntityStorageKind</c>への
/// アセンブリ参照を持たないため、属性引数から解決した値をこの内部enumへ写し取る。
/// 値(1/2)は<c>EntityStorageKind</c>の序数と一致させること。
/// </summary>
[Flags]
internal enum GenerateEntityStorageKind
{
    None = 0,
    Aos = 1,
    EcsSoa = 2
}

/// <summary>
/// 1コンポーネントプロパティ分の情報。<see cref="NormalizedIndex"/>は完全修飾名のOrdinalソート順、
/// <see cref="DeclarationIndex"/>はソース宣言順であり、用途に応じて使い分ける
/// (詳細は<see cref="GenerateEntityAnalysisResult"/>のドキュメントを参照)。
/// </summary>
/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する。
/// </remarks>
internal readonly struct GenerateEntityComponentModel
{
    internal readonly string PropertyName;
    internal readonly string ComponentTypeFullyQualifiedName;
    internal readonly int NormalizedIndex;
    internal readonly int DeclarationIndex;

    internal GenerateEntityComponentModel(
        string propertyName,
        string componentTypeFullyQualifiedName,
        int normalizedIndex,
        int declarationIndex)
    {
        this.PropertyName = propertyName;
        this.ComponentTypeFullyQualifiedName = componentTypeFullyQualifiedName;
        this.NormalizedIndex = normalizedIndex;
        this.DeclarationIndex = declarationIndex;
    }
}

/// <summary>
/// nested <c>[GenerateEntity]</c>構造体の1つの外側containing型。Emitterが生成コード内で
/// 同じcontaining型を<c>partial {Keyword} {Name} {{ ... }}</c>として再度開くために使う。
/// </summary>
internal readonly struct GenerateEntityContainingTypeModel
{
    internal readonly string Name;
    internal readonly string Keyword;

    internal GenerateEntityContainingTypeModel(string name, string keyword)
    {
        this.Name = name;
        this.Keyword = keyword;
    }
}

/// <summary>
/// 1つの<c>[GenerateEntity]</c>構造体の解析結果。<see cref="Diagnostics"/>が1件でもあれば
/// Emitterはこの型のソース生成をすべてスキップする。
/// </summary>
/// <remarks>
/// <see cref="DeclarationOrderedComponents"/>と<see cref="NormalizedComponents"/>は同じ要素集合を
/// 異なる順序で保持する2つの配列である。ユーザー向け生成メンバー(CreateInfoのフィールド、
/// partialプロパティ実装、ChunkViewの各プロパティ等)は宣言順(<see cref="DeclarationOrderedComponents"/>)で
/// 出力し、正規化が必要な箇所(NormalizedSignature、SoAマーカー構造体のComponentTypes/CreateColumns、
/// World.GetComponentRefへ渡すcolumnIndex)は各要素の<see cref="GenerateEntityComponentModel.NormalizedIndex"/>
/// を使う。宣言順のプロパティにNormalizedIndexではなくDeclarationIndexを誤って使うと、
/// 実行時に誤ったコンポーネント列を参照する静かなバグになるため、Emitter実装時は特に注意する。
/// </remarks>
internal readonly struct GenerateEntityAnalysisResult
{
    internal readonly string? Namespace;
    internal readonly ImmutableArray<GenerateEntityContainingTypeModel> ContainingTypeChain;
    internal readonly string StructName;
    internal readonly GenerateEntityStorageKind StorageKind;
    internal readonly ImmutableArray<GenerateEntityComponentModel> DeclarationOrderedComponents;
    internal readonly ImmutableArray<GenerateEntityComponentModel> NormalizedComponents;
    internal readonly ImmutableArray<Diagnostic> Diagnostics;

    internal GenerateEntityAnalysisResult(
        string? @namespace,
        ImmutableArray<GenerateEntityContainingTypeModel> containingTypeChain,
        string structName,
        GenerateEntityStorageKind storageKind,
        ImmutableArray<GenerateEntityComponentModel> declarationOrderedComponents,
        ImmutableArray<GenerateEntityComponentModel> normalizedComponents,
        ImmutableArray<Diagnostic> diagnostics)
    {
        this.Namespace = @namespace;
        this.ContainingTypeChain = containingTypeChain;
        this.StructName = structName;
        this.StorageKind = storageKind;
        this.DeclarationOrderedComponents = declarationOrderedComponents;
        this.NormalizedComponents = normalizedComponents;
        this.Diagnostics = diagnostics;
    }

    internal bool HasAos => (this.StorageKind & GenerateEntityStorageKind.Aos) != 0;

    internal bool HasSoa => (this.StorageKind & GenerateEntityStorageKind.EcsSoa) != 0;

    /// <summary>
    /// 両バックエンドを1つの宣言から生成する比較・計測用モード。生成される型名が衝突しないよう
    /// <c>{Type}Aos*</c>/<c>{Type}Soa*</c>へ切り替わり、Facadeは<see cref="HasAos"/>側にのみ生成される。
    /// </summary>
    internal bool IsCombined => this.HasAos && this.HasSoa;
}
