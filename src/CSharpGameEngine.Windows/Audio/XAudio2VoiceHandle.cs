namespace CSharpGameEngine.Audio.XAudio2;

/// <summary>
/// Opaque handle to one XAudio2 source voice. <see cref="BufferDataPointer"/> is the unmanaged PCM
/// buffer <see cref="XAudio2Backend.SubmitBuffer"/> allocated via <c>NativeMemory.Alloc</c> (zero
/// until a buffer has been submitted) - XAudio2 streams from this pointer for as long as the voice
/// plays, so it must stay valid until <see cref="XAudio2Backend.DestroyVoice"/> frees it.
/// </summary>
public readonly struct XAudio2VoiceHandle
{
    internal readonly nint VoicePointer;
    internal readonly nint BufferDataPointer;

    internal XAudio2VoiceHandle(nint voicePointer, nint bufferDataPointer)
    {
        VoicePointer = voicePointer;
        BufferDataPointer = bufferDataPointer;
    }
}
