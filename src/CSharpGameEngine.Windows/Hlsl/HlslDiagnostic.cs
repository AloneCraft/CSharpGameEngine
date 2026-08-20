namespace CSharpGameEngine.Compiler.Hlsl;

public enum HlslDiagnosticSeverity {
    Info,
    Warning,
    Error,
}

public sealed record HlslDiagnostic(
    string FileName,
    int Line,
    int Column,
    HlslDiagnosticSeverity Severity,
    string Message){
    public override string ToString()=>
        this.Line>0
            ? $"{this.FileName}({this.Line},{this.Column}): {this.Severity}: {this.Message}"
            : $"{this.FileName}: {this.Severity}: {this.Message}";
}
