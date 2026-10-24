# M0114 — 対数ネイティブの描画(標準深度の削除)
- 日付: 2026-10-25
- 状態: 計画(2026-10-25 に ADR-0080 を Accepted。実装はこれから)
- 決定: [ADR-0080](../Decisions/ADR-0080-Logarithmic-Native-Depth.md)(ADR-0076 を一部 Superseded)
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
ランタイムがシーンカメラの正射影を無視する既存の未実装、reversed-Z、MSAA、モーションベクトル、アセンブリ間の移動(M0115)、主 DSV への Lumen の表面前パスの統合。

## 機能 ID
F-L5-038(「Logarithmic depth of every perspective view」に改名)、F-L5-039、F-L0-002、F-L4-002、F-L8-018
