namespace CSharpGameEngine.Core;

public readonly struct Diagnostic {
    public Diagnostic(string Code,DiagnosticSeverity Severity,string Message,SourceLocation Location){
        if(string.IsNullOrWhiteSpace(Code)){
            throw new ArgumentException("Diagnostic code is required.",nameof(Code));
        }

        if(string.IsNullOrWhiteSpace(Message)){
            throw new ArgumentException("Diagnostic message is required.",nameof(Message));
        }

        this.Code=Code;
        this.Severity=Severity;
        this.Message=Message;
        this.Location=Location;
    }

    public string Code { get;}

    public DiagnosticSeverity Severity { get;}

    public string Message { get;}

    public SourceLocation Location { get;}

    public override string ToString(){
        if(Location.IsUnknown){
            return $"[{Severity}] {Code}: {Message}";
        }

        return $"[{Severity}] {Code}: {Message} @ {Location}";
    }
}
