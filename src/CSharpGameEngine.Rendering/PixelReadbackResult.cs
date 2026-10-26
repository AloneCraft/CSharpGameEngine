namespace CSharpGameEngine.Rendering;
/// <summary>GPU の読み戻しバッファから取得した、描画済みフレーム1つ分の CPU コピー。</summary>
public sealed class PixelReadbackResult {
    public required int Width { get; init;}
    public required int Height { get; init;}
    /// <summary><see cref="Pixels"/> の1行あたりのバイト数。GPU の行ピッチ調整により、<c>Width * BytesPerPixel</c> より大きい場合がある。</summary>
    public required uint RowPitch { get; init;}
    public required RenderPixelFormat Format { get; init;}
    public required byte[]Pixels { get; init;}
}
