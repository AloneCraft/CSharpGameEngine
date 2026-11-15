namespace CSharpGameEngine.Import.Gltf;

public enum GltfImportDiagnosticSeverity {
    Info,
    Warning,
    Error,
}

public sealed record GltfImportDiagnostic(GltfImportDiagnosticSeverity Severity,string Message){
    public override string ToString()=> $"{this.Severity}: {this.Message}";
}
