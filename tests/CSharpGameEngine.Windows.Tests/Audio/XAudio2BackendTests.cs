using CSharpGameEngine.Audio.Abstractions;
using CSharpGameEngine.Audio.XAudio2;
using CSharpGameEngine.Graphics.D3D12.Tests;

namespace CSharpGameEngine.Audio.Tests;

/// <summary>
/// テストホストが使用可能なオーディオデバイスに対し、実際のXAudio2バックエンドをスモークテストする。
/// ヘッドレスでもWindowsは最低1つの既定エンドポイントを登録する（実GPUを必要としないWARPベースのGPUテストと同じ考え方）。
/// Graphicsテストの<see cref="WindowsOnlyFactAttribute"/>を再利用する。同じ「ネイティブWindows APIなので、他環境ではスキップ」という条件である。
/// </summary>
public class XAudio2BackendTests {
    [WindowsOnlyFact]
    [Trait("Category","Audio")]
    public void CreateEngine_ThenDestroy_DoesNotThrow(){
        var Engine=AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.CreateEngine();
        AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.DestroyEngine(Engine);
    }

    [WindowsOnlyFact]
    [Trait("Category","Audio")]
    public void FullPlaybackLifecycle_DoesNotThrow(){
        var Engine=AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.CreateEngine();

        try {
            var Voice=AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.CreateVoice(Engine,SampleRate:44100,Channels:1);
            var Samples=ProceduralAudioClip.GenerateSineWave(440f,0.1f,44100);

            Voice=AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.SubmitBuffer(Voice,Samples,Loop:true);
            AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.SetVolume(Voice,0.1f);
            AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.Start(Voice);
            AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.Stop(Voice);
            AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.DestroyVoice(Voice);
        } finally {
            AudioBackend<XAudio2Backend,XAudio2EngineHandle,XAudio2VoiceHandle>.DestroyEngine(Engine);
        }
    }
}
