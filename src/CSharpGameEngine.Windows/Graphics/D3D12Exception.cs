namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>A native D3D12/DXGI call failed. Carries the failing API name and HRESULT for diagnostics.</summary>
public sealed class D3D12Exception:Exception {
    public D3D12Exception(string Api,int Hresult,string Context,string?Subject=null)
        :base(FormatMessage(Api,Hresult,Context,Subject)){
        this.Api= Api;
        this.HResult=Hresult;
    }

    public string Api { get;}

    private static string FormatMessage(string Api,int Hresult,string Context,string?Subject){
        var Message= $"{Api} failed while {Context}. HRESULT=0x{Hresult:X8} ({Hresult})";
        return Subject is null?Message: $"{Message} [{Subject}]";
    }
}
