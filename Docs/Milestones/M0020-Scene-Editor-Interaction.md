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

Phase 3以降の検証結果は、各Phase完了時に本節へ追記する。

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
- アスペクト比 < 1(縦長ウィンドウ)では水平FOVが縦より狭くなり境界球が横にはみ出す。これはPhase 0
  以前と同じ挙動なので踏襲した既知の逸脱。Phase 1で`min(FovY, 2*atan(tan(FovY/2)*aspect))`を検討する。
- ライトComponentの所在(エンジン標準 vs L9)の決定はPhase 6着手時の小ADRへ先送りしている。
- 実機VS Experimental Instanceでの目視確認(M0019から引き継いだPhase 9表示系の確認を含む)は
  Phase 0 完了基準に統合したが、**まだ未実施**(上記検証節の10項目)。
- **台帳F-L8-009のStatus誤り**(Phase 0 のスコープ外): 挙げている5ファイルとテスト3件はM0019
  Phase 5の`[SceneDefinition]`廃止で全て削除済みなのに`Status: Implemented`のまま。削除済みコードを
  実装済みと記述している状態であり、今回清算した種類の誤りと同種。別タスクで`Removed`へ直す。
- **vsctの死んだGroup宣言(0x1023)**(同スコープ外): `SceneDefinitionContextMenuGroup`は退役
  コメントがあるのにGroup宣言とIDSymbolが両方生きている(親付けされたButtonはゼロ)。挙動不変。

関連: [[ADR-0041-Scene-Editor-Interaction-Architecture]]、
[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]、[[M0019-CSharp-Scene-DSL]]、
`Docs/シーンエディタの仕様.md`、`Docs/Specifications/Scene-Editor-Design.md`
