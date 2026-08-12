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
public class AudioBackendFacadeTests {
    [Fact]
    public void CreateEngine_ForwardsToBackend(){
        FakeBackend.Reset();

        var Engine=AudioBackend<FakeBackend,int,int>.CreateEngine();

        Assert.Equal(1,FakeBackend.EnginesCreated);
        Assert.Equal(1,Engine);
    }

    [Fact]
    public void FullLifecycle_ForwardsEveryCallToBackend(){
        FakeBackend.Reset();

        var Engine=AudioBackend<FakeBackend,int,int>.CreateEngine();
        var Voice=AudioBackend<FakeBackend,int,int>.CreateVoice(Engine,44100,1);
        Voice=AudioBackend<FakeBackend,int,int>.SubmitBuffer(Voice,new short[]{ 1,2,3 },Loop:true);
        AudioBackend<FakeBackend,int,int>.SetVolume(Voice,0.5f);
        AudioBackend<FakeBackend,int,int>.Start(Voice);
        AudioBackend<FakeBackend,int,int>.Stop(Voice);
        AudioBackend<FakeBackend,int,int>.DestroyVoice(Voice);
        AudioBackend<FakeBackend,int,int>.DestroyEngine(Engine);

        Assert.Equal(1,FakeBackend.VoicesCreated);
        Assert.Equal(3,FakeBackend.LastSubmittedSampleCount);
        Assert.True(FakeBackend.LastLoop);
        Assert.Equal(0.5f,FakeBackend.LastVolume);
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
    private readonly struct FakeBackend:IAudioBackend<int,int>{
        public static int EnginesCreated;
        public static int VoicesCreated;
        public static int LastSubmittedSampleCount;
        public static bool LastLoop;
        public static float LastVolume;
        public static bool Started;
        public static bool Stopped;
        public static bool VoiceDestroyed;
        public static bool EngineDestroyed;

        public static void Reset(){
            EnginesCreated=0;
            VoicesCreated=0;
            LastSubmittedSampleCount=0;
            LastLoop=false;
            LastVolume=0f;
            Started=false;
            Stopped=false;
            VoiceDestroyed=false;
            EngineDestroyed=false;
        }

        public static int CreateEngine(){
            EnginesCreated++;
            return 1;
        }

        public static void DestroyEngine(in int Engine)=>EngineDestroyed=true;

        public static int CreateVoice(in int Engine,int SampleRate,int Channels){
            VoicesCreated++;
            return 42;
        }

        public static int SubmitBuffer(in int Voice,ReadOnlySpan<short>Samples,bool Loop){
            LastSubmittedSampleCount=Samples.Length;
            LastLoop=Loop;
            return Voice;
        }

        public static void Start(in int Voice)=>Started=true;

        public static void Stop(in int Voice)=>Stopped=true;

        public static void SetVolume(in int Voice,float Volume)=>LastVolume=Volume;

        public static void DestroyVoice(in int Voice)=>VoiceDestroyed=true;
    }
}
