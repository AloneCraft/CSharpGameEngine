# M0110 — Lumen 相当の土台と見た目の不足(ミップマップ、雲、空の球、メッシュの距離場、グローバル距離場)
- 日付: 2026-10-23
- 状態: 完了(下の項目表のとおり。実機の GPU は未検証)

## 目的
ADR-0078 の「Lumen 相当の動的 GI と反射、ボリュメトリック雲、空の球」のうち、土台と、UE の参照画像との比較(Conformance「Vehicle の UE 参照画像との比較」)で分かった見た目の不足を埋める。静的なライティングのベイクは作らない(ユーザーの決定)。サーフェスキャッシュ・最終収集・反射は M0111。

## 項目の完了表
| # | 項目 | 状態 |
|---|---|---|
| 1 | テクスチャのミップマップ(CPU で決定的に作る。sRGB は線形で平均。異方性 8) | 完了 |
| 2 | ボリュメトリック雲(Hillaire 系のレイマーチ。UE の値を取り込む) | 完了(形状は UE のテクスチャでなくエンジンのノイズ) |
| 3 | 空の球(`SkySphere`) | 完了 |
| 4 | メッシュの距離場(ビルド時に作る) | 完了 |
| 5 | グローバル距離場(クリップマップ、毎フレームの差分更新、トレース用に公開) | 完了(M0111 のトレース本体は範囲外) |
| 6 | 描画契約 | `stage-v11` のまま(下の決定) |
| 7 | 検証(UE の参照画像との並べ、ミップの数値) | 完了(WARP のみ) |

## 事実(観測)
- UE の 4 レベルは同じ雲を使う: `VolumetricCloud` は Layer Bottom 5 km、Layer Height 10 km、Planet Radius 6360 km、Tracing Start Max 350 km、Tracing Max 50 km、Sky Light Cloud Bottom Occlusion 0.5、Stop Tracing Transmittance 0.005。材質は `m_SimpleVolumetricCloud_Inst`(Density 0.008、Coverage −0.2、Layout Scale 256、Albedo 0.98、Phase (0.8, 0.1667, 0.575)、Multiscatter (0.6667, 0.25, 0.18))。値は `tools/UnrealExport/probe_sky_and_clouds.py` で UE 5.8 から読んだ(`.artifacts/UnrealExport/sky/*.txt`)。雲の形は UE のテクスチャ(`T_CloudPattern`、`VT_PerlinWorley_Balanced` など)から作られ、これは取り込んでいない。
- ThirdPerson・FirstPerson・TopDown には `SM_SkySphere`(40.96 m の球を 400 倍、半径 16.384 km)が置かれ、材質 `M_SimpleSkyDome` は `SkyAtmosphereViewLuminance + SkyAtmosphereLightDiskLuminance` だけの Unlit・IsSky。Vehicle には空の球が無い(`Lvl_VehicleBasic` は SkyAtmosphere のみ)。
- ミップ: 修正前は `mipLevels:1`。64×64 の市松の床を地平線へ向けて撮ると、遠景(地平線の下 10 行)の輝度の分散が 1267.5、2 フレーム(カメラ 0.37 m 前後)の平均絶対差が 9.50。ミップありでは 0.0 と 0.00(遠景は平均の灰色に収束)。近景(下 10 行)の分散は 4256.6 → 3953.4 で、近くの市松は潰れない。
- 雲の WARP の費用(320×180): 初回の全パノラマ(1024×256、24 サンプル)を含む最初のフレームは +128 ms 程度、以降の毎フレームは 1/16 の更新で雲なしと同程度(15〜16 ms 対 17〜20 ms。ばらつきの範囲)。

## 決定
### ミップ(`RenderTextureMipChain`)
- 取り込み後・アップロード時に CPU で作る。各レベルは前のレベルの面積重みつきボックス平均で、重みは整数(ターゲット 1 画素あたりの重なり長)、平均は丸め付き整数除算、レベルの間は 16 ビットで持ち回る。2 のべき乗でない大きさでも行を落とさない。1 画素は 1 レベル。sRGB(基本色・発光)の RGB は固定の 256 項目の復号表(製品コードは `double` 禁止のため定数で埋め込み)で線形にして平均し、最も近い 8 ビットの sRGB に戻す。アルファと法線・金属粗さは線形のまま。
- 常駐テクスチャは (内容ハッシュ, 色空間) で共有する(同じ白でも基本色と金属粗さは別)。画面 UI のテクスチャは `TopLevelOnly`(1 レベル)で変わらない。サンプラーは異方性 8(UE の `r.MaxAnisotropy`)。
### 雲
- `VolumetricCloud` Component(既定値が UE のテンプレートの値)。パノラマ(上半球。方位×√仰角)にレイマーチし、空の合成とスカイライトの SH が同じパノラマを読む。1/16(4×4 ブロックの Bayer 順の 1 画素)を毎フレーム更新し、雲・大気・太陽が変わったときだけ全画素。風はカメラの位置とは別に雲を動かす(固定 tick の足し算のゲーム時間。自動実行は全アダプターで同一)。WARP は 1024×256・24 サンプル、GPU は 2048×512・48 サンプル(`Device.IsWarp`)。表示は 3 次 B スプライン(4 回のバイリニア)で、雲の縁に格子を見せない。
- 形状(形の違い): UE の雲テクスチャは使わず、エンジンの Perlin-Worley の 3D ノイズ(32³ の GPU 生成)から積雲を作る。被覆(Coverage −0.2)で青空に雲が散る見た目に合わせた(UE の被覆の定義は材質のグラフが複雑で、同じ式ではない。推測)。
- `Atmosphere.hlsl` に足したので、Stage の契約は変えない(Atmosphere は `compute-v2` のホスト所有ルートシグネチャ)。雲の無い設定(`VolumetricCloud` なし)は既存の画像とビット単位で一致(`StandardModeGoldenFrameTests` の 7 つのハッシュは不変)。密度 0 の雲は雲なしと 1 段階以内。
### 空の球(`SkySphere`)
- UE と同じく「球より外の面は空に隠れる」。`SM_SkySphere` を描くのでなく、空の合成で、球の外の面(標準深度なら再構成した位置、radial 深度なら `T(2^(depth G) − 1)` の距離)の画素にも空を書く。球より内側の面は残る。大気が無いと効かない。既存の `StageAtmosphere` との関係: 空は `SkyAtmosphere` が描き続け、`SkySphere` はその範囲(どこから先を空で隠すか)を決めるだけ。インポーターは空の球のアクターを `SkySphere` ノードに置き換える(以前は捨てていた)。
### メッシュの距離場(`MeshDistanceField`)
- 置き場: ビルド時の `tools/CSharpGameEngine.ShaderGraph.Cli build-distance-fields <glb> <out.cgesdf>`。シェーダー成果物と同じ流れ(MSBuild の `CompileCgeMeshDistanceFields`、入力が新しいときだけ、`obj\...\DistanceFields\` から `Assets\<パス>.glb.cgesdf` へコピー)。Cook(ADR-0079)でなくこちらにした理由: Cook は「シーンごとに 1 つのプラグイン固有の成果物」を作る仕組みで、これはシーンに依存しないアセットごとの前処理(シェーダー成果物と同じ性質)であり、CLI がすでに SDK パッケージに入っているため。ジオメトリだけで、ライティングは焼かない。
- 形式 `CGEMSDF1`: メッシュごと(取り込み順)に内容ハッシュ、解像度、立方体のボクセル、角、ボクセルの大きさ、距離(ボクセル単位の半精度)。長い辺を 44 ボクセル(余白 2 ずつ、最大 48)。大きさは正確な最近三角形までの距離(median-split の BVH。同順位は三角形番号で決まり決定的)、符号は X・Y・Z の 3 方向の光線の交差の偶奇の多数決(閉じたメッシュで正確。開いた板は両側とも正)。スキンのメッシュには作らない。ランタイムは内容ハッシュが読み込んだメッシュと合わないエントリを無視する(ファイルが無くても動く)。基本形状(`PrimitiveMesh`)は実行時に一度作る。
### グローバル距離場(`StageGlobalDistanceField`、`GlobalDistanceField` Component)
- クリップマップ 4 段(0.5 m、各段 2 倍、64³。WARP の既定)、`Texture3D<half>` を世界の整数ボクセルへトロイダルにアドレスする(ボクセル g は texel g mod 解像度)。ボクセルは世界に固定で、カメラの固定小数位置の整数格子(`WorldVoxelGrid`)から原点を決めるので、遠くへ行っても泳がない(10^12 ボクセル先でも同じ値の試験)。距離は 4 ボクセルで打ち切る。
- 毎フレームの差分: 8³ のブリックだけ再計算する。カメラが動いて新しく覆われたブリックと、動いた・現れた・消えた物体の 4 ボクセル以内のブリック。ブリックはそこに届く全物体から一から計算するので、どんな更新の列の後でも、一度に作った場と同じ(浮動小数の丸めの範囲)。物体の距離は「メッシュの距離 × 最小のスケール」(過大にならない保守的な値)。NaN の行列の物体は外す。
- M0111 への公開: 各段の `Texture3D`(`ShaderResourceState`)と `ClipmapConstants`(ボクセルの大きさ・打ち切り・解像度、クリップの角の render 空間位置、角の texel)、HLSL の `SampleGlobalDistance` と `TraceGlobalDistanceField`(球トレース。試験は `TraceGlobalDistanceFieldForTesting`)。画面には何も描かない(試験: 標準フレームの画素は不変)。
### 描画契約
- `stage-v11`(Stage の ABI)は変えない。Stage シェーダー、Stage グラフ、`StagePresentationShaderSource` の変更は無い。変わったのは計算シェーダー(`Atmosphere.hlsl`、新しい `GlobalDistanceField.hlsl`。ともに `compute-v2`)と、材質テクスチャの SRV(1 レベル → 全レベル。ルートシグネチャの形は同じ)とサンプラー(異方性)。そのためゴールデンのハッシュ(`StandardModeGoldenFrameTests` の 7 つ)は不変だった。材質テクスチャが 1×1 の既定やミップで画素が変わらない試験の範囲では、ハッシュを更新していない(根拠: 実行して不変)。

## 作ったもの
`src/CSharpGameEngine.Rendering/RenderTextureMipChain.cs`、`MeshDistanceField.cs`、`src/CSharpGameEngine.Windows/Graphics/StageGlobalDistanceField.cs`、`StageAtmosphere.cs`・`StageRenderer.cs`・`EnvironmentLighting.cs` の拡張、`assets/Shaders/Atmosphere.hlsl`(雲・空の球)、`GlobalDistanceField.hlsl`、`src/CSharpGameEngine.Runtime.Lighting/EnvironmentComponents.cs`(`VolumetricCloud`・`SkySphere`・`GlobalDistanceField`)、`src/CSharpGameEngine.Runtime.Windows/`(`EnvironmentCollection`、`RuntimeSystems`、`EngineHost`、`StageSceneDataBuilder`)、`tools/CSharpGameEngine.ShaderGraph.Cli`(`build-distance-fields`)、`build/*.targets|props`、`tools/UnrealExport/probe_sky_and_clouds.py`、インポーター、4 つのレベルのシーン。

## 検証
- 単体: `RenderTextureMipChainTests`(18。1 画素、奇数の大きさ、sRGB の線形平均、平均の保存、決定性と復号表のハッシュ、壊れた入力)、`MeshDistanceFieldTests`(8。立方体・球の符号と大きさ、開いた板、決定性とファイルの往復、空・退化・NaN・巨大・薄いメッシュ、壊れたファイル)、`EnvironmentComponentTests`(+6)。
- GPU(WARP、D3D12 のデバッグ層のエラー 0): `MaterialMipmapGpuIntegrationTests`(2。遠景の分散 1267.5 → 0.0、フレーム間の差 9.50 → 0.00、近景は潰れない、1×1・3×5・300×7 のアップロード)、`VolumetricCloudGpuIntegrationTests`(6。雲が空の一部を覆う、決定的、風と 1/16 更新、無効な値、空の球が標準・radial の両方で効く、密度 0 は雲なしと一致)、`GlobalDistanceFieldGpuIntegrationTests`(8。全段の符号と大きさ(最大誤差 0.04 m)、カメラの移動で新しいブリックだけ(64 → 16/0/0)、動く物体・消えた物体、トレース、画素不変、遠方、`RenderServer` が距離場を保つ(修正前は失敗))。
- 全テスト `dotnet test CSharpGameEngine.slnx -c Debug`: 下の「実行結果」。

## 実走(WARP)と UE との再比較
下の Conformance と同じ内容(`Docs/Conformance.md` の M0110 の項)。

## 範囲外・未実装
- UE の雲テクスチャの取り込み(`T_CloudPattern` など)と、雲の影(地面へ落ちる影)、雲による大気の遠近感の詳細、雲のライトシャフト。
- 実機の GPU での見た目とフレーム時間(WARP のみ)。
- 距離場のサーフェスキャッシュ・最終収集・反射(M0111)。距離場の解像度の自動調整(UE は大きさで 128 まで)。
- スキンのメッシュの距離場。動的な `StaticMesh` の距離場の作り直し(読み込み後に追加した Entity は、そのアセットの `.cgesdf` があれば入る)。
