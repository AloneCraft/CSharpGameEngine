# Physics Runtime仕様

## 実行モデル

Physicsは60 Hz整数tickの`PhysicsSimulation` phaseで実行する。FrameInput、0..N FixedSimulation、Physics同期/solve/writeback、PostPhysics、Global Spatial同期、Presentationの順を正本とする。壁時計をsolverへ渡さない。

## 作者API

`Collider`のみはStatic、`Collider + RigidBody(Dynamic)`はPhysics所有姿勢、KinematicはCommand targetで移動する。Dynamicの直接Transform編集は禁止しTeleportを使う。Dynamic Triangle MeshとDynamicの親・非One Scaleは拒否する。

## 数値と決定性

authoritativeなposition、rotation、scale、velocity、mass、solver値はsigned Q64.64 LogicalScalarだけを使う。GPU、audio、UIへ渡すdomain/camera相対値だけをfloatへ変換する。製品Physicsコードはdouble、BigInteger、旧Logical profile、PhysicsScalarを保持しない。stable IDと明示的な決定順を使い、overflowは直前の有効poseへrollbackしてbodyを停止し、Fault eventを発行する。

## Collisionとsolver

Broadphaseはdomain-local dynamic AABB treeで、active bodyだけを対象にする。Box/Sphereは回転を含む解析的narrow phase、任意Convex/Capsuleはsupport-mapped GJK/EPA、Static Triangle Meshはimmutable cooked BVHを正本とする。contact cacheはBegin/Persist/End、最大4点manifold、warm-start impulseを一つの状態として所有する。solverはvelocity 8回、position 3回のsequential impulseで、linear/angular inertia、friction、restitution、trigger、sleep/wakeを処理する。ContinuousStaticはdynamic body対static bodyの保守的sweepでtunnelingを防ぐ。

定常tickは事前確保したscratch、直接binary search、comparison-allocationを持たないin-place sortを使う。Debug/Release双方で、empty、contact-free、box-box、sphere-boxのwarmed tickがmanaged allocation 0であることをテストする。

## Queryとdebug

Raycast/Sweep/Overlapは呼出側Listへ追記し失敗時に追加分を戻す。Physics broadphaseはGlobal Octreeと別である。Debug snapshotは要求時だけ列挙し、adapter内で検証済みのfloat projectionへ変換する。Scene Viewは最大512 Bodyを独立頂点予算で描画する。

## Artifact

Cooking keyはsource bytes、canonical asset identity、import/settings/scale/seed、cooker/schema/scalar versionを含む。Convex/Static Mesh artifactはcanonical binaryとexact manifest closureを持ち、同一keyはatomic publish後に変更しない。corrupt artifact、path-like identity、余分なentry、同一key異内容を拒否する。

Simulation cacheはidentity、strictly increasing tick、stable body order、event order、entity generation、normalized rotation、enum、末尾byteまで検証する。Runtimeは入力Streamを所有せず、Engine所有のdelete-on-close spoolへ固定する。load時に全体を検査して最大frame容量を一度だけ確保し、再生時は次の1 frameだけを読むため、RAM使用量はcache時間長に比例しない。body set不一致や欠落tickではWorldを部分更新せずFaultedへ遷移し、realtime handoffは明示された連続tickだけで許可する。

Offline Voronoi prefractureはclosed convex static meshをQ64.64 half-spaceで分割し、各fragmentのclosed consistently-wound single-shell render mesh、local convex collision shape、center、mass、stable index、ordered bond graphを一つのstrict canonical artifactへ保存する。seed/count/scaleをcooking identityへ含め、atomic immutable publish、corruption、trailing byte、disconnected shell、wrong winding、unused vertex、mesh/shape不一致を拒否する。Runtime topology mutationはDeferred destruction domainであり、L6 artifact契約へ混在させない。

Scene Bake HostはC# Sceneのcompile-time switch、Runtimeと同じ生成System set、Fixed/Physics/PostPhysicsだけのheadless tick、host-owned bounded scratch、逐次canonical cache writerを使う。CLIは同一directoryのtemporary outputをflushした後だけatomic replaceし、unknown Scene、cancellation、途中停止、IO失敗ではlast-known-goodを変更しない。Scene Viewはbody bounds、velocity、sleep、center of mass、contact normal、broadphase tree、prefracture bondをbounded overlayとして描画する。Bake Selected/Allは対象Sceneのlive編集だけをC#へcommitして対象source fileをdiskへ保存し、source closure fingerprintと一致するexact out-of-process hostへversioned requestを送る。未保存・build・cancellation・corruptionの失敗時はBakeを開始しないかlast-known-goodを維持する。
