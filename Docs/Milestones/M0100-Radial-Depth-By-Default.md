# M0100 — 対数深度を既定にする
- 日付: 2026-10-04
- 状態: 完了（2026-10-04）

## 目的
ユーザーの指示（2026-10-04）「最終的には対数になるように順々にテスト、修正していって」（期限 24 時間）。M0099 で選択式として入れた対数（放射）深度を、すべての描画経路の既定にする。標準は明示的な選択として残す（標準の基準フレームと、対数のテストで比べる相手）。決定は [ADR-0076 追記](../Decisions/ADR-0076-Native-Radial-Depth-Mapping.md)、契約は [Omni 仕様](../Specifications/Omni.md)。

## 描画経路
| 経路 | 深度方式を決めるもの |
|---|---|
| ゲームとエディタのランタイムビュー | シーンの `Camera` コンポーネント（`LevelLoad.ApplyProjection`） |
| エディタの分離プレビュー（未ビルドのゲーム）と Asset Preview | `RenderPreviewRenderSession` が行列だけを渡す（`SetViewProjection`） |
| カメラのサムネイル | `RenderCameraPreview`（行列） |
| ビューア CLI、TestHost、`StageRenderer`/`RenderServer` を直接使うコード | 描画カメラのファクトリー |
| 範囲外 | `ModelRenderer`/`TriangleRenderer`（テストと `rendering.baseline` の基礎の描画器。製品の経路ではない） |

## 進捗
| 段階 | 内容 | 状態 |
|---|---|---|
| 1 | 描画カメラのファクトリーの既定を対数にする | 完了 |
| 2 | シーンの Camera コンポーネントの既定を対数にする | 完了 |
| 3a | 行列で渡されるビューを対数で描くレンダラーの入口 | 完了 |
| 3b | エディタの分離プレビュー、Asset Preview、カメラのサムネイルを対数にする（プロトコル、プレビューホスト、VSIX） | 完了 |
| 4 | 文書、SDK の版 | 完了 |

## 段階 1. 描画カメラのファクトリー
- 実験: `Camera.CreateDefault`/`CreateFirstPerson`/`CreateBoundsFit` の既定を対数にして Windows.Tests を全件流した。失敗は 407 件中 4 件で、どれも標準を前提か比べる相手として使うテストだった（`StandardModeGoldenFrameTests`、`Radial_SeparatesNearlyCoplanarSurfacesFarAway` の「標準なら潰れる」側の 2 件、`RadialObserverTests.Preview_IsOnlyMadeForARadialCamera`）。
- 実装: ファクトリーが `DepthMapping = Radial` を設定する。上の 4 件は標準を明示した。不変条件 `CameraTests.Factories_ChooseRadialDepth`。
- 結果: Windows.Tests 407/407（性能の証拠 1 件はスキップ）。照明・影・AO・GI・空・スキニング・ギズモ・デバイス復旧のテストは変更なしで合格した。FeatureRunner の 11 ステージ（baseline、environment-lighting、device-recovery、performance、vsix.rendering-preview など）も WARP で合格した。

## 段階 2. シーンの Camera コンポーネント
- 実装: `Camera.DepthMapping` の既定を `Radial` にした。キーの無いシーンは対数になる。エディタのランタイムビューは、対数ではエディタとシーンカメラの両方の範囲を含む（`LevelLoad.ApplyProjection`）。M0099 の「シーンカメラの範囲に置き換える」のままだと、既定が対数になったとき、エディタで引いた視点からシーンカメラの far（既定 300 m）より先が切れるため。
- テスト: `SceneCameraTests`、`SceneJsonRuntimeValuesTests.Camera_ReadsTheDepthMappingAndIsRadialWhenAbsent`（キーが無ければ対数、`"Standard"` なら標準）、`EditorRuntimeViewTests.EditorCamera_OnARadialSceneCamera_SeesBothItsOwnRangeAndTheScenes`。
- 結果: Windows.Tests 408/408、CodeAnalysis 175/175、SceneModel 175/175、FeatureRunner 154/154、VSExtension 1079/1080（失敗はユーザーが作業中の未追跡テスト 1 件で無関係）。
- 実機: ThirdPerson を WARP で 90 フレーム（W で歩いてジャンプ）動かし、既定（対数）と、シーンに一時的に `"DepthMapping": "Standard"` を足した標準を撮影した。D3D12 エラーは両方 0、カメラの最終位置は同じ。画像は 99.98% の画素でビット一致し、違う 167 画素も最大 2 階調（`.artifacts/M0100/step2/`）。撮影後にシーンを元に戻した。

## 段階 3a. 行列で渡されるビュー
- 分離プレビューは VSIX が作ったビュー投影の行列を渡す（`SetViewProjection`）。ギズモやピッキングとずれないよう、この行列はそのまま使う。
- 実装: `RadialViewParameters`（FovY、NearZ、FarZ、T）と `StageRenderer`/`RenderServer.SetViewProjection(行列, 位置, RadialViewParameters)`。透視の行列では W がビューの深さなので、保守的深度と補助の行列は深度の列だけを `M33·z + M43`（z = −W）で作り直す（`RadialDepthProjection.CreateConservativeRasterViewProjection`/`CreateAuxiliaryViewProjection`）。逆行列は要らず、X・Y・W の列は元の行列とビット一致する。`RenderFrameState` が記録し、デバイス復旧で再生する。
- テスト: `RadialDepthTests.ViewProjectionOnly_RebuildsTheMatricesACameraWouldBuild`（4 つの範囲・3 つの姿勢で `View · Projection` と完全一致、正射影は透視でないと判定）。`RadialDepthGpuIntegrationTests` の 4 件（行列で描いた画像と深度が対数カメラとビット一致、near 1 mm・far 1e7 m の環境ありの場面でも一致、正射影と不正な値を拒否、デバイス復旧後も同じ画像と深度で、深度が対数の値）。Windows.Tests 412/412、Rendering 65/65。

## 段階 3b. 分離プレビューとサムネイル
- プロトコル 8: `RenderPreviewCamera` に `DepthMapping`（`RenderPreviewDepthMapping`）と `RadialTransitionMeters`（既定 1）を足した。対数のカメラは、透視の行列、(0, π) の FovY、0 < NearZ < FarZ、正の T を持たなければ拒否する（標準へ黙って戻さない）。
- VSIX: カメラはすべて `RenderPreviewSceneBuilder.DescribePose` を通るので、そこで透視なら Radial、正射影なら Standard にする（Scene ビュー、Asset Preview、フレーム）。サムネイルはシーンカメラの `DepthMapping` と `RadialTransitionMeters` をシーンモデルから読み（`SceneCameraPreviewBinding`、無ければコンポーネントの既定 Radial と 1）、`DescribePose` で投影を載せる。
- プレビューホスト: 対数のカメラは `RenderServer.SetViewProjection(行列, 位置, RadialViewParameters)`、対数のサムネイルは矩形の縦横比で `RadialDepthProjection` を作って `RenderCameraPreview` に渡す。
- テスト: `RenderPreviewHostProtocolTests.ProtocolV8_ValidatesRadialCameras`、`RenderPreviewRenderSessionTests.WARP_RadialCameraDrawsTheStandardImageWithRadialDepth`（箱の画像は標準とビット一致し、中心の深度は標準 0.98 超・対数 0.2 未満。サムネイルも標準と同じ画像）、`RenderPreviewCameraPoseTests.Pose_ChoosesRadialDepthForAPerspectiveViewAndStandardForAnOrthographicOne`、`SceneCameraPreviewPolicyTests.Build_ReadsTheDepthMappingAndIsRadialByDefault`。Windows.Tests 413/413、VSExtension 1081/1082（失敗はユーザーの作業中の未追跡テスト）、Contracts 21/21、FeatureRunner 154/154。

## 探索. 実機の GPU での精度
- 対数が既定になったので、WARP では見えない Early-Z と GPU の演算を実機（RTX 4060 Ti）で確かめた。対数の GPU テストを一時的に実機で流すと 2 件が失敗し、原因はシェーダーの `log2` がハードウェアの近似命令だったこと（[Conformance](../Conformance.md) の同日の項、ADR-0076 追記）。`log2(1 + x)` を float の四則演算だけの級数にして、実機でも 25 件が全件合格した。
- 回帰の防止: `StagePresentationShaderSourceTests`（表示用のエントリーに超越関数を使わない）と `RadialDepthPrecisionEvidence`（オンデマンド、実機でも中心 4 ulp 以内）。

## 段階 4. 文書と SDK の版
- 文書: ADR-0076 追記（既定を対数に、段階 1〜3、GPU の log2）、Omni 仕様（状態、深度方式の表、エディタ、曲線、精度予算）、Rendering 仕様（分離プレビューの深度）、Editor-VSIX 仕様、Architecture、Implementation-Status、Reverse-Engineered-Spec、Conformance、CHANGELOG。
- SDK の版: 0.1.14 はまだどのフィードにも NuGet のキャッシュにも展開されていないので、M0100 も 0.1.14 に含め、版は上げない（M0099 の保守的深度と同じ判断）。
- 実機: ThirdPerson を RTX 4060 Ti で 90 フレーム動かした（対数と標準、D3D12 エラー 0、`.artifacts/M0100/hardware/`）。実機ではシミュレーションが実時間で進むので、二つの実行で歩いた距離が違い、画素の比較はできない（WARP では軌跡も画像も一致した）。対数の画像に前後の誤りや Z ファイティングは無い。

## 完了の判定
- 対数で描くもの: ゲームとエディタのランタイムビュー（シーンの Camera の既定）、エディタの分離プレビューと Asset Preview の透視のビュー、対数のシーンカメラのサムネイル、描画カメラのファクトリーを使うコード（ビューア、TestHost、テスト）。
- 標準のまま残すもの: 明示的に Standard を選んだカメラ、正射影のビュー（対数は透視が要る）、標準の基準フレーム、影・GI の取込み・overlay のパス（カメラのビューではない）、テストと基礎の描画器 `ModelRenderer`/`TriangleRenderer`。
- 意図と観測の一致: Matches。

## 探索. 遠景の Early-Z
- 対数が既定になったので、遠景の重なりの性能を測った。100〜1020 m の層で対数は標準の約 18 倍遅く（1.738 対 0.097 ms）、原因は単一の接点（8 m）の保守的深度が遠くで低すぎること（[Conformance](../Conformance.md) の同日の項）。接点を物体ごとにして 0.123 ms（標準 0.091 ms）にした（ADR-0076 追記「物体ごとの接点」、ABI `stage-v10`）。
