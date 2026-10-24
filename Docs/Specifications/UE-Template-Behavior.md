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
| BlueprintUpdateAnimation(`Character` が有効なとき、順に 3 つ) | (0) `Velocity = 移動.Velocity`、`GroundSpeed = VSizeXY(Velocity)`、`Direction = bOrientRotationToMovement なら CalculateDirection(Velocity, 回転) を ±45° に制限、そうでなければそのまま`(グラフの `select` は Index=`bOrientRotationToMovement`、真で Clamp) | 実装済み(`LocomotionObserverTests.CalculateDirection_IgnoresTheLengthsOfItsInputsAndRefusesAZeroFacing`、`Observe_TurningTowardTheMovementLimitsTheDirectionButStrafingDoesNot`、`Observe_UsesTheBodysRotationNotTheWorldAxes`、`MannequinAssetTests.TheDirectionalClipsTravelTheWayTheyAreNamed`。符号はクリップの進む向きを測って決めた) | M0107 |
| | (1) `ShouldMove = GroundSpeed > 0.01 && GetCurrentAcceleration() ≠ 0` | 実装済み(`LocomotionObserverTests.Observe_MovingAndPushedShouldMove`、`Observe_CoastingOrStandingStillShouldNotMove`。加速度は `MoveIntent.Direction ≠ 0` で代える) | M0107 |
| | (2) `IsFalling = 移動.IsFalling()` | 済 | — |

### `ABP_Unarmed`(アニメーショングラフ)
- 出力 ← ControlRig `CR_Mannequin_FootIK`(`ShouldDoIKTrace = !IsFalling`)← Slot `DefaultSlot` ← 状態機械 `Main States`
  - 足の IK: 実装済み(M0107。`FootIkTests`、`LocomotionObserverTests.GroundHeight_*`、`MannequinAssetTests.TheFootIkOnTheRealMannequinLowersThePelvisAndKeepsTheFeetOnTheirGround`)。UE の PBIK と球のトレースの代わりに、解析的な 2 ボーン IK と光線(差は M0107 の文書)。スロット: 対象外(モンタージュを再生しない)。
- 状態機械 `Main States`: 状態は Locomotion(状態機械 `Locomotion` を入れ子に持つ)、Jump、Fall Loop、Land。別名状態 `To Falling`(Locomotion と Land から)と `To Land`。入口は Locomotion。

  | 遷移 | 条件 | ブレンド | 状態 |
  |---|---|---|---|
  | To Falling → Jump | `IsFalling && Velocity(Z) > 100` | 0.15 s、Cubic | 実装済み(`LocomotionGraphTests.Jump_CrossFadesInOverTheirTimesAndFallsWhenThreeTenthsOfASecondRemain`) |
  | To Falling → Fall Loop(優先 2) | `IsFalling` | 0.4 s | 実装済み(`LocomotionGraphTests.FallLoop_UsesTheFourTenthsSecondFade`) |
  | Jump → Fall Loop | Jump の再生の終わりで自動 | 0.3 s | 実装済み(クリップの残り 0.3 s で。`Jump_CrossFadesInOverTheirTimesAndFallsWhenThreeTenthsOfASecondRemain`) |
  | To Land → Land | `!IsFalling` | 0.1 s | 実装済み(`LocomotionGraphTests.Landing_GoesToLandThenBackToLocomotionWhenFourTenthsRemainOrTheCharacterShouldMove`、`Land_ToFallingAgainStartsJumpOrFallFromThere`) |
  | Land → Locomotion | Land の再生の終わりで自動、または `ShouldMove` | 0.4 s / 0.5 s | 実装済み(同上。残り 0.4 s で 0.4 s、`ShouldMove` で 0.5 s) |
  | 遷移の途中で別の遷移 | 画面にあったポーズから新しいブレンドを始める | | 実装済み(`LocomotionGraphTests.AnInterruptedFadeContinuesFromWhatWasOnScreen`) |

- 状態機械 `Locomotion`:
  - 状態: Idle(`MM_Idle`)と Walk / Run(ブレンドスペース `BS_Idle_Walk_Run`、X=`Direction`、Y=`GroundSpeed`、同期グループ Locomotion)。
  - 遷移: Idle → Walk/Run は `ShouldMove`(0.2 s)、逆は `!ShouldMove`(0.3 s)。
  - 状態: 実装済み(M0107。`LocomotionGraphTests.LocomotionMoveStates_FollowShouldMoveNotTheSpeed`、`IdleToMove_UsesTheTwoTenthsSecondCubicCrossFade`)。
- `BS_Idle_Walk_Run`:
  - X(方向)は −180〜180°、Y(速度)は 0 / 300 / 600。
  - 300 に Walk の 8 方向、600 に Jog の 8 方向を置く。0 は Idle。
  - 補間の速さは毎秒 5。
  - 実装済み(M0107。`BlendSpace2D`: `LocomotionGraphTests.BlendSpace_*`、`TheBlendSpacePhaseRunsAtTheWeightedLengthOfItsClips`、`Locomotion_UnnamedDirectionsUseTheForwardClip`)。16 本のクリップは 3 つのテンプレートの `Assets/Characters/Mannequins/Anims/` に取り込んだ(`tools/UnrealExport/export_mannequin_animations.py`)。ブレンドスペースの Y=0 の行は Idle のクリップで、状態機械の Idle 状態とは別に持つ(ABP の構造のとおり)。

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
| `BP_FirstPersonCameraManager` | `ViewPitchMin=-70`、`ViewPitchMax=80`、`DefaultFOV=90` | 実装済み(`TemplateCharacterSceneTests.TheFirstPersonCameraManagerLimitsThePitchToMinus70AndPlus80Degrees`) | M0107 |
| カメラ | `bUsePawnControlRotation`。一人称の FOV 70 と一人称の縮尺 0.6 を使う(腕だけをこの FOV と縮尺で描く)。位置 (2.79, 5.88, 0)、回転 (Yaw 90, Roll −90)。ボーンに付く(推測: 頭。目の位置の計算は頭のボーンの座標系を前提にした) | 実装済み(`TemplateCharacterSceneTests.TheFirstPersonEyeIsWhereTheRigsHeadAndTheCameraOffsetPutIt`、`FirstPersonViewWarpTests`)。目は頭のボーンでなく、体に対して固定の位置(`Ctrl_Head` が頭を固定するので) | M0107 |
| `FirstPersonMesh` | Manny、`ABP_FP_Copy`、持ち主にだけ見える、影を落とさない、一人称の描画 | 実装済み(`TemplateCharacterSceneTests.TheFirstPersonBodyIsHiddenFromTheCameraButCastsItsShadowAndTheArmsCastNone`、`RenderServerVisibilityTests`、`FirstPersonViewWarpTests`)。「持ち主にだけ」はカメラに見える・影を落とすの 2 つのフラグで表す | M0107 |
| `CharacterMesh0` | Manny、`ABP_Unarmed`、位置 (−20, 0, −96)、Yaw −90、ワールドの描画(持ち主には見えないが影は落とす) | 実装済み(同上、`EveryTemplateCharacterHasTheEightDirectionsOfWalkingAndJoggingAndAFootIk`) | M0107 |
| `ABP_FP_Copy` | 体のポーズをコピー(`CopyPoseFromMesh`、親のメッシュ)→ ControlRig `CtrlRig_FPWarp` | 実装済み(`FirstPersonPoseUpdate`。`MannequinAssetTests.TheFirstPersonWarpPutsTheHeadAndUpperArmsOnTheirControlsAndTheHandsFollow`) | M0107 |
| `CtrlRig_FPWarp` | spine_01〜spine_04 の FABRIK で背骨を後ろへ引いて頭をカメラに合わせ、upperarm_l / upperarm_r を前へ押す | 実装済み(`MannequinAssetTests.TheFirstPersonWarpPullsTheSpineTowardTheBodyOffsetAtHalfWeightKeepingItsLengths`、`TheFirstPersonWarpAtFullWeightReachesAReachableOffsetAndStretchesTowardAFarOne`、`TheFirstPersonWarpRefusesASkeletonItCannotWarp`)。コントロールの値は UE のプローブで読んだ(`BodyOffset` 以外は基準姿勢の骨と一致) | M0107 |
| Controller | ThirdPerson と同じ(`DelayUntilNextTick` が無い) | 済 | — |

## TopDown
| 対象 | 内容(値) | 状態 | 担当 |
|---|---|---|---|
| `IMC_Default` | `IA_SetDestination_Click` ← LeftMouseButton、`IA_SetDestination_Touch` ← Touch1 | クリックは済、タッチは対象外 | — |
| Controller の既定値 | `PressedThreshold=0.5` s、`FXCursor=FX_Cursor_Success`、カーソルを表示、クリックのイベントを有効にする | 実装済み(`ClickToMove.PressedThreshold` = 0.5 s。`Button.ReleasedBefore`、`InputActionBindingTests.AHoldOfExactlyTheThreshold…`、`InputActionValueTypesTests.ARelease_IsShortOnlyWhen…`。`FXCursor` は `CursorMarker`、カーソルは表示のまま(TopDown は `CaptureMouse` なし)、クリックのイベントは `Picking.TryPick`) | M0108 |
| `IA_SetDestination_Click` Started | `StopMovement()`。カーソルの下に当たりがあれば `CachedDestination = 当たりの位置` | 実装済み(`ClickToMove` が `Button.Pressed` で `MoveIntent.Stop()` を呼び、`ClickDestination`(`CachedDestination`)に当たりを覚える。WARP の実走 `CGE-TOPDOWN-CLICK` / `-LONG-PRESS`)。当たりが無いときは覚え直さない | M0108 |
| 同 Triggered(押している間) | 当たりがあれば `CachedDestination` を更新し、`Follow(位置)` = `AddMovementInput(GetDirectionUnitVector(Pawn の位置, 位置), 1.0)` | 実装済み(`Button.Held` の間、当たりがあれば `ClickDestination` を更新し、`MoveIntent.Direction` に Pawn の位置から当たりへの 3 次元の単位ベクトルを直接書く。ナビメッシュは使わない。当たりが無いフレームは入力 0。実走 `CGE-TOPDOWN-LONG-PRESS`: 押している間に 17.414 m 歩き続ける) | M0108 |
| 同 Canceled / Completed(離した) | 押していた時間 `ElapsedSeconds < PressedThreshold` なら `MoveTo(CachedDestination)` = `SimpleMoveToLocation`(ナビメッシュの経路)+ `SpawnSystemAtLocation(FXCursor, 位置)` | 実装済み(`Button.ReleasedBefore(0.5)` が真のとき、覚えた位置へ `MoveIntent.MoveTo`(M0106: Navigation プラグインのナビメッシュの経路)と `Spawner.Spawn<CursorMarker>`(M0105)。偽(長押し)なら `MoveIntent.Stop()` だけ。目印は短押しの離したフレームに出て、押した瞬間には出ない: `ClickToMoveVerification` が目印の出るフレームを判定する) | M0106 済(経路)・M0108 済(短押し) |
| `GetLocationUnderCursor` | `GetHitResultUnderCursorByChannel(Visibility, bTraceComplex=true)` の `bBlockingHit` と `Location` | 済(`Picking.TryPick`、三角形メッシュへの正確な光線) | — |
| ReceivePossess と OnDestroyed | Pawn のクラスを覚え、壊れたら全 PlayerStart から無作為に 1 つを選んで生成し、Possess する | 実装済み(`RespawnTests`、`RenderRosterTests.ASkeletalMeshSpawnedAfterTheLevelLoadedIsDrawnAndItsMeshesAreReusedByTheNextOne`。テンプレートの `RespawnPlayer`。Possess は、新しい Pawn が入力を受けてカメラが引き継ぐことで表す。TopDown のレベルの PlayerStart は 1 つ) | M0107 |
| Character の値 | 腕 1400、Pitch −50 / Yaw 45(絶対回転)、衝突なし、カメラの遅れ追従(速さ 3)、FOV 55。歩行 600、加速と制動 1000、重力 1.5、空中制御 0.05、旋回 640°/s、カプセル 42 / 96 | 実装済み(`CameraLagTests`、`TemplateCharacterSceneTests.TheTopDownCameraLagsAtSpeed3AndThePlaneConstraintHasUnrealsZeroNormal`) | M0107 |
| 平面への拘束 | `bConstrainToPlane`、`bSnapToPlaneAtStart`。`PlaneConstraintNormal` は (0, 0, 0)(UE の `ConstrainDirectionToPlane` は法線との内積を引くので、法線 0 は何も拘束しない) | 実装済み(`CharacterPlaneConstraintTests`。TopDown のシーンは UE と同じ値で拘束にならない) | M0107 |
| `KillZ`(ThirdPerson・FirstPerson・TopDown・Vehicle 共通。PlayerStart はどのレベルも 1 つ) | `WorldSettings.KillZ` = −1048575 cm(UE の既定。Vehicle は −5000 cm)。`bEnableWorldBoundsChecks`。下回った Pawn は壊れる | 実装済み(`RespawnTests.APawnBelowItsKillZIsDestroyedAtTheNextTick`、`TheDefaultKillZIsUnrealsDefaultOfMinus1048575Centimetres`、`KillZOnTheRootOfASceneDestroysTheWholeScene`、`EveryTemplatePawnCarriesUnrealsDefaultKillZ`) | M0107 |
| 経路の追従 | `FixedPathBrakingDistance=200`、`bUseFixedBrakingDistanceForPaths`、`bUseAccelerationForPaths` | 一部(経路の最後の点で、キャラクターの制動から計算した距離で減速して止まる。途中の点は最高速度で通り、`NavAgent.WaypointRadius`(0.3 m)に入ったら次へ。固定の制動距離 200 cm と、経路の追従で加速度を使うかの区別は無い)。目的地に着けないときは、UE の `SimpleMoveToLocation`(部分経路を許す)と同じく、行ける中で一番近い点まで歩いて止まる | M0106 済 |
| `FX_Cursor_Success` | Niagara の `SimpleSpriteBurst`: Spawn Burst Instantaneous で Spawn Count 1・Spawn Time 0、Loop Duration 0.7 s、Loop Count 1。粒子の Lifetime 0.7 s、色 (1,1,1,1)、位置のずれ・速度・回転の力は 0。描画はメッシュ `SM_CursorMesh`(材質 `M_Cursor`: BLEND_Masked、基本色 = `Color` × 矢印のテクスチャ、粗さ 1、`TexCoordY` ← `NormalizedLoopAge`) | 一部(`ParticleBurst` の Count 1・Lifetime 0.7、`SM_CursorMesh` の矢印をアルファの切り抜きから形状にした glb。`TexCoordY` の流れは縮み 1→0.25 で代用。短押しの離した瞬間に出す。M0108 で押した瞬間から変えた)。M0105。`SpawnSystemAtLocation(FXCursor, 位置)` は `Spawner.Spawn<CursorMarker>` | M0105 済。短押しで出す条件は M0108 済 |

## Vehicle(既定の Pawn は `BP_VehicleAdvSportsCar`、親は `BP_VehicleAdvPawnBase`)
M0109 で Chaos 相当に作り直した(Vehicles プラグイン。[M0109](../Milestones/M0109-Chaos-Vehicle.md))。式は UE 5.7 の ChaosVehiclesCore と ChaosVehicles プラグインのソース(`C:\Program Files\Epic Games\UE_5.7\Engine\...`)から、値は T3D・コンポーネントの書き出しと `tools/UnrealExport/probe_vehicle_physics.py`・`probe_vehicle_brake_lights.py`(UE を起動して読んだ値)から取った。試験は `tests/CSharpGameEngine.Windows.Tests/Character/ChaosVehicleTests.cs`(以下 `ChaosVehicleTests.`)と `Physics/PhysicsForceCommandTests.cs`。

### 入力(`IMC_Vehicle_Default`、`IMC_Vehicle_MouseLook`)
| アクション(型) | キー | 処理 | 状態 | 担当 |
|---|---|---|---|---|
| `IA_Throttle`(Axis1D) | W、Up、Gamepad_FaceButton_Left、Gamepad_RightTriggerAxis | Triggered/Completed → `SetThrottleInput(値)` | 実装済み(M0108 のアナログの `PlayerActions.Throttle` を、M0109 で別の入力 `VehicleIntent.Throttle` に渡す。`ChaosVehicleTests.FullThrottleReachesOneHundredKilometresPerHourInAboutFourSecondsAndChangesGearAtSixThousand`、実走 `CGE-VEHICLE-DRIVE-ANALOG-THROTTLE`) | M0108・M0109 済 |
| `IA_Brake`(Axis1D、Pressed/Released) | S、Down、Gamepad_FaceButton_Bottom、Gamepad_LeftTriggerAxis | Triggered → `SetBrakeInput(値)`、Started → `BrakeLights(true)`、Completed → `SetBrakeInput`・`BrakeLights(false)` | 実装済み(`VehicleIntent.Brake`。止まると後退(`ChaosVehicleTests.HoldingTheBrakeAfterStoppingReversesInReverseGear`)。ブレーキランプはテンプレートの `BrakeLightControl`(0 からの踏み込みで点け、0 へ戻ると消す)。実走の画像 `.artifacts/m0109/brake/rear_zoom.png`) | M0109 済 |
| `IA_Steering`(Axis1D) | D、A(Negate)、Right、Left(Negate)、Gamepad_LeftX | → `SetSteeringInput(値)` | 実装済み(M0108 のアナログ。M0109 で舵角はアッカーマン: `ChaosVehicleTests.SteeringRightTurnsRightWithAckermannAnglesAndATightRadiusAtLowSpeed`、実走 `CGE-VEHICLE-DRIVE-ANALOG-STEERING`) | M0109 済 |
| `IA_Handbrake` | SpaceBar、Gamepad_LeftShoulder | Triggered → `SetHandbrakeInput(true)`・`BrakeLights(true)`、Completed → false | 実装済み(`VehicleIntent.Handbrake`。`ChaosVehicleTests.TheHandbrakeLocksTheRearWheelsSoTheCarSlidesSideways`。ブレーキランプは押している間点き、離すと消える。ブレーキを踏んだまま離すと消えるのも UE と同じ) | M0109 済 |
| `IA_ToggleCamera` | Tab、Gamepad_Special_Left | 後ろのカメラを止め、FlipFlop で前と後ろのカメラを交互に有効にする | 実装済み(テンプレートの `CameraToggle` が、マーカー `VehicleCameraSwitch` の付いた 2 台のカメラの `Camera.Active` を押すたびに反転する。描画は、表示中のカメラが無効になり別のカメラが有効になったら切り替える(`RuntimeSystems` の `LevelLoad`)。ゲームパッドは新しい `Pad.Back`。実走 `.artifacts/m0109/camera/`: Tab でボンネットの視点へ) | M0109 済 |
| `IA_Reset` | BackSpace、Gamepad_Special_Right | リセット(下記) | 実装済み(テンプレートの `VehicleControls` が `VehicleIntent.Reset()`。ゲームパッドは新しい `Pad.Start`。実走 `.artifacts/m0109/reset/`: 0.5 m 上がって止まる) | M0109 済 |
| `IA_LookAround`(Axis1D) | Gamepad_RightX、MouseX(`IMC_Vehicle_MouseLook`) | 後ろの腕を Yaw に `値` だけ回す | 実装済み(テンプレートの `LookAround` が `VehicleCameraLook.YawDegrees` に `値 × 360`(エンジンの視点の入力は回転数)を足し、`VehicleChaseView` が腕の Yaw を車の Yaw + その角にする。実走 `.artifacts/m0109/look/`: マウスの横の移動でカメラが回り、離すと戻る。Vehicle のウィンドウはマウスを捕捉しないので、実機のマウスの移動は届かない(自動実行の注入とゲームパッドは届く)) | M0109 済 |
| `IA_Headlights` | L | FlipFlop でヘッドライトの表示を切り替える | 実装済み(テンプレートの `HeadlightToggle` が 光の隣の `LightVisibility.Visible`(M0109 で足した)を反転する。実走 `.artifacts/m0109/headlights/`。下の「ヘッドライト」のとおり、UE でも左だけが切り替わる) | M0109 済 |
| ハンドルコントローラー(`IMC_SteeringWheel`)、XR、タッチ | — | — | 対象外 | — |

### `BP_VehicleAdvPawnBase`
| イベント | 処理(値) | 状態 | 担当 |
|---|---|---|---|
| Tick | `Mesh.SetAngularDamping(移動.IsMovingOnGround() なら 0、そうでなければ 3)`。`InterpsToOriginalRotation`: 後ろの腕の Yaw を `FInterpTo(現在, 0, Δt, 速さ 1)` で前へ戻す | 実装済み。**事実**: 車両の移動コンポーネントは `IsMovingOnGround` を上書きせず、`UNavMovementComponent` の既定(常に false)を返す(UE 5.7 の `NavMovementComponent.h`)。したがって UE の実際の角速度の減衰は、地上でも空中でも常に 3。エンジンもシーンの `RigidBody.AngularDamping` = 3 で同じにした(`PhysicsForceCommandTests.AngularDampingSlowsTheSpinByItsRateEachTickAndZeroKeepsIt`)。腕は `VehicleCameraLook.ReturnSpeed` = 1 の FInterpTo | M0109 済 |
| BeginPlay | 移動コンポーネントを `ChaosWheeledVehicleMovementComponent` として保持。`Flipped Check` を 3 秒ごとに繰り返す | 実装済み(`Vehicle.FlipCheckSeconds` = 3。固定 tick で 180 tick ごと) | M0109 済 |
| `Flipped Check` | `Dot(Mesh の上, (0,0,1)) < −0.2` が 2 回続けて真なら `Manual Reset`。真でなければ記録を消す(リセットでは消さない) | 実装済み(`ChaosVehicleTests.AnUpsideDownCarIsResetAfterTwoFlippedChecksThreeSecondsApart`) | M0109 済 |
| `Manual Reset` / `IA_Reset` | `位置 + (0,0,50)`、Roll と Pitch を 0、Yaw はそのまま、テレポート。角速度と速度を 0 | 実装済み(`ChaosVehicleTests.TheResetLiftsTheCarHalfAMetreUprightKeepsItsYawAndStopsIt`。UE の Actor の原点(メッシュの原点)を `Vehicle.ResetPivot` で表し、その点を持ち上げてその点のまわりに直す。物理の `Teleport` と新しい `SetVelocity`) | M0109 済 |
| `BrakeLights(Is Braking)` | (SportsCar で上書き) | — | — |

### `BP_VehicleAdvSportsCar`
| 対象 | 内容(値) | 状態 | 担当 |
|---|---|---|---|
| Construction Script | `Mesh`(骨格メッシュ `VehicleMesh`)のマテリアル 0 番から動的マテリアルを作る | 実装済み(実行時にマテリアルの発光を変える `MeshEmissive`(描画の物体ごとの倍率。描画契約は変えない))。**事実**: UE の `VehicleMesh` は 4 頂点の見えない骨格メッシュ(`visible=False`)で、マテリアル 0 番は `WorldGridMaterial`。見える車体(`Chasis_Main` の `M_SportsCarBase`)の発光は `EmissivePower × 宣言の無い Named Reroute` で 0。つまり UE のこのテンプレートではブレーキランプは光らない(`probe_vehicle_brake_lights.py`)。**決定**: 意図(ブレーキで光る)を採り、見える車体のテールランプを光らせる | M0109 済 |
| `BrakeLights` | `BrakeLightPower = ブレーキ中 200 / 通常 4` | 実装済み(テンプレートの `BrakeLights` と `BrakeLightControl` が `MeshEmissive.Scale` に BrakeLightPower を書く。車体の glb の発光は UE の `T_SportsCar_Lights_M` の赤のチャンネル(シャシーの UV 0 のテールランプの範囲)を 100 nits で入れたもの(`tools/UnrealExport/add_brake_light_emission.py`)。UE の値 1 を 100 nits とするのは推測(UE の発光の値は露出に依る相対値。昼の光でもブレーキで見えるように選んだ)。実走の画像 `.artifacts/m0109/brake/rear_zoom.png` と `idle/rear_zoom.png`) | M0109 済 |
| `IA_Headlights` | FlipFlop で `Headlight_L` の表示を切り替える(`bPropagateToChildren=false`) | 実装済み(マーカー `HeadlightSwitch` は左だけ)。**事実**: `Headlight_R` は `Headlight_L` の子ではなく `VehicleMesh` に付く(SCS と実行時の `get_attach_parent`)ので、UE でも右は点かない。同じにした | M0109 済 |
| ヘッドライト | スポット、3000 lm、減衰半径 16384 cm、左 (200, −66.8, 65.1) Yaw −30、右 (200, 72.0, 65.1) Yaw 40、最初は非表示。円錐: **右**だけが内 22.49° / 外 50.30°、左は UE の既定の内 0° / 外 44°(M0102 の表は左右とも右の値と書いていた) | 実装済み(`SportsCar.json` の `Headlight_L`・`Headlight_R`。半角を全角にして `SpotConeAngles` に入れる。左の内 0° はエンジンが正の角を求めるので 0.036°。影あり(`LightShadow`)) | M0109 済 |
| 後ろのカメラ | 腕 381.02、ソケット z 71.80、FOV 110(水平。Pawn 基底の 650 / 150 / 90 を上書き)、腕の位置 z 75、遅れ 10(最大 50)、回転の遅れ 2、Yaw だけを継ぐ、衝突なし | 実装済み(`OrbitCamera`: 重心から見た腕の位置、`Distance` 3.81、`SocketOffset` 0.718、`Lag` 10、`LagMaxDistance` 0.5、`RotationLag` 2、`Collide` false。FOV は 16:9 の縦 77.55°) | M0109 済 |
| 前のカメラ | 腕 0、位置 (30, 0, 120)、遅れなし、最初は無効(`bAutoActivate=False`)、FOV 90 | 実装済み(車体の子の固定のカメラ。車体の向きをそのまま継ぐ(UE の腕は Pitch と Roll も継ぐ)) | M0109 済 |
| 車輪のメッシュ | 前左・後左は Yaw −90(と X の鏡映)、前右・後右は Yaw 90。骨 `Phys_Wheel_*` に付く | 実装済み(`VehicleWheelVisual` と `VehicleWheelPose`。休みの姿勢に、回転の角(前進で前が下がる)、舵角、サスペンションの上下を重ねる。`ChaosVehicleTests.TheWheelPoseRaisesTurnsAndSpinsTheWheelFromItsRest`) | M0109 済 |
| `ABP_SportsCar` | MeshRefPose → WheelController(車輪の回転・ステア・サスペンション)→ 部品空間 → ControlRig `CtrlRig_SportsCar` | 一部(WheelController は上の行。`CtrlRig_SportsCar` は見えない骨格メッシュのダンパーと車軸の骨だけを動かし、見えるメッシュは付いていないので作らない) | M0109 |

### Chaos の移動(`VehicleMovementComp`、`SportsCar_Wheels*`)
値は `Templates/CSharpGameEngine.Templates.Vehicle/Assets/Vehicles/SportsCar/SportsCarSetup.cgeasset`(`[DataAsset] VehicleSetup`。SI 単位に直した。`ChaosVehicleTests.TheTemplateAssetCarriesTheValuesTheTestsDriveWith`)。
| 項目 | 値 | 状態 | 担当 |
|---|---|---|---|
| 車体 | 質量 1500 kg、空気抵抗 0.31、ダウンフォース 0.3、車体の幅 180・高さ 144、慣性の倍率 1、重心の上書きなし(物理アセットの重心は (−6.96, 0.04, 50.09) cm) | 実装済み(剛体は質量 1500 の箱 1.9 × 0.9 × 4.5 m。根は重心。空気抵抗とダウンフォースは UE の式(後退ではダウンフォースが持ち上げる向きになるのも UE のまま)。慣性は箱の値で、UE の物理アセットの形の慣性ではない) | M0109 済 |
| エンジン | 最大トルク 750、最大回転 7000、アイドル 900、エンジンブレーキ 0.2、回転の上がり MOI 5、下がり 600/s。トルク曲線(回転→比): 1000→0.5、1800→0.9、3500→1.0、6000→0.8、7000→0 | 実装済み(UE の 21 点の標本と直線の補間、ギアが入っていないと自由に回り、入っていると車輪から決まる回転へ毎秒 4 で近づく、エンジンブレーキ = 回転数 × 0.2 N·m。`ChaosVehicleTests.TheEngineFollowsUesTorqueCurveGearsAndSprungMasses`、`ReleasingTheThrottleInGearSlowsTheCarByEngineBraking`) | M0109 済 |
| 変速 | 自動、自動後退、最終比 2.81、前進 4.25 / 2.52 / 1.66 / 1.22 / 1.00、後退 4.04、上げ 6000 / 下げ 2000、変速時間 0.2 s、効率 0.9 | 実装済み(変速中はニュートラル。空中と空転中は変速しない。ブレーキで後退へ、アクセルで 1 速へすぐ入れる。`FullThrottleReaches…`(5962 rpm で 2 速へ、12 tick のニュートラル)、`TheTopSpeedIsLimitedByTheAirInFifthGear`) | M0109 済 |
| 操舵 | アッカーマン、角度比 0.7、速度による曲線: 0→1.0、20→0.8、60→0.4、120→0.3。**横軸はマイル毎時**(UE の `ProcessSteering` が `CmSToMPH` で渡す。M0102 の表は km/h と書いていた) | 実装済み(棒の幾何は UE の `FAckermannSim` を cm で解く。内側の車輪が 40°。`FullLockAtFiveMetresPerSecondTurnsTheFrontWheelsTheirFortyDegreesOnTheInside`) | M0109 済 |
| 差動 | 後輪駆動(前後の配分 0.5) | 実装済み(後輪 2 つが半分ずつ) | M0109 済 |
| 入力の変化の速さ | アクセル・ブレーキは上がり 6、下がり 10(線形)。ハンドルは上がり 2.5、下がり 5(二乗)。ハンドブレーキは 12 / 12。エンジンへはアクセルの二乗 | 実装済み(`InputRatesRiseAndFallAtUesSpeeds`) | M0109 済 |
| 前輪 | 半径 39、幅 35、質量 20、コーナリング剛性 1000(内部 ×10000)、摩擦の倍率 3、滑りのしきい値 20、スキッドのしきい値 20、最大舵角 40°、ブレーキトルク 4500、ハンドブレーキは効かない、駆動しない | 実装済み(UE の `FSimpleWheelSim::Simulate`: 縦はブレーキか駆動、横は滑りの角 × 剛性(止めるのに要る力まで)、両方を荷重 × 地面の摩擦 × 倍率の円で切る。`FullBrakesStopFromOneHundredKilometresPerHourInAbout20Metres`(18.0 m)) | M0109 済 |
| 後輪 | 半径 40、幅 40、摩擦の倍率 4、しきい値 100、舵角 0、ブレーキトルク 1500、ハンドブレーキ 6000、駆動する | 実装済み(`ALowFrictionSurfaceSpinsTheRearWheelsAndAcceleratesSlower`) | M0109 済 |
| サスペンション(前後同じ) | 上下の最大 10、減衰比 0.5、ばね 250、予圧 50、荷重比 0.5、ロールバー 0.15、光線で測る | 一部(UE の力のサスペンション(`p.Vehicle.DisableConstraintSuspension=1` のときの `ApplySuspensionForces`): ばね 25000 N/m、減衰 = 比 × 2√(k × ばね上の質量)、荷重比、ロールバー。`RestsOnItsSuspensionsWithEveryWheelOnTheGroundThenSleeps`、`AHeavierCarSinksFurtherOnTheSameSprings`、`ACarDrivenOffARampFliesAndLandsOnItsWheels`。UE の既定は物理のソルバーの中のサスペンションの制約(ばね ×0.25 と予圧、縮みきりのハードストップ)で、エンジンには無い。縮みきると力は増えず、車体の箱が床に当たって支える) | M0109 |
| その他 | 停止のしきい値 10、逆向きのしきい値 100、眠りのしきい値 10、眠る斜面 0.866、アイドルのブレーキ 0 | 実装済み(UE の `CalcThrottleBrakeInput` と `ProcessSleeping`(15 回続くと眠り、操作で覚める)。眠るときは物理の新しい `Sleep` 命令で剛体も眠らせる) | M0109 済 |
| エンジン音 | (テンプレートに音の資産は無い。ADR-0078 §2) | 未(既存の音は一発の再生だけで、ループと音程の変更が無い) | — |

### Controller と HUD
| 対象 | 内容 | 状態 | 担当 |
|---|---|---|---|
| BeginPlay | ローカルのときだけ `IMC_Vehicle_Default`(優先 0)を足す。ハンドル用はオプション。タッチでなければ `IMC_Vehicle_MouseLook` を足す。`UI_Vehicle`(`bIsMPH=false`)を作って画面に足す | 一部 | M0104 |
| Tick | 移動コンポーネントが有効なら、`UpdateSpeed(GetForwardSpeed())`、`UpdateGear(GetCurrentGear())` | 実装済み(M0109 から本当のギア。変速中は N、ブレーキで R) | M0104・M0109 済 |
| `UI_Vehicle` | 単位の表示は km/h か MPH。速度は `|速度 cm/s| × (MPH なら 0.022、km/h なら 0.036)` を整数 3 桁で表示する。ギアは −1→R、0→N、それ以外は数字 | 一部 | M0104 |
| ReceivePossess と On Pawn Destroyed | 移動コンポーネントを保持する。壊れたら最初の PlayerStart に同じクラスを生成して Possess する | 実装済み(`RespawnTests.PickFirstTakesTheStartNotedFirstAndPickRandomIsDeterministicForASeed`。テンプレートの `RespawnVehicle`。Vehicle の `KillZ` は −50 m。M0109: 生成した車の剛体は生成した向きで作る(`VehicleBodySetup`)) | M0107 |

## レベル(4 つ共通)
| 対象 | 内容 | 状態 | 担当 |
|---|---|---|---|
| ライティング | Lumen の GI と反射、DirectionalLight(可動)、SkyLight(リアルタイムキャプチャ)、SkyAtmosphere、VolumetricCloud、ExponentialHeightFog、空の球、PostProcessVolume(ThirdPerson と Vehicle) | 一部(雲・空の球・メッシュの距離場とグローバル距離場は M0110 で実装。Lumen 相当の GI と反射が無い) | M0110(済)・M0111 |
| Vehicle の地面 | Landscape(実行時の仮想テクスチャ、Ground/Road の層、物理マテリアルの Slippery / NonSlippery)、Landscape のスプラインの道。**事実**(M0109 の `probe_vehicle_physics.py`): Slippery と NonSlippery の摩擦はどちらも 1.0(違いは反発 0.4 と 0、結合は Multiply)、Landscape の既定の物理マテリアルは NonSlippery、層の情報(`LI_Ground`・`LI_Road`)に物理マテリアルは無い。置かれた立方体とコーンは既定の物理マテリアル(摩擦 0.7、反発 0.3) | 一部(路面ごとの摩擦は実装済み: `StaticMeshCollider.Material`(M0109)で、地面の板は摩擦 1.0・反発 0、他は 0.7・0.3。タイヤは当たった面の摩擦を読む(`ALowFrictionSurfaceSpinsTheRearWheelsAndAcceleratesSlower`)。地面は平らな板のまま) | M0109 済・M0112 |
| TopDown のナビメッシュ | NavMeshBoundsVolume(位置 (0, 0, 0)、範囲 (2250, 2250, 375) cm)、RecastNavMesh(レベルのアクターは AgentRadius 35、AgentHeight 144、AgentMaxSlope 44、TileSizeUU 1000、MergeRegionSize 400、WATERSHED。`DefaultEngine.ini` は CellSize 19、CellHeight 10。AgentMaxStepHeight 35 は `RecastNavMesh.h` の既定) | 一部(Cook がシーンの静的な衝突形状から格子のナビメッシュを焼く。値はこの行のとおり。Recast の輪郭の単純化と斜めの辺、分割したタイル、動的な再生成は無い) | M0106 済 |
