namespace CSharpGameEngine.Compiler.Hlsl;

public sealed class HlslCompileResult
{
    private static readonly byte[] EmptyBytecode = [];

    private HlslCompileResult(bool success, byte[] bytecode, IReadOnlyList<HlslDiagnostic> diagnostics)
    {
        Success = success;
        Bytecode = bytecode;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }

    /// <summary>DXIL bytecode. Always empty when <see cref="Success"/> is false.</summary>
    public byte[] Bytecode { get; }

    public IReadOnlyList<HlslDiagnostic> Diagnostics { get; }

    public static HlslCompileResult Succeeded(byte[] bytecode, IReadOnlyList<HlslDiagnostic> diagnostics) =>
        bytecode.Length == 0
            ? throw new ArgumentException("A successful compile result must carry non-empty bytecode.", nameof(bytecode))
            : new HlslCompileResult(true, bytecode, diagnostics);

    public static HlslCompileResult Failed(IReadOnlyList<HlslDiagnostic> diagnostics) =>
        new(false, EmptyBytecode, diagnostics);
}
