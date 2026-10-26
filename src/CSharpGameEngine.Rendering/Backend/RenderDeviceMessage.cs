namespace CSharpGameEngine.Rendering.Backend;
public enum RenderDeviceMessageSeverity:byte {
    Unknown,
    Corruption,
    Error,
    Warning,
    Information,
    Message,
}
/// <summary>プロバイダーに依存しないデバイス診断情報。コードと名前は内容を解釈しない診断データ。</summary>
public sealed record RenderDeviceMessage(RenderDeviceMessageSeverity Severity,string Category,int Code,string Identifier,string Description){
    public bool IsErrorOrWorse=>this.Severity is RenderDeviceMessageSeverity.Corruption or RenderDeviceMessageSeverity.Error;
    public override string ToString()=>$"[{this.Severity}] {this.Category}/{this.Identifier}: {this.Description}";
}
