# M0030: SceneView Gizmo Transform Editing(監査とギャップ修正)

> **番号について**: 既存Milestoneは M0001〜M0020 で欠番が無いが、本書はユーザー指示により
> **M0030** を使用する。**M0021〜M0029 は欠番**であり、将来この範囲を埋める予定は無い。

## 目的

ユーザーの依頼は「シーンビューにギズモを表示し、ドラッグで位置を変更し、C#ソースへ1 Undoで反映する」
機能の新規実装だった。しかし着手前の調査で、**依頼された機能は[[M0020-Scene-Editor-Interaction]]の
Phase 3〜7で実装済み**であることが判明した(commit `94f75ae` / `978c0e7` / `984b510` / `d528776` /
`09b5c39`)。

そのため本Milestoneの目的を次へ再定義した。

> 依頼仕様の受け入れ基準(§9)と失敗時ハンドリング(§8)を既存実装へ突き合わせ、**本当に未達な項目
> だけ**を実装・テスト・検証する。動作している経路の再実装はしない。

## 監査結果: M0020で実装済みの対応表

| 依頼仕様の要件 | 実装箇所 |
|---|---|
| §4-1 選択中primaryにギズモ表示 | `SceneHwndHost.UpdateMoveGizmo()`(`SceneHwndHost.cs:564`) |
| §4-2 ドラッグ中プレビュー→マウスアップ確定 | `EditTransactionController.PreviewTranslation`(`:159`)/`SceneRenderer.UpdateObjectTransform`(`:385`) |
| §4-3 位置更新をUndo1ステップへ統合 | `SceneTextBufferWriter.TryApply` — 単一`ITextUndoTransaction` |
| §4-4 対象ノード消失時に安全停止 | `SceneHwndHost.cs:569`(`TryGetNode`失敗→`CurrentMoveGizmo=null`)/`SceneEditConflictMerger.cs:61`(commit拒否) |
| §4-5 未表示ファイルでも操作可・フォーカス不移動 | `SceneWorkspaceService.ApplyTextAsync`の`OpenDocument(activate:false)`(`:487`)/`RenderSurfaceFocusPolicy` |
| §4-6 反映失敗時の取り消し | `SceneHwndHost.CompleteEditCommit(false,…)`→`CancelEditTransaction()`(`:1064`) |
| §5-2 ズーム/距離に応じたギズモサイズ | `TransformGizmo.ComputeWorldScale`(`:101`、`ScreenFraction=0.16`) |
| §5-4 ギズモ種別の拡張可能構造 | `GizmoHandleId` enum + `TransformGizmo` partial(Move/Rotate/Scale/Property) |
| §8 解析失敗時は確定しない | `SceneEditorSessionService.CommitAsync`の`Model.HasErrors`判定(`:109`) |
| §8 例外をActivityLogへ記録 | `SceneDesignerToolWindowControl.CommitEditAsync`の`catch`(`:470`) |
| §8 ドラッグイベント重畳の状態ロック | `EditTransactionController`の`Idle/Dragging/Committing`+`EnsureState`、`SceneInputStateMachine`の`GizmoDragging` |
| §2 フェーズ2(Rotate/Scale/スナップ) | `TransformGizmoPhase4.cs`(Ctrl増分 Move 1.0 / Rotate 15° / Scale 0.1) |
| §2 フェーズ3(複数選択・子孫制約) | `SceneHwndHost.cs:944`(選択集合から子孫を除いたルート集合を算出) |

## 実装したこと

### T1. 失敗経路のギャップテスト(本体コード変更なし)

依頼仕様§9-6が挙げるテスト観点のうち「対象ノード消失時の停止」「解析失敗時に確定しない」を
機械的に固定するテストが無かった。既存実装は要件を満たしていたが、**その契約を守り続ける保証**が
無い状態だったため、失敗経路に絞ってテストを追加した(成功経路は`ScenePhase7Tests`が既に持つので
重複させていない)。

- `tests/CSharpGameEngine.VSExtension.Tests/SceneEditConflictMergerTests.cs`(新規、6件)
  - `Merge_NodeRemovedFromLatestScene_Fails` — §9-6 対象ノード消失
  - `Merge_TransformMemberBecameNonLiteralExpression_Fails` — 外部編集で式化したメンバーを上書きしない
  - `Merge_MismatchedSceneKey_Fails`
  - `Merge_SameDocumentVersion_SkipsRebaseAndKeepsTheDraggedValue`
  - `Merge_PropertyOperation_ComponentMissingFromNode_Fails`
  - `Merge_PropertyMemberBecameNonLiteralExpression_Fails`
- `EditTransactionControllerTests.cs`(2件追加)
  - `TryCreateCommit_WhenParentWorldIsNotInvertible_ReturnsFalseAndStaysDragging` — §8 解析失敗時は確定しない
  - `Cancel_DuringCommitting_RestoresOriginalWorldsAndReturnsPendingSnapshot` — §4-6 / §8 ロールバック
- `SceneInputStateMachineTests.cs`(1件追加)
  - `GizmoDragging_RedundantDownAndUpEvents_CommitExactlyOnce` — §8 ドラッグイベント重畳

### T2. 深度精度(M0020 残課題 R1)の解消

`near = 0.05` 固定と `far = max(FocusDistance*20, BoundsRadius*20)` の組み合わせで、実ステージ規模では
near:far 比が 20000:1 を超えていた。near を far 連動の比率で決める方式へ変更した。

```
Near = max(BaseNearPlane, Far / MaxDepthRatio)   // BaseNearPlane=0.05f, MaxDepthRatio=10000f
```

- `tools/CSharpGameEngine.VSExtension/SceneCameraFraming.cs` — `BaseNearPlane`/`MaxDepthRatio`/
  `ComputeNearPlane(float)`を新設し、`CreateProjection`をこれに切り替えた。
- `tools/CSharpGameEngine.VSExtension/SceneViewState.cs` — **`NearPlane=0.05f`の二重定義を削除**し、
  `SceneCameraFraming`側を唯一の出所にした。ドラッグ操作中に実際に使われるのはこちらの`GetProjection`。
- **正射投影には比率調整を適用しない**。正射の深度は線形なので1/z分布の問題が無く、nearを遠ざけると
  手前を無駄にクリップするだけになるため`BaseNearPlane`をそのまま渡す。
- 予算を10000:1にした根拠: 深度バッファは`D32_Float`(`SceneRenderer.cs:48`)で24bit UNORMより余裕が
  ある。この値だと実ステージ規模(far 8000〜10000)で near が 0.8〜1.0 になり、M0020 残課題R1が挙げた
  推奨値「0.5〜1.0」に一致する。半径1程度の小さいシーンでは`BaseNearPlane`が優先されるので、
  従来どおり0.05のまま手前が残る。
- ギズモ描画への影響は無い。Overlay PSOは`DepthStencilDescription.None`(`SceneRenderer.cs:325`)で
  深度テストを行わないため、この変更が効くのは通常メッシュのZ-fightingだけである。

回帰テスト:

- `SceneCameraFramingTests.CreateProjection_KeepsTheDepthRatioWithinTheBudget`(Theory 3件)
- `SceneCameraFramingTests.ComputeNearPlane_SmallScenes_KeepTheBaseNearPlaneSoCloseGeometryIsNotClipped`
- `SceneViewStateTests.GetProjection_Perspective_KeepsTheDepthRatioWithinTheBudget`(Theory 3件)
- `SceneViewStateTests.GetProjection_Orthographic_KeepsTheBaseNearPlane`

## 検証結果(実際に実行して確認)

```
dotnet build CSharpGameEngine.slnx -c Debug
  ビルドに成功しました。0 個の警告 / 0 エラー

dotnet test CSharpGameEngine.slnx -c Debug --no-build
  CSharpGameEngine.Entities.Tests        合格 42
  CSharpGameEngine.Import.Tests          合格 30
  CSharpGameEngine.Bridge.Hosting.Tests  合格 8
  CSharpGameEngine.Core.Tests            合格 122
  CSharpGameEngine.Windows.Tests         合格 87
  CSharpGameEngine.CodeAnalysis.Tests    合格 101
  CSharpGameEngine.SceneModel.Tests      合格 104
  CSharpGameEngine.VSExtension.Tests     合格 448
  合計 942件、失敗 0
```

VSExtension.Tests は M0020 完了時点の431件から **448件**(+17)へ増えた。

### テストが空虚でないことの確認(変異テスト)

追加テストは既存実装が既に満たしている契約を固定するものなので、通常のRed→Greenが観測できない。
そのため本体を一時的に壊して、意図したテストだけが落ちることを実際に確認した。

| 変異 | 結果 |
|---|---|
| `SceneEditConflictMerger.cs`の`CanEditTransformMembers`判定を`if(false&&…)`へ無効化 | `Merge_TransformMemberBecameNonLiteralExpression_Fails`のみ失敗(5件合格/1件失敗)。復元後6件合格 |
| `ComputeNearPlane`を`BaseNearPlane`固定(=修正前の挙動)へ戻す | 深度比テスト5件が失敗。**半径1(小シーン)のケースだけは合格したまま**で、テストが実ステージ規模の劣化だけを捉えていることが確認できた。復元後50件合格 |

## 意図的に除外したこと

- **依頼仕様§6のデータモデル案(`HierarchyEditKind.TranslateNode` / `SceneTransformEdit`)は採用しない。**
  `HierarchyEditKind`(`SceneEditorHub.cs:23`)は`AddChild/Rename/Remove/Reparent/Reorder`の**構造編集
  専用**であり、Transformの値編集は`EditTransactionController`→`SceneTransformChange`→
  `SceneEditCommitRequest`→`SetTransformOperation`という別経路で既に確立している。`TranslateNode`を
  追加すると同一の「値の書き戻し」が2系統になり、[[ADR-0041-Scene-Editor-Interaction-Architecture]]の
  編集トランザクション設計と衝突する。既存経路へ一本化する。ADR-0041が該当アーキテクチャを既に
  規定しているため、新規ADRは起こさない。
- **ギズモ機能そのものの再実装。** M0020 Phase 3〜7で完了しており、監査で要件充足を確認した。
- **Inspectorの数値編集UI。** M0020が明示的に将来送りにした別Milestone候補。
- **ADR-0042の軸ハンドスネス。** 世界+Xが画面左を向くのは意図的な設計であり、修正しない。
- **`far = Radius*20`係数の見直し。** Sky dome(`IncludeInBounds=false`、Scale=400)のクリップ回避と
  いう明示的な理由がある。今回はnear側だけで比を是正した。
- **`Docs/Implementation-Status.md`の`UserGameProject`陳腐化参照62件。** M0020が別タスクへ切り出し済み。

## 残課題

- **実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)**。M0020のPhase 0〜7も
  全て未実施のまま引き継いでいる。VSIXのコマンドライン配置サイクルは不安定なため**F5起動**を使う。
  手順: VSIXプロジェクトをF5(`/rootsuffix Exp UserEcsProject.slnx`が`StartArguments`に設定済み)→
  `FirstPersonStageScene.cs`を右クリック→「シーン デザイナーで開く」。

  | # | 確認対象 | 何が証明されるか |
  |---|---|---|
  | 1 | ノード選択でギズモが表示される | §9-1 選択→ギズモ表示 |
  | 2 | 軸ドラッグで位置が動き、Ctrl+Z 1回で戻る | §9-2 ドラッグ反映とUndo単位 |
  | 3 | ドラッグ中にコードエディタへフォーカスが移らない | §9-3 非フォーカス方針 |
  | 4 | ドラッグ後もコードエディタでTab/BackSpace/Deleteが効く | `RenderSurfaceFocusPolicy`の非回帰(commit `a162478`の既知回帰) |
  | 5 | 未表示の.csでもギズモ操作が成立し、対象ファイルがアクティブ化されない | §9-4 未表示ファイル |
  | 6 | 外部編集→再解析後もギズモが不整合を起こさない | §9-5 再解析後の再同期 |
  | 7 | 壁/床/Sky domeの境界にZ-fightingが無い | **T2の効果**(M0020検証項目8、残課題R1) |
  | 8 | カメラを寄せても手前が不自然にクリップされない | T2でnearを上げたことの副作用が無いこと |

  画面キャプチャはUIAの`NativeWindowHandle`+`PrintWindow`で取得する(`SetForegroundWindow`は
  フォーカスを奪い偽の入力を生むため使わない)。
- 上記の実機確認が完了するまで、台帳`F-L8-011`のVerificationは`TestVerified`のままとし、
  `Verified`へ上げない。

## 追記(2026-08-17): 実機フィードバック4件と対応

上記の実機確認をユーザーが開始し、4件のフィードバックを受けた。うち1件は確認手順の説明不足
(既に解決済み、ビューポートクリック→Wキーでギズモが出る仕様どおりの挙動だった)。残り4件は
コードの修正で対応した。

### 1. Wキー無しで即ギズモ表示(UX変更)

`SceneEditorSettings.DefaultActiveTool`を`Select`から`Move`へ変更した
([SceneEditorSettings.cs:42](tools/CSharpGameEngine.VSExtension/SceneEditorSettings.cs#L42))。
`UpdateMoveGizmo()`は`ActiveTool==Select`のとき明示的にギズモを隠す仕様のため、既定が`Select`だと
選択しただけではギズモが出ずWキーを押す一手間が必要だった。永続設定に既存の値がある場合はそちらが
優先されるため、初回起動時のみ影響する。

### 2. コミット直後のギズモ位置ズレ(バグ、原因特定)

`SceneHwndHost.CompleteEditCommit`はコミット成功直後に`UpdateMoveGizmo()`を呼ぶが、この関数は
`CurrentRenderObjects`(CPU側キャッシュ)からpivotを計算する。`CurrentRenderObjects`はコミット後の
非同期Roslyn再解析が完了するまで更新されないため、ドラッグで移動した直後は**一瞬だけ元の位置で
ギズモが再描画され、再解析完了後に正しい位置へ「追いつく」**という視覚的なズレが実機で確認された。

`EditTransactionController`に`CurrentPreviews`アクセサを追加し、`Complete()`が状態をクリアする前に
ドラッグ最終値を取得できるようにした。`CompleteEditCommit`は`CurrentRenderObjects`の該当ノード
(primaryと子孫)を最終Transformへ即時パッチしてから`UpdateMoveGizmo()`を呼ぶよう変更し、
`UpdateMoveGizmo()`自体は変更していない。回帰テスト:
`EditTransactionControllerTests.CurrentPreviews_AfterTryCreateCommit_StillReflectsTheFinalDraggedValueUntilComplete`。
実機での見た目の修正確認(HWND/D3D12を要するためユニットテストでは検証不可)は未実施。

### 3. Hierarchy選択時、マーク行を最上部へスクロール

`SceneSourceMarkerService.Update()`は選択ノードの`new Node{...}`をハイライトするだけでスクロールは
行っていなかった(M0020の非フォーカス方針どおり)。`IVsTextManager.EnumViews`(対象文書をアクティブ化
せず既存ビューを列挙できるAPI)経由でビューを取得し、`ITextView.DisplayTextLineContainingBufferPosition`
でcaretを動かさずにスクロールだけ行うようにした。再解析のたびに同じ選択で`Update()`が呼ばれても
毎回スクロールし直さないよう、直近にスクロールした選択集合を記録して比較している
(`LastScrolledPaths`)。

`IVsTextManager.EnumViews`/`IVsEnumTextViews.Next`のC# out/ref判定はildasmで
`Microsoft.VisualStudio.Interop.dll`を逆アセンブルして実シグネチャを確認した
(`Microsoft.VisualStudio.TextManager.Interop.dll`は型フォワーダのみの空アセンブリで、
素朴なreflectionでは検証できない)。`EnumViews(IVsTextBuffer,out IVsEnumTextViews)`、
`Next(uint,IVsTextView[],ref uint)` — `Next`の第3引数はIL上`[out]`属性が無い素の`uint32&`のため
`ref`が必要(`EnumViews`の`ppEnum`は`[out]`属性ありで`out`)。

**フォーカスを奪わないことが未検証。** `EnumViews`はAPIの性質上アクティブ化を伴わないはずだが、
実機で確認するまでは確証できない。

### 4. ソース反映後のコードエディタcaret/スクロール位置維持

`SceneWorkspaceService.ApplyTextAsync`で、`Buffer.Replace`を呼ぶ前に対象バッファを表示中の全ビューの
caret位置とビューポート先頭行を`ITrackingPoint`として退避し、適用直後に復元するようにした
(`CaptureViewPositions`/`RestoreViewPositions`)。`SceneTextBufferWriter.TryApply`自体は既に最小差分
(`GetTextChanges`)で適用しており全文置換ではないため、**何が具体的にcaret/スクロールを動かしていたのか
根本原因は特定できていない。** 対症療法としてsave/restoreを追加した。実機で直っているか未検証。

### 実機再確認が必要な項目(上記8項目に追加)

| # | 確認対象 |
|---|---|
| 9 | ノードを選択しただけ(Wキー不要)でMoveギズモが出る |
| 10 | ドラッグでオブジェクトを移動して確定した瞬間、ギズモが元の位置へ一瞬戻らず即座に新位置へ移る |
| 11 | Hierarchyでノードを選択すると、マークされた行の先頭がコードエディタの最上部までスクロールする。この際コードエディタのフォーカス/アクティブタブが変わらない |
| 12 | ギズモ/インスペクター編集でC#ソースへ反映されたあと、コードエディタのcaret位置とスクロール位置が変わらない |

## 追記(2026-08-17 続き): シーンビュー選択でもC#ソースをマーク・スクロール(M0032)

Hierarchyでノードを選択するとC#ソースがマーク＋スクロールされる(上記3.)一方、シーンビュー
(ビューポート)でオブジェクトをクリック選択したときはHierarchyツリーのハイライトだけが追従し、
C#ソース側は何も起きないという非対称がユーザーから報告された。

原因は`CSharpGameEngineViewerPackage.OnSelectionChangedForHierarchy`にあった。この関数は発生源を
問わず全選択変更を受け取るが、`Origin==Hierarchy`以外(実質Viewportのみ、`SceneDesignerToolWindowControl`
の`OnObjectPicked`/`OnObjectsMarqueePicked`等がすべて`SceneSelectionOrigin.Viewport`で発行する)では
明示的に`SourceMarkerService.Update(Model:null,Paths:[])`を呼びマークを消すだけで、マーク・スクロール
を一切行わない実装になっていた(M0020設計時点での意図的な制限、「ソース装飾はHierarchy起点の操作に
限る」というコメントがあった)。

`SourceMarkerService.Update(Model,Paths)`自体は発生源に依存しない汎用APIで、フォーカスを奪わない
スクロール(3.で実装した`EnumViews`経由の経路)や、選択が実際に変わった時だけスクロールする重複防止
ロジックも既に備えている。呼び出し側の`OnSelectionChangedForHierarchy`を、Hierarchy以外の発生源でも
選択中の`NodeIds`を`NodePath`へ解決して`SourceMarkerService.Update`へ渡すよう書き換えるだけで対応
できた([CSharpGameEngineViewerPackage.cs](tools/CSharpGameEngine.VSExtension/CSharpGameEngineViewerPackage.cs)、
`OnSelectionChangedForHierarchy`)。Hierarchyツリーへのprimary選択publish(既存動作)はそのまま維持。

マーキー(矩形)選択は完了時1回のみ選択発行され、ギズモドラッグ中は選択集合が変化しないため
`SceneSelectionService.Select`の重複排除で`SelectionChanged`自体が発火しない。よって連続スクロール等
の副作用は理論上発生しない。

このメソッドはライブVSサービス(`IVsRunningDocumentTable`等)に依存するPackage内グルーコードで、
姉妹メソッド`OnHierarchySelectionChanged`同様に既存のユニットテストは存在せず、今回も追加していない。
実機での動作確認は下表#13を参照。

| # | 確認対象 |
|---|---|
| 13 | シーンビューでオブジェクトをクリック選択すると、対応するC#コードがマークされ先頭行がエディタ最上部までスクロールする(Hierarchy選択時と同じ挙動)。フォーカス/アクティブタブは変わらない。矩形選択で複数選択した場合も複数箇所がマークされる |

## 追記(2026-08-17 続き): ギズモをMove+Rotate+Scale同時表示化、Local/Globalアイコン追加(M0033)

以前はMove/Rotate/Scaleが`SceneActiveTool`により排他的にどれか1系統だけ表示・操作可能で、
W/E/Rキーで切り替える方式だった。ユーザーからBlender/Unity寄りの「3系統を常に同時表示・操作
できるように」という要望を受け、以下を実装した。

**半径帯の再設計**: 旧`RotateRingRadius=0.9`と`ScaleAxisLength=0.9`が完全一致しており、Scale軸
キューブとRotateリングが同じ座標に重なる実バグがあった。加えてRotateリングはMove平面ハンドルと
同一平面上にあり、対角最大到達距離(≈0.68)より内側だと平面ハンドルの当たり判定を貫通する。
`TransformGizmo.cs`/`TransformGizmoPhase4.cs`の関連定数を、Rotateリング(0.78)→Scale軸キューブ
(1.0)→Move矢印の円錐(1.15〜1.35)の順に重ならない帯へ再配置した(詳細と根拠は
[TransformGizmoPhase4.cs](tools/CSharpGameEngine.VSExtension/TransformGizmoPhase4.cs)の該当コメント
参照)。半径は実機での見た目確認前提の出発値。

**ヒットテストの統合**: `TransformGizmo.TryHitTransform`を新設し、Move/Rotate/Scale/Local-Global
トグルアイコンを毎回まとめてヒットテストしレイ距離最小を採用するようにした
(`TryHitMove`/`TryHitRotate`/`TryHitScale`に`out float RayDistance`を追加)。呼び出し元
`SceneHwndHost.TryHitTransformGizmoAt`は`State.ActiveTool`による単一系統分岐をやめてこれを呼ぶ。

**ドラッグ系統をHandleから導出**: `BeginTransformGizmoDrag`は従来ambientな`State.ActiveTool`で
Move/Rotate/ScaleGizmoDragのどれを開始するか決めていたため、3系統同時ヒットテストにより
「`State.ActiveTool`と実際にヒットしたHandleの系統が食い違う」状況が起こり得た(潜在バグ)。
新設した`GetHandleFamily(GizmoHandleId)`でヒットした`Handle`自身から系統を逆引きし、
`CanEditTransform`/`MoveGizmoDrag.TryBegin`等のswitch/`ActiveCommitMembers`/`ActiveTransformTool`
すべてをこの値で駆動するよう修正した。`UpdateTransformGizmoDrag`/コミット処理はドラッグ開始時に
キャプチャした`ActiveTransformTool`を読むだけなので変更不要だった。

**Local/Globalアイコン**: ピボット中心は既にScaleUniformのキューブが占有しているため、
カメラのRight/Up方向へ固定オフセットした位置(全ハンドルの外側)に新設の`TransformSpaceToggleGizmo`
を配置した(`RotateView`リングのカメラ相対法線と同じ考え方)。Local時は立方体ワイヤーフレーム、
World時はリンググリフを表示し分ける(overlayパイプラインがunlit線/三角形のみのため、テクスチャ・
テキストは使えない)。クリックはドラッグを生成せず`SetTransformSpace`の反対値を呼んで即座に
トグルし、`false`を返すことで既存の`Machine.CancelCurrent()`経路(`SceneInputStateMachine`側の
変更ゼロ)でIdleへ戻す。従来の`TransformSpaceSelector`(WPF ComboBox)は同じ状態を表示する二重UIに
なるため削除した。

**W/E/Rキーとツール永続状態の廃止**: 3系統が常時全部有効になったため「現在のツール」という
排他状態自体が不要になった。`HandleKeyDown`のW/E特殊分岐・`ActivateScaleTool`(R)・
`SetActiveTool`を削除し、`SceneViewState.ActiveTool`フィールドと`SceneEditorSettings`の
`DefaultActiveTool`/`ReadActiveTool`/`WriteActiveTool`(未使用になった永続設定)も削除した。
`SceneActiveTool`列挙自体は「ハンドル系統識別子」として`GetHandleFamily`/`CanEditTransform`/
`GetTransformMembers`/`GetToolDisplayName`に残る。`UpdateMoveGizmo`/`TryHitTransformGizmoAt`の
`ActiveTool==Select`ゲート(元々W/E/R以外に書き換え経路が無く常時到達不能だった)も削除し、
ギズモの可視性は「ノードが選択されているか」だけで決めるようにした。

Evidence: `TransformGizmoTests.cs`に7件追加(`TryHitTransform`の系統横断最近傍解決・Move平面
ハンドルの生存確認・`TransformSpaceToggleGizmo`のヒット/オーバーレイ)。`SceneEditorSettingsTests.cs`/
`SceneViewStateTests.cs`のActiveTool関連テストを削除・置換。ソリューション全体974/974(既存969件
のうち2件削除+9件追加)。`SceneHwndHost`の実際の配線(`GetHandleFamily`によるドラッグ系統解決・
半径帯が実機で視覚的に重ならないか・アイコンの位置/クリック操作感)はHWND/D3D12依存のため
ユニットテスト不可、実機確認が必須。

| # | 確認対象 |
|---|---|
| 14 | ノードを選択すると、Move矢印・Rotateリング・Scaleハンドルが同時に表示される |
| 15 | それぞれを個別にドラッグでき、意図しない系統が反応しない(特に各半径帯の境界付近) |
| 16 | Move平面ハンドル(黄/シアン/マゼンタ)は半径帯変更後も引き続き機能する |
| 17 | ギズモ上のLocal/Globalアイコンをクリックすると座標空間が切り替わり、ギズモの向きが即座に変わる(ドラッグは発生しない) |
| 18 | W/E/Rキーは何も起こさない(以前のツール切替は発生しない) |
| 19 | 全体的な視覚的な煩雑さ(重なり・見づらさ)がないか — 半径定数は実機フィードバックで再調整する前提 |

## 追記(2026-08-17 続き): Shift+Ctrlのサーフェススナップを中心の平面四角ハンドル化(M0034)

M0033の実機確認と並行して、Unity同様の操作性でサーフェススナップ(位置+法線への回転整列)を
使いたいという要望を受けた。**サーフェススナップ自体の数学は既にM0020 Phase 5で実装済み**
(`ScenePicker.TrySurfaceSnapQuery`、`EditTransactionController.PreviewSurfaceSnap`が位置と
面法線への回転整列まで行う)で、従来は`N`キーを押しながらMoveハンドルをドラッグする方式
(`SceneHwndHost.cs`の`GetKeyState(VK_N)`ポーリング)だった。今回変えたのは**起動方法と
可視化だけ**: `N`キーホールドを廃止し、Shift+Ctrl押下中にギズモ中心へ現れる専用ハンドル
`GizmoHandleId.SurfaceSnapPlane`(平面四角、輪郭はfill色のRGB反転)をドラッグする方式へ変更した。
頂点スナップ(`V`キー)はUnity標準に合わせてそのまま残した。

**新規`SurfaceSnapGizmo.cs`**: 武装中(未ドラッグ)はピボット・カメラ正対で表示・ヒットテストする
(`GetArmedPlacement`)。`TransformGizmoPhase4.CreatePlaneBasis`を`internal`へ上げて基底計算を
再利用した。`TransformGizmo.TryHitTransform`へ`bool SurfaceSnapArmed`引数を追加し、武装中かつ
ハンドルが編集可能なら四角だけを最優先でヒットテストする(Propertyギズモが先に短絡する既存の
前例と同じ形)。無効(Rotationが式等で編集不可)なら通常の統合ヒットテストへフォールバックし、
修飾キーを押しっぱなしでも他ハンドルを掴めなくなることを避けた。

**編集可否**: `SceneTransformBinding.CanEditTransform`に`SurfaceSnapPlane`のHandle優先分岐を追加し
(Toolに依らず`CanEditRotation`と同じ「全Position+全Rotationが編集可能」要件)、既存の
`CanSurfaceSnapSelection`フィールド(ドラッグ開始時に同じ条件を別途チェックしていた)は完全に
不要になったため削除した。

**修飾キーの武装判定**: マウス静止中でも1フレーム以内に四角の表示・非表示を切り替えるため、
`CompositionTarget.Rendering`ティックで`GetKeyState`をポーリングする新規`UpdateSurfaceSnapArmed`
を追加した(既存のFly Fast/Precisionポーリングと同じ方式、新規のWndProc/WPFキー配線は無し —
ネイティブサーフェスは意図的にキーボードフォーカスを持たない設計のため、その経路は行き止まり
だったことを確認済み)。

**ドラッグ系統**: `SurfaceSnapPlane`は`MoveGizmoDrag`(軸/平面の拘束)と噛み合わないため、
`BeginTransformGizmoDrag`で`MoveGizmoDrag.TryBegin`の評価自体を`||`短絡で回避する専用分岐を
追加した。M0033で確立した「ドラッグ系統はambientな状態ではなくヒットしたHandleが決める」方針を
踏襲し、一度ドラッグが始まれば修飾キーを離してもmouse-upまでSurfaceSnapドラッグのまま継続する。
ドラッグ中はFollowView(Shift)を明示的に抑止する(カメラが動くとヒット面が動くフィードバック
ループになるため)。

Evidence: `SurfaceSnapGizmoTests.cs`(新規、7件: 武装配置・ヒット判定・`TryHitTransform`の武装/
非武装/無効時フォールバック・輪郭色反転・褪色)、`SceneTransformMathTests.cs`に2件追加
(`CanEditTransform`のSurfaceSnapPlane分岐)。ソリューション全体983/983(既存974+9)。実際の
掴みやすさ・武装の反応速度・吸着の操作感はHWND/D3D12/`GetKeyState`依存のためユニットテスト
不可、実機確認が必須。

| # | 確認対象 |
|---|---|
| 20 | ノードを選択しShift+Ctrlを押すと、マウスを動かさなくてもギズモ中心に四角が出る(離すと消える) |
| 21 | 四角の輪郭がfillの反転色で視認しやすい |
| 22 | 四角をドラッグすると別オブジェクトの表面へオブジェクトがぴったり吸着する |
| 23 | 吸着時にオブジェクトの向きが接触面と揃い、四角もその面の角度で描かれる |
| 24 | ドラッグ中にカメラが勝手に動かない(FollowView抑止の確認) |
| 25 | マウスを離すとC#ソースのPositionとRotationが1 Undoで更新される |
| 26 | Rotationが式(メソッド呼び出し等)のノードでは四角が褪色して掴めない |
| 27 | Nキーではもうサーフェススナップが起きない/Vキーの頂点スナップは従来どおり動く |
