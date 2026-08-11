using TerraFX.Interop.DirectX;

namespace CSharpGameEngine.Graphics.D3D12;

public sealed record D3D12DebugMessage(D3D12_MESSAGE_SEVERITY Severity,D3D12_MESSAGE_CATEGORY Category,D3D12_MESSAGE_ID Id,string Description){
    public bool IsErrorOrWorse=>
        Severity is D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_CORRUPTION or D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_ERROR;

    public override string ToString()=> $"[{Severity}] {Category}/{Id}: {Description}";
}
