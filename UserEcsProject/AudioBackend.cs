// ============================================================================
// プラットフォーム依存の集中点。このプロジェクトが選んだ唯一のオーディオバックエンド(XAudio2)への
// 参照はこのファイルだけに閉じる。Components.cs/Systems/RuntimeSystem.cs/Scenes/MainScene.csは
// AudioEngineHandle/AudioVoiceHandle/GameAudioという名前だけを見て、CSharpGameEngine.Audio.XAudio2を
// 直接参照しない。将来別バックエンドへ切り替える場合、変更箇所はこのファイルだけでよい。
//
// 座標系(xyz→RUF、CSharpGameEngine.Spatial.Abstractions)と同じ設計: IAudioBackend<TEngine,TVoice>は
// 全メンバーがstatic abstract、AudioBackend<TBackend,TEngine,TVoice>はstruct制約付きジェネリック
// Facadeなので、下のGameAudio.CreateEngine()等はコンパイル時にXAudio2Backend.CreateEngine()への
// 直接静的呼び出しへ解決される(仮想ディスパッチ・Boxing・Reflectionなし、Spatial<TConvention>と
// 同じ理由でゼロオーバーヘッド)。Spatial.cs のXMLコメントが推奨する「1プリセットを選んだプロジェクトは
// 自分の非ジェネリックFacadeを公開する」という規約に倣い、GameAudioという非ジェネリック型を公開する。
// ============================================================================

global using AudioEngineHandle=CSharpGameEngine.Audio.XAudio2.XAudio2EngineHandle;
global using AudioVoiceHandle=CSharpGameEngine.Audio.XAudio2.XAudio2VoiceHandle;

using CSharpGameEngine.Audio.Abstractions;
using CSharpGameEngine.Audio.XAudio2;

namespace UserEcsProject;

internal static class GameAudio {
    public static AudioEngineHandle CreateEngine()=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.CreateEngine();

    public static void DestroyEngine(in AudioEngineHandle engine)=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyEngine(engine);

    public static AudioVoiceHandle CreateVoice(in AudioEngineHandle engine,int sampleRate,int channels)=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.CreateVoice(engine,sampleRate,channels);

    public static AudioVoiceHandle SubmitBuffer(in AudioVoiceHandle voice,ReadOnlySpan<short>samples,bool loop)=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.SubmitBuffer(voice,samples,loop);

    public static void Start(in AudioVoiceHandle voice)=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Start(voice);

    public static void Stop(in AudioVoiceHandle voice)=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.Stop(voice);

    public static void SetVolume(in AudioVoiceHandle voice,float volume)=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.SetVolume(voice,volume);

    public static void DestroyVoice(in AudioVoiceHandle voice)=>
        AudioBackend<XAudio2Backend,AudioEngineHandle,AudioVoiceHandle>.DestroyVoice(voice);
}
