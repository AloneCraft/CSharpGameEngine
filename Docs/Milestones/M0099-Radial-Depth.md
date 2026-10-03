# M0099 — 対数（放射）深度レンダリングをネイティブに実装する
- 日付: 2026-10-03
- 状態: 完了（2026-10-04）

## 目的
Unity URP 用の対数（Radial）レンダリング資産 Omni（`D:\Unity\6.6\Omni\Assets\Omni`）を参考に、同じ機能をこのエンジンへネイティブに実装する。Omni は URP の Shader を変換・注入し Material を差し替える「アダプター」方式だが、このエンジンは Stage という単一の固定 ABI シェーダーを自前で持ち、既にカメラ相対の float で描いている。深度方式をカメラの性質として描画経路に組み込み、標準の描画は回帰させない。

範囲（ユーザー回答 2026-10-03）: ランタイムの対数深度カメラ（既定は標準、Camera ごとに選択）、エディタのランタイムビューの追従（UI 追加なし）、外から見る観察ビュー。曲線は Omni の Smooth だけ（Threshold は採らない）。調査中に見つけた既存バグ（グリッドと旧フォグがカメラに付いて動く）を先に別コミットで直す。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| Phase 0 | 既存バグ: ワールドグリッドと旧フォグがランタイムでカメラに付いて動く | 完了 |
| SDK | Phase 0（エンジンの修正）に合わせて SDK パッケージを 0.1.13 に上げた | 完了 |
| Phase 1 | 標準モードの基準フレーム（SHA-256）を固定する | 完了 |
| Phase 2 | CPU 側の数式（`RadialDepthCurve`、`RadialDepthProjection`）、ADR-0076、Omni 仕様 | 完了 |
| Phase 3 | シェーダー ABI（対数用のエントリー、FrameCB の深度方式パケット） | 完了 |
| Phase 4 | レンダラー（対数 PSO、補助 VP、プレビューごとの深度方式） | 完了 |
| Phase 5 | Camera コンポーネント、Scene JSON、エディタの追従 | 完了 |
| Phase 6 | 観察ビュー | 完了 |
| Phase 7 | FeatureRunner の `rendering.radial-depth` と文書の照合 | 完了 |
| SDK | 対数深度（Phase 2〜7 と保守的深度の追補）に合わせて SDK パッケージを 0.1.14 に上げた | 完了 |

## Phase 0. グリッドと旧フォグのカメラ相対化
### 観測した事実（Fact）
- ランタイムはカメラを描画空間の原点に置き（`LevelLoad` が `RenderCamera.CreateFirstPerson(Vector3.Zero)`）、物体をカメラ相対で渡す（`FixedTransform.TryToCameraRelativeMatrix`）。シェーダーの `input.WorldPosition` はワールド座標ではない。
- ADR-0075 のグリッドの線（`WorldGridWire(input.WorldPosition, …)`）と旧フォグの高さ（`input.WorldPosition.y`）は、それをワールド座標として使っていた。
- 再現（修正前に失敗）: カメラ相対で描いた床では主線の位置の画素が暗くならない（主線 126、面 99）。旧フォグはワールド座標の描画と最大 56 階調ずれる。
- 見逃した理由: `WorldGridGpuIntegrationTests` はワールド座標で `StageRenderer` を直接呼ぶ。M0098 の実機確認はカメラを動かさない静止画 1 枚だった。

### 実装
- シェーダー（`tools/ShaderGraphMaintenance/edits/render_origin_2026_10_03.py`）: `ObjectCB.WorldGridAnchor` を足し、線を `WorldPosition + WorldGridAnchor` から計算する。旧フォグの高さは `WorldPosition.y + FogParams0.z`。
- レンダラー: `StageRenderer.SetWorldGridAnchor(material, anchor)`（グリッドの間隔の範囲内を検証）と `SetRenderOriginAltitude(meters)`。`ObjectCB` は 160 → 176 バイト。
- 復旧: `RenderServer` に同名の API。`RenderFrameState` が記録・引き継ぎ・再生する。
- ランタイム: `LevelLoad` がグリッド材質を `EngineHost.RegisterWorldGridMaterials` に渡す。`EngineHost.SubmitEnvironment` が毎フレーム、カメラのフレーム内位置をグリッドの間隔で割った余りを Q64.64 で正確に求め（`ReduceToGridCell`）、カメラの高度とともに渡す。
- ワールド座標で描く呼び出し側（エディタの分離プレビュー、レンダラーを直接使うテスト）は 0 のままで、出力は変わらない。

### テスト
- `WorldGridGpuIntegrationTests.WorldGrid_Warp_CameraRelativeRenderingKeepsTheLinesOnTheWorldGrid`、`LegacyFog_Warp_CameraRelativeRenderingThinsWithTheWorldHeight`（修正前に失敗）。
- `RenderOriginTests`（`EngineHost` を通した端から端）: 0.75 m と 10000.75 m の視点で画素が一致し、半セルずらすと変わる。高度が 3 m 違えばフォグが変わる。余りの正確さ 6 件（±1e15 m を含む）。`EngineHost` からの送信を外すと端から端の 2 件が失敗することを確かめた。
- `RenderFrameStatePersistenceTests.RenderOriginSettings_AreInheritedUntilTheyChange`。
- `ShaderGraphCompilerTests` のゴールデンの Stage HLSL ハッシュを意図して更新した。
- `CSharpGameEngine.Windows.Tests` 368/368（GPU を含む。既存の厳密画素テストは変化なし）。

### 実機の確認
- ThirdPerson テンプレートを WARP で起動し、`CGE_AUTOMATION_INPUT=W@5-35` で 40 フレーム歩かせて撮影した（カメラは (−4, 3.09, 0) → (12.157, 0.985, −0.113)、D3D12 エラー 0）。
- 修正前のビルド（HEAD の一時 worktree）で同じ操作をすると同じ位置に着き、床の主線が画面のちょうど中央、つまりカメラの真下を通る（格子がカメラに付いている）。修正後は線がワールドの格子に乗り、中央からずれる。壁の上端と横線も一致する（`.artifacts/M0099/phase0/walked_before_after_floor.png`、`walked.png`、`walked_before_fix.png`）。
- スポーン視点はカメラがほぼ整数座標（x = −4、高度 3.085 m）にあるので、修正前でもずれがほとんど見えない。M0098 の静止画の確認で見逃したのはこのためと考えられる（推測）。

### 既知の制約
- 余りはカメラの参照フレームで求める。別の参照フレームに置いた物体の格子は、カメラのフレームの格子に揃う。

## Phase 1. 標準モードの基準フレーム
- `StandardModeGoldenFrameTests`: `RenderServer`（ランタイムが使う公開 API）で 7 場面を WARP 192×108 で描き、バックバッファの SHA-256 を固定した。対数モードを足す以後のすべてのコミットで一致を要求する。
  - `legacy-lit`: 型付きの平行光・点・スポット・面光源（影あり）、旧フォグ、グリッドの床、金属の球、箱、壁。
  - `environment`: 地球の大気、スカイライト、物理フォグ、太陽のカスケード影、GI、AO、物理カメラ露出（EV100 14）と Unreal filmic（4 フレーム）。
  - `selection`、`base-color`、`normal`、`wireframe`: 表示モード。
  - `previews-and-overlay`: カメラのサムネイル 2 枚（1 枚は物体を除外）と overlay の三角形・線。
- 固定する前に全場面の画像を書き出して確かめた。テストのメッシュの接線を全頂点 (1,0,0) にしていたため ±X 面で TBN が壊れて斑点が出ていた（テスト側の誤り）ので、法線に直交する接線を作るよう直した。2 回続けて実行して一致した。

## Phase 2. CPU 側の数式
- `src/CSharpGameEngine.Rendering/RadialDepth.cs`（float だけ。ADR-0063）:
  - `RenderDepthMapping`（Standard / Radial / RadialObserver）。
  - `RadialDepthCurve`: `G(r) = log2(1 + r/T)`（Kahan の log1p で `1 + x` の丸めを補正）、観察者の `Map = T·G`。
  - `RadialDepthProjection`: 錐台の角の距離、`InverseMaximumG`、`EncodeDepth`（0.99999994 で頭打ち）、補助の near/far と投影、観察者の世界半径。
- `RadialDepthTests`（19 件）: 倍精度の基準と 6e-7 以内（1e-30〜1e30）、単調性と原点の傾き、錐台の角で 1 未満、不正な入力、精度予算（near 0.1 m・far 1e18 m で約 3e-6）、観察者の半径、補助の投影が逆行列を持つこと、通常の範囲では標準と同じ行列。
- 事実として確かめた前提: far 1e18 m の標準の投影は NDC z = 1 を逆投影できない（`StandardProjection_WithAHugeFar_CannotBeUnprojected`）。これが補助の投影の理由。
- ADR-0076 と `Docs/Specifications/Omni.md` を書き、Implementation-Status に F-L5-038（Implementing）と F-L5-039（Planned）を登録した。

## Phase 3. シェーダー ABI
- FrameCB の末尾に `DepthMapping0 = (mode, T, 1/G(rMax), FarZ)` と `DepthMapping1 = (観察者の中心, 予約)` を足した（`tools/ShaderGraphMaintenance/edits/radial_depth_2026_10_04.py`、912 → 944 バイト、スロット幅 1024 は不変）。
- `StagePresentationShaderSource` に `RadialLog2OnePlus`（CPU と同じ Kahan の log1p）、`RadialSafeLength`、`RadialG`、`EncodeRadialDepth`、`IsBeyondRadialFarPlane`（物理のビュー投影の w が FarZ を超える画素を捨てる）と 4 エントリーを足した。
  - `RadialPSMain` / `PreviewRadialPSMain` / `PreviewCleanRadialPSMain`: それぞれ既存の `PSMain` / `PreviewPSMain` / `PreviewCleanPSMain` をそのまま呼び、`SV_Depth` を足す。
  - `RadialObserverVSMain`: `VSMain` を呼び、`SV_Position` だけを写像後の位置で置き換える。
- `ShaderArtifactAbi`: `stage-v8`、エントリー 9 → 13。`ShaderGraphCompilerTests`（エントリー数、存在、対数の画素エントリーが `SV_Depth` を書く構造体を返すこと、ゴールデンの SHA-256 を意図して更新）。`Shader-Graph.md` の古くなっていたエントリー一覧を直した。
- 描画は変わらない: `Windows.Tests` 369/369（`StandardModeGoldenFrameTests` を含む）、ShaderArtifacts 7/7、ShaderGraph 19/19、ShaderGraph.Cli 13/13、VSExtension 1079/1080（失敗はユーザーが作業中の未追跡の探索用テスト `GizmoVisibleHitConsistencyTests.Exploration_PrintMoveArrowPixelsThatHitNothing` だけで、この変更とは無関係）。
- 互換性: 以前の Stage グラフを複製した独自のグラフは FrameCB に `DepthMapping0/1` が無く、接尾辞がコンパイルできない（Phase 7 で CHANGELOG に書く）。

## Phase 4. レンダラー
- `Graphics/Camera` に `DepthMapping` と `RadialTransitionMeters`（既定 1 m）。`RenderCameraPreview` に `DepthMapping`・`Radial`・`RadialCenter`。
- `StageRenderer`:
  - 対数 PSO 7 つ（Shipping / Presentation / Wireframe / CameraPreview / CameraPreviewWireframe / Observer / ObserverWireframe）を列挙型で添字を付けた配列にまとめ、生成・ホットリロードのロールバック・破棄を一か所で扱う。標準の PSO のコードは変えていない（`CreateMeshPipelineState` に省略可能な頂点シェーダーの引数を足しただけ）。
  - `SetCamera` が対数のパラメーターを検証して `RadialDepthProjection` と補助のビュー投影を作る。不正な値と観察者のカメラは例外。`SetViewProjection` は標準へ戻す。
  - `WriteDepthMapping` が毎フレーム（露出と同じ扱い）パケットを書く。破棄の深さは `FarZ·(1 + 1/1024)`（投影の far が残した画素を丸めで捨てないため。`RadialDepthProjection.FarDiscardDepth`）。
  - 空の方向・太陽のカスケード・AO 前パスは `InvertibleViewProjection`（標準ならカメラの行列そのもの）。GI 取込みの写しは方式を標準に戻す。
  - プレビューごとにパケットと PSO を選ぶ。テスト用に `ReadDepthForTesting`。
- テスト: `RadialDepthGpuIntegrationTests`（11 件）、`ShaderArtifactHotReloadTests.RenderServer_DebugHotReload_RebuildsTheRadialPipelines`、テスト補助 `WindowsOnlyTheoryAttribute`。
- 調べて捨てた前提:
  - 「標準深度は 20 km 先の 0.5 m 差を区別できない」は WARP では成り立たなかった（全面が赤で正しく描けた）。標準が 1.0 に丸めて何も描けない 1e15 m と 5e18 m を判別に使い、20 km は対数側だけを確かめる。
  - 環境の場面で標準と対数の画素が違ったのは、同じレンダラーで続けて描いたため GI のプローブの履歴（ヒステリシス）が引き継がれたからだった。描画ごとに新しいレンダラーを使うとビット一致した。

## Phase 5. Camera コンポーネント、Scene JSON、エディタの追従
- シーンの `Camera` コンポーネントに `CameraDepthMapping DepthMapping`（既定 Standard）と `RadialTransitionMeters`（既定 1）を足した。Scene JSON は既定と違う値だけを書くので、既存のシーンは無変更で標準のまま。生成コードとエディタは列挙型と数値を汎用の規則で読む。
- `LevelLoad.ApplyDepthMapping`: 読み込み時に（露出と同じく）シーンカメラの方式と T を描画カメラへ写す。対数ならシーンカメラの NearZ/FarZ も使う。エディタのランタイムビューでは毎フレームの経路でもエディタの near/far を上書きした後にこれを当て直す。
- カメラのサムネイル: 描くのは未ビルドのゲームの分離プレビューだけで、そこでの対数は非目標なので標準のまま（計画の「サムネイルも追従」は、ランタイムビューがサムネイルを描かないことが分かったので取りやめた）。
- テスト: `SceneCameraTests`（既定値）、`SceneJsonRuntimeValuesTests.Camera_ReadsTheRadialDepthMappingAndKeepsStandardWhenAbsent`、`EditorRuntimeViewTests` の 2 件（ゲームが対数になりシーンの範囲を使う。エディタカメラが同じ姿勢のゲームと同じ画素を描き、2 フレーム目の経路でも対数とシーンの範囲を保つ）。毎フレームの経路での当て直しを外すとエディタのテストが失敗することを確かめた。

## Phase 6. 観察ビュー
- `RadialObserver` コンポーネント（有効、小窓の矩形を画面に対する割合で、FovY、NearZ、中央カメラの模型を描くか）。既定は右下の小窓。
- `RadialObserverSync`（Presentation、`CameraOriginSync` の後）: 観察者のノードの姿勢をカメラ相対で取り（`LightTransform.TryGetRectanglePose`、Q64.64 からの唯一の変換経路）、アクティブなカメラが対数でエディタカメラが無いときだけ `RadialObserverView.TryCreatePreview` でプレビューを作る。far は `|E − C| + 1.05·T·G(rMax)`。
- `EngineHost`: そのフレームのプレビュー（最大 3 枚）を `SetCameraPreviews` へ出す。観察者がいなくなったら空のリストを一度出して消す。
- `StageRenderer`: overlay のフレームごとの容量に模型の領域（プレビューごとに 16 頂点）を足し、`SetCamera` で中央カメラの向きと画角を保持して、`PrepareObserverMarkers` が観察者のプレビューごとに錐台の線を書く。overlay のパスでプレビューの矩形と観察者の定数で描く。
- テスト: `RadialObserverTests`（4 件）、`RadialDepthGpuIntegrationTests.ObserverPreview_DrawsTheCompressedWorldAndMarksTheCentre`。画像を書き出して、1e6 m 先の半径 3e5 m の球が放射方向に強く圧縮された縦長の形で写り、小窓の中央に中央カメラの模型が出ることを目で確かめた。

## Phase 7. ステージ、実機の確認、照合
- Visual Stage `rendering.radial-depth`（WARP 必須）: `RadialDepthTests`、`ShaderGraphCompilerTests`、Windows.Tests の対数・標準基準・観察者・エディタ・シーン・ホットリロードのテスト、Rendering パックの参照シーン。Roadmap の Rendering 表に F-L5-038（レベル 6）と F-L5-039（レベル 7）を足し、`FeatureStageCatalogTests` を更新した。`dotnet run --project tools/CSharpGameEngine.FeatureRunner -- run rendering.radial-depth --adapter warp` が合格した（`.artifacts/FeatureStages/20261003T154551274Z-single-b6d439afb5e14c29867861b963d5fa05.run-result.json`）。
- 実機: ThirdPerson テンプレートのシーンを一時的に対数カメラ（near 0.01 m・far 1e12 m）と観察者に変えて WARP で 40 フレーム歩かせ、撮影した（D3D12 エラー 0）。メインビューは標準の撮影とほぼ同じ（小窓の外で平均 0.2 階調）で、小窓には圧縮したレベルと中央カメラの模型が写った（`.artifacts/M0099/phase7/radial_walked.png`、`radial_inset_zoom.png`）。撮影後にシーンを元に戻した。
- 文書: Rendering 仕様のステージ表、Architecture、CHANGELOG（ABI `stage-v8` で古い Stage グラフの複製が読めなくなること、Early-Z を失うこと）、Conformance、Implementation-Status（F-L5-038/039 は Implemented、TestVerified と RuntimeVerified (WARP)）。

## 完了の判定
- 実装したもの: Phase 0〜7（上記）。
- 除外したもの: [Omni 仕様](../Specifications/Omni.md) の非目標（Threshold 曲線、通常カメラの頂点の写像、カリング、モーションベクトル、正射影の対数グリッド、エディタの切替 UI、天文規模のエディタのナビゲーション、分離プレビューでの対数、観察者の全画面表示とその中のピッキング・ギズモ、天体間の影、保守的深度）。
- 意図と観測の一致: Matches（F-L5-038、F-L5-039）。標準モードは `StandardModeGoldenFrameTests` の 7 場面が全フェーズで不変。
- 残っていること: 対数 PSO の性能の計測（Early-Z が効かない）。

## 追補（2026-10-04）. 対数の主パスで Early-Z を取り戻す
- 残っていた「性能の計測」を行った。`StageRenderer` にテスト用の GPU タイムスタンプ（主パスの描画の前後、有効にしない限り命令を積まない）を足し、`RadialDepthPerformanceEvidence`（`CGE_PERFORMANCE_EVIDENCE=1` のときだけ動く。`PerformanceEvidenceFactAttribute`）で測った。
- 結果（RTX 4060 Ti、全画面の板 24 枚、1280×720）: 手前から奥で標準 0.132 ms、`SV_Depth` の対数 2.972 ms。奥から手前ではどちらも約 2.98 ms。コストは Early-Z の喪失だけだった。
- 対策: 保守的深度（ADR-0076 追記）。対数 0.171 ms（標準 0.124 ms の 1.4 倍）。WARP は変わらない。
- 調べて分かったこと: 深度クリップを無効にすると near 面をまたぐ三角形の補間が変わった（1 階調）。標準の near/far を `SV_ClipDistance` で出すと箱の差は消えたが、床に散らばった 60 画素・最大 1 階調の差が残った。クリップの無い場面ではビット一致したので、クリップ距離による切断の丸めと確定し、不変条件を「クリップされない三角形はビット一致、クリップされる三角形は 1 階調以内」に改めた。
- 計測した前提の事実: `SV_DepthGreaterEqual` を出す画素シェーダーは DXIL の検証で `SV_Position` を centroid・no-perspective で受ける必要があった。
