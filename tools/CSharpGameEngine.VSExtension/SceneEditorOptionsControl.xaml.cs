using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools&gt;Options の「C# Game Engine / Scene Editor」ページのWPF内容。
///
/// <para>
/// 検証・クランプ・書式は<see cref="SceneEditorOptionsModel"/>側に置き、このコードビハインドは
/// コントロールとモデルの詰め替えだけを行う。ページ本体とXAMLは実VSホストが必要で
/// ユニットテストできないため、テストできるロジックを1箇所へ寄せている。
/// </para>
///
/// <para>
/// **ユーザーの編集は即座にモデルへ反映する**(<see cref="ValueEdited"/>)。[OK]の時点で
/// コントロールから読み出す方式にしていたときは、ページの再生成やナビゲーションでコントロールが
/// 作り直されると既定値を読んでしまい、チェックが保存されない経路があった。モデルを常に最新に
/// 保てば、<see cref="SceneEditorOptionsPage.OnApply"/>がどのタイミングで走っても正しい値を書ける。
/// </para>
///
/// <para>
/// 素の<c>DialogPage</c>はWinFormsの<c>PropertyGrid</c>で描画されるが、このVSIXは
/// <c>UseWPF=true</c>/<c>UseWindowsForms=false</c>で既存のツールウィンドウもすべてWPF+XAMLなので、
/// <see cref="SceneEditorOptionsPage"/>は<c>UIElementDialogPage</c>を使ってWPFで統一している。
/// </para>
/// </summary>
public partial class SceneEditorOptionsControl:UserControl {
    /// <summary>UI更新中はユーザー編集として扱わない(<see cref="LoadFrom"/>による再入を防ぐ)。</summary>
    private bool SuppressValueEdited;

    public SceneEditorOptionsControl(){
        InitializeComponent();

        this.NavigationSpeedRangeHint.Text=string.Format(
            CultureInfo.InvariantCulture,
            "指定できる範囲: {0} 〜 {1}(範囲外や解釈できない値は自動的に補正されます)",
            SceneViewState.MinNavigationSpeedMultiplier,
            SceneViewState.MaxNavigationSpeedMultiplier);
    }

    /// <summary>ユーザーがコントロールを編集した。ページ側がモデルへ取り込む。</summary>
    internal event Action?ValueEdited;

    /// <summary>モデルの値をコントロールへ反映する。</summary>
    internal void LoadFrom(SceneEditorOptionsModel Model){
        this.SuppressValueEdited=true;
        try {
            this.InvertLookXCheckBox.IsChecked=Model.InvertLookX;
            this.InvertLookYCheckBox.IsChecked=Model.InvertLookY;
            this.InvertOrbitXCheckBox.IsChecked=Model.InvertOrbitX;
            this.InvertOrbitYCheckBox.IsChecked=Model.InvertOrbitY;
            this.NavigationSpeedMultiplierTextBox.Text=
                SceneEditorOptionsModel.FormatNavigationSpeedMultiplier(Model.NavigationSpeedMultiplier);
        } finally {
            this.SuppressValueEdited=false;
        }
    }

    /// <summary>コントロールの値をモデルへ書き戻す(クランプはモデル側が行う)。</summary>
    internal void StoreTo(SceneEditorOptionsModel Model){
        Model.InvertLookX=this.InvertLookXCheckBox.IsChecked==true;
        Model.InvertLookY=this.InvertLookYCheckBox.IsChecked==true;
        Model.InvertOrbitX=this.InvertOrbitXCheckBox.IsChecked==true;
        Model.InvertOrbitY=this.InvertOrbitYCheckBox.IsChecked==true;
        Model.NavigationSpeedMultiplier=SceneEditorOptionsModel.ParseNavigationSpeedMultiplier(
            this.NavigationSpeedMultiplierTextBox.Text);
    }

    private void OnUserEditedValue(object Sender,RoutedEventArgs Args){
        if(this.SuppressValueEdited){
            return;
        }

        this.ValueEdited?.Invoke();
    }

    private void ResetToDefaultsButton_Click(object Sender,RoutedEventArgs Args){
        // 既定値を表示してモデルへも反映するが、永続化はしない(OKを押すまで確定させない)。
        SceneEditorOptionsModel Defaults=new();
        Defaults.ResetToDefaults();
        this.LoadFrom(Defaults);
        this.ValueEdited?.Invoke();
    }
}
