namespace CSharpGameEngine.Core;

public readonly struct Diagnostic(string Code,DiagnosticSeverity Severity,string Message,SourceLocation Location) {
    public string Code { get;}=ValidateRequired(Code,nameof(Code),"Diagnostic code is required.");

    private static string ValidateRequired(string Value,string ParameterName,string ErrorMessage){
        if(string.IsNullOrWhiteSpace(Value)){
            throw new ArgumentException(ErrorMessage,ParameterName);
        }

        return Value;
    }

    public DiagnosticSeverity Severity { get;}=Severity;

    public string Message { get;}=ValidateRequired(Message,nameof(Message),"Diagnostic message is required.");

    public SourceLocation Location { get;}=Location;

    public override string ToString(){
        if(this.Location.IsUnknown){
            return $"[{this.Severity}] {this.Code}: {this.Message}";
        }

        return $"[{this.Severity}] {this.Code}: {this.Message} @ {this.Location}";
    }
}
