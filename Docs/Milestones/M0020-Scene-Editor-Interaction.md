# M0020: Scene Editorインタラクション基盤(前方計画)

## 目的

ユーザーの依頼: 「`Docs/シーンエディタの仕様.md`を設計して」。要求仕様(ナビゲーション・
Transformツール・修飾操作・スナップ・ギズモ合成・双方向同期・編集トランザクション)を現行の
ADR-0040基盤の上に実現する。設計判断は[[ADR-0041-Scene-Editor-Interaction-Architecture]]、
クラスレベル詳細は`Docs/Specifications/Scene-Editor-Design.md`に記録した。

本書は前方視の計画書として開始し、各Phase完了時に「実装したこと」「検証結果」を追記してきた。
2026-08-16にPhase 0〜7と後続Task Cを実装し、台帳F-L8-011をImplementedへ同期した。

## 実装フェーズ

### Phase 0 — 前提修正とデッドコード削除

ギズモ・カメラ以前に、ワールド行列と描画基盤の前提を直す。**実装済み(2026-08)**。

1. `SceneModelRenderObjectBuilder`のTransform型ハードコード(`global::UserEcsProject.Transform`)を
   新規`SceneComponentBinding`/`SceneComponentBindings`表へ置換(設計§9)。実シーン
   (`global::CSharpGameEngine.Runtime.Windows.Transform`)が全ノードIdentity・原点固まりで
   描画される実バグの修正。`ExtractWorldTransform`は`ExtractLocalTransform`へ改名した
   (旧名が「ローカルTRSをワールドと呼ぶ」このバグの共犯だったため)。
2. 階層Transform合成: `Model.EnumerateNodes()`のフラット列挙を`ParentWorld`を運ぶpre-order再帰へ
   置き換え、`world = local * parentWorld`を畳み込む。**式のみ複製**(型は参照しない — net472から
   net10.0の`TransformMath`は参照不可。ADR-0041 代替案節)。
3. `SceneRenderer`の空オブジェクトリスト例外を撤廃(グリッド/ギズモのみのフレームを許容)。
   実際には3箇所を直す必要があった: ctorの`throw`、定数バッファ幅の`Math.Max(1,Count)`クランプ
   (D3D12は0バイトbufferを許可しない)、`UpdateAllConstantBuffers`の0件早期return。
   `SceneHwndHost`側の`Count==0`ガードも撤去した。
4. `.scene`残骸コードとシェイプパレットの全面削除: `SceneFile.cs`/`SceneNodeAdder.cs`/
   `PrimitiveSpawner.cs`/`ShapePaletteControl.xaml(.cs)`/`ShapePaletteToolWindowPane.cs`/
   `ShowShapePaletteToolWindowCommand.cs`/`SceneFileTests.cs`の8ファイルを削除し、
   `SceneEditorHub`の旧`.scene`チャンネル(`ActiveScene`/`RequestReload`/`ReloadRequested`)と
   購読者を持たなかった`AllSceneModels`系も撤去。ヒエラルキーのドラッグ&ドロップは`.xaml`の
   `AllowDrop`/`Drag*`/`Drop`束縛と`.xaml.cs`のハンドラを対で削除。vsct 0x0107は既存
   `0x0101`/`0x0108`の作法どおりIDSymbolを消して退役コメントのみ残した(IDは再利用しない)。
   台帳F-L8-006/007をRemovedへ。
5. カメラ行列の外部注入化: `SceneRenderer.SetCamera(in Matrix4x4 View,in Matrix4x4 Projection)`と
   `ConstantsDirty`フラグを追加し、`ComputeViewMatrix`/`ComputeProjectionMatrix`/
   `ComputeSceneBounds`/`BoundsCenter`/`BoundsRadius`を削除。視点ポリシーは新規
   `SceneCameraFraming`(純粋static)へ移設し、バウンズ計算が初めてGPU無しでテスト可能になった。
   定数バッファの更新経路は`RenderFrame`冒頭の1本に統一(旧ctor/`SetSelectedNode`/`Resize`の
   3箇所の明示呼び出しを廃止)。値はバウンズ由来の固定フレーミング(方向`normalize(1,0.75,1)`、
   距離`Radius/sin(FovY/2)*1.1`、near/far = `0.05`/`Radius*20`)。`Camera.CreateBoundsFit`とは
   Radiusの定義(境界球 vs AABB半対角)とnear/farが異なる。`SceneHwndHost`は新規`ApplyCamera()`を
   毎フレーム・リサイズ時・レンダラー再生成時に呼ぶ(Phase 1で`SceneViewState`へ差し替わる継ぎ目)。
   副次的に`new SceneRenderer(...,800,600,...)`のハードコードが消え、初回フレームのアスペクト歪みも
   解消した。

なお項目3は**観測可能なバグの修正ではなく Phase 3(グリッド/ギズモ)の前提整備**である。`Build`は
`Root`が非nullなら必ず1件以上返し(認識できないノードは`GizmoMeshFactory`フォールバック)、
`Root==null`は`Rebuild()`が先に弾くため、`Count==0`はPhase 0の本番経路では到達不能。

**完了基準**: 既存テスト全件グリーン+新規21件。実機Experimental Instanceでの目視確認(下記検証節)。

### Phase 1 — ナビゲーション

`SceneViewState`+`SceneViewCamera`(net472複製)+`SceneInputStateMachine`(設計§2〜4)。
RMB Look/Fly(WASDQE)、MMB Pan、Alt+LMB Orbit、Wheel Dolly/Zoom、RMB+Wheel速度、Shift/Ctrl速度修飾、
`InvertLookX/Y`、`WritableSettingsStore`永続化(設計§10)。キーボードはVS標準ツールウィンドウ
フォーカスモデル(ADR-0041決定事項2、`RenderSurfaceFocusPolicy`無改変)。**実装済み(2026-08)**。

**完了基準**: 状態機械・カメラ数学のユニットテスト(設計§11の該当行)グリーン。実機Experimental
Instanceでナビゲーション操作と「コードエディタのTab/BS/Del非回帰」を目視確認。

### Phase 2 — ピッキングと選択の双方向化

`ScenePicker`(CPUレイキャスト、設計§5)。LMB選択/Ctrl+LMB複数選択、
`SceneSelectionOrigin.Viewport`の発行(ヒエラルキーが追従=双方向化)、F(FocusSelection)。
**実装済み(2026-08)**。

**完了基準**: 交差クエリのユニットテスト。実機でビューポートクリック→ヒエラルキー選択同期を確認。

### Phase 3 — Moveギズモと編集トランザクション配線

TransformGizmo(Move: 軸+平面ハンドル)、オーバーレイPSO、スクリーン一定スケール、
`EditTransactionController`(設計§7)、`SceneRenderer.UpdateObjectTransform`プレビューAPI、
`SetTransform`操作(設計§8)、**`SceneSourceEditor`/`SceneTextBufferWriter`の初の製品配線**、
最小競合処理(リベース失敗=破棄+通知)、W/Escキー。
**実装済み(2026-08-16)**。ハンドネス判断は
[[ADR-0042-Scene-Editor-Gizmo-Axis-Handedness]]、実測結果と未実施の実機確認は本書の検証節を参照。

**完了基準**: 実機でドラッグ移動→C#ソースの該当リテラルのみが差分→Ctrl+Zでシーンとコードの両方が
復元。1ドラッグ=1 Undo単位をテキストUndo履歴で確認。トランザクション系ユニットテストグリーン。

### 後続Task C — 矩形選択

空間からのLMBドラッグを`MarqueeSelecting`として追加し、通常は置換、Ctrlは加算で複数選択を
`SceneSelectionOrigin.Viewport`へ発行する。オブジェクト開始時は距離にかかわらず`PendingSelect`から
切り替えない。投影済みメッシュ境界と矩形の交差は`ScenePicker.PickMarquee`、表示はPhase 3で導入した
overlay PSOへ`MarqueeOverlay`を合成する。**実装済み(2026-08-16)**。

**完了基準**: 状態機械、矩形×投影境界、置換/加算ポリシー、overlay形状のユニットテストがグリーン。
実機で空間矩形、Ctrl加算、オブジェクト開始固定、コードエディタ制御キーの非回帰を目視確認。

### Phase 4 — Rotate/Scaleと増分スナップ

E/Rツール、RotateHandle(リング)/ScaleHandle、Ctrl増分スナップ(移動/角度/スケール刻み)、
`TransformSpace`(World/Local)切替。
**実装済み(2026-08-16)**。Phase 3のframe/overlay/解析hit/編集トランザクションを拡張し、
専用の別描画経路や別commit方式は追加していない。実装値と実機未確認項目は本書の検証節を参照。

### Phase 5 — Duplicate・FollowView・頂点/面スナップ

Alt+ドラッグ複製(`DuplicateNode`op、trivia込み複製+Name一意化)、Shift=FollowView
(仕様§6.1の視点追従式)、V/Nホールドの頂点・面スナップ(`ScenePicker`クエリ2・3)。

**実装済み(2026-08-16)**。Alt+handle優先、元を動かさない一時複製描画、複製+最終Transformの
単一Undo、再解析後の複製Selection、Move/Rotateのtotal-delta Follow View、V/Nの自己除外snapを
既存state machine/renderer/transaction/source batchへ統合した。実機未確認項目は本書の検証節を参照。

### Phase 6 — Camera/LightプロパティギズモとCompoundGizmo合成

`CompoundGizmo`合成(設計§6.1)、`FrustumGizmo`(FOV/Near/Far/OrthographicSize)、
`AddComponent`/`RemoveComponent`op(必要範囲)、仕様§11の修飾キー適用可否表。
**着手時にライト語彙の小ADRを起こす**(エンジン標準化 vs L9型Binding登録、ADR-0041決定事項9)。
RectLightはスコープ外のまま。

**実装済み(2026-08-16)**。ADR-0043でL9完全修飾名Bindingを採用し、Cameraの
FOV/Near/Far/OrthographicSize、Directional/Point/Spot LightのDirection/Range/Coneを
TransformGizmoと同じoverlayへ合成した。PropertyはCtrl単位スナップのみ有効でShift/Altは無効。
省略Member追記とAddComponent/RemoveComponent source opも既存live text 1 Undo経路へ統合した。

### Phase 7 — 増分更新と競合仕上げ

`SceneDiff`の初消費: Transform/Member変更のみの場合にレンダラーを再生成しない増分更新、
毎フレーム`WaitForGpuIdle`の除去(フレームインフライト化)、ドラッグ開始値比較による差分統合
(設計§7競合マトリクスの拡張)、Escキャンセルの全状態網羅確認。
**実装済み(2026-08-16)**。`SceneIncrementalUpdatePolicy`がmesh/構造変更だけをrebuildへ分類し、
Transform/非mesh Member変更はmeshを再利用して全階層world行列とboundsを更新する。
`SceneRenderer`はallocator・定数/overlay buffer・fence値を2 frame slotへ分離し、slot再利用時だけ
待機する。Transform/Propertyの競合はドラッグ開始→終了差分を最新値へ合成し、Escは全7入力状態から
Idleへ終端する。実機未確認項目は本書の検証節を参照。

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

### Phase 0(2026-08)

```
dotnet build CSharpGameEngine.slnx -c Debug → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx -c Debug --no-build → 622件成功・失敗0
  (610 − SceneFileTests 9 + 階層合成7 + SceneComponentBindingTests 5 + SceneCameraFramingTests 9。
   VSExtension.Tests 単体は 118 → 130)

削除の取りこぼし確認(SceneFile/SceneNodeAdder/PrimitiveSpawner/ShapePalette/ActiveScene/
RequestReload/ReloadRequested/ActiveAllSceneModels/UserEcsProject.Transform の全 grep)→ 0件
(既知の偽陽性 SceneWorkspaceService.IsGeneratedSceneFile と
SceneDesignerContextMenuPolicyTests のテスト名を除く)
```

`SceneCameraFramingTests.FrameBounds_DegenerateBoundsAndAspect_ProduceFiniteMatrices`が実装バグを
1件検出した: 退化半径では`far`(0.02)が`near`(0.05)を下回り
`Matrix4x4.CreatePerspectiveFieldOfView`が`ArgumentOutOfRangeException`を投げていた。`far`に
下限ガード(`Math.Max(Radius*20, near*2)`)を追加して修正した。実経路では`ComputeBounds`が半径を
1fで床打ちするため到達しないが、退化入力テストがその境界を踏んだ。

**ユニットテストでカバーできていないもの(正直な記録)**: `SceneRenderer`の空オブジェクト対応3箇所
(throw撤去・定数バッファ幅クランプ・Map早期return)はHWNDとD3D12デバイスを要求するので
ユニットテスト不能。証跡は`Build_ModelWithoutRoot_ReturnsEmptyList`(空リストが作れること)、
コードレビュー、実機での既存シーン非回帰の3点に限られる。

**階層Transform合成の実シーン証跡は存在しない**: 入れ子`Children`を持つシーンがリポジトリに1つも
無い(`FirstPersonStageScene`はRoot直下フラット、`MainScene`/`PrimitiveShowcase*`は単一ノード)。
したがって項目2の証跡は`SceneModelRenderObjectBuilderTests`の階層合成テスト4件のみである
(実シーンへ入れ子ノードを追加するのは、`FirstPersonStageScene`がUE取り込みの正本であるため
スコープ外)。

**実機VS Experimental Instance での目視確認(未実施 — ユーザー側で実施)**:
`devenv.exe /rootsuffix Exp UserEcsProject.slnx`で`FirstPersonStageScene.cs`を開き、コードエディタ
右クリック→「シーン デザイナーで開く」。スクリーンショットはUIA `NativeWindowHandle`+`PrintWindow`
(`SetForegroundWindow`は使わない)。修正前は全メッシュが原点に重なった1つの塊だったので、
次の順で確認する。

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | 床が広がる(`Scale=(4,1,4)`) | スケールが効いている |
| 2 | 矩形の壁が囲む(±18〜20に配置) | 平行移動が効いている |
| 3 | 中央に薄い円盤がY=2に浮く(`SM_Cylinder`=`Position(0,2,0)`,`Scale(7,0.1,7)`) | Y方向移動と非等方スケールの同時証明。一目で分かるランドマーク |
| 4 | 四隅のQuarterCylinderが各々別の向きを向く(`RotationY=±0.7071`/180°) | **回転抽出の唯一の視覚的証明**(落ちていれば四隅が全部同じ向きになる) |
| 5 | SkySphereが巨大ドームとして包む一方、カメラはステージを収めたまま | `IncludeInBounds=false`が生きている |
| 6 | `SM_Cylinder`選択でその物体だけがオレンジに染まる | DFS書き換え後も`NodePath`同一性が保たれた |
| 7 | `PositionY=2f`→`6f`の編集で250ms後に円盤が上がる | ライブ更新の非回帰 |
| 8 | 壁/床/Sky domeの境界にZ-fightingが無い | 下記残課題R1 |
| 9 | 「シェイプ パネルを表示」がツールバー/メニューに無い | 撤去できている |
| 10 | コードエディタのTab/Backspace/Deleteが効く | `RenderSurfaceFocusPolicy`非回帰 |

⚠️ Experimental hiveは`[ProvideToolWindow]`とvsctを消してもレイアウトキャッシュに「シェイプ」ペインを
復元しうる(`CSharpGameEngine\InitialLayoutApplied`設定により初回レイアウトは二度と走らない)。
消えて見えない場合は`devenv /rootsuffix Exp /updateconfiguration`とウィンドウレイアウトのリセットを試す。

### Phase 1(2026-08)

```
dotnet build CSharpGameEngine.slnx -c Debug → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx -c Debug --no-build → 688件成功・失敗0(622 + 66)
```

実機Experimental Instanceでの検証で、コードエディタのTab/Backspace/Delete非回帰(最優先項目)は
問題なかったが、以下3件が仕様と異なる挙動を示し、その場で修正した(詳細はConformance.md
「2026-08 M0020 Phase 1: 実機検証で判明した3件の不具合」参照):

1. **設計時の事実誤り**: ADR-0041決定事項2は「`WM_MOUSEWHEEL`はフォーカスを持つHWND宛」という
   理解のもとホイールをWPF側`RenderHostGrid.PreviewMouseWheel`で受ける設計にしていたが、実機で
   一度も発火しなかった。他のマウスメッセージと同様`RenderSurfaceWindow.WndProc`で直接受ける
   実装へ戻し、機能しないWPF側購読は削除した。ADR-0041を訂正済み。
2. **実装バグ**: `SceneViewState.TickFly`のPitch(上下Look)が既定で仕様と逆方向。共有関数
   `SceneViewCamera.ApplyLookDelta`自体は変えず(Orbitは正しかったため)、Fly/Look呼び出し側で
   `DeltaY`を反転して修正。
3. **実装バグ**: `SceneViewState.ApplyPan`の左右(X)移動が既定で仕様と逆方向(上下Yは正しい)。
   符号を反転して修正。

ADR-0041決定事項8(「仕様§3の5項目のみ永続化」)もScene-Editor-Design.md §10と食い違っていたため
「7項目(InvertLookX/Y含む)」へ訂正した。

### Phase 2(2026-08)

```
dotnet build CSharpGameEngine.slnx -c Debug → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx -c Debug --no-build → 707件成功・失敗0(688 + 19)
```

`ScenePicker`(Möller–Trumbore、CPUレイキャスト)・`SceneViewCamera.ScreenPointToRay`(unproject)・
`SceneCameraFraming.ComputeSelectionBounds`・`SceneViewState.FocusOnBounds`・
`SceneInputStateMachine`の`PendingSelect`状態はすべてユニットテストで固定した。
`SceneHwndHost`/WPF側の配線(クリック→Pick→選択発行、ヒエラルキーとの双方向同期、Fキー)は
Phase 1と同じ理由でユニットテスト対象外(VS SDK型への依存が深い)。

**実機VS Experimental Instance での目視確認(未実施 — ユーザー側で実施)**:

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | ビューポートでオブジェクトをLMBクリック→そのオブジェクトがハイライトされる | Pick+単一選択 |
| 2 | 空間(何もない場所)をLMBクリック→ハイライトが消える | 選択解除 |
| 3 | 手順1の直後、ヒエラルキーツールウィンドウの該当ツリーアイテムがハイライトされる | 双方向化(Viewport→Hierarchy) |
| 4 | ヒエラルキーで別ノードをクリック→ビューポートのハイライトが移る(既存の一方向、非回帰) | 既存経路の非回帰 |
| 5 | オブジェクトをCtrl+LMBクリック→複数選択に追加。もう一度Ctrl+LMBクリック→選択から外れる | トグル |
| 6 | オブジェクトを選択してF→カメラがそのオブジェクトを中心に再センタリングされる(視線方向は変わらない) | FocusSelection |
| 7 | 何も選択していない状態でF→何も起きない | FocusSelectionの無選択ガード |
| 8 | RMBでNavigation中(Flying)はLMBクリックが選択を開始しない | 仕様§12優先順位(Navigation>Selection) |
| 9 | LMBを押したまま(PendingSelect中)にRMBを押す→Flyingへ切り替わる | 優先順位によるプリエンプト |
| 10 | コードエディタのTab/Backspace/Deleteが引き続き効く | `RenderSurfaceFocusPolicy`非回帰 |

### ヒエラルキーが常に空白だった不具合(2026-08、Phase 2の実機確認中に発見)

```
dotnet build CSharpGameEngine.slnx -c Debug → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx -c Debug --no-build → 712件成功・失敗0(707 + 5)
```

上表#3(Viewport→Hierarchy)を確認しようとした際、ヒエラルキーパネルが`IScene`実装ファイルを
開いても常に空白であることが判明した。原因はPhase 2の変更ではなく、ADR-0040 Phase 9で
`SceneNodeViewModel`(このVSIX初のMVVM型)を導入した時点から存在していたバグで、
`DisplayName`・`ComponentSummary`・`Children`が`internal`だったためWPFのバインドが**黙って失敗**
していた(WPFはpublicプロパティしか解決しない)。3件を`public`にして修正した。詳細はConformance.md
「2026-08 M0020: ヒエラルキーが常に空白だった不具合」参照。

このバグは`InternalsVisibleTo`が効くユニットテストからは構造的に検出できない(テストからは
`internal`でも読めるため既存テストは全件成功していた)。再発防止として、XAMLがバインドする5件が
`BindingFlags.Public|BindingFlags.Instance`で取得できることを固定するテストを追加した。
**今後このVSIXにデータバインド用のViewModelを追加する場合、バインド対象のプロパティは必ず
`public`にすること。**

### Phase 3(2026-08-16)

```text
dotnet build CSharpGameEngine.slnx -c Debug
  → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx -c Debug
  → 全8テストプロジェクト808件成功・失敗0
    (Phase 3前784 + 新規24。VSExtension.Testsは316件)

dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj \
  -c Debug --filter "FullyQualifiedName~SceneTransformMathTests|FullyQualifiedName~TransformGizmoTests|FullyQualifiedName~EditTransactionControllerTests|FullyQualifiedName~SceneSourceEditorTests|FullyQualifiedName~SceneInputStateMachineTests|FullyQualifiedName~SceneTextChangePlannerTests"
  → 54件成功・失敗0
```

`SceneTransformMath`/`TransformGizmo`/`MoveGizmoDrag`/`EditTransactionController`を純粋層として追加し、
軸3+平面3の解析ヒット、スクリーン一定スケール、円柱+円錐+クアッドのline/solid overlay、
world/local軸、軸/平面拘束をテストで固定した。World `+X`はスクリーン方向へ再マップせず、
ゲーム本体と同じ実行時行列では既定視点の画面左へ写る。調査・決定は
[[ADR-0042-Scene-Editor-Gizmo-Axis-Handedness]]に記録した。

ドラッグは`SceneRenderer.UpdateObjectTransform`で定数だけをpreviewし、同一Sceneの再解析snapshotは
最新1件だけ保留する。MouseUpでは最新CompilationからModel/Root/SourceTextを同一世代で再取得し、
安定`EditorNodeId`で`SetTransform(Position)`を適用する。live textが解決直後に変わった場合だけ
最新世代で1回再試行し、対象消滅・解析エラー・2回目競合は自動上書きせず元previewへ戻す。
成功時は`SceneTextBufferWriter`が全TextChangeを1つの`ITextUndoTransaction`へまとめる。
Position以外のQuaternion/Scaleリテラルは、値が正規化可能でもMoveでは書き換えない。

純粋層とRoslyn変換は**Matches**。`SceneHwndHost`、D3D12 overlay、RDT/live buffer、Visual Studio Undoの
製品配線は実COM/HWND/GPUを要求してユニットテスト対象外なので、Observedは実機確認まで**Unknown**。

**実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)**:

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | オブジェクトを選択してW→X/Y/Z軸とXY/YZ/ZX平面ハンドルが常に手前へ表示され、hover/active色が変わる | overlay PSO、深度無効、ツール切替 |
| 2 | 既定視点でワールド+X(赤)が画面左へ伸び、正方向へドラッグすると`PositionX`相当が増える | ADR-0042の軸/Camera規約 |
| 3 | 軸ハンドルは1軸、平面ハンドルは2軸だけ移動し、Perspective/Orthographicで見かけサイズが一定 | hit、拘束ソルバ、screen scale |
| 4 | ドラッグ中は滑らかにpreviewし、C#ソースはMouseUpまで変わらない | preview/commit分離 |
| 5 | MouseUp後、対象Positionの変更されたリテラルだけに差分が出て、コメント・書式・Rotation/Scaleは変わらない | Roslyn最小差分+Position mask |
| 6 | 1回のドラッグ後にCtrl+Zを1回→コードとScene表示が同時に元へ戻る | 1ドラッグ=1 text Undo+解析ループ |
| 7 | ドラッグ中にEsc、またはpointer captureを失う→previewが元へ戻り、コード/Undo履歴は変わらない | Cancel/競合安全性 |
| 8 | ドラッグ中に対象ノードを外部編集で削除→MouseUpで上書きせず破棄され、ツール内とVSステータスバーに理由が出る | リベース失敗通知 |
| 9 | 複数選択をMove→各選択rootへ同じworld差分が1 Undoで反映される | batch atomicity・複数選択 |
| 10 | コードエディタのTab/Backspace/Deleteが引き続き効き、RMB Navigation中のWはMove切替でなく前進になる | フォーカス/入力優先順位の非回帰 |

Phase 7の検証結果は本書末尾のPhase 7節へ記録した。

### 後続Task C(2026-08-16)

```text
dotnet build CSharpGameEngine.slnx -c Debug --no-restore
  → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restore
  → 全8テストプロジェクト824件成功・失敗0
    (Task C前808 + 新規16。VSExtension.Testsは332件)
```

`SceneInputStateMachine`へ`MarqueeSelecting`を追加し、空間開始・4px閾値・Down時Ctrl固定・
オブジェクト開始からの途中切替禁止・capture loss/Escキャンセルを純粋テストで固定した。
`ScenePicker.PickMarquee`は投影済みメッシュ境界との包含的交差を描画順・NodePath重複なしで返す。
`MarqueeSelectionPolicy`は通常置換とCtrl加算をWPFから分離し、`MarqueeOverlay`は既存の
triangle/line PSOへMoveギズモと同時投入する。純粋層は**Matches**、HWND/D3D12/WPF配線の
Observedは次表の実機確認まで**Unknown**。

**実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)**:

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | 空間から複数オブジェクトを囲むLMBドラッグ→矩形が表示され、交差した全対象だけが選択される | marquee遷移・overlay・矩形pick・置換発行 |
| 2 | 既存選択後、Ctrl+空間ドラッグで別オブジェクトを囲む→既存を解除せず追加される | Ctrl加算・Down時修飾固定 |
| 3 | オブジェクト上からLMB Downして大きくドラッグ→矩形へ切り替わらず、UpでDown時対象だけが選択される | `PendingSelect`既存経路と途中切替禁止 |
| 4 | Task C操作後もコードエディタのTab/Backspace/Deleteが効く | HWND/WPFフォーカスの非回帰 |

### Phase 4(2026-08-16)

```text
dotnet build CSharpGameEngine.slnx -c Debug
  → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restore
  → 全8テストプロジェクト838件成功・失敗0
    (Phase 4前824 + 新規14。VSExtension.Testsは346件)
dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj \
  --filter "FullyQualifiedName~TransformGizmoTests|FullyQualifiedName~EditTransactionControllerTests|FullyQualifiedName~SceneSourceEditorTests|FullyQualifiedName~SceneTransformMathTests" --no-restore
  → 39件成功・失敗0
```

`TransformGizmo`をpartial拡張し、Phase 3の`MoveGizmoFrame`とline/triangle overlayを共有したまま、
軸リング3+viewリングのRotate、軸3+軸端/中心立方体のScale、解析hit、リング面角度/軸距離比の
拘束ソルバを追加した。W/E/RはIdle時だけMove/Rotate/Scaleを選び、RMB Fly中のW/Eは従来どおり
Navigationへ渡す。上部ComboBoxのWorld/Localは`SceneViewState`経由で既存設定へ永続化する。

CtrlスナップはMove=1.0単位、Rotate=15°、Scale=0.1倍率(最小0.1)。複数選択はprimary位置を共有Pivot、
primary回転をLocal基底とし、selection rootと子孫へ同じworld deltaをpreviewする。commit maskはMove=
Position、Rotate=Position+Rotation、Scale=全TRSで、行列分解により更新しうるメンバーが式の場合は
該当Transform操作をdisabled表示かつ非hitにする。

追加テストは、複数の既存initializer assignmentを順に書き換えると、最初の置換後に保持していた古い
Roslyn node参照が次の`SeparatedSyntaxList.Replace`に存在せず例外になる既存不具合を検出した。
assignment名からindexを保持し、置換直前に現在のlistからnodeを取り直す方式へ修正した。
純粋層とRoslyn変換は**MatchesWithDocumentedExtension**(仕様一致+既存複数メンバー置換不具合の修正)。
HWND/D3D12/WPF/Visual Studio UndoのObservedは次表の実機確認まで**Unknown**。

**実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)**:

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | 選択後E→軸リング3本+白いviewリングを表示し、各リングのドラッグで対象がpreview/commitされる | Rotate overlay・hit・拘束・書き戻し |
| 2 | 選択後R→軸3本+軸端立方体+中心立方体を表示し、軸/均一Scaleをpreview/commitできる | Scale overlay・hit・拘束・書き戻し |
| 3 | 各ドラッグ中にCtrlを押下/解放し、Move=1.0、Rotate=15°、Scale=0.1へ吸着/解除する | 増分スナップの実入力配線 |
| 4 | World/Localセレクターで回転済みprimaryの軸が切り替わり、ツールウィンドウを開き直しても値が残る | 座標基底・設定永続化・ComboBox同期 |
| 5 | 複数選択をRotate/Scaleし、primary Pivot基準で全selection rootが動き、1操作がCtrl+Z 1回で戻る | shared Pivot・batch atomicity・Undo |
| 6 | Rotate/Scale中にEscまたはpointer capture喪失→previewが戻り、コード/Undo履歴が変わらない | 全Transform操作のcancel安全性 |
| 7 | 式で保持した必要TRSメンバーを持つ対象では該当ハンドルが淡色・非hitになる | 編集可能境界とソース保護 |
| 8 | コードエディタのTab/Backspace/Deleteが効き、RMB Fly中のEはRotate切替でなく上昇になる | フォーカス/入力優先順位の非回帰 |

### Phase 5(2026-08-16)

    dotnet build CSharpGameEngine.slnx -c Debug --no-restore
      → 成功、警告0/エラー0
    dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj --no-restore
      → 362件成功・失敗0
    dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restore
      → 全8テストプロジェクト854件成功・失敗0
        (Phase 4前838 + 新規16。VSExtension.Testsは362件)

Alt+LMBはhandle hit時だけDuplicateをOrbitより優先する。選択rootとrender descendantは
予測した兄弟末尾NodePathを持つ一時renderer objectとして追加し、元objectは動かさない。
MouseUpではDuplicateNodeOperationがclone trivia、X (n)形式の一意Name、最終local Transformを
まとめてChildren末尾へ挿入する。複数選択は同一親内の要求順を保ち、全予測pathが再解析で解決して
から複製rootへSelectionを移すため、複製とtransformは1回のlive text Undoになる。

Shift Follow Viewはdrag開始cameraに対するtotal deltaでMove/Rotateを再計算し、Scaleは追従しない。
Vは物理8px内の最近投影頂点へPositionだけを、Nは最近三角形交点へPositionを合わせprimaryの
ローカル+Yを幾何法線へ整列する。両queryはdrag対象rootと全子孫を除外し、成功優先順位はN→V→Ctrl。
Esc/capture loss/commit失敗では一時object、transform、cameraを開始状態へ戻す。

テスト追加16件はAlt優先状態遷移、頂点/面query、自己除外、Follow View復元、surface法線整列、
duplicate commit/operation変換、path予測/descendant再マップ/選択解決待ち、trivia/一意Name/
root拒否/同一親batch順を固定した。純粋層とRoslyn変換は**MatchesWithDocumentedExtension**。
HWND/D3D12/WPF/live buffer/Visual Studio UndoのObservedは次表の実機確認まで**Unknown**。

**実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)**:

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | Move handleをAlt+dragし、元が静止したまま複製previewだけが動き、非handle Alt+LMBはOrbitする | 入力優先順位・一時renderer object |
| 2 | Alt+Rotate/Scaleを確定し、コメント/書式を保ったX (n)名の兄弟が末尾へ増え、複製側が選択される | clone trivia・全Transform・path選択復元 |
| 3 | 複数選択Alt+drag後にCtrl+Zを1回実行し、全複製とTransformが同時に戻る | batch atomicity・1 text Undo |
| 4 | Duplicate/Follow View中にEscまたはcapture喪失し、複製表示・元Transform・cameraが全て戻る | cancel安全性 |
| 5 | Shift+Move/Rotateでcameraが対象に追従し、Shift再押下で追いつく一方、Shift+Scaleでは追従しない | total-delta Follow View |
| 6 | Move中Vで8px内の他object頂点へ吸着し、drag対象自身/子孫には吸着しない | VertexSnap query・自己除外 |
| 7 | Move中Nで他object面へ吸着し、primaryのローカル+Yが幾何法線へ揃い、自身/子孫を無視する | SurfaceSnap・Position+Rotation mask |
| 8 | N/V/Ctrlを併用してN→V→Ctrlの順にfallbackし、コードエディタのTab/Backspace/Deleteも効く | modifier優先順位・フォーカス非回帰 |

### Phase 6(2026-08-16)

    dotnet build CSharpGameEngine.slnx -c Debug --no-restore
      → 成功、警告0/エラー0
    dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj --no-restore
      → 381件成功・失敗0
    dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restore
      → 全8テストプロジェクト873件成功・失敗0
        (Phase 5後854 + 新規19。VSExtension.Testsは381件)

CompoundGizmo.Composeはactive Transform overlayと対象ComponentのProperty overlayを同時表示する。
CameraはPerspectiveでFOV/Near/Far、OrthographicでOrthographicSize/Near/Farを切り替える。
現行L9のDirectional/Point/Spot LightはADR-0043のFQN BindingでDirection/Range/InnerCone/
OuterConeを構成し、RectLightは未登録のまま。Alt押下中はProperty hitを除外するため非handleと同様に
Orbitし、Transform handleのAlt DuplicateはPhase 5どおり維持する。Ctrlは角度5°、方向15°、
距離1.0(Nearは0.1)へsnapし、ShiftはPropertyへ作用しない。

EditTransactionController.BeginDirectはProperty operation列にもsnapshot保留・cancel・1 Undoの
既存経路を再利用する。SetComponentMemberOperationは型に宣言済みだがinitializerで省略された
Memberを追記できる。AddComponentOperation/RemoveComponentOperationは
ComponentsListLocation/SceneComponent.Locationだけを編集し、既存triviaと他Componentを保持する。

追加19テストは4種Binding+Rect除外、Perspective/Orthographic Frustum、Compound合成、3ライト構成、
modifier表、FOV snap、direct transaction、省略Member追記、Component追加/削除を固定した。
純粋層とRoslyn変換は**MatchesWithDocumentedExtension**。HWND/D3D12/WPF/live buffer/
Visual Studio UndoのObservedは次表の実機確認まで**Unknown**。

**実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)**:

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | Camera選択時、active TransformとFrustumが同時表示され、PerspectiveでFOV/Near/Farをdragできる | Compound合成・Frustum overlay/hit |
| 2 | Orthographic CameraでFOV handleが消え、OrthographicSize/Near/Farへ切り替わる | 投影別構成 |
| 3 | Directional/Point/Spot LightでDirection/Range/Cone handleをdragし、C#の対応Memberだけが変わる | ADR-0043 Binding・Property source commit |
| 4 | 省略されたCamera既定Memberをdragし、initializerへ新しいassignmentが追加される | 省略Member追記 |
| 5 | Property drag中Ctrlを押下/解放して単位snapが切り替わり、Shiftは視点追従せず、Alt+PropertyはOrbitする | 仕様§11 modifier表 |
| 6 | Property dragをMouseUp後Ctrl+Z 1回で戻し、Esc/capture lossではsource/Undoを変更しない | direct transaction・1 Undo・cancel |
| 7 | 式で保持したPropertyは淡色・非hitで、RectLight型を宣言してもPropertyGizmoが出ない | 編集可能境界・Rect除外 |
| 8 | コードエディタのTab/Backspace/DeleteとPhase 5のAlt+Transform Duplicateが引き続き機能する | 入力優先順位の非回帰 |

### Phase 7(2026-08-16)

    dotnet build CSharpGameEngine.slnx -c Debug --no-restore
      → 成功、警告0/エラー0
    dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj --no-restore
      → 389件成功・失敗0
    dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restore
      → 全8テストプロジェクト881件成功・失敗0
        (Phase 6後873 + 新規8。VSExtension.Testsは389件)

`SceneIncrementalUpdatePolicy`は`SceneDiff`を`None/Incremental/Rebuild`へ分類する。
同一Sceneかつ直前表示世代と連続するTransform/非mesh Member変更では
`SceneRenderIncrementalUpdater`が全親子world chainを再計算し、既存`SimpleMesh`と
`SceneRenderer`を再利用する。mesh、Component追加/削除、Node構造変更、表示世代の飛びは
従来のlatest-only非同期rebuildへフォールバックする。

`FrameResourceTracker`と`SceneRenderer`はcommand allocator、object定数、overlay定数/
頂点領域を2 slot化した。`RenderFrame`末尾の全体`WaitForGpuIdle`は除去し、現在の
back-buffer slotに記録されたfenceが未完了の場合だけ待機する。resize、一時object資源破棄、disposeは
資源寿命を守るため全体待機を維持する。

`SceneTransformChange.OriginalLocal`と
`SetComponentMemberOperation.OriginalValueExpression`を開始値として保持し、
`SceneEditConflictMerger`がPositionの加算差分、RotationのQuaternion差分、Scaleの倍率差分、
Propertyの数値差分をcommit時点の最新値へ合成する。対象消失、Component消失、外部変更後の非editable式、
非有限結果は自動上書きせず失敗通知へ戻す。Escは`SceneNavState`の全7状態について
Idle終端、capture解放、Fly key消去、選択/commit非発火をテストで固定した。

追加8テストは増分/rebuild分類、mesh再利用と全階層world再計算、frame slot fence、Transform/Property
競合差分、Esc全状態を固定した。純粋層とRoslyn変換は**Matches**。HWND/D3D12/live buffer/
Visual Studio UndoのObservedは次表の実機確認まで**Unknown**。

**実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)**:

| # | 確認対象 | 何が証明されるか |
|---|---|---|
| 1 | TransformまたはCamera/Light Memberを外部編集し、視点・選択を維持したまま表示が更新される | SceneDiff増分更新・Renderer再利用 |
| 2 | Primitive/StaticMeshまたはNode構造を変更し、最新形状・階層で再表示される | rebuild分類・latest-only fallback |
| 3 | 親Transformを変更し、子孫が新しいworld位置へ追従する | 全階層world chain再計算 |
| 4 | 連続Navigation/drag中にD3D12警告やフレームごとのGPU完全待機由来の停止が発生しない | frames-in-flight・slot fence |
| 5 | drag中に同じTransform/Propertyをコード側で変更してMouseUpし、外部値へdrag差分だけが合成される | 開始値ベース競合マージ |
| 6 | Fly/Pan/Orbit/選択待ち/矩形/Gizmo/Propertyの各一時操作をEscで中止し、source/Undoを変更しない | Esc全状態終端・commit中非取消 |

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
- **ドラッグ&ドロップによるアセット配置**(旧ADR-0022 / 台帳F-L8-007): Phase 0 で`.scene`書き込み
  経路ごと削除した(Phase 9以降は恒久エラーで機能的損失はゼロ)。`SceneEditOperation`ベースでの
  再実装はヒエラルキー編集マイルストーンへ。`SceneNodeAdder.GetRelativePath`(net472用
  `Path.GetRelativePath`ポリフィル)も他の消費者が無いため一緒に落とした — 必要になったら書き直す。
- **シェイプパレット(プリミティブのクリック配置)**(旧ADR-0021 / 台帳F-L8-006): 同じ理由で
  Phase 0 で全面削除。プリミティブ生成そのものは`tools/CSharpGameEngine.Primitive.Cli`として存続し、
  Scene デザイナーの`PrimitiveMesh`描画(`SceneModelRenderObjectBuilder.ResolveViaBridge`)が
  利用している。

本マイルストーン自体のスコープ外(仕様・設計由来):

- `AddNode`/`RemoveNode`/`ReparentNode`/`ReorderNode`/`AssignAsset`操作(ヒエラルキー編集
  マイルストーンへ)。
- Unity/Unreal/Blender/Mayaナビゲーションプリセットの具体値充填(`Default`/`Custom`の器のみ)。
- カメラ姿勢の`SceneKey`別永続化(セッション内のみ)。
- Rect/Area Light(語彙が存在しない、仕様§10.4)。
- レイキャストの加速構造(必要になったら別ADR)。

## 残課題

- **R1: 深度精度**。Phase 0 でTransformが正しく合成された結果、`BoundsRadius`が数単位から実ステージ
  規模へ跳ね上がり、`far = Radius*20`も比例して伸びる。near 0.05 のままだとnear/far比が悪化し、
  `Camera.CreateFirstPerson`のコメントが「M0004で壁一面のZ-fightingを起こした」と記録している領域に
  入る。Sky domeは`CullNone`の巨大シェルなので特に出やすい。実機で出た場合の対処は
  `SceneCameraFraming.NearPlane`定数を0.5〜1.0へ上げること(視点は数十単位先なので視覚的損失ゼロ、
  ユニットテスト済みヘルパー内の定数1つ)。**Phase 0 は「直ったが汚い」をTransform修正のせいだと
  誤診しないため、この確認を検証項目8として明示している。**
  **(2026-08-16 解消 — [[M0030-SceneView-Gizmo-Transform-Editing]] T2)** 定数を上げる代わりに
  `Near = max(0.05, Far/10000)`のfar連動式(`SceneCameraFraming.ComputeNearPlane`)へ変更した。
  実ステージ規模では near が 0.8〜1.0 になり上記の推奨レンジに一致し、小さいシーンでは 0.05 の
  ままなので手前が切れない。併せて`SceneViewState`側にあった`NearPlane=0.05f`の**二重定義を削除**した
  (ドラッグ中に実際に使われるのは`SceneViewState.GetProjection`のほうで、定数を分けたままだと
  片方だけ直る事故になる)。正射投影は深度が線形なので比率調整の対象外。回帰テストは
  `SceneCameraFramingTests`/`SceneViewStateTests`に8件。**実機での目視確認は未実施のまま。**
- **(2026-08 解消)** アスペクト比 < 1(縦長ウィンドウ)では水平FOVが縦より狭くなり境界球が横に
  はみ出す既知の逸脱。`SceneCameraFraming.ComputeEffectiveFovRadians`として
  `min(FovY, 2*atan(tan(FovY/2)*aspect))`を実装し、距離計算にこれを使うようにした(投影行列には
  従来どおり縦FOVを渡す)。既存の behavioral テストが`±UnitX`をループしているのにNDCのYだけを
  見ていて破綻を素通りさせていたため、縦横両方+複数アスペクトを回す形に拡張してから直した
  (aspect 0.75 でも`NDC x=1.011734`と溢れていた)。詳細はConformance.md
  「追記(2026-08): 縦長ウィンドウのFOV補正と Tools>Options ページの新設」参照。
- **(2026-08-16 解消)** ライトComponentの所在はADR-0043で現行L9型の完全修飾名Binding登録を
  採用した。エンジン標準化はRendererハンドル所有の再設計を伴う別ADRへ送り、RectLightは対象外。
- 実機VS Experimental Instanceでの目視確認(M0019から引き継いだPhase 9表示系の確認を含む)は
  Phase 0 完了基準に統合したが、**まだ未実施**(上記検証節の10項目)。
- **(2026-08 解消)** 台帳F-L8-009のStatus誤り: `Removed`へ修正した。併せて同じADR-0031系列で
  同様に`Implemented`のまま残っていたF-L9-007(`UserGameProject/Scene1.cs`)とF-L7-014
  (`CSharpGameEngine.SceneAuthoring.Cli`)も`Removed`へ修正した。修正時にファイルの有無を実際に
  確認したところ、**この残課題の記述「5ファイルとテスト3件は全て削除済み」は不正確**で、
  `GizmoMeshFactory.cs`とそのテストは存続しており現行の`SceneModelRenderObjectBuilder`
  (ライブRoslyn描画経路)のフォールバックとして使われていた。台帳側にその旨を明記した。
- **(2026-08 解消)** vsctの死んだGroup宣言(0x1023): `SceneDefinitionContextMenuGroup`のGroup宣言と
  IDSymbolを削除し、退役コメントだけを残した(挙動不変、`VSCTCompile`成功を確認)。
- **(2026-08 解消)** Phase 2実機検証で判明した3件を修正した(詳細はConformance.md
  「2026-08 M0020 Phase 2 実機検証: A/Dの左右・複数選択の不可視・プリエンプト不発」参照):
  A/Dの左右が逆だった件、複数選択のハイライトがprimary 1件のみだった件、キャプチャ再取得で
  プリエンプトが不発だった件。
- **要注意(誤診の記録)**: A/Dの調査中に「基底は左手系なのに行列が右手系」という食い違いを
  根本原因と判断してView/Projectionを左手系へ組み替えたが、**ゲーム本体も同じ右手系**
  (`Camera.BuildViewMatrix`等)であり、シーンデザイナーだけがゲームと左右反転する改悪だったため
  差し戻した。`GetFlatRight`が画面の「左」へ写るのはエンジン全体の規約であり、A/Dと`ApplyPan`が
  符号を反転させているのはその正しい帰結。テストとコメントで維持を固定済み。
- **Phase 3着手時に判断が必要**: 上記の「ADR-0025はUnity=左手系と宣言、行列は右手系」という
  エンジン全体の不整合は未解決。**Moveギズモは+Xハンドルが画面左を指すことになる**ため、
  Phase 3では別ADRとして扱う必要がある。
- **(2026-08 解消)** 上記の仕様矛盾(縦方向)は、一時的にRMB Look(X/Y)を両方既定で反転する形に
  変更した後、実機確認を経てユーザーが反転しない側(`false`)を最終的な既定として維持することに
  決め、解消した。`SceneEditorSettings.DefaultInvertLookX`/`DefaultInvertLookY`は最終的に
  `false`のまま(詳細はConformance.md「追記(2026-08): RMB Look(X/Y)の既定を反転側へ一時変更 →
  実機確認後に反転しない側へ確定」参照)。
- **(2026-08 解消)** 上記のオプション画面(「Phase 3以降でオプションUIを作る際の実装対象として
  残す」としていたもの)を実装した。Tools>Options > C# Game Engine > Scene Editor に
  `InvertLookX`/`InvertLookY`/`InvertOrbitX`/`InvertOrbitY`/`NavigationSpeedMultiplier`の
  5項目を公開する(`SceneEditorOptionsPage`、このVSIX初の`DialogPage`系ページ)。残る4項目を
  公開しない理由と、「Scene Viewを開いたまま変更すると破棄時に上書き消滅する」問題への対処は
  Conformance.mdの同追記を参照。`NavigationPreset`はプリセット値の充填(このマイルストーンの
  スコープ外)が済んでから公開する。実機確認でコントロール寿命バグ(チェックしても保存されない
  経路)が見つかり修正済み(詳細はConformance.md「追記(2026-08): オプション画面のコントロール
  寿命バグ修正、Orbit反転設定、ピクセル完全一致ドラッグ」参照)。
- **(2026-08 追加)** Orbit(Alt+LMB)の反転設定(`InvertOrbitX`/`InvertOrbitY`)を追加した。
  これまで`ApplyOrbit`は反転設定を持たずfalse,false固定だった。Look/Orbitで独立して反転できる
  (前段の追記に同じ)。
- **(2026-08 追加)** RMB Look / Alt+LMB Orbit の「クリックした点(方向)がドラッグ中カーソル先端に
  完全一致し続ける」ピクセル完全一致ドラッグを追加した。Lookは厳密な閉形式、Orbitはカメラ位置
  自体が動くため閉形式が存在せず減衰付きNewton法(Levenberg–Marquardt)によるベストエフォート解
  (解の枝が折り返す区間では一時的に完全一致を維持できない、ユーザー承認済みの構造的な制約)。
  反転設定がONの間は従来のdelta*sensitivity経路のまま(反転しながらの完全一致は定義上不可能)。
  詳細はConformance.mdの同追記を参照。
- **(2026-08 追加)** ソリューションエクスプローラーの単一項目右クリックにも「シーン デザイナーで
  開く」を追加した(従来はコードエディタの右クリックのみ)。`SceneWorkspaceService`にアクティブ
  文書とは独立した第2解析スロットを追加し、コードエディタと同じくシーン名を列挙する体験にした
  (詳細はConformance.md「追記(2026-08): ソリューションエクスプローラーの右クリックにも
  『シーン デザイナーで開く』を追加」参照)。
  **(2026-08-16 修正)** 未表示ソースからの起動でもPin済みSceneのProjectカタログを使い、
  カタログ未取得で3D表示を空状態へ戻さない。
- **(2026-08-16 解消)** Hierarchyはスタイル拡張で複数選択を表示し、Ctrlトグル・Shift表示順範囲選択・primary/アンカーを`NodePath`基準で復元する。単一`NodePath`通知は互換経路として残すが、Hierarchy起点の集合は追加ペイロードで保持する。
- **(2026-08-16 追加)** Hierarchy起点だけが、既に開いているC#文書の`new Node { ... }` Spanを非選択背景マークする。通常操作はコードエディタを開かずフォーカスも奪わない。ダブルクリック/Enterだけがprimaryの`new`先頭へ移動する。Experimental Instanceの確認項目として、連続選択・親子/複数マーク・未表示文書非オープンを残す。

- **新規に判明した別タスク規模の負債**: `Docs/Implementation-Status.md`に`UserGameProject`への参照が
  62箇所ある。`UserGameProject`はM0019 Phase 5(`81c462e`)で撤去され現行は`UserEcsProject`なので、
  上記3件と同種の陳腐化が広範に残っている可能性が高い。台帳全体の棚卸しは本マイルストーンの
  スコープ外とし、別タスクとして起こす。

関連: [[ADR-0041-Scene-Editor-Interaction-Architecture]]、
[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]、[[M0019-CSharp-Scene-DSL]]、
`Docs/シーンエディタの仕様.md`、`Docs/Specifications/Scene-Editor-Design.md`

## 2026-08-16 後続実装: VS らしい Hierarchy と Component アイコン

- **実装・実機確認済み**: Hierarchy は Visual Studio の公開 TreeView テーマを使い、
  キーボードフォーカスの有無で active / inactive selection を切り替える。複数選択は全行を表示し、
  primary だけに左アクセントを表示する。子がある Node には `ExpandRight` / `ExpandDown` の展開アイコンを
  表示し、そのクリックは選択・primary・ソース同期を変更しない。
- **実装・実機確認済み**: `SceneComponentIconAttribute` は struct にプロジェクト相対
  SVG / PNG を関連付ける。Hierarchy と C# glyph margin は同じ `SceneVisualIcon` renderer を使い、無効・
  欠落・メタデータ由来の component は既定 icon にフォールバックする。候補 picker は既定 icon と
  プロジェクト配下の SVG / PNG だけを表示し、絶対パス・`..`・生成出力は受け付けない。lock overlay は
  将来の編集 lock 用予約で、今回は描画しない。
- **実装・実機確認済み**: Hierarchy 選択のソース背景は VS 標準
  `ITextMarkerProviderFactory` の `SimpleTagger<TextMarkerTag>` で描画する。通常クリックは既に開いた
  文書だけをマークし、コードエディタのフォーカス・キャレット・選択を変えない。double-click / Enter
  だけが primary の `new Node` 先頭へ移動する。選択解除、Scene 切替、解析失敗、buffer 破棄では拡張が
  所有する marker だけを解除する。
- **実機確認済み（Experimental Instance）**: Hierarchy と Solution Explorer の focus 表示、展開 icon の
  クリック、SVG / PNG icon の属性編集と Undo、複数/親子 marker、通常選択時の非フォーカス、Enter /
  double-click 時だけの移動、未表示ソースを開かないことを確認した。

## 2026-08-16 後続実装: Hierarchy構造編集とRuntime/User component icon

- `HierarchyToolWindowControl`へContextMenu、F2/Delete、DragDropを追加し、AddChild/Rename/Remove/Reparent/ReorderをSceneSourceEditorのRoslyn操作へ接続した。選択された複数のルートは1つのVisual Studio Undo単位で適用し、子を含む削除だけ確認する。
- RuntimeのTransform/StaticMesh/PrimitiveMesh/CameraとUserEcsProjectのScene-addable structに`SceneComponentIconAttribute`を付与した。SVGを主形式、PNGを互換形式とし、画像の所有元は宣言プロジェクトに限定する。
- Hierarchy/glyph marginは`SceneVisualIcon`とVS TreeViewのFontSize連動converterを共有し、Solution Explorerの文字サイズ設定に追従する。
- Debug/ReleaseでVS extension 431件、Entities 42件、component icon 20件、SceneSourceEditor 22件を確認済み。Experimental Instanceでの構造編集操作は未再確認。
