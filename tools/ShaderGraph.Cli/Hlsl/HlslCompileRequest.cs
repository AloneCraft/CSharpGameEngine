namespace CSharpGameEngine.Compiler.Hlsl;

public sealed class HlslCompileRequest {
    public required string FilePath { get; init;}

    public required string EntryPoint { get; init;}

    /// <summary>DXC のシェーダープロファイル。例：<c>vs_6_0</c> または <c>ps_6_0</c>。</summary>
    public required string TargetProfile { get; init;}

    public IReadOnlyList<HlslDefine>Defines { get; init;}=[];

    /// <summary>true の場合、埋め込みデバッグ情報を要求して最適化を無効にする (DXC <c>-Zi -Od</c>)。</summary>
    public bool EnableDebugInfo { get; init;}
}
