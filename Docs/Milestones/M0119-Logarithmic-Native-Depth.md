# M0119 — 対数ネイティブの描画(標準深度の削除)
- 日付: 2026-10-25
- 状態: 実装中(段階 1 完了 2026-10-25)
- 決定: [ADR-0084](../Decisions/ADR-0084-Logarithmic-Native-Depth.md)(ADR-0076 を一部 Superseded)
- ABI: `stage-v14`、プレビューのプロトコル 9

## 目的
透視ビューの深度経路を 1 系統(最初から対数深度を書く Stage シェーダー)にする。標準(双曲)深度の選択(型・欄・プロトコル・PSO・ゴールデン)を削除し、双曲深度のまま残っていた透視パス(Lumen の表面前パス、DDGI のプローブ撮影、AO の深度前パス)を対数にする。画素は削除前の対数と同じ。

## 段階
1. **最後の比較と対数のゴールデン**: `StageGoldenFrameTests`(対数の 7 ケース)を作り、同じ 7 ケースを標準と対数で描いて基準内(各チャンネル 1 階調以内、違う画素 1 % 以下。ワイヤーフレームは違う画素 1 % 以下)であることを確かめてから SHA を固定する。外れたら何も消さずに原因を調べる。`LogarithmicDepthOracleTests`(板を 4e-6 〜 5e18 m に置き、中央 4 ulp 以内、全画素で深度 < 1.0、正射影は 2 ulp 以内)、`GoldenFrameDump`(`CGE_GOLDEN_DUMP`/`CGE_GOLDEN_BASELINE`)を足す。
2. **予行**: 標準を明示しているだけの試験(Lumen、ミップ、距離場、雲)を対数で流し、落ちたものと原因を記録する。
3. **Stage を最初から対数にする**(1 項目 1 コミット、それぞれ付け替えの規則で確かめる): (a) `VertexOutput` の一本化・`VSMain` の保守的ラスタ深度・`PSMain` の `SV_DepthGreaterEqual` と `SceneDistance`・`Depth.cgesubgraph`・空の合成が `SceneDistance` を読む、(b) DDGI のプローブ撮影を対数にし `CaptureDistance` を足す、(c) Lumen の表面前パスを対数にする(先に far 1e18 m で落ちることを記録)、(d) AO が `SceneDistance` を読み、AO の深度前パスを消す。
4. **標準の削除**(1 変更。付け替えは認めない。7 SHA は段階 3 の後と同じ): ラッパーのエントリーと標準の PSO 系統、`RenderDepthMapping.Standard`・`CameraDepthMapping`・`RenderPreviewDepthMapping`・`Camera.DepthMapping`、`StandardModeGoldenFrameTests`、`ModelRenderer`/`TriangleRenderer` と `model-v1`/`triangle-v1` を消す。`RenderDepthEncoding`・`RenderViewDepth`(L4)を足す。プロトコル 9。ADR-0076 を一部 Superseded にする。
5. **実機の証拠と文書・版**: RTX 4060 Ti で `RadialDepthPrecisionEvidence`(中央 4 ulp 以内)と `RadialDepthPerformanceEvidence` を記録する。

## 完了条件
- `git grep -nE 'RenderDepthMapping\.Standard|CameraDepthMapping|RenderPreviewDepthMapping|RadialVSMain|RadialPSMain|PreviewRadialPSMain|PreviewCleanRadialPSMain|StandardModeGoldenFrameTests' -- src tools tests assets` が 0 件。
- 7 SHA が固定され、オラクルに合格。`dotnet test CSharpGameEngine.slnx -c Debug` が失敗 0、D3D12 のデバッグ層のエラー 0。
- 4 テンプレートの実走が M0111 と同じ(ThirdPerson 15.700 m・上昇 1.228 m、FirstPerson 16.000 m・0.863 m、TopDown 9.606 m・(6.79, 6.84)・17.414 m、Vehicle 57.150 m・30.5°)。
- `rendering.performance` の予算(中央値 6 ms、p95 8.33 ms、割り当て p95 0)を満たす。

## 範囲外
ランタイムがシーンカメラの正射影を無視する既存の未実装、reversed-Z、MSAA、モーションベクトル、アセンブリ間の移動(M0120)、主 DSV への Lumen の表面前パスの統合。

## 機能 ID
F-L5-038(「Logarithmic depth of every perspective view」に改名)、F-L5-039、F-L0-002、F-L4-002、F-L8-018

## 実行結果

### 段階 1(2026-10-25、WARP、192 × 108)
- 標準の 7 ハッシュは共有シーン(`GoldenFrameScene`)へ付け替えた後も不変(`StandardModeGoldenFrameTests.StandardFrames_MatchTheirPinnedHashes` 合格)。
- 標準と対数の最後の比較(`LogarithmicFrames_EqualTheStandardFramesWithinOneLevel`、基準: 各チャンネル 1 階調以内・違う画素 1 % 以下、ワイヤーフレームは違う画素 1 % 以下)は 7 ケースすべて合格。証拠は `.artifacts/M0114/golden/`(PNG・raw・`parity.json`)。

| ケース | 違う画素(20736 中) | 最大の階調差 | 標準の SHA-256 | 対数の SHA-256(固定) |
|---|---|---|---|---|
| legacy-lit | 6 | 1 | 3F7940D2... | 3E56DB8C... |
| environment | 1 | 1 | E16BAAAA... | 9D47DDD9... |
| selection | 6 | 1 | 2A65C348... | 19F90F94... |
| base-color | 0 | 0 | C1321971... | C1321971...(同一) |
| normal | 0 | 0 | A0C9623A... | A0C9623A...(同一) |
| wireframe | 29 | 192 | 25DB6AB1... | E115C10F... |
| previews-and-overlay | 6 | 1 | A823A163... | 5A7F4B3A... |

- 違う画素は、主パスの床が目の面と近平面をまたぎ、クリップされる三角形があるため(ADR-0076 追記の帰結)。ワイヤーフレームの 29 画素は、共有辺の同じ深度の決着が、標準では描画順、対数では丸めになるため(Omni 仕様の不変条件 3)。
- 対数の 7 ハッシュを `StageGoldenFrameTests` に固定した。
- 深度オラクル `LogarithmicDepthOracleTests`(`DepthOracle`、倍精度との比較)は 4e-6 m・0.5 m・50 m・2e4 m・1e15 m・5e18 m で、画面中央 4 ulp 以内、全画素で深度 < 1.0 に合格(WARP)。
- `GoldenFrameDump`(`CGE_GOLDEN_DUMP`・`CGE_GOLDEN_BASELINE`)を足した。付け替えの規則(ADR-0084 §7)で使う。

### 段階 2: 予行(2026-10-25、WARP)
- 標準を明示していただけの試験(`LumenGpuIntegrationTests`、`GlobalDistanceFieldGpuIntegrationTests`、`MaterialMipmapGpuIntegrationTests` の `DepthMapping=Standard`)を `Radial` に替えて、`VolumetricCloudGpuIntegrationTests`(標準と対数の両方を既に持つ)と一緒に流した: 25 件中 23 件が合格。
- 失敗した 2 件(`Reflections_MirrorFloorShowsTheBoxWhereGeometryPutsItAndRoughnessBlursIt`、`Lumen_ConvergesWithinTwentyFramesAndIsDeterministic`)は、どちらも読み戻しの `Map` で `0x887A0005`(デバイス削除)。M0111 に記録済みの WARP の一過性の事象で、対数深度とは無関係(測定値は出ていた: 鏡の IoU 0.84・重心の誤差 1.19 px、収束は 20 フレームで 5 % 以内)。この 2 件を単独で再実行して両方合格した。
- 3 ファイルは対数のまま固定した。標準と比べる試験は段階 4 で扱う。
