namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// ディレクショナルライトの意味上の基底。<see cref="Forward"/>は光子が進むライトの伝播方向であり、光源に向かう方向ではない。
/// 光源に向かう方向は<see cref="DirectionToLight"/>を参照。ポイントライトにForwardはない
///（Docs/Specifications/Spatial-RUF.mdの「Point Light」を参照）。
/// </summary>
public readonly struct DirectionalLightBasis(RufVector Forward) {
    /// <summary>光が進む方向。</summary>
    public RufVector Forward { get;}=Forward.Normalize();

    /// <summary>面からライトに向かう方向。常に<see cref="Forward"/>と反対。</summary>
    public RufVector DirectionToLight=>-this.Forward;
}
