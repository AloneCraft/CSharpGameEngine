using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-6: Roslyn再解析の対象文書を決める純粋な判定。
///
/// <para>
/// <b>解決する不具合</b>: 再解析は<c>ActiveDocumentId</c>(コードエディタでアクティブな文書)
/// ひとつだけを対象にしていた。Scene Designerが開いていても、アクティブ文書が別ファイルなら
/// pin中Sceneの<c>.cs</c>は解析されない。その状態でScene Designer側がテキストを書き換えると
/// (保存の書き戻し、委譲Undo/Redo)、<b>再解析イベントが一切来ず表示が永久に追いつかない</b> —
/// 遅いのではなく更新されない。
/// </para>
///
/// <para>
/// アクティブ文書の解析は従来どおり必要(コードウィンドウの右クリックメニューが
/// <c>SceneDocumentMenuSnapshot</c>を見るため)。pin中Sceneはそれに<b>追加</b>で解析する。
/// メニュー用スナップショットはアクティブ文書のものだけを使い、pin側の解析結果はモデル一覧へ
/// 足すだけにする。
/// </para>
/// </summary>
internal static class SceneAnalysisTargetPolicy {
    /// <summary>
    /// アクティブ文書とは別に、pin中Sceneの<c>.cs</c>も解析する必要があるか。
    /// 同じファイルなら二重解析しない。パス比較はWindowsのファイルシステムに合わせて大文字小文字を無視する。
    /// </summary>
    internal static bool ShouldAlsoAnalyzePinnedScene(string?ActiveDocumentPath,string?PinnedSceneFilePath){
        if(string.IsNullOrEmpty(PinnedSceneFilePath)){
            return false;
        }

        return !string.Equals(ActiveDocumentPath,PinnedSceneFilePath,StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// M0051 Step 1-6: 再解析デバウンスの待ち時間を決める純粋な判定。
///
/// <para>
/// <b>解決する不具合</b>: <c>ScheduleAnalysis</c>は呼ばれるたびに保留中のタイマを取り消して
/// 250msで張り直していた。VSの背景処理は対象Sceneと無関係な理由でも頻繁にworkspace変更を起こすため、
/// 変更が続く間<b>デバウンスが際限なく延び続け</b>、いつまでも解析が始まらないことがある。
/// 最初に保留になった時点からの上限を設け、それを超えたら張り直さずに発火させる。
/// </para>
/// </summary>
internal static class SceneAnalysisDebouncePolicy {
    /// <summary>連続する変更をまとめるための通常の待ち時間。</summary>
    internal static readonly TimeSpan Delay=TimeSpan.FromMilliseconds(250);

    /// <summary>最初に保留になってから、どれだけ延長されてもここで必ず発火させる上限。</summary>
    internal static readonly TimeSpan MaxDelay=TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// <paramref name="ElapsedSinceFirstPending"/>は最初に保留になってからの経過時間
    /// (保留が無い状態からの新規スケジュールなら<see cref="TimeSpan.Zero"/>)。
    /// <paramref name="Immediate"/>はScene Designer自身の書き込み直後など、デバウンスを挟まずに
    /// 解析させたい場合。
    /// </summary>
    internal static TimeSpan ResolveDelay(TimeSpan ElapsedSinceFirstPending,bool Immediate=false){
        if(Immediate||ElapsedSinceFirstPending>=MaxDelay){
            return TimeSpan.Zero;
        }

        TimeSpan Remaining=MaxDelay-ElapsedSinceFirstPending;
        return Remaining<Delay?Remaining:Delay;
    }
}
