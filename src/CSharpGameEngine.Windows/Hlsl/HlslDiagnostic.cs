namespace CSharpGameEngine.Compiler.Hlsl;

public enum HlslDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record HlslDiagnostic(
    string FileName,
    int Line,
    int Column,
    HlslDiagnosticSeverity Severity,
    string Message)
{
    public override string ToString() =>
        Line > 0
            ? $"{FileName}({Line},{Column}): {Severity}: {Message}"
            : $"{FileName}: {Severity}: {Message}";
}
