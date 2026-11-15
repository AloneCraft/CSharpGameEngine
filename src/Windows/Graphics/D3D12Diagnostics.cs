using TerraFX.Interop.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

internal static class D3D12Diagnostics {
    public static void EnsureSuccess(HRESULT Hr,string Api,string Context,string?Subject=null){
        if(Hr.FAILED){
            throw new D3D12Exception(Api,Hr.Value,Context,Subject);
        }
    }
}
