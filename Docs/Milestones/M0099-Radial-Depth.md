# M0099 — 対数（放射）深度レンダリングをネイティブに実装する
- 日付: 2026-10-03
- 状態: 実装中（Phase 0 完了）

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
| Phase 4 | レンダラー（対数 PSO、補助 VP、プレビューごとの深度方式） | 未着手 |
| Phase 5 | Camera コンポーネント、Scene JSON、エディタの追従 | 未着手 |
| Phase 6 | 観察ビュー | 未着手 |
| Phase 7 | FeatureRunner の `rendering.radial-depth` と文書の照合 | 未着手 |

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
