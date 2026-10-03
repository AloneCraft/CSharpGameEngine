# M0100 — 対数深度を既定にする
- 日付: 2026-10-04
- 状態: 進行中

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
| 3 | エディタの分離プレビュー、Asset Preview、カメラのサムネイルを対数にする | 未着手 |
| 4 | 文書、SDK の版 | 未着手 |

## 段階 1. 描画カメラのファクトリー
- 実験: `Camera.CreateDefault`/`CreateFirstPerson`/`CreateBoundsFit` の既定を対数にして Windows.Tests を全件流した。失敗は 407 件中 4 件で、どれも標準を前提か比べる相手として使うテストだった（`StandardModeGoldenFrameTests`、`Radial_SeparatesNearlyCoplanarSurfacesFarAway` の「標準なら潰れる」側の 2 件、`RadialObserverTests.Preview_IsOnlyMadeForARadialCamera`）。
- 実装: ファクトリーが `DepthMapping = Radial` を設定する。上の 4 件は標準を明示した。不変条件 `CameraTests.Factories_ChooseRadialDepth`。
- 結果: Windows.Tests 407/407（性能の証拠 1 件はスキップ）。照明・影・AO・GI・空・スキニング・ギズモ・デバイス復旧のテストは変更なしで合格した。FeatureRunner の 11 ステージ（baseline、environment-lighting、device-recovery、performance、vsix.rendering-preview など）も WARP で合格した。

## 段階 2. シーンの Camera コンポーネント
- 実装: `Camera.DepthMapping` の既定を `Radial` にした。キーの無いシーンは対数になる。エディタのランタイムビューは、対数ではエディタとシーンカメラの両方の範囲を含む（`LevelLoad.ApplyProjection`）。M0099 の「シーンカメラの範囲に置き換える」のままだと、既定が対数になったとき、エディタで引いた視点からシーンカメラの far（既定 300 m）より先が切れるため。
- テスト: `SceneCameraTests`、`SceneJsonRuntimeValuesTests.Camera_ReadsTheDepthMappingAndIsRadialWhenAbsent`（キーが無ければ対数、`"Standard"` なら標準）、`EditorRuntimeViewTests.EditorCamera_OnARadialSceneCamera_SeesBothItsOwnRangeAndTheScenes`。
- 結果: Windows.Tests 408/408、CodeAnalysis 175/175、SceneModel 175/175、FeatureRunner 154/154、VSExtension 1079/1080（失敗はユーザーが作業中の未追跡テスト 1 件で無関係）。
- 実機: ThirdPerson を WARP で 90 フレーム（W で歩いてジャンプ）動かし、既定（対数）と、シーンに一時的に `"DepthMapping": "Standard"` を足した標準を撮影した。D3D12 エラーは両方 0、カメラの最終位置は同じ。画像は 99.98% の画素でビット一致し、違う 167 画素も最大 2 階調（`.artifacts/M0100/step2/`）。撮影後にシーンを元に戻した。
