using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>プレビューレベルの選択状態を VS のユーザー設定ストア（<see cref="SceneEditorSettings"/> と同じコレクション）へ保存する。UI スレッド専用。</summary>
internal sealed class VsPreviewLevelSettings:IPreviewLevelSettings {
    public string?Read(string Name){
        ThreadHelper.ThrowIfNotOnUIThread();
        return SceneEditorSettings.ReadText(Name);
    }
    public void Write(string Name,string?Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneEditorSettings.WriteText(Name,Value);
    }
}
