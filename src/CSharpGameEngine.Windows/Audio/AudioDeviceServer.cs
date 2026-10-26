namespace CSharpGameEngine.Audio.XAudio2;

using CSharpGameEngine.Contracts;

public enum AudioDeviceState:byte {
    Unavailable,
    Creating,
    Ready,
    Recovering,
    Faulted,
    Disposed,
}

public enum AudioBackendMode:byte {
    None,
    XAudio2,
    Silent,
}

public readonly record struct AudioDeviceSnapshot(
    AudioDeviceState State,AudioBackendMode Mode,uint Generation,string?LastFailure,
    global::CSharpGameEngine.Audio.AudioVoiceRecoveryReport LastRecovery);

/// <summary>1つの XAudio2 エンジン世代だけを所有する。回復処理は明示的に行う。作成に失敗しても Ready を装わず、無音フォールバックがネイティブハンドルを所有することもない。</summary>
public sealed class AudioDeviceServer:IDisposable {
    private readonly Func<XAudio2EngineHandle>CreateEngine;
    private readonly Action<XAudio2EngineHandle>DestroyEngine;
    private readonly bool AllowSilentFallback;
    private XAudio2EngineHandle Engine;
    private bool HasEngine;

    public AudioDeviceServer(bool AllowSilentFallback=true)
        :this(
            XAudio2Backend.CreateEngine,
            static Engine=>XAudio2Backend.DestroyEngine(Engine),
            AllowSilentFallback){
    }

    public global::CSharpGameEngine.Audio.AudioMixer Mixer { get; }=new(
        global::CSharpGameEngine.Audio.AudioMixFormat.Default);

    internal AudioDeviceServer(
        Func<XAudio2EngineHandle>CreateEngine,
        Action<XAudio2EngineHandle>DestroyEngine,
        bool AllowSilentFallback){
        this.CreateEngine=CreateEngine??throw new ArgumentNullException(nameof(CreateEngine));
        this.DestroyEngine=DestroyEngine??throw new ArgumentNullException(nameof(DestroyEngine));
        this.AllowSilentFallback=AllowSilentFallback;
        this.Initialize();
    }

    public AudioDeviceSnapshot Snapshot=>new(
        this.State,this.Mode,this.Generation,this.LastFailure,this.LastRecovery);
    public AudioDeviceState State { get;private set; }=AudioDeviceState.Unavailable;
    public AudioBackendMode Mode { get;private set; }
    public uint Generation { get;private set; }
    public string?LastFailure { get;private set; }
    public global::CSharpGameEngine.Audio.AudioVoiceRecoveryReport LastRecovery { get;private set; }

    public bool TryRecreateHardware()=>this.TryRecreateHardware(default,out _);

    public bool TryRecreateHardware(
        Span<global::CSharpGameEngine.Audio.AudioVoiceRecoveryEvent>Events,
        out global::CSharpGameEngine.Audio.AudioVoiceRecoveryReport Recovery){
        ObjectDisposedException.ThrowIf(this.State==AudioDeviceState.Disposed,this);
        this.State=AudioDeviceState.Recovering;
        this.Mode=AudioBackendMode.None;
        this.ReleaseEngine();
        bool HardwareReady=this.Initialize(IsRecovery:true);
        Recovery=this.State==AudioDeviceState.Ready
            ?this.Mixer.RecoverVoices(Events)
            :default;
        this.LastRecovery=Recovery;
        return HardwareReady;
    }

    private bool Initialize(bool IsRecovery=false){
        if(!IsRecovery){ this.State=AudioDeviceState.Creating; }
        try {
            this.Engine=this.CreateEngine();
            if(this.Engine.EnginePointer==0||this.Engine.MasteringVoicePointer==0){
                throw new AudioException(
                    "CreateEngine",unchecked((int)0x80004005),
                    "validating the returned audio engine handle");
            }
            this.HasEngine=true;
            this.Generation=checked(this.Generation+1);
            this.LastFailure=null;
            this.Mode=AudioBackendMode.XAudio2;
            this.State=AudioDeviceState.Ready;
            return true;
        }
        catch(AudioException Exception){
            this.Engine=default;
            this.HasEngine=false;
            this.LastFailure=Exception.Message;
            this.Mode=this.AllowSilentFallback
                ?AudioBackendMode.Silent
                :AudioBackendMode.None;
            this.State=this.AllowSilentFallback
                ?AudioDeviceState.Ready
                :AudioDeviceState.Faulted;
            if(this.AllowSilentFallback){
                this.Generation=checked(this.Generation+1);
            }
            return false;
        }
    }

    private readonly List<XAudio2VoiceHandle>OneShots=[];

    /// <summary>要求されたワンショット再生の数（何も聞こえない無音モードでも数える）。テストと診断処理が読み取る。</summary>
    public int OneShotsRequested { get;private set; }

    /// <summary>デバイスが実際に再生を開始したワンショットの数。</summary>
    public int OneShotsStarted { get;private set; }

    /// <summary>
    /// 独自のソースボイスで <paramref name="Clip"/> を一度再生する（ADR-0077）。<paramref name="Gain"/> は最終音量（音量と距離減衰の積）。
    /// <paramref name="Pan"/> はモノラル音声を左右のステレオスピーカー間に定位する。再生を開始しなかった場合（無音モード、デバイス障害）は false を返す。再生が終わったボイスは
    /// <see cref="ReapOneShots"/> が解放する。
    /// </summary>
    public bool PlayOneShot(global::CSharpGameEngine.Audio.PcmClip Clip,float Gain,float Pan){
        ObjectDisposedException.ThrowIf(this.State==AudioDeviceState.Disposed,this);
        ArgumentNullException.ThrowIfNull(Clip);
        this.OneShotsRequested++;
        if(this.Mode!=AudioBackendMode.XAudio2||!this.HasEngine||!float.IsFinite(Gain)||Gain<=0f){ return false; }
        XAudio2VoiceHandle Voice=default;
        var Created=false;
        try {
            Voice=XAudio2Backend.CreateVoice(this.Engine,Clip.SampleRate,Clip.Channels);
            Created=true;
            Voice=XAudio2Backend.SubmitBuffer(Voice,Clip.Samples,false);
            XAudio2Backend.SetVolume(Voice,Math.Min(Gain,4f));
            if(Clip.Channels==1){ XAudio2Backend.SetMonoPan(Voice,Pan); }
            XAudio2Backend.Start(Voice);
            this.OneShots.Add(Voice);
            this.OneShotsStarted++;
            return true;
        }
        catch(AudioException Exception){
            this.LastFailure=Exception.Message;
            if(Created){ XAudio2Backend.DestroyVoice(Voice); }
            return false;
        }
    }

    /// <summary>再生が完了したワンショット用ボイスを破棄する。フレームごとに一度呼び出す。</summary>
    public void ReapOneShots(){
        for(var Index=this.OneShots.Count-1;Index>=0;Index--){
            if(XAudio2Backend.IsFinished(this.OneShots[Index])){
                XAudio2Backend.DestroyVoice(this.OneShots[Index]);
                this.OneShots.RemoveAt(Index);
            }
        }
    }

    private void ReleaseOneShots(){
        foreach(var Voice in this.OneShots){ XAudio2Backend.DestroyVoice(Voice); }
        this.OneShots.Clear();
    }

    private void ReleaseEngine(){
        this.ReleaseOneShots();
        if(!this.HasEngine){ return; }
        this.HasEngine=false;
        XAudio2EngineHandle Owned=this.Engine;
        this.Engine=default;
        this.DestroyEngine(Owned);
    }

    public void Dispose(){
        if(this.State==AudioDeviceState.Disposed){ return; }
        try {
            this.ReleaseEngine();
        }
        finally {
            this.Mixer.Dispose();
            this.Mode=AudioBackendMode.None;
            this.State=AudioDeviceState.Disposed;
        }
    }
}
