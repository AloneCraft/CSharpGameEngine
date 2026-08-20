using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-8: 再解析デバウンスの<b>状態遷移</b>を固定する。
///
/// <para>
/// <b>なぜ遷移列のテストなのか</b>: Step 1-6は待ち時間の計算(<c>ResolveDelay</c>)だけを
/// 純粋関数にし、状態(保留窓がいつ始まったか)の管理を呼び出し側へ残した。その呼び出し側が
/// 窓を<b>解析の完走時にしかクリアしていなかった</b>ため、解析が一度も完走しないまま上限を超えると
/// 以後ずっと待ち時間0になり、「即発火 → 次の変更で即キャンセル」を繰り返して
/// デバウンスが完全に無効化された。実機では右クリックメニューが「シーンを解析中」で
/// グレーアウトしたまま・Hierarchyも空になり、<b>VSを再起動するまで直らなかった</b>。
/// </para>
///
/// <para>
/// <c>ResolveDelay</c>の単発の入出力テストは当時すべて通っていた。この型の不具合は
/// <b>遷移列をシミュレートしないと捕まらない</b>。それが
/// <see cref="Debounce_DoesNotDegenerateToZeroWhenEveryAnalysisIsSuperseded"/>。
/// </para>
/// </summary>
public sealed class SceneAnalysisSchedulingPolicyTests {
    // ---- 状態遷移 ----

    [Fact]
    public void OnScheduled_StartsANewWindowWhenNothingIsPending(){
        var Now=TimeSpan.FromSeconds(5);

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                                                            SceneAnalysisDebouncePolicy.Idle,Now);

        Assert.Equal(Now,Outcome.State.PendingSince);
        Assert.Equal(SceneAnalysisDebouncePolicy.Delay,Outcome.Delay);
    }

    /// <summary>
    /// 張り直しても窓の開始時刻は保つ。ここを更新すると上限が永久に効かず、
    /// 無関係なworkspace変更が続く間いつまでも解析が始まらない(Step 1-6が直した元の不具合)。
    /// </summary>
    [Fact]
    public void OnScheduled_KeepsTheOriginalWindowWhileRearming(){
        var Start=TimeSpan.FromSeconds(5);
        var State=
            SceneAnalysisDebouncePolicy.OnScheduled(SceneAnalysisDebouncePolicy.Idle,Start).State;

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                                                            State,Start+TimeSpan.FromMilliseconds(100));

        Assert.Equal(Start,Outcome.State.PendingSince);
    }

    /// <summary>上限に達したら即発火する。ただし窓はまだ閉じない(タイマは武装中)。</summary>
    [Fact]
    public void OnScheduled_FiresImmediatelyAtTheCapButKeepsTheWindowArmed(){
        var Start=TimeSpan.FromSeconds(5);
        var State=
            SceneAnalysisDebouncePolicy.OnScheduled(SceneAnalysisDebouncePolicy.Idle,Start).State;

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                                                            State,Start+SceneAnalysisDebouncePolicy.MaxDelay);

        Assert.Equal(TimeSpan.Zero,Outcome.Delay);
        Assert.NotNull(Outcome.State.PendingSince);
    }

    [Fact]
    public void OnScheduled_NeverWaitsPastTheCap(){
        var Start=TimeSpan.FromSeconds(5);
        var State=
            SceneAnalysisDebouncePolicy.OnScheduled(SceneAnalysisDebouncePolicy.Idle,Start).State;
        var Now=Start+SceneAnalysisDebouncePolicy.MaxDelay-TimeSpan.FromMilliseconds(100);

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(State,Now);

        Assert.Equal(TimeSpan.FromMilliseconds(100),Outcome.Delay);
        Assert.True(Now-Start+Outcome.Delay<=SceneAnalysisDebouncePolicy.MaxDelay);
    }

    /// <summary>Scene Designer自身の書き込み直後はまとめる理由が無いので待たない。</summary>
    [Fact]
    public void OnScheduled_ImmediateReturnsZeroAndStillArmsTheWindow(){
        var Now=TimeSpan.FromSeconds(5);

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                                                            SceneAnalysisDebouncePolicy.Idle,Now,Immediate:true);

        Assert.Equal(TimeSpan.Zero,Outcome.Delay);
        Assert.Equal(Now,Outcome.State.PendingSince);
    }

    [Fact]
    public void OnScheduled_ImmediateDoesNotResetAnExistingWindow(){
        var Start=TimeSpan.FromSeconds(5);
        var State=
            SceneAnalysisDebouncePolicy.OnScheduled(SceneAnalysisDebouncePolicy.Idle,Start).State;

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                                                            State,Start+TimeSpan.FromMilliseconds(100),Immediate:true);

        Assert.Equal(Start,Outcome.State.PendingSince);
    }

    /// <summary>窓を閉じるのは解析の<b>開始</b>だけ(完走ではない)。</summary>
    [Fact]
    public void OnStarted_ClosesTheWindow(){
        var State=SceneAnalysisDebouncePolicy
                 .OnScheduled(SceneAnalysisDebouncePolicy.Idle,TimeSpan.FromSeconds(5)).State;

        Assert.Null(SceneAnalysisDebouncePolicy.OnStarted(State).PendingSince);
    }

    [Fact]
    public void OnStarted_IsIdempotent(){
        var Once=SceneAnalysisDebouncePolicy.OnStarted(
                                                       SceneAnalysisDebouncePolicy.Idle);

        Assert.Null(SceneAnalysisDebouncePolicy.OnStarted(Once).PendingSince);
    }

    /// <summary>単調でない時計(経過が負)でも上限判定が壊れないこと。</summary>
    [Fact]
    public void OnScheduled_ClampsANonMonotonicClockToZeroElapsed(){
        var State=SceneAnalysisDebouncePolicy
                 .OnScheduled(SceneAnalysisDebouncePolicy.Idle,TimeSpan.FromSeconds(10)).State;

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                                                            State,TimeSpan.FromSeconds(1));

        Assert.Equal(SceneAnalysisDebouncePolicy.Delay,Outcome.Delay);
    }

    // ---- 本命: ライブロックの否定 ----

    /// <summary>
    /// <b>今回の実機不具合を確実に落とす1本。</b>
    ///
    /// <para>
    /// 100ms間隔のworkspace変更が10秒続き、待ち時間0が返るたびにタイマが即発火する
    /// (=<c>OnStarted</c>が適用される)が、解析は毎回次の変更に追い越されて完走しない、
    /// という状況をシミュレートする。
    /// </para>
    ///
    /// <para>
    /// 正しい意味論では「即発火は<b>上限ごとに最大1回</b>」なので、10秒なら
    /// <c>10秒 / 750ms + 1 = 14回</c>以下に収まる。
    /// 壊れた意味論(完走時にしか窓を閉じない)では窓が永久に開いたままになり、
    /// <b>ほぼ毎回</b>0が返る = デバウンスの消滅。その差をここで固定する。
    /// </para>
    /// </summary>
    [Fact]
    public void Debounce_DoesNotDegenerateToZeroWhenEveryAnalysisIsSuperseded(){
        var Interval=TimeSpan.FromMilliseconds(100);
        var Total=TimeSpan.FromSeconds(10);
        var State=SceneAnalysisDebouncePolicy.Idle;
        var ImmediateFireCount=0;
        var Steps=0;

        for(var Now=TimeSpan.Zero;Now<Total;Now+=Interval){
            Steps++;
            var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(State,Now);
            State=Outcome.State;
            if(Outcome.Delay!=TimeSpan.Zero){
                continue;
            }

            // 待ち時間0 = タイマが即発火し、解析が始まる。完走はしない(次の変更が追い越す)。
            ImmediateFireCount++;
            State=SceneAnalysisDebouncePolicy.OnStarted(State);
        }

        var Allowed=(int)(Total.TotalMilliseconds/SceneAnalysisDebouncePolicy.MaxDelay.TotalMilliseconds)+1;
        Assert.Equal(100,Steps);
        Assert.True(
            ImmediateFireCount<=Allowed,
            $"即発火が{ImmediateFireCount}回/{Steps}回。上限ごとに1回({Allowed}回以下)に収まっていない "
            +"= デバウンスが消滅している(実機で「シーンが開けなくなる」状態)。");
    }

    /// <summary>
    /// 逆向きの担保。解析が始まりさえすれば窓は閉じるので、静穏区間では通常の待ち時間へ戻る。
    /// </summary>
    [Fact]
    public void Debounce_ReturnsToTheNormalDelayAfterAnAnalysisStarts(){
        var Start=TimeSpan.FromSeconds(5);
        var State=
            SceneAnalysisDebouncePolicy.OnScheduled(SceneAnalysisDebouncePolicy.Idle,Start).State;
        State=SceneAnalysisDebouncePolicy.OnScheduled(
            State,Start+SceneAnalysisDebouncePolicy.MaxDelay).State;
        State=SceneAnalysisDebouncePolicy.OnStarted(State);

        var Outcome=SceneAnalysisDebouncePolicy.OnScheduled(
                                                            State,Start+SceneAnalysisDebouncePolicy.MaxDelay+TimeSpan.FromMilliseconds(10));

        Assert.Equal(SceneAnalysisDebouncePolicy.Delay,Outcome.Delay);
    }

    // ---- 保留中のメニュー状態 ----

    [Fact]
    public void PendingMenuState_IsUnavailableWithoutAnActiveDocument(){
        Assert.Equal(
            SceneDesignerTargetState.Unavailable,SceneAnalysisDebouncePolicy.PendingMenuState(null));
    }

    [Fact]
    public void PendingMenuState_IsAnalyzingWithAnActiveDocument(){
        Assert.Equal(
            SceneDesignerTargetState.Analyzing,
            SceneAnalysisDebouncePolicy.PendingMenuState(@"C:\Game\MainScene.cs"));
    }

    // ---- ResolveDelay の意味が変わっていないこと(Step 1-6からの非退行) ----

    [Fact]
    public void ResolveDelay_KeepsItsOriginalMeaning(){
        Assert.Equal(SceneAnalysisDebouncePolicy.Delay,SceneAnalysisDebouncePolicy.ResolveDelay(TimeSpan.Zero));
        Assert.Equal(
            TimeSpan.Zero,SceneAnalysisDebouncePolicy.ResolveDelay(SceneAnalysisDebouncePolicy.MaxDelay));
        Assert.Equal(TimeSpan.Zero,SceneAnalysisDebouncePolicy.ResolveDelay(TimeSpan.Zero,Immediate:true));
        Assert.True(SceneAnalysisDebouncePolicy.MaxDelay>SceneAnalysisDebouncePolicy.Delay);
    }
}
