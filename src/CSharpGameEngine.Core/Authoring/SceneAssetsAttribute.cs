namespace CSharpGameEngine.Authoring;

/// <summary>
/// Scene DSLから静的に参照できるAssetコンテナであることを示す(ADR-0040)。
///
/// <para>
/// Scene DSLは任意のメンバー参照を許可しない(SCENE112)。<c>Material = Assets.Floor</c>のような
/// 静的Asset参照を成立させるには、その<c>Assets</c>クラスへこの属性を付ける。参照できるのは
/// <c>static readonly</c>フィールドと<c>static</c>のget-onlyプロパティに限る — 書き換え可能なメンバーは
/// 「Sceneの内容がRuntime状態に依存する」ことを意味してしまい、Scene DSLの前提が崩れるため。
/// </para>
///
/// <para>
/// Source Generatorは参照を完全修飾名でそのまま生成コードへ出す。VSIXは初期化子を辿って
/// アセットパスの解決を試み、辿れなければギズモ表示にフォールバックする。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class SceneAssetsAttribute:Attribute {
}
