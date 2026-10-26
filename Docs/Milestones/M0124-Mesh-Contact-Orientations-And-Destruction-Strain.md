# M0124 — メッシュ接触の複数の面の向きと縁石の衝突、破壊の内部ひずみと多段クラスター
- 日付: 2026-10-26
- 状態: 完了(2026-10-26。「範囲外と不確実性」に未対応を書いた)
- 関係: ADR-0078(§2 破壊、§6 Vehicle のレベル)、[M0109](M0109-Chaos-Vehicle.md)(箱と三角形メッシュの 4 点の接触)、[M0112](M0112-Template-Finishing.md)(縁石を描くだけにした報告)、[M0113](M0113-Chaos-Destruction.md)(破壊。範囲外の表の 2 行を本書で実装した)

## 目的
M0113 の完了後に残った物理の 2 つの課題。
1. 箱・凸多面体・複合形状と静的な三角形メッシュの接触が、1 tick に 1 つの面の向きしか使わない(M0112: 縁石の斜面と平らな地面にまたがった車体が沈み、縁石を描くだけにした)。
2. 破壊のひずみが tick ごとに捨てられ(UE の Chaos の内部ひずみが無い)、クラスターが 1 段だけ。

## 課題 1: メッシュとの接触の複数の面の向き
### 事実(再現した根本原因)
修正前に失敗する試験(`PhysicsMeshMultiFaceContactTests`、`VehicleLevelTests.ACarDrivenOverOrAlongTheCurbsStaysOnItsWheelsAboveTheGround`)で次の 4 つを観測した。
1. **一番深い角の面の向きしか使わない**: 谷(左右の斜面)に載せた箱は、片方の斜面の角だけで支えられた。
2. **メッシュの稜線(凸の縁)が形の面に入るのを見ない**: 箱の角が地面に入っていると、段の縁(高さ 0.2 m)が箱の底面に入っても接触にならず、箱・凸多面体・複合形状が 8.3〜8.6 cm 沈んだ(斜面と段にまたがる車体の大きさ 1.9 × 0.9 × 4.5 m、1500 kg)。
3. **法線の反転**: 面の表裏を「形の中心の側」で決めるので、小さな斜面の三角形の平面を延ばすと中心が裏に来て、角が「入った」と判定され、形を下へ引く法線 (0, −0.894, ±0.447) が出た。
4. **凸多面体の点の選び方**: 「一番深い点、次に一番離れた点」で 4 点を選ぶので、稜線の 2 端のうち 1 つが遠くの投機的な(深さが負の)角に負けて落ち、1 点で支えて回って 3.5 cm 沈んだ。

Vehicle のレベルに縁石の衝突を足した修正前のコードでは、内側の縁石(半径 37.5 m)から走った車が −98.8 m まで落ち、PlayerStart からの車は 0.22 m まで沈んで向きが 60° 変わって止まった(M0112 の「−47 m まで落ちた」と同じ現象)。

### 決定
- **三角形ごとの分離軸の比較**(`PhysicsNarrowPhase.TryPolytopeMeshContacts`。箱と凸多面体で 1 つの実装): 三角形ごとに、(a) 形の角が三角形の面の下にある深さ(面は形の中心の側。従来どおり投機的な角は 5 cm まで)と、(b) 三角形が形の面から入る深さ(稜線。形の他の面で三角形を切り取り、その面の内側の頂点が点で、法線はその面の向き)を比べ、小さいほうを採る。形の面とほぼ平行(内積 0.95 以上)な三角形は (a) だけ(平らな床の上の結果は M0109 と同じビット)。
- **入った角の確かめ**: 形の面で三角形から離れている(斜めの三角形は 0 以下、平行な三角形は接触の余裕 0.5 mm を超える)ときは、その三角形の入った角を採らない。斜めの三角形では、角から面の法線の向きへ深さぶん進んだ点(三角形の上の点)が形の中にあることも求める(本当に入った角の周りは面の表側にある。裏向きの面なら形の外へ出る)。投機的な角は残す。
- **向きごとのパッチ**: 入った点の一番深いものが 1 つ目の向き、法線が約 18° 以内(内積 0.95)の点がその向きに入る。残りの一番深い点が次の向き。**最大 3 つの向き × 4 点 = 12 点**(`PhysicsNarrowPhase.MaximumContactPatches`・`MaximumContactPoints`)。箱は向きごとに深い 4 点(角の順)、凸多面体は一番深い点と広がり(入った点を先に、残りの枠を投機的な点で)。前の向きの点から 1 mm 以内の点は除く。決定的(角の順、三角形の順、番号の小さいほうが勝つ)。
- **複合形状**: 子ごとの接触(子とメッシュなら向きごとのパッチ)を同じ規則で向きごとにまとめる。**相手が凸の形(箱の床、別の破片)の組は従来どおり 1 つの向き**: 凸の相手にも複数の向きを与えると破壊の見本の破片の積もり方が変わり、爆発が最後の木箱を外した(下の「検証」)。凹んだ塊が凸の物体を 2 面で挟む支えは範囲外。
- **ワールド**: 1 組の接触は複数のパッチを持ち、点ごとに法線を持つ(`CachedPoint.Normal`)。warm start はパッチごと(前の tick の、同じ特徴と約 18° 以内の法線のパッチの点を順に引き継ぐ)。イベントは従来どおり 1 組に 1 つ(力積は全点の和)。`CollectContactManifolds` の点は自分の法線を持つ。

### Vehicle の縁石(決定: 衝突を付ける)
- `Lvl_VehicleBasic.json` の `TrackCurbs` に `StaticMeshCollider`(摩擦 1、反発 0。UE の `SM_Track_10M` は路面と縁石が 1 つのメッシュで NonSlippery)。`tools/UnrealExport/build_vehicle_ground.py` も同じ節点を書く。
- 縁石を越える・縁石の上を走る車は沈まず、傾かない(重心の最低 0.514 m、上向きの最小 0.993)。
- **基準値の変化(意図した変化)**: `verify-thirdperson-character --template Vehicle`(WARP)は前進 62.613 → **62.614 m**、曲がり 27.8 → **25.4°**、追従カメラの高さ 1.534〜1.645 → **1.498〜1.717 m**、HUD の状態 37 → 34、半分のトリガー 23.306 → **23.115 m**、半分のスティック 9.3 → **12.8°**。D3D12 のエラー 0。理由: 右へ切った車が縁石を越える(車輪の光線のサスペンションが 0.2 m の縁石に当たり、向きが少し変わる)。**縁石の衝突だけを外した同じコードでは M0112 の値とビット単位で同じ**(62.613 m・27.8°・23.306 m・9.3°)なので、変化は縁石によるもので接触の変更によるものではない。判定の範囲(前進 20 m 以上、曲がり 5° 以上、カメラ 1〜6 m、HUD 3 状態以上、アナログ 0.9 倍以下)は変えない(余裕が十分)。

## 課題 2: 破壊の内部ひずみと多段クラスター
### 決定: 内部ひずみ(UE の Chaos の内部ひずみに相当)
- 結合ごとに tick をまたぐ内部ひずみ D(N·s)を持つ。切断の判定は **D + その tick のひずみ ≥ 強さ**(等しければ切れる)。
- tick の終わりに、切れていない結合は **D ← (D + その tick の「残る」ひずみ) × 保持率**。保持率 = max(0, 1 − 減衰 × Δt)(速度の減衰と同じ式)。D が強さの 2⁻²⁰(約 100 万分の 1)を下回ったら 0(古い衝撃は終わる)。切れた結合の D は 0。
- **減衰の既定 0.5/s**(`Destructible.StrainDecay`。60 Hz で保持率 0.99167、2 秒で約 37%、約 26 秒で消える)。0 は忘れない、60/s 以上(60 Hz)は M0113 と同じく毎 tick 捨てる。`FractureSettings.StrainRetention` の既定は 0(Runtime.Physics を直接使うコードは M0113 と同じ)。
- **残るひずみ = 衝撃だけ**: 接触が始まった tick の力積(`ContactBegin` のイベントの組)、ゲームの `ApplyImpulse`、`StrainField`、キャラクターの衝突(`DestructionImpact`)のうちそのキャラクターがその物体に触れ始めた tick のもの。続いている接触(置かれた物体の荷重、キャラクターが押し続ける間)はその tick だけのひずみで、溜まらない(自重や寄りかかりで少しずつ壊れない)。事実: 押し続ける毎 tick を溜めた途中の版では、見本で歩くだけで 4 つの木箱がすべて壊れ、爆発の前に壊すものが無くなった。
- 結果: 小さな衝撃の繰り返しで壊れ、1 回では壊れず、間が空けば減衰で溜まったひずみが消える。

### 決定: 多段クラスター(UE の Geometry Collection のレベル)
- 調理の段の数 `ClusterLevels`(1〜4、既定 1 = M0113)。段 0 は従来の `ClusterCount` のクラスター、段ごとに各クラスターをその破片の √n(最も近い整数。2 個以下はそのまま)個の連結なクラスターへ分ける(同じ幅優先と併合)。各段は最小の破片の順に番号。
- 結合の強さ = 面積 × `BondStrength` × `ClusterStrengthScale`^(両方の破片を同じクラスターに持つ段の数)。外側(段 0 のクラスターの間)が一番弱く先に切れ、次に段 1 のクラスターの間、最後に一番深いクラスターの中(UE の段ごとのしきい値)。1 段の資産はクラスターの中が ×2 で M0113 と同じ。
- 実行時の塊は従来どおり、切れた後のつながった破片の組(強さの順でクラスターごとに分かれる)。

### 決定: 形式と記録
- `.cgefracture` **版 3**(`CGEFRAC3`): 版 2 の破片数の後に段の数(int32、2〜4)、破片ごとにクラスターを段の数だけ(上から)。**1 段の資産は版 2 で書く**(M0113 と同じバイト列。見本の 1 段の木箱の調理は変わらない)。版 1・2 も読む。入れ子でない・連結でない・正準でない番号は拒否。`PrefractureArtifactCooker` の目録の schema は `prefracture-artifact-<版>`。
- `.cgephyscache` **スキーマ 4**: 各フレームの結合の切断の後に、衝撃で内部ひずみが増えた結合(物体の stable ID、結合、次の tick へ持ち越す内部ひずみ。昇順で一意)。スキーマ 2・3 も読む(内部ひずみなし)。再生(`FractureState.Replay`)は記録の結合を切り、すべての結合の D を減衰させ、記録の値を置く。リアルタイムと同じ D になるので、再生の後に実時間へ引き継いでも同じように切れる。
- Physics.Cli の `preview-prefracture` は最後に省略可の `[clusterLevels]`。点検の文書は段の数、破片ごとの各段のクラスター、結合ごとの共有段数(`Level`)を持つ。
- VSIX のプレビュー(`PrefractureDebugService`)は `ClusterLevels` を読んで渡し(1〜4 以外は理由を表示)、重ね描きは結合を共有段数で色分けする(段 0 = M0113 の桃色、1 = 紫、2 = 青、3 以上 = 水色)。

### 見本と verify-destruction(決定)
- 見本の壁 `ShotWall` は `ClusterLevels` 2(球が当たると大きな板の塊に分かれる)。床の木箱は既定の内部ひずみ(0.5/s)。
- 事実: 内部ひずみの蓄積で、歩く間に木箱が 4 つとも壊れる(M0113 は 3 つ。木箱の破片やキャラクターの衝突の始まりが溜まる)。床の木箱だけ減衰 60/s にした一時の版では M0113 と同じ流れ(2/5/5/6、最低 0.059、最速 0.014)に戻ったので、変化は蓄積によるもの(意図した効果)。押された速い破片(7.2 m/s)が 40 フレーム目に 1 フレームだけ床へ 1 mm 食い込んだ(重心 −0.001 m、次のフレームで 0.031 m。破片と静的な箱の間に連続衝突判定は無い)。
- 判定の更新: 「爆発が最後の木箱を壊す」→「終わりに 6 個すべて壊れ、爆発が何も壊さないときは破片を押す(爆発の 3 フレーム後の最速が直前より速い)」。「重心が床より下に行かない」→「どのフレームも −0.05 m より下に行かず(抜けた破片は落ち続ける)、終わりは 0 以上」。新しく「段の数 2 以上」「切れていない結合が内部ひずみを持ったことがある」。
- 画像(WARP、`.artifacts/feature-evidence/destruction.sample/warp/final.png`): 壁は厚さ 0.3 m の大きな板の塊で床に寝て、木箱の破片は積もって止まっている。床を抜けた破片は無い。

## 実装の場所
- L5 `Runtime.Physics`: `PhysicsNarrowPhase.cs`(`TryCreateContacts`、`TryPolytopeMeshContacts`、`TryCompoundContacts`、`SpreadPoints`)、`DeterministicPhysicsWorld.cs`(パッチ、点の法線、`BondStrains`・`ReportBondStrain`)、`FractureSolver.cs`(`StrainRetention`・`RetentionFor`・`InternalStrainOf`・`Replay`・段の強さ)、`PhysicsCooking.cs`(`SubClusters`・`ClusterLevelCount`・`ClusterOf(破片, 段)`・`SharedClusterLevels`)、`PrefractureCooking.cs`(`ClusterLevels`、版 3)、`PhysicsCache.cs`(スキーマ 4、`PhysicsBondStrain`)、`PhysicsWorldIntegration.cs`。
- L5 プラグイン `plugins/CSharpGameEngine.Destruction`: `Destructible.ClusterLevels`・`StrainDecay`、`Destruction`(衝撃の区別、記録と再生)、Cook の `ClusterLevels`(DST003 の範囲)。`Destruction.cgeplugin` の要求 ≥0.1.26。
- L7 `Physics.Cli`(`preview-prefracture`)、L8 VSIX(`PrefractureDebugService`・`PhysicsDebugOverlay`)。
- L9: Vehicle テンプレートのレベル(縁石の衝突)、見本(壁を 2 段、毎フレームの行に `strained`・`levels`)、`verify-destruction` の判定に段の数と内部ひずみ。
- SDK のパッケージの内容が変わるので版を上げる(作業中は 0.1.26 にしたが、並行する作業のコミットで HEAD が 0.1.37 になったので、コミットの時に次の版へ)。`Destruction.cgeplugin` の要求は ≥0.1.26。

## 検証
| コマンド | 結果 |
|---|---|
| `dotnet test tests/CSharpGameEngine.Windows.Tests --filter PhysicsMeshMultiFaceContactTests` | 修正前 10 失敗・3 合格(面の境の 3 件は修正前から合格の回帰の守り)、修正後 13 合格 |
| `dotnet test … --filter VehicleLevelTests` | 24 合格(縁石を含むレベル。新しい縁石の走行 3 件は修正前のコードで 2 件失敗: −98.776 m・0.220 m) |
| 物理の時間(Release、温まった後の 1 tick) | 0.25 m の三角形の床の上の箱 0.248 → 0.281 ms(+13%)、縁石の上の複合形状 0.196 → 0.233 ms(+19%) |
| `verify-thirdperson-character --template Vehicle --adapter warp` | 合格(値は上)。縁石の衝突なしで 62.613 m・27.8°(M0112 と同じ) |
| `verify-destruction --adapter warp`(複合形状の多向きを凸の相手にも与えた途中の版) | 不合格: `broken=2/5/5/5 bonds=179 loose=52`、爆発が最後の木箱を外した → 相手がメッシュのときだけにした |
| 同(相手がメッシュのときだけ、課題 2 の前) | 合格、M0113 と同じ: `broken=2/5/5/6 bonds=203 loose=64 sleeping=18 lowest=0.059 fastest=0.014`、`bytes=636670` |
| `verify-destruction --adapter warp`(課題 2 の後、判定の更新前) | 不合格: `broken=2/6/6/6`(爆発の前にすべて壊れた)、最低の重心 −0.001 m(1 フレーム) |
| 同(床の木箱だけ減衰 60/s の一時の版) | 合格: `broken=2/5/5/6 bonds=202 loose=63 sleeping=17 lowest=0.059 fastest=0.014` |
| 同(最終) | 合格: `destructibles=6 broken=2/6/6/6 bonds=204 loose=65 sleeping=25 lowest=-0.001/0.079 fastest=0.049 pushed=0.125->1.075 levels=2 mostStrained=51 d3d12Errors=0`、`bytes=637630 deterministic=True playbackMatches=True`(+960 バイトはスキーマ 4 の 1 フレーム 4 バイト × 240)。この実行の出力には `debugLayerEnabled=False` が出た(並行する別作業の `RuntimeAutomation.cs` の未コミットの変更で出るようになった表示と推測。D3D12 のデバッグ層が有効でなかった可能性があり、エラー 0 の意味は弱い) |
| `dotnet test tests/CSharpGameEngine.FeatureRunner.Tests` | 267 合格 |
| `dotnet test` Core・Cook・CodeAnalysis | 156・29・338 合格 |
| `dotnet test tests/CSharpGameEngine.Windows.Tests --filter Physics|Destruction|Fracture|Vehicle|Bake|Fragment|Character` | 250 合格 |
| `dotnet test tests/CSharpGameEngine.Destruction.Tests` | 57 合格(新しく `ClusterLevelCookTests` 10) |
| `dotnet test … --filter PhysicsFractureInternalStrainTests|PhysicsCacheBondStrainTests|PhysicsCacheBondBreakTests|FractureSolverTests` | 29 合格 |
| `dotnet test … --filter DestructionTests` | 16 合格(新しく 3: 宙の木箱は近い 3 回の打撃で切れ 1 回・2 秒おき・減衰 60/s では切れない、床に置いた木箱は自重で 20 秒立つ、記録の再生の後に実時間で同じ tick に切れる) |
| `dotnet test tests/CSharpGameEngine.VSExtension.Tests --filter Prefracture|PhysicsDebugOverlay` | 21 合格(net472) |
| `dotnet test tests/CSharpGameEngine.FeatureRunner.Tests --filter DestructionSampleVerification` | 14 合格 |

## 範囲外と不確実性
| 項目 | 状態 | 理由・次 |
|---|---|---|
| 辺と辺の接触(形の辺とメッシュの辺が交わるだけ) | 従来どおり | 角も稜線の頂点も入らない触れ方は GJK/EPA の 1 点 |
| 複合形状と凸の相手の複数の向き | 範囲外 | 上の決定。凹んだ塊が凸の物体を挟む支えは 1 つの向き |
| 再生から実時間への引き継ぎの接触 | 既知の制約 | キャッシュは接触の記憶(warm start、始まり/続き)を持たない。引き継いだ tick に置かれた物体の接触が「始まり」になり、その荷重が衝撃として内部ひずみに入る。内部ひずみの復元そのものは試験で一致を確かめた(宙の木箱) |
| 「接触の始まり = 衝撃」の判定 | 推測を含む | 静止の揺れで接触が切れてまた始まると、その荷重が衝撃として溜まる(眠りで多くは止まる)。UE は衝突の力積から損傷を作る方式で、同じではない |
| 弱い結合 | M0113 からの挙動 | 面積の小さい結合(数 mm²)は置かれた荷重の最初の接触で切れる(`DropCrate` を 0.5 mm から置くと 5 本。木箱は割れない) |
| UE の Field の任意の組み合わせ、実行時のブール演算 | 範囲外 | M0113 と同じ |
