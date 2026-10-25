# ADR-0084 — 対数深度を「標準を包むラッパー」から「最初から対数で書かれた Stage シェーダー」へ置き換え、標準(双曲)深度の経路を削除する
- 状態: Accepted(2026-10-25。ユーザーの依頼:「G:\Unity\6.6\Omni を参考にして対数レンダリングの部分だけこのゲームエンジンのデフォルト、既定で読みこむようにして」「公開するアプリではないので… 対数変換器ではなく最初から対数化したシェーダーとして」。計画はユーザーが承認し、「標準(双曲)深度の経路を完全に削除する」を選んだ)
- 関連: ADR-0076(対数深度の最初の決定。一部 Superseded)、ADR-0063(正準数値と階層 World)、ADR-0064(型付きライト)、ADR-0069(物理的な環境照明)、ADR-0078(UE テンプレートの完全移植)、ADR-0085(URP の 4 描画パス)、ADR-0087(層の配置)、M0119、[Omni 仕様](../Specifications/Omni.md)

## 背景
- 今の対数深度は「標準の `VSMain`/`PSMain` を `RadialVSMain`/`RadialPSMain` が包む」構造である(`src/CSharpGameEngine.ShaderArtifacts/StagePresentationShaderSource.cs:170-220`)。`StageRenderer` は PSO を標準 7 本と対数 7 本の 2 系統で持ち、フレームごとに選ぶ(`src/CSharpGameEngine.Windows/Graphics/StageRenderer.cs:1345-1436`、`:4344-4351`)。ユーザーの言う「対数変換器」はこの層に当たる(Omni の Converter に当たるものは、固定 ABI の Stage を自前で持つエンジンには元から無い)。
- 深度方式の選択が API・プロトコル・シーン JSON にまたがる: `RenderDepthMapping`、シーン Component の `CameraDepthMapping`、プレビューのプロトコル 8 の `RenderPreviewDepthMapping`、描画カメラの `Camera.DepthMapping`、VSIX の `DescribePose` と `SceneCameraPreviewBinding`。
- 双曲深度のまま残っている透視のパスがある: Lumen の表面前パス、DDGI のプローブ撮影、AO の深度前パス。far が約 2^24 倍を超えると標準の投影は逆投影できず(`RadialDepthTests.StandardProjection_WithAHugeFar_CannotBeUnprojected`)、これらのパスは極端な far で精度を失う。`LumenGpuIntegrationTests` は `DepthMapping=Standard` に固定されていて、Lumen と対数深度の組み合わせ(ゲームの既定)は GPU 試験で検証されていない。
- 空の合成は主深度を読み、空の球の判定でハードウェアの `exp2` による逆写像を使う(`assets/Shaders/Atmosphere.hlsl:855-877`)。ADR-0076 の追記で GPU の `log2` を禁じたのに、逆写像側は対象の外にある。
- Omni(`G:\Unity\6.6\Omni`。文書は `D:` を指していたが `G:` が新しい正本で、対数の核のファイルは同じ内容)は、頂点を写像し、写像した空間の双曲深度を `SV_Depth` に書き、Depth Priming で Early-Z の喪失を補う。ADR-0076 は、このエンジンでは補間もクリップも標準 VS が物理的に正しいので頂点の写像は何も足さず、弦から深度を取ると大きな床と近い小物体の前後が崩れるとして採らなかった。この判断は有効なまま残る。
- 公開するアプリではないので、標準深度を残して互換を保つ理由が無い(ユーザーの決定)。

## 決定

### 1. 透視ビューの深度は 1 系統だけ
- Stage の `VSMain` が物体ごとの保守的ラスタ深度 `a·w − b`(接点は物体のメッシュの境界の中心のビュー深さ、`RadialDepthProjection.ConservativeDepthAt`)と `SV_ClipDistance0 = (z, w − z)` を出す。メッシュを描く PS は `SV_DepthGreaterEqual` に `EncodeRadialDistance(r) = min(G(r)·InverseMaximumG, 0.99999994)` を書く。ADR-0076 の決定 1・2・追記(保守的深度、GPU の log2 を使わない、物体ごとの接点)はそのまま有効。
- `RadialVSMain`・`RadialPSMain`・`PreviewRadialPSMain`・`PreviewCleanRadialPSMain` と、その入出力の構造体(`RadialPixelInput`・`RadialVertexOutput`・`ToVertexOutput`)は削除する。`VSMain`/`PSMain`/`PreviewPSMain`/`PreviewCleanPSMain` が最初から対数を書く。`VertexOutput` は 1 つで、`noperspective centroid` の `SV_Position`、`DepthPosition`(カメラ相対のラスタ化した位置)、`ClipDistance` を持つ。
- 深度を書く全パスは同じ関数を使う。式は `precise` で評価し、パス間でビットが揃うようにする。
- 頂点は写像しない(観察ビューだけが写像する。決定 4)。

### 2. 正射影は線形深度、影マップは光源空間のまま
- 正射影のビュー(エディターの分離プレビュー、Lumen のカード撮影)は同じ Stage プログラムで描き、`DepthEncoding0 = (0, s0, s1, 0)` の線形の距離とラスタ z をそのまま出す。`s0`・`s1` は L4 の `RenderViewDepth.ForOrthographic` が投影行列の深度列から求める。
- 影アトラス、太陽のカスケード、空の向きは光源空間・補助の投影のまま(ADR-0076 の不変条件 7・8)。
- ランタイムがシーンカメラの正射影を無視する既存の未実装(Conformance.md:343)は、この決定で変えない。

### 3. 選択の型・欄・プロトコルを消す
- 削除: `RenderDepthMapping.Standard`(列挙そのもの)、`CameraDepthMapping`(シーン Component の `DepthMapping`)、`RenderPreviewDepthMapping`、描画カメラの `DepthMapping`、`StandardModeGoldenFrameTests`。
- 追加(L4、`src/CSharpGameEngine.Rendering/RadialDepth.cs`): `RenderDepthEncoding : byte { Linear=0, Logarithmic=1, Observer=2 }` と、FrameCB のパケット `RenderViewDepth`(`ForPerspective`/`ForOrthographic`/`ForObserver`)。深度の符号化と検証は L4、どのビューにどれを使うかの方針は L5(`RenderViewPolicy`、ADR-0087)。値を暗黙の 0・1・2 にせず明示する。
- プレビューのプロトコルは 9 にする。VSIX・プレビューホスト・パッケージした TestAssets・`Editor-VSIX.md` を同じ変更で直す。
- シーン JSON の Camera から `DepthMapping` を消す。リポジトリのシーンに書いたものは無い(schema にあるだけ)ので、`Templates/*/.cge/scene.schema.json` と `UserEcsProject` を作り直す。`new Camera{}` の既定値は今 Standard(列挙の 0)なので、ファクトリーを通さない呼び出し元を洗い出し、すべて対数になることを `CameraTests` で固定する。
- 旧来の `ModelRenderer`/`TriangleRenderer` は標準深度で描く最後のカメラ描画で、どこからも生成されていない(`RenderServer.Create` が Stage を使う)。同じ変更で消す。`model-v1`/`triangle-v1` の ABI の読み手がいなくなるので、生産者・VSIX のテンプレート・`FeatureStageCatalog`・`RenderingReleaseEvidence` も同じ変更で扱う(Rendering.md:116-118「置き換えた公開 API を同じ変更で消す」)。

### 4. 観察ビューは残す
- `RadialObserver`(F-L5-039)は頂点を写像する唯一のビューとして残す。`RadialObserverVSMain` は `ObserverVSMain` に改名し、`DepthPosition` に写像後の位置を出す。PS は同じで、`DepthEncoding0 = (2, T, 1/G(rObs), 0)` で圧縮後のジオメトリまでの距離を対数で書く。双曲深度はどこにも残らない。

### 5. 双曲深度のまま残っていた透視パスを対数にする
| パス | 決定 |
|---|---|
| DDGI のプローブ撮影 | 対数(重み 0)で描き、`CaptureDistance`(R32F の配列)を足して、`GlobalIllumination.hlsl:103-113` の双曲深度の線形化を置き換える |
| Lumen の表面前パス | 対数にする。DSV は今の専用の `SurfaceDepth` のまま、主パスは `LESS` のまま(主 DSV への統合は計測してから行う範囲外の最適化) |
| AO の深度前パス | 消す。AO は `SceneDistance` を読み、補助の投影による約 655 m の制限が無くなる |
| 空の合成 | `SceneDistance`(R32F、`PSMain` が `SV_Target1` に書く目からの物理距離)を読む。`exp2` による逆写像は消す |

- `SceneDistance` の導入で、深度から距離を戻す関数の精度の問題(CPU と GPU で同じ逆関数を契約にする、ハードウェアの `exp2` を使わない)を避ける。

### 6. 正解(オラクル)を置き換える
- 標準の 7 フレーム(`StandardModeGoldenFrameTests`)は、削除の前の最後の比較で「対数 = 標準」(各チャンネル 1 階調以内、違う画素 1 % 以下。ワイヤーフレームは違う画素 1 % 以下)を 7 ケースすべてで示してから、対数の 7 つの SHA-256 を `StageGoldenFrameTests` に固定する。1 つでも外れたら固定も削除もしない。
- 対数の正しさは CPU の倍精度のオラクル `LogarithmicDepthOracleTests`(板を 4e-6 〜 5e18 m に置き、中央 4 ulp 以内)と、実機の `RadialDepthPrecisionEvidence`(WARP は log2 の誤差を隠すので実機で測る)で示す。
- 標準が far で潰れることの証拠は、CPU の `RadialDepthTests.StandardProjection_WithAHugeFar_CannotBeUnprojected` に残す。

### 7. ゴールデンの付け替えの規則
- SHA を付け替えてよいのは次の 3 つがそろったときだけ: (1) 変更前に書いたダンプ(`CGE_GOLDEN_DUMP`)と比べ、その段階の基準(既定は各チャンネル 1 階調以内・違う画素 1 % 以下)を満たす、(2) 原因をマイルストーンの文書に書く、(3) 古い SHA と新しい SHA を表に並べる。
- 画素を変えない段階(削除・移動・分離)では付け替えを認めない。変わったら直すか、その段階を戻す。

### 8. ABI
- `stage-v14`: エントリー 18 → 14(`RadialVSMain`・`RadialPSMain`・`PreviewRadialPSMain`・`PreviewCleanRadialPSMain` を削除、`RadialObserverVSMain` → `ObserverVSMain`、`LumenSurfacePSMain` → `SurfacePrepassPSMain`)。FrameCB は 960 → 976 バイト。使われていない 304(`ShadowViewProjection`)と 368(`DirectionalShadowAtlasUvScaleOffset`)の欄を `InverseViewProjection` と `ViewportSize` に使い回す(同じ変更で、書き込み・定数・`Lumen.hlsl` の写し・`ComputeDirectionalShadow` を消す)。`DepthMapping0/1` は `DepthEncoding0/1` に改め、`DepthEncoding1.w` に保守的ラスタ深度の重み(主カメラ 1、プレビュー・プローブ・観察 0)を持たせる。`ObjectCB.RadialRasterDepth` は `ConservativeRasterDepth`。複製した独自の Stage グラフは読めなくなる(CHANGELOG に書く)。
- 対数の関数(`RadialLog2OnePlus`・`RadialSafeLength`・`RadialG`・`EncodeRadialDistance`・`IsBeyondViewFarPlane`)は新しいサブグラフ `assets/Shaders/Depth.cgesubgraph` に字句どおり移す。超越関数の禁止の検査は、接尾辞全体の禁止を残したうえで、Stage グラフ本体のこの関数群を取り出して検査する(本体のフォグは `exp` を使うので全体には掛けられない)。

## 採らなかった案
- 頂点を写像する(Omni の方式): ADR-0076 のとおり、このエンジンでは補間もクリップも標準 VS が正しく、弦の深度が前後を崩し、`SV_DepthGreaterEqual` による Early-Z も失う。
- 標準を内部の比較基準として残す: PSO・シェーダーが 2 系統のままになり、保守が続く(ユーザーの決定で完全に削除)。
- Depth Priming(`ZTest Equal`)で Early-Z を補う: `SV_DepthGreaterEqual` では早期棄却ができず、隠れた断片をすべてシェーディングする。
- 深度から `exp2` で位置を戻す: CPU と GPU で逆関数を新しく一組作ることになり、精度も相対 3e-6 に落ちる。距離を持てば 2^-24 になる。
- 深度を少し奥にずらして穴を防ぐ: ほぼ同一平面の面を分けられなくなる(ADR-0076 の 2.5e-6 の例)。

## 帰結
- カメラの描画経路が 1 系統になり、PSO・エントリー・試験の保守が減る。極端な far でも全透視パスが同じ精度で動く。
- 標準フレームとのビット一致(Omni.md の不変条件 1・2)は、削除の前の最後の比較(基準内の証拠)に置き換わる。
- 複製した独自の Stage グラフと、プレビューのプロトコル 8 は読めなくなる。

## 検証
- CPU: `RadialDepthTests`、`LogarithmicDepthOracleTests`(WARP、常時走る)。
- 画素: `StageGoldenFrameTests`(対数 7 つの SHA)、削除の前の最後の比較。
- 実機: `RadialDepthPrecisionEvidence`・`RadialDepthPerformanceEvidence`(`CGE_PERFORMANCE_EVIDENCE=1`、`CGE_PERFORMANCE_ADAPTER=hardware`)。
- ABI: `ShaderGraphCompilerTests`(エントリー 14、`VertexOutput` のメンバー、`SV_DepthGreaterEqual`、サブグラフ 5 つ)、`ProductionStage_DepthEncodingAvoidsHardwareTranscendentals`、`StagePresentationShaderSourceTests`。
- Lumen: `Lumen_WithAnInterstellarFarPlane_MatchesTheOrdinaryFrame`(直す前に落ちることを記録してから直す)。
- Stage: `rendering.radial-depth`(ID は残し、名前・フィルター・Oracle を直す)。

## 関連する機能
- F-L5-038(「Logarithmic depth of every perspective view」に改名)、F-L5-039、F-L0-002、F-L4-002、F-L8-018
