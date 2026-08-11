namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// Contract one concrete audio backend (e.g. XAudio2) must implement. Mirrors
/// <see cref="CSharpGameEngine.Spatial.Abstractions.ICoordinateConvention"/>: every member is
/// <c>static abstract</c> so <see cref="AudioBackend{TBackend, TEngine, TVoice}"/> can resolve every
/// call as a compile-time-monomorphized static call - no runtime branching, no reflection, no
/// boxing, no virtual dispatch. Implementations must be a stateless <c>readonly struct</c>; the
/// native resources live in <typeparamref name="TEngine"/>/<typeparamref name="TVoice"/> themselves
/// (opaque value-type handles, e.g. an <c>nint</c>-backed struct), never in the backend type.
/// </summary>
/// <typeparam name="TEngine">Opaque handle to one running audio engine instance.</typeparam>
/// <typeparam name="TVoice">Opaque handle to one playable voice created from an engine.</typeparam>
public interface IAudioBackend<TEngine, TVoice>
    where TEngine : struct
    where TVoice : struct
{
    static abstract TEngine CreateEngine();

    static abstract void DestroyEngine(in TEngine engine);

    static abstract TVoice CreateVoice(in TEngine engine, int sampleRate, int channels);

    /// <summary>
    /// Submits 16-bit PCM samples for playback and returns the updated handle. The handle is
    /// returned (rather than mutated in place) because <typeparamref name="TVoice"/> is an immutable
    /// value type; a backend that must remember where it copied <paramref name="samples"/> (so
    /// <see cref="DestroyVoice"/> can free it) folds that into the returned handle.
    /// </summary>
    static abstract TVoice SubmitBuffer(in TVoice voice, ReadOnlySpan<short> samples, bool loop);

    static abstract void Start(in TVoice voice);

    static abstract void Stop(in TVoice voice);

    static abstract void SetVolume(in TVoice voice, float volume);

    static abstract void DestroyVoice(in TVoice voice);
}
