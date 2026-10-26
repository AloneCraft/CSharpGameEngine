using CSharpGameEngine.Rendering.Backend;
using TerraFX.Interop.DirectX;

namespace CSharpGameEngine.Graphics.D3D12;

public sealed record D3D12DebugMessage(D3D12_MESSAGE_SEVERITY Severity,D3D12_MESSAGE_CATEGORY Category,D3D12_MESSAGE_ID Id,string Description){
    public bool IsErrorOrWorse=> this.Severity is D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_CORRUPTION or D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_ERROR;

    public override string ToString()=> $"[{this.Severity}] {this.Category}/{this.Id}: {this.Description}";
    internal RenderDeviceMessage ToRenderDeviceMessage()=>new(this.Severity switch {
        D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_CORRUPTION=>RenderDeviceMessageSeverity.Corruption,
        D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_ERROR=>RenderDeviceMessageSeverity.Error,
        D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_WARNING=>RenderDeviceMessageSeverity.Warning,
        D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_INFO=>RenderDeviceMessageSeverity.Information,
        D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_MESSAGE=>RenderDeviceMessageSeverity.Message,
        _=>RenderDeviceMessageSeverity.Unknown,
    },this.Category.ToString(),(int)this.Id,this.Id.ToString(),this.Description);

}
