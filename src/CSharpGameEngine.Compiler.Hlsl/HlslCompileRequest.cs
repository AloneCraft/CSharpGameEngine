namespace CSharpGameEngine.Compiler.Hlsl;

public sealed class HlslCompileRequest
{
    public required string FilePath { get; init; }

    public required string EntryPoint { get; init; }

    /// <summary>DXC shader profile, e.g. <c>vs_6_0</c> or <c>ps_6_0</c>.</summary>
    public required string TargetProfile { get; init; }

    public IReadOnlyList<HlslDefine> Defines { get; init; } = [];

    /// <summary>When true, requests embedded debug info and disables optimization (DXC <c>-Zi -Od</c>).</summary>
    public bool EnableDebugInfo { get; init; }
}
