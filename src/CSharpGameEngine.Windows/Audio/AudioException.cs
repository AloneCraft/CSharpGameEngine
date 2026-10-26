namespace CSharpGameEngine.Audio.XAudio2;

/// <summary>ネイティブ XAudio2 呼び出しの失敗。失敗した API 名と診断用 HRESULT を保持する。<c>CSharpGameEngine.Graphics.D3D12.D3D12Exception</c> と同様の形式。</summary>
public sealed class AudioException:Exception {
    public AudioException(string Api,int Hresult,string Context,string?Subject=null)
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
