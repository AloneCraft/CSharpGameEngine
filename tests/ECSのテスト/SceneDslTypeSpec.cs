using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// Scene DSLの型がランタイムで持つべき性質(ADR-0040)。
///
/// <para>
/// 「Scene DSLをRuntimeで実行しない」を言語機能で保証する手段は無い — <c>IScene.Root</c>のプロパティ本体は
/// 通常のC#としてILへコンパイルされてしまう。したがって保証内容は<b>「実行されない」ではなく
/// 「実行したら即座に大声で落ちる」</b>とし、<see cref="Node"/>のコンストラクターをトリップワイヤーにする。
/// 沈黙して壊れたSceneが動き続けるより望ましい。
/// </para>
///
/// <para>
/// このファイルはトリップワイヤーが実際に作動することを証明するために、意図的にScene宣言の外で
/// <see cref="Node"/>を構築する。それを禁止しているのが<c>SceneDslAnalyzer</c>のSCENE114なので、
/// ここだけ明示的に抑止する — <b>これがSCENE114を抑止してよい唯一の場所である</b>。
/// </para>
/// </summary>
#pragma warning disable SCENE114
public sealed class SceneDslTypeSpec {
    [Fact]
    public void ConstructingANodeAtRuntimeThrows(){
        Assert.Throws<NotSupportedException>(static ()=>new Node());
    }

    [Fact]
    public void ObjectInitializerSyntaxAlsoThrowsBecauseItCallsTheConstructor(){
        // フィールド初期化子(Components/Children)はコンストラクター本体より前に走るが、
        // 例外はそのあと必ず投げられる。
        Assert.Throws<NotSupportedException>(static ()=>new Node { Name="Anything" });
    }

    [Fact]
    public void TheThrownMessagePointsAtTheSupportedApi(){
        NotSupportedException Failure=Assert.Throws<NotSupportedException>(static ()=>new Node());

        Assert.Contains("InstantiateScene",Failure.Message,StringComparison.Ordinal);
    }
}
#pragma warning restore SCENE114
