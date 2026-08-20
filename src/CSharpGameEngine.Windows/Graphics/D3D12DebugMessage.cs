using TerraFX.Interop.DirectX;

namespace CSharpGameEngine.Graphics.D3D12;

public sealed record D3D12DebugMessage(D3D12_MESSAGE_SEVERITY Severity,D3D12_MESSAGE_CATEGORY Category,D3D12_MESSAGE_ID Id,string Description){
    public bool IsErrorOrWorse=> this.Severity is D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_CORRUPTION or D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_ERROR;

    public override string ToString()=> $"[{this.Severity}] {this.Category}/{this.Id}: {this.Description}";
}
