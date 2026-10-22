# UE5 テンプレートの挙動仕様(解析の結果)

ADR-0078 / M0102。UE5.7 の Blueprint 版テンプレートを UE5.8 で開き、テキスト書き出し(T3D)からコードレベルで読んだ結果。

- 出典の作り方:
  - `tools/UnrealExport/run_dump_blueprint_logic.cmd`(T3D、生成クラス、CDO、コンポーネントの全属性)で書き出す。
  - それを `CSharpGameEngine.Unreal.SceneImporter logic <dir>`(疑似コードの `.logic.txt`)にかける。
  - 出力は `.artifacts/UnrealExport/logic/<Template>/`。
- 単位: UE の値は cm・cm/s・度のまま書く(エンジンは m・turns)。
- 「状態」の列: **済** = 実装済み、**一部**、**未** = 未実装、**対象外** = ADR-0078 §2 で作らないもの。「担当」は実装するマイルストーン。

## 共通(ThirdPerson・FirstPerson・TopDown の Character と ABP)

### 入力(`IMC_Default`、`IMC_MouseLook`、`IA_*`)
| 割り当て | 値 | 状態 | 担当 |
|---|---|---|---|
| `IA_Jump`(Pressed/Released トリガー): SpaceBar、Gamepad_FaceButton_Bottom | Started → `Jump()`、Completed → `StopJumping()` | 済(`StopJumping` は `JumpMaxHoldTime=0` なので効果なし) | — |
| `IA_Move`(Axis2D): W(Swizzle)、S(Swizzle+Negate)、A(Negate)、D、矢印キーも同じ、Gamepad_Left2D(DeadZone+Scalar) | Triggered → `Move(X, Y)` | 済 | — |
| `IA_Look`(Axis2D): Gamepad_Right2D(DeadZone) | Triggered → `Aim(X, Y)` | 済 | — |
| `IA_MouseLook`(Axis2D): Mouse2D(Negate Y) | Triggered → `Aim(X, Y)`。タッチのときは割り当てない | 済 | — |
| タッチ(`BPI_TouchInterface` の Primary/Secondary Thumbstick、Touch Jump Start/End) | 同じ関数へ | 対象外 | — |

### `BP_ThirdPersonCharacter`(TopDown の `BP_TopDownCharacter` は入力の処理を持たない)
| イベント | 処理 | 状態 |
|---|---|---|
| `Move(X, Y)` | `AddMovementInput(GetRightVector(Roll=制御.Roll, Pitch=0, Yaw=制御.Yaw), X)`、`AddMovementInput(GetForwardVector(0, 0, 制御.Yaw), Y)` | 済(`View.Planar`) |
| `Aim(X, Y)` | `AddControllerYawInput(X)`、`AddControllerPitchInput(Y)` | 済(`View.Turn`) |

### `ABP_Unarmed`(イベントグラフ)
| イベント | 処理 | 状態 | 担当 |
|---|---|---|---|
| BlueprintInitializeAnimation | `Character = cast<Character>(GetOwningActor())`、`MovementComponent` を保持 | 済(Parent で代用) | — |
| BlueprintUpdateAnimation(`Character` が有効なとき、順に 3 つ) | (0) `Velocity = 移動.Velocity`、`GroundSpeed = VSizeXY(Velocity)`、`Direction = bOrientRotationToMovement なら CalculateDirection(Velocity, 回転) をそのまま、そうでなければ ±45° に制限` | 一部(Direction 無し) | M0107 |
| | (1) `ShouldMove = GroundSpeed > 0.01 && GetCurrentAcceleration() ≠ 0` | 未(速度だけで判定) | M0107 |
| | (2) `IsFalling = 移動.IsFalling()` | 済 | — |

### `ABP_Unarmed`(アニメーショングラフ)
- 出力 ← ControlRig `CR_Mannequin_FootIK`(`ShouldDoIKTrace = !IsFalling`)← Slot `DefaultSlot` ← 状態機械 `Main States`
  - 足の IK: 未(M0107)。スロット: 対象外(モンタージュを再生しない)。
- 状態機械 `Main States`: 状態は Locomotion(状態機械 `Locomotion` を入れ子に持つ)、Jump、Fall Loop、Land。別名状態 `To Falling`(Locomotion と Land から)と `To Land`。入口は Locomotion。

  | 遷移 | 条件 | ブレンド | 状態 |
  |---|---|---|---|
  | To Falling → Jump | `IsFalling && Velocity(Z) > 100` | 0.15 s、Cubic | 一部(条件を確かめる) |
  | To Falling → Fall Loop(優先 2) | `IsFalling` | 0.4 s | 一部 |
  | Jump → Fall Loop | Jump の再生の終わりで自動 | 0.3 s | 一部 |
  | To Land → Land | `!IsFalling` | 0.1 s | 一部 |
  | Land → Locomotion | Land の再生の終わりで自動、または `ShouldMove` | 0.4 s / 0.5 s | 一部 |

- 状態機械 `Locomotion`:
  - 状態: Idle(`MM_Idle`)と Walk / Run(ブレンドスペース `BS_Idle_Walk_Run`、X=`Direction`、Y=`GroundSpeed`、同期グループ Locomotion)。
  - 遷移: Idle → Walk/Run は `ShouldMove`、逆は `!ShouldMove`(0.3 s)。
  - 状態: 一部(M0107)。今は Walk と Jog の 2 本を速度で混ぜるだけで、方向が無い。
- `BS_Idle_Walk_Run`:
  - X(方向)は −180〜180°、Y(速度)は 0 / 300 / 600。
  - 300 に Walk の 8 方向、600 に Jog の 8 方向を置く。0 は Idle。
  - 補間の速さは毎秒 5。
  - M0107 で全部の素材を取り込む。

## ThirdPerson
| 対象 | 内容(値) | 状態 | 担当 |
|---|---|---|---|
| `BP_ThirdPersonPlayerController` の BeginPlay | `DelayUntilNextTick` のあと、ローカルのときだけ `IMC_Default` を足し、タッチでなければ `IMC_MouseLook` を足す | 済 | — |
| Character の値 | 腕 400、Pawn の回転を使う、衝突あり、腕の位置 z 8.49。歩行 600、ジャンプ 500、加速 1500、制動 2000 / 空中 1500、空中制御 0.35、旋回 500°/s、移動の向きを向く | 済 | — |

## FirstPerson
| 対象 | 内容(値) | 状態 | 担当 |
|---|---|---|---|
| `BP_FirstPersonCharacter.Move` | 制御の向きではなく、Actor の右と前(`GetActorRightVector`、`GetActorForwardVector`)。体の Yaw は制御に従う(`bUseControllerRotationYaw`) | 済(結果は同じ) | — |
| 移動の値 | 歩行 600、ジャンプ 420、加速と制動 2048、空中の制動 1500、空中制御 0.6、旋回 360°/s、カプセル 34 / 96、段差 45、歩ける斜面 44.77° | 済 | — |
| `BP_FirstPersonCameraManager` | `ViewPitchMin=-70`、`ViewPitchMax=80`、`DefaultFOV=90` | 未(既定の ±89° のまま) | M0107 |
| カメラ | `bUsePawnControlRotation`。一人称の FOV 70 と一人称の縮尺 0.6 を使う(腕だけをこの FOV と縮尺で描く)。位置 (2.79, 5.88, 0)、回転 (Yaw 90, Roll −90)。ボーンに付く(推測: 頭) | 一部(目の高さと FOV 90 だけ) | M0107 |
| `FirstPersonMesh` | Manny、`ABP_FP_Copy`、持ち主にだけ見える、影を落とさない、一人称の描画 | 未 | M0107 |
| `CharacterMesh0` | Manny、`ABP_Unarmed`、位置 (−20, 0, −96)、Yaw −90、ワールドの描画(持ち主には見えないが影は落とす) | 未 | M0107 |
| `ABP_FP_Copy` | 体のポーズをコピー(`CopyPoseFromMesh`、親のメッシュ)→ ControlRig `CtrlRig_FPWarp` | 未 | M0107 |
| `CtrlRig_FPWarp` | spine_01〜spine_04 の FABRIK で背骨を後ろへ引いて頭をカメラに合わせ、upperarm_l / upperarm_r を前へ押す | 未 | M0107 |
| Controller | ThirdPerson と同じ(`DelayUntilNextTick` が無い) | 済 | — |

## TopDown
| 対象 | 内容(値) | 状態 | 担当 |
|---|---|---|---|
| `IMC_Default` | `IA_SetDestination_Click` ← LeftMouseButton、`IA_SetDestination_Touch` ← Touch1 | クリックは済、タッチは対象外 | — |
| Controller の既定値 | `PressedThreshold=0.5` s、`FXCursor=FX_Cursor_Success`、カーソルを表示、クリックのイベントを有効にする | 一部 | M0108 |
| `IA_SetDestination_Click` Started | `StopMovement()`。カーソルの下に当たりがあれば `CachedDestination = 当たりの位置` | 未(押したら止まる処理が無い) | M0108 |
| 同 Triggered(押している間) | 当たりがあれば `CachedDestination` を更新し、`Follow(位置)` = `AddMovementInput(GetDirectionUnitVector(Pawn の位置, 位置), 1.0)` | 一部(今は `MoveTo` で目的地へ歩く) | M0108 |
| 同 Canceled / Completed(離した) | 押していた時間 `ElapsedSeconds < PressedThreshold` なら `MoveTo(CachedDestination)` = `SimpleMoveToLocation`(ナビメッシュの経路)+ `SpawnSystemAtLocation(FXCursor, 位置)` | 一部(目印は `Spawner.Spawn<CursorMarker>` で出す。M0105 済。ただし押した瞬間に出す。経路と短押しの判定は未) | M0106・M0108 |
| `GetLocationUnderCursor` | `GetHitResultUnderCursorByChannel(Visibility, bTraceComplex=true)` の `bBlockingHit` と `Location` | 済(`Picking.TryPick`、三角形メッシュへの正確な光線) | — |
| ReceivePossess と OnDestroyed | Pawn のクラスを覚え、壊れたら全 PlayerStart から無作為に 1 つを選んで生成し、Possess する | 未 | M0107 |
| Character の値 | 腕 1400、Pitch −50 / Yaw 45(絶対回転)、衝突なし、カメラの遅れ追従(速さ 3)、FOV 55。歩行 600、加速と制動 1000、重力 1.5、空中制御 0.05、旋回 640°/s、カプセル 42 / 96 | 一部(遅れ追従が無い) | M0107 |
| 経路の追従 | `FixedPathBrakingDistance=200`、`bUseFixedBrakingDistanceForPaths`、`bUseAccelerationForPaths` | 未 | M0106 |
| `FX_Cursor_Success` | Niagara の `SimpleSpriteBurst`: Spawn Burst Instantaneous で Spawn Count 1・Spawn Time 0、Loop Duration 0.7 s、Loop Count 1。粒子の Lifetime 0.7 s、色 (1,1,1,1)、位置のずれ・速度・回転の力は 0。描画はメッシュ `SM_CursorMesh`(材質 `M_Cursor`: BLEND_Masked、基本色 = `Color` × 矢印のテクスチャ、粗さ 1、`TexCoordY` ← `NormalizedLoopAge`) | 一部(`ParticleBurst` の Count 1・Lifetime 0.7、`SM_CursorMesh` の矢印をアルファの切り抜きから形状にした glb。`TexCoordY` の流れは縮み 1→0.25 で代用。押した瞬間に出す)。M0105。`SpawnSystemAtLocation(FXCursor, 位置)` は `Spawner.Spawn<CursorMarker>` | M0105 済。短押しで出す条件は M0108 |

## Vehicle(既定の Pawn は `BP_VehicleAdvSportsCar`、親は `BP_VehicleAdvPawnBase`)

### 入力(`IMC_Vehicle_Default`、`IMC_Vehicle_MouseLook`)
| アクション(型) | キー | 処理 | 状態 | 担当 |
|---|---|---|---|---|
| `IA_Throttle`(Axis1D) | W、Up、Gamepad_FaceButton_Left、Gamepad_RightTriggerAxis | Triggered/Completed → `SetThrottleInput(値)` | 一部(デジタルだけ) | M0108・M0109 |
| `IA_Brake`(Axis1D、Pressed/Released) | S、Down、Gamepad_FaceButton_Bottom、Gamepad_LeftTriggerAxis | Triggered → `SetBrakeInput(値)`、Started → `BrakeLights(true)`、Completed → `SetBrakeInput`・`BrakeLights(false)` | 一部 | M0109 |
| `IA_Steering`(Axis1D) | D、A(Negate)、Right、Left(Negate)、Gamepad_LeftX | → `SetSteeringInput(値)` | 済 | M0109 |
| `IA_Handbrake` | SpaceBar、Gamepad_LeftShoulder | Triggered → `SetHandbrakeInput(true)`・`BrakeLights(true)`、Completed → false | 一部(ブレーキランプ無し) | M0109 |
| `IA_ToggleCamera` | Tab、Gamepad_Special_Left | 後ろのカメラを止め、FlipFlop で前と後ろのカメラを交互に有効にする | 未 | M0109 |
| `IA_Reset` | BackSpace、Gamepad_Special_Right | リセット(下記) | 未 | M0109 |
| `IA_LookAround`(Axis1D) | Gamepad_RightX、MouseX(`IMC_Vehicle_MouseLook`) | 後ろの腕を Yaw に `値` だけ回す | 未 | M0109 |
| `IA_Headlights` | L | FlipFlop でヘッドライトの表示を切り替える | 未 | M0109 |
| ハンドルコントローラー(`IMC_SteeringWheel`)、XR、タッチ | — | — | 対象外 | — |

### `BP_VehicleAdvPawnBase`
| イベント | 処理(値) | 状態 | 担当 |
|---|---|---|---|
| Tick | `Mesh.SetAngularDamping(接地なら 0、空中なら 3)`。`InterpsToOriginalRotation`: 後ろの腕の Yaw を `FInterpTo(現在, 0, Δt, 速さ 1)` で前へ戻す | 未 | M0109 |
| BeginPlay | 移動コンポーネントを `ChaosWheeledVehicleMovementComponent` として保持。`Flipped Check` を 3 秒ごとに繰り返す | 未 | M0109 |
| `Flipped Check` | `Dot(Mesh の上, (0,0,1)) < −0.2` が 2 回続けて真なら `Manual Reset`。真でなければ記録を消す | 未 | M0109 |
| `Manual Reset` / `IA_Reset` | `位置 + (0,0,50)`、Roll と Pitch を 0、Yaw はそのまま、テレポート。角速度と速度を 0 | 未 | M0109 |
| `BrakeLights(Is Braking)` | (SportsCar で上書き) | — | — |

### `BP_VehicleAdvSportsCar`
| 対象 | 内容(値) | 状態 | 担当 |
|---|---|---|---|
| Construction Script | ボディのマテリアル 0 番から動的マテリアルを作る | 未 | M0109 |
| `BrakeLights` | `BrakeLightPower = ブレーキ中 200 / 通常 4` | 未 | M0109 |
| `IA_Headlights` | FlipFlop で `Headlight_L` の表示を切り替える(右は子として付く) | 未 | M0109 |
| ヘッドライト | スポット、3000 lm、内 22.49° / 外 50.30°、減衰半径 16384、左 (200, −66.8, 65.1) Yaw −30、右 (200, 72.0, 65.1) Yaw 40、最初は非表示 | 未 | M0109 |
| 後ろのカメラ | 腕 381.02、ソケット z 71.80、FOV 110(Pawn 基底の 650 / 150 / 90 を上書き)、遅れ 10(最大 50)、回転の遅れ 2、Yaw だけを継ぐ | 一部(6.5 m・FOV 90) | M0109 |
| 前のカメラ | 腕 0、位置 (30, 0, 120)、遅れなし | 未 | M0109 |
| 車輪のメッシュ | 前左・後左は Yaw −90、前右・後右は Yaw 90。骨 `Phys_Wheel_*` に付く | 済(静止) | M0109(回転・ステア) |
| `ABP_SportsCar` | MeshRefPose → WheelController(車輪の回転・ステア・サスペンション)→ 部品空間 → ControlRig `CtrlRig_SportsCar` | 未 | M0109 |

### Chaos の移動(`VehicleMovementComp`、`SportsCar_Wheels*`)
| 項目 | 値 | 状態 | 担当 |
|---|---|---|---|
| 車体 | 質量 1500 kg、空気抵抗 0.31、ダウンフォース 0.3、車体の幅 180・高さ 144、慣性の倍率 1、重心の上書きなし | 未(簡易モデル) | M0109 |
| エンジン | 最大トルク 750、最大回転 7000、アイドル 900、エンジンブレーキ 0.2、回転の上がり MOI 5、下がり 600/s。トルク曲線(回転→比): 1000→0.5、1800→0.9、3500→1.0、6000→0.8、7000→0 | 未 | M0109 |
| 変速 | 自動、自動後退、最終比 2.81、前進 4.25 / 2.52 / 1.66 / 1.22 / 1.00、後退 4.04、上げ 6000 / 下げ 2000、変速時間 0.2 s、効率 0.9 | 未 | M0109 |
| 操舵 | アッカーマン、角度比 0.7、速度による曲線: 0 km/h→1.0、20→0.8、60→0.4、120→0.3 | 未 | M0109 |
| 差動 | 後輪駆動(前後の配分 0.5) | 未 | M0109 |
| 入力の変化の速さ | アクセル・ブレーキは上がり 6、下がり 10(線形)。ハンドルは上がり 2.5、下がり 5(二乗)。ハンドブレーキは 12 / 12 | 未 | M0109 |
| 前輪 | 半径 39、幅 35、質量 20、コーナリング剛性 1000、摩擦の倍率 3、滑りのしきい値 20、スキッドのしきい値 20、最大舵角 40°、ブレーキトルク 4500、ハンドブレーキは効かない、駆動しない | 未 | M0109 |
| 後輪 | 半径 40、幅 40、摩擦の倍率 4、しきい値 100、舵角 0、ブレーキトルク 1500、ハンドブレーキ 6000、駆動する | 未 | M0109 |
| サスペンション(前後同じ) | 上下の最大 10、減衰比 0.5、ばね 250、予圧 50、荷重比 0.5、ロールバー 0.15、光線で測る | 未 | M0109 |
| その他 | 停止のしきい値 10、逆向きのしきい値 100、眠りのしきい値 10、眠る斜面 0.866、アイドルのブレーキ 0 | 未 | M0109 |

### Controller と HUD
| 対象 | 内容 | 状態 | 担当 |
|---|---|---|---|
| BeginPlay | ローカルのときだけ `IMC_Vehicle_Default`(優先 0)を足す。ハンドル用はオプション。タッチでなければ `IMC_Vehicle_MouseLook` を足す。`UI_Vehicle`(`bIsMPH=false`)を作って画面に足す | 一部 | M0104 |
| Tick | 移動コンポーネントが有効なら、`UpdateSpeed(GetForwardSpeed())`、`UpdateGear(GetCurrentGear())` | 一部(タイトルバー) | M0104 |
| `UI_Vehicle` | 単位の表示は km/h か MPH。速度は `|速度 cm/s| × (MPH なら 0.022、km/h なら 0.036)` を整数 3 桁で表示する。ギアは −1→R、0→N、それ以外は数字 | 一部 | M0104 |
| ReceivePossess と On Pawn Destroyed | 移動コンポーネントを保持する。壊れたら最初の PlayerStart に同じクラスを生成して Possess する | 未 | M0107 |

## レベル(4 つ共通)
| 対象 | 内容 | 状態 | 担当 |
|---|---|---|---|
| ライティング | Lumen の GI と反射、DirectionalLight(可動)、SkyLight(リアルタイムキャプチャ)、SkyAtmosphere、VolumetricCloud、ExponentialHeightFog、空の球、PostProcessVolume(ThirdPerson と Vehicle) | 一部(雲・空の球・Lumen 相当が無い) | M0110・M0111 |
| Vehicle の地面 | Landscape(実行時の仮想テクスチャ、Ground/Road の層、物理マテリアルの Slippery / NonSlippery)、Landscape のスプラインの道 | 一部(平らな板) | M0109・M0112 |
| TopDown のナビメッシュ | NavMeshBoundsVolume、RecastNavMesh | 未 | M0106 |
