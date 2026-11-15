using System;
using System.Globalization;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools&gt;Options の「Scene Editor」ページが編集する値の保持と検証。
///
/// <para>
/// ページ本体(<see cref="SceneEditorOptionsPage"/>)から検証ロジックを切り離してあるのは、
/// テストプロジェクトが<c>Package</c>/<c>DialogPage</c>系の型を読み込めない(実VSホストが必要)
/// ためである。VS型に一切依存しないこのクラスへロジックを寄せることで、クランプと文字列解釈を
/// ユニットテストで固定できる。
/// </para>
///
/// <para>
/// 公開するのは<see cref="SceneEditorSettings"/>の9項目のうち恒久的な「設定」に相当する5項目だけ。
/// <c>ActiveTool</c>/<c>TransformSpace</c>/<c>ProjectionMode</c>は「今どのツールを選んでいるか」という
/// 一時的な現在状態なのでツールバー側の責務であり、<c>NavigationPreset</c>はenumが
/// <c>Default</c>/<c>Custom</c>の2値しか無くプリセット値の充填がM0020スコープ外(ADR-0041
/// 「器のみ用意する」)なので、今公開すると何も起きないダミーUIになる。
/// </para>
/// </summary>
internal sealed class SceneEditorOptionsModel {
    internal SceneEditorOptionsModel(){
        this.NavigationSpeedMultiplier=SceneEditorSettings.DefaultNavigationSpeedMultiplier;
        this.InvertLookX=SceneEditorSettings.DefaultInvertLookX;
        this.InvertLookY=SceneEditorSettings.DefaultInvertLookY;
        this.InvertOrbitX=SceneEditorSettings.DefaultInvertOrbitX;
        this.InvertOrbitY=SceneEditorSettings.DefaultInvertOrbitY;
    }

    /// <summary>
    /// ナビゲーション速度倍率。実行時のRMB+Wheel調整
    /// (<see cref="SceneViewState.ApplyNavigationSpeedAdjust"/>)と同じ範囲へクランプする。
    /// </summary>
    internal float NavigationSpeedMultiplier { get;set;}

    internal bool InvertLookX { get;set;}

    internal bool InvertLookY { get;set;}

    internal bool InvertOrbitX { get;set;}

    internal bool InvertOrbitY { get;set;}

    /// <summary>
    /// 速度倍率をクランプする。NaN/無限は「入力が壊れている」ケースなので既定値へ倒す
    /// (<c>SceneEditorSettings.ReadNavigationSpeedMultiplier</c>が壊れた文字列に対して
    /// 既定値へフォールバックするのと同じ方針)。
    /// </summary>
    internal static float ClampNavigationSpeedMultiplier(float Value){
        if(float.IsNaN(Value)||float.IsInfinity(Value)){
            return SceneEditorSettings.DefaultNavigationSpeedMultiplier;
        }

        return Math.Min(
            Math.Max(Value,SceneViewState.MinNavigationSpeedMultiplier),
            SceneViewState.MaxNavigationSpeedMultiplier);
    }

    /// <summary>
    /// テキスト入力を速度倍率として解釈する。解釈できない文字列は既定値へ倒す(ユーザーが
    /// 入力欄を空にしたまま OK を押しても壊れないようにする)。カルチャ非依存で読むのは
    /// <see cref="SceneEditorSettings"/>の永続化が<c>InvariantCulture</c>である以上そこと
    /// 揃える必要があるため。
    /// </summary>
    internal static float ParseNavigationSpeedMultiplier(string?Text){
        if(string.IsNullOrWhiteSpace(Text)
            ||!float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var Value)){
            return SceneEditorSettings.DefaultNavigationSpeedMultiplier;
        }

        return ClampNavigationSpeedMultiplier(Value);
    }

    /// <summary>速度倍率の表示文字列。永続化側と同じ<c>InvariantCulture</c>で書く。</summary>
    internal static string FormatNavigationSpeedMultiplier(float Value)=>
        Value.ToString("G9",CultureInfo.InvariantCulture);

    /// <summary>永続化された値を読み込む。UIスレッド前提(<see cref="SceneEditorSettings"/>の制約)。</summary>
    internal void LoadFromSettings(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.NavigationSpeedMultiplier=ClampNavigationSpeedMultiplier(
            SceneEditorSettings.ReadNavigationSpeedMultiplier());
        this.InvertLookX=SceneEditorSettings.ReadInvertLookX();
        this.InvertLookY=SceneEditorSettings.ReadInvertLookY();
        this.InvertOrbitX=SceneEditorSettings.ReadInvertOrbitX();
        this.InvertOrbitY=SceneEditorSettings.ReadInvertOrbitY();
    }

    /// <summary>
    /// 5項目をまとめて書き戻す。<see cref="SceneEditorSettings.WriteNavigationSettings"/>が
    /// 最後に<c>SettingsChanged</c>を1回だけ発火し、開いているScene Viewへ反映される。
    /// </summary>
    internal void SaveToSettings(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.NavigationSpeedMultiplier=ClampNavigationSpeedMultiplier(this.NavigationSpeedMultiplier);
        SceneEditorSettings.WriteNavigationSettings(
            this.NavigationSpeedMultiplier,this.InvertLookX,this.InvertLookY,
            this.InvertOrbitX,this.InvertOrbitY);
    }

    /// <summary>「既定へ戻す」。書き込みはしない(OKまで確定させない)。</summary>
    internal void ResetToDefaults(){
        this.NavigationSpeedMultiplier=SceneEditorSettings.DefaultNavigationSpeedMultiplier;
        this.InvertLookX=SceneEditorSettings.DefaultInvertLookX;
        this.InvertLookY=SceneEditorSettings.DefaultInvertLookY;
        this.InvertOrbitX=SceneEditorSettings.DefaultInvertOrbitX;
        this.InvertOrbitY=SceneEditorSettings.DefaultInvertOrbitY;
    }
}
