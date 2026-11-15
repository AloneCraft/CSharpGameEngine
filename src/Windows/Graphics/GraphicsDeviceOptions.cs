namespace CSharpGameEngine.Graphics.D3D12;

public sealed class GraphicsDeviceOptions {
    /// <summary>ハードウェアアダプターを列挙する代わりに、WARP ソフトウェアアダプターを強制する。</summary>
    public bool UseWarp { get; init;}

    /// <summary>デバイス作成前に D3D12 デバッグレイヤーを有効にする。</summary>
    public bool EnableDebugLayer { get; init;}

    /// <summary>GPU ベースの検証を有効にする。<see cref="EnableDebugLayer"/> が true の場合だけ意味を持ち、処理コストが高い。</summary>
    public bool EnableGpuBasedValidation { get; init;}
}
