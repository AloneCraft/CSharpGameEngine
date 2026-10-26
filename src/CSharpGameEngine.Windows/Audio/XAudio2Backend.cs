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
/// このエンジンが提供する唯一の具象 <see cref="IAudioBackend{TEngine, TVoice}"/> 実装。D3D12 ですでに参照している
/// TerraFX.Interop.Windows 経由の XAudio2 を使い、新しいパッケージは追加しない。インターフェース契約に従う状態なしの <c>readonly struct</c> とし、
/// ネイティブポインターはすべて <see cref="XAudio2EngineHandle"/> / <see cref="XAudio2VoiceHandle"/> 内に保持する。
/// <c>nint</c> から実際の COM ポインター型へ戻すのは、この型のメソッド内だけ。<c>-&gt;</c> 呼び出しや HRESULT 検査用の
/// <see cref="AudioDiagnostics.EnsureSuccess"/> など、
/// <c>CSharpGameEngine.Graphics.D3D12.GraphicsDevice</c> と同じ COM の用法を使う。
/// </summary>
public readonly unsafe struct XAudio2Backend:IAudioBackend<XAudio2EngineHandle,XAudio2VoiceHandle>{
    public static XAudio2EngineHandle CreateEngine(){
        IXAudio2*Engine;
        AudioDiagnostics.EnsureSuccess(
            XAudio2Create(&Engine,0,(uint)XAUDIO2_DEFAULT_PROCESSOR),
            "XAudio2Create","creating the XAudio2 engine");

        IXAudio2MasteringVoice*MasteringVoice;
        var Hr=Engine->CreateMasteringVoice(
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


    /// <summary>送信したすべてのバッファをボイスが再生し終えたか。再生済みならワンショットを破棄できる。</summary>
    public static bool IsFinished(in XAudio2VoiceHandle Voice){
        XAUDIO2_VOICE_STATE State;
        ((IXAudio2SourceVoice*)Voice.VoicePointer)->GetState(&State,(uint)XAUDIO2_VOICE_NOSAMPLESPLAYED);
        return State.BuffersQueued==0;
    }

    /// <summary>モノラルボイスをステレオマスターボイスへ等電力で定位する（-1 は左、0 は中央、1 は右）。</summary>
    public static void SetMonoPan(in XAudio2VoiceHandle Voice,float Pan){
        var Angle=(Math.Clamp(Pan,-1f,1f)+1f)*(MathF.PI/4f);
        var Levels=stackalloc float[2];
        Levels[0]=MathF.Cos(Angle);
        Levels[1]=MathF.Sin(Angle);
        AudioDiagnostics.EnsureSuccess(
            ((IXAudio2SourceVoice*)Voice.VoicePointer)->SetOutputMatrix(null,1,2,Levels,(uint)XAUDIO2_COMMIT_NOW),
            "IXAudio2Voice::SetOutputMatrix","panning a voice");
    }


    public static void DestroyVoice(in XAudio2VoiceHandle Voice){
        if(Voice.VoicePointer!=0){
            ((IXAudio2SourceVoice*)Voice.VoicePointer)->DestroyVoice();
        }

        if(Voice.BufferDataPointer!=0){
            NativeMemory.Free((void*)Voice.BufferDataPointer);
        }
    }
}
