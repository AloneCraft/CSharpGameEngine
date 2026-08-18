namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0043: <see cref="SceneSourceMarkerService.Update"/>がコードエディタのスクロール位置へ
/// 触れてよいかどうかを、<b>呼び出し側が明示する</b>ための語彙。
/// </summary>
internal enum SceneSourceMarkerScrollBehavior {
    /// <summary>ユーザーが選択を変えた。マーク先頭をビュー最上部へスクロールしてよい。</summary>
    ScrollToSelection,

    /// <summary>
    /// 再解析でマーク位置を貼り直すだけ。スクロール位置には一切触れない。
    /// 編集→書き戻し→再解析の経路は必ずこちらを通す。
    /// </summary>
    PreserveScroll,
}

/// <summary>
/// M0043: スクロールするかどうかの判定。
///
/// <para>
/// 以前は<see cref="SceneSourceMarkerService"/>内部で「選択が変わったか」を
/// <c>BufferChanged||!LastScrolledPaths.SequenceEqual(Paths)</c>から<b>推測</b>していたが、
/// <c>BufferChanged</c>は<c>MarkedBuffer</c>が<c>null</c>のときも必ずtrueになり、
/// <c>Clear()</c>は編集サイクル中の過渡状態(解析エラー・文書未オープン・pin不一致)で普通に走る。
/// その結果「編集中に一度でも過渡状態を経由すると、次の正常な再解析が選択変更と誤認して
/// スクロールする」という、選択の変化とは無関係な飛び方をしていた(実機で飛び先が毎回違った)。
/// </para>
///
/// <para>
/// 推測をやめ、呼び出し側の意図(<see cref="SceneSourceMarkerScrollBehavior"/>)だけで決める。
/// これにより過渡状態が何であっても再解析経路からスクロールが起きえなくなる。
/// </para>
/// </summary>
internal static class SceneSourceMarkerScrollPolicy {
    internal static bool ShouldScroll(SceneSourceMarkerScrollBehavior Behavior,int MarkerRangeCount)=>
        Behavior==SceneSourceMarkerScrollBehavior.ScrollToSelection&&MarkerRangeCount>0;
}
