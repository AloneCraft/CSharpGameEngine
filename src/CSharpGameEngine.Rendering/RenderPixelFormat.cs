namespace CSharpGameEngine.Rendering;
/// <summary>グラフィックスプロバイダーに依存しない CPU 読み戻し形式。</summary>
public enum RenderPixelFormat:byte {
    Unknown=0,
    Rgba8Unorm=1,
    Bgra8Unorm=2,
}
