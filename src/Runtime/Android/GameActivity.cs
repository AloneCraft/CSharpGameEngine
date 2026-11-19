using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
namespace CSharpGameEngine.Runtime.Android;
/// <summary>
/// ADR-0101 段階1：アプリを開くと最初に出る画面。ゲームの生成済み Main を<see cref="AndroidGameHost"/>で動かし、
/// 描画がまだ無い代わりに、ループが回っていることを文字で示す(フレーム数、経過時間、エラー)。
/// 画面の回転などで作り直されないよう、構成の変化はこの画面が自分で受ける。
/// </summary>
[Activity(MainLauncher=true,Exported=true,Theme="@android:style/Theme.DeviceDefault.NoActionBar",
    ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize|ConfigChanges.ScreenLayout|ConfigChanges.SmallestScreenSize|ConfigChanges.Keyboard|ConfigChanges.KeyboardHidden|ConfigChanges.Navigation|ConfigChanges.UiMode)]
public sealed class GameActivity:Activity {
    private const long RefreshMilliseconds=250;
    private TextView?Status;
    private Handler?Refresher;
    private Action?RefreshStep;
    protected override void OnCreate(Bundle?SavedInstanceState){
        base.OnCreate(SavedInstanceState);
        this.Status=new TextView(this){ TextSize=18f };
        this.Status.SetPadding(48,96,48,48);
        this.SetContentView(this.Status);
        AndroidGameHost.Start();
        this.Refresher=new Handler(Looper.MainLooper!);
        this.RefreshStep=()=>{
            this.ShowStatus();
            this.Refresher?.PostDelayed(this.RefreshStep!,RefreshMilliseconds);
        };
        this.Refresher.Post(this.RefreshStep);
    }
    protected override void OnDestroy(){
        if(this.RefreshStep is not null){ this.Refresher?.RemoveCallbacks(this.RefreshStep); }
        // 画面が閉じられたら、ゲームのループにも終わるよう頼む。
        if(this.IsFinishing){ AndroidGameHost.RequestClose(); }
        base.OnDestroy();
    }
    private void ShowStatus(){
        if(this.Status is null){ return; }
        var State=AndroidGameHost.Failure is not null?"Stopped by an error"
            :AndroidGameHost.Completed?"Finished"
            :AndroidGameHost.Running?"Running"
            :"Starting";
        var Text=$"C# Game Engine (Android, no renderer yet)\n\n{State}\nFrames: {AndroidGameHost.FrameCount}\nTime: {AndroidGameHost.Elapsed:hh\\:mm\\:ss}";
        if(AndroidGameHost.Failure is{ }Failure){ Text+=$"\n\n{Failure}"; }
        this.Status.Text=Text;
    }
}
