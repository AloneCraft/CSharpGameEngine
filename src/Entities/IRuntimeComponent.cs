namespace CSharpGameEngine.Entities;

/// <summary>
/// エンジンが実行時に埋めるComponent。作者がScene DSLの<c>Components</c>へ書くことはなく、
/// 書くとSCENE119で弾かれる(<see cref="Parent"/>/<see cref="SceneNodeTag"/>と同じ扱い)。
///
/// <para>
/// <b>なぜ型で分けるのか</b>: Component構造体は「作者が設定する値」と「エンジンが実行時に
/// 差し込むハンドル」(<c>StageRenderer</c>/<c>Win32Window</c>等)を混在させがちだが、後者を
/// InspectorやComponentパレットへ編集可能として並べるのは誤りである。属性で印を付けるのではなく
/// 型そのものを分けることで、<c>ISystem</c>が構造的推論の入口である前例と同じ形になり、
/// 作者が書く側の型定義を一切汚さずに区別できる。
/// </para>
///
/// <para>
/// 随伴先を持たない(単独で<c>World.Create</c>されるだけの)実行時Componentはこの非genericの方を
/// 実装する。Scene DSLからの自動付与を受けるものは<see cref="IRuntimeComponent{TAuthored}"/>を使う。
/// </para>
/// </summary>
public interface IRuntimeComponent {
}

/// <summary>
/// <typeparamref name="TAuthored"/>を宣言したNodeへ、Source Generatorが自動的に付与する実行時Component。
/// <see cref="Parent"/>の自動付与と同じ仕組み(<c>SceneInstantiateEmitter.BuildEffectiveComponents</c>)に乗る。
///
/// <para>
/// 1つの<typeparamref name="TAuthored"/>に対して実装は1つでなければならない。複数が主張すると
/// どちらを付与するかが非決定になるためSYSGEN027で弾かれる。
/// </para>
/// </summary>
/// <typeparam name="TAuthored">この実行時状態が随伴する、作者が宣言する側のComponent型。</typeparam>
public interface IRuntimeComponent<TAuthored>:IRuntimeComponent where TAuthored:struct {
}
