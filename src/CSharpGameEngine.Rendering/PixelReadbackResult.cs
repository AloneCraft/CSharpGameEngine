namespace CSharpGameEngine.Rendering;
/// <summary>A CPU copy of one rendered frame, as read back from a GPU readback buffer.</summary>
public sealed class PixelReadbackResult {
    public required int Width { get; init;}
    public required int Height { get; init;}
    /// <summary>Bytes per row in <see cref="Pixels"/>. May be larger than <c>Width * BytesPerPixel</c> due to GPU row-pitch alignment.</summary>
    public required uint RowPitch { get; init;}
    public required RenderPixelFormat Format { get; init;}
    public required byte[]Pixels { get; init;}
}
