namespace CSharpGameEngine.Entities;

/// <summary>
/// Scene定義。<b>これはRuntime instanceではなく宣言である</b>(ADR-0040)。
///
/// <para>
/// <c>Root</c>の本体はC#として書かれるが<b>実行されない</b>。Roslynが<c>ObjectCreationExpression</c>として
/// 読み取り、Source Generatorが直接Runtime ECS構築コード(<c>ArchetypeNN.Create</c>の直接呼び出し)を
/// 生成する。<c>new MainScene()</c>も行われない — SGが生成partialへprivateコンストラクターを出すため
/// 外部から構築できず、<see cref="Node"/>のコンストラクターは実行されると必ず例外を投げる。
/// </para>
///
/// <para>
/// Runtimeで生成するには<c>World.InstantiateScene&lt;TScene&gt;()</c>を使う。同じScene定義を何度でも
/// instantiateでき、それぞれ独立した<c>SceneInstance</c>になる。
/// </para>
/// </summary>
public interface IScene {
    /// <summary>このSceneの根ノード。式本体(<c>=&gt; new Node { … }</c>)で宣言する。</summary>
    Node Root { get;}
}
