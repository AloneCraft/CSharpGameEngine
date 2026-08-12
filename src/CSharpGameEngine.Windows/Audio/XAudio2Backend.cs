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
        IXAudio2*Engine;
        AudioDiagnostics.EnsureSuccess(
            XAudio2Create(&Engine,0,(uint)XAUDIO2_DEFAULT_PROCESSOR),
            "XAudio2Create","creating the XAudio2 engine");

        IXAudio2MasteringVoice*MasteringVoice;
        HRESULT Hr=Engine->CreateMasteringVoice(
            &MasteringVoice,
            (uint)XAUDIO2_DEFAULT_CHANNELS,
            (uint)XAUDIO2_DEFAULT_SAMPLERATE,
            0,
            null,
            null,
            AudioCategory_GameEffects);

        if(Hr.FAILED){
            _=Engine->Release();
            AudioDiagnostics.EnsureSuccess(Hr,"IXAudio2::CreateMasteringVoice","creating the mastering voice");
        }

        return new XAudio2EngineHandle((nint)Engine,(nint)MasteringVoice);
    }

    public static void DestroyEngine(in XAudio2EngineHandle Engine){
        if(Engine.MasteringVoicePointer!=0){
            ((IXAudio2MasteringVoice*)Engine.MasteringVoicePointer)->DestroyVoice();
        }

        if(Engine.EnginePointer!=0){
            _=((IXAudio2*)Engine.EnginePointer)->Release();
        }
    }

    public static XAudio2VoiceHandle CreateVoice(in XAudio2EngineHandle Engine,int SampleRate,int Channels){
        var PEngine=(IXAudio2*)Engine.EnginePointer;

        var BlockAlign=(ushort)(Channels*(16/8));
        WAVEFORMATEX Format=new(){
            wFormatTag=(ushort)WAVE_FORMAT_PCM,
            nChannels=(ushort)Channels,
            nSamplesPerSec=(uint)SampleRate,
            wBitsPerSample=16,
            nBlockAlign=BlockAlign,
            nAvgBytesPerSec=(uint)SampleRate*BlockAlign,
            cbSize=0,
        };

        IXAudio2SourceVoice*Voice;
        AudioDiagnostics.EnsureSuccess(
            PEngine->CreateSourceVoice(&Voice,&Format,0,XAUDIO2_DEFAULT_FREQ_RATIO,null,null,null),
            "IXAudio2::CreateSourceVoice","creating a source voice");

        return new XAudio2VoiceHandle((nint)Voice,0);
    }

    public static XAudio2VoiceHandle SubmitBuffer(in XAudio2VoiceHandle Voice,ReadOnlySpan<short>Samples,bool Loop){
        var PVoice=(IXAudio2SourceVoice*)Voice.VoicePointer;

        var ByteCount=Samples.Length*sizeof(short);
        var Buffer=NativeMemory.Alloc((nuint)ByteCount);
        Samples.CopyTo(new Span<short>(Buffer,Samples.Length));

        XAUDIO2_BUFFER XaBuffer=new(){
            AudioBytes=(uint)ByteCount,
            pAudioData=(byte*)Buffer,
            LoopCount=(uint)(Loop?XAUDIO2_LOOP_INFINITE:0),
        };

        AudioDiagnostics.EnsureSuccess(
            PVoice->SubmitSourceBuffer(&XaBuffer,null),
            "IXAudio2SourceVoice::SubmitSourceBuffer","submitting the PCM buffer");

        return new XAudio2VoiceHandle(Voice.VoicePointer,(nint)Buffer);
    }

    public static void Start(in XAudio2VoiceHandle Voice)=>
        AudioDiagnostics.EnsureSuccess(
            ((IXAudio2SourceVoice*)Voice.VoicePointer)->Start(0,(uint)XAUDIO2_COMMIT_NOW),
            "IXAudio2SourceVoice::Start","starting playback");

    public static void Stop(in XAudio2VoiceHandle Voice)=>
        AudioDiagnostics.EnsureSuccess(
            ((IXAudio2SourceVoice*)Voice.VoicePointer)->Stop(0,(uint)XAUDIO2_COMMIT_NOW),
            "IXAudio2SourceVoice::Stop","stopping playback");

    public static void SetVolume(in XAudio2VoiceHandle Voice,float Volume)=>
        AudioDiagnostics.EnsureSuccess(
            ((IXAudio2SourceVoice*)Voice.VoicePointer)->SetVolume(Volume,(uint)XAUDIO2_COMMIT_NOW),
            "IXAudio2Voice::SetVolume","setting voice volume");

    public static void DestroyVoice(in XAudio2VoiceHandle Voice){
        if(Voice.VoicePointer!=0){
            ((IXAudio2SourceVoice*)Voice.VoicePointer)->DestroyVoice();
        }

        if(Voice.BufferDataPointer!=0){
            NativeMemory.Free((void*)Voice.BufferDataPointer);
        }
    }
}
