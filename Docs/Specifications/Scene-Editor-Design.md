# Scene Editor詳細設計(インタラクション基盤)

本書は`Docs/シーンエディタの仕様.md`(要求仕様)を現行アーキテクチャへ写像したクラスレベルの
詳細設計である。**全節の内容は計画済み(未実装)**。意思決定の根拠は
[[ADR-0041-Scene-Editor-Interaction-Architecture]]、実装フェーズと完了条件は
[[M0020-Scene-Editor-Interaction]]、既存基盤の決定は
[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]を参照。実装が本書と食い違った場合は
実装・テストを正とし、本書を追記修正する(ドキュメント真実性の序列に従う)。

```text
Docs/シーンエディタの仕様.md   … 要求仕様(何をしたいか)
        ↓
ADR-0041                        … 意思決定(なぜこの構造か)
        ↓
本書(Scene-Editor-Design.md)  … 詳細設計(どのクラスがどう動くか)
        ↓
M0020                           … 実装フェーズ計画と完了条件
```

## 1. 全体アーキテクチャ

既存パイプライン(実装済み・ADR-0040)へ、新規コンポーネント(◆)を差し込む。

```text
                                [既存] VisualStudioWorkspace.WorkspaceChanged
                                   ↓ 250msデバウンス・バックグラウンド解析
                                [既存] SceneWorkspaceService → SceneModel
                                   ↓
                                [既存] SceneEditorSessionService → SceneDocumentSnapshot
                                   ↓ PinnedSnapshotChanged
              ┌─────────── [既存] SceneDesignerToolWindowControl ─────────────┐
              │  [既存] SceneSelector  [◆] ツールバー(ActiveTool/Space/投影)     │
              │  ┌──────────── [既存] SceneHwndHost ─────────────────┐  │
              │  │ [◆] SceneViewState(カメラ・ツール状態。Renderer再生成を跨ぐ)   │  │
              │  │ [◆] SceneInputStateMachine(§5) ← WndProcマウス/WPFキー      │  │
              │  │ [◆] ScenePicker(§6) / [◆] GizmoSystem(§7)                │  │
              │  │ [◆] EditTransactionController(§8)                          │  │
              │  │ [既存] SceneRenderer(+[◆]SetCamera/UpdateObjectTransform/     │  │
              │  │        オーバーレイPSO。編集状態は持たない)                     │  │
              │  └───────────────────────────────────────┘  │
              └───────────────────────────────────────────┘
   コミット経路(§8): EditTransactionController
     → [既存] SceneSourceEditor.ApplyLatest → [既存] SceneTextBufferWriter(1 Undo)
     → VS TextBuffer → 上記の既存ループが表示を確定
   選択経路(§6): ScenePicker → [既存] SceneSelectionService(Origin=Viewport) ⇄ Hierarchy
```

スレッド規律はADR-0040決定事項7を継承する。入力・プレビュー・ギズモ判定はすべてUIスレッド
(`CompositionTarget.Rendering`ティックとWndProc/WPFイベント)で完結し、Roslynへの接触はコミット時の
`SceneSourceEditor`呼び出し(UIスレッドでの同期的なsyntax編集。解析はしない)に限る。

## 2. SceneViewState(§仕様2・3・20)

`SceneHwndHost`が所有し、`SceneRenderer`の破棄・再生成(再解析ごと)を跨いで生存する。
依存はSystem.Numericsのみ(ユニットテスト可能)。

| フィールド | 型 | 意味 | 永続化 |
|---|---|---|---|
| `FocusPosition` | Vector3 | Orbit/Dolly/FocusSelectionの基準点(仕様: Focus) | しない |
| `FocusDistance` | float | 視点とFocusの距離 | しない |
| `Yaw`/`Pitch` | float | 視線方位(Pitchは±89°でクランプ) | しない |
| `ProjectionMode` | enum | Perspective/Orthographic | する |
| `FovYRadians` | float | 透視投影の縦FOV(既定45°) | しない |
| `OrthographicSize` | float | 平行投影の表示半径 | しない |
| `ActiveTool` | enum | Select/Move/Rotate/Scale | する |
| `TransformSpace` | enum | World/Local | する |
| `NavigationPreset` | enum | Default/Custom(将来: Unity/Unreal/Blender/Maya) | する |
| `NavigationSpeedMultiplier` | float | Fly速度倍率(RMB+Wheelで変更) | する |
| `InvertLookX`/`InvertLookY` | bool | Look反転(仕様§4.1) | する |

不変条件: 視点位置は`FocusPosition - Forward(Yaw,Pitch) * FocusDistance`から導出する
(Orbit・Dollyが自然に成立する)。Fly(RMB)中は視点位置を直接動かし、終了時に
`FocusPosition = 視点位置 + Forward * FocusDistance`で焦点を再導出する。

一時修飾(`FollowView`/`Duplicate`/`VertexSnap`/`SurfaceSnap`/`FastNavigation`/
`PrecisionNavigation`)は本クラスに置かず、§5の状態機械の遷移中フラグとして表現する
(仕様§3「永続モードにしない」の構造的保証)。

## 3. カメラ数学(§仕様4)

数学はnet10.0の`src/CSharpGameEngine.Windows/Graphics/Camera.cs`(型は
`CSharpGameEngine.Graphics.D3D12.Camera`。Scene DSLのComponent `Runtime.Windows.Camera`とは別物)の
**式**をnet472側へ書き写した`SceneViewCamera`(仮称)に置く。行列規約は**現行`SceneRenderer`と同一**
(System.Numericsの行ベクトル規約、`Matrix4x4.CreateLookAt`/`CreatePerspectiveFieldOfView`、
MVP = `World * View * Projection`)。エンジン座標系との対応は
[[ADR-0026-Camera-Geometry-Ruf-Integration]]の既存整理に従い、本設計で新しい変換は導入しない。

**移植時の制約(2026-08 訂正、ADR-0041 決定事項1の訂正と対応)**: `Graphics/Camera.cs`は依存ゼロ
ではない — `[SceneFactory]`(`CSharpGameEngine.Authoring`)属性を持ち、`GetForward`/`GetFlatForward`/
`GetFlatRight`は`RufQuaternion`/`RufVector`/`Spatial<UnityCoordinateConvention>`(いずれもnet10.0)を
経由する。net472側では属性を落とし、`CameraTests`が固定している閉形式
`Forward = normalize(cosPitch*sinYaw, sinPitch, cosPitch*cosYaw)` /
`FlatRight = normalize(Cross(UnitY, FlatForward))`へインライン展開する。**`MathF`はnet472に存在
しない**ので`(float)Math.*`を使う(Phase 0 の`SceneCameraFraming`が既にこの規約で書かれている)。

初期フレーミング(Phase 0 で`SceneCameraFraming`として実装済み): 方向`normalize(1,0.75,1)`、距離
`Radius / sin(FovY/2) * 1.1`(`Radius`は真の境界球半径=中心から最遠頂点までの距離)、near `0.05`、
far `Radius*20`。`Camera.CreateBoundsFit`とはRadiusの定義(AABB半対角)とnear/farが異なる。

| 概念操作 | 計算 |
|---|---|
| `Look`(RMB+Mouse) | `Yaw += Δx * 感度 * (InvertLookX ? -1 : 1)`、`Pitch += Δy * 感度 * (InvertLookY ? -1 : 1)`。位置固定 |
| `Fly`(RMB+WASDQE) | `位置 += (前後A/D左右Q/E上下の合成方向) * 速度 * Δt`。速度 = 基準 × `NavigationSpeedMultiplier` × (Shift: ×4 / Ctrl: ×0.25) |
| `Pan`(MMB Drag) | 視線を変えずカメラの右・上ベクトル方向へ`FocusPosition`と視点を平行移動。移動量はNavigationScale比例 |
| `Orbit`(Alt+LMB) | `Yaw`/`Pitch`を回し、視点位置を導出式で再計算(`FocusPosition`固定) |
| `Dolly`(Wheel、透視) | `FocusDistance *= pow(0.9, ホイールノッチ)`(下限クランプ) |
| `Zoom`(Wheel、平行) | `OrthographicSize *= pow(0.9, ホイールノッチ)` |
| `AdjustNavigationSpeed`(RMB+Wheel) | `NavigationSpeedMultiplier *= pow(1.2, ノッチ)`(上下限クランプ) |
| `FocusSelection`(F) | 選択ノード群のワールド境界球へ`FocusPosition`=中心、`FocusDistance`=半径から算出 |

**NavigationScale**(仕様§2): Pan/Dolly/Flyの基準移動量は`FocusDistance`に比例させる。これにより
編集スケール(部屋規模〜都市規模)へ自動追従する。

unproject(§6で使用): `Inverse(View * Projection)`でNDC→ワールド。透視はニアプレーン点と視点から、
平行は方向固定のレイを作る。

## 4. 入力状態機械(§仕様5・12・21)

### 4.1 入力の入口

| 経路 | メッセージ/イベント | 変換先 |
|---|---|---|
| `RenderSurfaceWindow.WndProc`(追加) | `WM_LBUTTONDOWN/UP`、`WM_RBUTTONDOWN/UP`、`WM_MBUTTONDOWN/UP`、`WM_MOUSEMOVE` | `PointerEvent { Button, Position, Modifiers }`(**座標は物理ピクセル**) |
| WPF(`RenderHostGrid`) | `PreviewMouseWheel` | `PointerEvent`(ホイール。`WM_MOUSEWHEEL`は**フォーカスを持つHWND宛**なのでネイティブ子には構造的に届かない。DIP→`TransformToDevice`で物理ピクセルへ) |
| WPF(`RenderHostGrid`、`Focusable=true`) | `KeyDown`/`KeyUp`/`LostKeyboardFocus` | `KeyEvent { Key, IsDown }` / フォーカス喪失通知 |

**座標単位**: `WndProc`は物理ピクセル、WPFはDIP(1/96インチ)。スワップチェーン・ビューポート・
ピッキングレイはすべて物理ピクセルなので物理ピクセルを正本とし、WPF由来の座標は
`PresentationSource.FromVisual(...).CompositionTarget.TransformToDevice`で変換する(150%スケーリングで
1.5倍ずれる)。ホイール量は両経路とも`WHEEL_DELTA=120`単位なので変換不要。

**既存の初期値**: `SceneDesignerToolWindowControl`のコンストラクタは現在`RenderHostGrid.Focusable=false`
かつ`IsTabStop=false`を設定している(F5起動時のフォーカス奪取バグ対策、コミット`394ff04`)。Phase 1では
`Focusable`のみ`true`へ反転し、`IsTabStop=false`は維持する(Tabがここへ着地しないようにする)。
フォーカス移動はビューポート内のマウス押下時のみに限り、`OnLoaded`/`ShowSnapshot`等では行わない。

ビューポート内のボタン押下時、`SceneHwndHost`がWPFホストGridへ`Keyboard.Focus`する(VS標準の
ツールウィンドウアクティブ化)。ネイティブ子HWNDは`RenderSurfaceFocusPolicy`無改変のまま
Win32フォーカスを取らない。キーはフォーカス保持中のみ届き、`LostKeyboardFocus`で押下集合を
全クリアする(キー取りこぼしによる暴走防止)。修飾キーはイベント時点の`Keyboard.Modifiers`参照。

`SceneInputStateMachine`は`PointerEvent`/`KeyEvent`と現在の`SceneViewState`・ギズモヒット結果だけを
入力とする純粋クラスで、出力は概念操作コマンド(カメラ更新・選択発行・トランザクション開始等)の
列。WPF/Win32/VS型へは依存しない。

### 4.2 状態と遷移(仕様§12の優先順位の実現)

```text
Idle
 ├─ RMB Down                    → Flying(Look+WASDQE。以後LMB/MMB/ギズモを無視=優先度2)
 ├─ MMB Down                    → Panning
 ├─ Alt + LMB Down              → Orbiting
 ├─ LMB Down(ギズモにヒット)   → GizmoDragging(優先度3。§8のBeginTransaction)
 ├─ LMB Down(ヒットなし/対象) → PendingSelect(Up時に選択確定。Ctrl併用で複数選択トグル)
 └─ Wheel                       → Dolly/Zoom(状態遷移なし)
Flying / Panning / Orbiting
 └─ 該当ボタンUp                → Idle
GizmoDragging
 ├─ MouseMove                   → プレビュー更新(V/Nホールドでスナップクエリ切替、§6)
 ├─ LMB Up                      → CommitTransaction → Idle
 └─ Esc                         → CancelTransaction → Idle
```

- ドラッグ系状態への遷移で`SetCapture`、離脱で解放。
- `Esc`は現在の一時操作のキャンセル(仕様§12)。Idle時は何もしない。
- RMB保持中はW/E/Q/A/S/DをFly入力が占有する(仕様§5)。ツール切替キーはIdle時のみ有効:
  W=Move、E=Rotate、R=Scale、F=FocusSelection。
- コンテキストメニュー(優先度5)はM0020スコープ外だが、状態機械上は「RMB Down→移動なしでUp」を
  将来メニュー表示に割り当てられる形にしておく(Flyは移動発生時のみ確定)。

### 4.3 修飾キーの意味(仕様§6・11)

| 修飾 | ナビゲーション中 | TransformGizmoドラッグ中 | PropertyGizmoドラッグ中 |
|---|---|---|---|
| Shift | FastNavigation(×4) | FollowView(§7.4) | 原則なし |
| Ctrl | PrecisionNavigation(×0.25) | Grid/Incrementスナップ | 単位スナップ(角度・距離) |
| Alt | (Orbit開始の判定のみ) | Duplicate(ドラッグ開始時に複製) | 適用しない |
| V / N ホールド | ― | 頂点スナップ / 面スナップ | ― |

## 5. ピッキング(§仕様7)

`ScenePicker`(純粋クラス)は`SceneRenderObject`列(CPU側`SimpleMesh`+ワールド行列)に対して
次の3クエリを提供する。

1. **Pick(選択・ギズモ用)**: スクリーン座標→レイ(§3のunproject)→ 各オブジェクトの全三角形へ
   Möller–Trumbore交差 → 最近ヒットの`NodePath`と交点・法線を返す。加速構造なしの線形走査
   (現行シーン規模で十分。必要になったら別ADRで導入判断)。
2. **VertexSnapQuery**: レイ近傍のオブジェクトについて、頂点をスクリーン空間へ投影し、カーソルから
   スクリーン半径R(既定8px)以内の最近頂点のワールド座標を返す。Positionのみに作用し
   Rotationは変更しない(仕様§7.1)。
3. **SurfaceSnapQuery**: Pickと同じ交差で、ドラッグ中オブジェクト自身を除外した最近ヒットの
   交点+面法線を返す。Positionを交点へ、Rotationを法線整列へ
   (`SurfaceSnap.AlignRotationToNormal=false`で整列のみ無効化可、仕様§7.2)。

ギズモハンドルの判定はメッシュ交差ではなく解析形状で先に行う(§7.3)。ヒット優先順位:
アクティブドラッグ > ギズモハンドル > シーンオブジェクト(仕様§12)。

## 6. ギズモ(§仕様8〜11)

### 6.1 クラス構造(合成モデル)

```text
CompoundGizmo(選択ノードごとに構成を決定)
├─ TransformGizmo
│   ├─ MoveHandle    … 軸3(円柱+円錐の矢印)+ 平面3(クアッド)
│   ├─ RotateHandle  … 軸リング3 + ビュー平面リング
│   └─ ScaleHandle   … 軸3(円柱+立方体)+ 中心(均一スケール)
└─ PropertyGizmo(SceneComponentBinding(§9)が対象Component存在時のみ合成)
    └─ FrustumGizmo(Camera) … FrustumAngleHandle(FOV)/NearPlaneHandle/FarPlaneHandle/
                                OrthographicBoundsHandle(投影モードで切替)
```

表示するTransformGizmoは`ActiveTool`に対応する1種。PropertyGizmoは置き換えではなく**同時表示**
(仕様§8)。ライトギズモ(Range/Cone/Direction等)は語彙決定後にPropertyGizmo実装を追加する
(ADR-0041決定事項9、M0020 Phase 6)。

### 6.2 描画

- `SceneRenderer`に**オーバーレイPSO**を追加: 深度テスト無効・ライン+ソリッド両トポロジ・
  per-objectハンドル色(通常/ホバー/アクティブ)。既存`Selected`フラグと同じ定数バッファ方式。
- ハンドルメッシュは`GizmoMeshFactory`拡張の手続き生成(矢印・リング・クアッド・円錐・立方体)。
- スクリーン一定サイズ: ギズモのワールドスケール = `ハンドル位置とカメラの距離 * tan(FovY/2) * 定数`
  (Orthographicでは`OrthographicSize * 定数`)。
- 座標空間: `TransformSpace=World`では軸をワールド軸に、`Local`では対象ノードの回転を適用。

### 6.3 ハンドルヒット判定

解析形状で行う(§5参照): 軸=有限円柱(半径はスクリーン一定サイズから逆算)、リング=トーラス
近似(レイ・平面交差+半径帯)、平面クアッド=矩形交差。ヒットしたハンドルが拘束
(軸/平面/リング面)を決める。

### 6.4 ドラッグ解決(拘束ソルバ)

- 軸移動: レイと「軸を含みカメラへ最も正対する平面」の交点を軸へ射影し、開始点との差分ΔPを得る。
- 平面移動: レイと拘束平面の交点差分。
- 回転: レイとリング平面の交点を中心角へ変換し、開始角との差分ΔR。
- スケール: 軸射影距離の比。中心ハンドルはスクリーン距離比で均一スケール。
- Ctrlスナップ: ΔP→グリッド刻み(既定1.0/0.1)、ΔR→角度刻み(既定15°)、スケール→0.1刻み。
- Shift=FollowView(仕様§6.1): Moveでは`ΔP`を`SceneViewState.FocusPosition`と視点位置へも加算。
  Rotateでは同一Pivot周りで視点位置・Yaw/Pitchへ`ΔR`を合成。Scaleでは追従しない(既定)。
- Alt=Duplicate(仕様§6.2): ドラッグ開始時に`DuplicateNode`をプレビュー用の複製表示で開始し、
  MouseUpで「複製+Transform設定」を1トランザクションとしてコミットする。元ノードは無変更。

## 7. 編集トランザクション(§仕様16〜19)

`EditTransactionController`が1ユーザー操作=1トランザクションを管理する。

```text
BeginTransaction(MouseDown)
  捕捉: SceneKey / EditorNodeId(複数選択可) / 元Transform値 / snapshot.DocumentVersion
  以後、PinnedSnapshotChangedが届いても表示へは適用せず PendingSnapshot に保留(SetTarget保留)
Preview(Drag)
  SceneRenderer.UpdateObjectTransform(NodePath, Matrix4x4) … 定数バッファのみ更新
Commit(MouseUp)
  1. SceneEditOperation列を生成(§8)
  2. SceneSourceEditor.ApplyLatest(現Root, 最新snapshot, op) … Versionずれは1回リベース
  3. SceneTextBufferWriter.TryApply(buffer, before, after, "Move Cube" 等) … 1 ITextUndoTransaction
  4. PendingSnapshotを解放 → 通常ループが確定表示
Cancel(Esc / リベース失敗)
  プレビュー値を元値へ戻し、PendingSnapshotを解放。テキスト無変更(Undo履歴も無傷)。
  リベース失敗時はVSステータスバー/InfoBarで通知(自動上書きしない、仕様§19)
```

競合マトリクス(仕様§18・19):

| 状況 | 挙動 |
|---|---|
| ドラッグ中に外部編集(別値・別ノード) | 保留。MouseUpのリベースで統合される(EditorNodeIdはセッション安定) |
| ドラッグ中に外部編集(対象ノード消滅) | Commit時に`ApplyLatest`が失敗 → プレビュー破棄+通知 |
| ドラッグ中に同一メンバーが外部変更 | Commitはエディタ操作の値で上書き(最後の操作が勝つ。ドラッグ開始時点の値比較による差分統合はPhase 7の拡張候補) |
| 非ドラッグ時の外部編集 | 既存どおり即時反映(250msデバウンス) |

Undo/Redo(仕様§17): 追加実装なし。1コミット=1テキストUndo単位であり、Ctrl+Zでテキストが戻ると
既存の解析ループが表示・ヒエラルキー・選択根拠をすべて戻す。

## 8. SceneEditOperation拡張仕様

書き込みアンカーは解析済み`SceneNode`が保持する4つの`SceneSourceLocation`を使う。

| アンカー | 用途 |
|---|---|
| `NameValueLocation` | RenameNode(実装済み) |
| メンバー`SceneValue.Location`(RHSのみ) | SetComponentMember(実装済み)・SetTransformの既存メンバー書換 |
| `InitializerLocation`/`ComponentsListLocation` | メンバー・Component追記(SetTransformでメンバー欠落時、AddComponent) |
| `ChildrenListLocation` | DuplicateNodeの複製挿入(兄弟末尾) |

- **SetTransform**(Phase 3): 意味は「対象ノードのTransformを新TRSへ」。実装は
  `PositionX/Y/Z`等の各フラットfloatへの`SetComponentMember`束だが、宣言に該当メンバーが無い場合
  (例: `RotationX`未記述のノードを回転)にメンバー追記が必要になるため専用op。値の書式は
  既存記述を尊重し、新規追記は`0.5f`形式(InvariantCulture・`f`サフィックス)で統一する。
  親子合成(§9)下では、ワールド差分を`TransformMath.TryCalculateLocal`でローカルTRSへ戻して書く。
- **DuplicateNode**(Phase 5): 対象ノードの`ObjectCreationExpression`全文(trivia込み)を
  `ChildrenListLocation`の末尾へ複製挿入し、`Name`があれば`"X (1)"`形式で一意化する。複製直後に
  SetTransformを同一トランザクション内で適用する。
- **AddComponent/RemoveComponent**(Phase 6): `ComponentsListLocation`への挿入/`SceneComponent.
  Location`の削除。追加可能型は既存`SceneComponentCatalog.Build(Compilation)`で列挙する。
- 編集可否は既存`SceneValue.IsEditable`(Literal/EnumMember/Defaultのみtrue)に従う。式・
  `[SceneAssets]`参照が書かれたメンバーはギズモから書き換えない(該当軸のドラッグを無効化し、
  ハンドルを淡色表示する)。

## 9. Component認識(SceneComponentBinding)と語彙ギャップ

`SceneModelRenderObjectBuilder`のFQNハードコードを、宣言的なバインディング表
(`SceneComponentBindings`)へ置換した(**M0020 Phase 0 で実装済み**)。

| Component完全修飾名 | `SceneComponentRole` | エディタ上の意味 |
|---|---|---|
| `global::CSharpGameEngine.Runtime.Windows.Transform` | `LocalTransform` | ローカルTRS。**親子合成**: pre-order DFSで`world = local * parentWorld`を畳み込む(Phase 0 で「各ノードのローカル=ワールド」を廃止済み) |
| `global::CSharpGameEngine.Runtime.Windows.PrimitiveMesh` | `PrimitiveMesh` | プリミティブ描画 |
| `global::CSharpGameEngine.Runtime.Windows.StaticMesh` | `StaticMesh` | アセット描画 |
| `global::CSharpGameEngine.Runtime.Windows.Camera` | (Phase 6 で追加) | FrustumGizmo対象 |

合成式はランタイムの`TransformMath.ToWorld`(`TransformWorldResolverTests`が固定している規約)と
**同一だが、型は参照しない** — net472からnet10.0の型は参照できないため、式1つだけを写している
(`GizmoMeshFactory`と同じ複製方針、ADR-0041 代替案節)。Phase 3 の`SetTransform`が
`TryCalculateLocal`を要求した時点で共有方式を再検討する。`SceneNode.Children`は構文木由来の
ツリーなのでサイクルガードは不要(深さは解析器が`MaxRecursionDepth`で打ち切る)。

enumメンバー名は`Transform`ではなく`LocalTransform`にしてある(`Transform` struct型名との混同を避け、
「これはローカル、worldは合成結果」という区別を型名で表す)。**退役した`global::UserEcsProject.Transform`は
登録しない** — 互換のため両方登録するとバグを静かに塗り潰し、直ったかどうかが判定不能になる。

語彙ギャップ(ADR-0041決定事項9): ライト各種は`UserEcsProject`(L9)にのみ存在し、RectLightは
存在しない。ライトギズモはPhase 6着手時の小ADRで「エンジン標準化」か「L9型のBinding登録」かを
決定する。RectLightはスコープ外として記録のみ。

## 10. 永続化スキーマ(§仕様3・20)

`WritableSettingsStore`(`SettingsScope.UserSettings`)、コレクション`CSharpGameEngine\SceneEditor`。

| プロパティ | 型 | 既定値 |
|---|---|---|
| `ActiveTool` | string(enum名) | `Select` |
| `TransformSpace` | string | `World` |
| `ProjectionMode` | string | `Perspective` |
| `NavigationPreset` | string | `Default` |
| `NavigationSpeedMultiplier` | float(文字列格納) | `1.0` |
| `InvertLookX` / `InvertLookY` | bool | `false` |

保存はツールウィンドウ非表示時と値変更時(デバウンス)。カメラ姿勢・`FocusPosition`・選択・
ギズモ表示はセッション内のみ。**`SceneDeclaration`へSceneView状態を書くことは構造的に不可能**
(書き込み経路が`SceneEditOperation`列挙に限定されており、そこにSceneView状態を運ぶopが無い)。

## 11. テスト戦略

VS非依存の純粋層を最大化し、既存`RenderSurfaceFocusPolicyTests`と同じ形で固定する。

| 対象 | テスト内容 |
|---|---|
| `SceneViewCamera`(数学) | view/proj導出、unprojectの往復、Orbit/Fly/Pan/Dolly各式、NavigationScale比例、Invert反転、Pitchクランプ |
| `SceneInputStateMachine` | §4.2遷移表の全遷移、優先順位(RMB中のギズモ/選択無視)、Escキャンセル、フォーカス喪失時の押下集合クリア、修飾キー適用可否表 |
| `ScenePicker` | 三角形交差(表裏・最近ヒット)、頂点スナップの半径境界、面スナップの法線、Orthographicレイ |
| ギズモ拘束ソルバ | 軸/平面/リングの各拘束解決、Ctrl刻みスナップ、スクリーン一定スケール逆算 |
| `EditTransactionController` | Begin→Preview→Commitの操作列生成、Escで無変更、Versionずれのリベース委譲、対象消滅時の破棄+通知経路 |
| SetTransform操作生成 | メンバー既存時の値置換列、欠落時の追記、`TryCalculateLocal`経由のローカル化、`IsEditable=false`軸の除外 |
| 親子Transform合成 | `SceneModelRenderObjectBuilder`のDFS畳み込み(実解析済みSceneModelに対して) |
| 統合(既存パターン) | `SceneSourceEditorTests`系にDuplicateNode/AddComponentのtrivia保持テストを追加 |

実機VS Experimental Instanceの目視確認はM0020各Phaseの完了条件として定義する(スクリーンショットは
UIA `NativeWindowHandle`+`PrintWindow`方式、`SetForegroundWindow`不使用)。

## 12. 用語対応表(仕様§2・§22 → 実装)

| 仕様の識別子 | 実装 |
|---|---|
| `SceneView` / `SceneViewState` | `SceneHwndHost`(ビュー)/ 新規`SceneViewState` |
| `Focus` / `FocusPosition` / `FocusDistance` | `SceneViewState`の同名フィールド |
| `Navigation` / `NavigationScale` / `NavigationPreset` | 状態機械の概念操作 / `FocusDistance`比例則 / バインディングテーブル |
| `Look` `Fly` `Pan` `Orbit` `Dolly` `Zoom` | `SceneViewCamera`の各操作(§3表) |
| `TransformOperation`(Move/Rotate/Scale) | `ActiveTool` + `TransformGizmo`各ハンドル |
| `InteractionModifier`(FollowView/Duplicate/Snap) | 状態機械のドラッグ中修飾(§4.3表) |
| `VertexSnap` / `SurfaceSnap` | `ScenePicker`のクエリ2・3(V/Nホールド) |
| `TransformSpace` / `ProjectionMode` / `ActiveTool` | `SceneViewState`(永続化対象) |
| `Gizmo` / `TransformGizmo` / `PropertyGizmo` / `CompoundGizmo` | §6のクラス構造(同名) |
| `MoveHandle`〜`PlaneHandle` | §6.1のハンドル群(同名) |
| `CameraGizmoComposition` / `FrustumGizmo` | `CompoundGizmo(TransformGizmo + FrustumGizmo)` |
| `PointLight/SpotLight/DirectionalLight/RectLightGizmoComposition` | Phase 6以降(語彙決定後)。RectLightはスコープ外 |
| `SceneDeclaration` | `IScene`実装クラスのC#ソース(ADR-0038/0040) |
| `SceneModel` | 既存`CSharpGameEngine.SceneAnalysis.SceneModel`(同名) |
| `SceneSync` | 既存パイプライン(ADR-0040決定事項1)+§7コミット経路の総称。専用クラスは作らない |
| `EntityId` / `ComponentId` / `PropertyPath` | `EditorNodeId` / Component型完全修飾名 / `(型FQN, MemberName)`対(ADR-0040決定事項3) |
| `EditTransaction` | 新規`EditTransactionController`のトランザクション |

関連: [[ADR-0041-Scene-Editor-Interaction-Architecture]]、
[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]、[[M0020-Scene-Editor-Interaction]]、
`Docs/シーンエディタの仕様.md`
