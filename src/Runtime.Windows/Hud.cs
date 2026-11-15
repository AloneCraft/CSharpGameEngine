namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// ゲームが画面に出す情報の入口(ADR-0077)。System の引数に <c>Hud Hud</c> と書くと、生成されたループが <c>EngineHost.Hud</c> を渡す。
/// 画面の文字は <c>ScreenText</c>(M0104)で出す。これはウィンドウのタイトルバーに出す、デバッグ用の簡単な経路。
/// 同じ文字を毎フレーム渡しても、変わったときにだけウィンドウを更新する。
/// </summary>
public sealed class Hud {
    private readonly EngineHost Host;
    private string?Title;
    internal Hud(EngineHost Host){
        this.Host=Host;
    }
    /// <summary>ウィンドウのタイトルバーの文字を替える。ウィンドウが無い(エディターのプレビューなど)ときは何もしない。</summary>
    public void SetTitle(string Title){
        ArgumentNullException.ThrowIfNull(Title);
        if(string.Equals(this.Title,Title,StringComparison.Ordinal)){ return; }
        this.Title=Title;
        this.Host.SetWindowTitle(Title);
    }
}
