using TerraFX.Interop.Windows;

namespace CSharpGameEngine.Audio.XAudio2;

internal static class AudioDiagnostics {
    public static void EnsureSuccess(HRESULT Hr,string Api,string Context,string?Subject=null){
        if(Hr.FAILED){
            throw new AudioException(Api,Hr.Value,Context,Subject);
        }
    }
}
