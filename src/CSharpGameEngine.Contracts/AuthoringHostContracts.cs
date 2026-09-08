namespace CSharpGameEngine.Contracts;

public static class AuthoringHostProtocol {
    public const int CurrentVersion=1;

    public static string?Validate(AuthoringHostRequest Request){
        if(Request is null){ return "Request is required."; }
        if(Request.ProtocolVersion!=CurrentVersion){
            return $"Unsupported authoring host protocol {Request.ProtocolVersion}; expected {CurrentVersion}.";
        }
        if(Request.RequestId==Guid.Empty){ return "RequestId is required."; }
        if(!Enum.IsDefined(typeof(AuthoringHostOperation),Request.Operation)){
            return "Authoring host operation is unknown.";
        }
        if(Request.TimeoutMilliseconds<1||Request.TimeoutMilliseconds>900_000){
            return "TimeoutMilliseconds must be between 1 and 900000.";
        }
        if(Request.Arguments is null){ return "Arguments are required."; }
        if(Request.Arguments.Length>64){ return "Authoring host request has too many arguments."; }
        for(int Index=0;Index<Request.Arguments.Length;Index++){
            if(Request.Arguments[Index] is null){
                return $"Argument {Index} is null.";
            }
        }
        return null;
    }
}

public enum AuthoringHostOperation:byte { Preview,Import,Bake }
public enum AuthoringHostTerminalState:byte {
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    HostCrashed,
    VersionMismatch,
}
public enum AuthoringHostDiagnosticSeverity:byte { Info,Warning,Error }

public sealed class AuthoringHostRequest {
    public int ProtocolVersion { get;set; }=AuthoringHostProtocol.CurrentVersion;
    public Guid RequestId { get;set; }
    public AuthoringHostOperation Operation { get;set; }
    public int TimeoutMilliseconds { get;set; }=120_000;
    public string[]Arguments { get;set; }=[];
}

public sealed class AuthoringHostProgress {
    public int ProtocolVersion { get;set; }=AuthoringHostProtocol.CurrentVersion;
    public Guid RequestId { get;set; }
    public ulong Sequence { get;set; }
    public float Fraction { get;set; }
    public string Phase { get;set; }=string.Empty;
}

public sealed class AuthoringHostDiagnostic {
    public AuthoringHostDiagnosticSeverity Severity { get;set; }
    public string Code { get;set; }=string.Empty;
    public string Message { get;set; }=string.Empty;
}

public sealed class AuthoringHostResult {
    public int ProtocolVersion { get;set; }=AuthoringHostProtocol.CurrentVersion;
    public Guid RequestId { get;set; }
    public AuthoringHostTerminalState State { get;set; }
    public int ExitCode { get;set; }
    public string Output { get;set; }=string.Empty;
    public AuthoringHostProgress?Progress { get;set; }
    public AuthoringHostDiagnostic[]Diagnostics { get;set; }=[];
}
