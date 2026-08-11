using System.Runtime.InteropServices;
using CSharpGameEngine.Audio.Abstractions;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.XAUDIO2;
using static TerraFX.Interop.Windows.AUDIO_STREAM_CATEGORY;
using static TerraFX.Interop.Windows.WAVE;

namespace CSharpGameEngine.Audio.XAudio2;

/// <summary>
/// The one concrete <see cref="IAudioBackend{TEngine, TVoice}"/> implementation this engine ships:
/// XAudio2 via TerraFX.Interop.Windows (already referenced for D3D12, no new package). Stateless
/// <c>readonly struct</c> per the interface's contract - every native pointer lives inside
/// <see cref="XAudio2EngineHandle"/>/<see cref="XAudio2VoiceHandle"/>, cast back from <c>nint</c> only
/// inside this type's methods. Same COM idiom as <c>CSharpGameEngine.Graphics.D3D12.GraphicsDevice</c>
/// (<c>-&gt;</c> calls, <see cref="AudioDiagnostics.EnsureSuccess"/> for HRESULT checks).
/// </summary>
public readonly unsafe struct XAudio2Backend:IAudioBackend<XAudio2EngineHandle,XAudio2VoiceHandle>{
    public static XAudio2EngineHandle CreateEngine(){
        IXAudio2*engine;
        AudioDiagnostics.EnsureSuccess(
            XAudio2Create(&engine,0,(uint)XAUDIO2_DEFAULT_PROCESSOR),
            "XAudio2Create","creating the XAudio2 engine");

        IXAudio2MasteringVoice*masteringVoice;
        HRESULT hr=engine->CreateMasteringVoice(
            &masteringVoice,
            (uint)XAUDIO2_DEFAULT_CHANNELS,
            (uint)XAUDIO2_DEFAULT_SAMPLERATE,
            0,
            null,
            null,
            AudioCategory_GameEffects);

        if(hr.FAILED){
            _=engine->Release();
            AudioDiagnostics.EnsureSuccess(hr,"IXAudio2::CreateMasteringVoice","creating the mastering voice");
        }

        return new XAudio2EngineHandle((nint)engine,(nint)masteringVoice);
    }

    public static void DestroyEngine(in XAudio2EngineHandle engine){
        if(engine.MasteringVoicePointer!=0){
            ((IXAudio2MasteringVoice*)engine.MasteringVoicePointer)->DestroyVoice();
        }

        if(engine.EnginePointer!=0){
            _=((IXAudio2*)engine.EnginePointer)->Release();
        }
    }

    public static XAudio2VoiceHandle CreateVoice(in XAudio2EngineHandle engine,int sampleRate,int channels){
        var pEngine=(IXAudio2*)engine.EnginePointer;

        var blockAlign=(ushort)(channels*(16/8));
        WAVEFORMATEX format=new(){
            wFormatTag=(ushort)WAVE_FORMAT_PCM,
            nChannels=(ushort)channels,
            nSamplesPerSec=(uint)sampleRate,
            wBitsPerSample=16,
            nBlockAlign=blockAlign,
            nAvgBytesPerSec=(uint)sampleRate*blockAlign,
            cbSize=0,
        };

        IXAudio2SourceVoice*voice;
        AudioDiagnostics.EnsureSuccess(
            pEngine->CreateSourceVoice(&voice,&format,0,XAUDIO2_DEFAULT_FREQ_RATIO,null,null,null),
            "IXAudio2::CreateSourceVoice","creating a source voice");

        return new XAudio2VoiceHandle((nint)voice,0);
    }

    public static XAudio2VoiceHandle SubmitBuffer(in XAudio2VoiceHandle voice,ReadOnlySpan<short>samples,bool loop){
        var pVoice=(IXAudio2SourceVoice*)voice.VoicePointer;

        var byteCount=samples.Length*sizeof(short);
        var buffer=NativeMemory.Alloc((nuint)byteCount);
        samples.CopyTo(new Span<short>(buffer,samples.Length));

        XAUDIO2_BUFFER xaBuffer=new(){
            AudioBytes=(uint)byteCount,
            pAudioData=(byte*)buffer,
            LoopCount=(uint)(loop?XAUDIO2_LOOP_INFINITE:0),
        };

        AudioDiagnostics.EnsureSuccess(
            pVoice->SubmitSourceBuffer(&xaBuffer,null),
            "IXAudio2SourceVoice::SubmitSourceBuffer","submitting the PCM buffer");

        return new XAudio2VoiceHandle(voice.VoicePointer,(nint)buffer);
    }

    public static void Start(in XAudio2VoiceHandle voice)=>
        AudioDiagnostics.EnsureSuccess(
            ((IXAudio2SourceVoice*)voice.VoicePointer)->Start(0,(uint)XAUDIO2_COMMIT_NOW),
            "IXAudio2SourceVoice::Start","starting playback");

    public static void Stop(in XAudio2VoiceHandle voice)=>
        AudioDiagnostics.EnsureSuccess(
            ((IXAudio2SourceVoice*)voice.VoicePointer)->Stop(0,(uint)XAUDIO2_COMMIT_NOW),
            "IXAudio2SourceVoice::Stop","stopping playback");

    public static void SetVolume(in XAudio2VoiceHandle voice,float volume)=>
        AudioDiagnostics.EnsureSuccess(
            ((IXAudio2SourceVoice*)voice.VoicePointer)->SetVolume(volume,(uint)XAUDIO2_COMMIT_NOW),
            "IXAudio2Voice::SetVolume","setting voice volume");

    public static void DestroyVoice(in XAudio2VoiceHandle voice){
        if(voice.VoicePointer!=0){
            ((IXAudio2SourceVoice*)voice.VoicePointer)->DestroyVoice();
        }

        if(voice.BufferDataPointer!=0){
            NativeMemory.Free((void*)voice.BufferDataPointer);
        }
    }
}
