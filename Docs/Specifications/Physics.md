# Physics Runtime仕様

## 実行モデル

Physicsは60 Hz整数tickの`PhysicsSimulation` phaseで実行する。FrameInput、0..N FixedSimulation、Physics同期/solve/writeback、PostPhysics、Global Spatial同期、Presentationの順を正本とする。壁時計をsolverへ渡さない。

## 作者API

`Collider`のみはStatic、`Collider + RigidBody(Dynamic)`はPhysics所有姿勢、KinematicはCommand targetで移動する。Dynamicの直接Transform編集は禁止しTeleportを使う。Dynamic Triangle MeshとDynamicの親・非One Scaleは拒否する。

### 命令(`PhysicsCommands`)と減衰(ADR-0078/M0109)
命令は対象のtickを持ち、そのtickの`Step`の最初(重力と積分の前)に、(tick、送り手のstable ID、番号)の順で適用する。動的でない物体への力・速度・眠りの命令は何もしない。

| 命令 | 効果 |
|---|---|
| `Force(力)` | 線速度 += 力 / 質量 × Δt(重心) |
| `Impulse(力積)` | 線速度 += 力積 / 質量 |
| `ForceAtPoint(力, ワールドの点)` | `Force`と同じ線速度の変化に加えて、角速度 += I⁻¹((点 − 重心) × 力) × Δt。I は形の慣性(箱は m(b² + c²)/3 など)をワールドへ回したもの |
| `Torque(トルク)` | 角速度 += I⁻¹ トルク × Δt |
| `SetVelocity(線速度, 角速度)` | 両方を置き換えて起こす(UEの`SetPhysicsLinearVelocity`・`SetPhysicsAngularVelocityInDegrees`) |
| `Sleep()` | 速度を0にしてすぐ眠らせる(同じtickの眠りの更新が`Sleep`イベントを出す)。後の力・力積・テレポート・速度の命令で起きる |
| `Teleport(位置, 向き)` | 動的な物体の姿勢を置き換えて起こす(速度は変えない) |
| `SetKinematicTarget(位置, 向き)` | キネマティックな物体の次の姿勢 |

- 点の力の命令の`Point`は`PhysicsCommand.Point`(既定値つきの末尾の引数)で、`SetVelocity`では角速度に使う。
- `RigidBody.LinearDamping`・`AngularDamping`(1/s。既定0)は、毎tickの重力の後に速度へ max(0, 1 − 減衰 × Δt) を掛ける(UE・Chaosの減衰)。0なら速度はそのまま(以前と同じ値)。Systemが実行中に書き換えてよく、次の同期で剛体に入る。
- `DeterministicPhysicsWorld.TryGetMaterial(Entity)`は物体の`PhysicsMaterial`を返す(車両のタイヤが、光線が当たった面の摩擦を読む。UEのヒットの物理マテリアル)。`StaticMeshCollider.Material`(既定はこれまでと同じ摩擦0.5・反発0)が静的メッシュの物体の材質になる。

## 数値と決定性

authoritativeなposition、rotation、scale、velocity、mass、solver値はsigned Q64.64 LogicalScalarだけを使う。GPU、audio、UIへ渡すdomain/camera相対値だけをfloatへ変換する。製品Physicsコードはdouble、BigInteger、旧Logical profile、PhysicsScalarを保持しない。stable IDと明示的な決定順を使い、overflowは直前の有効poseへrollbackしてbodyを停止し、Fault eventを発行する。

## Collisionとsolver

Broadphaseはdomain-local dynamic AABB treeで、active bodyだけを対象にする。Box/Sphereは回転を含む解析的narrow phase、任意Convex/Capsuleはsupport-mapped GJK/EPA、Static Triangle Meshはimmutable cooked BVHを正本とする。動的な箱・凸多面体(`PhysicsShapeAsset.CreateConvexPolyhedron`。破壊の破片)と静的な三角形メッシュの接触(M0109、M0113、M0124)は、候補の三角形ごとに2つの重なりを比べる(分離軸の考え方): (a) 形の角が三角形の面より下にある深さ(面は形の中心の側を表とする。面への射影が三角形の中にあり、形の厚みより浅い角。面から5 cm以内(`BoxMeshSpeculativeMargin`)の角は投機的な点)と、(b) 三角形が形の面から入る深さ(メッシュの稜線: 三角形を形の他の面で切り取り、その面の内側の頂点が点。法線はその面の向き)。小さいほうを採る。形の面とほぼ平行(内積0.95以上)な三角形は(a)だけ(平らな床の上の箱はM0109と同じ結果)。形の面で三角形から離れている(斜めの三角形は0以下、平行な三角形は接触の余裕0.5 mmを超える)三角形の入った角は採らない。斜めの三角形では、角から面の法線の向きへ深さぶん進んだ三角形の上の点が形の中にあることも求める(小さな斜面の三角形の平面を延ばすと形の中心が裏に来て、法線が反転し形を下へ引いた。M0124の修正)。各角は一番深い面の1つだけ。点は面の向きごとのパッチにまとめる: 入った点の一番深いものが1つ目の向き、法線が約18°以内(内積0.95)の点がその向きに入り、残りの一番深い入った点が次の向き。最大3つの向き(`MaximumContactPatches`)、1つの向きに4点、1組に最大12点(`MaximumContactPoints`)。箱は向きごとに深い4点(角の順)、凸多面体は一番深い点と広がり(入った点を先に、残りの枠を投機的な点で。投機的な遠い角が稜線の2つ目の端に勝ち、1点で支えて回った。M0124の修正)。前の向きの点から1 mm以内の点は除く。入った点のない面の角は投機的な点としてだけ加わる(M0109: 時速100 kmは1 tickで0.5 mなので、傾いた箱の前の面は下の角だけが数mm壁に入り、衝撃の全部が下の2点に掛かって箱が回った)。M0124より前は一番深い角の面の向きだけで、斜面と平らな地面にまたがった車体が支えを失い、段の縁が底面に入っても接触にならず沈んだ(M0112で縁石を描くだけにした原因。M0124で縁石は衝突する)。箱と凸多面体、凸多面体どうしは、面の平面で判定する(外接球が離れていれば離れている。相手の角がすべて外にある面があれば離れている。そうでなければ重なりの最も小さい面が法線と深さを決め、相手の中にある角が点になる。角が入らない辺と辺はGJK/EPA)。同じ形の資産を共有する2つの物体の組では、2つ目のワールドの枠が1つ目のものを上書きしない(M0113の修正: 自分自身と重なると判定され、積もった破片が跳ね続けた)。複合形状(`ColliderShape.Compound`、`PhysicsShapeAssetKind.Compound`。複数の破片からなる塊)は、子ごとに相手と判定し、一番深い子の法線から約18°以内の子の点を1つのパッチ(最大4点)にまとめる。相手が三角形メッシュなら、残りの子のパッチを同じ規則で次の向き(最大3つ)にまとめる(M0124)。相手が凸の形(箱の床、別の破片)なら1つの向き(凸の相手にも複数の向きを与えると破壊の見本の積もり方が変わり、爆発が最後の木箱を外した)。点の選び方は深さ0.5 mm・距離1 mm²以内を同じとみなして先の点を採る(毎tick選ぶ点が入れ替わると、warm startの力積が別の角へ移って物体が揺れ続けた)。contact cacheはBegin/Persist/End、1組に最大3つの向きのパッチ(各4点、点ごとの法線)、warm-start impulseを一つの状態として所有する。warm startはパッチごとで、前のtickの同じ特徴と約18°以内の法線のパッチの点を順に引き継ぐ(1つの向きの接触はM0109と同じ)。イベントは1組に1つ(力積は全点の和)。solverはvelocity 8回、position 3回のsequential impulseで、linear/angular inertia、friction、restitution、trigger、sleep/wakeを処理する。眠り(M0113): 眠っている動的な物体と、静的な物体または眠っている物体の組は、狭域判定も解法もせず前のtickの接触を保つ(積もった破片は1 tickに何もしない)。解く接触は、片方が起きている動的な物体かキネマティックな物体のものだけで、その相手が眠っていれば起こす。位置の補正は物体を起こさない(静止した物体は毎tick重力でg·Δt²沈んで補正で戻るので、起こすと永遠に眠らなかった)。物体を取り除く・形や種類を変える(`Remove`、`Upsert`、形の変わる`Synchronize`)と、その接触の相手で眠っている物体は次の`Step`の最初(命令の後、積分の前)に`Wake`イベントとともに起きる(M0113の修正: 支えが消えても眠った物体が宙に残った)。ContinuousStaticはdynamic body対static bodyの保守的sweepでtunnelingを防ぐ。既知の制限(M0109で見つけた): ContinuousStaticは静的な物体を外接箱で扱うので、大きな三角形メッシュ(地面)の外接箱を物体の外接箱の半分だけ広げた範囲の中では、その tick の移動が0に切られる(傾いた車体が地面の上で止まった)。車体は離散(既定)にした。数kmの三角形1枚と箱のGJKは`LogicalScalar`の範囲を超える(試験の床は100 mの格子に分ける)。

定常tickは事前確保したscratch、直接binary search、comparison-allocationを持たないin-place sortを使う。Debug/Release双方で、empty、contact-free、box-box、sphere-boxのwarmed tickがmanaged allocation 0であることをテストする。

## Queryとdebug

Raycast/Sweep/Overlapは呼出側Listへ追記し失敗時に追加分を戻す。Raycastは、静的三角形メッシュのBodyには三角形そのもの(メッシュのBVHで候補を絞り、Möller–Trumboreで両面を判定する。法線は光線に向いた側。ADR-0077/M0101段階9)に当て、それ以外の形は従来どおり外接AABBに当てる。SweepはStaticTriangleMeshを扱わない。Physics broadphaseはGlobal Octreeと別である。Debug snapshotは要求時だけ列挙し、adapter内で検証済みのfloat projectionへ変換する。Scene Viewは最大512 Bodyを独立頂点予算で描画する。

## Artifact

Cooking keyはsource bytes、canonical asset identity、import/settings/scale/seed、cooker/schema/scalar versionを含む。Convex/Static Mesh artifactはcanonical binaryとexact manifest closureを持ち、同一keyはatomic publish後に変更しない。corrupt artifact、path-like identity、余分なentry、同一key異内容を拒否する。

Simulation cacheはidentity、strictly increasing tick、stable body order、event order、entity generation、normalized rotation、enum、末尾byteまで検証する。スキーマ3(M0113)は各frameのeventの後に、そのtickに切れた破壊の結合(物体のstable ID、結合の番号、切ったひずみ。stable IDと番号の昇順で一意)を書く。スキーマ4(M0124、書く版)はその後に、衝撃で内部ひずみが増えた結合(物体のstable ID、結合、次のtickへ持ち越す内部ひずみ(正)。同じ順で一意)を書く。スキーマ2(M0055)と3のfileも、スキーマ以外のidentityが一致すれば読む(2は切断なし、2と3は内部ひずみなし)。古いスキーマの書き手は書けないものを拒む。回転は長さの二乗が1から2⁻³⁰以内を正規化済みとみなす(Q64.64の正規化は冪等でなく、正規化の不動点を求めると回る物体のbakeが拒否された)。Runtimeは入力Streamを所有せず、Engine所有のdelete-on-close spoolへ固定する。load時に全体を検査して最大frame容量を一度だけ確保し、再生時は次の1 frameだけを読むため、RAM使用量はcache時間長に比例しない。body set不一致や欠落tickではWorldを部分更新せずFaultedへ遷移し、realtime handoffは明示された連続tickだけで許可する。

Offline Voronoi prefractureはclosed convex static meshをQ64.64 half-spaceで分割し、各fragmentのclosed consistently-wound single-shell render mesh(外向き)、三角形ごとの材質の区別(`FractureMesh.OuterSlot` 0 = ソースの表面、`InteriorSlot` 1 = 断面)、local convex collision shape(面の平面を持つ凸多面体)、center、mass、Voronoi site、cluster、stable index、ordered bond graph(結合ごとの共有面の面積と強さ = 面積 × 面積あたりの強さ)を一つのstrict canonical artifact(`.cgefracture`版2、`CGEFRAC2`。2〜4段のクラスターは版3(M0124、`CGEFRAC3`: 破片数の後に段の数、破片ごとに各段のクラスター)、1段の資産は版2のバイト列のまま。版1も読む)へ保存する。seed/count/scaleをcooking identityへ含め、atomic immutable publish、corruption、trailing byte、disconnected shell、wrong winding、unused vertex、mesh/shape不一致、つながっていないcluster、正準でないcluster番号、上の段のクラスターに収まらない下の段のクラスターを拒否する。調理の規則と診断(FRACTURE001〜007)はM0113の文書。実行時の切断は下の「破壊」で、artifactは変えない。

## 破壊(ADR-0078/M0113)

ソルバーは`FractureSolver.cs`の`FractureState`(ECSに依存しない。調理・実行時・試験が共有する)。Component、System、Fieldは`plugins/CSharpGameEngine.Destruction`(`Destructible`ほか。`Docs/Specifications/Plugins-And-Assets.md`)。

- ひずみは力積(N·s)。1 tickの間に`AddImpulse`(当たった破片の結合に全部、結合1つ離れるごとに`DamagePropagation`倍、最大4環)・`AddStrain`・`AddStrainSphere`で集める。結合は tick をまたぐ内部ひずみ D を持ち(M0124、UEのChaosの内部ひずみ)、`Resolve`が D + そのtickのひずみ ≥ 強さの結合を結合の番号順に切る(等しければ切れる)。続けて切れていない結合ごとに D ← (D + そのtickの残るひずみ) × `StrainRetention`(保持率 = max(0, 1 − 減衰 × Δt)、`FractureSettings.RetentionFor`)、強さの2⁻²⁰を下回ったら0。残るひずみは衝撃だけ(`Lasting`: 接触が始まったtickの力積、ゲームの力積、キャラクターの衝突のうちそのキャラクターがその物体に触れ始めたtickのもの、`StrainField`)。続いている接触(置かれた物体の荷重)はそのtickだけのひずみで、溜まらない。`StrainRetention`の既定は0(毎tick捨てる。M0113と同じ)、Destructionプラグインは`Destructible.StrainDecay`(既定0.5/s)から作る。
- 多段クラスター(M0124): 結合の強さは面積 × 面積あたりの強さ × `ClusterStrengthScale`^(両方の破片を同じクラスターに持つ段の数、`PrefractureAsset.SharedClusterLevels`)。外側の段のクラスターの間の結合が一番弱く先に切れ、深い段ほど後(UEの段ごとのしきい値)。1段の資産ではクラスターの中が`ClusterStrengthScale`倍(M0113と同じ)。
- 切れた後、塊(`FracturePiece`)の中を結合でたどり、つながる組ごとに新しい塊にする(番号は作った順、破片は昇順、組は最小の破片の順)。塊0は元の物体。アンカーの破片(`AnchorBottom`: 最下面から1 cm以内)を含む塊は動かない。`Apply(結合)`は記録した結合を切り、`Resolve`と同じ塊を作る。`Replay(結合, 内部ひずみ)`(キャッシュの再生、M0124)は記録の結合を切り、すべての結合の内部ひずみを減衰させてから記録の値を置くので、再生の後のDはリアルタイムと同じ(実時間へ引き継いでも同じように切れる。ただしキャッシュは接触の記憶を持たないので、引き継いだtickに置かれた物体の接触は「始まり」になり、その荷重が衝撃として溜まる)。`Remove`で除いた塊の破片はひずみを受けない。
- 塊の衝突形状は、1破片ならその凸多面体、複数ならそれらの複合形状(塊の重心が原点。破片の組ごとに1度作る)。
- `DeterministicPhysicsWorld.ReportBondBreak(tick, 物体, stable ID, 結合, ひずみ)`は`BondBreaks`(次のtickの最初に空になる。キャッシュが記録する)と`PhysicsEventType.BondBroken`のevent(`First`が物体、力積がひずみ、pair IDは物体のstable IDと結合のhash)に入る。eventは`Step`と同じ正準の順に挿入する。`ReportBondStrain(stable ID, 結合, 内部ひずみ)`(M0124)は`BondStrains`(同じく次のtickに空。キャッシュが記録する。eventは出さない)に入る。
- 決定性: 整数とQ64.64、番号の順だけ。同じ入力は同じ塊を作る。

Scene Bake HostはC# Sceneのcompile-time switch、Runtimeと同じ生成System set、Fixed/Physics/PostPhysicsだけのheadless tick、host-owned bounded scratch、逐次canonical cache writerを使う。CLIは同一directoryのtemporary outputをflushした後だけatomic replaceし、unknown Scene、cancellation、途中停止、IO失敗ではlast-known-goodを変更しない。Scene Viewはbody bounds、velocity、sleep、center of mass、contact normal、broadphase tree、prefracture bondと破片の輪郭をbounded overlayとして描画する。破片はシーンの`Destructible`の箱の`Collider`をそのFragmentCount・Seed・ClusterCountで`Physics.Cli preview-prefracture`が別processで切ったもので、Inspectorの値の変更がbuild前に見える(UEのFractureモードのプレビュー。`SourceMesh`の物体は最後のbuildが調理した資産。M0113)。Scene Viewの編集用ワールドは物体を登録するだけで時間を進めない(M0097)。そのためoverlayは画面の物体と一致し、時間の進む物理はゲームの実行中だけになる。Bake Selected/Allは対象Sceneのlive編集だけをC#へcommitして対象source fileをdiskへ保存し、エンジンのsource fingerprintと一致するexact out-of-process hostへversioned requestを送る。hostはゲームを参照せず、Sceneのゲームプロジェクトをそのsource fingerprint(エンジン、build規約、プロジェクトとAssets。Bake出力は除く)ごとにbuildした成果物を`bake --assembly`で読み込む。このfingerprintがcacheのdependency hashになる(M0096 G)。未保存・build・cancellation・corruptionの失敗時はBakeを開始しないかlast-known-goodを維持する。
