using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Audio.Tests;

public sealed class AudioSpatializationTests {
    private static readonly StableId IdNamespace=
        StableId.Parse("8d691bc2-e675-55ae-a614-f7c8b69e1347");

    [Fact]
    public void SpatializerUsesOnlyListenerLocalPositionForPanAndLinearAttenuation(){
        AudioSpatialParameters Parameters=new(1f,11f);

        Assert.True(AudioSpatializer.TryCalculate(new Vector3(5f,0f,0f),Parameters,out var Right));
        Assert.Equal(0.6f,Right.Attenuation,5);
        Assert.Equal(1f,Right.Pan);
        Assert.Equal(5f,Right.Distance);

        Assert.True(AudioSpatializer.TryCalculate(new Vector3(0f,0f,-6f),Parameters,out var Behind));
        Assert.Equal(0.5f,Behind.Attenuation,5);
        Assert.Equal(0f,Behind.Pan);

        Assert.True(AudioSpatializer.TryCalculate(Vector3.Zero,Parameters,out var AtListener));
        Assert.Equal(new AudioSpatialMix(1f,0f,0f),AtListener);
    }

    [Fact]
    public void SpatializerRejectsInvalidOrOverflowingInputAndSilencesPastMaximum(){
        AudioSpatialParameters Parameters=new(1f,10f);

        Assert.False(AudioSpatializer.TryCalculate(
            new Vector3(float.MaxValue,float.MaxValue,0f),Parameters,out _));
        Assert.False(AudioSpatializer.TryCalculate(
            new Vector3(float.NaN,0f,0f),Parameters,out _));
        Assert.True(AudioSpatializer.TryCalculate(new Vector3(0f,0f,10f),Parameters,out var Mix));
        Assert.Equal(0f,Mix.Attenuation);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new AudioSpatialParameters(1f,1f));
    }

    [Fact]
    public void DeviceRecoveryKeepsOnlyArtifactBackedEligibleVoicesAtDefinedFrame(){
        using AudioMixer Mixer=new(new AudioMixFormat(48000,1));
        PcmAudioStreamArtifact Artifact=new(
            ArtifactReference("loop"),new AudioMixFormat(48000,1),[0.1f,0.2f,0.3f,0.4f]);
        AudioVoiceId Restorable=Voice("restorable");
        AudioVoiceId Ephemeral=Voice("ephemeral");
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Restorable,AudioBusId.Master,Artifact.OpenClip(),Loop:new AudioLoopRegion(1,3),
            RecoveryPolicy:AudioVoiceRecoveryPolicy.RestoreFromArtifact)));
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Ephemeral,AudioBusId.Master,new PcmAudioClip(
                new AudioMixFormat(48000,1),[0.1f,0.2f,0.3f,0.4f]))));
        Span<short>Pcm=stackalloc short[3];
        Mixer.Mix(Pcm,3);
        Span<AudioVoiceRecoveryEvent>Events=stackalloc AudioVoiceRecoveryEvent[2];

        AudioVoiceRecoveryReport Report=Mixer.RecoverVoices(Events);

        Assert.Equal(new AudioVoiceRecoveryReport(1,1,2,0),Report);
        Assert.Equal(1,Mixer.ActiveVoiceCount);
        Assert.Equal(Restorable,Events[0].VoiceId);
        Assert.Equal(AudioVoiceRecoveryDecision.RestoredFromArtifact,Events[0].Decision);
        Assert.Equal(1,Events[0].LogicalFrame);
        Assert.Equal(Artifact.Reference,Events[0].ArtifactReference);
        Assert.Equal(Ephemeral,Events[1].VoiceId);
        Assert.Equal(AudioVoiceRecoveryDecision.StoppedNotEligible,Events[1].Decision);
    }

    [Fact]
    public void RestorePolicyRequiresArtifactAndArtifactOwnsCanonicalSamples(){
        float[]Samples=[0.25f];
        PcmAudioStreamArtifact Artifact=new(
            ArtifactReference("owned"),new AudioMixFormat(48000,1),Samples);
        Samples[0]=1f;
        using AudioMixer Mixer=new(new AudioMixFormat(48000,1));
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Voice("owned"),AudioBusId.Master,Artifact.OpenClip(),
            RecoveryPolicy:AudioVoiceRecoveryPolicy.RestoreFromArtifact)));
        Span<short>Pcm=stackalloc short[1];
        Mixer.Mix(Pcm,1);
        Assert.Equal(8192,Pcm[0]);

        Assert.Throws<ArgumentException>(()=>Mixer.StartVoice(new(
            Voice("invalid"),AudioBusId.Master,
            new PcmAudioClip(new AudioMixFormat(48000,1),[1f]),
            RecoveryPolicy:AudioVoiceRecoveryPolicy.RestoreFromArtifact)));
        Assert.Throws<ArgumentException>(()=>Mixer.StartVoice(new(
            Voice("invalid-enum"),AudioBusId.Master,Artifact.OpenClip(),
            RecoveryPolicy:(AudioVoiceRecoveryPolicy)255)));
    }

    [Fact]
    public void SpatialMixCanUpdateAnActiveVoiceWithoutReplacingItsCursor(){
        using AudioMixer Mixer=new(new AudioMixFormat(48000,2));
        AudioVoiceId VoiceId=Voice("spatial");
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            VoiceId,AudioBusId.Master,
            new PcmAudioClip(new AudioMixFormat(48000,1),[1f,0.5f]))));
        Assert.True(Mixer.TrySetVoiceMix(VoiceId,0.5f,1f));
        Span<short>Pcm=stackalloc short[2];

        Mixer.Mix(Pcm,1);

        Assert.Equal(0,Pcm[0]);
        Assert.Equal(16384,Pcm[1]);
        Assert.False(Mixer.TrySetVoiceMix(Voice("missing"),1f,0f));
    }

    private static AudioVoiceId Voice(string Name)=>
        new(StableId.FromName(IdNamespace,$"voice/{Name}"));

    private static AssetArtifactReference ArtifactReference(string Name){
        AssetId Asset=new(StableId.FromName(IdNamespace,$"asset/{Name}"));
        return new AssetArtifactReference(
            new AssetKey(Asset,null),Hash256.Compute([1,2,3,4]));
    }
}
