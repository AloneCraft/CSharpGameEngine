using CSharpGameEngine.Audio.Abstractions;

namespace CSharpGameEngine.Audio.Tests;

/// <summary>
/// 汎用の <see cref="AudioBackend{TBackend, TEngine, TVoice}"/> Facade が、実際に委譲先の
/// <see cref="IAudioBackend{TEngine, TVoice}"/> へすべての呼び出しを転送することを検証する。XAudio2 ではなくハードウェア不要の偽バックエンドを使うため
/// 、CI を含むどの環境でも実行できる。また、UserEcsProject を変更せずに第2のバックエンドを追加できる証拠にもなる。ここでは <c>XAudio2Backend</c> が使うのと同じ
/// 汎用の仕組みを、無関係な具象型で実行する。
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
    /// 両方のハンドル型に <c>int</c> を使う、単純なメモリ内バックエンド。ネイティブリソースを持たず、汎用ディスパッチ自体を検証するためだけに使う。状態を保持するインスタンスが存在しないため、呼び
    /// 出しは static フィールドへ記録する。<c>static abstract</c> インターフェースメンバーにはインスタンス上の状態を保持できないためである。
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
