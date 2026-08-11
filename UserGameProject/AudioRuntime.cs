using System.Numerics;
using CSharpGameEngine.Audio.Abstractions;
using CSharpGameEngine.Audio.XAudio2;
using CSharpGameEngine.Entities;
using AudioEngineHandle=CSharpGameEngine.Audio.XAudio2.XAudio2EngineHandle;
using AudioVoiceHandle=CSharpGameEngine.Audio.XAudio2.XAudio2VoiceHandle;

namespace UserGameProject;

public sealed class AudioRuntime:IDisposable {
    private readonly Dictionary<AudioClipHandle,AudioClipResource> clips=[];
    private readonly Dictionary<int,VoiceState> voices=[];
    private AudioEngineHandle engine;
    private bool engineCreated;
    private bool disposed;

    public AudioRuntime(){
        try {
            engine=AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.CreateEngine();
            engineCreated=true;
        } catch (Exception exception) {
            InitializationError=exception.Message;
        }

        RegisterClip(new AudioClipHandle(0),new AudioClipResource(
            ProceduralAudioClip.GenerateSineWave(440f,1f,44100),44100,1));
        RegisterClip(new AudioClipHandle(1),new AudioClipResource(
            ProceduralAudioClip.GenerateSineWave(660f,0.75f,44100,0.22f),44100,1));
    }

    public bool IsAvailable=>engineCreated&&!disposed;
    public string?InitializationError{ get; private set;}
    public Vector3 ListenerPosition{ get; private set;}

    public void RegisterClip(AudioClipHandle handle,AudioClipResource clip){
        ArgumentNullException.ThrowIfNull(clip);
        clips[handle]=clip;
    }

    public void SetListenerPosition(in Vector3 position)=>ListenerPosition=position;

    public void UpdateSource(Entity entity,in Vector3 sourcePosition,in AudioSourceData source){
        if(disposed||!engineCreated||!source.Playing||!clips.TryGetValue(source.Clip,out AudioClipResource? clip)){
            RemoveSource(entity);
            return;
        }

        if(!voices.TryGetValue(entity.Id,out VoiceState voice)||voice.Clip!=source.Clip){
            if(voices.ContainsKey(entity.Id)){
                AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Stop(voice.Voice);
                AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyVoice(voice.Voice);
            }

            AudioVoiceHandle handle=AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.CreateVoice(
                engine,clip.SampleRate,clip.Channels);
            handle=AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.SubmitBuffer(
                handle,clip.Samples,source.Loop);
            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Start(handle);
            voice=new VoiceState(handle,source.Clip);
            voices[entity.Id]=voice;
        }

        var volume=AudioSpatialization.ComputeAttenuatedVolume(
            source.Volume,ListenerPosition,sourcePosition,source.MinDistance,source.MaxDistance);
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.SetVolume(voice.Voice,volume);
    }

    public void RemoveSource(Entity entity){
        if(!voices.Remove(entity.Id,out VoiceState voice)){
            return;
        }

        if(!disposed&&engineCreated){
            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Stop(voice.Voice);
        }

        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyVoice(voice.Voice);
    }

    public void Dispose(){
        if(disposed){
            return;
        }

        disposed=true;
        foreach(VoiceState voice in voices.Values){
            if(engineCreated){
                AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Stop(voice.Voice);
            }

            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyVoice(voice.Voice);
        }

        voices.Clear();
        if(engineCreated){
            AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyEngine(engine);
            engineCreated=false;
        }
    }

    private readonly record struct VoiceState(AudioVoiceHandle Voice,AudioClipHandle Clip);
}

public sealed class AudioClipResource {
    public AudioClipResource(short[] samples,int sampleRate,int channels){
        Samples=samples??throw new ArgumentNullException(nameof(samples));
        if(samples.Length==0){
            throw new ArgumentException("Audio clips must contain at least one sample.",nameof(samples));
        }

        if(sampleRate<=0||channels<=0){
            throw new ArgumentOutOfRangeException(nameof(sampleRate),"Sample rate and channels must be positive.");
        }

        SampleRate=sampleRate;
        Channels=channels;
    }

    public short[]Samples{ get;}
    public int SampleRate{ get;}
    public int Channels{ get;}
}
