namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// ゲームが画面に出す情報の入口(ADR-0077)。System の引数に <c>Hud Hud</c> と書くと、生成されたループが <c>EngineHost.Hud</c> を渡す。
/// 画面上の文字(UIの描画)はまだ無いので、今はウィンドウのタイトルバーに出す。車の速度計がこれを使う。
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
