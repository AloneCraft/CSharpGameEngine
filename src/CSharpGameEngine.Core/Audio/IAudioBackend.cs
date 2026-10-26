namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// 具体的な音声バックエンド（XAudio2など）が実装するコントラクト。<see cref="CSharpGameEngine.Spatial.Abstractions.ICoordinateConvention"/>と同様、
/// すべてのメンバーを<c>static abstract</c>とする。これにより<see cref="AudioBackend{TBackend, TEngine, TVoice}"/>のすべての呼び出しが
/// コンパイル時に特化した静的呼び出しへ解決される。実行時分岐、リフレクション、ボックス化、仮想ディスパッチは発生しない。
/// 実装型は状態を持たない<c>readonly struct</c>にする。ネイティブリソースはバックエンド型ではなく、
/// <typeparamref name="TEngine"/>／<typeparamref name="TVoice"/>自体（<c>nint</c>を内部に持つ構造体などの不透明な値型ハンドル）に格納する。
/// </summary>
/// <typeparam name="TEngine">実行中の1つの音声エンジンインスタンスを表す不透明なハンドル。</typeparam>
/// <typeparam name="TVoice">エンジンから作成した、再生可能なボイスを表す不透明なハンドル。</typeparam>
public interface IAudioBackend<TEngine,TVoice>
    where TEngine:struct
    where TVoice:struct {
    static abstract TEngine CreateEngine();

    static abstract void DestroyEngine(in TEngine Engine);

    static abstract TVoice CreateVoice(in TEngine Engine,int SampleRate,int Channels);

    /// <summary>
    /// 再生用に16ビットPCMサンプルを送信し、更新されたハンドルを返す。<typeparamref name="TVoice"/>は不変の値型なので、
    /// ハンドルをその場で変更せずに返す。バックエンド側で<paramref name="Samples"/>のコピー先を記録し、
    /// <see cref="DestroyVoice"/>で解放する必要がある場合は、その情報を戻り値のハンドルに含める。
    /// </summary>
    static abstract TVoice SubmitBuffer(in TVoice Voice,ReadOnlySpan<short>Samples,bool Loop);

    static abstract void Start(in TVoice Voice);

    static abstract void Stop(in TVoice Voice);

    static abstract void SetVolume(in TVoice Voice,float Volume);

    static abstract void DestroyVoice(in TVoice Voice);
}
