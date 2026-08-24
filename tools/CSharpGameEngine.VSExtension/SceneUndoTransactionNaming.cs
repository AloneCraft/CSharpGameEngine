using System.Globalization;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-5: Scene Designerが積むVSテキストUndoトランザクションの表示名。
/// VSのUndo/Redoドロップダウンにこの形で並ぶため、どの操作がデザイナー由来かを人が見て判別できる。
///
/// <para>
/// <b>所有判定には使えない(実機で判明)</b>。当初はこの接頭辞を
/// <c>ITextUndoHistory.LastUndoTransaction.Description</c>から読み戻して「自分が積んだものか」を
/// 判定する設計だったが、VSの実装(<c>VsUndoHistory</c>)はスタックを覗く系のメンバーを
/// 実装しておらず<see cref="System.NotSupportedException"/>を投げる。
/// 所有判定は<see cref="SceneTextUndoLedgerPolicy"/>(自分が積んだ単位数＋バッファ版番号)が担う。
/// </para>
/// </summary>
internal static class SceneUndoTransactionNaming {
    /// <summary><paramref name="Label"/>は<see cref="SceneUndoEntry.Label"/>(例: "Rename node")。</summary>
    internal static string Format(string Label)=>Format(VsixLocalization.UICulture,Label);

    internal static string Format(CultureInfo Culture,string Label)=>
        VsixText.Format(Culture,"SceneUndoTransaction",Label);
}
