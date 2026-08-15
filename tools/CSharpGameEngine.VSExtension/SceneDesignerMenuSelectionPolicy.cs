using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 複数 Scene を持つファイルのシーン一覧メニューで、項目ごとの強調表示を決めます。2つの状態を
/// 混同しないことがこのクラスの存在理由です。
///
/// <para>
/// <b>✓(<c>OLECMDF_LATCHED</c>)は「シーン デザイナーで今開いているシーン」だけに付けます</b>
/// (<see cref="IsLatched"/>)。VS のメニューで ✓ は「今その状態にある」ことを表す慣習であり、
/// 以前ここが右クリック位置を基準にしていたため、デザイナーが別ファイルのシーンを表示している
/// のに手元のシーンに ✓ が付く、という誤解を招く表示になっていた。
/// </para>
///
/// <para>
/// 右クリックしたキャレット位置のシーン(<see cref="IsAtCaret"/>)は、項目テキストへの
/// 「(カーソル位置)」の付記で示します(<see cref="SceneDesignerContextMenuPolicy.ItemText"/>)。
/// 太字にできれば済む話だが、VS のメニュー項目に任意の FontWeight を与える手段は公式 API に無い
/// (<c>OleMenuCommand</c> が公開するのは <c>Checked</c>/<c>Enabled</c>/<c>Visible</c>/<c>Text</c> のみで、
/// <c>OLECMDF</c> に太字を表すビットは無く、.vsct の CommandFlag にもフォント指定は無い)。
/// ✓ を開いているシーンへ譲った以上、キャレット位置は文字列で表すしかない。
/// </para>
/// </summary>
internal static class SceneDesignerMenuSelectionPolicy {
    /// <summary>✓ を付けるか。基準はシーン デザイナーが開いている(Pin している)シーン。</summary>
    internal static bool IsLatched(string?OpenSceneFullyQualifiedName,SceneDesignerMenuItem Item)=>
        Matches(OpenSceneFullyQualifiedName,Item);

    /// <summary>「(カーソル位置)」を付けるか。基準は右クリックしたキャレット位置のシーン。</summary>
    internal static bool IsAtCaret(string?TargetSceneFullyQualifiedName,SceneDesignerMenuItem Item)=>
        Matches(TargetSceneFullyQualifiedName,Item);

    private static bool Matches(string?FullyQualifiedName,SceneDesignerMenuItem Item)=>
        !string.IsNullOrEmpty(FullyQualifiedName)
        &&string.Equals(FullyQualifiedName,Item.FullyQualifiedName,StringComparison.Ordinal);
}
