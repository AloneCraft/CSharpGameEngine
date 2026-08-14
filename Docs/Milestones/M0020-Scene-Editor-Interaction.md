# M0020: Scene Editorインタラクション基盤(前方計画)

## 目的

ユーザーの依頼: 「`Docs/シーンエディタの仕様.md`を設計して」。要求仕様(ナビゲーション・
Transformツール・修飾操作・スナップ・ギズモ合成・双方向同期・編集トランザクション)を現行の
ADR-0040基盤の上に実現する。設計判断は[[ADR-0041-Scene-Editor-Interaction-Architecture]]、
クラスレベル詳細は`Docs/Specifications/Scene-Editor-Design.md`に記録した。

**本書は通常のマイルストーン文書(実装後の回顧録)と異なり、前方視の計画書である。**
全フェーズのStatusは**計画済み**。各Phase完了時に「実装したこと」「検証結果(実際に実行して確認)」を
本書へ追記し、完了までPlanned→Implementedの遷移を台帳(F-L8-011)と同期させる。
未実装の項目を実装済みと記述してはならない。

## 計画フェーズ(未実装)

### Phase 0 — 前提修正とデッドコード削除

ギズモ・カメラ以前に、ワールド行列と描画基盤の前提を直す。

1. `SceneModelRenderObjectBuilder`のTransform型ハードコード(`global::UserEcsProject.Transform`)を
   `SceneComponentBinding`表へ置換(設計§9)。実シーン
   (`global::CSharpGameEngine.Runtime.Windows.Transform`)が全ノードIdentity・原点固まりで
   描画される実バグの修正。
2. 階層Transform合成: DFSで`TransformMath.ToWorld(Local, ParentWorld)`を畳み込む
   (現状は各ノードのローカルTRSをワールドとして使用)。
3. `SceneRenderer`の空オブジェクトリスト例外を撤廃(グリッド/ギズモのみのフレームを許容)。
4. `.scene`残骸コードの削除: `SceneFile.cs`/`SceneNodeAdder.cs`/`SceneEditorHub.ActiveScene`・
   `RequestReload`残滓と`SceneFileTests.cs`(Phase 9受け入れ済み退行の清算。台帳F-L8-003/007を
   Removed系記録へ更新)。
5. カメラ行列の外部注入化: `SceneRenderer.SetCamera(in Matrix4x4 View,in Matrix4x4 Projection)`+
   毎フレーム定数更新(値は当面`CreateBoundsFit`相当の現行固定値)。

**完了基準**: `FirstPersonStageScene`(実シーン)が正しいワールド変換で描画される(親子合成を含む
`SceneModelRenderObjectBuilderTests`拡張がグリーン)。既存テスト全件グリーン。台帳更新。

### Phase 1 — ナビゲーション

`SceneViewState`+`SceneViewCamera`(net472複製)+`SceneInputStateMachine`(設計§2〜4)。
RMB Look/Fly(WASDQE)、MMB Pan、Alt+LMB Orbit、Wheel Dolly/Zoom、RMB+Wheel速度、Shift/Ctrl速度修飾、
`InvertLookX/Y`、`WritableSettingsStore`永続化(設計§10)。キーボードはVS標準ツールウィンドウ
フォーカスモデル(ADR-0041決定事項2、`RenderSurfaceFocusPolicy`無改変)。

**完了基準**: 状態機械・カメラ数学のユニットテスト(設計§11の該当行)グリーン。実機Experimental
Instanceでナビゲーション操作と「コードエディタのTab/BS/Del非回帰」を目視確認。

### Phase 2 — ピッキングと選択の双方向化

`ScenePicker`(CPUレイキャスト、設計§5)。LMB選択/Ctrl+LMB複数選択、
`SceneSelectionOrigin.Viewport`の発行(ヒエラルキーが追従=双方向化)、F(FocusSelection)。

**完了基準**: 交差クエリのユニットテスト。実機でビューポートクリック→ヒエラルキー選択同期を確認。

### Phase 3 — Moveギズモと編集トランザクション配線

TransformGizmo(Move: 軸+平面ハンドル)、オーバーレイPSO、スクリーン一定スケール、
`EditTransactionController`(設計§7)、`SceneRenderer.UpdateObjectTransform`プレビューAPI、
`SetTransform`操作(設計§8)、**`SceneSourceEditor`/`SceneTextBufferWriter`の初の製品配線**、
最小競合処理(リベース失敗=破棄+通知)、W/Escキー。

**完了基準**: 実機でドラッグ移動→C#ソースの該当リテラルのみが差分→Ctrl+Zでシーンとコードの両方が
復元。1ドラッグ=1 Undo単位をテキストUndo履歴で確認。トランザクション系ユニットテストグリーン。

### Phase 4 — Rotate/Scaleと増分スナップ

E/Rツール、RotateHandle(リング)/ScaleHandle、Ctrl増分スナップ(移動/角度/スケール刻み)、
`TransformSpace`(World/Local)切替。

### Phase 5 — Duplicate・FollowView・頂点/面スナップ

Alt+ドラッグ複製(`DuplicateNode`op、trivia込み複製+Name一意化)、Shift=FollowView
(仕様§6.1の視点追従式)、V/Nホールドの頂点・面スナップ(`ScenePicker`クエリ2・3)。

### Phase 6 — Camera/LightプロパティギズモとCompoundGizmo合成

`CompoundGizmo`合成(設計§6.1)、`FrustumGizmo`(FOV/Near/Far/OrthographicSize)、
`AddComponent`/`RemoveComponent`op(必要範囲)、仕様§11の修飾キー適用可否表。
**着手時にライト語彙の小ADRを起こす**(エンジン標準化 vs L9型Binding登録、ADR-0041決定事項9)。
RectLightはスコープ外のまま。

### Phase 7 — 増分更新と競合仕上げ

`SceneDiff`の初消費: Transform/Member変更のみの場合にレンダラーを再生成しない増分更新、
毎フレーム`WaitForGpuIdle`の除去(フレームインフライト化)、ドラッグ開始値比較による差分統合
(設計§7競合マトリクスの拡張)、Escキャンセルの全状態網羅確認。

## 検証結果(実際に実行して確認)

2026-08の文書整備時点(本書・ADR-0040改稿・ADR-0041・Scene-Editor-Design.md・M0019作成、
文字化け修復、EditorSpec.md削除)で実行した確認:

```
grep -rlE '\?{4,}' --include='*.cs' --include='*.md' Docs/ src/ tools/ tests/ UserEcsProject/
  → 0件(ADR-0040とコード12ファイルの文字化けを全て書き直したことの機械的証明)

dotnet build CSharpGameEngine.slnx -c Debug
  → 成功、警告0/エラー0(コメント・文字列修復がビルドへ影響しないことの確認)

dotnet test CSharpGameEngine.slnx -c Debug --no-build
  → 全8テストプロジェクト 610件成功・失敗0(実装フェーズ開始前の基準値、内訳はM0019参照)
```

実装フェーズの検証結果は、各Phase完了時に本節へ追記する。

## 意図的に除外したこと(将来マイルストーン候補)

旧`Docs/EditorSpec.md`(`.scene` JSON前提の目標仕様書。ギズモ・Undo/Redo・複数選択をスコープ外と
宣言しており、`Docs/シーンエディタの仕様.md`と正面矛盾するため2026-08に削除)から、まだ生きている
目標を以下へ移設して保全する。

- **C#スクリプト(振る舞い)のアタッチ**(旧EditorSpec Phase 3): 選択ノードへ振る舞いを付与する
  導線。現アーキテクチャでは`GameScript`基底クラスではなくECS System/Componentの宣言
  (`UserEcsProject`)への統合として再設計が必要。エディタ内でユーザーコードを実行しない原則
  (ADR-0010と同根)は維持する。
- **Play(WYSIWYG)の導線**(旧EditorSpec Phase 4): エディタで組んだシーンが
  `dotnet run --project UserEcsProject`(またはF5)でそのまま再現されることの検証と、任意の
  Playツールバーボタン。シーン正本がC#である現在、実行時再現の仕組み自体は
  `[StartupScene]`/`InstantiateScene`(ADR-0038/0040)として実装済みであり、残るのはエディタ表示と
  実行時描画の一致検証(レンダラーが別実装であるため)。
- **Inspectorツールウィンドウ本体**(旧EditorSpec Phase 2の残り): 純粋モデル層
  (`SceneInspectorModelBuilder`/`SceneValue.IsEditable`)は実装済み。数値編集UIは
  `SetComponentMember`配線(Phase 3)後に小さく足せる状態になるが、本マイルストーンには含めない。

本マイルストーン自体のスコープ外(仕様・設計由来):

- `AddNode`/`RemoveNode`/`ReparentNode`/`ReorderNode`/`AssignAsset`操作(ヒエラルキー編集
  マイルストーンへ)。
- Unity/Unreal/Blender/Mayaナビゲーションプリセットの具体値充填(`Default`/`Custom`の器のみ)。
- カメラ姿勢の`SceneKey`別永続化(セッション内のみ)。
- Rect/Area Light(語彙が存在しない、仕様§10.4)。
- レイキャストの加速構造(必要になったら別ADR)。

## 残課題

- ライトComponentの所在(エンジン標準 vs L9)の決定はPhase 6着手時の小ADRへ先送りしている。
- 実機VS Experimental Instanceでの目視確認は各Phase完了条件に含めたが、M0019から引き継いだ
  「Phase 9時点の表示系の実機確認」自体も未消化のまま残っている(Phase 0完了基準に統合して消化する)。

関連: [[ADR-0041-Scene-Editor-Interaction-Architecture]]、
[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]、[[M0019-CSharp-Scene-DSL]]、
`Docs/シーンエディタの仕様.md`、`Docs/Specifications/Scene-Editor-Design.md`
