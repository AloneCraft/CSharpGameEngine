# M0107 — キャラクターとアニメーション
- 日付: 2026-10-23
- 状態: 完了(2026-10-23。下の「範囲外(未実装)と不確実性」に、UE との差を行ごとに書いた)

## 目的
ADR-0078 の「UE テンプレートの完全移植」のうち、`UE-Template-Behavior.md` の表で担当が M0107 の行をすべて実装する。計画の段階 1 の 4。
ThirdPerson・FirstPerson・TopDown の `ABP_Unarmed`(8 方向のブレンドスペース、加速の有無、状態機械の遷移、足の IK)、FirstPerson の腕と体(`ABP_FP_Copy`、`CtrlRig_FPWarp`、一人称の FOV と縮尺、カメラに見えず影だけを落とす体、`BP_FirstPersonCameraManager` のピッチ制限)、TopDown のカメラの遅れ追従と平面への拘束、Pawn が壊れたとき(KillZ)のリスポーン(TopDown は無作為の PlayerStart、Vehicle は最初の PlayerStart)。

## 事実(再現して確かめたこと)
- UE5.8 から取った値(`tools/UnrealExport/probe_character_animation.py`、`run_probe_character_animation.cmd`。出力は `.artifacts/UnrealExport/probe_character_<Project>.json`):
  - `WorldSettings.KillZ`: ThirdPerson・FirstPerson・TopDown は -1048575 cm(UE の既定。`bEnableWorldBoundsChecks` あり)、Vehicle は -5000 cm。どのレベルも `PlayerStart` は 1 つ(位置は (0, 0, 302.01) cm、Vehicle は (4450, 0, 102) cm)。
  - `CtrlRig_FPWarp` のコントロールの初期の世界の位置(メッシュのコンポーネント空間、cm): `BodyOffset` (0, -3.586, 109.510)、`Ctrl_Head` (0, 0.663, 162.575)、`Ctrl_Shoulder_L` (19.010, -2.557, 143.583)、`Ctrl_Shoulder_R` (-19.010, -2.557, 143.583)。後ろの 3 つは、基準姿勢の `head`・`upperarm_l`・`upperarm_r` の位置と一致する(`BodyOffset` だけが別の値)。
  - TopDown の `CharacterMovement`: `bConstrainToPlane` あり、`bSnapToPlaneAtStart` あり、**`PlaneConstraintAxisSetting` は Custom で `PlaneConstraintNormal` は (0, 0, 0)**。UE の `ConstrainDirectionToPlane` は法線との内積を引くだけなので、法線が 0 の拘束は何も拘束しない。TopDown のキャラクターは、UE でも平面に拘束されていない。
  - TopDown の `SpringArm`: `bEnableCameraLag` あり、`CameraLagSpeed` 3、`CameraLagMaxTimeStep` 1/60 s、`CameraLagMaxDistance` 0、`bUseCameraLagSubstepping` あり、`bEnableCameraRotationLag` なし、`bDoCollisionTest` なし、腕 1400、相対回転 (Pitch -50, Yaw 45)、絶対回転。
  - FirstPerson の `FirstPersonMesh`: `FIRST_PERSON`、持ち主にだけ見える、影を落とさない。`CharacterMesh0`: `WORLD_SPACE_REPRESENTATION`、持ち主には見えない(`OwnerNoSee`)、影を落とす。カメラ: `FirstPersonFieldOfView` 70、`FirstPersonScale` 0.6、`FieldOfView` 90、`bUsePawnControlRotation`、`bUseControllerRotationYaw`。目の位置は、メッシュの相対位置 (-20, 0, -96) cm と、リグの `Ctrl_Head` と、頭のボーンの座標系でのカメラの相対位置 (2.794, 5.881, 0) cm から、キャラクターの前向きの -13.456 cm、上へ 69.369 cm(これまでの目の高さ 64 cm は `BaseEyeHeight`)。
- ABP_Unarmed のグラフ(`.artifacts/UnrealExport/logic/ThirdPerson/ABP_Unarmed.logic.txt`): 遷移とブレンドの時間は `UE-Template-Behavior.md` の表のとおり。`AlphaInterp` の補間は上がりも下がりも 10(足の IK の `ZOffset`)。`FootTrace` は、足のボーンの X と Y + 5 cm、`root` の Z + 30 cm から Z - 50 cm まで、半径 5 cm の球でトレースする。
- 三角形分割: `BS_Idle_Walk_Run` の 32 個の三角形のうち、X の左半分のセルは「左下から右上」、右半分は「右下から左上」の対角線で切る(`BlendSpaceData` の頂点から読んだ。X = ±180 が Bwd)。
- **クリップの向きの測定**: 8 方向のクリップは足踏みで、移動を持たない。接地している足(低い方)は体に対して後ろへ動くので、その動きを逆にした向きがクリップの進む向き。FK で測ると、glb の空間では Fwd = +Z、Left = +X、Right = -X、Fwd_Left = (+0.71, +0.71)、Bwd_Right = (-0.71, -0.71)。インポーターは glTF の Z を反転する(エンジンの軸は左手系)ので、モデル空間では Fwd = -F、Right の足(`foot_r`)は -R 側にあり、テンプレートのメッシュのノードは半回転(Yaw 0.5 回転)しているので、シーンでは Fwd = +F、`Walk_Right` は **+R へ進む**(R は前を向いたとき画面の左)。つまり `Direction`(UE の CalculateDirection の符号: 右が正)は、シーンの +R 側を正にとらないと、横歩きの足の動きと進む向きが逆になる。`MannequinAssetTests.TheDirectionalClipsTravelTheWayTheyAreNamed` が 12 個のクリップで固定する。
  - 副産物の事実: インポーターの Z の反転(左手系)とレンダラーの右手系のビューで、メッシュは UE の見た目の鏡像になる(前を向いたキャラクターを後ろから見ると、`foot_l` が画面の右にある)。クリップ・骨・メッシュは同じ変換を受けるので動きは自己整合している。修正はしていない(既存の変換)。
- アニメーションの書き出し: `tools/UnrealExport/export_mannequin_animations.py` で Walk と Jog の 16 本と Idle・Jump・Fall・Land を UE から書き出し、`CSharpGameEngine.Unreal.SceneImporter compact-animations` でジョイントとクリップだけの glb にする。既にあった 6 本(Idle・Walk_Fwd・Jog_Fwd・Jump・Fall・Land)は、書き出し直した結果とバイト単位で同じだった。書き出しに `-nullrhi` が要る(描画のデバイスがあると、プレビューのメッシュのマテリアルが入って 1 本 10 MB になる)。
- 修正前の実走(WARP): ThirdPerson は歩き 15.700 m・上昇 1.228 m、FirstPerson は 16.000 m・0.863 m、Vehicle は 48.281 m・38.3°、TopDown は直線 10.845 m、ナビメッシュ 14.87 m。

## 決定
### 1. アニメーションの評価(`Runtime.Animation`)
- `BlendSpace2D`: 規則的な格子のブレンドスペース。UE と同じ三角形分割(左半分と右半分で対角線を鏡映)で、重みは UE の `TargetWeightInterpolationSpeedPerSec`(5)で目標へ近づく(`FInterpTo` の指数的な近づき方。重みは 1 に正規化する。最初の問い合わせは補間せずに目標にする)。
- `LocomotionStateMachine`(作り直し): ABP_Unarmed の `Main States`(Locomotion・Jump・Fall Loop・Land)と、Locomotion の中の状態機械(Idle と Move = ブレンドスペース)。遷移の条件と時間は UE の値:
  - Locomotion か Land から、落下中で上向きの速度が 1 m/s(100 cm/s)より大きければ Jump(0.15 s)、でなければ Fall Loop(0.4 s)。
  - Jump は、クリップの残りが 0.3 s 以下(StartPosition は 0.08 s)になったら Fall Loop(0.3 s)。Jump と Fall Loop は、落下が終わったら Land(0.1 s)。
  - Land は、クリップの残りが 0.4 s 以下になったら Locomotion(0.4 s)、`ShouldMove` なら Locomotion(0.5 s)、落下したら Jump か Fall Loop。
  - Idle → Move は `ShouldMove`(0.2 s)、Move → Idle は `!ShouldMove`(0.3 s)。すべて Cubic(3t² - 2t³)のブレンドで、遷移の途中で別の遷移が起きたら、そのとき画面にあったポーズを出発点にする(`PoseFade`)。
  - ブレンドスペースのクリップは、重みつきの長さに合わせた 1 つの正規化した位相で進む(同期グループ Locomotion)。
- `Locomotion`(コンポーネント): 8 方向の Walk・Jog のクリップを持つ(`Walk`・`Jog` が前。ほかの 7 方向は省略でき、省略した方向は前のクリップを使う = 前のテンプレートの動き)。`BlendSeconds` は無くなった(遷移ごとの時間を持つ)。入力は `Speed`・`Direction`(度)・`ShouldMove`・`Falling`・`VerticalSpeed`。
- `LocomotionObserver`(`Runtime.Character`): BlueprintUpdateAnimation 相当。`CalculateDirection` は上の事実のとおり、速度の水平成分と体の前向きの符号つきの角度(+R 側が正)。`bOrientRotationToMovement`(`CharacterMotion.FaceMovement`)なら ±45° に制限する。`ShouldMove` は「地面の速さが 0.01 cm/s より大きい、かつ `MoveIntent.Direction` が 0 でない(加速度がある)」。テンプレートの `ThirdPersonAnimation`・`TopDownAnimation`(新規。TopDown のメッシュはこれまで動かなかった)・`FirstPersonAnimation` は、`in Parent<…>` で親の値を渡すだけ。
### 2. 足の IK(`FootIk`、`FootIkUpdate`)
- UE の `CR_Mannequin_FootIK` の手順: 足の上(メッシュの原点 + 0.3 m)から下(- 0.5 m)へ、足首の少し前(0.05 m)から光線を落とし、地面の高さ(メッシュの原点を基準)を足の目標のずれにする。当たらなければ 0、落下中は両足とも 0。ずれは 10/s で補間し、骨盤は低い方に合わせる。
- 差: UE は半径 5 cm の球のトレースと PBIK(全身の IK)。こちらは光線(`DeterministicPhysicsWorld.Raycast`。`Sweep` は光線で近似されていて球の当たりにならない)と、解析的な 2 ボーン IK(`TwoBoneIk`。膝の向きは今の曲がりを保ち、まっすぐなら足の向きを使う。足の向きは保つ)。骨盤を動かしてから、足首を目標へ、足首の向きは動かさない。
- 地面がメッシュの原点より 1 cm 低い(テンプレートのメッシュは `CharacterMesh0` の z が capsule の底より 1 cm 上)ので、平らな床でも足は 1 cm 下がる。UE も同じ。
### 3. 一人称(`FirstPersonPose`、`FirstPersonRig`、`FirstPersonView`、`MeshVisibility`)
- 体(`CharacterMesh0`)と腕(`FirstPersonMesh`)は、同じ Manny を 2 つのスキンつきメッシュとして置く(`FirstPersonMesh` は体のメッシュの子 = UE の `bUseAttachedParent` のコピー元)。体は `MeshVisibility { VisibleToCamera: false }`(カメラに見えず、影は落とす)、腕は `MeshVisibility { CastsShadow: false }` と `FirstPersonView`。
- `FirstPersonPoseUpdate`(`ABP_FP_Copy`): 体の最終のポーズ(足の IK のあと)を腕のスケルトンへ写し、`FirstPersonRig`(`CtrlRig_FPWarp`)で補正する: 背骨 `spine_01`→`spine_04` を FABRIK(最大 10 回、精度 1 cm、重み 0.5)で `BodyOffset` へ引き(`spine_01` は動かさない。各ボーンはその先の関節の新しい位置へ回り、下の枝は一緒に動く)、`head` を基準姿勢の位置と向きへ置き、`upperarm_l`・`upperarm_r` は位置だけ基準姿勢へ(向きはアニメーションのまま、前腕と手は剛体で追従)。コントロールの値は、事実のとおり `BodyOffset` 以外は基準姿勢の骨と一致するので、骨から取る。FABRIK の `Weight` は、解いた位置と元の位置の補間と解釈した(UE のソースで確かめていない)。
- カメラの位置: 頭のボーンは `Ctrl_Head` で固定されるので、目は体に対して固定。`OrbitCamera.Pivot` を (0, 0.69369, -0.13456) にした(事実の計算)。これまでの (0, 0.64, 0) から、約 5 cm 高く、13 cm 後ろ。
- 一人称の描画: `FirstPersonViewWarp` が、メッシュのカメラ相対のワールド行列に、カメラ空間の変換 `(縮尺 × 広がり, 縮尺 × 広がり, 縮尺)` を後ろから掛ける。広がり = tan(既定の FOV / 2) / tan(一人称の FOV / 2)。カメラの射影を通すと、一人称の狭い FOV の射影が置く位置に、縮尺 0.6 だけ目に近く見える。描画の契約(`stage-v*`)も、射影の行列も変えない。`TransformSync` が、`FirstPersonView` を持つ Entity にだけ掛ける。
- 描画の指定: `StageRenderer.SetObjectVisibility(物体, カメラに見える, 影を落とす)`(`RenderServer`、`RenderFrameState` にも記録して、デバイスの復旧で再生される)。カメラの描画(メイン、GI プローブ、カメラプレビュー)は前者、影の描画は後者を見る。シェーダーと契約は変えていない。
- `ViewAngles` の上下の制限は `BP_FirstPersonCameraManager` の -70° と 80°(`PlayerControl.AddLook` は元からこの形)。FirstPerson の体は、`bUseControllerRotationYaw` に当たる `PlayerControl.YawRotation` で視点の Yaw に合わせる(テンプレートの `PlayerControls`)。
### 4. カメラの遅れ追従(`OrbitCamera`)
- 新しい値: `Lag`(UE の CameraLagSpeed)、`LagMaxTimeStep`(既定 1/60)、`LagMaxDistance`、`RotationLag`(CameraRotationLagSpeed)、`SocketOffset`(カメラの座標系のずれ。腕を障害物で縮めたあとに足す)。既定はすべて遅れなし。
- `CameraLag`: `VInterpTo`(距離 × clamp(dt × 速さ))、`QInterpTo`(slerp)、UE の substepping(1 フレームが `LagMaxTimeStep` より長いと、追従する点を腕の動きの分だけ進めながら刻み幅で進める。UE と同じ式で、フレームレートに依存しない近似ではない: 30 fps は 60 fps より遅れが大きい)。`OrbitCameraRuntime` が前のフレームの軸の位置と回転を持つ(スナップショットに入らない)。新しい Pawn のカメラは最初のフレームで遅れずに置かれる。
- TopDown は `Lag` 3。
### 5. 平面への拘束(`CharacterMotion`)
- `ConstrainToPlane`、`PlaneNormal`、`PlaneOrigin`、`SnapToPlaneAtStart`。入力・速度・各移動の変位を、法線との内積を引いて平面に平らにする(法線は使うとき正規化。0 なら何もしない)。最初の tick に平面へ寄せる(`CharacterMotionState.PlaneSnapped`)。制約なし(既定)の動きは、ビットまで変わらない。TopDown のシーンは UE と同じ値(`ConstrainToPlane`・`SnapToPlaneAtStart` あり、法線 0)で、事実のとおり拘束にならない。
### 6. KillZ とリスポーン(`Respawn.cs`)
- `KillZ { Height }`(既定は UE の -10485.75 m)を Pawn の根に置く。`KillZUpdate`(PostPhysics)が、位置の高さが `Height` より低い(等しいのは中)Pawn を壊し(根なら Scene ごと)、`PawnDestroyed` を送る。
- `PlayerStarts`(`[EngineService]`): ゲームの PlayerStart の位置を `Note` で教え、`TryPick(World, First|Random, 種)` で選ぶ。壊れた Entity の PlayerStart は忘れる。無作為は SplitMix64 の決定的な選び方(同じ tick なら同じ地点。UE の `Array_Random` と違って再生で同じになる)。
- テンプレート: TopDown の `RespawnPlayer`(`PawnDestroyed` → 無作為の PlayerStart に `TopDownCharacter` を `Spawner.Spawn`)、Vehicle の `RespawnVehicle`(最初の PlayerStart に `SportsCar`)。生成した Pawn のカメラが画面を引き継ぐ(`LevelLoad` が、追っていたカメラの Entity が死んだら、新しい有効なカメラへ移る)。Pawn のシーンは `KillZ` を持つ(Vehicle は -50 m)。
- 後から作る `SkeletalMesh`(M0105 は描かなかった): `RenderRoster.BindSkeletalMesh` が、資産ごとにスキンつきメッシュとマテリアルの組を `StageRenderer.AddMesh`(スキンつきも可に。パレットのリングを広げるので GPU の待ちが 1 回入る)で足し、Entity が消えたら組は資産ごとのプールへ戻り、次の Entity が使う(リスポーンを繰り返してもレンダラーは増えない)。
### 7. 実走の報告(`RuntimeAutomation`)
- `focusPositions`: カメラの親の Entity(カメラが追うキャラクター)の位置をフレームごとに報告に足した(`cameraPositions` は変えない)。遅れて追うカメラの位置は、キャラクターの経路を表さないため。`ClickToMoveVerification` はこれで判定する(古い報告はカメラへ戻る)。

## 作ったもの
| 層 | 中身 |
|---|---|
| L5 Animation | `BlendSpace2D`、`PoseBlending`(`PoseMixer`・`PoseFade`)、`LocomotionStateMachine`(作り直し)、`TwoBoneIk`、`FootIk`(`FootIkPose`)、`FirstPersonRig`(`FirstPersonPose`)、`Locomotion` の 8 方向、`Skeleton.ComputeModel` |
| L5 Character | `LocomotionObserver`、`FootIkUpdate`・`FirstPersonPoseUpdate`(`FootIkSystems.cs`)、`CameraLag`・`OrbitCamera` の遅れ・`SocketOffset`、`CharacterMotion` の平面の拘束、`PlayerControl.YawRotation`、`KillZ`・`PawnDestroyed`・`PlayerStarts`(`Respawn.cs`) |
| L5 Windows | `MeshVisibility`・`FirstPersonView`・`FirstPersonViewWarp`、`TransformSync`、`RenderRoster.BindSkeletalMesh`、`StageSceneDataBuilder.LoadSkeletalAsset`、`LevelLoad` のカメラの引き継ぎ、`RuntimeAutomation` の `focusPositions` |
| L4 描画 | `StageRenderer.SetObjectVisibility`・スキンつきの `AddMesh`、`RenderServer`、`RenderFrameState` |
| L7 | `tools/UnrealExport/export_mannequin_animations.py`・`probe_character_animation.py`、`CSharpGameEngine.Unreal.SceneImporter compact-animations` |
| L9 | 3 つの Character シーン(8 方向、足の IK、KillZ)、FirstPerson(体と腕、目の位置、ピッチ)、TopDown(遅れ追従、平面の拘束、リスポーン)、Vehicle(KillZ、リスポーン)、各テンプレートの Mannequin のクリップ(+14 本。FirstPerson は +20 本とメッシュ)、`build/CSharpGameEngine.Version.props` を 0.1.18 |

## 検証
- 単体テスト(`CSharpGameEngine.Windows.Tests`): `LocomotionGraphTests`、`FootIkTests`、`LocomotionObserverTests`、`MannequinAssetTests`(実際の Mannequin。クリップの向き、基準姿勢の位置、足の IK と一人称の補正)、`CameraLagTests`、`CharacterPlaneConstraintTests`、`RespawnTests`、`FirstPersonViewWarpTests`、`TemplateCharacterSceneTests`、`RenderServerVisibilityTests`(WARP)、`RenderRosterTests`(+2。WARP)、`SkinningGpuIntegrationTests`(+2。WARP)、`AnimationRuntimeTests`(既存の 2 つ)。`CSharpGameEngine.FeatureRunner.Tests`: `ClickToMoveVerificationTests`(+1、目的地の値を更新)。
- 実走(WARP、リポジトリのビルド): 値は次のとおり(詳しくは「観測」)。
- 画像: `.artifacts/m0107/run/`(ThirdPerson の走り、FirstPerson の腕と体の影、TopDown の走りとリスポーン)。

## 観測(最終のコードから)
実走は WARP(`CGE_GRAPHICS_ADAPTER=WARP`)。D3D12 のデバッグレイヤーのエラーはすべて 0。
| テンプレート | 値 | 修正前 | 注 |
|---|---|---|---|
| ThirdPerson(リポジトリのビルドと SDK パッケージから作ったゲーム) | 移動 15.700 m、上昇 1.228 m、下降 1.228 m、最低 0.985 m、足音 15 回 | 15.700 / 1.228 | 変化なし |
| FirstPerson | 移動 16.000 m、上昇 0.863 m、下降 0.863 m、最低 1.654 m | 16.000 / 0.863 | 変化なし(カメラは目の位置が 5 cm 高く 13 cm 後ろだが、報告はカメラの位置の差で測るので同じ) |
| Vehicle(SDK パッケージ) | 移動 48.281 m、曲がり 38.3°、HUD 28 状態 | 48.281 / 38.3° | 変化なし |
| TopDown ナビメッシュなし(`CGE_NAVIGATION_DISABLED=1`) | 移動 9.865 m、最後の動き 0.000 m、描画物 56 → 57 → 56、目印のフレーム 5 | 10.845 m | **意図した変更**: 下の「TopDown の値が変わった理由」 |
| TopDown ナビメッシュあり | 移動 9.826 m(キャラクターの経路は 14.89 m)、(6.75, 7.14) へ着いて目的地の誤差 0.00 m、壁の端まで 0.28 m、1 フレームの落ち最大 0.40 m | 14.87 m・(7.06, 8.06) | **意図した変更**: 同じ理由。経路の長さは 0.02 m の違い |

- TopDown の値が変わった理由(Fact): TopDown のカメラが遅れて追従する(速さ 3)ようになった。実走の入力は「押している間、カーソルの下の地面を毎フレーム選び直す」ので、カメラが遅れると同じカーソルの位置が地面の別の点になり、目的地が約 1 m 手前(移動距離 9.865 m、10.845 m でなく)になる。`ClickToMoveVerification` の判定は、カメラの位置ではなくカメラの親の Entity の位置(新しい報告の `focusPositions`)で測るように変えた。目的地の期待値は (7.06, 8.06) から (6.75, 7.14) にした(許容 0.5 m)。
- 画像(`.artifacts/m0107/run/`): `frame_ThirdPerson_58.png`(走るキャラクター)、`frame_FirstPerson_14.png`・`frame_FirstPerson_110.png`(腕の描画と、床に落ちる体の影)、`frame_TopDown_*.png`(TopDown のキャラクターが走る様子と、リスポーン後)。画像は目で確かめた。
- リスポーン(一時的に KillZ を高く置いた実走。元に戻した): 壊れたあとに `PlayerStart` の位置へ新しいキャラクターが出て、カメラがそれを追い、描かれた。TopDown のレベルの `PlayerStart` は 1 つなので、無作為の選択が実際に分かれる場面は実走では出ない(選択の分布は単体テスト `RespawnTests.PickRandomReachesEveryStartAndNoneIsFavoured`)。描画物の数は繰り返しても増えない(`RenderRosterTests.ASkeletalMeshSpawnedAfterTheLevelLoadedIsDrawnAndItsMeshesAreReusedByTheNextOne`)。

## 実装中に見つけた事実
- `Locomotion.Direction` の符号の取り違えは、理屈ではなく測定で見つけた。右を `forward × up` とした最初の実装でも単体テストは通ったが、実際のクリップの進む向きとは逆だった(`TheDirectionalClipsTravelTheWayTheyAreNamed` で捕まえた)。
- `UE-Template-Behavior.md` の `Direction` の行は条件が逆だった(「`bOrientRotationToMovement` なら `CalculateDirection` をそのまま、そうでなければ ±45° に制限」)。グラフの `select` は `bOrientRotationToMovement` が真のとき Clamp の側(Option 1)を選ぶので、直した(M0102 の書き写しの誤りと思われる。実装は最初からグラフどおり)。
- FABRIK のあとの腕の位置は、UE のコントロールの値が基準姿勢の骨と一致するので、骨の値で足りた。
- TopDown の平面の拘束は UE でも無効(法線 0)。「平面への拘束(TopDown)」の行は、機能の実装と、TopDown の値が何も拘束しないことの両方が答えになる。
- 追っていたカメラが死ぬと `LevelLoad` が置けなくなる問題(カメラは読み込み時に 1 つだけ覚えていた)。新しい Pawn が出ても画面が止まったままになる。追うカメラが死んだら、新しい有効なカメラへ移るようにした。
- KillZ を試すために start より高く置くと、最初のフレームの描画の前に Pawn が壊れて「有効なカメラが無い」で落ちる(読み込み前の話。現実の KillZ では起きない)。
- 後から作るスキンつきメッシュは、パレットのリングを作り直すので GPU を待つ。2 つ目以降(プールから使う)は待たない。
- 自動検証の `Click` の目的地: 押している間カーソルの下の地面を毎フレーム選び直すので、カメラの遅れ追従で目的地が約 1 m 手前になった。TopDown の実走の値は意図した変更として更新した。

## 範囲外(未実装)と不確実性
行ごとの「できたこと」と「UE との差」:
1. 8 方向のブレンドスペースと `ShouldMove`: 実装済み。差: UE の `GetCurrentAcceleration() ≠ 0` は `MoveIntent.Direction ≠ 0` で代える(加速度は移動の入力から決まるので同じ向きに働くが、坂や地面の摩擦のような入力以外の加速度は見ない)。ブレンドスペースの重みの補間が UE の `FInterpTo` と同じ式かは、UE のソースでなく挙動(5/s)から合わせた(推測)。
2. 足の IK: 実装済み(解析的な 2 ボーン IK と光線)。差: UE は `CR_Mannequin_FootIK`(PBIK = 全身の IK、半径 5 cm の球のトレース)。足が段差の縁にかかる場面や、斜面での足首の傾き(UE は地面の法線に足を合わせる)は同じにならない。ポーズを見た目で UE と並べる比較はしていない。
3. 一人称の腕・体・FOV・縮尺・影: 実装済み。差: FABRIK は `Weight` を補間の係数と読んだ(推測)。腕のコントロールの値は基準姿勢の骨から取る(`BodyOffset` 以外は UE の値と一致する事実を使った)。カメラは頭のボーンに付けず固定の目の位置(`Ctrl_Head` が頭を固定するので同じ結果のはずだが、頭の揺れを足す場合は付け直しが必要)。腕のメッシュの描画は、カメラの射影ではなく行列の変更(カメラの空間の変換)で作る。大きく傾けたカメラの端に近い画では、UE の射影と厳密には一致しない場合がある(推測。端の誤差は測っていない)。「持ち主にだけ見える」はこのエンジンに持ち主の概念が無いので、カメラに見える・影を落とすの 2 つのフラグで表した(複数カメラや分割画面で別々に決めることはできない)。
4. カメラの遅れ追従とピッチ制限: 実装済み。差: UE の substepping の式のとおりなので、フレームレートが低いと遅れが大きい(UE も同じ)。`OrbitCamera.RotationLag` は実装し単体テストで固定したが、テンプレートは使わない(TopDown は位置の遅れのみ)。FOV 55(TopDown の `Camera`)は元からの値。
5. 平面への拘束: 実装済み。TopDown のシーンは UE と同じ値で、法線 0 のため拘束にならない。法線を持つ拘束は単体テストだけで確かめた(テンプレートの実走には出ない)。
6. リスポーンと KillZ: 実装済み。差: UE の「Possess」は、新しい Pawn へ入力を渡す操作。このエンジンは入力が Component で決まるので、新しい Pawn は生成の直後から入力を受ける。無作為の選択は決定的な疑似乱数(同じ tick の入力なら同じ地点)。KillZ の既定は UE の既定値で、ThirdPerson・FirstPerson・TopDown の Pawn は 10 km 余りの下まで落ちないと壊れない(実走では起こらない。単体テストと一時的な実走で確かめた)。壊れた Pawn の持ち物(音、画面の文字)の後始末は、シーンごと壊すので Scene の寿命に従う(個別には確かめていない)。
7. 4 つのテンプレートの実走: 壊れていない(上の「観測」)。VS の実機での新規プロジェクト(F5 や VSIX のテンプレート)は確かめていない。`verify-template-package` は ThirdPerson・TopDown・Vehicle を SDK パッケージから作って通した。FirstPerson はこのコマンドが対応しないので、リポジトリのビルドで確かめた。
共通の不確実性:
- 実機の GPU(WARP のみ)。スキニングの追加のパレット、可視のフラグ、影の描画の差は実機で確かめていない。
- インポーターの Z の反転とレンダラーの視点で、メッシュが UE の見た目の鏡像になっている(既存の性質。上の「事実」)。クリップの向きの符号はこれに合わせて測って決めた。この鏡像を直す場合、`Direction` の符号とクリップの割り当て(テスト `TheDirectionalClipsTravelTheWayTheyAreNamed`)も一緒に直す必要がある。
- アニメーションの資産が増えた: 3 つのテンプレートの `Assets/Characters/Mannequins/Anims` は、それぞれ 20 本・約 8 MB(ThirdPerson と TopDown は +14 本、FirstPerson は +20 本)と、FirstPerson の `SKM_Manny_Simple.glb`(10.5 MB。腕と体の 2 つのメッシュに使う)。SDK のパッケージ(`CSharpGameEngine.Sdk.0.1.18.nupkg`)は 61,968,749 バイトで、0.1.17 より 28 KB 大きいだけ(テンプレートの資産はパッケージに入らない)。VSIX が同梱するテンプレートの zip の大きさは測っていない。

## 完了の判定
- 依頼への答え: M0107 の 7 行を実装し、`UE-Template-Behavior.md` の各行を実装済みへ更新した。8 方向ブレンドと状態機械・足の IK・一人称・カメラの遅れ・平面の拘束・リスポーン(KillZ)を、単体テスト(境界・退化入力・実際の Mannequin の資産)と WARP の実走で確かめた。
- 意図と観測の一致: MatchesWithDocumentedExtension(足の IK は解析的な 2 ボーン、一人称の補正は基準姿勢の制御から。上の「範囲外」)。
- 全テスト: `dotnet test CSharpGameEngine.slnx -c Debug --nologo -v q` を最後まで実行し、22 プロジェクトで失敗 0。`CSharpGameEngine.Windows.Tests` 700 合格(2 スキップ = 環境変数で有効にする性能の証拠のテスト、元から)、`VSExtension.Tests` 1197、`FeatureRunner.Tests` 201、`CodeAnalysis.Tests` 303、`Core.Tests` 132(`SolutionProjectRegistrationSpec` を含む。このマイルストーンでプロジェクトは増えていない)、`Navigation.Tests` 57。出力は `.artifacts/m0107/full_test.txt`。M0106 の時点の `Windows.Tests` は 550 だったので、+150。
- 実走: `verify-thirdperson-character`(ThirdPerson・FirstPerson)と `verify-template-package`(ThirdPerson・TopDown・Vehicle。SDK 0.1.18)が通った。
