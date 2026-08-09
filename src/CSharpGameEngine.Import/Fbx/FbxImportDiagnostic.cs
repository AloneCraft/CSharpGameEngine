namespace CSharpGameEngine.Import.Fbx;

public enum FbxImportDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record FbxImportDiagnostic(FbxImportDiagnosticSeverity Severity, string Message)
{
    public override string ToString() => $"{Severity}: {Message}";
}
