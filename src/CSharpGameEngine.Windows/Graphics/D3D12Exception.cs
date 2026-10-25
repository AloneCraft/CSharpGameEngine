using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Backend;
namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>A native D3D12/DXGI call failed. Carries the failing API name and HRESULT for diagnostics.</summary>
public sealed class D3D12Exception:RenderBackendFailureException {
    public D3D12Exception(string Api,int Hresult,string Context,string?Subject=null)
        :base(Api,Hresult,FormatMessage(Api,Hresult,Context,Subject),RecoveryReasonFor(Hresult)){
    }

    public string Api=>this.Operation;

    private static RenderRecoveryReason?RecoveryReasonFor(int NativeCode)=>NativeCode switch {
        unchecked((int)0x887A0005)=>RenderRecoveryReason.DeviceRemoved,
        unchecked((int)0x887A0006)=>RenderRecoveryReason.DeviceRemoved,
        unchecked((int)0x887A0007)=>RenderRecoveryReason.DeviceReset,
        _=>null,
    };
    private static string FormatMessage(string Api,int Hresult,string Context,string?Subject){
        var Message= $"{Api} failed while {Context}. HRESULT=0x{Hresult:X8} ({Hresult})";
        return Subject is null?Message: $"{Message} [{Subject}]";
    }
}
