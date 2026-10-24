# M0110 — Lumen 相当の土台と見た目の不足
- 日付: 2026-10-23
- 状態: 完了(2026-10-23)。範囲外と残りは末尾の表

## 目的
ADR-0078 §2「Lumen 相当の動的 GI と反射、ボリュメトリック雲、空の球」のうち、M0111(GI と反射)の土台と、UE の参照画像との比較(Conformance「Vehicle の UE 参照画像との比較」、2026-10-22)で分かった見た目の不足を埋める。

- 雲が無い(UE は VolumetricCloud の雲のある青空)。
- ミップマップが無い(材質テクスチャは 1 段だけで、遠景がちらつく)。
- 空の球(`SM_SkySphere`)を取り込んでいない。
- M0111 がトレースする距離場が無い。

静的なライティングのベイクは作らない(ユーザーの決定)。焼くのはジオメトリの前処理(メッシュの距離場)だけ。

## 決定(ADR-0078 に従う細部)
| 項目 | 決定 | 理由 |
|---|---|---|
| ミップを作る時と場所 | テクスチャをアップロードするとき、CPU で作る(`RenderTextureMipChain`、`CSharpGameEngine.Rendering`)。成果物の形式は変えない | 同じテクスチャが sRGB の欄(基本色・発光)と線形の欄(法線・金属と粗さ)の両方に使われうる。平均の仕方が欄で違うので、使う欄が決まる読み込み時が自然。成果物を変えないので作り直しも要らない |
| ミップのフィルター | 面積加重の箱フィルター。重みは整数(目標の 1 画素が覆う元の画素の重なりの長さ)で、2 のべき乗でない大きさも全画素を使う。sRGB の RGB は 16 ビットの線形へ表で戻して平均し、最も近い 8 ビットの値へ戻す。アルファは線形。段から段へは 16 ビットで運ぶ | 決定的(浮動小数を使わない)。Kaiser は使わない(箱で十分にちらつきが消えることを測った) |
| sRGB の表 | 256 個の定数で持つ(IEC 61966-2-1 の式 × 65535、偶数丸め)。表のハッシュを試験で固定 | 製品コードに double を書けない(NUM001)。どの環境の pow でも結果が変わらない |
| サンプラー | 材質のサンプラー(Stage の s0)を異方性 8 にする(UE の `r.MaxAnisotropy` の既定) | 遠くの斜めの面を鈍らせない |
| 描画契約 | `stage-v12`(`s0-linear` → `s0-anisotropic-wrap`)。Stage グラフと `StagePresentationShaderSource` の中身は変えない | 材質のサンプラーの意味が変わったので、契約の文字列を正直にする。シェーダーは同じ |
| 画面レイヤー | テクスチャは 1 段のまま、別のルートシグネチャとサンプラー(線形、clamp)のまま | 文字と画像は表示の大きさで描く。距離場の文字の閾値を変えない |
| 雲の値 | UE 5.8 の 4 テンプレートのレベルの `VolumetricCloud`(同じ値)と、既定の雲の材質 `m_SimpleVolumetricCloud_Inst` の値を `tools/UnrealExport/probe_sky_and_clouds.py` で読んだ。Component の既定値が UE の値 | テンプレートは既定のまま使っているので、既定値 = UE の値にすると取り込みは「Component を置く」だけで済む |
| 雲の描き方 | カメラのまわりの上半球のパノラマ(u = +F からの方位、v = √(仰角 / 90°) で地平線に行を多く)に Hillaire 系でレイマーチし、空の合成で読む。毎フレーム 4 × 4 の区画ごとに 1 画素(Bayer の順)を描き直し、16 フレームで一巡。雲・大気・太陽が変わったら全画素。WARP は 1024 × 256・視線 24 サンプル、GPU は 2048 × 512・48 サンプル | 雲は 5 km より上にあり、カメラの小さな動きでは見え方がほとんど変わらないので、方向だけで引ける。画素ごとに描く UE より安く、WARP でも動く。一巡の間の古さ(WARP で約 5 秒、実機では 0.3 秒)は雲の動きに対して小さい |
| 雲の光 | 太陽(大気の透過率 LUT を通した照度)、2 葉の Henyey-Greenstein(UE の Phase_Controls)、多重散乱の 3 段(Multiscatter_Controls)、太陽への影は 6 区間で 4.32 km まで、環境光は晴れた空の上半球の平均(空のビュー LUT から 5 方向)、雲の底の暗さ(Sky Light Cloud Bottom Occlusion)。雲の手前の空気は、雲の輝度で重みづけた深さまでの透過率で近似 | 雲の環境光に雲を含む SH を使うと、パノラマが自分自身を照らして一巡ごとに変わり、点状の模様が出た(後述)。晴れた空からなら帰還が無い |
| 雲の形 | エンジンの手続きのノイズ(タイルする Perlin-Worley、32³、GPU で一度だけ作る)。値の分布を測り(5 % が 0.585、95 % が 0.746)、0〜1 へ広げた。被覆は `0.5 × 配置 + 0.25 + 0.5 × Coverage`、高さは層の下 2 割の積雲 | UE の雲は材質のテクスチャ(`VT_PerlinWorley_Balanced`、`T_CloudPattern` など)で形を作る。テクスチャは取り込んでいない。見た目を UE の参照画像に合わせて決めた |
| 風 | 10 m/s、+F から時計回りに 45°(エンジンの選択)。時間はフレームの経過時間(自動実行では 8/60 秒ずつ) | UE の既定の雲は材質の `Layout_WindControls` で動くが、速さの単位を読めなかった。決定的な時間にして、自動実行ではどの adapter でも同じ雲にする |
| 空の光と雲 | 空の光の SH(Stage の t11)は雲を含む(パノラマを一巡するたびに作り直す) | UE の SkyLight はリアルタイムキャプチャで雲を含む |
| 空の球 | `SkySphere` Component(半径。既定 16.384 km = UE の 40.96 m × 400)。中心はノードの位置。空の合成が、遠平面の画素に加えて「球の中心から半径より遠い面」の画素を空で覆う | UE の `M_SimpleSkyDome` は Unlit・Is Sky で、色は `SkyAtmosphereViewLuminance + SkyAtmosphereLightDiskLuminance`。つまり StageAtmosphere の空の合成と同じ色を球の面に描く。違いは「球より遠いものは見えない」ことだけなので、それを合成の判定に足した(新しいメッシュの描画や Stage の項目は要らない) |
| StageAtmosphere との関係 | 空の色は StageAtmosphere(SkyAtmosphere)が作る。空の球は「どこまでが空か」を決める。雲は空の上に重なる。どれも無ければ従来と同じ(遠平面の画素だけ、雲なし) | 既存の画像を変えない |
| メッシュの距離場を作る場所 | ビルド時に `CSharpGameEngine.ShaderGraph.Cli build-distance-fields`(`build/CSharpGameEngine.ShaderGraph.targets` の `CompileCgeMeshDistanceFields`)。ゲームの `Assets\**\*.glb` ごとに `obj\...\DistanceFields\<path>.glb.cgesdf` を作り、出力の glTF の隣へコピー | glTF ごとの前処理で、シェーダー成果物と同じ「入力が新しければ作り直す」流れに載る。Cook(ADR-0079)はシーンごとでプラグインを要るので合わない。SDK パッケージは CLI を既に持つ |
| メッシュの距離場の中身 | 最長辺を 44 ボクセル(余白 2 ボクセル、1 軸最大 48)。正確な最近点距離(BVH)、符号は 3 軸の光線の奇偶の多数決、ボクセル単位の半精度。`CGEMSDF1` の項目はメッシュの内容のハッシュを持ち、読み込んだメッシュと合わない項目は使わない。骨格メッシュには作らない。基本形状(PrimitiveMesh)は実行時に作る | 決定的(単精度の決まった順の演算、BVH の同点は三角形の番号)。開いた板(床の 1 枚の面など)は両側とも正になる |
| グローバル距離場 | `GlobalDistanceField` Component(既定 4 段 × 64³、最も細かい段 0.5 m、以降 2 倍)。R16F の Texture3D をトーラス状の番地(世界の格子の座標 mod 解像度)で持つ。4 ボクセルで打ち切り。8³ のブリックを単位に、クリップが新しく覆ったブリックと、動いた・現れた・消えた物体の届く範囲だけを作り直す。原点はカメラの固定小数の位置から整数のボクセルと余りで決める | 世界に固定した格子なので、カメラが動いても場が泳がず、新しく覆った区画だけで済む。ブリックは毎回その時の物体から作り直すので、差分の結果が一括で作った場と等しい。UE は Project Settings の「Generate Mesh Distance Fields」で作る。こちらは環境の Component で有効にする(テンプレートの 4 レベルに置いた) |
| M0111 への公開 | `StageGlobalDistanceField` の段の Texture3D(シェーダーの読める状態)と `ClipmapConstants`(段ごとのボクセル・打ち切り・解像度、クリップの角の描画空間の位置、角のテクセル)、HLSL の `SampleGlobalDistance` と `TraceGlobalDistanceField`。試験用のトレース(`TraceGlobalDistanceFieldForTesting`)で使い方を確かめた | M0111 の最終収集と反射がそのまま使える |
| DDGI | ADR-0069 の DDGI プローブは残し、M0111 で低品質と WARP の代替の段にする | ADR-0078 の決定 |

## 作ったもの
| 層 | 中身 |
|---|---|
| L1 描画の型 | `RenderTextureMipChain`・`RenderTextureColorSpace`(`src/CSharpGameEngine.Rendering/RenderTextureMipChain.cs`)、`MeshDistanceField`・`MeshDistanceFieldBuilder`・`MeshDistanceFieldCodec`(`MeshDistanceField.cs`) |
| L0 描画 | `StageRenderer`(材質テクスチャの全ミップ、異方性 8、`MaterialMipLevelLimitForTesting`、距離場の受け取りと更新、試験用の読み戻し)、`StageAtmosphere`(雲のノイズとパノラマ、空の球、定数 512 バイト、ルートシグネチャに t5-t6 と u2 と s1)、`StageGlobalDistanceField`(新規)、`EnvironmentLighting.cs`(`VolumetricCloudSettings`、`SkySphereSettings`、`GlobalDistanceFieldSettings`、`LongVector3`)、`RenderServer`(距離場を写す、数の公開) |
| シェーダー | `assets/Shaders/Atmosphere.hlsl`(パス 5・6、空の合成の雲と空の球、空の光の雲)、`assets/Shaders/GlobalDistanceField.hlsl`(新規。ブリックの更新とトレース) |
| L5 ランタイム | `VolumetricCloud`・`SkySphere`・`GlobalDistanceField`(`Runtime.Lighting`、アイコン 3 つ)、`EnvironmentCollection`(変換、`EnvironmentClock`、`WorldVoxelGrid`)、`EnvironmentCollect`、`EngineHost`(雲の時間、カメラの位置、距離場の原点)、`StageSceneDataBuilder.LoadDistanceFields`、自動実行のレポートの `distanceFieldMeshes`・`distanceFieldObjects`・`frameMilliseconds` |
| L7 ビルド | `ShaderGraph.Cli build-distance-fields`(Import と Rendering を参照)、`ShaderGraph.targets` の `CompileCgeMeshDistanceFields`・`CollectCgeMeshDistanceFields`、`Game.props` の `CgeGenerateMeshDistanceFields`(既定 true)と `GlobalDistanceField.hlsl` |
| 取り込み | `tools/UnrealExport/probe_sky_and_clouds.py`・`run_probe_sky_and_clouds.cmd`、`Unreal.SceneImporter`(`VolumetricCloud` と、空の球の俳優から `SkySphere` のノードを出す) |
| テンプレート | 4 レベルに `VolumetricCloud` と `GlobalDistanceField`、ThirdPerson・FirstPerson・TopDown に `SM_SkySphere`(`SkySphere`)。Vehicle には UE でも空の球が無い |
| SDK | 0.1.21 |

## 検証
- 単体(GPU なし): `RenderTextureMipChainTests`(18。段の数と大きさ、1 画素、2 のべき乗でない 3 × 1・5 × 1・5 × 3、sRGB の黒白が線形の半分(コード 188)になる、平均の保存、決定性と表のハッシュ、壊れた入力)、`MeshDistanceFieldTests`(8。立方体の全ボクセルの符号と距離(誤差はボクセルの 1 % 未満)、球、開いた板、決定性とファイルの往復、空・退化・NaN・範囲外の添字、20 万 m の巨大なメッシュと厚さ 0.2 m の板、壊れたファイル)、`EnvironmentComponentTests`(+6。スナップショットの往復、既定値が UE の値、カメラと風の km、雲・空の球の不正な値はそれだけを止める、`EnvironmentClock`)。
- GPU(WARP、D3D12 のデバッグ層のエラー 0):
  - `MaterialMipmapGpuIntegrationTests`(2): 6.25 cm の白黒の市松の床を地平線へ向けて、ミップを 1 段に制限した描画(M0110 の前の状態)と全ミップを比べる。遠景(地平線の下 10 行)の緑の分散 1267.5 → 0.0、カメラを 0.37 m 進めた 2 フレームの差の平均 9.50 → 0.00、近景(下 10 行)の分散 4256.6 → 3953.4(模様は残る)。1 × 1、3 × 5、300 × 7 のテクスチャのアップロード。
  - `VolumetricCloudGpuIntegrationTests`(6): 密度 0 の雲は雲の無いフレームとビット単位で同じ。UE の値の雲は空の画素の 71 %(13624 / 19200)を変え、変わった画素はすべて元の空より灰色寄り、パノラマの 44.3 % が「空が半分未満」、2 つの描画でフレームとパノラマがビット単位で同じ。風のずれでパノラマが変わり、同じずれなら同じ。全面の更新の次のフレームで変わる画素は、4 × 4 の区画の (0, 0) だけ。不正な値は拒まれる。空の球は標準と放射の深度の両方で、30 km 先の壁を 16.384 km の球なら空で覆い(空だけの画素と同じ)、50 km の球なら残す。WARP の 320 × 180 で、最初のフレーム(全面の更新) 128 ms、次から 16 ms(雲なし 162 ms・20 ms。揺れの範囲)。
  - `GlobalDistanceFieldGpuIntegrationTests`(8): 3 段 × 32³ で、立方体の正確な距離との差は最大 0.040 m(打ち切り 4 ボクセル、符号も一致)、拡大して回した箱は距離を超えない。カメラを (5.3, 0.7, 0) m 動かすと、段 0 は 64 ブリック中 16 だけ作り直し(段 1・2 は 0)、一度に作った場との差は 1 mm 以内(浮動小数の丸め、最大 1.2e-7)。物体を 3 m 動かすと 12 ブリックを作り直し、一度に作った場とビット単位で同じ。消した物体の場所も戻る。トレースは箱の正面と側面に当たり(期待との差 0.3 m 以内)、空へは外れる。距離場は画素を変えない(ハッシュが同じ)。10^12 ボクセル離れた原点でも同じ値。距離場の無いメッシュは入らない。NaN の行列の物体は入らない。RenderServer を通しても距離場が届く(修正前に失敗、後述)。
  - `StandardModeGoldenFrameTests`(7 つのハッシュは不変)、`ScreenLayerTests`、`StageGpuIntegrationTests`(常駐テクスチャ 4 → 5)、`AtmosphereGpuIntegrationTests`、`GlobalIlluminationGpuIntegrationTests`、`RadialDepthGpuIntegrationTests`、`ShaderArtifactHotReloadTests`、`ShaderGraphCompilerTests`(Stage の HLSL のハッシュは不変)。
- 実走(WARP、`verify-thirdperson-character`): ThirdPerson 15.700 m(上昇 1.228 m)、FirstPerson 16.000 m(0.863 m)、TopDown ナビなし 9.606 m・ナビあり (6.79, 6.84)・長押し 17.414 m、Vehicle 52.014 m・30.5°(右トリガー 0.5 は 23.172 m、左スティック 0.5 は 9.4°)。すべて M0109 と同じ。D3D12 のエラー 0。ThirdPerson の自動実行のレポートで、距離場つきメッシュ 8、場の物体 54(レベルの全 StaticMesh。キャラクターは骨格メッシュなので入らない)。
- フレーム時間(WARP、ThirdPerson 60 フレーム、W を 20〜40 フレーム): 雲・空の球・距離場あり 1119・892・917 ms、外した場合 1526・1130・1000 ms(中央値)。同じ機械で Unity などが動いており、差は揺れの範囲で、増えたとは言えない。実機の GPU は未確認。
- 全テスト(`dotnet test CSharpGameEngine.slnx -c Debug`): (後で記入)
- パッケージ(`verify-template-package`、SDK 0.1.21): (後で記入)

## 実装中に見つけた事実
- `RenderServer` は受け取ったメッシュを(デバイスの復旧のために)写すが、その写しが新しい `DistanceField` を落としていた。ゲームでは距離場つきメッシュが 0 になった(ビルドの `.cgesdf` は正しく、ランタイムの読み込みも正しかった)。写しで距離場を共有するように直し、`RenderServer_KeepsEachMeshsDistanceFieldForTheRendererAndAddedMeshes` は修正を外すと失敗し、戻すと合格することを確かめた。
- 雲の Perlin-Worley ノイズの値は 0.58〜0.75 に集まっていた(Python で HLSL と同じ計算をして分布を測った)。被覆の閾値がそのままでは空の 75 % を覆ったので、5 %・95 % の点で 0〜1 に広げた。
- 最初の全面の更新では空の光の SH がまだ 0 で、雲の環境光が 0 の画素と、次の一巡で SH を使った画素が混ざり、4 × 4 の点の格子が見えた。雲の環境光を晴れた空のビュー LUT から取るようにして、帰還をなくした。
- パノラマを双線形で読むと、1280 × 720 では雲の縁がパノラマの画素に沿った階段に見えた。合成だけ 4 回の双線形で 3 次 B スプラインにした。
- 製品コードでは double が使えない(NUM001)。sRGB の表は定数にし、自動実行のフレーム時間は `ElapsedTicks` から float で出した。
- 材質の常駐テクスチャは色空間ごとになったので、共有の白(基本色の sRGB と金属・粗さの線形)が 2 つになる。`StageGpuIntegrationTests` の期待値は 4 → 5。
- UE の `M_SimpleSkyDome` は Unlit・Is Sky で、発光が `SkyAtmosphereViewLuminance + SkyAtmosphereLightDiskLuminance` だけ(`probe_sky_and_clouds.py`)。Vehicle のレベルには空の球の俳優が無い(UE の manifest の `skyDomeMaterial` は書き出しの既定値)。
- 4 テンプレートの雲の値は同じ(下端 5 km、厚さ 10 km、Tracing Start Max 350 km、Tracing Max 50 km、密度 0.008、被覆 −0.2、配置の大きさ 256、アルベド 0.98、位相 (0.8, 0.166667, 0.575)、多重散乱 (0.666667, 0.25, 0.18)、底の暗さ 0.5、打ち切り 0.005)。

## UE の参照画像との再比較
- 方法: エンジンの 8 フレーム目(入力なし、WARP、1280 × 720)を、UE の `ue_reference_spawn_camera`(ThirdPerson)と `ue_reference_chase`(Vehicle)と並べて見た。画像は `.artifacts/m0110/ue2/`。
- ThirdPerson: 前回は雲の無い勾配だったが、今は青空に白い積雲が散らばり、地平線へ向かって平たく小さくなる。UE とほぼ同じ見た目の空になった。UE の雲は縁がより滑らかで、こちらは細かい凹凸が多い。被覆はほぼ同じ。
- Vehicle: 雲のある青空になった。こちらの空は UE より青が濃く、雲の陰が暗い(UE はヒストグラムの自動露出で全体が明るい。露出と、車の塗装の暗さ・金属の反射が無いことは M0111 の間接光と反射の範囲)。床(Landscape とトラック)は M0112 の範囲のまま。
- ミップ: テンプレートの床と壁はテクスチャでなく色か手続きの格子なので、画像の差はほぼ見えない。車のテクスチャは全ミップになった。ちらつきの減り方は上の GPU 試験の数値で示した。

## 範囲外・残り
| 項目 | 状態 |
|---|---|
| サーフェスキャッシュ、最終収集、反射(Lumen 相当の GI と反射) | M0111 で実装([M0111](M0111-Lumen-Dynamic-GI-And-Reflections.md)) |
| 地面への雲の影、遠くのジオメトリの手前の雲、雲の中に入るカメラの見え方の検証 | 未実装(テンプレートでは起きない) |
| UE の雲のテクスチャ(`VT_PerlinWorley_Balanced`、`T_CloudPattern`、`T_Profile_08`、`T_CloudMask`)と嵐の雲 | 取り込んでいない(手続きのノイズで代用) |
| 実機の GPU での見た目とフレーム時間 | 未確認(WARP のみ) |
