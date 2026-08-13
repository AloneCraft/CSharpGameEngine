using System;
using System.Collections.Generic;

namespace CSharpGameEngine.VSExtension;

internal enum SceneDesignerTargetState {
    Unavailable,
    Analyzing,
    Failed,
    OutsideType,
    NonSceneType,
    SceneType,
}

internal enum SceneDesignerMenuMode {
    Hidden,
    Disabled,
    Direct,
    Submenu,
}

internal readonly record struct SceneDesignerMenuItem(string Text,string FullyQualifiedName);

internal sealed class SceneDesignerCommandPresentation {
    internal SceneDesignerCommandPresentation(
        SceneDesignerMenuMode Mode,
        string Text,
        IReadOnlyList<SceneDesignerMenuItem> Items){
        this.Mode=Mode;
        this.Text=Text;
        this.Items=Items;
    }

    internal SceneDesignerMenuMode Mode { get; }

    internal string Text { get; }

    internal IReadOnlyList<SceneDesignerMenuItem> Items { get; }

    internal bool Visible=>this.Mode is not SceneDesignerMenuMode.Hidden;

    internal bool Enabled=>this.Mode is SceneDesignerMenuMode.Direct or SceneDesignerMenuMode.Submenu;
}

/// <summary>
/// VSのBeforeQueryStatusで使う純粋な表示ポリシー。
/// UIスレッドからRoslyn解析を開始・待機してはならないため、呼び出し元は解析済みの状態だけを渡す。
/// </summary>
internal static class SceneDesignerContextMenuPolicy {
    internal const string OpenText="シーン デザイナーで開く";

    internal static SceneDesignerCommandPresentation Create(
        string? FilePath,
        SceneDesignerTargetState TargetState,
        IReadOnlyList<SceneDesignerMenuItem> Items){
        if(FilePath is null||!FilePath.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)
            ||TargetState==SceneDesignerTargetState.Unavailable){
            return new(SceneDesignerMenuMode.Hidden,OpenText,[]);
        }

        return TargetState switch {
            SceneDesignerTargetState.Analyzing=>Disabled("シーンを解析中"),
            SceneDesignerTargetState.Failed=>Disabled("Scene 解析に失敗"),
            // Scene宣言の直前コメントやusing/namespace上からも開けるよう、同じファイルに
            // Sceneがあれば型外は選択メニューへ委譲する。通常型の内部は誤操作防止のため無効のまま。
            SceneDesignerTargetState.OutsideType when Items.Count>0=>Available(Items),
            SceneDesignerTargetState.OutsideType=>Disabled("このファイルには IScene 実装クラスがありません"),
            SceneDesignerTargetState.NonSceneType=>Disabled("このクラスは IScene を実装していません"),
            SceneDesignerTargetState.SceneType=>Available(Items),
            _=>Disabled("このファイルには IScene 実装クラスがありません"),
        };
    }

    private static SceneDesignerCommandPresentation Available(IReadOnlyList<SceneDesignerMenuItem> Items)=>
        Items.Count==1
            ?new(SceneDesignerMenuMode.Direct,OpenText,Items)
            :new(SceneDesignerMenuMode.Submenu,OpenText,Items);
    private static SceneDesignerCommandPresentation Disabled(string Reason)=>
        new(SceneDesignerMenuMode.Disabled,$"{OpenText}（{Reason}）",[]);
}
