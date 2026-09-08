using CSharpGameEngine.Audio.XAudio2;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Audio.Tests;

public sealed class AudioDeviceServerTests {
    [Fact]
    public void FailedCreationPublishesSilentThenExplicitRecreatePublishesReady(){
        int CreateCount=0;
        int DestroyCount=0;
        using AudioDeviceServer Server=new(
            ()=>{
                CreateCount++;
                if(CreateCount==1){
                    throw new AudioException("Create",-1,"fault injection");
                }
                return new XAudio2EngineHandle((nint)1,(nint)2);
            },
            _=>DestroyCount++,
            AllowSilentFallback:true);

        Assert.Equal(AudioDeviceState.Ready,Server.State);
        Assert.Equal(AudioBackendMode.Silent,Server.Mode);
        Assert.Equal(1u,Server.Generation);
        Assert.NotNull(Server.LastFailure);
        PlatformDebugAudioSnapshot Silent=Server.CaptureDebugSnapshot();
        Assert.Equal(PlatformDebugSubsystemState.Ready,Silent.State);
        Assert.Equal(PlatformDebugAudioBackendMode.Silent,Silent.Mode);
        Assert.Equal(1u,Silent.Generation);
        Assert.NotNull(Silent.LastFailure);
        Assert.Equal(Server.Mixer.MaximumVoices,Silent.MaximumVoiceCount);
        Assert.Equal(Server.Mixer.BusCount,Silent.BusCount);

        Assert.True(Server.TryRecreateHardware());
        Assert.Equal(AudioDeviceState.Ready,Server.State);
        Assert.Equal(AudioBackendMode.XAudio2,Server.Mode);
        Assert.Equal(2u,Server.Generation);
        Assert.Null(Server.LastFailure);
        PlatformDebugAudioSnapshot Hardware=Server.CaptureDebugSnapshot();
        Assert.Equal(PlatformDebugAudioBackendMode.XAudio2,Hardware.Mode);
        Assert.Equal(2u,Hardware.Generation);
        Assert.Null(Hardware.LastFailure);
        Assert.Equal(PlatformDebugAudioBackendMode.Silent,Silent.Mode);
        Assert.Equal(1u,Silent.Generation);

        Server.Dispose();
        Server.Dispose();
        Assert.Equal(AudioDeviceState.Disposed,Server.State);
        Assert.Equal(1,DestroyCount);
    }

    [Fact]
    public void FailureWithoutFallbackIsFaultedAndNeverReportsSuccess(){
        AudioDeviceServer Server=new(
            ()=>throw new AudioException("Create",-1,"fault injection"),
            _=>throw new InvalidOperationException("No engine was created."),
            AllowSilentFallback:false);

        Assert.Equal(AudioDeviceState.Faulted,Server.State);
        Assert.Equal(0u,Server.Generation);
        Assert.False(Server.TryRecreateHardware());
        Assert.Equal(AudioDeviceState.Faulted,Server.State);
        Assert.Equal(AudioBackendMode.None,Server.Mode);
        Server.Dispose();
    }

    [Fact]
    public void SuccessfulRecreationRestoresArtifactVoiceAndStopsEphemeralVoice(){
        int CreateCount=0;
        using AudioDeviceServer Server=new(
            ()=>{
                CreateCount++;
                return new XAudio2EngineHandle((nint)CreateCount,(nint)100);
            },
            _=>{},AllowSilentFallback:false);
        PcmAudioStreamArtifact Artifact=new(
            ArtifactReference(),new AudioMixFormat(48000,1),[0.25f,0.5f]);
        Assert.Equal(AudioVoiceStartStatus.Started,Server.Mixer.StartVoice(new(
            Voice("restorable"),AudioBusId.Master,Artifact.OpenClip(),
            Loop:new AudioLoopRegion(0,2),
            RecoveryPolicy:AudioVoiceRecoveryPolicy.RestoreFromArtifact)));
        Assert.Equal(AudioVoiceStartStatus.Started,Server.Mixer.StartVoice(new(
            Voice("ephemeral"),AudioBusId.Master,
            new PcmAudioClip(new AudioMixFormat(48000,1),[0.25f,0.5f]),
            Loop:new AudioLoopRegion(0,2))));
        Span<AudioVoiceRecoveryEvent>Events=stackalloc AudioVoiceRecoveryEvent[2];

        Assert.True(Server.TryRecreateHardware(Events,out var Recovery));

        Assert.Equal(2u,Server.Generation);
        Assert.Equal(new AudioVoiceRecoveryReport(1,1,2,0),Recovery);
        Assert.Equal(Recovery,Server.Snapshot.LastRecovery);
        Assert.Equal(1,Server.Mixer.ActiveVoiceCount);
        Assert.Equal(AudioVoiceRecoveryDecision.RestoredFromArtifact,Events[0].Decision);
        Assert.Equal(AudioVoiceRecoveryDecision.StoppedNotEligible,Events[1].Decision);
    }

    [Fact]
    public void FailedRecreationWithoutFallbackKeepsLogicalVoicesForAnExplicitRetry(){
        int CreateCount=0;
        using AudioDeviceServer Server=new(
            ()=>{
                CreateCount++;
                if(CreateCount>1){ throw new AudioException("Create",-1,"device remains unavailable"); }
                return new XAudio2EngineHandle((nint)1,(nint)2);
            },_=>{},AllowSilentFallback:false);
        Assert.Equal(AudioVoiceStartStatus.Started,Server.Mixer.StartVoice(new(
            Voice("pending"),AudioBusId.Master,
            new PcmAudioClip(new AudioMixFormat(48000,1),[0.25f,0.5f]),
            Loop:new AudioLoopRegion(0,2))));

        Assert.False(Server.TryRecreateHardware(default,out var Recovery));

        Assert.Equal(AudioDeviceState.Faulted,Server.State);
        Assert.Equal(default,Recovery);
        Assert.Equal(1,Server.Mixer.ActiveVoiceCount);
    }

    [Fact]
    public void EngineHostOwnsOneLazyAudioDeviceServer(){
        using CSharpGameEngine.Runtime.Windows.EngineHost Host=new();

        AudioDeviceServer First=Host.EnsureAudioDevice();
        AudioDeviceServer Second=Host.EnsureAudioDevice();

        Assert.Same(First,Second);
        Assert.Same(First.Mixer,Host.Audio);
        Assert.Equal(AudioDeviceState.Ready,First.State);
        Assert.True(First.Mode is AudioBackendMode.XAudio2 or AudioBackendMode.Silent);
        Host.Dispose();
        Assert.Equal(AudioDeviceState.Disposed,First.State);
        Assert.Equal(AudioBackendMode.None,First.Mode);
        Assert.True(First.Mixer.IsDisposed);
    }

    private static readonly StableId IdNamespace=
        StableId.Parse("b2ac1d4c-88a8-52fe-a822-8016c6ca52de");
    private static AudioVoiceId Voice(string Name)=>
        new(StableId.FromName(IdNamespace,$"voice/{Name}"));
    private static AssetArtifactReference ArtifactReference()=>new(
        new AssetKey(new AssetId(StableId.FromName(IdNamespace,"asset/recovery")),null),
        Hash256.Compute([5,6,7,8]));
}
