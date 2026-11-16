namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// カメラの意味的な基底。<see cref="Forward"/> は視線/注視方向、つまりカメラが見ている向きであり、生の「ベクトル」ではない（このリポジトリが <c>Direction</c>/<c>Vector</c> のような限定のない名前を避ける理由は Docs/Specifications/Spatial-RUF.md の「Public APIの命名」を参照）。具体的なエンジンの <c>Camera</c> 型（例：<c>CSharpGameEngine.Rendering.View.RenderCamera</c>）とは意図的に分離している。これは「カメラの基底が何を意味するか」を表す RUF 空間のスキーマにすぎず、ランタイムのカメラコントローラーではない。
/// </summary>
public readonly struct CameraBasis(RufVector Forward,RufVector Up) {
    /// <summary>カメラが向く方向。Transform Conventionによって異なる場合がある、ビュー行列内部の前方軸とは異なる。</summary>
    public RufVector Forward { get;}=Forward.Normalize();

    /// <summary><see cref="Forward"/>の正規直交基底を構築するために使う、カメラの基準Up方向。</summary>
    public RufVector Up { get;}=Up;
}
