namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>スポットライトの意味上の基底。<see cref="Forward"/>は光円錐が伸びる方向。</summary>
public readonly struct SpotLightBasis(RufVector Forward) {
    /// <summary>スポットライトの円錐が伸びる方向。</summary>
    public RufVector Forward { get;}=Forward.Normalize();

    /// <summary>面からライトに向かう方向。常に<see cref="Forward"/>と反対。</summary>
    public RufVector DirectionToLight=>-this.Forward;
}
