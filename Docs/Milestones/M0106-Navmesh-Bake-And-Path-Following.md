# M0106 — ナビメッシュのベイクと経路追従(最初の実際の Cook)
- 日付: 2026-10-23
- 状態: 完了(2026-10-23)

## 目的
ADR-0078 の「TopDown のナビメッシュの経路」(`SimpleMoveToLocation`)を、ADR-0079 のプラグインとして作る。M0103 が契約だけ作った Cook(`IAssetCooker`)の最初の実際の利用で、M0103 が先送りした「ビルド中の自動実行」と「VSIX のボタン」を足す。計画の段階 1 の 3。

## 事実(再現して確かめたこと)
- UE5.8 TopDown の値(`tools/UnrealExport/probe_nav.py` を `run_probe_nav.cmd TopDown` で実行、`DefaultEngine.ini`、`RecastNavMesh.h`):
  - `NavMeshBoundsVolume`: 位置 (0, 0, 0)、範囲 (2250, 2250, 375) cm = ±22.5 m × ±22.5 m × ±3.75 m。
  - レベルの `RecastNavMesh` アクター: AgentRadius 35、AgentHeight 144、AgentMaxSlope 44、TileSizeUU 1000、MergeRegionSize 400、MaxSimplificationError 1.3、WATERSHED。
  - `DefaultEngine.ini`: CellSize 19、CellHeight 10、AgentRadius 34(レベルのアクターは 35。アクターの値を使う)、AgentHeight 144、`RuntimeGeneration=Dynamic`。
  - AgentMaxStepHeight 35 は `RecastNavMesh.h` の `FNavMeshResolutionParam` の既定(ini の `NavMeshResolutionParams` も 35)。CellSize・CellHeight・AgentMaxStepHeight は Python のプロパティとしては読めなかった(エラー)ので、設定ファイルとヘッダーから取った。
- 修正前の TopDown(WARP): クリック移動は直線。中央の台(高さ 2 m、半径 3.5 m)の上から、ふちを越えて 2 m 飛び降りて目的地へ行く(10.845 m、カメラの高さが 1 フレームで最大 0.67 m 落ちる)。
- レベルの形(`Lvl_TopDown.json` の 54 個のメッシュ): 床、台、台の四方のスロープ(4 × 2 × 4 m、約 27°)とその両脇の壁、四隅の建物、外周の壁。すべて `StaticMesh` + `StaticMeshCollider`(`Collider` や `RigidBody` は無い)。glb の三角形は、どれも外向きの巻き順(`Cross(B-A, C-A)` が外を向く。床は厚さ 0.5 m の閉じた板)。
- 物理の接触の面は両面(法線を、当たった点の側へ向ける)なので、歩ける面の判定も向きによらない。
- 実装中に、単体テストが見つけた不具合(すべて根本原因を直した):
  1. funnel の左右の取り違え: Detour の面積の符号は「R が右、F が上(数学の向き)」。逆にすると、経路が角から角へ振れて(`(-1.6, y)` と `(1.44, y')` を往復)、一直線のスロープが折れ線になった。
  2. 大きな長方形どうしでは、境の真ん中を通る費用で A* が遠い側の壁の端を回った(箱を回る経路が、近い側ではなく遠い側を選んだ)。境の上の、「今の位置から目的地への最短の通り方に一番近い点」(境の線について折り返した目的地への直線との交点)で測るようにした。
  3. 閉じた形(箱、壁)が床に載っていると、形の内側(高さ 2 m の空洞)に歩ける面が島として残った。底面(下向き)が床の上面(上向き)と同じ高さで重なる span は歩けないことにした。厚さ 0.1 m の台(TopDown の中央)は底面が上面より下なので歩ける。
  4. 目的地が壁の中・ナビメッシュの外でも、経路は目的地を床へ寄せた点で終わる。最後の区間を本物の目的地へ向けると、壁を押し続けた。最後の点を、止まる点(`WaypointIsEnd`)にした。
- 実走で分かったこと: クリックを押している間、カーソルの下の地面は毎フレーム選び直される(カメラがキャラクターに追従するので、キャラクターが動くと目的地が動く)。同じ入力でも、ナビメッシュの有無で着く場所が違う。検証は、それぞれの走りの値で行う。

## 決定
### 1. 形(ADR-0079 のプラグインと Cook)
- `plugins/CSharpGameEngine.Navigation`: `Navigation.cgeplugin`、`.plugin.props`、`.plugin.items`、`Runtime/`(Component・System・サービス。ゲームと一緒にコンパイル)、`Cook/`(`IAssetCooker` の DLL)。ゲームは `<CgePlugins>Navigation</CgePlugins>` で有効にする(TopDown と UserEcsProject)。
- Runtime の `NavMesh`(形式・読み込み・経路探索)は、Cook にも同じソースをコンパイルする(書く側と読む側が同じコード)。Library の DLL にはしなかった(SDK パッケージで Library の DLL を参照する道が無いため。ソースの共有で足りる)。

### 2. 領域と値(`NavMeshBounds`)
- UE の NavMeshBoundsVolume に当たる Component。シーンのノードの Transform(位置と拡縮。回転は、領域を含む軸に平行の箱にして使う)で置く。値は `Id`、`HalfExtents`、`AgentRadius`(0.35)、`AgentHeight`(1.44)、`AgentMaxClimb`(0.35)、`AgentMaxSlopeDegrees`(44。シーン JSON では回転数、キーは `AgentMaxSlope`)、`CellSize`(0.19)、`CellHeight`(0.1)。既定値は UE5.8 TopDown の値(`NavigationDefaults`)。
- 同じ `Id` の領域は 1 つのナビメッシュにまとまる(領域の和)。値が違えば NAV010。
- 領域は `Lvl_TopDown.json` の中ではなく、別のシーン `TopDownNavigation.json`(`startupOrder` 3)に置いた。`Lvl_TopDown.json` は UE から書き出した生成物で、取り込み直すと書き換えが消えるため。Cook は、領域のあるシーンと、プロジェクトの起動シーン(`startupOrder` を持つもの)すべての衝突形状を集める(UE のナビメッシュが、読み込まれている全レベルの形で作られるのと同じ。実行中に出すシーンは含めない)。

### 3. Cook(`NavMeshCooker`、`IAssetCooker`)
- 入力: 領域のあるシーンの JSON。出力: `Assets/Navigation/<Id>.cgenav`。シーンの読み方は物理と同じ元から: `StaticMesh` + `StaticMeshCollider` の三角形(glTF のノードの変換を含め、シーンの階層の世界の変換で置く)、`RigidBody` の無い `Collider` の箱。`RigidBody` を持つ物とトリガーは含めない。球・カプセル・凸形状は、警告 NAV008 を出して読まない。負の拡縮は、巻き順を戻す。
- 計算は `LogicalScalar` と整数だけ(`double` を使わない)。同じ入力から同じバイト列(`--verify` が通る)。
- 手順(Recast の簡約版。`NavMeshBuilder`):
  1. ボクセル化: 三角形を、`dividePoly` で格子の行と列に切り、セルごとの高さの区間(span)にする。傾きが歩ける三角形(向きによらない)の上面を「歩ける」にする。span の併合は Recast の `rcAddSpan` と同じ(登れる段差以内なら歩ける印を引き継ぐ)。
  2. 歩ける面の絞り込み: 閉じた形の底面が重なる床を外す(上の事実 3)、低い出っ張りを歩ける面に、崖のふち(登れない段差の落差)と険しい坂を外す、頭がつかえる所(背丈未満)を外す。
  3. 開いた空間: 歩ける span ごとに、上の空きと、4 方向の隣への接続(段差が登れ、頭が通る)。
  4. 半径ぶん縁から離す: Recast の `rcErodeWalkableArea`(縁からの 2/3 の距離変換)。縁は、4 方向のどれかに接続の無い span。
  5. 多角形: 同じ高さで続くセルを、+R へ伸ばし、次に +F へ伸ばして長方形にまとめる(平らで、凸)。坂は段の高さごとの細い長方形になる。
  6. 接続(portal): 長方形の +R の辺と +F の辺を歩き、接続のあるセルの並びのうち、同じ隣の多角形に続く区間を portal にする。
- 領域が大きすぎる(格子が 4096 セルを超える、または 1600 万セルを超える)ときは、NAV011 で断る。
- Recast との差: 輪郭の単純化、斜めの辺、領域の分割(watershed)、タイルは無い。多角形は格子に沿った平らな長方形で、経路の角は、縁の格子点にそろう。UE のナビメッシュと同じ形にはならない(この差は、目的の「歩ける領域が正しく出る」の範囲。TopDown のレベルで確かめた)。

### 4. ファイル形式(`.cgenav`、版 1)
- 印 `CGENAV01`、設定(半径・背丈・段差・坂・セル・セルの高さ、各 16 バイトの `LogicalScalar`)、原点(3 つ)、格子の幅と奥行き、多角形(5 つの `int32`: MinX、MinZ、MaxX、MaxZ、Y)、portal(6 つの `int32`: 多角形 A・B、X0、Z0、X1、Z1)。リトルエンディアン。
- 読み込みは、壊れたファイルを例外ではなく理由つきで断る: 印・版の違い、あらゆる位置での切り詰め、後ろの余り、件数の異常(領域を確保する前に確かめる)、格子の外の長方形、存在しない多角形への portal、2 つの長方形が接する線の上に無い portal、不正な設定。

### 5. 経路探索(`NavMesh.TryFindRoute`)
- 出発点・目的地は足の位置。出発点の多角形は、点を含む(または水平に歩く者の半径の 2 倍 + 1 セル以内にある)多角形のうち、床の高さが近いもの(背丈の範囲)。無ければ false。
- A*: 多角形の隣接(portal)の上で、費用は整数(1 セルの 1/16 を単位とし、整数の平方根で距離を測る)。同じ費用は多角形の番号の小さい方が先(決定的)。
- 目的地が多角形に届かない、または着けない: 行ける多角形のうち、目的地に一番近い(高さの差を加えた距離)点までの部分経路(`IsPartial`)。
- funnel(Mikko Mononen の simple stupid funnel)で、portal を引っ張って角だけの折れ線にする。面積の符号は整数で厳密。
- 整数だけなので、同じ入力から同じ経路になる(どのマシンでも)。

### 6. 実行時
- `Navigator`(`[EngineService]`): `Assets/Navigation/<Id>.cgenav` を読み(`ContentRoot` から)、経路を探す。`CGE_NAVIGATION_DISABLED=1` のとき読まない(比較の検証のため)。
- `NavigationLoad`: `NavMeshBounds` を持つ Entity が現れたら、その `Id` を読む(1 度だけ。失敗は標準エラーに `CGE-NAVIGATION …` を 1 回出して、`NavMeshBoundsRuntime.Problem` に残す。そのとき歩く者は直線)。
- `NavAgent`(Component)と `NavAgentState`(`IRuntimeComponent<NavAgent>`。スナップショットに入らず、経路とそれを探したときの目的地を持つ)。`NavigationSteering`(PhysicsSimulation。`CharacterMotionUpdate` の前)が、`MoveIntent.Destination` への経路を探し(目的地が 0.25 m 以上動いたとき、経路の線から 1.5 m 以上離れたときに探し直す)、次の経由点を `MoveIntent.Waypoint` へ置く。
- `MoveIntent` に `Waypoint`・`HasWaypoint`・`WaypointIsEnd` を足した。`Steer` は、経由点があればそこへ向ける。通過点(`WaypointIsEnd` が false)は最高速度のまま、半径 0.3 m(`NavAgent.WaypointRadius`)に入ったらプラグインが次へ進める。最後の点は、目的地と同じ減速で止まる。経由点が無ければ、`Steer` は従来どおり。
- 仕様として決めたこと:
  - ナビメッシュが読まれていない、または出発点がナビメッシュの外: 従来どおり直線で歩く(`MoveTo` の挙動を変えない)。
  - 目的地に着けない: 行ける中で一番近い点まで歩いて止まる(UE の `SimpleMoveToLocation` は部分経路を許す)。止まるとき `HasDestination` は false に戻る。
  - 目的地がナビメッシュの外(壁の中など): 近くの多角形の点へ寄せて、そこで止まる。壁を押し続けない。

### 7. ビルドと VSIX
- ビルド(`Navigation.plugin.items`): `Scenes/**/*.json` のうち `CSharpGameEngine.Navigation.NavMeshBounds` を含むシーンごとに、`dotnet CSharpGameEngine.Cook.Cli.dll cook <モジュール> <シーン> <obj/…/cge-navigation> --project <プロジェクト>` を動かす(ターゲット `CgeCookNavigation`。`AssignTargetPaths` の前)。出力は `Assets/Navigation/` へ複製(`None` の `CopyToOutputDirectory`)。シーン・アセット・ツールが新しいときだけ(`Inputs`/`Outputs` とスタンプ)。リポジトリのビルドは Cook CLI とモジュールを自分でビルドする。デザイン時のビルドでは動かさない。
  - ターゲットの `Condition` は `DependsOnTargets` より先に評価されるので、見つけたシーンへの条件はタスクに付けた。
  - 失敗は、Cook の `scene.json: error NAVnnn: 内容` の行が、そのままビルドのエラーになる。
- Cook の CLI(`CSharpGameEngine.Cook.Cli`): 失敗した処理のメッセージを 1 行ずつ出す(`CookFailedException`)。成功した処理のメッセージ(警告、統計)は標準エラーへ。
- SDK パッケージ: Cook モジュール(と依存)を `plugins/CSharpGameEngine.Navigation/Cook/lib/` へ入れる。版を 0.1.17 に上げた(パッケージの中身を変えたため)。
- VSIX: Scene Designer の物理のベイクの下に「ナビメッシュ」の節(説明、ボタン、状態、診断)。選択したシーンの保存のあと、そのプロジェクトで `dotnet msbuild -t:CgeCookNavigation -getItem:CgeNavigationOutput` を動かし、出力と診断を出す(`NavigationCookService`)。エンジンのソースの場所に頼らないので、パッケージから作ったプロジェクトでも使える。VS の実機での操作は、検証していない(単体テストの範囲)。
- 範囲外: 既存の `Physics.Cli bake` を Cook の上に載せ直すこと(ベイクの形が違う。ゲームのアセンブリを読み、世界を回す)。

## 作ったもの
| 層 | 中身 |
|---|---|
| L5 プラグイン | `plugins/CSharpGameEngine.Navigation/Runtime`: `NavMesh`(形式、読み込み、検査)、`NavMesh.Query`(A*、funnel、位置の探索)、`NavigationComponents`(`NavMeshBounds`・`NavMeshBoundsRuntime`・`NavAgent`・`NavAgentState`)、`Navigator`、`NavigationSystems`(`NavigationLoad`・`NavigationSteering`)、`NavigationDefaults` |
| L5 コア | `MoveIntent` の `Waypoint`・`HasWaypoint`・`WaypointIsEnd` と `Steer` |
| L7 Cook | `plugins/CSharpGameEngine.Navigation/Cook`: `NavMeshCooker`、`NavMeshBuilder`、`SceneGeometry`。`tools/CSharpGameEngine.Cook.Cli` のメッセージの出し方 |
| L7 ビルド | `Navigation.plugin.items`(ターゲット)、`packaging/CSharpGameEngine.Sdk` |
| L8 | `NavigationCookService`、Scene Designer のボタン、`VsixStrings*.resx` |
| L9 | TopDown の `TopDownNavigation.json`、`NavAgent`、csproj の `CgePlugins`、VS テンプレートの csproj |
| 検証 | `ClickToMoveVerification`(2 回の実走)、`verify-template-package --template TopDown` |

## 検証
- 単体テスト:
  - `CSharpGameEngine.Navigation.Tests`(新規 57): ファイル形式(往復、決定的なバイト、あらゆる切り詰めの拒否、巨大な件数、格子の外・存在しない多角形・境の上に無い portal・不正な設定の拒否)、ビルダー(空のシーン、歩ける面が無い、平らな床は 1 つの長方形で縁から半径ぶん内側、障害物からの距離、隙間が狭い・広い、登れる段差と登れない段差、坂、低い天井、島、閉じた形の内側、薄い板、向きの逆の床、決定性、大きすぎる領域)、経路(同じ多角形、左右どちらの回り込みも近い側、角が障害物から半径の位置、着けない目的地、遠い出発点、壁に触れた出発点、同じ問い合わせは同じ経路、坂の高さ)、シーンの読み取り(親子の変換、箱の外向きの巻き順と負の拡縮、glb の配置、動く物・トリガー・読めない形の除外、起動シーンだけを足す、領域の値と角度のキー、壊れた入力の符号つきの拒否、無い・壊れた glb)、Cook(`Id` ごとのファイル、領域の和、値の不一致、領域無し、空の領域の警告、領域が大きすぎる、CLI を別のプロセスで動かして `--verify` が通る、失敗が `ファイル: error NAV009: …` の行で終了コード 1)、TopDown のレベル(UE の値で焼く・決定的、台からスロープを下りる、角の建物へスロープで登る、遠い目的地は壁を回る)。
  - `CSharpGameEngine.Windows.Tests`: `NavigationSteeringTests`(新規 5。決定的な物理の壁を、本物の `CharacterMotor` で回り込んで着く / ナビメッシュが無いと同じキャラクターは壁に止まり続ける / 壁の中の目的地は一番近い点で止まる / 目的地が動いたら探し直す・終わったら経路を残さない / ナビメッシュの外からは直線)、`CharacterMotorTests`(+4。経由点)。
  - `CSharpGameEngine.VSExtension.Tests`: `NavigationCookServiceTests`(新規 8)、`GameProjectGeneratorTests`(TopDown の `CgePlugins`)。
  - `CSharpGameEngine.FeatureRunner.Tests`: `ClickToMoveVerificationTests`(+6。ナビメッシュで歩いたレポートの判定)。
  - `CSharpGameEngine.Cook.Tests`: `CookerHostTests`(+1。成功した処理のメッセージを呼び出し側へ渡す。失敗の検査を、型の階層を許す `ThrowsAny` と `CookFailedException` のメッセージにした)。
- 既存の実走の値(WARP)は変わらない: ThirdPerson 移動 15.700 m・上昇 1.228 m、FirstPerson 移動 16.000 m・上昇 0.863 m、Vehicle 移動 48.281 m・曲がり 38.3°・HUD 28 状態(リポジトリのビルドと、SDK パッケージから作った ThirdPerson・Vehicle の両方)。`verify-plugin-package`(Weather 2664.0、Lightning 17760.0)も同じ。TopDown も SDK パッケージから作ったもの(`verify-template-package --template TopDown`。ビルドがパッケージの Cook モジュールでナビメッシュを焼く)で、下の 2 回の実走が同じ値になった。
- 実走(WARP、`verify-thirdperson-character --template TopDown`。同じ入力を 2 回):
  - ナビメッシュを切る(`CGE_NAVIGATION_DISABLED=1`): 移動 10.845 m、最後の 10 フレームの動き 0.000 m、足音 7 回、描画物の数 56 → 57 → 56、D3D12 のエラー 0(M0105 と同じ値)。
  - ナビメッシュを使う: 出発点から (7.06, 8.06) へ着いて止まる(目的地の誤差 0.00 m)。経路の長さ 14.87 m(直線 10.72 m の 1.39 倍)、壁の端 (2.2, 9.0) まで最も近づいた距離 0.30 m、1 フレームでカメラの高さが落ちた最大 0.40 m(スロープを下る。直線では 0.67 m の落下)、足音 12 回、D3D12 のエラー 0。画像: `.artifacts/m0106/nav_frame_29.png`(スロープの上の端)、`nav_frame_36.png`(壁の端を回って床の上)、`.artifacts/feature-evidence/rigid.character-controller/TopDown/warp/navigation/final.png`。
- 焼く時間: TopDown のレベル(54 個のメッシュ、237 × 237 セル、541 個の多角形、747 個の portal、28,916 バイト)で、`--verify`(2 回)を入れて 2.7 秒。

## 実装中に見つけた事実
- 面の向き: glb の三角形は外向きの巻き順で、物理の床は両面。歩ける面の判定は向きによらず、閉じた形の内側の床を見分けるときだけ、巻き順を使う。向きが逆の 1 枚の床も歩ける(テスト)。
- 高さは、面を含むボクセルの上端に丸められる(段の高さ 0.1 m まで)ので、ナビメッシュの床は実際の面より最大 1 段(0.1 m)高い。
- 登れる段差の判定も段の単位: 0.35 m は 3 段で、0.4 m の段差は 4 段になり登れない(ボクセルの丸めで、境の値はずれうる)。
- 閉じた形の内側の島(底面が床の上面と重ならない、埋め込まれた形)は残る。行き着けないが、`Covered` のような単純な判定は島を数える。
- MSBuild の `-getItem` を、ターゲットを動かすとき(`-t:`)と組み合わせると、ビルドのあとの項目の値が JSON で出る(VSIX が使う)。

## 範囲外(未実装)と不確実性
- Recast の輪郭の単純化・斜めの辺・領域の分割・タイル、動的な再生成(`RuntimeGeneration=Dynamic`)、`NavModifierVolume` と領域の種類(費用)、複数の歩く者の大きさ(Supported Agents)、ジャンプや落下のリンク。
- 球・カプセル・凸形状の `Collider` は読まない(警告)。
- `.cgenav` に、元の入力のハッシュは入れていない(古さはビルドの `Inputs`/`Outputs` で判断する)。実行時に、元のシーンとの不一致は検出しない。
- 歩く者の半径(0.35 m)は、TopDown のキャラクターのカプセル(0.42 m)より小さい(UE も同じ)。縁を回るとき、カプセルが壁に触れて滑ることがある。TopDown の実走では詰まらなかった。
- 実機の GPU と VS の実機(Scene Designer のボタン)は、検証していない。
- glb のノードの変換は `System.Numerics`(`float`)で行うので、FMA の有無が違う CPU では、最後の 1 ビットが違いうる(物理の静的メッシュの取り込みと同じ)。`--verify` は同じマシンの 2 回を比べる。
- 大きな長方形のナビメッシュでは、A* が選ぶ多角形の列が、最短の経路の列でないことがある(境の上の点を、1 つの多角形につき 1 つしか持たないため)。funnel が角を引っ張るので経路は有効だが、最短とは限らない。

## 完了の判定
- 依頼への答え: Navigation プラグインと最初の実際の Cook(シーンの静的な衝突形状 → 歩ける面 → 長方形の多角形 → 隣接グラフ → `.cgenav`)、A* と funnel の経路追従、ビルド中の自動実行、VSIX のボタン、TopDown の実走(台のスロープを下り、壁の端を回って着く)を、決定性・境界・壊れたファイルのテストと、WARP の実走で確かめた。
- 意図と観測の一致: MatchesWithDocumentedExtension(長方形の多角形。Recast の輪郭と斜めの辺は無い)。
