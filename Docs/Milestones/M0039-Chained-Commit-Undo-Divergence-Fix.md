# M0039: 予測コミット失敗時にUndo履歴が分岐する不具合の修正

## 目的

[[M0038-Edit-Latency-Prediction-Layer]]出荷直後、実機で「Undoが直前の1回しかできない」と報告された。
本マイルストーンはその根本原因を特定し修正する。

## 根本原因

M0038は「編集後モデルをローカルに予測配信し、ソース書き戻しを待たずに次の操作を受け付ける」
高速化を実装した。これが2つの構造的な欠陥を持ち込んでいた。

### 原因1: 予測が「実際の解析版数」の連番を消費していた

`SceneEditorSessionService.TryPublishPrediction`は、予測snapshotの`DocumentVersion`/
`AnalysisGeneration`の両方を同じ共有カウンタ`this.Generation`から払い出していた。しかし
`DocumentVersion`は`SceneEditConflictMerger.Merge`が「ドラッグ開始時から実際のソースが変化したか」を
判定する楽観的並行性制御の版数であり、まだソースへ書き込まれていない予測がこの連番を進める理由は
無い。これにより、1回でも予測が発行された後はほぼ必ずrebase経路に落ちるようになっていた
(以前は本当に何も変わっていなければfast path、変わっていればrebaseという設計だったのに、予測自体が
「何か変わった」と誤認させていた)。

### 原因2: ローカル完了とソース書き戻し成功の対応関係が保証されていなかった(本命)

M0038により、前の編集のソース書き戻しがまだ背後で飛行中でも次の編集を開始できるようになった。
`SceneHwndHost.CompleteEditCommit`はローカル完了のたびに`RecentTransformCommits`(M0036の先読み
Undoスタック)へ無条件でpushしていたが、実際のソース書き戻しが成功したかどうかは一切確認して
いなかった。

書き戻しは直列(`SceneDesignerToolWindowControl.PendingCommitWrite`)でチェーンされていたが、
**チェーン中のどれか1つが失敗すると、それより後ろの書き込みは全て「実際には起きていない編集」の
上に積み上がっていた**:

1. 編集A→B→Cを連続で行う。3つとも予測でローカル完了し、`RecentTransformCommits`に3件push。
2. 書き込みはA→B→Cの順で直列実行。Bの書き込みが(原因1のせいで常態化していたrebase経路上の
   不整合を含む何らかの理由で)失敗する。
3. 失敗はステータスバーへ出るだけで、ローカルの予測・`RecentTransformCommits`のpush・
   以降のチェーンは一切ロールバックされなかった。
4. Cの書き込みは「Bも本当に反映された」という前提でRoslynへ再解決した最新ソースを基準に
   rebaseする。しかしBは実際には反映されていないため、`Merged = Current(A止まり) +
   (C.Final − C.Original(予測A+B基準))`という式でBの差分がそのまま失われた誤った値になる。
5. 結果として、実際にVSの`ITextUndoHistory`へ積まれるトランザクションはAだけになりうる。
   `RecentTransformCommits`は3件持ったままのため、`Session.TryUndo()`(実ソースUndo)は1回で
   `History.CanUndo`が尽き、2回目以降は`Applied=false`となって`Host.TryFastUndo()`にすら
   到達しなかった。

表示上は3回分編集したように見えても、ソースに実際に書き込まれた回数がそれより少ないため、
Undo可能回数もそれに一致してしまっていた — これが「Undoは直前の1回しかできない」の実体。

## 実装したこと

### 1. 予測は「実際の解析版数」を消費しない

`SceneEditorSessionService.TryPublishPrediction`が発行する予測snapshotの`DocumentVersion`は
**元のsnapshotのDocumentVersionをそのまま引き継ぎ**(進めない)、`AnalysisGeneration`だけ
引き続き`++this.Generation`で新しい値を払い出すよう変更した。これにより、何も他の書き込みが
割り込んでいなければ`SceneEditConflictMerger.Merge`は正しくfast pathを通り、rebase経路は
「本当に前の編集が割り込んだとき」だけに限定される — 元の設計意図に戻した。

### 2. 書き戻し失敗を検知したらチェーンの残りを中断し、ソースの真実へ強制同期する

`SceneDesignerToolWindowControl`の書き戻しチェーンを`Task`から`Task<bool>`(健全性フラグ)へ
変更した。`CommitEditAsync`は開始時に前段の健全性を受け取り、**前段が既に不健全なら自分の
書き込みは試みず失敗として後続へ連鎖伝播する**(原因2の「欠落したまま積み上がる」を断つ)。

チェーン中で最初に不健全化した箇所だけ、新設した
`SceneEditorSessionService.RefreshPinnedSnapshotFromSourceAsync`を呼ぶ。これはRoslynから
対象Sceneを予測を経由せず直接再解決し、`PinnedSnapshot`を実際のソース内容へ強制的に
publishし直す。合わせて新設した`SceneHwndHost.ResyncAfterCommitFailure()`で
`RecentTransformCommits`/`RecentTransformUndone`を丸ごとClearする — 既存の「Property編集・
複製・別Scene切替時は先読みUndoキャッシュを丸ごと破棄する」という確立済みのidiomを再利用した。

### 既知の残存する軽微なケース

不健全化したチェーンの後ろに、予測非対応の編集(Inspectorの複雑な式、構造編集など)が
たまたま連なっていた場合、その編集のローカルキャンセル処理(`CancelEditTransaction`)が
自身のドラッグ開始時点の(既に古い)基準値を、直前に強制同期した真実の上へ上書きしうる。
これは表示のみの一時的なズレで、次のRoslyn再解析(既存の250msデバウンス経由)で自己修復する
(M0036で既に文書化されている「Hierarchy編集を挟んだ直後のUndoの自己修復」と同種の既知の
軽微な限界として扱う)。

## Evidence

`SceneEditorSessionService`は`SceneWorkspaceService`(Visual Studio Workspace/RDT/Editor Adapters
サービス依存)を必須の依存として構築されるため、実インスタンスを使うユニットテストは既存の
`SceneTextBufferWriter`/`SceneWorkspaceService`と同様に対象外(このテストプロジェクトに
VS SDKサービスのモック基盤が無い)。`SceneDesignerToolWindowControl`のチェーン中断伝播も
UIコントロール内のprivateな配線でユニットテスト対象外。ソリューション全体1037/1037
(既存1037、テスト数増減なし、リグレッションなし)。実際にUndoが編集回数分だけ正しく遡れるか、
連続編集後もソースと表示が一致するかは実機確認が必須。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | 連続してTransform編集を行い、Ctrl+Zを**編集回数分**連続で押すと、その回数分だけ正しく遡って元に戻る(今回の主目的) |
| 2 | 通常速度の単発編集での体感速度がM0038から変わっていない(fast pathへ戻ったことの確認) |
| 3 | 短時間に大量の編集を連打しても、最終的に画面とC#ソースの内容が一致する |
| 4 | コードエディタ側の通常のCtrl+Zに回帰が無い |
