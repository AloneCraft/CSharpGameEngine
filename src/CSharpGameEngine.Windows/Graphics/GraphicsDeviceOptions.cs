namespace CSharpGameEngine.Graphics.D3D12;

public sealed class GraphicsDeviceOptions {
    /// <summary>Force the WARP software adapter instead of enumerating hardware adapters.</summary>
    public bool UseWarp { get; init;}

    /// <summary>Enable the D3D12 debug layer before device creation.</summary>
    public bool EnableDebugLayer { get; init;}

    /// <summary>Enable GPU-based validation. Only meaningful when <see cref="EnableDebugLayer"/> is true; expensive.</summary>
    public bool EnableGpuBasedValidation { get; init;}
}
