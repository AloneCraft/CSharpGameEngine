using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools&gt;Options の「C# Game Engine / Scene Editor」ページ。このVSIX初のオプションページ
/// (ADR-0006が将来の方向性として挙げていたもの)。
///
/// <para>
/// 保存先は<see cref="SceneEditorSettings"/>(コレクション<c>CSharpGameEngine\SceneEditor</c>、
/// ADR-0041決定事項8)であり、<c>DialogPage</c>標準の永続化
/// (<c>ApplicationPrivateSettings</c>)は**使わない**。両方に書くと同じ設定が2箇所に別々に
/// 永続化され、Scene View側が読む値とオプション画面が表示する値が食い違うため、
/// <see cref="LoadSettingsFromStorage"/>/<see cref="SaveSettingsToStorage"/>を上書きして
/// <see cref="SceneEditorSettings"/>へ委譲する。
/// </para>
///
/// <para>
/// **コントロールは一度作ったら使い回す**(以前は<c>OnClosed</c>でnullにしていたため、
/// <see cref="OnApply"/>が<c>Child</c>経由で既定値の新しいコントロールを作り直して読み、
/// チェックが保存されない経路があった)。加えてユーザー編集は
/// <see cref="SceneEditorOptionsControl.ValueEdited"/>で即座にモデルへ取り込むので、
/// <see cref="OnApply"/>がコントロールの状態に依存しなくても正しい値を書ける。
/// </para>
///
/// <para>
/// WPFで描くため<c>UIElementDialogPage</c>を使う(素の<c>DialogPage</c>はWinFormsの
/// <c>PropertyGrid</c>描画になり、<c>UseWindowsForms=false</c>のこのプロジェクトの方針と
/// 既存のWPF+XAMLツールウィンドウ群から外れる)。
/// </para>
/// </summary>
[Guid("6b6d4f3a-59c8-4a2e-9a1d-0f7b2c8e4d51")]
public sealed class SceneEditorOptionsPage:UIElementDialogPage {
    private readonly SceneEditorOptionsModel Model=new();
    private SceneEditorOptionsControl?Control;

    protected override UIElement Child=>this.EnsureControl();

    /// <summary>ページを開くたびに永続化された現在値を読み直してコントロールへ反映する。</summary>
    protected override void OnActivate(CancelEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnActivate(Args);
        this.Model.LoadFromSettings();
        this.EnsureControl().LoadFrom(this.Model);
    }

    /// <summary>
    /// OK/適用。モデルは<see cref="SceneEditorOptionsControl.ValueEdited"/>で常に最新だが、
    /// 念のためコントロールからも読み直してから確定させる(どちらの経路でも同じ結果になる)。
    /// 書き込みの最後に<c>SettingsChanged</c>が1回だけ発火し、既に開いているScene Viewへ反映される
    /// (<c>SceneHwndHost</c>が購読)。
    /// </summary>
    protected override void OnApply(PageApplyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.ApplyBehavior==ApplyKind.Apply){
            this.Control?.StoreTo(this.Model);
            this.Model.SaveToSettings();
            // クランプ後の値を表示へ戻す(範囲外を入力したまま画面に残さない)。
            this.Control?.LoadFrom(this.Model);
        }

        base.OnApply(Args);
    }

    /// <summary>
    /// VSの設定インポート/エクスポートや初回表示時にも呼ばれる。UIスレッド以外から来た場合は
    /// 何もしない — ここで例外を投げるとページ全体が壊れるため(値は<see cref="OnActivate"/>で
    /// 読み直される)。
    /// </summary>
    public override void LoadSettingsFromStorage(){
        if(!ThreadHelper.CheckAccess()){
            return;
        }

        // 上のCheckAccessでUIスレッド以外は既に返しているが、VSTHRD010はこの形を追えないため明示する。
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Model.LoadFromSettings();
    }

    public override void SaveSettingsToStorage(){
        if(!ThreadHelper.CheckAccess()){
            return;
        }

        ThreadHelper.ThrowIfNotOnUIThread();
        this.Model.SaveToSettings();
    }

    private SceneEditorOptionsControl EnsureControl(){
        if(this.Control is not { } Existing){
            Existing=new SceneEditorOptionsControl();
            // ユーザーの編集を即座にモデルへ取り込む。OKの時点でコントロールを読む方式だけに
            // 頼ると、コントロールが作り直された場合に既定値を書いてしまう。
            Existing.ValueEdited+=this.OnControlValueEdited;
            this.Control=Existing;
        }

        return Existing;
    }

    private void OnControlValueEdited(){
        this.Control?.StoreTo(this.Model);
    }
}
