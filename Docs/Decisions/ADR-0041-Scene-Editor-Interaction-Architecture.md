# ADR-0041: Scene Editorインタラクション基盤(ナビゲーション・ピッキング・ギズモ・編集トランザクション)

## ステータス

承認済み(M0020 Phase 0〜7+後続Task Cは**実装済み**)。実装は
[[M0020-Scene-Editor-Interaction]]で段階的に行い、各Phase完了時に同文書の「検証結果」と
`Docs/Conformance.md`へ実行結果を追記する。要求仕様は
`Docs/シーンエディタの仕様.md`(§1〜23)、既存基盤は
[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]を前提とする。

## 背景

`Docs/シーンエディタの仕様.md`は、Unityの操作体系を基本にUE/Blender/Mayaの操作を選択採用した
シーンエディタの要求仕様であり、次を要求する: RMBフライ/Orbit/Pan/Dollyのナビゲーション(§4)、
W/E/RのTransformツールとLMB選択(§5)、Shift=FollowView/Alt=Duplicate/Ctrl=Snapの修飾(§6)、
V/Nホールドの頂点・面スナップ(§7)、`CompoundGizmo = TransformGizmo + PropertyGizmo`の合成(§8〜11)、
入力優先順位(§12)、宣言コードとの双方向同期・編集トランザクション・共通Undo(§13〜19)、
SceneView状態を宣言へ保存しない原則(§20)。

現状(ADR-0040 Phase 9完了時点)とのギャップ:

- Code→Editor方向のライブ表示は動くが、ビューポート入力は皆無。`RenderSurfaceWindow.WndProc`は
  `WM_SIZE`/`WM_MOUSEACTIVATE`(`MA_NOACTIVATE`を返しマウスメッセージ自体は届く)/`WM_SETFOCUS`
  (直前の所有者へ即時返却)しか処理しない。
- この振る舞いは`RenderSurfaceFocusPolicy`として意図的に設計されたもの。ネイティブ子HWNDが
  Win32フォーカスを保持するとWPFの`Keyboard.FocusedElement`と脱同期し、**VS全体の**Tab/Backspace/
  Deleteがセッション終了まで壊れる実バグ(コミット`a162478`)への対策であり、無効化できない。
- Editor→Code配管(`SceneSourceEditor`/`SceneTextBufferWriter`)は完成済みだが呼び出し元ゼロ。
  `SceneEditOperationKind`11種のうち実装は2種。
- カメラは`SceneRenderer`内の固定計算(bounds中心+`normalize(1,0.75,1)*radius*3`、FOV 45°)で、
  `SceneRenderer`自体が再解析のたびに破棄・再生成されるため編集状態を持てない。
- ピッキング・レイキャスト・ギズモ描画・ライン描画は一切存在しない(リポジトリ全域でヒット0件を
  確認済み)。
- `SceneModelRenderObjectBuilder`のTransform型ハードコードバグ(ADR-0040既知の制約)により、
  実シーンが原点固まりで描画される。ワールド行列が正しくない限りギズモの配置は成立しない。

ユーザーの指定(2026-08確認): キーボード入力は「エディタ(コードエディタ)から**フォーカスを
奪わない**。対象ツールウィンドウのコントロールにフォーカスがあるときだけ効く」方式とする。
旧仕様`Docs/EditorSpec.md`(ギズモ・Undo・複数選択をスコープ外と宣言した`.scene` JSON前提の設計)は
本仕様と正面から矛盾するため削除する(生き残り項目はM0020「意図的に除外したこと」へ移設)。

## 決定事項

### 1. `SceneViewState`: 編集ビュー状態はRendererの外で生存させる

新規クラス`SceneViewState`(VSIX内、純C#・依存なし)に仕様§2/§3の状態を集約する。

| 分類 | 内容 |
|---|---|
| 視点 | 位置・Yaw/Pitch、`FocusPosition`、`FocusDistance` |
| 投影 | `ProjectionMode`(Perspective/Orthographic)、FovY、OrthographicSize |
| 永続ツール状態 | `ActiveTool`(Select/Move/Rotate/Scale)、`TransformSpace`(World/Local)、`NavigationPreset`、`NavigationSpeedMultiplier` |

所有者は`SceneHwndHost`。`SceneRenderer`は再解析のたびに破棄・再生成される(当面は現行仕様のまま、
決定事項11参照)ため、**`SceneRenderer`には一切の編集状態を置かない**。`SceneRenderer`は
`ComputeViewMatrix`の固定計算をやめ、view/proj行列を毎フレーム外部から受け取る
(`SetCamera(in Matrix4x4 View,in Matrix4x4 Projection)`+毎フレームの定数バッファ更新)。

カメラ数学はnet10.0側の実績実装`src/CSharpGameEngine.Windows/Graphics/Camera.cs`(型は
`CSharpGameEngine.Graphics.D3D12.Camera`。Scene DSLのComponent `CSharpGameEngine.Runtime.Windows.Camera`
とは**別物**)の**式**をnet472側へ書き写す。TFM境界の共有は`GizmoMeshFactory.cs`冒頭コメントの前例
(小さな断片は複製し、ランタイムAPIをエディタ都合で汚さない)に従う。座標系は
[[ADR-0026-Camera-Geometry-Ruf-Integration]]のエンジン規約(左手系、+Y up、+Z forward)。

> **2026-08 訂正(M0020 Phase 0 の実装時に判明)**: 本節の初版は`Graphics/Camera.cs`を「純
> `System.Numerics`」と記述していたが**誤り**だった。実際は`[SceneFactory]`
> (`CSharpGameEngine.Authoring`)属性を持ち、`GetForward`/`GetFlatForward`/`GetFlatRight`は
> `RufQuaternion`/`RufVector`/`Spatial<UnityCoordinateConvention>`(いずれもnet10.0の
> `CSharpGameEngine.Core`)を経由する。したがってnet472移植では (a) `[SceneFactory]`を落とし、
> (b) 方向計算を既存テスト`CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep` /
> `GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep`が固定している閉形式
> `Forward = normalize(cosPitch*sinYaw, sinPitch, cosPitch*cosYaw)` /
> `FlatRight = normalize(Cross(UnitY, FlatForward))`へインライン展開する。さらに**`MathF`はnet472に
> 存在しない**ので`(float)Math.*`を使う(Phase 0 の`SceneCameraFraming`は最初からこの形で書いた)。

初期フレーミングは`Distance = Radius / sin(FovY/2) * 1.1`(`CreateBoundsFit`と同じFOV導出形)を採る。
ただし`Radius`は`CreateBoundsFit`のAABB半対角ではなく、`ComputeSceneBounds`の**真の境界球半径**
(中心から最遠頂点までの距離)を使う。FovY=45°で約`2.874*Radius`となり、Phase 0まで使っていた固定値
`3.0*Radius`と4%しか違わないため見た目は実質不変(この主張は
`SceneCameraFramingTests.ComputeDistance_AtFortyFiveDegrees_StaysWithinFivePercentOfTheLegacyThreeRadiusFraming`
が固定している)。方向は`normalize(1,0.75,1)`のまま。near/farはPhase 0では現行値(`0.05` /
`Radius*20`)を維持する — Sky domeを自動フレーミング半径から除外しつつ描画しているため、
`CreateBoundsFit`の`far = Distance + 4*Radius`ではSky domeがクリップされる。Phase 1で
`FocusDistance`からfarを再計算する。

> 初版は「`CreateBoundsFit`相当を用い、現行の見た目を維持する」と書いていたが、両者は距離係数も
> `Radius`の定義もnear/farも異なるため自己矛盾だった。上記が確定形である。

### 2. 入力ルーティング: マウスはWndProc→純粋状態機械、キーボードはVS標準フォーカスモデル

**マウス(ボタン・移動)**: `RenderSurfaceWindow.WndProc`に`WM_LBUTTONDOWN/UP`・
`WM_RBUTTONDOWN/UP`・`WM_MBUTTONDOWN/UP`・`WM_MOUSEMOVE`を追加する。`WM_MOUSEACTIVATE`が
`MA_NOACTIVATE`でもこれらはヒットテストで子HWNDへ届く(現行コメントが「将来のPicking入力のために
維持」と明記している経路)。メッセージはイベントへ変換して新規の**純粋・ユニットテスト可能**な
`SceneInputStateMachine`へ転送する。

**マウス(ホイール)**: `RenderSurfaceWindow.WndProc`に`WM_MOUSEWHEEL`を追加し、他のマウスメッセージと
同じくここで受ける。`wParam`上位ワードの符号付き回転量(`WHEEL_DELTA`=120単位)を
`RenderSurfaceMouseMessageTranslator.DecodeWheelDelta`で取り出し、`RenderSurfaceWindow.WheelInput`
イベント経由で`SceneHwndHost.HandleWheel`へ渡す。フォーカスの有無に関わらず動作する
(`RenderSurfaceFocusPolicy`によりこのサーフェスはWin32フォーカスを決して取らないが、ホイールは
それとは無関係にヒットテストで届く)。

> **2026-08 訂正(M0020 Phase 0)**: 初版は`WM_MOUSEWHEEL`もWndProcで受ける計画だったが、
> 「`WM_MOUSEWHEEL`はフォーカスを持つHWND宛にpostされ、Win32フォーカスを取らないこの
> サーフェスには構造的に届かない」という理解のもとWPF側`PreviewMouseWheel`経由に変更した。
>
> **2026-08 再訂正(M0020 Phase 1、実機検証)**: 上記の"フォーカス依存"という理解が誤りだった。
> 実際にExperimental Instanceで検証したところ、WPF側`PreviewMouseWheel`はビューポート上で
> **一度も発火しなかった**(RMB/MMB/Alt+LMB/WASDQEはすべて仕様どおり動作したにも関わらず)。
> VS自体がカーソル直下のHWNDへホイールをリダイレクトしていると見られ、他のマウスメッセージと
> 同様にヒットテストでこの子HWNDへ直接届く。WndProcでの直接受信へ戻すのが確定形であり、
> WPF側の`PreviewMouseWheel`購読は削除した(実機で機能しないコードを残さない)。

**座標単位**: `WndProc`の`LPARAM`はサーフェスのクライアント座標の**物理ピクセル**、WPFの
`GetPosition`は**DIP**(1/96インチ)。スワップチェーン・ビューポート・ピッキングレイはすべて物理
ピクセルなので**物理ピクセルを正本**とし、WPF由来の座標は
`PresentationSource.FromVisual(...).CompositionTarget.TransformToDevice`で変換してから`PointerEvent`を
作る(150%スケーリングで1.5倍ずれる)。ホイール量(`HIWORD(wParam)`、`WHEEL_DELTA=120`単位)は
位置情報を持たないため、この座標変換とは無関係。

- 状態機械は仕様§12の優先順位(1. Modal操作 2. ナビゲーション 3. ギズモ操作 4. 選択
  5. コンテキストメニュー)を状態遷移として実装する。RMBドラッグ中は選択・ギズモ操作を開始しない。
  ドラッグ開始で`SetCapture`、終了・`Esc`キャンセルで解放。
- WPF/Win32型に依存しない入力イベント型(ボタン・座標・修飾キー・ホイール量)を定義し、
  状態機械・カメラ数学・ギズモ判定は`RenderSurfaceFocusPolicyTests`と同様にVS無しでテストする。

**キーボード(ユーザー指定の方式)**: VS標準のツールウィンドウフォーカスモデルに従う。

- ビューポートをクリックしたら、WPF側のフォーカス可能要素(`SceneDesignerToolWindowControl`内の
  ホストGrid)へ`Keyboard.Focus`する。これは「ユーザーがそのツールウィンドウを操作した」通常の
  アクティブ化であり、他のVSツールウィンドウと同じ振る舞い。
- **ネイティブ子HWNDは引き続きWin32フォーカスを一切取らない**。`RenderSurfaceFocusPolicy`
  (`MA_NOACTIVATE`・`WM_SETFOCUS`即時返却・`AllowTabInto=false`)は無改変。
- W/E/R/F/V/N/Q/A/S/D/Esc等の単発・ホールドキーは、そのWPF要素の`KeyDown`/`KeyUp`で処理する。
  **フォーカスを持たないときは一切反応しない**(コードエディタでWを打ってもツールは切り替わらない)。
- コードエディタからフォーカスを奪う自動アクティブ化・`SetForegroundWindow`・`Activate()`は行わない
  (F5起動フォーカス問題の再発防止と同じ原則)。
- 修飾キー(Shift/Ctrl/Alt)はイベント時点の`Keyboard.Modifiers`(必要ならWin32 `GetKeyState`)で
  読む押下状態参照のみで、フォーカスに依存しない。
- RMBフライ中のWASDQEは、RMB押下(=クリックでフォーカス取得済み)後のWPF `KeyDown`/`KeyUp`で
  押下集合を管理し、描画ティックで移動へ積分する。`WM_KILLFOCUS`相当(WPF `LostKeyboardFocus`)で
  押下集合をクリアし、キーの取りこぼしによる走り続けを防ぐ。

### 3. ピッキングはCPUレイキャスト(選択・頂点スナップ・面スナップを同一クエリ層で)

`SceneRenderObject`はCPU側に`SimpleMesh`(positions/indices)とワールド行列を既に保持しているため、
GPUピッキングは使わず、新規の純粋クラス`ScenePicker`でCPUレイキャストを行う。

- レイ生成: スクリーン座標→NDC→view/proj逆変換(unproject)。Orthographic時は平行レイ。
- 三角形交差: Möller–Trumbore。全オブジェクトの全三角形を線形走査し最近ヒットを採用する
  (現行のシーン規模では加速構造は不要。必要になったら後続ADRで導入を判断する)。
- 同一クエリ層に仕様§7のスナップクエリを実装する:
  頂点スナップ=スクリーン半径内の最近頂点(Position のみ変更)、
  面スナップ=最近ヒットの交点+面法線(Position+Rotation整列、`AlignRotationToNormal`設定で
  Rotation整列のみ無効化可)。
- ギズモハンドルは三角形メッシュではなく解析形状(円柱・トーラス断片・クアッド)で先行判定し、
  仕様§12の優先順位(ギズモ操作 > 選択)を実現する。

### 4. ギズモ描画: 深度無効オーバーレイの第2 PSOと`CompoundGizmo`合成

`SceneRenderer`に第2のパイプラインステート(オーバーレイパス)を追加する: 深度テスト無効
(常に手前)、ラインとソリッドの両トポロジ、ホバー/アクティブ状態の色は既存`Selected`フラグと
同様のper-object定数で切り替える。ハンドル形状(矢印・リング・クアッド・円錐等)は
`GizmoMeshFactory`を拡張した手続き生成。ギズモのスクリーン一定サイズは
`FocusDistance * tan(FovY/2) * 定数`のスケーリングで実現する。

クラス構造は仕様§8の合成モデルをそのまま採用する(置き換えではなく合成):

```text
CompoundGizmo
├─ TransformGizmo(Move/Rotate/Scaleハンドル。全対象共通)
└─ PropertyGizmo(Component固有: FrustumGizmo、RangeGizmo等。対象Componentがあるときだけ合成)
```

修飾キーの適用可否は仕様§11の表に従う(Snapは両方、FollowViewはTransformのみ既定、Duplicateは
PropertyGizmoに原則不適用)。

### 5. 編集トランザクション: ドラッグ=プレビュー、MouseUp=1コミット(仕様§16〜19)

ADR-0040決定事項9を具体化する。新規`EditTransactionController`が1ユーザー操作を1トランザクション
として管理する。

```text
MouseDown(ハンドル確定)
  → BeginTransaction: 対象EditorNodeId・元値・snapshot.DocumentVersionを捕捉(in-flightロック)
Drag
  → プレビュー: SceneRenderer.UpdateObjectTransform(NodePath, Matrix4x4)で表示だけ更新
  → トランザクション中に届いた新snapshotは適用せず保留キューへ(SetTarget保留)
MouseUp
  → SceneEditOperationバッチ生成 → SceneSourceEditor.ApplyLatest(EditorNodeIdで1回リベース)
  → SceneTextBufferWriter.TryApply(1 ITextUndoTransaction)
  → 保留snapshotを解放し、通常の再解析ループが表示を確定
Esc
  → プレビュー破棄・元値へ復帰・保留snapshot解放(テキスト無変更なのでUndo履歴も汚れない)
```

- `SceneRenderer`へ追加するプレビューAPIは`UpdateObjectTransform`(既存の定数バッファ書き換えの
  範囲で実装可能)のみ。メッシュ・ロースターの変更はプレビューしない。
- 競合(仕様§18〜19): 操作中はロック(上記の保留)、コミット時に`DocumentVersion`が進んでいれば
  `SceneEditConflictMerger`が開始値→終了値の差分を最新値へ合成する。TransformはPosition加算/
  Rotation Quaternion差分/Scale倍率、Propertyは数値加算差分とする。ノード・Component消失、
  外部変更後の非editable式、非有限結果では**プレビューを破棄して通知**し、自動上書きしない。
- Undo/Redo(仕様§17)は追加実装なし: テキストUndoが戻ればADR-0040決定事項1のループが表示も戻す。

### 6. `SceneEditOperationKind`のフェーズ割当

| 操作 | 実装Phase | 備考 |
|---|---|---|
| SetComponentMember / RenameNode | 実装済み(ADR-0040) | 配線のみM0020 |
| SetTransform | Phase 3 | 内部的に`PositionX/Y/Z`等のフラットfloatへの`SetComponentMember`束として実装(メンバー欠落時は追記が必要になるため専用opとして扱う) |
| DuplicateNode | Phase 5 | 対象Nodeの`ObjectCreationExpression`を丸ごと複製し兄弟末尾へ挿入(`ChildrenListLocation`アンカー使用) |
| AddComponent / RemoveComponent | Phase 6 | Camera/Lightギズモが必要とする範囲のみ |
| AddNode / RemoveNode / ReparentNode / ReorderNode / AssignAsset | M0020スコープ外 | ヒエラルキー編集マイルストーン(将来)で実装 |

### 7. `NavigationPreset`: 物理入力と操作概念の分離(仕様§1/§3)

「物理入力(ボタン+キー+修飾)→概念操作(`Look`/`Fly`/`Pan`/`Orbit`/`Dolly`/`Zoom`/
`AdjustNavigationSpeed`/`FocusSelection`/…)」のバインディングテーブルをデータとして定義し、
状態機械は概念操作だけを見る。M0020では`Default`(仕様§4/§21の表そのもの)と`Custom`の器のみを
実装し、Unity/Unreal/Blender/Mayaプリセットの具体値充填は保留する(プリセット差分はテーブルの
データ差分に閉じるため、後から追加してもアーキテクチャに影響しない)。`InvertLookX`/`InvertLookY`は
独立した設定項目(仕様§4.1)。

### 8. 永続状態の保存先はVS `WritableSettingsStore`(仕様§3/§20)

`GameEngineLayout.cs`の既存前例(`ShellSettingsManager`→`SettingsScope.UserSettings`、コレクション
`CSharpGameEngine`)に倣い、コレクション`CSharpGameEngine\SceneEditor`へ7項目
(`ActiveTool`/`TransformSpace`/`ProjectionMode`/`NavigationPreset`/`NavigationSpeedMultiplier`/
`InvertLookX`/`InvertLookY`、Scene-Editor-Design.md §10)のみを保存する。
カメラ姿勢・`FocusPosition`・選択・ギズモ表示状態はセッション内のみ
(`SceneKey`別の永続化は将来判断)。**`SceneDeclaration`(C#ソース)へはSceneView状態を一切
書き込まない**(仕様§20)。`FollowView`/`Duplicate`/`VertexSnap`/`SurfaceSnap`/`FastNavigation`/
`PrecisionNavigation`はキー押下中のみ有効の一時状態であり、永続モード化しない(仕様§3)。

### 9. Component認識の一般化と語彙ギャップ

`SceneModelRenderObjectBuilder`の完全修飾名ハードコード(`global::UserEcsProject.Transform`)を、
「Component型完全修飾名→エディタ上の意味(Transform/メッシュ/カメラ/ライト)」を宣言する
`SceneComponentBinding`表へ置き換える(M0020 Phase 0。実シーンの
`global::CSharpGameEngine.Runtime.Windows.Transform`が認識されないバグの修正を兼ねる)。

語彙の現状: エンジン標準(`CSharpGameEngine.Runtime.Windows`)にあるのは`Transform`/`Camera`/
`PrimitiveMesh`/`StaticMesh`のみ。**ライト(DirectionalLight/PointLight/SpotLight/AmbientLight/
HeightFog)はすべて`UserEcsProject/Components.cs`(L9)にしか存在せず、RectLightは皆無**。したがって:

- PropertyGizmoの第一対象はエンジン標準の`Camera`(FrustumGizmo)とする。
- **(Phase 6で決定済み)** ライトギズモ(仕様§10)はADR-0043によりL9型の完全修飾名Bindingを
  登録する。エンジン標準化はRendererハンドル所有を再設計する別ADRへ送る。
- 仕様§10.4のRect/Area Lightは語彙ギャップとして記録し、M0020スコープ外とする。

### 10. 選択の双方向化: `SceneSelectionOrigin.Viewport`の有効化

ビューポートのLMBピック結果を`SceneSelectionService.Select(..., Origin: Viewport, ...)`で発行し、
ヒエラルキーが選択を購読して追従する(現状のHierarchy→Viewport片方向が双方向になる)。
Ctrl+LMBは既存契約の複数選択(`NodeIds`+`PrimaryNodeId`)をそのまま使う。エコー抑制は既存の
`CorrelationId`方式。`F`(FocusSelection)は選択ノードのワールド境界へ`FocusPosition`/
`FocusDistance`を合わせる。

### 11. レンダラー増分更新への段階戦略

Phase 0〜6は「`SceneRenderer`全再構築+状態外出し(決定事項1)」で成立させた。
Phase 7では`SceneDiff`を初めて消費し、Transform/非mesh Member変更だけなら既存meshと
Rendererを再利用して全階層world/boundsを更新する。mesh・Node構造・Component追加削除・表示世代の
飛びは全再構築へ戻す。`SceneRenderer`は2 frames-in-flightとし、allocator・object定数・
overlay定数/頂点・fence値をslot別に持つ。毎フレーム末尾の`WaitForGpuIdle`は廃止し、
slot再利用時だけ待つ。resize・資源破棄・disposeの全体待機は資源寿命契約として維持する。

## 代替案として検討したもの

- **全キーを`GetAsyncKeyState`ポーリングで処理**(フォーカス完全不要): `RenderSurfaceFocusPolicy`に
  一切触れない点は最も安全だが、「コードエディタにフォーカスがあってもマウスがビューポート上に
  あればWが効いてしまう」誤爆があり、ユーザー指定(フォーカスを持つときだけ効く)と矛盾するため
  不採用。修飾キーの押下状態参照(イベント時点の`Keyboard.Modifiers`)に限って残す。
- **VSコマンド+キーバインディング(.vsct)でW/E/R等を定義**: VS流儀でカスタマイズ性は高いが、
  修飾なし単键のグローバルキーバインドはコードエディタのタイピングと衝突しやすく、スコープ制御も
  ツールウィンドウ単位では効かない。ツールウィンドウ内WPFの`KeyDown`で足りるため不採用。
- **ネイティブ子HWNDにWin32フォーカスを持たせて`WM_KEYDOWN`を直接受ける**: コミット`a162478`で
  実証済みのVS全体フォーカス破壊バグを再発させる。`RenderSurfaceFocusPolicy`の存在理由そのもので
  あり不採用。
- **カメラ数学の共有ソース化**(`SceneModel`と同じ`<Compile Include>`方式): 採らない理由は依存の
  有無ではない(初版が書いていた「依存ゼロだから技術的には可能」という前提自体が誤りだった —
  決定事項1の訂正を参照)。エディタカメラはfly/pan/焦点距離など編集専用の拡張が必要で、ランタイムAPIを
  エディタ都合で変化させたくないため。`GizmoMeshFactory`の複製前例に従い複製を選択。
- **`Runtime/Transform.cs`の共有ソース化**(階層Transform合成のため): 技術的には通る(純
  `System.Numerics`で、`Matrix4x4.Invert`/`Decompose`もnet472に存在する)。しかし`public struct Transform`
  と`CSharpGameEngine.Runtime.Windows`名前空間をVSIXアセンブリへ持ち込むことになる。VSIXが現在
  linkしている共有ソースは`SceneModel/**`一本で、その存在理由は「SGとエディタが同じ**解析コード**を
  走らせる」ことにある。ここにランタイムのComponent型を足すと、アナライザーが文字列としてしか見ない
  型名の第二定義が生まれ、VSIXがエンジンランタイム型をホストしていると主張することになる。Phase 0 の
  階層合成は`Local * ParentWorld`の乗算1個であり型を必要としないため、式だけを写した。
  **Phase 3で再検討し、共有ソース化は採らず、VSIX内の純粋層`SceneTransformMath`へ同じrow-vector式と
  `TryCalculateLocal`相当を実装してテストで固定する判断へ確定した**
  ([[ADR-0042-Scene-Editor-Gizmo-Axis-Handedness]])。
- **GPUピッキング**(IDバッファ読み戻し): Phase 7の現行レンダラーは2 frames-in-flightだが
  ID buffer/readback pathを持たず、CPU側に全メッシュが既にあるため、CPUレイキャストの方が
  実装・テストともに小さい。頂点・面スナップにも同一の幾何クエリを流用できる。
- **ドラッグ中の逐次テキスト書き込み**: 1ドラッグでUndo履歴が数百件に汚染される
  (ADR-0040決定事項9で棄却済み。本ADRはプレビュー/コミット分離の具体化)。
- **編集用に`SceneModel`を直接書き換える正本化**: ADR-0040決定事項1(C#テキスト単一正本)への
  違反。棄却済み。

## 帰結

### 得られるもの

- 仕様§3の原則どおり永続モードが7項目に限定され、その他はすべて「押している間だけ」の直交修飾に
  なる。状態機械・カメラ・ピッキング・ギズモ判定が純粋層に分離され、VS無しでユニットテストできる。
- 既存の未配線部品(`SceneEditOperation`/`ApplyLatest`/`SceneTextBufferWriter`/
  `SceneSelectionOrigin.Viewport`/`SceneNode`の書き込みアンカー)がすべて設計どおりの位置に収まり、
  新規発明はビューポート入力・ギズモ・プレビューAPIに限定される。
- `RenderSurfaceFocusPolicy`は無改変のまま、キーボード操作はVS標準のツールウィンドウ
  フォーカスモデルで成立する(過去の実バグを踏み直さない)。

### 失うもの

- キー操作の前に「ビューポートを一度クリックしてフォーカスを与える」1アクションが必要になる
  (Unityも同じ挙動であり、仕様§1の設計原則と矛盾しない)。
- meshまたはScene構造が変わる再解析では安全側に倒してレンダラーを全再構築する。
- CPUレイキャストは巨大シーンでは線形走査コストが顕在化しうる(加速構造は必要になってから)。

## 検証結果(実際に実行して確認)

### Phase 0(2026-08、決定事項1・9・11の一部を実装)

```
dotnet build CSharpGameEngine.slnx -c Debug → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx -c Debug --no-build
  → 全8プロジェクト 622件成功・失敗0
    (ベースライン610 − SceneFileTests 9 + 階層合成7 + SceneComponentBinding 5 + SceneCameraFraming 9)
```

実装したのは決定事項1の一部(`SceneRenderer`からのカメラ外部注入=`SetCamera`+`ConstantsDirty`、
`SceneCameraFraming`への視点ポリシー移設)、決定事項9のバインディング表化、決定事項11の前提整備
(空オブジェクトフレームの許容)。`SceneViewState`・入力・ピッキング・ギズモはPhase 1以降。

`SceneCameraFramingTests.FrameBounds_DegenerateBoundsAndAspect_ProduceFiniteMatrices`が実装バグを
1件検出した: 退化半径では`far`が`near`を下回り`Matrix4x4.CreatePerspectiveFieldOfView`が
`ArgumentOutOfRangeException`を投げていた(`far`に下限ガードを追加して修正)。

実機VS Experimental Instanceでの目視確認は[[M0020-Scene-Editor-Interaction]]の検証節に記録する。
Phase 1以降の検証結果も同様に本節へ追記する。

### Phase 3(2026-08-16、決定事項3・4・5・7・11の一部を実装)

Moveギズモ(軸3+平面3)、スクリーン一定スケール、解析形状ヒット、軸/平面拘束ソルバ、深度無効の
line/solid overlay PSOを実装した。`EditTransactionController`は開始snapshot・対象・元world値を捕捉し、
ドラッグ中のRenderer定数だけを更新する。MouseUpでは最新Roslyn世代へNode IDを1回だけリベースし、
Positionメンバーだけを`ITextUndoTransaction` 1件でlive bufferへ反映する。対象消滅、解析エラー、
適用直前の連続text変更ではプレビューを破棄してツールウィンドウとVSステータスバーへ通知する。

ハンドネス判断とTransform数学境界は[[ADR-0042-Scene-Editor-Gizmo-Axis-Handedness]]で確定した。
最終の全体検証はDebug build警告0/エラー0、全8テストプロジェクト808件成功・失敗0。
HWND/D3D12/VS live buffer/Undo配線はユニットテスト不能であり、Experimental Instance確認は
[[M0020-Scene-Editor-Interaction]]のPhase 3表に未実施として残す。

### Phase 7(2026-08-16、決定事項5・11と競合契約を完了)

`SceneDiff`増分分類、mesh再利用と親子world再計算、2 slot fence、Transform/Propertyの
開始値ベース差分マージ、Esc全7入力状態を新規8テストで固定した。VSExtension.Tests 389件、
全8テストプロジェクト881件が成功し、Debug buildは警告0/エラー0。
HWND/D3D12/live buffer/Visual Studio UndoのObservedはExperimental Instance未実施のため
**Unknown**であり、[[M0020-Scene-Editor-Interaction]]のPhase 7表へ残す。

## 関連機能

- F-L8-011 — シーンエディタ インタラクション基盤(Status: Implemented、Phase 0〜7+後続Task C実装済み)
- F-L8-010 — Scene デザイナー: SceneModel(ライブRoslyn)描画(前提基盤)

関連: [[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]、
[[ADR-0026-Camera-Geometry-Ruf-Integration]]、[[ADR-0008-VSIX-InProcess-Vortice-Renderer]]、
[[M0019-CSharp-Scene-DSL]]、[[M0020-Scene-Editor-Interaction]]、`Docs/シーンエディタの仕様.md`、
`Docs/Specifications/Scene-Editor-Design.md`
