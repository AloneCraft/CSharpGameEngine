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

Broadphaseはdomain-local dynamic AABB treeで、active bodyだけを対象にする。Box/Sphereは回転を含む解析的narrow phase、任意Convex/Capsuleはsupport-mapped GJK/EPA、Static Triangle Meshはimmutable cooked BVHを正本とする。動的な箱と静的な三角形メッシュの接触は、箱の8つの角のうち、触れた三角形の面より下にあり(箱の中心の側を面の表とする)面への射影が三角形の中にある角を集め、一番深い角の面の法線に近い(内積0.95以上)面の角から深い順に最大4点を1つのmanifoldにする(M0109。それまでは一番深い三角形の1点だけで、メッシュの床の上の箱が揺れて回り、沈んだ)。入った角のない面の角でも、面から5 cm以内(`BoxMeshSpeculativeMargin`)なら、入った角のある面のmanifoldの点に加える(負の貫入の投機的な点。M0109の修正: 時速100 kmは1 tickで0.5 mなので、傾いた箱の前の面は下の角だけが数mm壁に入り、衝撃の全部が下の2点に掛かって箱が回った)。投機的な点は、その tick に面へ届く速さで近づく(接近速さ × Δt が隙間より大きい)ときだけ、触れている点と同じ値に止める。届かない点は何も制約しない(傾いて落ちる箱の上の角が、床から5 cm以内で止められて傾いたまま乗ることを避ける)。反発は、solverの繰り返しの前の接近速さから点ごとに1度だけ決め(`CachedPoint.Bounce`)、繰り返しのすべてがその値を狙う(以前は最初の繰り返しだけが反発を狙い、あとの繰り返しは0を狙うので、点ごとに違う値になって箱が回った)。衝突(接近速さが反発のしきい値0.5 m/sを超える)の tick の力積は、次の tick に支える力としては持ち越さない(`CachedPoint.Impacted`。持ち越すと、止まった体を衝突の速さで面から投げ返し、それを取り除く繰り返しの残りが回転になった)。面に角が入らない触れ方(辺と辺)は従来のGJK/EPAの1点。球とカプセルは従来どおり三角形ごとの解析的な接触の一番深い1点。contact cacheはBegin/Persist/End、最大4点manifold、warm-start impulseを一つの状態として所有する。solverはvelocity 8回、position 3回のsequential impulseで、linear/angular inertia、friction、restitution、trigger、sleep/wakeを処理する。ContinuousStaticはdynamic body対static bodyの保守的sweepでtunnelingを防ぐ。既知の制限(M0109で見つけた): ContinuousStaticは静的な物体を外接箱で扱うので、大きな三角形メッシュ(地面)の外接箱を物体の外接箱の半分だけ広げた範囲の中では、その tick の移動が0に切られる(傾いた車体が地面の上で止まった)。車体は離散(既定)にした。数kmの三角形1枚と箱のGJKは`LogicalScalar`の範囲を超える(試験の床は100 mの格子に分ける)。

定常tickは事前確保したscratch、直接binary search、comparison-allocationを持たないin-place sortを使う。Debug/Release双方で、empty、contact-free、box-box、sphere-boxのwarmed tickがmanaged allocation 0であることをテストする。

## Queryとdebug

Raycast/Sweep/Overlapは呼出側Listへ追記し失敗時に追加分を戻す。Raycastは、静的三角形メッシュのBodyには三角形そのもの(メッシュのBVHで候補を絞り、Möller–Trumboreで両面を判定する。法線は光線に向いた側。ADR-0077/M0101段階9)に当て、それ以外の形は従来どおり外接AABBに当てる。SweepはStaticTriangleMeshを扱わない。Physics broadphaseはGlobal Octreeと別である。Debug snapshotは要求時だけ列挙し、adapter内で検証済みのfloat projectionへ変換する。Scene Viewは最大512 Bodyを独立頂点予算で描画する。

## Artifact

Cooking keyはsource bytes、canonical asset identity、import/settings/scale/seed、cooker/schema/scalar versionを含む。Convex/Static Mesh artifactはcanonical binaryとexact manifest closureを持ち、同一keyはatomic publish後に変更しない。corrupt artifact、path-like identity、余分なentry、同一key異内容を拒否する。

Simulation cacheはidentity、strictly increasing tick、stable body order、event order、entity generation、normalized rotation、enum、末尾byteまで検証する。Runtimeは入力Streamを所有せず、Engine所有のdelete-on-close spoolへ固定する。load時に全体を検査して最大frame容量を一度だけ確保し、再生時は次の1 frameだけを読むため、RAM使用量はcache時間長に比例しない。body set不一致や欠落tickではWorldを部分更新せずFaultedへ遷移し、realtime handoffは明示された連続tickだけで許可する。

Offline Voronoi prefractureはclosed convex static meshをQ64.64 half-spaceで分割し、各fragmentのclosed consistently-wound single-shell render mesh、local convex collision shape、center、mass、stable index、ordered bond graphを一つのstrict canonical artifactへ保存する。seed/count/scaleをcooking identityへ含め、atomic immutable publish、corruption、trailing byte、disconnected shell、wrong winding、unused vertex、mesh/shape不一致を拒否する。Runtime topology mutationはDeferred destruction domainであり、L6 artifact契約へ混在させない。

Scene Bake HostはC# Sceneのcompile-time switch、Runtimeと同じ生成System set、Fixed/Physics/PostPhysicsだけのheadless tick、host-owned bounded scratch、逐次canonical cache writerを使う。CLIは同一directoryのtemporary outputをflushした後だけatomic replaceし、unknown Scene、cancellation、途中停止、IO失敗ではlast-known-goodを変更しない。Scene Viewはbody bounds、velocity、sleep、center of mass、contact normal、broadphase tree、prefracture bondをbounded overlayとして描画する。Scene Viewの編集用ワールドは物体を登録するだけで時間を進めない(M0097)。そのためoverlayは画面の物体と一致し、時間の進む物理はゲームの実行中だけになる。Bake Selected/Allは対象Sceneのlive編集だけをC#へcommitして対象source fileをdiskへ保存し、エンジンのsource fingerprintと一致するexact out-of-process hostへversioned requestを送る。hostはゲームを参照せず、Sceneのゲームプロジェクトをそのsource fingerprint(エンジン、build規約、プロジェクトとAssets。Bake出力は除く)ごとにbuildした成果物を`bake --assembly`で読み込む。このfingerprintがcacheのdependency hashになる(M0096 G)。未保存・build・cancellation・corruptionの失敗時はBakeを開始しないかlast-known-goodを維持する。
