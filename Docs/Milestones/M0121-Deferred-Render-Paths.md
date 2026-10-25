# M0121 — Deferred / Deferred+ と 4 パスの選択
- 日付: 2026-10-25
- 状態: 計画(2026-10-25 に ADR-0085 を Accepted。M0120 の完了後に始める)
- 決定: [ADR-0085](../Decisions/ADR-0085-Urp-Render-Paths.md)
- ABI: `stage-v16`

## 目的
GBuffer、StencilDeferred 相当(Deferred)、ClusterDeferred 相当(Deferred+)を作り、4 パスを選べるようにする。4 パスで 7 フレームが許容内に一致する。

## 段階
1. **GBuffer と Deferred**: 主 DSV を `D32_FLOAT_S8X24_UINT` にし(`CreateMeshPipelineState` に DSV の形式と RT ごとの書き込みマスクを足す、プローブとカードは D32 の別 PSO、オーバーレイは DSV `UNKNOWN`、クリアにステンシル、readback は plane 0)、`PSMain` を表面の評価と照明の解決に分け、GBuffer(Albedo/Metallic、NormalRoughness(Lumen と共有)、GeometryNormal、HDR、`SceneDistance`)、全画面 Global、光源ごとのステンシル 2 パス(`DepthClipEnable = FALSE`)、Fog。ルートシグネチャ 13 パラメーター。
2. **Deferred+**: 全画面 1 ドローのクラスタ照明。
3. **4 パスの一致と実走**: `RenderPathEquivalenceTests`、`DeferredLightVolumeTests`、`LogarithmicDepthOracleTests` を 4 パスの Theory に、4 パス × 7 の SHA、4 テンプレート × 4 パスの実走、`RenderPathPerformanceEvidence`(RTX 4060 Ti)。
4. **カタログ・文書**: F-L0-011・F-L4-020 を Implemented に。

## 完了条件
- 許容(ADR-0085 §7)を満たし、28 SHA を固定する(Forward+ の 7 は M0120 と同じ)。
- 4 テンプレート × 4 パスの実走で D3D12 のエラー 0、移動の値が M0111 と同じ。

## 範囲外
半透明、MSAA、Depth Priming、デカール、Rendering Layers、GBuffer 版の SSAO、URP の頂点ライティング、反射プローブのクラスタ化。

## 機能 ID
F-L4-020、F-L0-011、F-L5-038、F-L4-014、F-L4-019
