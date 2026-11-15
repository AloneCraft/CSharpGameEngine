// 名前空間を CSharpGameEngine.Scene にしてはならない: CSharpGameEngine.Import.Fbx が Assimp の
// `Scene` 型を単純名で使っており、共通の祖先である CSharpGameEngine 配下に Scene 名前空間ができると
// そちらが優先されて CS0118 でビルドが壊れる(実測済み)。
namespace CSharpGameEngine.Authoring;

/// <summary>
/// Scene DSLの宣言的subsetに対する<b>明示的な脱出口</b>(ADR-0040)。
///
/// <para>
/// Scene DSLは静的に意味を確定できる式しか許可しないため、任意のメソッド呼び出しはSCENE111で弾かれる。
/// しかし<c>Camera.CreateFirstPerson(new Vector3(0f, 4.62f, 0f))</c>のような既存のstaticファクトリを
/// 全部オブジェクト初期化子へ展開させるのは実用的でない。この属性を付けたstaticメソッドだけは
/// Scene DSLから呼べるようになり、Source Generatorは呼び出しをそのまま(完全修飾して)生成コードへ出す。
/// </para>
///
/// <para>
/// <b>トレードオフ</b>: 戻り値はコンパイル時に評価できないため、VSIXのInspectorはその値を読み取り専用
/// として表示する(数値ピッカーで直接編集できない)。引数自体はScene DSLの値式でなければならない
/// (SCENE113)。
/// </para>
///
/// <para>
/// <c>CSharpGameEngine.Entities</c>ではなく<c>CSharpGameEngine.Core</c>に置いているのは、
/// <c>CSharpGameEngine.Rendering</c>(<c>RenderCamera</c>の定義元)がEntitiesを参照しておらずCoreだけを
/// 参照しているため。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method,AllowMultiple=false,Inherited=false)]
public sealed class SceneFactoryAttribute:Attribute {
}
