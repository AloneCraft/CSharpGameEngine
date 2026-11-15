using System.Globalization;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designerが積むVSテキストUndoトランザクションの表示名。
/// VSのUndo/Redoドロップダウンにこの形で並ぶため、どの操作がデザイナー由来かを人が見て判別できる。
/// Undo/Redoの所有権は常にVisual Studioのnative text historyにあり、Sessionは独自履歴を持たない。
/// </summary>
internal static class SceneUndoTransactionNaming {
    /// <summary><paramref name="Label"/>はgestureの表示名(例: "Rename node")。</summary>
    internal static string Format(string Label)=>Format(VsixLocalization.UICulture,Label);

    internal static string Format(CultureInfo Culture,string Label)=>
        VsixText.Format(Culture,"SceneUndoTransaction",Label);
}
