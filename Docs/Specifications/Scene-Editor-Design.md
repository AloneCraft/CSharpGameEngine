# Scene Editor詳細設計(インタラクション基盤)

本書は`Docs/シーンエディタの仕様.md`(要求仕様)を現行アーキテクチャへ写像したクラスレベルの
詳細設計である。M0020 の Phase 0〜7(2026-08-16 完了)で実装した。M0020 が「意図的に除外したこと」に
挙げた項目は本書の範囲外である。意思決定の根拠は
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

> Parent specification: [Editor / VSIX](Editor-VSIX.md). This document remains the implementation-level specification for the currently observed Scene Designer baseline. Target Preview/Import/Bake host, source-sync v2, and release contracts are owned by the parent specification.

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
      → SceneEditConflictMerger → [既存] SceneSourceEditor.ApplyLatest → [既存] SceneTextBufferWriter(1 Undo)
     → VS TextBuffer → 上記の既存ループが表示を確定
   選択経路(§6): ScenePicker → [既存] SceneSelectionService(Origin=Viewport) ⇄ Hierarchy
```

スレッド規律はADR-0040決定事項7を継承する。入力・プレビュー・ギズモ判定はすべてUIスレッド
(`CompositionTarget.Rendering`ティックとWndProc/WPFイベント)で完結し、Roslynへの接触はコミット時の
`SceneSourceEditor`呼び出し(UIスレッドでの同期的なsyntax編集。解析はしない)に限る。

## 2. SceneViewState(§仕様2・3・20)

`SceneHwndHost`が所有し、mesh/構造変更時の`SceneRenderer`破棄・再生成を跨いで生存する。
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
| `InvertLookX`/`InvertLookY` | bool | Look(RMB)反転(仕様§4.1) | する |
| `InvertOrbitX`/`InvertOrbitY` | bool | Orbit(Alt+LMB)反転(2026-08追加、Lookと独立) | する |

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
`Radius / sin(実効FOV/2) * 1.1`(`Radius`は真の境界球半径=中心から最遠頂点までの距離)、near `0.05`、
far `Radius*20`。`Camera.CreateBoundsFit`とはRadiusの定義(AABB半対角)とnear/farが異なる。

**実効FOV**(2026-08 追加)は`min(FovY, 2*atan(tan(FovY/2) * aspect))`、すなわち縦FOVと水平FOVの
狭い方。縦長ウィンドウ(aspect<1)では水平FOVが縦より狭くなるため、距離を縦FOVだけで決めると
境界球が左右にはみ出す(aspect 0.75 という緩い縦長でも実測でNDC x=1.012と溢れた)。
aspect≥1では実効FOV=FovYとなり、横長・正方形の距離はPhase 0以来まったく変わらない
(`ComputeDistance_WideAspect_MatchesTheSquareCaseExactly`が完全一致を固定)。
なお投影行列そのものには**縦FOV**を渡す(実効FOVは距離決定にのみ使う)。

| 概念操作 | 計算 |
|---|---|
| `Look`(RMB+Mouse、Alt+RMBも同じ) | 基準(反転スイッチOFF): 右ドラッグで`Yaw`が減り、下ドラッグで`Pitch`が下がる(水平は2026-10-01にユーザー確認、垂直はM0096でユーザーの依頼により、それまでチェックを入れたときの向きを既定にした)。`Yaw -= Δx * 感度 * (InvertLookX ? -1 : 1)`、`Pitch -= Δy * 感度 * (InvertLookY ? -1 : 1)`。位置固定。`InvertLookX=ON`かつ`InvertLookY=ON`のとき(右ドラッグでYaw増・下ドラッグでPitch増)はピクセル完全一致(下記)を使う |
| `Fly`(RMB+WASDQE) | `位置 += (前後A/D左右Q/E上下の合成方向) * 速度 * Δt`。速度 = 基準 × `NavigationSpeedMultiplier` × (Shift: ×4 / Ctrl: ×0.25) |
| `Pan`(MMB Drag) | 視線を変えずカメラの右・上ベクトル方向へ`FocusPosition`と視点を平行移動。移動量はNavigationScale比例 |
| `Orbit`(Alt+LMB) | `Yaw`/`Pitch`を回す。選択があればドラッグ開始時の選択中心(Focus Selectionと同じメッシュ境界の中心、メッシュが無ければギズモのピボット)を回転中心にし、その点のカメラ座標が変わらないよう視点を移す — 選択中心は画面上の同じ位置・同じ距離に留まり、そこにある真球は描画が変わらない(M0095、2026-10-01ユーザー要望)。選択が無ければ`FocusPosition`固定で旋回する。旋回中は選択のギズモを隠し、終われば戻す(Altを押しただけでは隠さないので、§6.2のAlt+ハンドルのDuplicateは使える)。基準はLookと同じ向きで、反転は`InvertOrbitX`/`InvertOrbitY`(Lookとは独立)。Orbitは常にドラッグ量ベースで更新し、クリック位置のピクセル固定は行わない。 |

**ピクセル完全一致ドラッグ**(2026-08追加): RMBでクリックした点(方向)が、Lookの向きが
「右ドラッグでYaw増・下ドラッグでPitch増」(`InvertLookX=ON`、`InvertLookY=ON`)の間は
ドラッグ中カーソル先端に一致し続ける。Alt+LMB Orbitには適用しない。
- Look: Eyeが回転中固定なので、クリック時の方向だけから`atan2`/`acos`のみで厳密な閉形式が
  導ける(`SceneViewCamera.SolveLookOrientation`)。
- Orbit: ピクセル固定は行わず、マウスのフレーム差分をそのままYaw/Pitchへ適用する。
- それ以外の向きでは上表の`delta*sensitivity`式のまま(向きが逆の完全一致は定義上不可能)。
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
| `RenderSurfaceWindow.WndProc`(追加) | `WM_MOUSEWHEEL` | `int Delta`(`WHEEL_DELTA`=120単位、`WheelInput`イベント)。設計時はWPF `PreviewMouseWheel`経由の予定だったが、M0020 Phase 1の実機検証で一切発火しないことが判明したため他のマウスメッセージと同じくここで受ける(ADR-0041訂正) |
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
 ├─ LMB Down(ヒットなし/対象) → PendingSelect
 └─ Wheel                       → Dolly/Zoom(状態遷移なし)
Flying / Panning / Orbiting
 └─ 該当ボタンUp                → Idle
PendingSelect
 ├─ LMB Up                      → Down時の対象を選択確定 → Idle(既存経路を維持)
 ├─ MouseMove(空間開始かつ開始点から4px以上) → MarqueeSelecting
 └─ MouseMove(対象開始)         → PendingSelectのまま(途中切替禁止)
MarqueeSelecting
 ├─ MouseMove                   → 正規化矩形を更新
 ├─ LMB Up                      → 矩形内NodePath群を選択確定 → Idle
 └─ Esc / CaptureLost           → 確定せず矩形を破棄 → Idle
GizmoDragging
 ├─ MouseMove                   → プレビュー更新(V/Nホールドでスナップクエリ切替、§6)
 ├─ LMB Up                      → CommitTransaction → Idle
 └─ Esc                         → CancelTransaction → Idle
```

- ドラッグ系状態への遷移で`SetCapture`、離脱で解放。
- `PendingSelect`はLMB Down時点で対象/空間とCtrl状態を固定する。空間開始だけが4pxのユークリッド距離
  閾値を越えた時点で`MarqueeSelecting`へ進み、進んだ後はRMB/MMBでも操作種別を変えない。
- `Esc`は現在の一時操作のキャンセル(仕様§12)。Idle時は共通Scene選択を解除する。

### 4.3 共通Escルーティング

`SceneEditorEscapeRouter`はScene View、Hierarchy、Inspector、Component Paletteの各rootに1つだけ置く。WPFの`PreviewKeyDown`/`PreviewKeyUp`とownerの`HwndSource`に登録した`WM_KEYDOWN`/`WM_KEYUP` hookの双方からEscを受ける。ownerがキーボードフォーカスを持つ場合だけ消費するため、C# editor、Solution Explorer、その他のVisual Studio画面のEscは変更しない。KeyDownの最初の1回だけ意味処理を実行し、リピートは消費する。KeyUp、フォーカス喪失、Unload、Disposeでリピート状態を解除する。

優先順位は「一時操作または編集中UIを取消す」→「Scene選択を解除する」→「何もなくてもEscを消費する」。Scene ViewはFly/Pan/Orbit、矩形選択、ギズモ/Transform transactionを先に取消す。Hierarchyはinline rename、InspectorはTextBox復元またはComboBox close、Component Paletteは検索文字の消去を先に行う。EscではPhysics panel、Camera Preview、Scene Designer documentを閉じない。
- RMB保持中はW/E/Q/A/S/DをFly入力が占有する(仕様§5)。ツール切替キーはIdle時のみ有効:
  W=Move、E=Rotate、R=Scale、F=FocusSelection。
- コンテキストメニュー(優先度5)はM0020スコープ外だが、状態機械上は「RMB Down→移動なしでUp」を
  将来メニュー表示に割り当てられる形にしておく(Flyは移動発生時のみ確定)。

### 4.3 修飾キーの意味(仕様§6・11)

| 修飾 | Idle中 | ナビゲーション中 | TransformGizmoドラッグ中 | PropertyGizmoドラッグ中 |
|---|---|---|---|---|
| Shift | ― | FastNavigation(×4) | FollowView(§7.4)。SurfaceSnapドラッグ中は抑止(M0034) | 原則なし |
| Ctrl | ― | PrecisionNavigation(×0.25) | Grid/Incrementスナップ | 単位スナップ(角度・距離) |
| Shift+Ctrl | SurfaceSnap武装(§7.2、M0034)。中心に平面四角ハンドルが出る | ― | ― | ― |
| Alt | (Orbit開始の判定のみ) | (Orbit開始の判定のみ) | Duplicate(ドラッグ開始時に複製) | 適用しない |
| V ホールド | ― | ― | 頂点スナップ | ― |

IdleのAlt+LMB Downでは、現在のTransformギズモhandle hitをOrbit判定より先に行う。
handle hitならDuplicate drag、非hitならOrbitとなり、既存のAltナビゲーションを失わない。

M0034: SurfaceSnapは`N`キーホールド方式から、Shift+Ctrl押下中に現れる専用ハンドル
(`GizmoHandleId.SurfaceSnapPlane`)のドラッグへ変更した。武装判定はIdle中かつギズモ表示中のみ
(`SceneHwndHost.UpdateSurfaceSnapArmed`、Renderingティックで`GetKeyState`をポーリング、
マウス静止中でも1フレーム以内に反映される)。一度ドラッグが始まれば、掴んだハンドルの系統
(`GetHandleFamily`)がドラッグ内容を決めるため、途中でShift/Ctrlを離してもmouse-upまで
SurfaceSnapドラッグのまま継続する(M0033で確立した「ドラッグ系統はambientな状態ではなく
ヒットしたHandleが決める」方針と一貫させた)。専用ハンドルになったことで、V(Vertex)との
同時押し優先順位という概念自体が不要になった。

## 5. ピッキング(§仕様7)

`ScenePicker`(純粋クラス)は`SceneRenderObject`列(CPU側`SimpleMesh`+ワールド行列)に対して
次の4クエリを提供する。

1. **Pick(選択・ギズモ用)**: スクリーン座標→レイ(§3のunproject)→ 各オブジェクトの全三角形へ
   Möller–Trumbore交差 → 最近ヒットの`NodePath`と交点・法線を返す。加速構造なしの線形走査
   (現行シーン規模で十分。必要になったら別ADRで導入判断)。
2. **PickMarquee**: 各`SceneRenderObject`の全頂点を`World * View * Projection`で物理ピクセルへ
   投影し、その最小/最大から得るスクリーン境界矩形と選択矩形の交差を判定する。境界接触を含み、
   同じ`NodePath`を構成するrender objectが複数あっても描画順で1回だけ返す。全頂点が視錐台の
   depth外またはカメラ後方なら対象外。
3. **VertexSnapQuery**: レイ近傍のオブジェクトについて、頂点をスクリーン空間へ投影し、カーソルから
   スクリーン半径R(既定8px)以内の最近頂点のワールド座標を返す。Positionのみに作用し
   Rotationは変更しない(仕様§7.1)。
4. **SurfaceSnapQuery**: Pickと同じ交差で、ドラッグ中オブジェクト自身を除外した最近ヒットの
   交点+面法線を返す。Positionを交点へ、Rotationを法線整列へ
   (`SurfaceSnap.AlignRotationToNormal=false`で整列のみ無効化可、仕様§7.2)。

VertexSnapQueryとSurfaceSnapQueryは、現在のselection rootだけでなくその全子孫も候補から除外する。
Vertexは物理8px半径内のスクリーン距離を優先し、同距離ならdepthが手前の点を選ぶ。
Surfaceは最近三角形の幾何法線を返し、primaryのworld回転後ローカル+Yを法線へ合わせる。
Phase 5では法線整列を常時有効とし、無効化設定UIは追加しない。
全selection rootのPosition+Rotationが編集可能な場合だけSurface queryを適用し、そうでなければ
V/ Ctrlへfallbackして式を保持するRotationを保護する。

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
    ├─ DirectionGizmo(DirectionalLight)
    ├─ RangeGizmo(PointLight)
    └─ Direction/Cone/RangeGizmo(SpotLight)
```

表示するTransformGizmoは`ActiveTool`に対応する1種。PropertyGizmoは置き換えではなく**同時表示**
(仕様§8)。Phase 6でCameraとDirectional/Point/Spot Lightを実装済み。ライト語彙はADR-0043により
現行L9型の完全修飾名Bindingとした。RectLightはBindingを持たず対象外。

### 6.2 描画

- `SceneRenderer`に**オーバーレイPSO**を追加: 深度テスト無効・ライン+ソリッド両トポロジ・
  per-objectハンドル色(通常/ホバー/アクティブ)。既存`Selected`フラグと同じ定数バッファ方式。
- `MarqueeOverlay`は矩形4角をnear面から同一スクリーンレイ上へ僅かに進めたworld座標へ変換し、
  半透明triangle 2枚+line 4辺として同じオーバーレイPSO/固定upload bufferへMoveギズモと合成する。
- ハンドルメッシュは`GizmoMeshFactory`拡張の手続き生成(矢印・リング・クアッド・円錐・立方体)。
- スクリーン一定サイズ: ギズモのワールドスケール = `ハンドル位置とカメラの距離 * tan(FovY/2) * 定数`
  (Orthographicでは`OrthographicSize * 定数`)。
- 座標空間: `TransformSpace=World`では軸をワールド軸に、`Local`では対象ノードの回転を適用。
- Phase 4の実装はPhase 3の`MoveGizmoFrame`、`GizmoOverlayPrimitive`、line/triangle overlay PSOを
  そのまま合成モデルとして拡張する。Rotateは軸リング3本+viewリングを各48線分、Scaleは軸線+
  8分割solid円柱3本+軸端立方体3個+中心立方体1個として同じ固定upload bufferへ投入する。

### 6.3 ハンドルヒット判定

解析形状で行う(§5参照): 軸=有限円柱(半径はスクリーン一定サイズから逆算)、リング=トーラス
近似(レイ・平面交差+半径帯)、平面クアッド=矩形交差。ヒットしたハンドルが拘束
(軸/平面/リング面)を決める。

### 6.4 ドラッグ解決(拘束ソルバ)

- 軸移動: レイと「軸を含みカメラへ最も正対する平面」の交点を軸へ射影し、開始点との差分ΔPを得る。
- 平面移動: レイと拘束平面の交点差分。
- 回転: レイとリング平面の交点を中心角へ変換し、開始角との差分ΔR。
- スケール: 軸射影距離の比。中心ハンドルはスクリーン距離比で均一スケール。
- Ctrlスナップ: ΔP→1.0単位、ΔR→15°、スケール倍率→0.1単位(最小0.1)。Ctrl状態はドラッグ中の
  各Moveイベントで評価するため、ドラッグ途中の押下/解放にも追従する。
- 複数選択: primaryの位置を共有Pivot、primaryの回転をLocal基底とする。選択済み祖先を持たない
  selection rootへ同じワールド差分を適用し、子孫のpreviewにも同じ差分を伝播する。
- 書き戻しmask: MoveはPosition、RotateはPosition+Rotation、ScaleはPosition+Rotation+Scale。
  Pivot操作後の行列分解で変化しうる全メンバーがliteral/defaultで編集可能な場合だけハンドルを有効にする。
- Shift=FollowView(仕様§6.1): Moveでは`ΔP`を`SceneViewState.FocusPosition`と視点位置へも加算。
  Rotateでは同一Pivot周りで視点位置・Yaw/Pitchへ`ΔR`を合成。Scaleでは追従しない(既定)。
- Alt=Duplicate(仕様§6.2): ドラッグ開始時に`DuplicateNode`をプレビュー用の複製表示で開始し、
  MouseUpで「複製+Transform設定」を1トランザクションとしてコミットする。元ノードは無変更。

Phase 5のDuplicate previewは、SceneRendererのobject定数バッファを初期描画件数の2倍まで確保し、
選択rootと全render descendantを末尾の一時GPU objectとして追加する。元objectと同じNodePathを
更新してしまわないよう整数handleで一時objectだけを更新し、Cancel/Commit時はGPU idle後に末尾から
破棄する。各親の既存Children数と選択順から結果NodePathを先に計算し、そのpathで一時表示を選択色にする。
MouseUp後は再解析されたprojectionで予測pathが全件解決してからduplicate rootへSelectionを移す。
Scene Rootのduplicateは親Childrenがないため開始しない。

FollowViewはdrag開始時のFocusPosition/FocusDistance/Yaw/Pitchを捕捉し、各Moveイベントのtotal deltaから
再計算する。Moveは視点とFocusを平行移動、Rotateは共有Pivot周りに視点とforwardを回転、Scaleは無視する。
Esc/capture loss/commit失敗では捕捉値へ戻し、成功時は追従後のcameraを維持する。

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
  2. SceneEditConflictMerger.Merge(開始値, 終了値, 最新snapshot) … Versionずれは差分統合
  3. SceneSourceEditor.ApplyLatest(現Root, 最新snapshot, merged op)
  4. SceneTextBufferWriter.TryApply(buffer, before, after, "Move Cube" 等) … 1 ITextUndoTransaction
  5. PendingSnapshotを解放 → 通常ループが確定表示
Cancel(Esc / リベース失敗)
  プレビュー値を元値へ戻し、PendingSnapshotを解放。テキスト無変更(Undo履歴も無傷)。
  リベース失敗時はVSステータスバー/InfoBarで通知(自動上書きしない、仕様§19)
```

競合マトリクス(仕様§18・19):

| 状況 | 挙動 |
|---|---|
| ドラッグ中に外部編集(別値・別ノード) | 保留。MouseUpのリベースで統合される(EditorNodeIdはセッション安定) |
| ドラッグ中に外部編集(対象ノード消滅) | Commit時に`ApplyLatest`が失敗 → プレビュー破棄+通知 |
| ドラッグ中に同一Transform/Propertyが外部変更 | 開始→終了差分を最新値へ合成。Position/Propertyは加算、RotationはQuaternion差分、Scaleは倍率(開始scaleが0近傍なら加算fallback) |
| 外部変更で対象/Componentが消失、または対象Memberが非editable式化 | Commit失敗 → プレビュー破棄+通知。外部式を上書きしない |
| 非ドラッグ時の外部編集 | 既存どおり即時反映(250msデバウンス) |

Undo/Redo(仕様§17): 追加実装なし。1コミット=1テキストUndo単位であり、Ctrl+Zでテキストが戻ると
既存の解析ループが表示・ヒエラルキー・選択根拠をすべて戻す。

通常snapshot更新は`SceneIncrementalUpdatePolicy`が`SceneDiff`を分類する。
Transform/非mesh Memberのみなら`SceneRenderIncrementalUpdater`が既存meshを保持して全nodeの
`world = local * parentWorld`とboundsを再計算する。mesh/構造変更または表示世代が連続しない場合は
latest-only全再構築へ戻す。`SceneRenderer`は2 frame slotごとのallocator・object/overlay upload領域・
fence値を持ち、`RenderFrame`ではslot再利用時だけ待機する。resize・資源破棄・disposeは全体待機する。

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
- **AddComponent/RemoveComponent**(Phase 6実装済み): `ComponentsListLocation`への挿入/
  `SceneComponent.Location`の削除。source opはcollection式と配列initializerを扱い、
  同型重複・未解決対象・不正object creationを拒否する。Inspector/Add Component UIは本マイルストーン
  対象外のため、`SceneComponentCatalog`からの列挙UI配線は行わない。
- 編集可否は既存`SceneValue.IsEditable`(Literal/EnumMember/Defaultのみtrue)に従う。式・
  `[SceneAssets]`参照が書かれたメンバーはギズモから書き換えない(該当軸のドラッグを無効化し、
  ハンドルを淡色表示する)。

Phase 5製品実装では最終TransformをDuplicateNodeOperation自身に持たせ、clone内Transformを
書き換えてから兄弟末尾へ1回だけ挿入する。複数親のbatchはChildren位置の後方順、同一親内は
選択順で適用し、Roslyn span安全性と予測NodePath順を両立する。

## 9. Component認識(SceneComponentBinding)と語彙ギャップ

`SceneModelRenderObjectBuilder`のFQNハードコードを、宣言的なバインディング表
(`SceneComponentBindings`)へ置換した(**M0020 Phase 0 で実装済み**)。

| Component完全修飾名 | `SceneComponentRole` | エディタ上の意味 |
|---|---|---|
| `global::CSharpGameEngine.Runtime.Windows.Transform` | `LocalTransform` | ローカルTRS。**親子合成**: pre-order DFSで`world = local * parentWorld`を畳み込む(Phase 0 で「各ノードのローカル=ワールド」を廃止済み) |
| `global::CSharpGameEngine.Runtime.Windows.PrimitiveMesh` | `PrimitiveMesh` | プリミティブ描画 |
| `global::CSharpGameEngine.Runtime.Windows.StaticMesh` | `StaticMesh` | アセット描画 |
| `global::CSharpGameEngine.Runtime.Windows.Camera` | `Camera` | FrustumGizmo対象 |
| `global::UserEcsProject.DirectionalLight` | `DirectionalLight` | Direction対象 |
| `global::UserEcsProject.PointLight` | `PointLight` | Range対象 |
| `global::UserEcsProject.SpotLight` | `SpotLight` | Direction/InnerCone/OuterCone/Range対象 |

合成式はランタイムの`TransformMath.ToWorld`(`TransformWorldResolverTests`が固定している規約)と
**同一だが、型は参照しない** — net472からnet10.0の型は参照できない。Phase 3で再検討した結果、
VSIX内の純粋層`SceneTransformMath`へ同じrow-vector式(`local * parentWorld`、
`world * inverse(parentWorld)`、TRS分解)を実装し、読み取り・preview・source write-backで共有する
([[ADR-0042-Scene-Editor-Gizmo-Axis-Handedness]])。ランタイムComponent型の共有コンパイルは行わない。
`SceneNode.Children`は構文木由来のツリーなのでサイクルガードは不要
(深さは解析器が`MaxRecursionDepth`で打ち切る)。

enumメンバー名は`Transform`ではなく`LocalTransform`にしてある(`Transform` struct型名との混同を避け、
「これはローカル、worldは合成結果」という区別を型名で表す)。**退役した`global::UserEcsProject.Transform`は
登録しない** — 互換のため両方登録するとバグを静かに塗り潰し、直ったかどうかが判定不能になる。

語彙ギャップ(ADR-0041決定事項9)はADR-0043で解消した。ライト各種は`UserEcsProject`(L9)の
完全修飾名を文字列表へ登録し、VSIXからL9へのバイナリ参照は追加しない。Rendererハンドル所有を含む
エンジン標準化は別ADRへ送る。RectLightは型・描画契約とも存在せず、スコープ外として未登録。

## 10. 永続化スキーマ(§仕様3・20)

`WritableSettingsStore`(`SettingsScope.UserSettings`)、コレクション`CSharpGameEngine\SceneEditor`。

| プロパティ | 型 | 既定値 |
|---|---|---|
| `ActiveTool` | string(enum名) | `Select` |
| `TransformSpace` | string | `World` |
| `ProjectionMode` | string | `Perspective` |
| `NavigationPreset` | string | `Default` |
| `NavigationSpeedMultiplier` | float(文字列格納) | `1.0` |
| `InvertLookX` / `InvertLookY` | bool | `false`(基準に対する反転) |
| `InvertOrbitX` / `InvertOrbitY` | bool | `false`(基準に対する反転、Lookと独立) |

`InvertLookX`/`InvertLookY`/`InvertOrbitX`/`InvertOrbitY`/`NavigationSpeedMultiplier`の5項目は
拡張機能 > C# Game Engine > オプション...(2026-10-01追加)、または Tools>Options > C# Game Engine > Scene Editor から編集できる(2026-08追加、
`SceneEditorOptionsPage`)。残る4項目は現在状態(ツールバー相当)またはプリセット未実装のため
非公開。

ナビゲーション反転の基準を変えたときは、VS user settingsの移行バージョン(`NavigationSettingsMigrationVersion`)を上げ、既存プロファイルを一度だけ変換する。
- 版1: 未移行プロファイルの4軸を旧既定(`true`)へ揃えた。
- 版2(2026-10-01): 基準を「旧既定の水平・Orbit」と「旧既定の逆の垂直Look」へ移し、スイッチの既定をOFFにした。未移行プロファイルは既定(OFF)を書く。版1のプロファイルは4値を反転する。水平とOrbitは式の基準も反転したので見た目の向きが保たれ、垂直Lookは式が同じなのでユーザーの依頼どおり実際の向きが一度だけ反転する。
- 版3(M0096): Look・Orbitの垂直の基準を反転した(それまでチェックを入れたときの向きが既定)。値は基準に対する反転のまま変換しないので、チェックの無いプロファイルは新しい既定の向きに、チェックを入れていたプロファイルはそれまでの既定の向きになる。
- 移行後にユーザーがOptionsで変更した値は保持する。

保存はツールウィンドウ非表示時と値変更時(デバウンス)。カメラ姿勢・`FocusPosition`・選択・
ギズモ表示はセッション内のみ。**`SceneDeclaration`へSceneView状態を書くことは構造的に不可能**
(書き込み経路が`SceneEditOperation`列挙に限定されており、そこにSceneView状態を運ぶopが無い)。

## 10.1 ヒエラルキー起点のソース同期

`HierarchyToolWindowControl`は独自の複数選択状態(`NodePath`集合、primary、範囲アンカー)を保持する。通常クリックは置換、Ctrl+クリックはトグル、Shift+クリックは深さ優先の表示順でアンカーからの連続範囲を選択する。再解析によるTreeView再構築時も、展開状態と同様に`NodePath`で復元する。

ソース同期の起点は**ヒエラルキー操作だけ**である。Viewport/Inspector起点の`SceneSelectionService`同期はヒエラルキー表示を更新するが、エディタのマークまたはキャレット移動を発行しない。

通常クリックでは、対象のScene C#文書が既に開かれている場合に限り、Roslyn `SceneNode.Location.Span`（`new Node { ... }`全体）を薄い`TextMarkerTag`背景でマークする。親のSpanは子の構文を含む。親子の重複Spanは併合し、独立した複数Spanは同時に保持する。対象文書を開く・アクティブ化する・テキスト選択を変更することはない。選択解除、Scene切替、解析失敗、再解析では古いマーカーを解除または再計算する。

ダブルクリックまたはEnterだけがprimaryの明示移動要求を発行する。Running Document Tableで対象文書が既に開いていることを確認できた場合に限り、そのテキストビューを前面化し、`SceneNode.Location.Span.Start`（通常は`new`の先頭）へキャレットを置く。未表示文書、消滅ノード、テキストビュー取得失敗では何もしない。

## 10.2 シーンを開く経路

コードエディタとソリューションエクスプローラーの右クリックメニューには、シーン用の項目を置かない（2026-10-01、ADR-0071）。パッケージが読み込まれる前は、VSが`.vsct`の既定値のまま右クリック項目を表示するため、以前は.csファイルにも「シーン デザイナーで開く」と「Scene」の行が並んでいた。

シーン JSON（プロジェクトの`Scenes\**\*.json`、ADR-0070）はダブルクリックでシーン エディター（`SceneJsonEditorFactory`）に開く。タブの下の「デザイン / JSON」で表示を切り替える（XAMLエディターと同じ）。どちらの表示も同じJSONテキストバッファーを見るので、保存・`*`・Undo は VS のもの。タブが前面に来るとそのシーンをPinし、各タブのデザイナーは自分のシーンだけを表示する。ツールバーの`シーン デザイナーで開く`は、アクティブな`*.scene.json`をこのエディターのデザイン表示で開く。SceneSelectorのカタログは、アクティブ文書ではなくPin済み`SceneKey.ProjectId`を優先して取得する。

カタログは補助UIであるため、取得不能・失敗時に表示済みのPin/`SceneHwndHost`を空状態へ戻してはならない。Sceneの再解析でRootが消失または解析エラーになった場合だけ、既存の空状態遷移を行う。

## 10.3 Visual Studio テーマのヒエラルキー、Component アイコン、標準マーカー

Hierarchy の TreeView は Visual Studio 公開テーマ資源
`ThemedDialogTreeViewStyleKey` / `ThemedDialogTreeViewItemStyleKey` を基礎とする。TreeView 内に
キーボードフォーカスがある間は VS の active selection、他ツールウィンドウへ移った後は inactive
selection を用いる。複数選択行はすべて表示し、primary だけに VS ブラシの左アクセントを表示する。
通常クリックは Hierarchy 自身へフォーカスを置くが、C# エディタへは移さない。

子を持つ行だけは `CrispImage` の `ExpandRight` / `ExpandDown` を使う展開ボタンを表示する。展開
ボタンのクリックは展開状態だけを変え、選択集合・primary・選択通知・ソース同期を発行しない。葉には
展開ボタンを表示しない。

`CSharpGameEngine.Entities.SceneComponentIconAttribute(string projectRelativePath)` は `struct` 専用、
複数指定不可の公開属性である。値はプロジェクト相対の `.svg` / `.png` に正規化し、絶対パス・`..`・
不正拡張子を拒否する。解決不能な属性、参照アセンブリまたはメタデータ由来の component は安全に
既定 `Component` アイコンへ戻し、編集 UI は表示しない。Scene root は `ThreeDScene`、通常 Node は
`AbstractCube`、属性未指定 component は `Component` の `KnownMonikers` を使う。component アイコンは
宣言順に 16px で全件表示する。`SceneVisualIcon` は既定 moniker、プロジェクト画像、将来の lock
overlay を共通に記述する。lock overlay は予約だけで、今回の UI には表示しない。SVG は
`SharpVectors.Wpf`、PNG は WPF `BitmapImage` で読み込み、パスと更新時刻をキーにキャッシュする。

C# glyph margin は、現在の Roslyn Project の書込み可能なソースにある追加可能 component `struct` の
宣言行だけに表示する。glyph の明示クリックで、その位置にアンカーした選択メニューを開く。先頭の
「既定アイコン」と、プロジェクト配下を再帰列挙した SVG / PNG だけを候補にする。`bin`、`obj` 等の
生成物とパストラバーサルは候補にしない。選択は完全修飾した属性を Roslyn 構文更新で追加・置換し、
既定アイコンはその属性だけを削除する。partial `struct` は既存属性の所有宣言だけを更新し、未指定なら
クリックした宣言を所有箇所にする。書込みは既存バッファの 1 Undo 操作で行い、他の文書を開かず、
フォーカスも移さない。

ソースマーカーは独自 `ITaggerProvider` を持たず、Visual Studio 標準の
`ITextMarkerProviderFactory.GetTextMarkerTagger(ITextBuffer)` が返す
`SimpleTagger<TextMarkerTag>` に、この拡張が所有する `TrackingTagSpan` だけを登録する。書式名は
`MarkerFormatDefinition/CSharpGameEngine.SceneHierarchySource`、背景は半透明青紫、前景色は未指定と
して C# 構文色を変えない。RDT、対象 buffer、tagger 取得、登録数および失敗は ActivityLog に記録する。
選択解除、Scene 切替、解析失敗、文書／buffer 消失、ソース更新、破棄では当拡張所有の古い tag だけを
除去または最新位置へ置換する。

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
| Phase 6 Property | Camera投影別handle、3ライト構成、Compound合成、modifier適用表、値clamp/snap、direct transaction |
| Phase 6 source | 省略Member追記、AddComponent/RemoveComponentの対象collection限定編集、他trivia保持 |
| Phase 7 incremental | SceneDiff分類、mesh再利用、親子world chain/bounds再計算、構造/mesh変更のrebuild |
| Phase 7 frame/競合/Esc | frame slot fence、Transform/Property開始値差分マージ、全7入力状態のEsc終端 |

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
| `PointLight/SpotLight/DirectionalLight/RectLightGizmoComposition` | Phase 6で前3種をPropertyGizmoとして実装。RectLightはスコープ外 |
| `SceneDeclaration` | `IScene`実装クラスのC#ソース(ADR-0038/0040) |
| `SceneModel` | 既存`CSharpGameEngine.SceneAnalysis.SceneModel`(同名) |
| `SceneSync` | 既存パイプライン(ADR-0040決定事項1)+§7コミット経路の総称。専用クラスは作らない |
| `EntityId` / `ComponentId` / `PropertyPath` | `EditorNodeId` / Component型完全修飾名 / `(型FQN, MemberName)`対(ADR-0040決定事項3) |
| `EditTransaction` | 新規`EditTransactionController`のトランザクション |

関連: [[ADR-0041-Scene-Editor-Interaction-Architecture]]、
[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]、[[M0020-Scene-Editor-Interaction]]、
`Docs/シーンエディタの仕様.md`

## 13. View 描画モードとランタイム空間デバッグ

Scene View 上部の `View` は `Lit`、`Base Color`、`Normal`、`Wireframe` をラジオ選択する。設定は Visual Studio のユーザー設定へ保存し、カメラプレビューにも同じモードを適用する。`Debug Overlays > Global Spatial Octree` は既定オフのチェック項目で、オンのときだけ実行中ゲームの `World.GlobalSpatialIndex` をNamed Pipe経由で取得して主Scene Viewへワイヤー重畳する。個別Systemの索引は自動表示しない。

ゲーム側はプロセスIDを含むユーザー限定Named Pipeを公開し、Scene Viewから要求されたときだけ最大8,192ノードを不変スナップショット化する。VSIXは最大512ノードを視錐台優先で表示し、Rootを必ず含める。通信断、終了、不正パケット、float変換不能座標は表示停止とステータス表示だけで扱い、ゲーム実行や通常描画を失敗させない。

## Editor Preview World

Scene Viewの未実行状態は、Source Generatorが出力する GeneratedEditorPreviewBridge を隔離net10 Preview Hostから実行する。Runtimeと同じWorld、SystemOrder、GlobalSpatialIndex、SpatialDebugHubを使い、[ExecuteInEditor] のSystemだけを変更時（Realtime=trueは15Hz追加）に実行する。Runtimeスナップショットが有効な間はRuntimeを優先し、終了または未接続時はEditor Previewへ自動フォールバックする。


## Runtime component namespace

Scene View light bindings use `CSharpGameEngine.Runtime.Windows` physical light components. UserEcsProject contains scene declarations and gameplay markers only; runtime/editor preview use the same generated source boundary.

## VSIX表示言語

Scene View、Hierarchy、Inspector、Component Palette、Options、ダイアログ、ステータスはVisual Studio起動時のUI cultureへ追従する。英語`en-US`をneutral／最終フォールバック、日本語`ja-JP`をsatelliteとして提供し、未対応言語では英語を表示する。言語変更はVisual Studio再起動後に反映し、VSIX独自の言語選択は持たない。

日本語UIでも`Scene View`、`Transform`、`Collider`、`RigidBody`、`AABB`、`Logical256`等のコード対応用語は英語表記を維持する。Scene名、Node名、Component/member名、パス、PID、tick、HRESULT、Stable ID、C#リテラルは値をそのまま表示する。Spatial/Physics Named Pipeの理由は内部enumとして扱い、wire tokenそのものをHUDへ表示しない。

`VSIX 0.1.x | build yyyy-MM-dd HH:mmZ | current`はスクリーンショット／動画による配置同一性確認のためinvariantとする。選択UI cultureと詳細なrevision/hash/pathはToolTipおよびActivity Logで確認できる。

## Physicsパネルの右端折りたたみ

Physicsの操作とデバッグ表示設定はScene Viewヘッダーから分離し、ビューポート右端の専用サイドバーへ配置する。既定は閉じた状態で、閉じているときは幅24 DIPの`<`タブだけを表示する。タブをクリックすると幅280 DIPのパネルを開き、パネル見出し右側の`>`で閉じる。パネルはネイティブ`HwndHost`の上へ重ねず、右列としてレイアウトするため、開閉時はビューポート幅も更新する。

Scene Viewのビューポートがキーボードフォーカスを持つとき、`N`で同じ開閉操作を行う。`N`はVisual Studio全体のコマンドには登録せず、Scene Viewの入力処理だけで消費する。パネルの開閉状態は`SceneEditorSettings.ShowPhysicsPanel`へ保存し、未保存・不正値は閉じた状態へフォールバックする。パネルは2つの区画からなる(M0097)。
- **物理のデバッグ表示**: 親のチェックと1行の説明、その下に字下げした7項目(当たり判定の範囲、速度、スリープ中の物体、重心、接触点と法線、当たり判定の分割木、破砕のつながり)。7項目は親のチェックが外れている間は無効(グレー)で、それぞれツールチップで何を描くかを説明する。ゲームの実行中はそのゲームの物理を、それ以外は編集用ワールドの物体を置いた位置で描く。編集用ワールドは時間を進めない。
- **Bake(物理の事前計算)**: 何をするか(既定600ティックを記録し、ゲームが再生できる)と出力先(`Assets/Physics/Bakes`)の説明、選択中のシーン/全シーンのBake、進捗・キャンセル・成果物の一覧。
- シミュレートの操作(Simulate/Pause/Step/Reset)とランタイム診断はM0097で削除した。診断にはVisual Studioのデバッガーとプロファイラーを使う(拡張機能メニューとツールバーの「診断ツール」)。
