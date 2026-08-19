namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-5: Scene Designerが積んだVSテキストUndoトランザクションの説明文の規約。
///
/// <para>
/// <b>解決する問題</b>: 保存後のCtrl+Zは<see cref="SceneTextUndoDelegationPolicy"/>により
/// テキストUndoへ委譲される。しかしテキストUndo履歴には<b>ユーザーがコードエディタで手打ちした編集</b>も
/// 同じスタックに積まれている。区別せずに委譲すると、Ctrl+Zを押し続けたときデザイナー由来でない編集まで
/// 巻き戻してしまう。説明文へ接頭辞を付けておき、<b>自分が積んだトランザクションのときだけ委譲する</b>。
/// </para>
///
/// <para>
/// 接頭辞は偽陽性を避けるため十分に特徴的なものにする。ユーザーが自分の編集にたまたま同じ説明文を
/// 付けることは、VSのテキスト編集では説明文を指定できないため実質起こらない。
/// </para>
/// </summary>
internal static class SceneUndoTransactionNaming {
    /// <summary>説明文の接頭辞。VSのUndoドロップダウンにもこの形で表示される。</summary>
    internal const string Prefix="Scene デザイナー: ";

    /// <summary><paramref name="Label"/>は<see cref="SceneUndoEntry.Label"/>(例: "Rename node")。</summary>
    internal static string Format(string Label)=>Prefix+Label;

    /// <summary>
    /// <paramref name="Description"/>は<c>ITextUndoHistory.LastUndoTransaction?.Description</c>。
    /// 履歴が空ならnullが渡る。
    /// </summary>
    internal static bool IsSceneTransaction(string?Description)=>
        Description is { }Value&&Value.StartsWith(Prefix,System.StringComparison.Ordinal);
}
