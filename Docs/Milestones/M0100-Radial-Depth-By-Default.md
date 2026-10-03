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
| 2 | シーンの Camera コンポーネントの既定を対数にする | 未着手 |
| 3 | エディタの分離プレビュー、Asset Preview、カメラのサムネイルを対数にする | 未着手 |
| 4 | 文書、SDK の版 | 未着手 |

## 段階 1. 描画カメラのファクトリー
- 実験: `Camera.CreateDefault`/`CreateFirstPerson`/`CreateBoundsFit` の既定を対数にして Windows.Tests を全件流した。失敗は 407 件中 4 件で、どれも標準を前提か比べる相手として使うテストだった（`StandardModeGoldenFrameTests`、`Radial_SeparatesNearlyCoplanarSurfacesFarAway` の「標準なら潰れる」側の 2 件、`RadialObserverTests.Preview_IsOnlyMadeForARadialCamera`）。
- 実装: ファクトリーが `DepthMapping = Radial` を設定する。上の 4 件は標準を明示した。不変条件 `CameraTests.Factories_ChooseRadialDepth`。
- 結果: Windows.Tests 407/407（性能の証拠 1 件はスキップ）。照明・影・AO・GI・空・スキニング・ギズモ・デバイス復旧のテストは変更なしで合格した。
