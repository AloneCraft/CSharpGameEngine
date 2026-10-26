namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// 選択した1つの<typeparamref name="TBackend"/>を型で指定するエントリーポイント。すべてのメンバー呼び出しはコンパイル時に
/// <typeparamref name="TBackend"/>の静的メンバーへ解決される（実行時のバックエンド振り分けなし）。座標規約を
/// <see cref="CSharpGameEngine.Spatial.Abstractions.Spatial{TConvention}"/>で扱う方法と同じである。既にバックエンドを1つだけ選択したゲームプロジェクトでは、
/// <c>GameAudio</c>などの非ジェネリックなファサードを用意し、<c>AudioBackend&lt;ThatBackend, ...&gt;</c>へ委譲すること。
/// そうすればアプリケーションコードで<typeparamref name="TBackend"/>／<typeparamref name="TEngine"/>／<typeparamref name="TVoice"/>を
/// 明記せずに済む。<c>UserEcsProject/GameAudio.cs</c>を参照。
/// </summary>
public static class AudioBackend<TBackend,TEngine,TVoice>
    where TBackend:struct,IAudioBackend<TEngine,TVoice>
    where TEngine:struct
    where TVoice:struct {
    public static TEngine CreateEngine()=>TBackend.CreateEngine();

    public static void DestroyEngine(in TEngine Engine)=>TBackend.DestroyEngine(Engine);

    public static TVoice CreateVoice(in TEngine Engine,int SampleRate,int Channels)=>
        TBackend.CreateVoice(Engine,SampleRate,Channels);

    public static TVoice SubmitBuffer(in TVoice Voice,ReadOnlySpan<short>Samples,bool Loop)=>
        TBackend.SubmitBuffer(Voice,Samples,Loop);

    public static void Start(in TVoice Voice)=>TBackend.Start(Voice);

    public static void Stop(in TVoice Voice)=>TBackend.Stop(Voice);

    public static void SetVolume(in TVoice Voice,float Volume)=>TBackend.SetVolume(Voice,Volume);

    public static void DestroyVoice(in TVoice Voice)=>TBackend.DestroyVoice(Voice);
}
