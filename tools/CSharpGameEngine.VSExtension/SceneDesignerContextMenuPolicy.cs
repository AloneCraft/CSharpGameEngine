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

    /// <summary>
    /// 同じファイルに複数のSceneがあり、どれを開くか選ばせる状態。以前は入れ子のサブメニューで
    /// 見せていたが、<c>&lt;Menu&gt;</c>の可視性はQueryStatusに応答するコマンドを登録しないと制御
    /// できず、Scene1件のときも空のサブメニューが残って「シーン デザイナーで開く」が2つ並んで
    /// いたため、コンテキストメニュー直下の平らな動的項目へ変更した(.vsct参照)。
    /// </summary>
    SceneList,
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

    internal bool Enabled=>this.Mode is SceneDesignerMenuMode.Direct or SceneDesignerMenuMode.SceneList;
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
        // M0050: *.scene.jsonは1ファイル=1シーンなので、拡張子だけ対応させればJSON経路にも
        // TargetState/Itemsの分岐(下のswitch)がそのまま使える(AnalyzeJsonAsyncが
        // 常にSceneType+Items.Count==1を返すため、Directモードへ自然に収束する)。
        if(FilePath is null
            ||(!FilePath.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)
                &&!FilePath.EndsWith(".scene.json",StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// <see cref="SceneDesignerMenuMode.SceneList"/>の動的項目1件分の表示テキスト。サブメニューだった
    /// 頃は親項目の「シーン デザイナーで開く」が文脈を与えていたためクラス名だけで足りたが、
    /// コンテキストメニュー直下に並ぶようになったので、項目単体で何をするか読めるようにする。
    ///
    /// <para>
    /// <paramref name="IsAtCaret"/>が真のとき「(カーソル位置)」を付記する。✓は
    /// 「デザイナーで開いているシーン」専用なので(<see cref="SceneDesignerMenuSelectionPolicy"/>)、
    /// キャレット位置はテキストで示すしかない。両方成立する項目では ✓ と付記が同時に出る。
    /// </para>
    /// </summary>
    internal static string ItemText(SceneDesignerMenuItem Item,bool IsAtCaret)=>
        IsAtCaret?$"{Item.Text} を{OpenText}（カーソル位置）":$"{Item.Text} を{OpenText}";

    /// <summary>
    /// シーン一覧のスロット(.vsctに固定IDで並べたButton)を表示するか。<see cref="SceneDesignerMenuMode.SceneList"/>
    /// のときだけ、実際のシーン件数の分だけ表示する。スロット数は有限なので、件数がそれを超えた分は
    /// <see cref="HiddenItemCount"/> として溢れ表示で伝える(黙って消さない)。
    /// </summary>
    internal static bool IsItemSlotVisible(SceneDesignerMenuMode Mode,int SlotIndex,int ItemCount)=>
        Mode==SceneDesignerMenuMode.SceneList&&SlotIndex>=0&&SlotIndex<ItemCount;

    /// <summary>スロットに入りきらなかった件数。0なら溢れ表示は出さない。</summary>
    internal static int HiddenItemCount(int ItemCount,int SlotCount)=>
        ItemCount>SlotCount?ItemCount-SlotCount:0;

    /// <summary>
    /// 溢れ表示の本文。押せない情報行であり、全件を列挙できる唯一の経路(シーン デザイナーの
    /// コンボボックス)へ誘導する。
    /// </summary>
    internal static string OverflowText(int HiddenCount)=>
        $"他 {HiddenCount} 件はシーン デザイナーのコンボボックスから選択してください";

    private static SceneDesignerCommandPresentation Available(IReadOnlyList<SceneDesignerMenuItem> Items)=>
        Items.Count==1
            ?new(SceneDesignerMenuMode.Direct,OpenText,Items)
            :new(SceneDesignerMenuMode.SceneList,OpenText,Items);
    private static SceneDesignerCommandPresentation Disabled(string Reason)=>
        new(SceneDesignerMenuMode.Disabled,$"{OpenText}（{Reason}）",[]);
}
