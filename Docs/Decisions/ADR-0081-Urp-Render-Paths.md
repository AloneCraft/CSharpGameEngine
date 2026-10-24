# ADR-0081 — URP の 4 描画パス(Forward / Forward+ / Deferred / Deferred+)を、最初から対数深度で書いた Stage シェーダーとして移植する
- 状態: Accepted(2026-10-25。ユーザーの依頼:「unity の URP のシェーダーの Forward, Forward+, Deferred, Deferred+ を参考に… 最初から対数化したシェーダーとして」。計画はユーザーが承認し、「4 つすべてを移植し、既定は Forward+」を選んだ)
- 関連: ADR-0080(対数ネイティブの深度)、ADR-0083(層の配置)、ADR-0076(対数深度)、ADR-0064(型付きライト)、ADR-0069(物理的な環境照明)、ADR-0078(UE テンプレートの完全移植)、ADR-0052(Render Graph)、M0115、M0116

## 背景
- 今のエンジンの描画パスは Forward+ 相当の 1 つだけ。CPU が光の範囲の箱の 8 隅をクリップ空間の XY と W へ投影して 2D タイル(16 px、1 タイル最大 64 灯、局所光は全体で 1024 灯)へ振り分け、`PSMain` が自分のタイルのリストだけを回す。深度方向の分割(クラスタ)は無い(`src/CSharpGameEngine.Windows/Graphics/StageRenderer.cs:86-106`、`:3330-3371`、`:3512-3566`、`Docs/Specifications/Rendering.md:140`)。Deferred、GBuffer、ステンシル、半透明の物体は無い。
- パス構成を決めた ADR・仕様は無い(Docs 全体を検索して確認)。Render Graph(ADR-0052)は宣言だけで、実行器は未実装(`Rendering.md:36`)。宣言のグラフ(`RenderQualityProfile.cs`)は実際のパス(Lumen、GDF、AO、大気、プレビュー、画面レイヤー)と一致しない。
- URP 17.6(`G:\Unity\6.6\Omni\Library\PackageCache\com.unity.render-pipelines.universal@9b1eb8be54e0`)の 4 モードの軸は 2 つ: Deferred の照明か(`usesDeferredLighting` は Deferred と Deferred+)、クラスタの光源ループか(`usesClusterLightLoop` は Forward+ と Deferred+。`Runtime/UniversalRenderer.cs:120-122`)。
  - Forward: CPU の物体ごとの光源インデックス(最大 8 灯、`RealtimeLights.hlsl:216-253`)、頂点ライティング可。
  - Forward+: 2D タイル(8 px から倍々で予算に収める)と Z-bin(`log2(viewZ)` の等分)のビットマスク(`ForwardLights.cs:274-436`、`Clustering.hlsl:26-149`)。平行光源はクラスタ外の別ループ。
  - Deferred: GBuffer(albedo、specular、法線、GI+発光の色、深度)に書き、`StencilDeferred` が平行光源を 1 灯ずつ全画面で、点・スポットをステンシル 2 パスの光源体積で照らす。位置は深度から `_ScreenToWorld` で復元する。
  - Deferred+: 同じ GBuffer を `ClusterDeferred` の全画面で、クラスタのループで照らす。
  - 透明物と ForwardOnly の物体は全モードで Forward。Deferred 系は MSAA 不可・Depth Priming 不可。
- Omni は Forward / Forward+ だけに対応し、Deferred / Deferred+ は Fail Closed にしている(`OmniUniversalInvariants.md:17`)。深度から位置を戻す標準の効果は Radial の深度で黙って壊れると記録している(`:29-30`)。つまり Deferred 系に参照実装は無く、このエンジンが新しく設計する。

## 決定

### 1. 4 パスを選べるようにする。既定は Forward+
- `enum RenderPath : byte { Forward, ForwardPlus, Deferred, DeferredPlus }`(L4)。既定は `ForwardPlus`(今の挙動に近い)。
- 作者向けの入口: シーン Component `Level` に `RenderPath RenderPath = RenderPath.ForwardPlus` を足す。環境変数 `CGE_RENDER_PATH`(`forward|forwardplus|deferred|deferredplus`。不正な値は起動時に止める)が上書きする。`EngineHost.UseRenderer` が `RenderServer.RenderPath` に入れ、デバイスの復旧の後も保つ。
- 実効パス(`RenderPathResolution`): 表示モードが `Lit` 以外のとき、または選択中の物体があるときは、Deferred 系を Forward+ に落とす(表示モードと選択の色付けは Forward 系の PS だけが持つため)。実際に使ったパスは `RenderServer.LastFrameRenderPath` で外から見える。
- 正射影も 4 パスとも対応する(位置の復元式が投影の種類に依らない。下記 §5)。Lumen は透視ビューのときだけ有効。

### 2. 照明は 1 つのコードを共有する
- Stage の `PSMain` を、表面の評価と照明の解決に分ける。`EvaluateSurface`(メッシュ)と `LoadGBufferSurface`(GBuffer)が `SurfaceData`(位置、基本色、metallic、粗さ、法線、頂点法線、発光)を作り、共通の `EvaluateIndirectLighting`(環境色、AO、空 SH、DDGI、Lumen)、`EvaluateDirectionalLights`(最大 4 灯、影は 4 カスケード)、`EvaluateLocalLights`(`BeginLocalLights`/`NextLocalLight`。点・スポット・矩形面光源・発光パッチ。Pbr/Shadow/Pcss サブグラフは無変更)、`EvaluateFog` で照らす。
- 局所光の加算は `NextLocalLight` のループ 1 か所だけに置く。ObjectCB の並びを光源 index の昇順にし、クラスタも昇順で回す。こうすると、1 物体あたり 8 灯以下のとき Forward と Forward+ は同じ非ゼロ項を同じ順に足し、ビット一致する(クラスタにだけ入っている光源は厳密に 0 を返し、和を変えない)。
- 霧と露出は Deferred 系ではブレンド `(ONE, SRC_ALPHA)` で掛け、Forward と代数的に同じ値にする。Forward の最終値は `(I + D + L + E)·t + S` に露出を掛けたもの。Deferred は `dst ← E·x`、全画面で `+(I+D)·x`、光源体積ごとに `+L_i·x`、最後の Fog で `dst·t + S·x`。Deferred+ は 1 パスで `((I+D+C)·t + S)·x`、α = t。

### 3. Forward(物体ごとに最大 8 灯)
- L4 の `ForwardObjectLightAssignment` が、選ばれた局所光のうち球が物体のワールド AABB と交わるものから、輝度 × R² / 距離² の上位 8 灯を取り、index の昇順に並べる。書き込み先は `ObjectCB.ForwardLightIndices0/1` と `ForwardLightCount`。
- 方向光は 4 灯とも FrameCB のループで評価する(URP のように 8 灯の枠に入れない)。URP の頂点ライティングは移植しない(PBR の見た目がパス間でずれるため)。

### 4. Forward+(タイルと対数殻の Z-bin。URP の移植)
- L4 の `ClusterLightGrid`: タイル幅は 8 px から倍々に広げ、`タイル数 × 語数 ≤ ClusterTileWordBudget(16384)` に収める。bin 数 = `floor(ClusterZBinWordBudget(4096) / (1 + 語数))`。語数 = `ceil(局所光の数 / 32)`。
- Z-bin は URP の `log2(viewZ)` を移さず、**深度バッファの値そのもの**で切る: `d = EncodeRadialDistance(r)`(放射距離 `r` の対数符号化)、`bin = floor(d · N)`。光源の範囲は `[max(|c|−R, 0), |c|+R]` を `d` に写し、両端を相対 2^-20 だけ外へ広げて保守的にする。正射影は線形の深度で同じ処理。PS は書いた深度そのもので bin を引く(Deferred+ は `SceneDistance` から同じ関数で求め、ビット単位で同じになる)。
- バッファは t6 の `ClusterWords` 1 本(タイルのビットマスクと Z-bin を並べる。bin の先頭語の位置は `FrameCB.ClusterTiles`)。ルートシグネチャは 11 パラメーターのまま。
- URP との違い: bin の見出しは 1 語(光源の min と max の index を、max に 65536 を掛けて min を足した 1 語で持つ。リフレクションプローブが無いので URP の 2 語から減る)。`ClusterTileWordBudget = 16384` は URP で局所光が 32 灯を超えるときの値。タイル 1 つ 64 灯の上限と `DroppedTileLightReferences` は無くなる。
- 平行光源はクラスタに入れず、FrameCB のループ(最大 4 灯)。

### 5. Deferred と Deferred+
- 主 DSV だけを `D32_FLOAT_S8X24_UINT`(SRV は `R32_FLOAT_X8X24_TYPELESS`)にする。プローブ撮影とカードの DSV は D32 のまま(別 PSO)。オーバーレイの PSO は DSV を `UNKNOWN` にする。
- GBuffer:

  | 面 | 形式 | 中身 |
  |---|---|---|
  | Albedo/Metallic | R8G8B8A8_SRGB | 基本色、metallic |
  | NormalRoughness | R16G16B16A16F | シェーディング法線、粗さ。Lumen の表面前パスと共有し、Lumen 専用の `Surface*` は消す |
  | GeometryNormal | R10G10B10A2 | 頂点法線(カスケード影のバイアス用) |
  | HDR(= GBuffer3) | 既存の HDR | 照明の蓄積(発光 × 露出から) |
  | SceneDistance | R32F | 目からの物理距離 |

  ステンシルのビットは 0x1 = 面、0x2 = 光源。材質は Lit の 1 種類なので、URP の材質ビットや材質別の 2 ドローは持たない。
- 位置の復元は `ReconstructCameraRelativePosition(ndc, SceneDistance)`: `InverseViewProjection`(`InvertibleViewProjection` の逆行列。透視は補助の投影で far が大きくても正則、正射影は VP そのもの)で近点と遠点を求め、光線上の距離 `SceneDistance` の点にする。深度のデコード(`exp2`)も、位置を深度から戻す関数も作らない(ADR-0080)。
- Deferred(StencilDeferred の移植): GBuffer、AO と Lumen の compute、Global(方向光と間接光、全画面、ステンシル EQUAL 1)、光源ごとのステンシル 2 パス、Fog(全画面)の順。
  - 印付け: 両面を描き、深度は `LESS_EQUAL` で書き込まず、`DepthFail` でステンシル INVERT。PS が `SV_Depth = EncodeRadialDistance(...)` を書き、光源体積のメッシュも画面の深度と同じ符号化で比べる。
  - 照明: Cull FRONT、ステンシル EQUAL 3 で Pass は ZERO、ブレンド ONE/SRC_ALPHA。
  - 光源体積の VS は `DepthClipEnable = FALSE` とし、`SV_ClipDistance` で近面だけを切る(far 1e18 m で 1e15 m の背面が丸めで切られるのを避ける)。体積のメッシュは L4 の `LightVolumeGeometry`(球と 16 角錐)。
- Deferred+(ClusterDeferred の移植): GBuffer の後、全画面の `DeferredLightingPSMain` 1 ドローでクラスタのループを回す。PS で行い、compute にはしない(UAV の読み書きと WARP での違いが増えるため)。

### 6. ABI の版
- v15(M0115): FrameCB に `ClusterTiles`、ObjectCB に `ForwardLightIndices0/1`・`ForwardLightCount`、t6 が `ClusterWords`。ルートシグネチャは 11 パラメーターのまま。
- v16(M0116): b2 のルート定数 `DrawParams`(4 個)、t19〜t22(GBuffer と `SceneDistance`)のテーブルでルートシグネチャは 13 パラメーター。エントリー `GBufferPSMain`、`DeferredLightingPSMain`、`LightVolumeVSMain`、`LightVolumeStencilPSMain`、`LightVolumePSMain` を足す。
- 各マイルストーンの中で最初に ABI を変えるコミットで版を上げ、そのマイルストーンの間は据え置く。

### 7. 一致の許容(`RenderPathEquivalenceTests`)
| 比べる組 | 許容 |
|---|---|
| 深度と `SceneDistance` | 4 パスでビット一致 |
| M0115 の Forward+ と M0114 の Forward+ | ビット一致(7 SHA が不変) |
| Forward と Forward+ | 物体ごとの局所光が 8 以下ならビット一致 |
| Forward+ と Deferred・Deferred+ | 各チャンネル 2 階調以内、違う画素 1 % 以下。BaseColor・Normal の表示は 1 階調以内 |
| Deferred と Deferred+ | 同じ(2 階調・1 %) |
| ワイヤーフレーム、オーバーレイ、画面 UI | 4 パスでビット一致(どのパスでも Forward) |
| Forward で物体に 9 灯以上 | 意図した差。`Forward_KeepsTheEightMostImportantLightsOfAnObject` で URP の選び方を固定 |

- 差の要因: 法線の fp16、GeometryNormal の 10 bit、HDR の fp16 加算 1〜3 回、影の比較の反転。許容を広げるときは、原因をこの ADR の追記に書く。

## 非目標
MSAA、Depth Priming、半透明のメッシュ(オーバーレイと画面 UI はどのパスでも Forward)、デカール、Rendering Layers、GBuffer 版の SSAO、URP の頂点ライティング、反射プローブのクラスタ化、XR と単一パス 2 ビュー。

## 採らなかった案
- Z-bin を `log2(viewZ)` で切る(URP どおり): 深度バッファには放射距離の符号化が入っているので、`viewZ` を別に計算する必要が出て、Deferred+ が深度から 1 回の MAD で bin を引けなくなる。
- GBuffer に `positionCS.z` を書く(URP どおり): このエンジンの主パスのラスタ深度は保守的な `a − b/w` で、DSV に書く値とは別になる。
- Deferred+ を compute で行う: UAV 経路と WARP での違い、ブレンドの代数を別に書く必要が増える。
- D24S8: 近距離の対数深度の分解能が落ち、ADR-0076 の精度の根拠(float32)が崩れる。
- 主 DSV を全パスで D32S8 にする: 主・プローブ・Lumen のカード・Lumen の表面・影の DSV は 5 種類あり、PSO が DSV の形式ごとに分かれる。主だけを変える。

## 帰結
- パスの選択が API(`RenderServer.RenderPath`)・シーン(`Level`)・環境変数にまたがる。Deferred 系は Forward+ と許容つきで一致する。
- Forward+ の ABI が変わり(v15)、複製した独自の Stage グラフは作り直しになる(CHANGELOG に書く)。
- `RenderQualityProfile` の宣言だけのグラフは、実際に実行する計画(`RenderServer.CurrentPipeline`、ADR-0083)で置き換えて消す。

## 検証
- CPU: `ClusterLightGridTests`(倍精度の総当たりと比べて取りこぼし 0、セル内は光源の昇順、far が 1e6〜1e18 m でも有限で単調)、`ForwardObjectLightAssignmentTests`、`RenderPathResolutionTests`。
- GPU(WARP): `RenderPathEquivalenceTests`、`DeferredLightVolumeTests`(カメラが体積の中にある場合、far 1e18 m で 1e15 m の光源)、`LogarithmicDepthOracleTests` を 4 パスの Theory にする、4 パス × 7 の SHA。
- 実機: `RenderPathPerformanceEvidence`(8/64/256/1024 灯、1280 × 720)。
- Stage: `rendering.render-paths`。4 テンプレート × 4 パスの実走(D3D12 のエラー 0、移動の値は同じ)。

## 関連する機能
- F-L4-020(URP render paths)、F-L0-011(D3D12 render plan executor)、F-L5-038、F-L4-014、F-L4-019
