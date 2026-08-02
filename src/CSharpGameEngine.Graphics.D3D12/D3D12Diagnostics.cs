using TerraFX.Interop.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

internal static class D3D12Diagnostics
{
    public static void EnsureSuccess(HRESULT hr, string api, string context, string? subject = null)
    {
        if (hr.FAILED)
        {
            throw new D3D12Exception(api, hr.Value, context, subject);
        }
    }
}
