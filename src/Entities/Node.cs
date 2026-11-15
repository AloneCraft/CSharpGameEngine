namespace CSharpGameEngine.Entities;

/// <summary>
/// Scene DSLの宣言的ノード。<b>実行時には決して構築されない</b>(ADR-0040)。
///
/// <para>
/// <see cref="Components"/>が<c>object[]</c>なのは意図的である。異なるComponent型を1つのcollection
/// expressionへ入れるための<b>構文上の容器</b>としてのみ使い、Scene DSLは実行されないので
/// structをobjectへ入れる際のboxingはRuntimeコストにならない。型安全性はRoslyn Analyzer(SCENE107等)が
/// 保証する — <c>object[]</c>は<c>null</c>・文字列・<see cref="Node"/>の混入を型システムでは止められない
/// ため、Analyzerが明示的に弾く。
/// </para>
///
/// <para>
/// コンストラクターが常に例外を投げるのは<b>トリップワイヤー</b>である。「Scene DSLはRuntimeで実行しない」
/// を言語機能で保証する手段は無い(<c>Root</c>のプロパティ本体はILへコンパイルされてしまう)ので、
/// 保証内容は「実行されない」ではなく<b>「実行したら即座に大声で落ちる」</b>とする。沈黙して壊れた
/// Sceneが動き続けるより望ましい。
/// </para>
/// </summary>
public sealed class Node {
    public Node(){
        throw new NotSupportedException(
            "Scene DSLはコンパイル時にのみ解釈されます。Nodeを実行時に構築することはできません。"
            +"Sceneを生成するにはWorld.InstantiateScene<TScene>()を使用してください。");
    }

    /// <summary>ノード名。コンパイル時定数の文字列に限る(SCENE105)。一意性は保証しない。</summary>
    public string?Name { get;init;}

    /// <summary>このノードに載せるRuntime Component。要素は非nullableなstructのオブジェクト生成式に限る。</summary>
    public object[]Components { get;init;}=[];

    /// <summary>子ノード。生成コードでは自動的に<see cref="Parent"/>が付与される。</summary>
    public Node[]Children { get;init;}=[];
}
