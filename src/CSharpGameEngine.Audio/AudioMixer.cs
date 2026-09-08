using CSharpGameEngine.Core;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Audio;

public readonly record struct AudioMixFormat {
    public AudioMixFormat(int SampleRate,int ChannelCount){
        if(SampleRate is <8000 or >384000){
            throw new ArgumentOutOfRangeException(nameof(SampleRate));
        }
        if(ChannelCount is <1 or >2){
            throw new ArgumentOutOfRangeException(nameof(ChannelCount));
        }
        this.SampleRate=SampleRate;
        this.ChannelCount=ChannelCount;
    }
    public int SampleRate { get; }
    public int ChannelCount { get; }
    public bool IsValid=>this.SampleRate is >=8000 and <=384000
        &&this.ChannelCount is >=1 and <=2;
    public static AudioMixFormat Default=>new(48000,2);
}

public readonly record struct AudioLoopRegion {
    public AudioLoopRegion(int StartFrame,int EndFrame){
        if(StartFrame<0){ throw new ArgumentOutOfRangeException(nameof(StartFrame)); }
        if(EndFrame<=StartFrame){ throw new ArgumentOutOfRangeException(nameof(EndFrame)); }
        this.StartFrame=StartFrame;
        this.EndFrame=EndFrame;
    }
    public int StartFrame { get; }
    public int EndFrame { get; }
}

/// <summary>
/// Immutable decoded PCM stream artifact. Runtime codec/provider objects are deliberately absent;
/// the canonical content reference and owned samples are sufficient to reopen a recovering voice.
/// </summary>
public sealed class PcmAudioStreamArtifact {
    private readonly float[]SamplesStorage;

    public PcmAudioStreamArtifact(AssetArtifactReference Reference,AudioMixFormat Format,
        ReadOnlySpan<float>Samples,int?DeclaredFrameCount=null){
        if(!Reference.IsValid){ throw new ArgumentException("A valid artifact reference is required.",nameof(Reference)); }
        if(!Format.IsValid){ throw new ArgumentException("PCM format is invalid.",nameof(Format)); }
        if(Samples.Length%Format.ChannelCount!=0){
            throw new ArgumentException("PCM sample count must contain complete interleaved frames.",nameof(Samples));
        }
        this.Reference=Reference;
        this.Format=Format;
        this.SamplesStorage=Samples.ToArray();
        foreach(float Sample in this.SamplesStorage){
            if(!float.IsFinite(Sample)){
                throw new ArgumentException("PCM samples must be finite.",nameof(Samples));
            }
        }
        this.AvailableFrameCount=this.SamplesStorage.Length/Format.ChannelCount;
        this.DeclaredFrameCount=DeclaredFrameCount??this.AvailableFrameCount;
        if(this.DeclaredFrameCount<this.AvailableFrameCount){
            throw new ArgumentOutOfRangeException(
                nameof(DeclaredFrameCount),"Declared frames cannot truncate owned PCM data.");
        }
    }

    public AssetArtifactReference Reference { get; }
    public AudioMixFormat Format { get; }
    public int AvailableFrameCount { get; }
    public int DeclaredFrameCount { get; }
    public PcmAudioClip OpenClip()=>new(this);
    internal float[]GetOwnedSamples()=>this.SamplesStorage;
}

public sealed class PcmAudioClip {
    private readonly float[]SamplesStorage;

    public PcmAudioClip(AudioMixFormat Format,ReadOnlySpan<float>Samples,
        int?DeclaredFrameCount=null){
        if(!Format.IsValid){ throw new ArgumentException("PCM format is invalid.",nameof(Format)); }
        if(Samples.Length%Format.ChannelCount!=0){
            throw new ArgumentException("PCM sample count must contain complete interleaved frames.",nameof(Samples));
        }
        this.Format=Format;
        this.SamplesStorage=Samples.ToArray();
        foreach(float Sample in this.SamplesStorage){
            if(!float.IsFinite(Sample)){
                throw new ArgumentException("PCM samples must be finite.",nameof(Samples));
            }
        }
        this.AvailableFrameCount=this.SamplesStorage.Length/Format.ChannelCount;
        this.DeclaredFrameCount=DeclaredFrameCount??this.AvailableFrameCount;
        if(this.DeclaredFrameCount<this.AvailableFrameCount){
            throw new ArgumentOutOfRangeException(
                nameof(DeclaredFrameCount),"Declared frames cannot truncate owned PCM data.");
        }
    }

    internal PcmAudioClip(PcmAudioStreamArtifact Artifact){
        ArgumentNullException.ThrowIfNull(Artifact);
        this.Format=Artifact.Format;
        this.SamplesStorage=Artifact.GetOwnedSamples();
        this.AvailableFrameCount=Artifact.AvailableFrameCount;
        this.DeclaredFrameCount=Artifact.DeclaredFrameCount;
        this.ArtifactReference=Artifact.Reference;
    }

    public AudioMixFormat Format { get; }
    public int AvailableFrameCount { get; }
    public int DeclaredFrameCount { get; }
    public AssetArtifactReference?ArtifactReference { get; }
    internal float GetSample(int Frame,int Channel)=>
        this.SamplesStorage[checked(Frame*this.Format.ChannelCount+Channel)];
}

public enum AudioUnderrunPolicy:byte { Silence,Stop }
public enum AudioVoiceRecoveryPolicy:byte { Stop,RestoreFromArtifact }
public enum AudioVoiceStartStatus:byte { Started,DuplicateVoice,VoiceLimitReached,UnknownBus }
public enum AudioVoiceStopReason:byte { Requested,Completed,Underrun }
public enum AudioVoiceRecoveryDecision:byte { RestoredFromArtifact,StoppedNotEligible }

public readonly record struct AudioVoiceRequest(
    AudioVoiceId VoiceId,AudioBusId BusId,PcmAudioClip Clip,float Gain=1f,float Pan=0f,
    AudioLoopRegion?Loop=null,AudioUnderrunPolicy UnderrunPolicy=AudioUnderrunPolicy.Silence,
    AudioVoiceRecoveryPolicy RecoveryPolicy=AudioVoiceRecoveryPolicy.Stop);

public readonly record struct AudioVoiceEvent(AudioVoiceId VoiceId,AudioVoiceStopReason Reason);

public readonly record struct AudioVoiceRecoveryEvent(
    AudioVoiceId VoiceId,AudioVoiceRecoveryDecision Decision,int LogicalFrame,
    AssetArtifactReference?ArtifactReference);

public readonly record struct AudioVoiceRecoveryReport(
    int RestoredVoiceCount,int StoppedVoiceCount,int EventCount,int DroppedEventCount);

public readonly record struct AudioMixReport(
    int MixedFrameCount,int ActiveVoiceCount,int CompletedVoiceCount,
    int UnderrunFrameCount,int EventCount,int DroppedEventCount);

public sealed class AudioMixer:IDisposable {
    private const int MaximumBusDepth=16;
    private readonly Dictionary<AudioBusId,BusState>Buses=[];
    private readonly List<VoiceState>Voices=[];
    private bool Disposed;

    public AudioMixer(AudioMixFormat Format,int MaximumVoices=128){
        if(!Format.IsValid){ throw new ArgumentException("Mixer format is invalid.",nameof(Format)); }
        if(MaximumVoices<=0){ throw new ArgumentOutOfRangeException(nameof(MaximumVoices)); }
        this.Format=Format;
        this.MaximumVoices=MaximumVoices;
        this.Buses.Add(AudioBusId.Master,new BusState(default,1f,0f,0));
    }

    public AudioMixFormat Format { get; }
    public int MaximumVoices { get; }
    public int ActiveVoiceCount=>this.Voices.Count;
    public int BusCount=>this.Buses.Count;
    public bool IsDisposed=>this.Disposed;

    public void AddBus(AudioBusId BusId,AudioBusId ParentId,float Gain=1f,float Pan=0f){
        this.ThrowIfDisposed();
        ValidateBusMix(Gain,Pan);
        if(!BusId.IsValid||BusId==AudioBusId.Master){
            throw new ArgumentException("A child bus requires a valid non-master ID.",nameof(BusId));
        }
        if(this.Buses.ContainsKey(BusId)){
            throw new ArgumentException($"Audio bus already exists: {BusId}.",nameof(BusId));
        }
        if(!this.Buses.TryGetValue(ParentId,out BusState?Parent)){
            throw new ArgumentException($"Parent audio bus does not exist: {ParentId}.",nameof(ParentId));
        }
        int Depth=checked(Parent.Depth+1);
        if(Depth>MaximumBusDepth){
            throw new InvalidOperationException($"Audio bus depth exceeds {MaximumBusDepth}.");
        }
        this.Buses.Add(BusId,new BusState(ParentId,Gain,Pan,Depth));
    }

    public void SetBusMix(AudioBusId BusId,float Gain,float Pan){
        this.ThrowIfDisposed();
        ValidateBusMix(Gain,Pan);
        if(!this.Buses.TryGetValue(BusId,out BusState?Bus)){
            throw new ArgumentException($"Audio bus does not exist: {BusId}.",nameof(BusId));
        }
        Bus.Gain=Gain;
        Bus.Pan=Pan;
    }

    public AudioVoiceStartStatus StartVoice(in AudioVoiceRequest Request){
        this.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(Request.Clip);
        if(!Request.VoiceId.IsValid){
            throw new ArgumentException("A voice requires a valid ID.",nameof(Request));
        }
        ValidateVoiceMix(Request.Gain,Request.Pan);
        if(Request.Clip.Format.SampleRate!=this.Format.SampleRate){
            throw new ArgumentException("The mixer does not perform sample-rate conversion.",nameof(Request));
        }
        if(Request.Clip.Format.ChannelCount is <1 or >2){
            throw new ArgumentException("Only mono and stereo PCM sources are supported.",nameof(Request));
        }
        if(Request.UnderrunPolicy is not AudioUnderrunPolicy.Silence and not AudioUnderrunPolicy.Stop){
            throw new ArgumentException("The underrun policy is invalid.",nameof(Request));
        }
        if(Request.RecoveryPolicy is not AudioVoiceRecoveryPolicy.Stop
            and not AudioVoiceRecoveryPolicy.RestoreFromArtifact){
            throw new ArgumentException("The recovery policy is invalid.",nameof(Request));
        }
        if(Request.RecoveryPolicy==AudioVoiceRecoveryPolicy.RestoreFromArtifact
            &&Request.Clip.ArtifactReference is null){
            throw new ArgumentException(
                "Only an artifact-backed voice can be restored after device recreation.",nameof(Request));
        }
        if(Request.Loop is { }Loop&&(Loop.StartFrame<0||Loop.EndFrame<=Loop.StartFrame
            ||Loop.EndFrame>Request.Clip.AvailableFrameCount)){
            throw new ArgumentException(
                "A loop must be non-empty and entirely backed by available PCM frames.",nameof(Request));
        }
        for(int Index=0;Index<this.Voices.Count;Index++){
            if(this.Voices[Index].Id==Request.VoiceId){
                return AudioVoiceStartStatus.DuplicateVoice;
            }
        }
        if(!this.Buses.ContainsKey(Request.BusId)){
            return AudioVoiceStartStatus.UnknownBus;
        }
        if(this.Voices.Count>=this.MaximumVoices){
            return AudioVoiceStartStatus.VoiceLimitReached;
        }
        this.Voices.Add(new VoiceState(Request));
        return AudioVoiceStartStatus.Started;
    }

    public bool TrySetVoiceMix(AudioVoiceId VoiceId,float Gain,float Pan){
        this.ThrowIfDisposed();
        ValidateVoiceMix(Gain,Pan);
        for(int Index=0;Index<this.Voices.Count;Index++){
            VoiceState Voice=this.Voices[Index];
            if(Voice.Id!=VoiceId){ continue; }
            Voice.Gain=Gain;
            Voice.Pan=Pan;
            return true;
        }
        return false;
    }

    public AudioVoiceRecoveryReport RecoverVoices(
        Span<AudioVoiceRecoveryEvent>Events=default){
        this.ThrowIfDisposed();
        int Restored=0;
        int Stopped=0;
        int EventCount=0;
        int DroppedEvents=0;
        for(int Index=0;Index<this.Voices.Count;Index++){
            VoiceState Voice=this.Voices[Index];
            if(Voice.RecoveryPolicy==AudioVoiceRecoveryPolicy.RestoreFromArtifact
                &&Voice.Clip.ArtifactReference is { }ArtifactReference){
                if(Voice.Loop is { }Loop&&Voice.Cursor>=Loop.EndFrame){ Voice.Cursor=Loop.StartFrame; }
                PublishRecovery(new AudioVoiceRecoveryEvent(
                    Voice.Id,AudioVoiceRecoveryDecision.RestoredFromArtifact,
                    Voice.Cursor,ArtifactReference),Events,ref EventCount,ref DroppedEvents);
                Restored++;
                continue;
            }
            Voice.Active=false;
            PublishRecovery(new AudioVoiceRecoveryEvent(
                Voice.Id,AudioVoiceRecoveryDecision.StoppedNotEligible,
                Voice.Cursor,null),Events,ref EventCount,ref DroppedEvents);
            Stopped++;
        }
        for(int Index=this.Voices.Count-1;Index>=0;Index--){
            if(!this.Voices[Index].Active){ this.Voices.RemoveAt(Index); }
        }
        return new AudioVoiceRecoveryReport(Restored,Stopped,EventCount,DroppedEvents);
    }

    public bool StopVoice(AudioVoiceId VoiceId,out AudioVoiceEvent Event){
        this.ThrowIfDisposed();
        for(int Index=0;Index<this.Voices.Count;Index++){
            if(this.Voices[Index].Id!=VoiceId){ continue; }
            this.Voices.RemoveAt(Index);
            Event=new AudioVoiceEvent(VoiceId,AudioVoiceStopReason.Requested);
            return true;
        }
        Event=default;
        return false;
    }

    public AudioMixReport Mix(Span<short>Destination,int FrameCount,
        Span<AudioVoiceEvent>Events=default){
        this.ThrowIfDisposed();
        if(FrameCount<0){ throw new ArgumentOutOfRangeException(nameof(FrameCount)); }
        int SampleCount=checked(FrameCount*this.Format.ChannelCount);
        if(Destination.Length<SampleCount){
            throw new ArgumentException("Destination is smaller than the requested frame count.",nameof(Destination));
        }
        Span<short>Output=Destination[..SampleCount];
        Output.Clear();
        int Completed=0;
        int Underruns=0;
        int EventCount=0;
        int DroppedEvents=0;
        for(int Frame=0;Frame<FrameCount;Frame++){
            float Left=0f;
            float Right=0f;
            for(int VoiceIndex=0;VoiceIndex<this.Voices.Count;VoiceIndex++){
                VoiceState Voice=this.Voices[VoiceIndex];
                if(!Voice.Active){ continue; }
                if(Voice.Loop is { }Loop&&Voice.Cursor>=Loop.EndFrame){
                    Voice.Cursor=Loop.StartFrame;
                }
                if(Voice.Cursor>=Voice.Clip.DeclaredFrameCount){
                    Stop(Voice,AudioVoiceStopReason.Completed,Events,
                        ref EventCount,ref DroppedEvents);
                    Completed++;
                    continue;
                }
                if(Voice.Cursor>=Voice.Clip.AvailableFrameCount){
                    Underruns++;
                    if(Voice.UnderrunPolicy==AudioUnderrunPolicy.Stop){
                        Stop(Voice,AudioVoiceStopReason.Underrun,Events,
                            ref EventCount,ref DroppedEvents);
                        Completed++;
                        continue;
                    }
                    Voice.Cursor++;
                    if(Voice.Cursor>=Voice.Clip.DeclaredFrameCount){
                        Stop(Voice,AudioVoiceStopReason.Completed,Events,
                            ref EventCount,ref DroppedEvents);
                        Completed++;
                    }
                    continue;
                }
                GetEffectiveBusMix(Voice.BusId,out float BusGain,out float BusPan);
                float Gain=Voice.Gain*BusGain;
                float Pan=Math.Clamp(Voice.Pan+BusPan,-1f,1f);
                if(this.Format.ChannelCount==1){
                    float Sample=Voice.Clip.Format.ChannelCount==1
                        ?Voice.Clip.GetSample(Voice.Cursor,0)
                        :(Voice.Clip.GetSample(Voice.Cursor,0)+Voice.Clip.GetSample(Voice.Cursor,1))*0.5f;
                    Left+=Sample*Gain;
                } else if(Voice.Clip.Format.ChannelCount==1){
                    float Sample=Voice.Clip.GetSample(Voice.Cursor,0)*Gain;
                    Left+=Sample*(1f-MathF.Max(0f,Pan));
                    Right+=Sample*(1f+MathF.Min(0f,Pan));
                } else {
                    Left+=Voice.Clip.GetSample(Voice.Cursor,0)*Gain*(1f-MathF.Max(0f,Pan));
                    Right+=Voice.Clip.GetSample(Voice.Cursor,1)*Gain*(1f+MathF.Min(0f,Pan));
                }
                Voice.Cursor++;
                if(Voice.Loop is null&&Voice.Cursor>=Voice.Clip.DeclaredFrameCount){
                    Stop(Voice,AudioVoiceStopReason.Completed,Events,
                        ref EventCount,ref DroppedEvents);
                    Completed++;
                }
            }
            int OutputIndex=Frame*this.Format.ChannelCount;
            Output[OutputIndex]=ToPcm16(Left);
            if(this.Format.ChannelCount==2){ Output[OutputIndex+1]=ToPcm16(Right); }
        }
        for(int Index=this.Voices.Count-1;Index>=0;Index--){
            if(!this.Voices[Index].Active){ this.Voices.RemoveAt(Index); }
        }
        return new AudioMixReport(
            FrameCount,this.Voices.Count,Completed,Underruns,EventCount,DroppedEvents);
    }

    private void GetEffectiveBusMix(AudioBusId BusId,out float Gain,out float Pan){
        Gain=1f;
        Pan=0f;
        AudioBusId Current=BusId;
        while(true){
            BusState Bus=this.Buses[Current];
            Gain*=Bus.Gain;
            Pan+=Bus.Pan;
            if(Current==AudioBusId.Master){
                Pan=Math.Clamp(Pan,-1f,1f);
                return;
            }
            Current=Bus.ParentId;
        }
    }

    private static void Stop(VoiceState Voice,AudioVoiceStopReason Reason,
        Span<AudioVoiceEvent>Events,ref int EventCount,ref int DroppedEvents){
        Voice.Active=false;
        if(EventCount<Events.Length){
            Events[EventCount++]=new AudioVoiceEvent(Voice.Id,Reason);
        } else {
            DroppedEvents++;
        }
    }

    private static void PublishRecovery(AudioVoiceRecoveryEvent Event,
        Span<AudioVoiceRecoveryEvent>Events,ref int EventCount,ref int DroppedEvents){
        if(EventCount<Events.Length){ Events[EventCount++]=Event; }
        else { DroppedEvents++; }
    }

    private static short ToPcm16(float Sample){
        float Clamped=Math.Clamp(Sample,-1f,1f);
        return checked((short)MathF.Round(Clamped*32767f,MidpointRounding.ToEven));
    }

    private static void ValidateBusMix(float Gain,float Pan){
        if(!float.IsFinite(Gain)||Gain is <0f or >4f){
            throw new ArgumentOutOfRangeException(nameof(Gain));
        }
        if(!float.IsFinite(Pan)||Pan is <-1f or >1f){
            throw new ArgumentOutOfRangeException(nameof(Pan));
        }
    }

    private static void ValidateVoiceMix(float Gain,float Pan)=>ValidateBusMix(Gain,Pan);

    private void ThrowIfDisposed()=>ObjectDisposedException.ThrowIf(this.Disposed,this);

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.Voices.Clear();
        this.Buses.Clear();
    }

    private sealed class BusState(AudioBusId ParentId,float Gain,float Pan,int Depth) {
        public AudioBusId ParentId { get; }=ParentId;
        public float Gain { get;set; }=Gain;
        public float Pan { get;set; }=Pan;
        public int Depth { get; }=Depth;
    }

    private sealed class VoiceState {
        public VoiceState(in AudioVoiceRequest Request){
            this.Id=Request.VoiceId;
            this.BusId=Request.BusId;
            this.Clip=Request.Clip;
            this.Gain=Request.Gain;
            this.Pan=Request.Pan;
            this.Loop=Request.Loop;
            this.UnderrunPolicy=Request.UnderrunPolicy;
            this.RecoveryPolicy=Request.RecoveryPolicy;
        }
        public AudioVoiceId Id { get; }
        public AudioBusId BusId { get; }
        public PcmAudioClip Clip { get; }
        public float Gain { get;set; }
        public float Pan { get;set; }
        public AudioLoopRegion?Loop { get; }
        public AudioUnderrunPolicy UnderrunPolicy { get; }
        public AudioVoiceRecoveryPolicy RecoveryPolicy { get; }
        public int Cursor { get;set; }
        public bool Active { get;set; }=true;
    }
}
