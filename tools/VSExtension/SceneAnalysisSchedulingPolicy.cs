using System;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 再解析デバウンスの<b>状態</b>。
/// <para>
/// <b>不変条件: 「タイマが武装中 ⟺ <see cref="PendingSince"/>が非null」。</b>
/// この不変条件を破ると<see cref="SceneAnalysisDebouncePolicy"/>の上限判定が壊れる
/// (実機で「シーンが開けなくなる」に直結した。クラスのコメント参照)。
/// </para>
/// </summary>
internal readonly struct SceneAnalysisDebounceState {
    internal SceneAnalysisDebounceState(TimeSpan?PendingSince)=>this.PendingSince=PendingSince;

    /// <summary>今の保留窓が始まった時刻(単調時計基準)。武装していなければnull。</summary>
    internal TimeSpan?PendingSince { get; }
}

/// <summary>1回のスケジュール要求の結果。</summary>
internal readonly struct SceneAnalysisScheduleOutcome {
    internal SceneAnalysisScheduleOutcome(SceneAnalysisDebounceState State,TimeSpan Delay){
        this.State=State;
        this.Delay=Delay;
    }

    internal SceneAnalysisDebounceState State { get; }

    internal TimeSpan Delay { get; }
}

/// <summary>
/// M0051 Step 1-8: 再解析デバウンスの<b>状態遷移</b>を純粋関数として持つ。
///
/// <para>
/// <b>解決した不具合(実機で機能停止)</b>: Step 1-6は待ち時間の計算
/// (<see cref="ResolveDelay"/>)だけを純粋関数にし、状態(「保留窓がいつ始まったか」)の管理を
/// 呼び出し側へ残した。その呼び出し側が窓を<b>解析の完走時にしかクリアしていなかった</b>ため、
/// 解析が一度も完走しないまま上限を超えると<b>以後ずっと待ち時間0</b>になり、
/// 「即発火 → 次の変更で即キャンセル」を繰り返してデバウンスが完全に無効化された。
/// 結果、右クリックメニューが「シーンを解析中」でグレーアウトしたまま・Hierarchyも空になり、
/// VSを再起動するまで直らなかった。
/// </para>
///
/// <para>
/// <b>正しい意味論</b>: 上限が守る区間は「最初のスケジュール → タイマ発火」だけ。
/// 発火した瞬間その待ちは終わるので、<see cref="OnStarted"/>が窓を閉じる。
/// 追い越しは<i>新しい</i>窓の開始であり、その窓もまた上限で強制発火する。これで
/// 「上限ごとに1回は必ず解析が始まる」が<b>繰り返し</b>成立し、一度きりで壊れて0msへ縮退しない。
/// </para>
///
/// <para>
/// <b>時刻は単調時計(<c>Stopwatch.Elapsed</c>)を渡すこと。</b> 壁時計だとNTP/DST補正で
/// 経過が負になり上限判定が壊れる。念のためここでも負はゼロへクランプする。
/// </para>
/// </summary>
internal static class SceneAnalysisDebouncePolicy {
    /// <summary>連続する変更をまとめるための通常の待ち時間。</summary>
    internal static readonly TimeSpan Delay=TimeSpan.FromMilliseconds(250);

    /// <summary>ひとつの保留窓が、どれだけ延長されてもここで必ず発火する上限。</summary>
    internal static readonly TimeSpan MaxDelay=TimeSpan.FromMilliseconds(750);

    internal static readonly SceneAnalysisDebounceState Idle=new(PendingSince:null);

    /// <summary>
    /// スケジュール要求。窓が無ければ<paramref name="Now"/>で開き、あれば<b>開始時刻を保つ</b>
    /// (これがコアレスの実体 — 張り直しのたびに時刻を更新すると上限が永久に効かない)。
    /// <paramref name="Immediate"/>はScene Designer自身の書き込み直後など、待たせたくない場合。
    /// </summary>
    internal static SceneAnalysisScheduleOutcome OnScheduled(
        SceneAnalysisDebounceState State,TimeSpan Now,bool Immediate=false){
        var Since=State.PendingSince??Now;
        var Elapsed=Now-Since;
        if(Elapsed<TimeSpan.Zero){
            Elapsed=TimeSpan.Zero;
        }

        return new SceneAnalysisScheduleOutcome(
            new SceneAnalysisDebounceState(Since),ResolveDelay(Elapsed,Immediate));
    }

    /// <summary>
    /// タイマが発火して解析が<b>始まった</b>ときに呼ぶ。窓を閉じる唯一の場所。
    /// 解析が完走したかどうかとは無関係 — 完走を条件にすると、追い越され続けたときに
    /// 窓が永久に閉じずライブロックする(これが実機不具合の直接の原因だった)。
    /// </summary>
    internal static SceneAnalysisDebounceState OnStarted(SceneAnalysisDebounceState State)=>Idle;

    /// <summary>
    /// <paramref name="ElapsedSinceFirstPending"/>は今の保留窓が始まってからの経過時間。
    /// <see cref="OnScheduled"/>から使う。単体でも意味が変わっていないことをテストで担保する。
    /// </summary>
    internal static TimeSpan ResolveDelay(TimeSpan ElapsedSinceFirstPending,bool Immediate=false){
        if(Immediate||ElapsedSinceFirstPending>=MaxDelay){
            return TimeSpan.Zero;
        }

        var Remaining=MaxDelay-ElapsedSinceFirstPending;
        return Remaining<Delay?Remaining:Delay;
    }
}
