namespace CSharpGameEngine.Audio.XAudio2;

/// <summary>
/// XAudio2 のソースボイス1つを指す、不透明なハンドル。<see cref="BufferDataPointer"/> は、
/// <see cref="XAudio2Backend.SubmitBuffer"/> が <c>NativeMemory.Alloc</c> で確保したアンマネージ PCM バッファを指す（バッファ
/// 送信前はゼロ）。XAudio2 はボイスの再生中このポインターから音声を読み取るため、<see cref="XAudio2Backend.DestroyVoice"/> が解放するまで有効でなければな
/// らない。
/// </summary>
public readonly struct XAudio2VoiceHandle(nint VoicePointer,nint BufferDataPointer) {
    internal readonly nint VoicePointer=VoicePointer;
    internal readonly nint BufferDataPointer=BufferDataPointer;
}
