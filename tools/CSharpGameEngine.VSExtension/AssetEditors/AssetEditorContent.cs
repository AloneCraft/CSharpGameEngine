using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// アセットエディター 1 種類分の中身。共通の <see cref="AssetEditorShell"/> がツールバー・中央・Details 欄・ステータスの枠を持ち、
/// 各エディターはここで種類固有の部分だけを差し込む（UE5 のアセットエディターの「ツールバー／ビューポート／Details」構成）。
/// </summary>
internal abstract class AssetEditorContent(string FilePath):IDisposable {
    private string CurrentPath=FilePath;
    internal string FilePath=>this.CurrentPath;
    internal string CurrentStatus { get;private set; }=string.Empty;
    /// <summary>中央の表示（ビューポート、画像、波形、フォーム等）。</summary>
    internal abstract UIElement Center { get;}
    /// <summary>右側の Details 欄。</summary>
    internal virtual UIElement?Details=>null;
    /// <summary>共通ボタンの右に並べる、種類固有のツールバー項目。</summary>
    internal virtual IReadOnlyList<UIElement>ToolbarItems=>Array.Empty<UIElement>();
    /// <summary>ステータスバーに表示する文言。</summary>
    internal event Action<string>?StatusChanged;
    /// <summary>描画面が押された等で、シェルが WPF のキーボードフォーカスを取る必要がある。</summary>
    internal event Action?FocusRequested;
    /// <summary>ステータスバーの進行バー（0..1）。</summary>
    internal event Action<float>?ProgressChanged;
    protected void SetStatus(string Text){
        this.CurrentStatus=Text;
        this.StatusChanged?.Invoke(Text);
    }
    protected void SetProgress(float Fraction)=>this.ProgressChanged?.Invoke(Fraction);
    protected void RequestFocus()=>this.FocusRequested?.Invoke();
    /// <summary>シェルの <c>PreviewKeyDown</c> から呼ぶ。処理したら true。</summary>
    internal virtual bool HandleKeyDown(Key Key,ModifierKeys Modifiers)=>false;
    /// <summary>Ctrl+S／Z／Y。VS が WPF の KeyDown より先に消費する場合のため、ペインの <c>PreProcessMessage</c> からも呼ぶ。</summary>
    internal virtual bool HandleNativeShortcut(Key Key)=>false;
    /// <summary>VS が名前変更・再読み込みで DocData を別のパスに向けた。</summary>
    internal void Reload(string FilePath){
        this.CurrentPath=FilePath;
        this.OnReload();
    }
    protected virtual void OnReload(){
    }
    public virtual void Dispose(){
    }
}
