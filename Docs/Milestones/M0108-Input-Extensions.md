# M0108 — 入力の拡張
- 日付: 2026-10-23
- 状態: 完了(2026-10-23。下の「範囲外(未実装)と不確実性」に、UE との差と未実装を書いた)

## 目的
ADR-0078 の「UE テンプレートの完全移植」のうち、`UE-Template-Behavior.md` の表で担当が M0108 の行をすべて実装する。計画の段階 1 の 5。
`[InputActions]` に、1 軸のアナログ値(ゲームパッドのトリガー・スティックの軸)、ボタンを押していた時間、マウスの片方の軸だけを足し、TopDown の短押しと長押し(`PressedThreshold` 0.5 秒)と、Vehicle のアナログのアクセル・ブレーキ・ハンドルを UE のとおりにする。自動実行(`CGE_AUTOMATION_INPUT`)で新しい入力を再現できるようにする。

## 事実(再現して確かめたこと)
- UE の `BP_TopDownController`(`.artifacts/UnrealExport/logic/TopDown/BP_TopDownController.logic.txt`):
  - Started: `StopMovement()`、当たりがあれば `CachedDestination` を更新。
  - Triggered(押している間、押した瞬間も): 当たりがあれば `CachedDestination` を更新して `Follow`(`AddMovementInput(GetDirectionUnitVector(Pawn の位置, 位置), 1)`)。ナビメッシュを使わず、3 次元の単位ベクトルで押す。
  - Canceled / Completed: `ElapsedSeconds < PressedThreshold` なら `MoveTo(CachedDestination)`。`MoveTo` は `SimpleMoveToLocation` と `SpawnSystemAtLocation(FXCursor)` を呼ぶ。つまり目印は短押しの離した瞬間だけに出る。M0105 は押した瞬間に出していた。
- UE の Vehicle の入力(`UE-Template-Behavior.md` の表): `IA_Throttle`・`IA_Brake`・`IA_Steering` は Axis1D で、トリガー(`Gamepad_RightTriggerAxis`・`Gamepad_LeftTriggerAxis`)、キー(A/Left は Negate)、`Gamepad_LeftX`。`IA_LookAround` は Axis1D の `Gamepad_RightX` と `MouseX`。
- 自動実行の 1 フレームは 8/60 秒(`FixedFrameTime`)だが、`[InputActions]` の `Read` に渡す経過時間は `Math.Min(FrameDeltaSeconds, 0.1)`。したがって自動実行の `HeldSeconds` は 1 フレーム 0.1 秒で進む。M0107 までの実走のクリック(`MouseLeft@20-25` = 5 フレーム)は、離したフレームの値がちょうど 0.5 秒で、厳密な不等号の短押しの判定では長押しになる。実走のクリックは 4 フレーム(`MouseLeft@20-24` = 0.4 秒)にした。
- 修正前の実走(WARP): ThirdPerson 15.700 m、FirstPerson 16.000 m、Vehicle 48.281 m・38.3°、TopDown はナビメッシュなし 9.865 m、ナビメッシュあり (6.75, 7.14)。

## 決定
### 1. `Axis`(1 軸のアナログ値)
- `Axis.Value` は -1〜1。割り当て: `[Keys(...)]`(どれかを押している間 +1)、`[NegativeKeys(...)]`(-1。新しい属性で、`Axis` だけに書ける。両方なら打ち消し合って 0)、`[Gamepad(Pad.LeftTrigger|RightTrigger)]`、`[Gamepad(Pad.LeftStick|RightStick, StickAxis.X|Y)]`、`[Gamepad(Pad.South…)]`(ボタン。押している間 +1)。
  - 理想コードの案(`Keys(Negative:…, Positive:…)`)は採らなかった: `Keys(Key.W, Key.S)` のような 2 つのキーが、4 キーの `Stick` と曖昧になる(位置引数の 2 つのキーが新しい形に束縛される)。専用の属性で明示した。
- 複数の割り当ては足し合わせ、`Axis.FromSum` が -1〜1 に丸める(W と Up を同時に押しても 2 にならない)。NaN と無限大は 0(壊れた入力で車やカメラが飛ばないように)。`Axis` は `float` へ暗黙に変換でき、`Throttle - Brake` と書ける。
- `[Gamepad]` の `DeadZone`(既定: スティック 0.2 = M0107 までの値、トリガー 0.1)、`Scale`、`Invert`。不感帯のあとの値は `(|v| - 不感帯) / (1 - 不感帯)` に引き伸ばし、倍率を掛け、反転する。範囲外は INPUT005(不感帯は 0 以上 1 未満、倍率は有限で 0 より大きい。NaN は書かれた値として拒否する。ボタンには書けない)。トリガーの既定 0.1 は、XInput が 30/255(0.12)を閾値にするのに合わせた推測の値。
- `Pad` に `LeftTrigger`・`RightTrigger`・`LeftShoulder`・`RightShoulder` を足した(末尾。既存の値は変えない)。アダプターはトリガーを元から報告していた(`Gamepad_LeftTriggerAxis`・`Gamepad_RightTriggerAxis`)。
- スティックの片方の軸: アダプターがスティックの動きを、2 軸の制御(`Gamepad_Left2D`)に加えて、軸ごとの 1D の制御(`Gamepad_LeftX`・`LeftY`・`RightX`・`RightY`。UE のキー名)としても報告する(その軸の値が変わったときだけ)。片方の軸の割り当ては、その 1D の制御に付く。2 軸の制御に付けて片方を 0 にする案は採らなかった: 不感帯が倒した長さに掛かり、X をほとんど倒していないのに Y を倒すと、X が不感帯を超えて出てしまう(UE の 1D の制御は軸ごとに掛かる)。
### 2. `Button.HeldSeconds`
- 押したフレームは 0、押している間は押してから今のフレームまで、離したフレームはそれまで押していた時間、それ以外は 0。同じフレームで押して離したら 0。離したフレームに押し直したら、離した値を返し、新しい時計を 0 から始める。
- 時計は `InputServer.TrackHold` が操作ごとに持つ(生成された `Read` は静的で状態を持てないため)。各フレームの経過時間(`Read` の `DeltaSeconds`)を足す。同じ `FrameNumber`(`ProcessEvents` の呼び出しの回数 = 表示したフレーム)の読み直しは進まない。複数の System が同じ構造体を受けると、`Read` はその数だけ呼ばれるため。負・NaN・無限大の経過時間は 0 とみなす。
- 離したフレームの値は、そのフレームの経過時間を含む(押したフレームから離したフレームまでの経過時間。UE の `ElapsedSeconds` との差は最大 1 フレーム。UE のソースは読んでいないので、差は推測)。
- `Button.ReleasedBefore(秒)`: 離したフレームで `HeldSeconds < 秒`。ちょうど同じ長さは偽(UE の `Less` は厳密な比較で、ちょうど `PressedThreshold` は長押し)。`Read` の呼び出しの形(`Server, DeltaSeconds, CursorPointer`)は変えていない。
### 3. マウスと右スティックの片方の軸
- `MouseAxis.DeltaX`(横だけ)・`DeltaY`(縦だけ。上が正)を `Look` に割り当てる。`InputMaskModifier`(新しい。選んだ軸を 0 にする)が、`Delta` の 2D の制御の反対の軸を消す。`DeltaY` は、これまでの `Delta` と同じく Y の符号を反転する(上へ動かすと上を向く)。
- `Look` のスティックは `StickAxis` で片方の軸だけも選べる(Y は `InputSwizzleModifier` で `Look` の Y へ移す)。
### 4. 自動実行の入力
- `CGE_AUTOMATION_INPUT` に `コード=値@開始-終了` を足した。`GamepadLeftX`・`LeftY`・`RightX`・`RightY`・`LeftTrigger`・`RightTrigger` は範囲の間その値で、外は 0。`GamepadSouth`・`East`・`West`・`North`・`LeftShoulder`・`RightShoulder` は 0 以外で押す。`MouseDeltaX`・`MouseDeltaY` は範囲の各フレームにその値のカウントだけ動かす。値を持たないコード(キー・マウスのボタン)に `=` を付ける、値が有限でない、未知の `Gamepad*`・`MouseDelta*` は、起動時に拒否する(黙って何も押さない実走にならないように)。
- 自動実行のウィンドウは実機のゲームパッドを読まない(これまでどおり)。コントローラーは最初の呼び出しで接続され、それまでに設定した値を保つ(`Win32InputEventAdapter.InjectGamepad`)。
### 5. TopDown(`ClickToMove`、`ClickDestination`)
- UE のとおり: 押した瞬間に `MoveIntent.Stop()`(`StopMovement`)して、カーソルの下の地面を覚える。押している間は、地面が見つかれば覚え直し、Pawn の位置からその点への 3 次元の単位ベクトルを `MoveIntent.Direction` に書く(`Follow`。見つからないフレームは入力 0)。離した瞬間に、`ReleasedBefore(0.5)` なら覚えた地面へ `MoveTo`(M0106 のナビメッシュの経路)と目印 `Spawner.Spawn<CursorMarker>`、そうでなければ(長押し)`Stop()` だけ。
- `CachedDestination` は、キャラクターの Entity に付ける Component `ClickDestination`(`Point`・`Found`)で持つ。System に状態を持たせるとスナップショットに入らず、再生で食い違うため。UE の `CachedDestination` は (0, 0, 0) で始まり、地面を見つける前に離すと原点へ歩くが、こちらは `Found` が偽なら何もしない(推測: UE の挙動が意図ではなく、原点へ歩かせる理由は無い)。
- `PressedThreshold`(0.5 秒)は System の定数 `ClickToMove.PressedThreshold`(UE は Blueprint の変数)。カーソルは表示のまま(TopDown は `CaptureMouse` なし)で、クリックのイベントは `Picking.TryPick`。
### 6. Vehicle
- `PlayerActions`: `Axis Throttle`(W・Up・右トリガー・西ボタン)、`Axis Brake`(S・Down・左トリガー・南ボタン)、`Axis Steering`(D/Right、A/Left、左スティックの横)、`Button Handbrake`(Space・左ショルダー)、`Look LookAround`(マウスの横・右スティックの横)。`VehicleControls` は `Drive = Throttle - Brake`(簡易の車両モデルの入力は符号つきの 1 つ)、`Steering`、`Handbrake.Held`。
- ハンドブレーキのゲームパッドは、これまでの南ボタンから左ショルダーへ変えた(UE の `IA_Handbrake` は左ショルダーで、南ボタンは `IA_Brake`。南ボタンを両方に割り当てると、ブレーキとハンドブレーキが同時に効く)。
- `LookAround` は宣言だけで、使う System は無い(後ろの腕を回す処理は Chaos の車両と同じ M0109)。Vehicle のウィンドウはマウスを捕捉しないので、マウスの移動は届かない(捕捉の切り替えも M0109 で決める)。
### 7. パッケージ
- SDK の内容が変わったので `build/CSharpGameEngine.Version.props` を 0.1.19 にした(`SceneProjectOptions.EngineVersion` は 0.1.16 の固定でこの版とは連動しない。プラグインの `EngineVersion` の範囲 `>=0.1.16 <0.2` も満たす)。

## 実装したもの
| 層 | 中身 |
|---|---|
| L0/L1 Core | `Axis`、`Button.HeldSeconds`・`ReleasedBefore`、`Pad` の追加、`StickAxis`、`MouseAxis.DeltaX/DeltaY`、`[NegativeKeys]`、`[Gamepad]` の `DeadZone`/`Scale`/`Invert`/第 2 引数 |
| L0 Windows | `InputServer.TrackHold`・`FrameNumber`、`InputMaskModifier`、`Win32InputEventAdapter` の軸ごとの制御と `InjectGamepad`、`Win32Window.InjectGamepad`・`InjectMouseDelta` |
| L7 | `InputActionsEmitter`(`Axis`、`NegativeKeys`、片方の軸、`Button` の `TrackHold`、INPUT005) |
| L5 Windows | `RuntimeAutomation` の `コード=値@開始-終了`、`MouseDelta*`、`Gamepad*` |
| L9 | TopDown(`ClickToMove`、`ClickDestination`、`TopDownCharacter.json`)、Vehicle(`PlayerActions`、`VehicleControls`)、`build/CSharpGameEngine.Version.props` を 0.1.19、理想コード(`Templates/TopDown/Code`・`Templates/Vehicle/Code`)と README を実装の書き方に更新 |
| ツール | `ClickToMoveVerification`(目印のフレーム、長押しの実走)、`VehicleDriveVerification`(アナログの 2 回の実走) |

## 検証
- 単体テスト: `CSharpGameEngine.Core.Tests`: `InputActionValueTypesTests`(`Axis.FromSum` の境界と NaN・無限大、`ReleasedBefore` のちょうどの境界)。`CSharpGameEngine.Windows.Tests`: `InputActionBindingTests`(`TrackHold`: 押した瞬間 0・各フレームの足し算・離した瞬間の合計・ちょうど 0.5 秒・同じフレームの読み直しで進まない・同じフレームの押して離す・離したフレームの押し直し・見えなかった押下・負と NaN と無限大の経過時間・操作ごとの独立、トリガーの不感帯と倍率と反転、キーのハンドルが打ち消し合う、軸ごとの不感帯、スティックの Y から `Look` の Y、マウスの片方の軸、`InputMaskModifier`、アダプターの軸ごとの制御と `InjectGamepad`)、`Win32InputEventAdapterTests`(更新)、`RuntimeAutomationTests`(値つきの入力と拒否 10 通り)。`CSharpGameEngine.CodeAnalysis.Tests`: `InputActionsGeneratorTests`(`Axis` の割り当て・片方の軸・`HeldSeconds` の生成、型に合わない割り当て 16 通りの INPUT003、不感帯と倍率の範囲外 9 通りの INPUT005、範囲の端 5 通りは受理、ボタンへの指定)。`CSharpGameEngine.FeatureRunner.Tests`: `ClickToMoveVerificationTests`(目印が押した瞬間に出たら不合格、離したフレームなら合格、長押しの判定)、`VehicleDriveVerificationTests`(アナログの判定、オン・オフの動きを弾く)。
- 実走(WARP、リポジトリのビルド): 次の「観測」。画像: `.artifacts/m0108/`(TopDown の押している間、離した直後の目印、長押しの途中)、`.artifacts/feature-evidence/rigid.character-controller/`(各テンプレートの最後のフレーム)。画像は目で確かめた。

## 観測(最終のコードから)
実走は WARP(`CGE_GRAPHICS_ADAPTER=warp`)。D3D12 のデバッグレイヤーのエラーはすべて 0。
| テンプレート | 値 | 修正前 | 注 |
|---|---|---|---|
| ThirdPerson | 移動 15.700 m、上昇 1.228 m、下降 1.228 m、足音 15 回 | 15.700 / 1.228 | 変化なし |
| FirstPerson | 移動 16.000 m、上昇 0.863 m、下降 0.863 m | 16.000 / 0.863 | 変化なし |
| Vehicle(キー) | 移動 48.281 m、曲がり 38.3°、HUD 28 状態 | 48.281 / 38.3° | 変化なし(`Axis` でも W は 1、D は +1 で、これまでの `Stick` と同じ値が `Intent` へ行く) |
| Vehicle(右トリガー 0.5 + キーのハンドル) | 移動 26.889 m(キーの 0.56 倍) | — | 新しい実走。アナログの値がそのまま効く |
| Vehicle(キーのアクセル + 左スティック 0.5) | 曲がり 31.5°(キーの 0.82 倍) | — | 新しい実走 |
| TopDown ナビメッシュなし(短押し) | 移動 9.606 m、最後の動き 0.000 m、描画物 56 → 57 → 56、目印のフレーム 5(離したフレームから) | 9.865 m | **意図した変更**: 下の「TopDown の値が変わった理由」 |
| TopDown ナビメッシュあり(短押し) | 移動 9.636 m(経路 15.44 m)、(6.79, 6.84) へ着いて目的地の誤差 0.01 m、壁の端まで 0.39 m、1 フレームの落ち最大 0.40 m | (6.75, 7.14) | **意図した変更**: 同じ理由 |
| TopDown 長押し | 押している間に 17.414 m(台から落ちるまで直接歩く)、離したあと 0.378 m で止まる、描画物 56 → 56(目印なし) | — | 新しい実走 |

- TopDown の値が変わった理由(Fact): 短押しの判定(0.5 秒未満)のため、実走のクリックを 5 フレームから 4 フレームにした(5 フレームは `HeldSeconds` がちょうど 0.5 秒になり長押しになる。上の「事実」)。押している間にカーソルの下の地面を選び直すので、離すのが 1 フレーム早いと目的地が変わる。切り分けの実験: クリックを 4 フレームにして、M0107 の `ClickToMove`(直接歩く処理なし)のまま走らせると、ナビメッシュなし 9.625 m、ナビメッシュあり (6.63, 6.67) だった。したがって変化(9.865 → 9.606 m)の大半はクリックの長さで、押している間の直接歩き(`Follow`)が加えた差は、ナビメッシュなしで 0.02 m、ナビメッシュありで約 0.2 m。`ClickToMoveVerification` の目的地の期待値は (6.79, 6.84)(許容 0.5 m)にした。
- 画像: `topdown_frame_22.png`(押している途中。目印なし)、`topdown_frame_27.png`(離した 2 フレーム後。遠くの目的地に目印)、`topdown_longpress_36.png`(長押しの途中。キャラクターは台から降りて直接歩いている)。

## 実装中に見つけた事実
- 目印の出るフレームの判定は、押した瞬間に出ていた M0105 の実装を弾く形にして、確かめた(`Evaluate_RejectsAMarkerThatAppearsWhileTheButtonIsStillDown`)。
- 自動実行のフレームの時間は 8/60 秒だが、`Read` が 0.1 秒に頭打ちするので、実走の `HeldSeconds` は 0.1 秒刻み。UE の `PressedThreshold` 0.5 秒との境界(5 フレーム)を避けるのに、このことが要った。
- 半分のトリガーは、そのままの比ではなく不感帯のあとの値(0.5 → 0.444)になる。実走は「キーより短い」ことだけを判定し、比の範囲は 0.9 倍以下とした。

## 範囲外(未実装)と不確実性
- `Touch`(`IA_SetDestination_Touch`)、ハンドルのコントローラー(`IMC_SteeringWheel`)、XR: ADR-0078 §2 で作らないもの。
- `IA_Brake` のブレーキランプ、`IA_Handbrake` のブレーキランプ、`IA_LookAround` の後ろの腕、`IA_Reset`、`IA_ToggleCamera`、`IA_Headlights`、Chaos の車両: M0109。
- `Drive = Throttle - Brake` は簡易の車両モデルの都合(`VehicleIntent` は符号つきの 1 つ)。UE は別々の入力(`SetThrottleInput`・`SetBrakeInput`)で、両方を踏んだときの挙動が違う(こちらは打ち消し合う)。
- 実機のゲームパッド・マウスは確かめていない(自動実行は実機の入力を読まず、注入した値だけを使う)。アダプターの変換(XInput の値から制御へ)は単体テストで確かめた。トリガーの不感帯の既定 0.1 は推測。
- UE の `ElapsedSeconds` との 1 フレームの差(上の決定 2)は推測。
- 実機の GPU は WARP のみ(これまでと同じ)。VS の実機での新規プロジェクトは確かめていない(`verify-template-package` は SDK パッケージから作って通した)。
- `[Gamepad]` の `DeadZone` は、2 軸の `Stick`/`Look` では倒した長さ、片方の軸では軸ごと(上の決定 1)。同じスティックを両方に割り当てると、不感帯の掛かり方が違う。

## 完了の判定
- 依頼への答え: M0108 の行(TopDown の `PressedThreshold`・Started・Triggered・Completed、`FX_Cursor_Success` の出る条件、Vehicle の `IA_Throttle`)を実装し、`UE-Template-Behavior.md` の状態列を実装済みへ更新した。`Axis`・`HeldSeconds`・片方の軸を、単体テスト(境界・退化入力・生成器の診断)と WARP の実走(短押し 2 種・長押し・アナログ 2 種)で確かめた。
- 意図と観測の一致: MatchesWithDocumentedExtension(上の「範囲外」)。
- 全テスト: `dotnet test CSharpGameEngine.slnx -c Debug --nologo -v q` を最後まで実行し、22 プロジェクトで失敗 0(警告なし)。`CSharpGameEngine.Windows.Tests` 740 合格(2 スキップ = 環境変数で有効にする性能の証拠のテスト、元から)、`VSExtension.Tests` 1197、`FeatureRunner.Tests` 222、`CodeAnalysis.Tests` 338、`Core.Tests` 154(`SolutionProjectRegistrationSpec` を含む。このマイルストーンでプロジェクトは増えていない)、`Navigation.Tests` 57。出力は `.artifacts/m0108/full_test.txt`。M0107 の時点より、Windows.Tests +40、Core.Tests +22、CodeAnalysis.Tests +35、FeatureRunner.Tests +21。
- 実走: `verify-thirdperson-character`(ThirdPerson・FirstPerson・TopDown・Vehicle)と `verify-template-package`(ThirdPerson・TopDown・Vehicle。SDK 0.1.19)が通った(出力は `.artifacts/m0108/package_*.txt`、`verify_*.txt`)。
