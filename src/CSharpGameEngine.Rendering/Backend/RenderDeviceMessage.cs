namespace CSharpGameEngine.Rendering.Backend;
public enum RenderDeviceMessageSeverity:byte {
    Unknown,
    Corruption,
    Error,
    Warning,
    Information,
    Message,
}
/// <summary>Provider-independent device diagnostics. Codes and names are opaque diagnostic data.</summary>
public sealed record RenderDeviceMessage(RenderDeviceMessageSeverity Severity,string Category,int Code,string Identifier,string Description){
    public bool IsErrorOrWorse=>this.Severity is RenderDeviceMessageSeverity.Corruption or RenderDeviceMessageSeverity.Error;
    public override string ToString()=>$"[{this.Severity}] {this.Category}/{this.Identifier}: {this.Description}";
}
