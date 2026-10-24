# M0112 — テンプレートごとの仕上げ
- 日付: 2026-10-24〜25
- 状態: 完了(ただし項目ごとの表のとおり。実機の GPU は未確認)。範囲外と残りは末尾の表

## 目的
ADR-0078 の段階 2。Vehicle のレベルを UE の Landscape の路面・Spline のトラック・傾斜・アーチに近づけ、4 つのレベルの見た目(雲・空の球・Lumen 相当の GI と反射)を UE と比べ、`UE-Template-Behavior.md` に残っていた「一部」「未」の行をすべて「実装済み」か「作らない」に決める。VS テンプレートとパッケージ検証を最新にする。

## 事実(UE の読み出し。2026-10-24)
- Landscape は **平らではない**。126 × 126 の格子(1 m、`ComponentSizeQuads` 63 の 4 コンポーネント、位置 (0, −6300, 0) cm、拡大 100)で、中央に最大 2.84 m の丘がある(高さは衝突のライン トレース。127 × 127 点のうち 0 点の外れ)。M0102〜M0109 の「Landscape は平らな高さ 0」は誤りだった。UE の三角形の分け方は各格子の対角線 (x, y)-(x+1, y+1)(格子の間の 400 点の高さが 0.002 cm 以内で一致。もう一方の対角線は最大 1.26 cm ずれた)。
- Landscape Spline は 4 つの制御点(半径 45 m の輪)と 4 つの区間で、層 `Road` を塗る。`SM_Track_10M`(10 m × 10 m、路面 ±3.93 m、縁石の頂 0.123 m)を 16 個、1.6076 倍に広げて沿わせる(路面の高さ 0.077 m、縁石の頂 0.198 m、輪の路幅 ±8 m、層の落ち 5 m)。物理マテリアルは Landscape が Slippery(摩擦 1.0 で掛け算、反発 0.4)、トラックが NonSlippery(摩擦 1.0、反発 0)。
- 材質: `M_Landscape` は Material Attributes で、基本色は RVT(Runtime Virtual Texture)を読み、無ければ `LandscapeLayerBlend`(Road = 橙 (1, 0.095, 0)、Ground = 灰 0.26 と橙を `pow(法線·上, 4)` で混ぜる)。粗さ 0.8。`M_Track` は 10 × 10 のチェッカー(`CheckerUV` 5 を 2 倍)で、路面 `MI_TrackEdge_Inst` は線形 0.0052 と 0.0156 の暗い灰、縁石 `MI_TrackEdge` は橙 (0.58, 0.066, 0) と灰 0.18。粗さ 1、金属 0。UE の glTF エクスポーターは、これらを UV 空間の基本色のテクスチャへ焼く(描画デバイスありで)。
- 参照画像(2026-10-22 の `ue_reference_chase` など)は FOV 90 の SceneCapture2D だったが、Vehicle のチェイスカメラは水平 110°。この項で FOV 110 に撮り直した。
- **SceneCapture2D は Lumen の GI を含まない**(下の「陰の明るさ」)。

## 決定
| 項目 | 決定 | 理由 |
|---|---|---|
| Landscape の表し方 | 静的な三角形メッシュ `Landscape_Terrain.glb`(5,959 頂点・11,496 三角形)に `StaticMesh` と `StaticMeshCollider`(摩擦 1、反発 0.4)。地形のコンポーネントは作らない | エンジンの物理は静的な三角形メッシュを持ち、起伏は 1 つの丘だけ。7 m 四方が平らなら 2 枚の三角形にするので、格子のままより 1/3 の大きさ |
| 地形の色 | 真上から `SCS_BASE_COLOR` で撮った 2048 画素(6 cm)のテクスチャ。UV は撮影の範囲 | 層の重み・斜面の色・RVT を式から読み解かずに、UE が描く基本色をそのまま使える。照明は含まない(エンジンが照らす) |
| トラック | 16 個の `SplineMeshComponent` を UE の `SplineMeshCommon.ush` の式(`SplineMeshCalcSlice`、`SplineMeshCalcSliceTransform`、法線は `SliceRot`)で曲げて、路面(`Track_Road.glb`、衝突あり、摩擦 1・反発 0)と縁石(`Track_Curbs.glb`、**描くだけ**)の 2 つのメッシュにする | 曲げた結果の位置が UE と同じになるように、同じ式を使った。UE は GPU で曲げるので CPU に値が無い |
| 縁石に衝突を付けない | 縁石は `StaticMesh` だけ | 事実(再現済み): 縁石(路面から 0.2 m の薄い殻)と路面と地形を物理に置いて、車を PlayerStart から真っすぐ走らせると、縁石の斜面と平らな地面にまたがったところで車体が沈んだ(路面と縁石を 1 つのメッシュにしても、地形に縁石だけでも、路面だけでは起きない)。`PhysicsNarrowPhase.TryBoxMeshManifold` は、箱の角のうち一番深い面の向きと合う角だけで 1 つの patch を作るので、斜面と平らな面にまたがると、平らな面(地形)の支えが無くなる。接触が複数の面の向きを持てるようになるまで視覚だけにする |
| 経路の最後の点の制動 | `CharacterMotion.FixedPathBrakingDistance`(m)。0 なら従来のとおり制動から計算する。TopDown は 2 m(UE の 200 cm) | UE の `bUseFixedBrakingDistanceForPaths`。経路の最後の点(`MoveIntent.WaypointIsEnd`)だけに使い、直線で歩くときと途中の点には使わない |
| 雲の量 | 4 つのレベルの `VolumetricCloud.Coverage` を −0.8 にする | 見える雲の割合を UE の 0.289 に合わせる(「陰の明るさ」) |
| 「一部」「未」の行 | すべて決めた(下の表) | |
| パッケージ検証 | `verify-template-package` に FirstPerson を足した(ThirdPerson と同じ歩きと跳躍の判定) | ADR の目的は 4 つのテンプレートの配布を試すこと |
| SDK | 0.1.24(`CharacterMotion` の欄が増えた) | NuGet は取り出した版を使い続ける |

## 作ったもの
| 層 | 中身 |
|---|---|
| L7 取り込み | `tools/UnrealExport/probe_vehicle_landscape.py`・`probe_vehicle_landscape_heights.py`・`probe_vehicle_surfaces.py`・`probe_vehicle_ground_materials.py`・`probe_vehicle_ground_t3d.py`・`run_probe_vehicle_landscape.cmd`(UE の読み出し)、`capture_vehicle_ground_color.py`・`run_capture_vehicle_ground_color.cmd`(真上からの基本色と照明つきの真上の画像)、`build_vehicle_ground.py`(UE を使わない組み立て。生成物は同じ入力で同じバイト列)、`capture_vehicle_reference.py`(FOV 110、丘と車の脇の視点を足した)、`capture_lumen_reference_check.py`・`capture_thirdperson_lighting_split.py`・`capture_thirdperson_lumen_toggle.py`・`capture_thirdperson_viewport.py`(参照画像の撮り方の確認) |
| L9 テンプレート | `Assets/Stage/Lvl_VehicleBasic/Meshes/Landscape_Terrain.glb`・`Track_Road.glb`・`Track_Curbs.glb`、`Scenes/Lvl_VehicleBasic.json`(`Ground` の板を `Landscape`・`Track`・`TrackCurbs` に)、TopDown の `TopDownCharacter.json`(`FixedPathBrakingDistance`)、4 つの ReadMe |
| L5 | `CharacterMotion.FixedPathBrakingDistance`、`MoveIntent.Steer` |
| ツール | `FeatureRunner` の `verify-template-package --template FirstPerson`、`FeatureStageCatalog`(試験の追加) |
| 試験 | `VehicleLevelTests`(21)、`TemplateLightingSceneTests`(4)、`TemplateCharacterSceneTests`(+1)、`CharacterMotorTests`(+1)、`ChaosVehicleTrack`(glb の三角形を置く `AddGltf`、開始位置) |

## 「一部」「未」の行の決定(`UE-Template-Behavior.md`)
| 行 | 決定 | 試験・理由 |
|---|---|---|
| TopDown の経路の追従(固定の制動距離) | 実装済み | `CharacterMotorTests.AnEndWaypoint_BrakesOverTheFixedPathBrakingDistanceWhenTheSettingsHaveOne`、`TemplateCharacterSceneTests.TheTopDownCharacterBrakesOverUesFixedPathBrakingDistanceOfTwoMetres` |
| `FX_Cursor_Success` の `TexCoordY` の流れ | 作らない | 描画契約に UV の動きが無い |
| ライティング(Lumen 相当の GI と反射、雲、空の球) | 実装済み | M0110・M0111。`TemplateLightingSceneTests` が 4 つのレベルの部品を固定 |
| Vehicle の地面 | 実装済み | `VehicleLevelTests` |
| TopDown のナビメッシュの Recast の細部 | 作らない | 経路の結果に出る差が無い |
| 車輪のメッシュの左の X の鏡映 | 作らない | トレッドの向きだけ |
| `ABP_SportsCar` の `CtrlRig_SportsCar` | 作らない | 見えるメッシュが付かない |
| サスペンション(UE の既定のソルバー内の制約) | 実装済み(UE の力の経路)。ソルバー内の制約は作らない | 物理に無い |
| エンジン音 | 作らない | UE のテンプレートに音の資産が無い(3 テンプレートとも `SoundWave`・`SoundCue` が 0 個) |
| Controller の BeginPlay、`UI_Vehicle` | 実装済み | M0104 の HUD。MPH は作らない(コントローラーが `bIsMPH=false`、切り替える入力が無い) |
| 陰の明るさ | 一部(調査の結果。下) | |

## 検証
- 単体: `VehicleLevelTests`(21。UE の衝突で測った高さと格子の間の 9 点が 0.5 cm 以内、丘 2.7〜2.9 m と周りの平ら、輪の路面の高さ 7.7 cm を一周、縁石の頂 0.2 m・半径 36.5〜54.5 m と縁石に衝突が無いこと、PlayerStart に落とした車が 4 輪で路面に載る、地形の上・丘の上・路面の端の外に落とした車の静止、路面から地形へ出て走り続ける、シーンの材質)。三角形メッシュの置き方は `ChaosVehicleTrack.AddGltf` がゲームの `StaticMeshCollider` と同じ。縁石と路面を衝突に含めた構成では、車が沈む事実を 一時の試験で再現してから、縁石を描くだけに分けた。`TemplateLightingSceneTests`(4)、`TemplateCharacterSceneTests`(+1)、`CharacterMotorTests`(+1)。
- 実走(WARP、デバッグ層のエラー 0): Vehicle 移動 **62.613 m**・曲がり **27.8°**(M0111 は 57.150 m・30.5°。路面が輪の接線上にあり、約 28 m で路面の外へ出て地形を走るため。カメラの高さは 1.534〜1.645 m で `VehicleDriveVerification` の範囲 1〜6 m の中)、右トリガー半分 23.306 m(23.173)・左スティック半分 9.3°(9.4°)・HUD 37 状態。ThirdPerson 15.700 m・FirstPerson 16.000 m・TopDown ナビなし 9.606 m・長押し 17.414 m は変わらない。TopDown のナビありは (6.79, 6.84) から **(6.80, 6.83)** に変わった(経路の最後の固定の制動距離 2 m。意図した変更)。
- パッケージ(`verify-template-package`、SDK 0.1.24、WARP): ThirdPerson・FirstPerson(新規)・TopDown・Vehicle の 4 つが通った。値は上と同じ。

## 陰の明るさ(UE の参照画像との比較)
依頼は「陰の明るさを UE に合わせる」(M0111 の数値: 右奥の壁 UE 7.8 に対しエンジン 68.4、斜面の陰 95 に対し 146)。**測り直して分かったこと(Fact)**:
1. **UE の参照画像に Lumen の GI が入っていなかった。** SceneCapture2D の画像は、GI の方法を `r.DynamicGlobalIlluminationMethod 0` に切っても 255 のうち 0.1 しか変わらず、右奥の壁の値は GI を切ったビューポート(7.0)と同じだった。ビューポートのスクリーンショット(`take_high_res_screenshot`)は GI を含む。`ShowFlag.GlobalIllumination`・`LumenGlobalIllumination` は SceneCapture2D の `show_flag_settings` では効かない。
2. Lumen を含む UE(ビューポート、同じカメラ)の間接光は、撮り始めてから 1000 tick 以上かけてゆっくり明るくなる(右奥の壁 24.6 → 27.4 → 30.1。1200・1700・2200 tick)。AO を切ると 31.8、Lumen の短距離の AO も切ると 34.0。つまり UE の AO の効きは 6〜13 %。**エンジンの AO は同じ場面で約 1 %**(AO の強さ 0 と 0.5 で 47.1 と 46.7)。
3. 日向の面はもともと一致する(床 184.6〜185.2 対 186.7)。陰の面は、Lumen を含む UE に対してエンジンが明るく、日向の背の壁はエンジンが暗い(コントラストが低い)。
4. 空の雲の割合(空の領域で、彩度が低く明るい画素の割合): UE 0.289、エンジン 0.711。雲を消したエンジンの右奥の壁は 46.7 → 37.6、空の光を消すと 19.9(UE は空の光を消すと 1.3、GI を切ると 7.0)。**雲の量の差が、陰の明るさの差の一部を作っていた。**

| 領域(輝度 0〜255。エンジンは左右反転して比較) | UE の SceneCapture(Lumen なし) | UE のビューポート(Lumen あり) | エンジン High・調整前 | エンジン High・調整後 |
|---|---|---|---|---|
| 左の斜面の面 | 114.3 | 132.3 | 145.7(線形 1.23 倍) | 140.2(1.13 倍) |
| 右の斜面の面 | 90.9 | 110.0 | 128.1(1.39 倍) | 122.5(1.26 倍) |
| 右の壁(陰) | 7.3 | 30.1 | 40.4(1.65 倍) | 35.9(1.34 倍) |
| 左の壁 | 37.1 | 58.0 | 67.1(1.33 倍) | 61.2(1.11 倍) |
| 背の壁(日向) | 108.9 | 118.7 | 111.4(0.87 倍) | 107.1(0.80 倍) |
| 床の近く(日向) | 184.6 | 185.2 | 186.7(1.02 倍) | 182.7(0.97 倍) |
| 空の雲の割合 | 0.277 | 0.289 | 0.711 | 0.287 |

**調整**(根拠は 4 の測定): 4 つのレベルの `VolumetricCloud.Coverage` を既定の −0.2 から **−0.8** にした。雲の割合を UE の 0.289 に合わせる値(−0.2 → 0.711、−0.6 → 0.454、−0.8 → 0.287、−1.0 → 0.148 を測った)。陰の面の差が 1.23〜1.65 倍から 1.11〜1.34 倍になった。日向の面は少し暗くなった(背の壁 0.87 → 0.80)。値を陰の明るさに合わせて決めたのではなく、見える雲の量に合わせた。**残る差(未解決)**: 陰の面がまだ 1.1〜1.3 倍明るく、日向の背の壁が 0.8 倍暗い。エンジンの AO が効いていない(上の 2)ことと、UE の Lumen が間接光を時間をかけて溜めること(上の 2)が候補で、まだ試していない(UE は収束の途中の値なので、差は最大で 1.1 倍ほど小さくなる見込み)。実機の GPU は未確認。

## UE の参照画像との再比較(Vehicle)
- 方法: `capture_vehicle_reference.py`(FOV 110)の丘(`ue_reference_hill`)と車の脇(`ue_reference_track_side`)を、エンジンの `CGE_AUTOMATION_CAMERA="-62.5,8.36,32,140,18"`・`"-6,1.58,15,150,6"`(車からの位置のずれと向き。エンジンの画像は UE の左右の鏡像)で撮り、左右反転して並べた(`.artifacts/m0112/run/vehicle_*_vs_ue.png`)。
- 観測: 構図は一致した。UE の床は **チェッカーの路面と縁石つきのトラック、灰色の丘、橙のにじみ**(Road 層の 5 m の落ち)、アーチは丘から立つ。エンジンも同じになった(路面、縁石、にじみ、丘の上のアーチとコーン)。参照画像の車の位置は、UE の静的な車が空中に浮く構図で、エンジンの車は路面に載る。
- 差: 路面のチェッカーはエンジンが暗い(上から見た路面の平均輝度 UE 75〜96、エンジン 26〜34。UE の SceneCapture は Lumen を含まず、粗さ 1・F0 0.04 の路面の反射の寄与がエンジンより大きいと推測するが、測っていない)。空の雲はエンジンの形が異なる(M0110)。壁の色は UE が黄みのオレンジと暗い赤茶、エンジンは赤みが強い。

## 範囲外・残り
| 項目 | 状態 |
|---|---|
| 実機の GPU での見た目・フレーム時間 | 未確認(WARP のみ) |
| 縁石の衝突 | 作らない(物理の接触が複数の面の向きを持てるようになるまで。次の課題) |
| 陰の明るさ | 一部の差が残る(日向の背の壁 0.80 倍、陰の面 1.1〜1.3 倍)。原因の候補: エンジンの AO が効かない、UE の Lumen の溜め方。UE の参照は Lumen を含むビューポートに取り直した |
| Vehicle の路面の暗さ(UE の約 1/3) | 未調査(粗い路面の反射の寄与の差と推測) |
| UE のビューポートの参照を長く収束させる(1000 tick 以上) | 次の比較で行う |
