using CSharpGameEngine.Audio.Abstractions;

namespace CSharpGameEngine.Audio.Tests;

/// <summary>
/// Verifies the generic <see cref="AudioBackend{TBackend, TEngine, TVoice}"/> Facade actually
/// forwards every call to whatever <see cref="IAudioBackend{TEngine, TVoice}"/> it is closed over -
/// using a hardware-free fake backend, not XAudio2, so this stays runnable everywhere (CI included).
/// This is also the proof that a second backend really can be plugged in without touching
/// UserEcsProject: the same generic mechanism <c>XAudio2Backend</c> uses is exercised here with an
/// unrelated concrete type.
/// </summary>
public class AudioBackendFacadeTests
{
    [Fact]
    public void CreateEngine_ForwardsToBackend()
    {
        FakeBackend.Reset();

        int engine = AudioBackend<FakeBackend, int, int>.CreateEngine();

        Assert.Equal(1, FakeBackend.EnginesCreated);
        Assert.Equal(1, engine);
    }

    [Fact]
    public void FullLifecycle_ForwardsEveryCallToBackend()
    {
        FakeBackend.Reset();

        int engine = AudioBackend<FakeBackend, int, int>.CreateEngine();
        int voice = AudioBackend<FakeBackend, int, int>.CreateVoice(engine, 44100, 1);
        voice = AudioBackend<FakeBackend, int, int>.SubmitBuffer(voice, new short[] { 1, 2, 3 }, loop: true);
        AudioBackend<FakeBackend, int, int>.SetVolume(voice, 0.5f);
        AudioBackend<FakeBackend, int, int>.Start(voice);
        AudioBackend<FakeBackend, int, int>.Stop(voice);
        AudioBackend<FakeBackend, int, int>.DestroyVoice(voice);
        AudioBackend<FakeBackend, int, int>.DestroyEngine(engine);

        Assert.Equal(1, FakeBackend.VoicesCreated);
        Assert.Equal(3, FakeBackend.LastSubmittedSampleCount);
        Assert.True(FakeBackend.LastLoop);
        Assert.Equal(0.5f, FakeBackend.LastVolume);
        Assert.True(FakeBackend.Started);
        Assert.True(FakeBackend.Stopped);
        Assert.True(FakeBackend.VoiceDestroyed);
        Assert.True(FakeBackend.EngineDestroyed);
    }

    /// <summary>
    /// Trivial in-memory backend using <c>int</c> as both handle types - no native resources, purely
    /// to prove the generic dispatch mechanism itself. Static fields record calls because
    /// <c>static abstract</c> interface members have no instance to hold state on.
    /// </summary>
    private readonly struct FakeBackend : IAudioBackend<int, int>
    {
        public static int EnginesCreated;
        public static int VoicesCreated;
        public static int LastSubmittedSampleCount;
        public static bool LastLoop;
        public static float LastVolume;
        public static bool Started;
        public static bool Stopped;
        public static bool VoiceDestroyed;
        public static bool EngineDestroyed;

        public static void Reset()
        {
            EnginesCreated = 0;
            VoicesCreated = 0;
            LastSubmittedSampleCount = 0;
            LastLoop = false;
            LastVolume = 0f;
            Started = false;
            Stopped = false;
            VoiceDestroyed = false;
            EngineDestroyed = false;
        }

        public static int CreateEngine()
        {
            EnginesCreated++;
            return 1;
        }

        public static void DestroyEngine(in int engine) => EngineDestroyed = true;

        public static int CreateVoice(in int engine, int sampleRate, int channels)
        {
            VoicesCreated++;
            return 42;
        }

        public static int SubmitBuffer(in int voice, ReadOnlySpan<short> samples, bool loop)
        {
            LastSubmittedSampleCount = samples.Length;
            LastLoop = loop;
            return voice;
        }

        public static void Start(in int voice) => Started = true;

        public static void Stop(in int voice) => Stopped = true;

        public static void SetVolume(in int voice, float volume) => LastVolume = volume;

        public static void DestroyVoice(in int voice) => VoiceDestroyed = true;
    }
}
