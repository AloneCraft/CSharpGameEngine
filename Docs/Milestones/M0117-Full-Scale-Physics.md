# M0117 — 全スケール物理(Q64.64 のまま ±9.2e18 m)
- 日付: 2026-10-25
- 状態: 計画(2026-10-25 に ADR-0082 を Accepted。進行中の M0112 がコミットされてから始める)
- 決定: [ADR-0082](../Decisions/ADR-0082-Full-Scale-Physics.md)

## 目的
物理を Q64.64 のまま全スケールで動かす。ペアごとの局所原点、物体相対の静的メッシュ、平行移動で変わらない広域判定、複数ドメインの同時進行、物体ごとの Fault、float の入口の Logical 化。対数空間で物理は解かない。

## 段階
1. **正解の用意**: `ComputeStateHash(Origin)` に回転・角速度・SleepTicks・Faulted を入れ、基準 6 場面(箱 3 段、メッシュの坂を転がる球、CharacterMotor、車、32 体の落下、3 km の三角形 1 枚の床)のゴールデンを固定する。場面 6 は今は例外になることを固定する。`PhysicsTranslationInvarianceTests` の枠を作り、修正前に失敗するオフセットと例外の型・場所を記録する。
2. **物体ごとの Fault**: `ArithmeticException`(0 除算を含む)を Upsert・BuildContacts・SolveContacts・Raycast・Sweep で捕まえる。原点のハッシュは変わらない。
3. **Numerics と局所原点**: Numerics の新 API、`PhysicsKernelScale`、ペアごと・照会ごとの局所原点、`PhysicsLimits`、慣性を 1 回だけ計算、Raycast の方向を正規化。絶対座標への乗算・除算の監査(CCD、`TriangleCenter`、CharacterMotor、Vehicles、FootIk、OrbitCamera、Navigation)。原点のハッシュが変わるので付け替えの規則で確かめる。
4. **静的メッシュと広域判定**: 物体相対のメッシュ(キャッシュの鍵は (資産, L))、静的 Collider が親・回転・スケールを尊重する、半周長のコストと `QueryRay`。調理の版を上げる。
5. **複数ドメイン**: 全ドメインを StableId の順に逐次進める。ドメインをまたぐ物体の速度。
6. **float の入口を Logical に**(1 項目 1 コミット。テンプレートの値をそのつど記録): シーン JSON の `LogicalScalar` を十進のまま、`PhysicsWorldSettings`(`FixedDelta = One/TickRate`)、`PlayerControl`、Picking、`PhysicsDebug`(プロトコル 3)。
7. **全範囲の確認と文書**: 2^50 m ずらした ThirdPerson・Vehicle の実走、床を 100 m の格子に分ける回避策を消す、Stage `rigid.full-scale`、ADR-0049 を一部 Superseded に、`Physics.md`・`World-Scale-Runtime.md`。

## 完了条件
- 不変性の試験(6 場面 × 10 オフセット × 4 方向)が Debug と Release の両方でビット一致。
- `PhysicsOverflowFaultTests`・`PhysicsMultiDomainTests`・`PhysicsGoldenStateTests` に合格。ChaosVehicleTests 101、CharacterMotorTests 25、Physics の 9 ファイルの回帰に合格。
- ベンチがオフセット 0 で基準の 1.10 倍以内。2^50 m ずらした実走が原点の値と同じ。

## 範囲外
対数空間での物理、Q95.96、浮動原点、動的物体の親・スケール≠1 の拒否、外接 AABB だけの光線判定、Sweep の近似、GPU ソルバー、向きが変わる重力。

## 機能 ID
F-L5-046、F-L5-003、F-L1-009、F-L6-002、F-L5-006
