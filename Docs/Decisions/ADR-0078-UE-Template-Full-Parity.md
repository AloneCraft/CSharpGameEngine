# ADR-0078 — UE5 テンプレートの挙動をコードレベルで解析し、ECS で完全に移植する

M0114以降の番号は、[ADR-0080](ADR-0080-Universal-Model-Import.md)のユニバーサルモデル取り込みロードマップ(M0114〜M0122、Proposed)が使用する。M0112/M0113はこのADRの予約を維持する。M0114の実装・検証状況は専用の[マイルストーン](../Milestones/M0114-Universal-Model-Import.md)で追跡し、この番号予約を機能の実装完了とは扱わない。
- 状態: Accepted(2026-10-22。ユーザーの依頼:「ue5テンプレートの動作全てをコードレベルで解析して実装して」「ecsで実装するが必要となるライティングベイクシステムなども」「カオス破壊機能も実装」「ライティングの静的ベイクは今はなし。lumen相当を実装して」)
- 関連: ADR-0077(ゲームコードの書き方)、ADR-0069(物理ベースの空と動的 GI)、ADR-0067(キャラクターの移動)、ADR-0068(VS テンプレート)、ADR-0017(UE のレベル書き出し)、ADR-0079(プラグインとアセット)、M0102〜M0113

## 背景
- 4 つのテンプレート(ThirdPerson / FirstPerson / TopDown / Vehicle)は、核の遊び(歩く・見る・跳ぶ、クリック移動、運転)だけを移植していた(M0101 段階 8・9)。
- それまで UE の Blueprint は、既定値の書き出し(`dump_blueprint.py`)と、`.uasset` の中の読める文字列からの推測でしか読めず、処理の流れは推測だった。例: 推測では「速度 3 以上で歩く」としていたが、実際の `ABP_Unarmed` は `GroundSpeed > 0.01 && 加速度 ≠ 0` だった。

## 決定

### 1. 解析の正本は、UE のテキスト書き出し(T3D)とする
- `tools/UnrealExport/dump_blueprint_logic.py`(`run_dump_blueprint_logic.cmd`)で、資産ごとに次を書く。
  - `<名前>.t3d`: Blueprint 本体の全文。グラフのノード・ピン・接続、アニメーションの状態機械と遷移、ウィジェット、入力の割り当て、カーブを含む。
  - `.class.t3d` と `.defaults.t3d`: 生成クラスと既定オブジェクト(CDO)。Blueprint で足したコンポーネントのテンプレートが入る。T3D は既定値からの差分だけを書く。
  - `.components.txt`: CDO と、その各コンポーネントの全属性。構造体は UE の `export_text` で書く。Python の `dir()` に出ない編集専用の属性(例: `EngineSetup`、`Mass`、`WheelSetups`)は、エンジンのヘッダーの `UPROPERTY` から名前を拾って読む。
  - `.graphs.json`: `BlueprintGraphEditor` によるノードとピンの一覧。UE のロケールの文字列が入るので、参考扱い。
- 疑似コードは、T3D からエンジン側のツールで作る(`CSharpGameEngine.Unreal.SceneImporter logic <dir>` → `<名前>.logic.txt`)。
  - 入口(イベント・入力・関数)から実行ピンをたどって文にする。データのピンは、純粋なノードを式として展開する。分岐は出力ピンごとに字下げする。
  - アニメーションは、状態・遷移・遷移の条件式と、出力から入力ポーズへさかのぼる木にする。
  - UE5.8 の EditorToolset の逆コンパイラ(`read_graph_dsl`)は、ヘッドレスでは空を返したので使わない。
- 出力先は `.artifacts/UnrealExport/logic/<Template>/` で、リポジトリには入れない。UE の資産から何度でも作り直せるため。仕様は `Docs/Specifications/UE-Template-Behavior.md` にまとめる。

### 2. 作るもの・作らないもの
| 対象 | 判断 | 理由 |
|---|---|---|
| 4 テンプレートの Blueprint の処理(入力、移動、カメラ、アニメーション、リスポーン、車両の全操作、HUD) | 作る | 依頼の本体 |
| タッチ操作(`BPI_TouchInterface*`、`UI_Thumbstick`、`UI_Touch*`)とハンドルコントローラー(`IMC_SteeringWheel`) | 作らない | ユーザーの決定(画面 UI は作る、タッチは除く)。UE でもモバイルか強制設定のときだけ有効 |
| 配置されていない Blueprint(`BP_JumpPad`、`BP_DoorFrame`、`BP_WobbleTarget`、`BP_VehicleAdvOffroadCar`) | 作らない(解析だけ残す) | どのレベルにも置かれておらず、テンプレートの動作に現れない |
| XR(Vive・Oculus など)のキーの割り当て | 作らない | エンジンは XR の入力を持たない |
| 静的なライティングのベイク | 作らない | ユーザーの決定。UE のテンプレートも Lumen の動的 GI で、焼いていない |
| Lumen 相当の動的 GI と反射、ボリュメトリック雲、空の球 | 作る | ユーザーの決定。ADR-0069 の DDGI プローブは、低品質と WARP の代替の段として残す |
| Chaos Vehicle 相当(剛体、車輪ごとのサスペンション、トルク曲線、変速、アッカーマン操舵、タイヤの摩擦、空中の減衰、転倒とリセット) | 作る(M0109 で実装) | ユーザーの決定。M0101 段階 9 の簡易モデルは置き換えた |
| Chaos の破壊(Geometry Collection、Fracture、Field に相当) | 作る | ユーザーの追加依頼。テンプレートには無いので、エンジン機能と専用の検証シーンにする |
| Vehicle のレベルの路面(Landscape の舗装の材質)、Spline に沿ったトラックと縁石(`SM_Track_10M`)、傾斜、アーチ | 作る(M0112 で実装) | UE の参照画像との比較(2026-10-22)で、エンジンが平らな板 1 枚だと分かった。Landscape は glTF に書き出せないので、UE から高さと Spline のメッシュの配置を読み、静的な三角形メッシュにした。地形のコンポーネントは作らない(§6) |
| 地形(`Terrain`)のコンポーネント、Landscape の層と仮想テクスチャ | 作らない(M0112) | テンプレートのレベルは静的で、起伏は静的なメッシュで足りる。層の重みと斜面の色は UE の撮影で基本色のテクスチャにした |
| Vehicle のエンジン音 | 作らない(M0112) | UE のテンプレートに音の資産が無い。§2 の「音」と同じ理由 |
| UE の制約のサスペンション(Chaos のソルバーの中)、`CtrlRig_SportsCar`、左の車輪の X の鏡映、`M_Cursor` の UV の流れ、Recast の輪郭の単純化・タイル | 作らない(M0112) | 見た目や結果に出る差が無いか、契約を変えるほどではない。行ごとの理由は `UE-Template-Behavior.md` |
| ナビメッシュ(TopDown の `SimpleMoveToLocation`) | 作る(M0106 で実装) | 経路探索はテンプレートの挙動。ナビメッシュは生成したデータで、ライティングではない |
| 音 | テンプレートには無い(UE のテンプレートに音声資産が無い) | M0101 で取り込んだ Epic 教材の音はそのまま |

### 3. Chaos 相当(車両と破壊)のレイヤー
`Docs/Architecture.md` の L0〜L9 と、既存の決定的な物理(M0055 / M0096)に合わせて置く。

| レイヤー | 置くもの |
|---|---|
| L5 `Runtime.Physics`(既存の拡張) | ソルバー本体: 剛体、結合のひずみ、クラスターの分離、破片の剛体化、アンカー、破片の上限と除去。UE の Chaos の実行時に当たる。 |
| L5 プラグイン `plugins/CSharpGameEngine.Vehicles`(ADR-0079) | 車両: 車輪ごとのサスペンション、トルク曲線と変速、アッカーマン操舵、タイヤの摩擦、空中の減衰、転倒とリセット。UE の ChaosVehicles プラグインに当たり、物理の上に載る。M0101 段階 9 の `Runtime.Character` の簡易版は、ここへ移して置き換える。 |
| L5 プラグイン `plugins/CSharpGameEngine.Destruction`(ADR-0079) | 破壊の Component と System、Field。UE の GeometryCollection プラグインに当たる。結合とクラスターを解くソルバーは上の `Runtime.Physics` に置く(UE でも Chaos 本体はエンジンにある)。 |
| L4 物理ドメインの契約(`IPhysicsHost`、`PhysicsDomainRegistry`、`PhysicsContracts`) | 点に加える力、トルク、接触の衝撃(ひずみの元)のコマンドとイベント。L5 の車両は、この契約だけを使う(L5 の solver は engine-owned な L4 の契約だけを参照する、という Architecture の決定)。 |
| L3 ECS | Component と System。固定 tick の PhysicsSimulation・PostPhysics のフェーズで回る。ランタイムとベイクで同じ System を使う。 |
| L6 成果物 | 調理済みの破砕資産(破片・結合・クラスター)、破壊を記録した物理キャッシュ(`.cgephyscache` の拡張)。 |
| L7 ベイクとコンパイラ | ボロノイ分割の調理(`Physics.Cli`)、破壊を記録する Scene Bake Host。 |
| L8 VS | Fracture の操作(分割数・シード・プレビュー)、物理パネルのベイク。 |
| L9 ゲーム | Vehicle テンプレート、破壊の検証用シーン。 |
| L0 | 置かない。解くのは決定的な CPU のコードで、描画は既存のレンダラーが行う。 |

### 4. マイルストーン
計画の段階 0〜2 を、M0102(解析)、M0103(プラグインとアセットの仕組み。ADR-0079。車両・ナビメッシュ・破壊をプラグインとして作るので先に置く)、M0104(画面 UI)、M0105(実行時の生成と使い捨てエフェクト)、M0106(ナビメッシュ)、M0107(キャラクターとアニメーション)、M0108(入力の拡張)、M0109(Chaos 相当の車両)、M0110〜M0111(Lumen 相当)、M0112(テンプレートの仕上げ)、M0113(破壊)とする。各マイルストーンの決定は、そのマイルストーンの文書に書き、この ADR の表を更新する。

完了したマイルストーン: M0102(解析)、M0103(プラグインとアセット)、M0104(画面 UI。[M0104](../Milestones/M0104-Screen-UI.md): 描画契約 `stage-v11`、SDF のフォント、`ScreenText`・`ScreenImage`、Vehicle の HUD)、M0105(実行時の生成と使い捨てエフェクト。[M0105](../Milestones/M0105-Runtime-Spawn-And-Effects.md): 描画物が Entity の増減に追従する(描画契約は変えない)、`FixedTransform` の Generation 固定の修正、`Spawner.Spawn<TScene>(位置)` と `ISpawnableScene`、`Lifetime`、`ParticleBurst`、TopDown のクリック地点の目印)、M0106(ナビメッシュのベイクと経路追従。[M0106](../Milestones/M0106-Navmesh-Bake-And-Path-Following.md): Navigation プラグイン。ビルドがシーンの `NavMeshBounds` と静的な衝突形状から `Assets/Navigation/<Id>.cgenav` を焼き(最初の実際の Cook。ビルド中の自動実行と VSIX のボタン)、`NavAgent` のキャラクターが `MoveIntent.MoveTo` の目的地への A* の経路に沿って歩く。TopDown が使う)、M0107(キャラクターとアニメーション。[M0107](../Milestones/M0107-Characters-And-Animation.md): 8 方向のブレンドスペースと ABP_Unarmed の状態機械の作り直し、足の IK、FirstPerson の腕・体(カメラに見えず影だけ)・一人称の FOV と縮尺(描画の契約は変えない)、`OrbitCamera` の遅れ追従と `SocketOffset`、`CharacterMotion` の平面への拘束、`KillZ` と `PlayerStarts` によるリスポーン、後から作る `SkeletalMesh` の描画)、M0108(入力の拡張。[M0108](../Milestones/M0108-Input-Extensions.md): `[InputActions]` に 1 軸のアナログ値 `Axis`(キー・`NegativeKeys`・トリガー・スティックの片方の軸・不感帯と倍率と反転)、`Button.HeldSeconds`(押した瞬間 0、離した瞬間に押していた時間。`ReleasedBefore` で短押しを判定)、マウスの横だけ・縦だけの `Look`、自動実行の入力にゲームパッドの軸とマウスの移動量。TopDown は UE のとおり短押し=離した瞬間に `MoveTo` と目印、長押し=押している間カーソルへ直接歩く。Vehicle はアナログのアクセル・ブレーキ・ハンドル。描画の契約は変えない。SDK 0.1.19)、M0109(Chaos 相当の車両。[M0109](../Milestones/M0109-Chaos-Vehicle.md): Vehicles プラグインを UE 5.7 の ChaosVehiclesCore の式で書き直した。車体は物理の動的剛体で、車輪ごとの光線の力のサスペンション、トルク曲線と 5 速の自動変速、アッカーマン操舵、タイヤの摩擦(路面の物理マテリアルの摩擦)、空気抵抗、UE の眠り、転倒の判定とリセット。値は `[DataAsset] VehicleSetup`(`SportsCarSetup.cgeasset`)。物理の契約に点の力・トルク・速度・眠りの命令と減衰、箱と三角形メッシュの複数点の接触。描画に物体ごとの発光の倍率 `MeshEmissive`(ブレーキランプ。描画契約は変えない)、`LightVisibility`(ヘッドライト。光の隣の補助のコンポーネント)、カメラの切り替え。調査で、UE ではブレーキランプが光らないこと、角速度の減衰が常に 3 であることが分かった。SDK 0.1.20)。、M0110(Lumen 相当の土台と見た目の不足。[M0110](../Milestones/M0110-Lumen-Foundation-And-Visual-Gaps.md): 材質テクスチャのミップマップ(読み込み時に CPU で作る。決定的、sRGB は線形空間で平均)と異方性 8、ボリュメトリック雲(`VolumetricCloud`。UE の値、上半球のパノラマに Hillaire 系でレイマーチして 16 フレームで一巡)、空の球(`SkySphere`。球の外のジオメトリを空が覆う)、メッシュの距離場(ビルドが glTF ごとに `.glb.cgesdf` を作る)、グローバル距離場(`GlobalDistanceField`。4 段のクリップマップ、8³ のブリックの差分更新、M0111 のトレース用)。描画契約 `stage-v12`(材質のサンプラーが異方性)。SDK 0.1.21)、M0111(Lumen 相当の動的 GI と反射。[M0111](../Milestones/M0111-Lumen-Dynamic-GI-And-Reflections.md): サーフェスキャッシュ(箱の 6 面のカード、太陽・局所光・前のフレームの放射輝度キャッシュ・空・発光で毎フレーム照らす)、画面のプローブの最終収集(画面 → 距離場 → 世界に固定したクリップマップの放射輝度キャッシュ)、反射(画面 → 距離場 → 空、粗さ 0.4 以上はプローブ)。UE の Scalability に合わせた段で、High 以上が Lumen、Medium 以下が DDGI。既定は実機 High・WARP Medium(Epic は環境変数で選ぶ)。PostProcessVolume に UE の Lumen の欄。描画契約 `stage-v13`。SDK 0.1.23)、M0112(テンプレートの仕上げ。[M0112](../Milestones/M0112-Template-Finishing.md): Vehicle のレベルを UE のとおりにした。Landscape の高さ(中央に 2.84 m の丘)を衝突のライン トレースで読んで三角形メッシュにし、基本色を真上から撮影し、Landscape Spline の `SM_Track_10M` 16 個を UE の式で曲げて、衝突のある路面と描くだけの縁石にした。TopDown の経路の最後の点に UE の固定の制動距離。`UE-Template-Behavior.md` の「一部」「未」の行を実装済みか作らないに決めた。4 つのレベルの環境の部品を試験で固定。UE の参照画像(SceneCapture2D)が Lumen の GI を含まないことを見つけ、Lumen を含むビューポートに取り直した。陰の明るさの差の一部(雲の量: UE 0.289、エンジン 0.711)を `VolumetricCloud.Coverage` −0.8 で合わせ、残る差(陰 1.1〜1.3 倍、日向の背の壁 0.8 倍)は文書に残した。`verify-template-package` に FirstPerson を足した。描画契約は変えない。SDK 0.1.24。残りは M0113)

### 5. Lumen 相当の構成と UE との差(M0110〜M0111)
- 静的なライティングのベイクは作らない(ユーザーの決定)。焼くのはジオメトリの前処理(メッシュの距離場)だけで、光は毎フレーム解く。
- M0110(土台と見た目の不足。完了): 材質テクスチャのミップマップと異方性フィルター、ボリュメトリック雲(Hillaire 系。UE の `VolumetricCloud` と既定の雲の材質の値)、空の球(UE の `SM_SkySphere` と `M_SimpleSkyDome`)、メッシュの距離場(ビルド時に glTF ごと)、カメラまわりのクリップマップのグローバル距離場(毎フレームの差分更新)。グローバル距離場は描画には使わず、M0111 がトレースする GPU 資源として公開する。
- M0111(GI と反射。完了): サーフェスキャッシュ、画面のプローブからグローバル距離場と放射輝度キャッシュへの最終収集、反射(画面・距離場・空)。ADR-0069 の DDGI プローブは、Medium 以下(WARP の既定)の代替の段として残した(`RenderScalability`、`CGE_SCALABILITY_GI`)。DXR は任意の後の拡張。
- UE との差(M0110 の時点):
  - ハードウェアのレイトレーシングは無い(ソフトウェアの距離場だけ)。ナナイトの代わりに通常のメッシュ。
  - メッシュの距離場は最長辺 48 ボクセル(UE は大きさに応じて最大 128 程度)。グローバル距離場は既定で 4 段 × 64³、最も細かい段のボクセル 0.5 m(UE の Lumen の最初のクリップマップは 25 m まで)。値は Component で変えられる。
  - 雲はカメラのまわりの上半球のパノラマに描いて画面へ合成する(UE は画素ごと)。雲の形は UE の雲のテクスチャではなくエンジンの手続きのノイズ。雲の影(地面への)と、遠くのジオメトリの手前の雲は無い。
  - 詳細は [M0110](../Milestones/M0110-Lumen-Foundation-And-Visual-Gaps.md)。
- UE との差(M0111 の時点): カードは箱の 6 面だけ(UE はメッシュに合わせた複数のカード)、放射輝度キャッシュは全域の密な格子、半透明・多重の反射・屈折は無い、既定の段は実機でも High(UE のテンプレートは Epic。WARP では Epic で装置の削除が断続的に起き、原因は未解決)。詳細は [M0111](../Milestones/M0111-Lumen-Dynamic-GI-And-Reflections.md)。

### 6. M0112 の決定(Vehicle のレベルとテンプレートの仕上げ)
- Landscape は **静的な三角形メッシュ** で表す(地形のコンポーネントは作らない)。起伏は 1 m 格子(127 × 127 点)の高さで、UE の衝突のライン トレースから読む(Python から高さのデータは読めない)。各格子の対角線は UE と同じ(0.002 cm で一致)。7 m 四方がすべて平らなら 2 枚の三角形にして、5,959 頂点・11,496 三角形にした。基本色は、真上から `SCS_BASE_COLOR` で撮ったもの(層の重みと斜面の色と RVT を、式を読み解かずに写す)をテクスチャにした。
- Landscape Spline の `SplineMeshComponent` 16 個は、UE の `SplineMeshCommon.ush` の式で曲げて 1 つの路面メッシュにまとめる。**縁石は描くだけで衝突を持たない**: エンジンの箱と三角形メッシュの接触は 1 tick に 1 つの面の向きしか使わないので、縁石の斜面と平らな地面にまたがった車体が地面の支えを失い、沈んだ(再現済み。路面と縁石を 1 つのメッシュにしても同じ)。接触が複数の面の向きを扱えるようになるまで(M0113 以降の課題)、縁石は視覚だけにする。
- 物理マテリアルは UE のとおり: 地形 摩擦 1・反発 0.4(Slippery)、路面 摩擦 1・反発 0(NonSlippery)。
- 生成は UE を使う 3 つの読み出し(`probe_vehicle_landscape_heights.py`、`run_export_skeletal_mesh.cmd`、`capture_vehicle_ground_color.cmd`)と、UE を使わない `build_vehicle_ground.py`。生成物はテンプレートに置き、シーン JSON がその後の正本(UE との同期はしない。ADR-0067 と同じ)。
- 経路の最後の点の制動に UE の `FixedPathBrakingDistance`(TopDown は 2 m)を使う(`CharacterMotion.FixedPathBrakingDistance`)。
- `verify-template-package` は FirstPerson も対象にした(ThirdPerson と同じ歩きと跳躍の判定)。

## 結果
- UE の処理を推測ではなく、ノードの接続から読める。実装と UE の差は `UE-Template-Behavior.md` の表の行ごとに、出典のノードと実装・テストを並べて追える。
- 解析は UE の書き出しに依る。UE の版が変わったら、`run_dump_blueprint_logic.cmd` と `logic` を流し直して差分を見る。
