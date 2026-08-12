using System.Numerics;
using CSharpGameEngine.Audio.Abstractions;
using CSharpGameEngine.Audio.XAudio2;
using CSharpGameEngine.Entities;
using AudioEngineHandle=CSharpGameEngine.Audio.XAudio2.XAudio2EngineHandle;
using AudioVoiceHandle=CSharpGameEngine.Audio.XAudio2.XAudio2VoiceHandle;

namespace UserGameProject;

public sealed class AudioRuntime:IDisposable {
    private readonly Dictionary<AudioClipHandle,AudioClipResource> Clips=[];
    private readonly Dictionary<int,VoiceState> Voices=[];
    private AudioEngineHandle Engine;
    private bool EngineCreated;
    private bool Disposed;

    public AudioRuntime(){
        try {
            Engine=AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.CreateEngine();
            EngineCreated=true;
        } catch (Exception Exception) {
            InitializationError=Exception.Message;
        }

        RegisterClip(new AudioClipHandle(0),new AudioClipResource(
            ProceduralAudioClip.GenerateSineWave(440f,1f,44100),44100,1));
        RegisterClip(new AudioClipHandle(1),new AudioClipResource(
            ProceduralAudioClip.GenerateSineWave(660f,0.75f,44100,0.22f),44100,1));
    }

    public bool IsAvailable=>EngineCreated&&!Disposed;
    public string?InitializationError{ get; private set;}
    public Vector3 ListenerPosition{ get; private set;}

    public void RegisterClip(AudioClipHandle Handle,AudioClipResource Clip){
        ArgumentNullException.ThrowIfNull(Clip);
        Clips[Handle]=Clip;
    }

    public void SetListenerPosition(in Vector3 Position)=>ListenerPosition=Position;

    public void UpdateSource(Entity Entity,in Vector3 SourcePosition,in AudioSourceData Source){
        if(Disposed||!EngineCreated||!Source.Playing||!Clips.TryGetValue(Source.Clip,out AudioClipResource? Clip)){
            RemoveSource(Entity);
            return;
        }

        if(!Voices.TryGetValue(Entity.Id,out VoiceState Voice)||Voice.Clip!=Source.Clip){
            if(Voices.ContainsKey(Entity.Id)){
                AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Stop(Voice.Voice);
                AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyVoice(Voice.Voice);
            }

            AudioVoiceHandle Handle=AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.CreateVoice(
                Engine,Clip.SampleRate,Clip.Channels);
            Handle=AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.SubmitBuffer(
                Handle,Clip.Samples,Source.Loop);
            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Start(Handle);
            Voice=new VoiceState(Handle,Source.Clip);
            Voices[Entity.Id]=Voice;
        }

        var Volume=AudioSpatialization.ComputeAttenuatedVolume(
            Source.Volume,ListenerPosition,SourcePosition,Source.MinDistance,Source.MaxDistance);
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.SetVolume(Voice.Voice,Volume);
    }

    public void RemoveSource(Entity Entity){
        if(!Voices.Remove(Entity.Id,out VoiceState Voice)){
            return;
        }

        if(!Disposed&&EngineCreated){
            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Stop(Voice.Voice);
        }

        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyVoice(Voice.Voice);
    }

    public void Dispose(){
        if(Disposed){
            return;
        }

        Disposed=true;
        foreach(VoiceState Voice in Voices.Values){
            if(EngineCreated){
                AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Stop(Voice.Voice);
            }

            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyVoice(Voice.Voice);
        }

        Voices.Clear();
        if(EngineCreated){
            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyEngine(Engine);
            EngineCreated=false;
        }
    }

    private readonly record struct VoiceState(AudioVoiceHandle Voice,AudioClipHandle Clip);
}

public sealed class AudioClipResource {
    public AudioClipResource(short[] Samples,int SampleRate,int Channels){
        this.Samples= Samples ?? throw new ArgumentNullException(nameof(Samples));
        if(Samples.Length==0){
            throw new ArgumentException("Audio clips must contain at least one sample.",nameof(Samples));
        }

        if(SampleRate<=0||Channels<=0){
            throw new ArgumentOutOfRangeException(nameof(SampleRate),"Sample rate and channels must be positive.");
        }

        this.SampleRate=SampleRate;
        this.Channels=Channels;
    }

    public short[]Samples{ get;}
    public int SampleRate{ get;}
    public int Channels{ get;}
}
