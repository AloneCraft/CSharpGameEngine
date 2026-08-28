# M0059 — ECS Baseline固定とFixed-Tick Shipping Loop

## 目的

既存の静的ECS挙動を`ecs.baseline`として実行可能なStageへ固定し、生成されたshipping loopを60 Hz・host frameあたり最大8 tickのfixed-tick方式へ移行する。Physics Solver、Cooking、Bake Host、VSIX Physics UIは対象外とする。

## 実装

- `F-L7-030`を`Implemented/TestVerified`へ変更し、`ecs.baseline`でEntitiesテストを先に、CodeAnalysisテストを後に実行する。
- 閉集合Archetype、Scene生成と決定的lifecycle、system-major順序、phase barrierでの遅延構造変更を既存テストとStage Oracleで固定する。
- `FixedTickScheduler.Advance`は実行tick数、clamp前のattempted tick数、clamp遷移、backlog、補間率、`RetainBacklog`方針を返す。Schedulerはtick番号を所有せず、`State.FixedTick`を正本とする。
- `State`へRunning/Paused、補間率、Pause/Resume/Step APIを追加する。Pause中は経過時間を蓄積せず、Stepは1 host frameにつき1 tick消費する。
- 生成`RunFrame`はScheduler、elapsed counter、counter frequencyを受け取り、FrameInput・Spatial refresh・Presentationを各1回、simulation phaseを0〜8回実行する。Stopwatchは生成`Run`に限定する。
- Editor Previewは明示的なtick呼び出しを継続し、shipping loopと同じsimulation-tick helperでtick番号を進める。

## 検証

- Scheduler単体で0/1/余り/最大8/backlog/Pause/Step/補間/overrunを検証する。
- Generator E2Eでhost frame境界、phase順、tick番号、Pause/Step、8 tick capを検証する。
- `ecs.baseline`と`ecs.fixed-tick`をDebug/ReleaseでFeature Runnerから実行する。
- UserEcsProjectをDebug/Releaseでビルドし、全SolutionのDebug/Releaseテストを共有Analyzer競合を避けて順次実行する。

## 次マイルストーン

次はF-L5-003以降のPhysics Solver、Cooking/Cache、Bake Host、VSIX Physics UIを個別Stageとして実装する。M0059ではこれらの挙動を実装済みとは扱わない。
