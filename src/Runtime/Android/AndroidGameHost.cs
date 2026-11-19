using System.Diagnostics;
namespace CSharpGameEngine.Runtime.Android;
/// <summary>
/// ADR-0101 段階1：Android でゲームの生成済み Main を動かす部分と、<see cref="GameActivity"/>が画面に出す状態。
/// 描画・音・入力はまだ無く、ゲームの System だけが回る。同時に動く Main は1つだけ。
/// Android の SDK はアプリをライブラリとしてコンパイルするので、Main を呼ぶ人がいない。生成された入口が、アセンブリを
/// 読み込んだときに<see cref="SetEntryPoint"/>で Main を渡す。
/// </summary>
public static class AndroidGameHost {
    private const long FrameMilliseconds=16;
    private static readonly object Gate=new();
    private static readonly Stopwatch Clock=new();
    private static Action?EntryPoint;
    private static Thread?GameThread;
    private static long FrameCountStorage;
    private static long LastFrameMilliseconds;
    private static volatile bool CloseRequestedStorage;
    private static volatile string?FailureStorage;
    private static volatile bool CompletedStorage;
    /// <summary>アプリの画面が閉じられ、ゲームのループに終わってほしいとき true。</summary>
    public static bool CloseRequested=>CloseRequestedStorage;
    /// <summary>ゲームのループが回したフレームの数。</summary>
    public static long FrameCount=>Interlocked.Read(ref FrameCountStorage);
    /// <summary>ゲームを始めてからの時間。</summary>
    public static TimeSpan Elapsed=>Clock.Elapsed;
    /// <summary>Main が例外で止まったときの内容。止まっていなければ null。</summary>
    public static string?Failure=>FailureStorage;
    /// <summary>Main が戻った(ゲームが終わった)とき true。</summary>
    public static bool Completed=>CompletedStorage;
    /// <summary>ゲームのループが動いているか。</summary>
    public static bool Running=>GameThread is{IsAlive:true};
    /// <summary>生成された入口が、ゲームの Main を渡す。[GameSystem] が無いゲームでは呼ばれない。</summary>
    public static void SetEntryPoint(Action Main){
        ArgumentNullException.ThrowIfNull(Main);
        lock(Gate){ EntryPoint=Main; }
    }
    /// <summary>
    /// 渡された Main を別のスレッドで動かす。まだ動いていれば、終わる頼みを取り消すだけにする。前のゲームが終わっていれば
    /// (画面を閉じてから開き直したとき)、新しく始め直す。Main は毎回 EngineHost を新しく作る。
    /// 画面のスレッドを止めないために、ゲームのループは専用のスレッドで回す。
    /// </summary>
    public static void Start(){
        lock(Gate){
            CloseRequestedStorage=false;
            if(GameThread is{IsAlive:true}){ return; }
            if(EntryPoint is not{ }Main){
                FailureStorage="The game has no entry point. Add a [GameSystem] with an [Update] method so the build generates Main.";
                return;
            }
            Interlocked.Exchange(ref FrameCountStorage,0);
            LastFrameMilliseconds=0;
            FailureStorage=null;
            CompletedStorage=false;
            Clock.Restart();
            GameThread=new Thread(()=>Run(Main)){ IsBackground=true,Name="CgeGameLoop" };
            GameThread.Start();
        }
    }
    /// <summary>ゲームのループに終わるよう頼む。ループは次のフレームの初めに止まる。</summary>
    public static void RequestClose()=>CloseRequestedStorage=true;
    /// <summary>
    /// 1フレームが終わるたびに<see cref="CSharpGameEngine.Runtime.Windows.EngineHost"/>から呼ばれる。描画の待ちが無いので、
    /// ここで約 60 回/秒に抑え、CPU と電池を使い切らないようにする。
    /// </summary>
    internal static void EndFrame(){
        Interlocked.Increment(ref FrameCountStorage);
        var Now=Clock.ElapsedMilliseconds;
        var Wait=LastFrameMilliseconds+FrameMilliseconds-Now;
        if(Wait>0){ Thread.Sleep((int)Wait); }
        LastFrameMilliseconds=Math.Max(Now,LastFrameMilliseconds+FrameMilliseconds);
    }
    private static void Run(Action Main){
        try {
            Main();
        } catch(Exception Error){
            FailureStorage=Error.ToString();
            global::Android.Util.Log.Error("CSharpGameEngine",Error.ToString());
        } finally {
            Clock.Stop();
            CompletedStorage=true;
        }
    }
}
