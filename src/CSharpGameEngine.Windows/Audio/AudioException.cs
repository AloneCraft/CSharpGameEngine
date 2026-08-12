namespace CSharpGameEngine.Audio.XAudio2;

/// <summary>A native XAudio2 call failed. Carries the failing API name and HRESULT for diagnostics. Mirrors <c>CSharpGameEngine.Graphics.D3D12.D3D12Exception</c>.</summary>
public sealed class AudioException:Exception {
    public AudioException(string Api,int Hresult,string Context,string?Subject=null)
        :base(FormatMessage(Api,Hresult,Context,Subject)){
        this.Api= Api;
        HResult=Hresult;
    }

    public string Api { get;}

    private static string FormatMessage(string Api,int Hresult,string Context,string?Subject){
        var Message= $"{Api} failed while {Context}. HRESULT=0x{Hresult:X8} ({Hresult})";
        return Subject is null?Message: $"{Message} [{Subject}]";
    }
}
