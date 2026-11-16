using CSharpGameEngine.Audio;
using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// ゲームコードから効果音を鳴らす入口(ADR-0077)。System の引数に <c>Audio Audio</c> と書くと、生成されたループが
/// <c>EngineHost.Sound</c> を渡す。鳴らす要求は貯めておき、そのフレームの最後(<c>Present</c>の直前)にまとめて処理する。
/// その時点ではリスナー(<c>AudioListener</c>)の姿勢が確定しているので、System の実行順を気にしなくてよい。
/// </summary>
public sealed class Audio {
    /// <summary>1フレームに受け付ける要求の上限。これを超える分は捨てる(音の洪水でデバイスを詰まらせない)。</summary>
    public const int MaxRequestsPerFrame=32;
    private readonly EngineHost Host;
    private readonly Dictionary<string,PcmClip?>Clips=new(StringComparer.Ordinal);
    private readonly List<Request>Pending=[];
    internal Audio(EngineHost Host){
        this.Host=Host;
    }
    /// <summary>この frame に捨てた要求の数(上限超過)。</summary>
    public int DroppedRequests { get;private set;}
    /// <summary>鳴らし終えた(または捨てた)要求の数。</summary>
    public int FlushedRequests { get;private set;}
    /// <summary>
    /// <paramref name="Source"/>(Entity)の位置から鳴らす。リスナーとの距離で音量が下がり、左右に振られる。
    /// <paramref name="Sound"/>は型付きの参照(<c>Assets.Sounds.Jump</c>。<c>Assets/Sounds/Jump.wav</c>)。モデルや画像は渡せない。
    /// </summary>
    public void Play(SoundAsset Sound,Entity Source,float Volume=1f)=>this.Add(new Request(Sound.Path,Source,false,Volume));
    /// <summary>位置を持たない音(自分の耳元の音、UIの音)を鳴らす。</summary>
    public void PlayLocal(SoundAsset Sound,float Volume=1f)=>this.Add(new Request(Sound.Path,default,true,Volume));
    private void Add(Request Value){
        if(this.Pending.Count>=MaxRequestsPerFrame){
            this.DroppedRequests++;
            return;
        }
        this.Pending.Add(Value);
    }
    /// <summary>貯めた要求をデバイスへ渡す。フレームの最後に1回呼ぶ。</summary>
    internal void Flush(){
        if(this.Pending.Count==0){ return; }
        foreach(var Value in this.Pending){
            this.FlushedRequests++;
            var Clip=this.Load(Value.Sound);
            if(Clip is null||!float.IsFinite(Value.Volume)||Value.Volume<=0f){ continue; }
            var Gain=Math.Min(Value.Volume,4f);
            var Pan=0f;
            if(!Value.Local&&this.Host.TrySpatialize(Value.Source,out var Mix)){
                Gain*=Mix.Attenuation;
                Pan=Mix.Pan;
            }
            this.Host.EnsureAudioDevice().PlayOneShot(Clip,Gain,Pan);
        }
        this.Pending.Clear();
    }
    private PcmClip?Load(string Sound){
        if(string.IsNullOrWhiteSpace(Sound)){ return null; }
        if(this.Clips.TryGetValue(Sound,out var Cached)){ return Cached; }
        PcmClip?Clip=null;
        var Path=System.IO.Path.IsPathRooted(Sound)?Sound:System.IO.Path.Combine(AppContext.BaseDirectory,Sound);
        try {
            if(!WavReader.TryRead(File.ReadAllBytes(Path),out Clip,out var Error)){
                Console.Error.WriteLine($"CGE-AUDIO-CLIP-REJECTED path={Sound} reason={Error}");
            }
        }
        catch(IOException Exception){
            Console.Error.WriteLine($"CGE-AUDIO-CLIP-MISSING path={Sound} reason={Exception.Message}");
        }
        catch(UnauthorizedAccessException Exception){
            Console.Error.WriteLine($"CGE-AUDIO-CLIP-MISSING path={Sound} reason={Exception.Message}");
        }
        this.Clips[Sound]=Clip;
        return Clip;
    }
    private readonly record struct Request(string Sound,Entity Source,bool Local,float Volume);
}
