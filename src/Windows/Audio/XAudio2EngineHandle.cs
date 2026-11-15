namespace CSharpGameEngine.Audio.XAudio2;

/// <summary>
/// 実行中の XAudio2 エンジン1つと、そのマスターボイスを指す、不透明なハンドル。<c>nint</c> として保存し、生ポインター型は使わない。この構造体と、これを保持するすべての
/// Component フィールドを安全なコンテキストに保つためである。実際の COM ポインター型へキャストするのは <see cref="XAudio2Backend"/> のみ。
/// </summary>
public readonly struct XAudio2EngineHandle(nint EnginePointer,nint MasteringVoicePointer) {
    internal readonly nint EnginePointer=EnginePointer;
    internal readonly nint MasteringVoicePointer=MasteringVoicePointer;
}
