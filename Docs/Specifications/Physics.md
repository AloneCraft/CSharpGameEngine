# Physics Runtime仕様

## 実行モデル

Physicsは60 Hz整数tickの`PhysicsSimulation` phaseで実行する。FrameInput、0..N FixedSimulation、Physics同期/solve/writeback、PostPhysics、Global Spatial同期、Presentationの順を正本とする。壁時計をsolverへ渡さない。

## 作者API

`Collider`のみはStatic、`Collider + RigidBody(Dynamic)`はPhysics所有姿勢、KinematicはCommand targetで移動する。Dynamicの直接Transform編集は禁止しTeleportを使う。Dynamic Triangle MeshとDynamicの親・非One Scaleは拒否する。

## 数値と決定性

World/Domain原点は選択Logical、局所solverはQ32.32、積除算はInt128、ties-to-even。stable IDと全処理列の明示sortを使う。overflow等は直前の有効poseで停止しFault eventを出す。

## Queryとdebug

Raycast/Sweep/Overlapは呼出側Listへ追記し失敗時に追加分を戻す。Physics broadphaseはGlobal Octreeと別である。Debug snapshotは要求時だけ列挙・double変換し、Scene Viewは最大512 Bodyを独立頂点予算で描画する。

## Artifact

Cooking keyはsource bytes、GUID、import/settings/scale/seed、cooker/schema/scalar versionを含む。Simulation cache identity不一致はhard error。現在の実装範囲と未完成事項は`Docs/今は実装していないが実装すべき機能.md`を参照する。
