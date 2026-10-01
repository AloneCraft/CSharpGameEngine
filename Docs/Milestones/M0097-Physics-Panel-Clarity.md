# M0097 — Physics パネルを分かりやすくする（シミュレート操作とランタイム診断の廃止、VS の診断ツールの呼び出し）
- 日付: 2026-10-01
- 状態: 進行中

## 目的
ユーザーが Scene Designer の Physics パネルを見て「意味が分からない。わかりやすくして」と依頼した。調べて分かった問題を、ユーザーの判断に沿って直す。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| A | シミュレート / 一時停止 / ステップ / リセットの削除と、編集中は物理を進めない | 完了（77dc466） |
| B | ランタイム診断を機能ごと廃止（F-L8-027 / F-L8-030） | 完了（a8cbcdb） |
| C | パネルの構成と文言 | 完了（本コミット） |
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

## B. ランタイム診断を機能ごと廃止（F-L8-027 / F-L8-030）
- 事実（コードで確認）:
  - 「スナップショットを更新」は、プレビューのホストから GPU デバイス・宣言済みの Render Graph・D3D12 のメッセージ・OS の情報を 1 回だけ取得していた。
  - 入力と音声は、プレビューのホストが持たないので常に `NotOwned` だった。
  - JSON シーンのランタイムビュー（今の Scene ビュー）は取得に対応しておらず、押すとエラーになっていた。
  - エンジン側の入力・音声の診断の取得（`EngineHost.CapturePlatformDebugSnapshot` など）は、製品の中からは呼ばれていなかった。
- ユーザーの判断: 機能ごと廃止。代わりに VS のデバッガーとプロファイラーを呼び出せるようにする（D）。
- 削除したもの:
  - パネルの「ランタイム診断」の区画、`RuntimeDebugSnapshotPresentation`、クライアントの `GetDebugSnapshot`、`SceneHwndHost.CaptureRuntimeDebugSnapshot`、文言。
  - 通信規約の `Snapshot` コマンド、応答の `DebugSnapshot`、診断の DTO と検証。コマンドが減る非互換の変更なので、プロトコルの版を 6 から 7 に上げた。
  - ホストの診断の分岐と `IRenderPreviewSession.CaptureDebugSnapshot`。
  - エンジンの `PlatformDebugSnapshotCapture`、`InputServer` / `AudioDeviceServer` の `CaptureDebugSnapshot`、`EngineHost.CapturePlatformDebugSnapshot`。入力・音声のサーバー自体は残る。
  - Feature カタログ: F-L8-027 / F-L8-030 とステージ `vsix.debug-tools` / `vsix.platform-debug`。依存を F-L8-028 → F-L8-026、F-L7-034 → F-L4-012 に付け替えた（機能数は 71 から 69）。プラットフォームの行列から `vsix.platform-debug` を、リリース証拠から診断の 2 ファイルを外した。
  - VSIX のリリースのチェック `preview-render-audio`: 3 つの観測（WARP で描画、音声を持たない、ホストの所有）は、すべて診断の表示を読んで判定していた。そのため、台帳・リリースの行列・ベースライン・自動化シナリオから外した。
  - 消した機能だけを確かめていたテスト（`RuntimeDebugSnapshotPresentationTests`、`RuntimeDebugArchitectureTests`、各テストの診断部分）。
- テスト: `ProtocolV7_HasNoDiagnosticsSnapshotCommand`。版が 7 で、`Snapshot` コマンドと `DebugSnapshot` が無く、旧い値のコマンドは「unknown」で拒否されることを確かめる。修正前は版 6 で失敗する。
- docs:
  - Roadmap、Implementation-Status、Reverse-Engineered-Spec、Feature-Stages の 2 機能の行と節を削除、または「M0097 で廃止」と注記した。
  - Editor-VSIX と Audio-Input-Platform の表の行を削除した。ADR-0056 にも注記した。
  - 過去の Milestone と Conformance はそのまま残した。

## C. パネルの構成と文言
- 問題（コードで確認）:
  - 7 つの表示項目は「デバッグ」に印を付けないと何も描かない。それなのに、「Collider / AABB」と「スリープ」には最初から印が付いていて、有効に見えた。
  - Bake のツールチップは「exact Bake Host」「atomic」など内部の言葉で、何が起き、どこにファイルができるかが書かれていなかった。
- 変更（`SceneDesignerToolWindowControl.xaml`）:
  - **物理のデバッグ表示**:
    - 親のチェックを太字にし、その下に 1 行の説明を置いた。説明の内容は、ゲームの実行中はそのゲームの物理、それ以外は編集中のシーンの配置を描き、編集中は時間を進めないこと。
    - 7 項目は字下げしたパネル（`PhysicsDebugOptionsPanel`）に入れた。親のチェックが外れている間は、`IsEnabled` の結び付けで無効（グレー）になる。
    - 各項目にツールチップを付けた。速度・スリープ・接触は、ゲームの実行中に意味があると書いた。
  - **Bake（物理の事前計算）**:
    - 見出しと説明を付けた。説明には、既定のティック数（`PhysicsSceneBakeService.DefaultTickCount` を書式化）、ゲームが再生できること、出力先（Assets/Physics/Bakes）を書いた。
    - ボタンを「選択中のシーンを Bake」「プロジェクトの全シーンを Bake」にし、ツールチップを平易な言葉にした。
  - 順序をデバッグ表示 → Bake に入れ替えた。
  - 英語の文言も同じ構成にした。「Collider / AABB」は英日で別の文言になったので、英日が同じ値のキーの許可リストから外した。
- テスト: `PhysicsPanelGroupsTheOverlayOptionsUnderTheirSwitchAndExplainsBothSections`（XAML を解析して確かめる）。修正前は `PhysicsDebugOptionsPanel` が無く失敗した。確かめる内容:
  - 7 項目が親のチェックに結び付いたパネルの中にあり、それぞれにツールチップがある。
  - 2 つの区画の説明がある。
  - シミュレート操作と診断が残っていない。
- docs: `Scene-Editor-Design.md` の Physics パネルの節を、現在の構成に書き換えた。「今は実装していないが実装すべき機能」から、実装済みの Bake と描画の行を削除した。
