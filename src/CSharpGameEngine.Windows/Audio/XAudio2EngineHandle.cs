namespace CSharpGameEngine.Audio.XAudio2;

/// <summary>
/// Opaque handle to one running XAudio2 engine + its mastering voice. Stored as <c>nint</c> (not a
/// raw pointer type) so this struct - and every Component field that holds it - stays in safe
/// context; <see cref="XAudio2Backend"/> is the only place that casts these back to the real COM
/// pointer types.
/// </summary>
public readonly struct XAudio2EngineHandle(nint EnginePointer,nint MasteringVoicePointer) {
    internal readonly nint EnginePointer=EnginePointer;
    internal readonly nint MasteringVoicePointer=MasteringVoicePointer;
}
