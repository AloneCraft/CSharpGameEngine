# M0109 — Chaos 相当の車両(Vehicles プラグインの書き直し)
- 日付: 2026-10-23
- 状態: 完了(2026-10-23。下の「範囲外(未実装)と不確実性」に、UE との差と未実装を行ごとに書いた)

## 目的
ADR-0078 の「UE テンプレートの完全移植」のうち、`UE-Template-Behavior.md` の Vehicle の表で担当が M0109 の行をすべて実装する。計画の段階 1 の 6。
M0101 段階 9 の簡易の車両モデル(運動学で動く車)を、UE の ChaosVehicles 相当(車体は物理の動的剛体、車輪ごとの光線のサスペンション、トルク曲線と 5 速の自動変速、アッカーマン操舵、タイヤの摩擦、空気抵抗、転倒とリセット)に置き換え、ブレーキランプ・ヘッドライト・車輪の動き・カメラの切り替えと見回しを付ける。

## 事実(再現して確かめたこと)
- UE の式の正本: UE 5.8 には ChaosVehiclesCore の公開ヘッダーしか無いが、UE 5.7(`C:\Program Files\Epic Games\UE_5.7\Engine`)には実装の `.cpp` がある(`EngineSystem.cpp`、`TransmissionSystem.cpp`、`SteeringSystem.cpp`、`SuspensionSystem.cpp`、`SuspensionUtility.cpp`、`WheelSystem.cpp`、`AerodynamicsSystem.cpp`、`VehicleUtility.cpp`、プラグインの `ChaosWheeledVehicleMovementComponent.cpp`・`ChaosVehicleMovementComponent.cpp`)。テンプレートは UE 5.7 で作られたもの(M0102)なので、5.7 のソースを正本とした。
- 値の正本: T3D とコンポーネントの書き出し(`.artifacts/UnrealExport/logic/Vehicle/`)と、UE を起動して読んだ値(`tools/UnrealExport/probe_vehicle_physics.py`・`probe_vehicle_brake_lights.py`・`probe_vehicle_emissive.py`。出力は `.artifacts/m0109/` の外の作業フォルダ。主な値は下)。
  - 車輪の骨(メッシュの原点から、cm): FL (135, −90, 25)、FR (135, 90, 25)、BL (−129.2, −90, 26.5)、BR (−129.2, 90, 26.5)。物理アセットの重心 (−6.96, 0.04, 50.09)。
  - 物理マテリアル: Slippery と NonSlippery はどちらも摩擦 1.0(反発 0.4 と 0)。Landscape の既定は NonSlippery。層の情報に物理マテリアルは無い。置かれたメッシュは既定(摩擦 0.7、反発 0.3)。
  - 後ろの腕: 長さ 381.02、ソケット z 71.80、位置 z 75、遅れ 10(最大 50)、回転の遅れ 2、衝突なし、Pitch と Roll を継がない。前の腕: 位置 (30, 0, 120)、長さ 0、遅れなし。前のカメラは最初は無効、FOV 90。後ろは FOV 110。
  - ヘッドライト: 左は内 0° / 外 44°(UE の既定)、右は内 22.49° / 外 50.30°(M0102 の表は左右とも右の値と書いていた)。右は左の子ではなく `VehicleMesh` に付く。
- **UE ではブレーキランプは光らない**: `BrakeLights` が値を書く `Mesh`(`VehicleMesh`)は 4 頂点の見えない骨格メッシュで、そのマテリアル 0 番は `WorldGridMaterial`(`BrakeLightPower` という引数を持たない)。見える車体のマテリアル `M_SportsCarBase` の発光は `EmissivePower × Named Reroute` で、その Reroute は宣言が無い(T3D の `DeclarationGuid` だけ)ので 0。
- **UE の角速度の減衰は常に 3**: BP の Tick は `SetAngularDamping(IsMovingOnGround ? 0 : 3)` だが、`ChaosVehicleMovementComponent` は `IsMovingOnGround` を上書きせず、`UNavMovementComponent` の既定(常に false)を返す。
- **操舵の速さの曲線の横軸はマイル毎時**: `ProcessSteering` が `CmSToMPH(ForwardSpeed)` で曲線を引く(M0102 の表は km/h と書いていた)。
- UE の既定のサスペンションは、物理のソルバーの中のサスペンションの制約(ばね ×0.25、予圧、縮みきりのハードストップ。`p.Vehicle.DisableConstraintSuspension=0`)。力のサスペンション(`ApplySuspensionForces` が力を加える経路)は、そのコンソール変数を 1 にしたときだけ。どちらの経路でも、タイヤの荷重はばねの力の式(`FSimpleSuspensionSim::Simulate`)で決まる。
- 物理(実装中に見つけた既存の制限。下の「実装中に見つけた事実」):
  - 動的な箱と静的な三角形メッシュの接触は、一番深い三角形の 1 点だけだった。メッシュの床の上の箱(車体)は揺れて回り出し、床を抜けて落ちた。
  - `Ccd=ContinuousStatic` は静的な物体を外接箱で扱うので、傾いた車体の外接箱が地面の外接箱(を広げたもの)に入ると、その tick の移動が 0 に切られ、車が止まった。
  - 静的な物体を持つ動的な物体の `PhysicsTransformWriteback` は `PhysicsRotation` だけを書き、`Transform.Rotation` は書かない(物理の剛体の見た目の回転は、その物体の System が写す)。
- 修正前の実走(WARP): Vehicle(キー) 48.281 m・38.3°、右トリガー 0.5 は 26.889 m、左スティック 0.5 は 31.5°。ThirdPerson 15.700 m、FirstPerson 16.000 m、TopDown 9.606 m(ナビメッシュなし)・(6.79, 6.84)(あり)。

## 決定
### 1. 層と置き場所(ADR-0078 §3)
- L4 物理の契約(`Runtime.Physics`): `PhysicsCommands` に `ForceAtPoint`・`Torque`・`SetVelocity`・`Sleep`(`PhysicsCommand` の末尾に既定値つきの `Point`)、`RigidBody.LinearDamping`・`AngularDamping`、`DeterministicPhysicsWorld.TryGetMaterial`、`StaticMeshCollider.Material`。車両はこの契約と光線だけを使う(L5 → L4 の向き)。
- L5 プラグイン `plugins/CSharpGameEngine.Vehicles`(ECS のソース。ADR-0079): `VehicleSetup`([DataAsset])、`Vehicle`・`VehicleState`・`VehicleIntent`・`VehicleWheelVisual`・`VehicleCameraLook`、`VehicleChaos`(UE の式)、`VehicleTuning`(前もって求める値)、`VehicleAckermann`、`VehicleMotor`(1 tick)、System(`VehicleBodySetup`・`VehicleMotionUpdate`・`VehicleBodyWriteback`・`VehicleWheelPose`・`VehicleChaseView`)。
- L5 描画(`Runtime.Windows`、`Windows`): `MeshEmissive` と `StageRenderer.SetObjectEmissiveScale`(物体の定数バッファの既存の発光の欄に掛ける。描画契約 `stage-v11` とシェーダーは変えない)、`LightVisibility`(`Runtime.Lighting`)、表示中のカメラが無効になったら有効な別のカメラへ切り替える。
- L1/L7 入力: `Pad.Back`・`Pad.Start`(末尾に追加。UE の Gamepad_Special_Left/Right)。L1 数値: `LogicalMath.TryAtan2`(決定的)。
- L9 テンプレート: `VehicleControls`・`UseSportsCarSetup`・`CameraToggle`・`LookAround`・`BrakeLightControl`・`HeadlightToggle`、`SportsCar.json`、`SportsCarSetup.cgeasset`、車体の glb の発光、レベルの路面の摩擦。

### 2. UE の式をそのまま移す(`VehicleMotor`)
1 固定 tick の順は UE と同じ: ゲームのスレッドの `UpdateState`(後退と 1 速の選択、`CalcThrottleBrakeInput`、入力の速さ、`ProcessSleeping`)→ 物理のスレッドの `UpdateState`(車輪の位置と速さ、光線、接地)→ `ApplyInput`(エンジンへアクセルの二乗、ブレーキとエンジンブレーキとハンドブレーキのトルク)→ 空気抵抗 → エンジンと変速 → サスペンションとロールバー → 操舵 → タイヤの摩擦。力は車輪ごとに 1 つにまとめて車輪の位置に `ForceAtPoint`、空気の力は重心に `Force`。
- 単位は m・kg・s。UE の内部の cm の式は同じ値になるように直した(コーナリング剛性 1000 → 1e5 N/rad、ばね 250 → 25000 N/m、しきい値の cm/s → m/s)。
- 計算は `LogicalScalar` だけ(三角関数は `TrySinCos` と新しい `TryAtan2`)。入力の float は `FromSingle` で決定的に変換する。
- 曲線は UE と同じく 21 点の標本(トルクは 0〜最大回転、操舵は 0〜最後の点)と直線の補間。アッカーマンは UE の棒の幾何を cm で解く(入力の倍率 10 が cm のため)。

### 3. 力のサスペンションを採る
- UE の既定の制約のサスペンションは、物理のソルバーに新しい制約を足すことになり(ばね・ダンパーの位置の拘束とハードストップ)、範囲が大きい。力のサスペンション(UE の `DisableConstraintSuspension=1` の経路)を採った。値は同じ(ばね 25000 N/m、減衰比 0.5、荷重比 0.5、ロールバー 0.15)。
- 違い: 縮みきると(光線の始点より地面が上)、光線が当たらず力が 0 になる。車体の箱(下面は地面の上約 0.14 m)が床に当たって支える。

### 4. 車体の剛体
- 根は重心(UE の物理アセットの重心)。剛体は箱 1.9 × 0.9 × 4.5 m(UE の `Chasis_Main` の大きさから)、1500 kg、`LinearDamping` 0.01(UE の BodyInstance)、`AngularDamping` 3(上の事実: UE の実際の値)。CCD は使わない(上の事実の制限)。
- 慣性は箱の値で、UE の物理アセット(凸包)の慣性ではない(推測で差は小さい)。
- 生成した車(リスポーン)の剛体は、生成した向きで作る: FixedSimulation の `VehicleBodySetup` が最初の tick に `PhysicsRotation` を Transform の向きにする。`VehicleBodyWriteback` が物理の向きを Transform に写す。

### 5. 箱と三角形メッシュの接触を直す(根本原因の修正)
- 物理の狭域判定で、箱は「触れた三角形の面より下にあり、面への射影が三角形の中にある角」を集め、一番深い角の面の法線に近い面の角から最大 4 点を 1 つの manifold にする(`PhysicsNarrowPhase.TryBoxMeshManifold`)。面に角が入らない触れ方は従来の GJK/EPA。
- 修正前(1 点)は、裏返しの車が屋根で回り出して床を抜けた。修正後は屋根で静止し、6 秒後にリセットされる(`AnUpsideDownCarIsResetAfterTwoFlippedChecksThreeSecondsApart`)。壁は前から止まる(`StopsAtAWallInFrontOfTheCar`)。箱の静止は `PhysicsForceCommandTests.ABoxRestsFlatOnATriangleMeshFloorOnFourCornersWithoutTurningOrSinking`。

### 6. データアセット `VehicleSetup`
- UE の移動コンポーネントと車輪のクラスの値を 1 つのデータ(エンジン・変速・操舵・差動・前輪・後輪・入力の速さ・眠り)にした。値はテンプレートの `SportsCarSetup.cgeasset`。シーンからデータアセットを参照する仕組みはまだ無い(ADR-0079)ので、ゲームのコード(`UseSportsCarSetup`)が `Vehicle.Setup` に入れる。設定の無い車は力を加えない。
- 車輪の取り付け(重心から見た位置と前後)はシーンの `Vehicle.Wheels`(4 つまで。状態は `VehicleState.Wheel0`〜`Wheel3` の値の型で、スナップショットに入る)。
- `Vehicle` は `[EcsSnapshotTransient]`(スナップショットに入れない)。ECS のスナップショットは値の型と文字列の欄だけを書け、`Setup`(クラス)と `Wheels`(配列)を持つ `Vehicle` があると、生成された `World` の型の初期化で例外になっていた(全テストの `PersistentComponentSnapshotTests` などで見つけた)。`Vehicle` は実行中に変わらない設定で、復元はシーンから読み直したセルの Entity にだけ値を書くので、読み直した値(とコードが入れる設定)を保てばよい。変わる状態の `VehicleState` はスナップショットに入り、往復で同じ値になる(`TheDrivingStateGoesIntoASnapshotAndTheSetupStaysWithTheScene`)。

### 7. ブレーキランプ(UE では光らないが、意図を採る)
- 描画: `MeshEmissive.Scale` が、メッシュのマテリアルの発光(nits)に掛かる(物体ごと。描画契約は変えない)。`MeshEmissive` を持つメッシュは、発光で周りを照らさない(表面だけが光る。大きなメッシュの小さな発光を毎フレーム光源に分けない)。
- 車体の glb のマテリアル 0 に、UE の `T_SportsCar_Lights_M` の赤のチャンネル(シャシーの UV 0 のテールランプの範囲)を発光のテクスチャとして 100 nits で足した。テンプレートは `MeshEmissive.Scale` に BrakeLightPower(200 か 4)を書く。UE の値 1 を 100 nits とするのは推測(UE の発光の値は露出に依る相対値)。200 nits のままだと昼の光で見えなかった(実走の画像で確かめた)。

### 8. カメラ
- 後ろ: `OrbitCamera`(M0107 の遅れと `SocketOffset`)を UE の腕の値で。`VehicleChaseView` が腕の Yaw を車の Yaw + 見回しの角にし(Pitch は継がず 0)、見回しの角を毎フレーム FInterpTo(速さ 1)で 0 へ戻す。`LookAround` の入力(回転数)は度に直して足す。
- 前: 車体の子の固定のカメラ(車体の向きをそのまま継ぐ)。最初は無効。
- 切り替え: 2 台の `Camera.Active` を Tab で反転(UE の FlipFlop と同じ結果)。描画の `LevelLoad` は、表示中のカメラが無効になり、有効な別のカメラがあれば切り替える(M0107 の「壊れたら切り替える」を広げた)。

### 9. その他
- ヘッドライト: 光の隣に置く補助のコンポーネント `LightVisibility`(`Visible`。無いか true なら表示。偽なら光源を出さない。4 種の光すべてに効く)。最初は `SpotLight` の欄にしたが、光の型は物理量の欄だけという契約(VS 拡張の光の型の変換 `LightConversionPlanner` が全欄を物理量として作り直す)を壊し、変換のテストが落ちたので、`LightShadow` と同じ補助のコンポーネントに移した。左だけを L で切り替える(UE と同じ)。左の内の角 0° はエンジンが正の角を求めるので 0.036°。
- エンジン音: 作らない。既存の音は一発の再生だけで、ループと音程の変更が無い。UE のテンプレートにも音は無い(ADR-0078 §2)。
- 自動実行の検証(`VehicleDriveVerification`): 曲がりの比較の窓を 34〜42 から 30〜34 に移した。新しい車は速く、WARP では 1 フレームに数 tick 進むので、右へ曲がった車が 37 フレームごろにレベルの壁(PlayerStart から +R の壁まで約 18 m)に当たり、以前の窓は壁の跳ね返りを測っていた(実走の位置の推移で確かめた)。
- SDK の内容が変わったので `build/CSharpGameEngine.Version.props` を 0.1.20 にした。Vehicles プラグインのマニフェストは 0.2.0、`EngineVersion` は `>=0.1.20`(新しい物理の命令を使う)。

## 実装したもの
| 層 | 中身 |
|---|---|
| L1 Numerics | `LogicalMath.TryAtan2` |
| L1/L7 入力 | `Pad.Back`・`Pad.Start`、`InputActionsEmitter`、`Win32InputEventAdapter`(自動実行の `GamepadBack`・`GamepadStart`) |
| L4/L5 物理 | `PhysicsCommands`(`ForceAtPoint`・`Torque`・`SetVelocity`・`Sleep`)、`RigidBody.LinearDamping`・`AngularDamping`、`TryGetMaterial`、`StaticMeshCollider.Material`、`PhysicsNarrowPhase.TryBoxMeshManifold` |
| L5 描画 | `MeshEmissive`、`RenderServer`・`StageRenderer.SetObjectEmissiveScale`、`RenderFrameState`(装置の回復で再生)、`MeshEmissive` のメッシュを光源にしない、`LightVisibility`、カメラの切り替え |
| L5 プラグイン | `plugins/CSharpGameEngine.Vehicles/Runtime/`(`VehicleSetup.cs`・`VehicleChaos.cs`・`VehicleComponents.cs`・`VehicleMotor.cs`・`VehicleSystems.cs`)、`Vehicles.cgeplugin` 0.2.0 |
| L9 | Vehicle テンプレート(`Gameplay/`、`Scenes/SportsCar.json`・`Lvl_VehicleBasic.json`、`Assets/Vehicles/SportsCar/SportsCarSetup.cgeasset`・`SM_SportsCar.glb`)、`build/CSharpGameEngine.Version.props` 0.1.20 |
| ツール | `tools/UnrealExport/probe_vehicle_physics.py`・`probe_vehicle_brake_lights.py`・`probe_vehicle_emissive.py`(と `.cmd`)、`add_brake_light_emission.py`、`VehicleDriveVerification` の窓 |

## 検証
### 単体テスト(決定的な物理の世界で、外から見える振る舞い)
`tests/CSharpGameEngine.Windows.Tests/Character/ChaosVehicleTests.cs`(25 件。場は `ChaosVehicleTrack.cs`)の主な値:
| 振る舞い | 値 |
|---|---|
| 0〜100 km/h | 3.42 秒。1 → 2 速は約 5962 rpm(ChangeUpRpm 6000)、変速中の空走 12 tick |
| 最高速(6000 m の直線) | 5 速、約 269 km/h |
| 100 km/h からのブレーキ | 18.0 m、1.28 秒 |
| 右いっぱいの旋回(30 km/h 前後) | 半径 4.17 m。アッカーマンで内輪 37.4°、外輪 30.8° |
| ジャンプ台 | 空中 68 tick、最高 4.24 m。着地して走り続ける |
| 裏返し | 3 秒ごとの判定で 2 回続けて裏返しならリセット(持ち上げ、向きを直し、速度 0) |
| 低い摩擦(0.1) | 後輪が空転し、2 秒後の速さが摩擦 1 の路面の 0.6 倍未満 |
| ハンドブレーキ | 後輪が滑り、横滑りの角が大きくなる |
| 決定性 | 同じ入力の 2 回の走りが Q64.64 のビットまで一致 |
そのほか、スナップショット(走りの状態は往復で同じ、設定は入らない)、静止と眠り、重い車は沈む、落下と着地、後退、エンジンブレーキ、入力の速さ、壁で止まる、手動のリセット、設定の無い車、車輪の見た目の姿勢、テンプレートのアセットの値。
物理: `Physics/PhysicsForceCommandTests.cs`(9 件。点の力・トルク・速度・眠り・減衰の決定性と、箱が三角形の床に 4 隅で静止)。数値: `tests/CSharpGameEngine.Numerics.Tests/LogicalMathAtan2Tests.cs`(14 件。4 象限と軸の上で `Math.Atan2` と小数 6 桁まで一致、45° は π/4 と 2^-64 の 1000 単位以内、(0, 0) は 0、同じ入力は同じビット)。

### 実走(WARP、`CGE_GRAPHICS_ADAPTER=WARP`、D3D12 のエラーはすべて 0)
| 走り | 値 |
|---|---|
| Vehicle キー(`verify-thirdperson-character --template Vehicle`) | 52.014 m、曲がり 30.5°(窓 30〜34) |
| Vehicle 右トリガー 0.5 | 23.172 m(キーの 0.45 倍) |
| Vehicle 左スティック 0.5 | 9.4°(キーの 0.31 倍。舵角は速さの曲線と入力の速さ(二乗)を通る) |
| カメラの切り替え(Tab) | 前のカメラ (44.50, 1.29, −0.36) へ切り替わる |
| リセット(Backspace) | 車が持ち上がって止まる |
| 見回し | 腕が回り、離すと 0 へ戻る |
| ブレーキランプ | 減速中に車体の後ろのランプが赤く光る(`.artifacts/m0109/brake/` の画像。アイドルの 4 との比較) |
| ヘッドライト(L) | 1 回で左の光が前の地面を照らし、2 回で消える(`.artifacts/m0109/headlights2/`)。エラー 0 |
| ThirdPerson / FirstPerson / TopDown | 15.700 m / 16.000 m / 9.606 m・(6.79, 6.84)。修正前と同じ |

## 範囲外(未実装)と不確実性
| 行(UE-Template-Behavior.md) | 状態 | 内容 |
|---|---|---|
| 車体の剛体・点の力・減衰 | 実装 | 慣性は箱の値(UE は凸包の物理アセット) |
| 車輪の光線のサスペンション | 一部 | UE の既定の制約のサスペンション(ソルバーの中のばねとハードストップ)ではなく、力のサスペンション(UE の別の経路)。縮みきりは車体の箱が支える |
| タイヤの摩擦・路面の摩擦 | 実装 | UE の Slippery と NonSlippery は同じ摩擦 1.0 なので、見た目の差は無い |
| エンジン・変速・操舵・空気・ブレーキ | 実装 | UE の数値との一致は中程度(UE の制約のサスペンションでの荷重の配り方と、物理のソルバーの違い) |
| 空中・転倒・リセット | 実装 | |
| 車輪の見た目・ヘッドライト | 実装 | 右のヘッドライトは UE と同じく切り替わらない |
| ブレーキランプ | 実装(UE から逸脱) | UE では光らない(事実)。意図を採って光らせた。UE の発光 1 を 100 nits とするのは推測 |
| カメラ(前後、見回し、遅れ) | 実装 | 実機のマウスとゲームパッドは未検証 |
| リスポーン | 実装 | |
| エンジン音 | 未実装 | 音の仕組みにループと音程の変更が無い。UE のテンプレートにも音の資産は無い |
| `CtrlRig_SportsCar`(車輪の Control Rig) | 未実装 | 車輪の姿勢は `VehicleWheelPose` が直接書く(同じ見た目の結果) |
| 物理の既知の制限 | 記録 | `ContinuousStatic` の CCD は静的な物体を外接箱で扱う(車には使わない)。箱以外の形とメッシュの接触は 1 点のまま |

## 完了の判定
- 全テスト `dotnet test CSharpGameEngine.slnx -c Debug`: 22 プロジェクトで失敗 0(Windows.Tests 753 合格・2 スキップ、VSExtension.Tests 1197、CodeAnalysis.Tests 338、FeatureRunner.Tests 222、SceneModel.Tests 212、Core.Tests 154、Numerics.Tests 32)。
- 最初の全テストで 2 つの欠陥が見つかり、根本から直した: `Vehicle` の参照型の欄によるスナップショットの型の初期化の例外(決定 6)と、`SpotLight` に足した `bool Visible` による VS 拡張の光の型の変換の失敗(決定 9。`LightVisibility` に移した)。
- `verify-thirdperson-character`(ThirdPerson・FirstPerson・TopDown・Vehicle)と、SDK 0.1.20 のパッケージから作ったゲームの `verify-template-package`(ThirdPerson・TopDown・Vehicle)が通る。最初のパッケージ検証は `.artifacts/packages` の最新の 0.1.19 を使って失敗した(`Pad.Back` が無い)ので、`dotnet pack packaging/CSharpGameEngine.Sdk -c Release` で 0.1.20 を作ってやり直した。
- 車の振る舞いの範囲の決め方: UE の式と値から見積もった物理の量(最高速は空気抵抗とエンジンの釣り合い、ブレーキの距離はタイヤの摩擦の上限など)。UE を同じ入力で走らせた数値との比較はしていない。
