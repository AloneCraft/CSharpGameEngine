using System.Buffers.Binary;
using System.Security.Cryptography;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Audio.Tests;

public sealed class AudioMixerTests {
    private static readonly StableId IdNamespace=
        StableId.Parse("f1aef2b7-d258-5724-bf22-e223068f45dc");

    [Fact]
    public void Mix_RoutesBusGainAndVoicePanToDeterministicPcm(){
        using AudioMixer Mixer=new(new AudioMixFormat(48000,2),4);
        AudioBusId Effects=Bus("effects");
        Mixer.AddBus(Effects,AudioBusId.Master,0.5f);
        PcmAudioClip Left=new(new AudioMixFormat(48000,1),[1f,0.5f]);
        PcmAudioClip Right=new(new AudioMixFormat(48000,1),[0.5f,0.5f]);
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Voice("left"),Effects,Left,1f,-1f)));
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Voice("right"),AudioBusId.Master,Right,1f,1f)));
        short[]Pcm=new short[4];
        Span<AudioVoiceEvent>Events=stackalloc AudioVoiceEvent[2];

        AudioMixReport Report=Mixer.Mix(Pcm,2,Events);

        Assert.Equal([16384,16384,8192,16384],Pcm);
        Assert.Equal(2,Report.CompletedVoiceCount);
        Assert.Equal(2,Report.EventCount);
        Assert.All(Events.ToArray(),Event=>Assert.Equal(AudioVoiceStopReason.Completed,Event.Reason));
        Assert.Equal("E18F7B7D75F6F65025406EED6D5BF9E34B2BE2A70464AABE91B9DB5560E60413",Hash(Pcm));
    }

    [Fact]
    public void Loop_RepeatsOnlyTheDeclaredRegion(){
        using AudioMixer Mixer=new(new AudioMixFormat(48000,1));
        PcmAudioClip Clip=new(new AudioMixFormat(48000,1),[0.25f,0.5f,0.75f]);
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Voice("loop"),AudioBusId.Master,Clip,Loop:new AudioLoopRegion(1,3))));
        short[]Pcm=new short[6];

        AudioMixReport Report=Mixer.Mix(Pcm,6);

        Assert.Equal([8192,16384,24575,16384,24575,16384],Pcm);
        Assert.Equal(1,Report.ActiveVoiceCount);
        Assert.Equal(0,Report.CompletedVoiceCount);
    }

    [Fact]
    public void VoiceLimitAndDuplicateAreExplicitAndNeverSteal(){
        using AudioMixer Mixer=new(new AudioMixFormat(48000,1),1);
        PcmAudioClip Clip=new(new AudioMixFormat(48000,1),[1f]);
        AudioVoiceId First=Voice("first");

        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            First,AudioBusId.Master,Clip,Loop:new AudioLoopRegion(0,1))));
        Assert.Equal(AudioVoiceStartStatus.DuplicateVoice,Mixer.StartVoice(new(
            First,AudioBusId.Master,Clip)));
        Assert.Equal(AudioVoiceStartStatus.VoiceLimitReached,Mixer.StartVoice(new(
            Voice("second"),AudioBusId.Master,Clip)));
        Assert.Equal(1,Mixer.ActiveVoiceCount);
    }

    [Fact]
    public void UnderrunPoliciesEitherRenderSilenceOrStopWithTypedEvidence(){
        PcmAudioClip Incomplete=new(new AudioMixFormat(48000,1),[1f],DeclaredFrameCount:3);
        using AudioMixer SilenceMixer=new(new AudioMixFormat(48000,1));
        Assert.Equal(AudioVoiceStartStatus.Started,SilenceMixer.StartVoice(new(
            Voice("silence"),AudioBusId.Master,Incomplete,
            UnderrunPolicy:AudioUnderrunPolicy.Silence)));
        short[]SilencePcm=new short[3];
        Span<AudioVoiceEvent>SilenceEvents=stackalloc AudioVoiceEvent[1];

        AudioMixReport SilenceReport=SilenceMixer.Mix(SilencePcm,3,SilenceEvents);

        Assert.Equal([32767,0,0],SilencePcm);
        Assert.Equal(2,SilenceReport.UnderrunFrameCount);
        Assert.Equal(AudioVoiceStopReason.Completed,SilenceEvents[0].Reason);

        using AudioMixer StopMixer=new(new AudioMixFormat(48000,1));
        Assert.Equal(AudioVoiceStartStatus.Started,StopMixer.StartVoice(new(
            Voice("stop"),AudioBusId.Master,Incomplete,
            UnderrunPolicy:AudioUnderrunPolicy.Stop)));
        short[]StopPcm=new short[3];
        Span<AudioVoiceEvent>StopEvents=stackalloc AudioVoiceEvent[1];

        AudioMixReport StopReport=StopMixer.Mix(StopPcm,3,StopEvents);

        Assert.Equal([32767,0,0],StopPcm);
        Assert.Equal(1,StopReport.UnderrunFrameCount);
        Assert.Equal(AudioVoiceStopReason.Underrun,StopEvents[0].Reason);
    }

    [Fact]
    public void ClipOwnsItsPcmAndSteadyStateLoopMixAllocatesNothing(){
        float[]Source=[0.25f];
        PcmAudioClip Clip=new(new AudioMixFormat(48000,1),Source);
        Source[0]=1f;
        using AudioMixer Mixer=new(new AudioMixFormat(48000,1));
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Voice("owned"),AudioBusId.Master,Clip,Loop:new AudioLoopRegion(0,1))));
        Span<short>Pcm=stackalloc short[1];
        Mixer.Mix(Pcm,1);
        long Before=GC.GetAllocatedBytesForCurrentThread();

        for(int Index=0;Index<100;Index++){ Mixer.Mix(Pcm,1); }

        Assert.Equal(Before,GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(8192,Pcm[0]);
    }

    [Fact]
    public void DisposeRejectsFurtherMutationAndDropsOwnedVoices(){
        AudioMixer Mixer=new(new AudioMixFormat(48000,1));
        Mixer.Dispose();
        Mixer.Dispose();

        Assert.True(Mixer.IsDisposed);
        Assert.Equal(0,Mixer.ActiveVoiceCount);
        Assert.Throws<ObjectDisposedException>(()=>Mixer.Mix(new short[1],1));
    }

    [Fact]
    public void DefaultStructFormatsAndLoopRegionsCannotBypassValidation(){
        Assert.Throws<ArgumentException>(()=>new AudioMixer(default));
        Assert.Throws<ArgumentException>(()=>new PcmAudioClip(default,[]));
        using AudioMixer Mixer=new(new AudioMixFormat(48000,1));
        PcmAudioClip Clip=new(new AudioMixFormat(48000,1),[1f]);

        Assert.Throws<ArgumentException>(()=>Mixer.StartVoice(new(
            Voice("invalid-loop"),AudioBusId.Master,Clip,Loop:default(AudioLoopRegion))));
    }

    [Fact]
    public void HierarchicalBusPanIsSummedBeforeItIsClamped(){
        using AudioMixer Mixer=new(new AudioMixFormat(48000,2));
        AudioBusId Grandparent=Bus("grandparent");
        AudioBusId Parent=Bus("parent");
        AudioBusId Child=Bus("child");
        Mixer.AddBus(Grandparent,AudioBusId.Master,Pan:-1f);
        Mixer.AddBus(Parent,Grandparent,Pan:1f);
        Mixer.AddBus(Child,Parent,Pan:1f);
        PcmAudioClip Clip=new(new AudioMixFormat(48000,1),[1f]);
        Assert.Equal(AudioVoiceStartStatus.Started,Mixer.StartVoice(new(
            Voice("balanced"),Child,Clip)));
        short[]Pcm=new short[2];

        Mixer.Mix(Pcm,1);

        Assert.Equal([0,32767],Pcm);
    }

    private static AudioBusId Bus(string Name)=>new(StableId.FromName(IdNamespace,$"bus/{Name}"));
    private static AudioVoiceId Voice(string Name)=>new(StableId.FromName(IdNamespace,$"voice/{Name}"));
    private static string Hash(ReadOnlySpan<short>Pcm){
        byte[]Bytes=new byte[Pcm.Length*sizeof(short)];
        for(int Index=0;Index<Pcm.Length;Index++){
            BinaryPrimitives.WriteInt16LittleEndian(Bytes.AsSpan(Index*sizeof(short)),Pcm[Index]);
        }
        return Convert.ToHexString(SHA256.HashData(Bytes));
    }
}
