# M0097 — Physics パネルを分かりやすくする（シミュレート操作とランタイム診断の廃止、VS の診断ツールの呼び出し）
- 日付: 2026-10-01
- 状態: 進行中

## 目的
ユーザーが Scene Designer の Physics パネルを見て「意味が分からない。わかりやすくして」と依頼した。調べて分かった問題を、ユーザーの判断に沿って直す。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| A | シミュレート / 一時停止 / ステップ / リセットの削除と、編集中は物理を進めない | 完了（本コミット） |
| B | ランタイム診断を機能ごと廃止（F-L8-027 / F-L8-030） | 未着手 |
| C | パネルの構成と文言 | 未着手 |
| D | VS のデバッガーとプロファイラーを、拡張機能メニューとツールバーから呼び出す。ツールバーにオプションも追加 | 未着手 |

## A. シミュレート操作の削除と、編集中は物理を進めない
- 問題（コードで確認）:
  - 4 つのボタンは、裏で 15Hz で回る編集用ワールドの物理を操作していた（`PhysicsSimulationControl`、物理デバッグのパイプのコマンド）。
  - 編集用ワールドは開いた直後から再生中で、動く物体は落ちていく。
  - 画面に描くのは別のワールドなので、画面の物体は動かず、デバッグ表示だけがずれていった。
- ユーザーの判断: 操作は削除。編集中の物理は進めない。
- 変更:
  - `PhysicsStep` は `EngineExecutionMode.Runtime` のときだけ物理を進める。編集用ワールド（`EditorPreview`）は物体を登録するだけなので、デバッグ表示が画面の物体と一致する。
  - `PhysicsSimulationControl` を削除した。物理デバッグのパイプは snapshot だけに答え、他のコマンドには `unsupported command` を返す。
  - VSIX の 4 ボタン、ハンドラー、`RequestPhysicsControl`、`PhysicsDebugClient.TryControl`、文言を削除した。
- テスト: `EditorPhysicsTests.PhysicsAdvancesOnlyInTheRunningGame`
  - UserEcsProject の `PhysicsStackBottomScene` を、生成されたループで 8 tick 回す。
  - `Runtime` では落ち、`EditorPreview` では動かない。どちらでも物体は物理に登録されている。
  - 修正前は `EditorPreview` で物体が動いて失敗した。
