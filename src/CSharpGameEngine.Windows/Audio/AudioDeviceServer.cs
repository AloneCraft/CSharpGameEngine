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

/// <summary>
/// Owns exactly one XAudio2 engine generation. Recovery is explicit; a failed create never
/// masquerades as Ready and silent fallback owns no native handle.
/// </summary>
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

    private void ReleaseEngine(){
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
