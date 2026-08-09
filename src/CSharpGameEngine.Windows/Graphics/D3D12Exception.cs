namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>A native D3D12/DXGI call failed. Carries the failing API name and HRESULT for diagnostics.</summary>
public sealed class D3D12Exception : Exception
{
    public D3D12Exception(string api, int hresult, string context, string? subject = null)
        : base(FormatMessage(api, hresult, context, subject))
    {
        Api = api;
        HResult = hresult;
    }

    public string Api { get; }

    private static string FormatMessage(string api, int hresult, string context, string? subject)
    {
        string message = $"{api} failed while {context}. HRESULT=0x{hresult:X8} ({hresult})";
        return subject is null ? message : $"{message} [{subject}]";
    }
}
