# ADR-0086 — 物理を Q64.64 のまま全スケール(±9.2e18 m)で動かす: ペアごとの局所原点、物体相対の静的メッシュ、複数ドメインの同時進行、物体ごとの Fault
- 状態: Accepted(2026-10-25。ユーザーの依頼:「対数レンダリングすることで物理エンジンもその論理座標にネイティブに書き換えて」。計画はユーザーが承認し、「Q64.64 のまま全スケール対応」を選んだ。対数空間で物理を解く案は採らない)
- 関連: ADR-0063(正準数値と階層 World)、ADR-0049(決定的な物理ドメイン。一部 Superseded)、ADR-0076・ADR-0084(対数深度)、ADR-0087(層の配置)、ADR-0078(UE テンプレートの完全移植)、ADR-0067(キャラクターの移動)、M0122、[Physics 仕様](../Specifications/Physics.md)

## 背景
- 物理はすでに Q64.64 の `LogicalScalar`(`Int128`、±9.22e18 m、分解能 2^-64 m)の局所座標でネイティブに動いている。ソルバー内部(積分、拘束、接触、狭域判定、Dynamic AABB Tree、レイキャスト、スイープ、CCD、スリープ)に `float`/`double` は無い(`src/CSharpGameEngine.Runtime.Physics`。`Docs/Specifications/Physics.md:31`)。float が入るのは定数(`FromSingle`)、glTF の取り込み、入出力の境界だけである。
- 対数は GPU の深度の書き方であり、座標系ではない。位置の正本は Q64.64 のままで、カメラ相対の float への変換は 1 か所だけ(ADR-0063、ADR-0076)。Omni の Charter も「論理座標対応は、物理・照明・ベイクを対数空間で計算することと同義ではない」と明記し、Omni に Logical 空間の物理エンジンは無い。対数で圧縮した空間で解くと、積分・慣性・貫入の線形性が崩れるので採らない。
- 「全スケール対応」で実際に残っている問題(調べた事実、2026-10-25):
  - 次数による桁あふれ。GJK の `Cross(Cross(AB,AO),AB)` は L³、その `LengthSquared` は L⁶(`PhysicsNarrowPhase.cs:857,900-901,927`)で、約 1.4 km で桁あふれする(`Physics.md:35` は床を 100 m の格子に分けて回避している)。広域判定の表面積は L²(`DeterministicAabbTree.cs:216-219`)で約 1.2e9 m。静的メッシュはドメインの絶対座標で焼き込み(`StaticMeshCollision.cs:54-61,131-142`)、サポート関数の `Dot` が絶対座標の 2 乗になるので約 3e9 m。`LogicalVector3.LengthSquared` は演算子で例外を投げ(`LogicalVector.cs:8-11,28`)、`TryNormalize` は約 3.04e9 m を超えると Try なのに例外を出す。
  - `Step` の try/catch は積分だけを囲む(`DeterministicPhysicsWorld.cs:210-225`)。`BuildContacts`・`SolveContacts`・最後の `Broadphase.Upsert` は外にあり、桁あふれは生成ゲームループを止める。Raycast の `Direction.LengthSquared` と Sweep も try の外。0 除算(`DivideByZeroException`)は `OverflowException` の catch に入らない。
  - 進むドメインは `Active` の 1 つだけ(`PhysicsDomainRegistry.cs:19-24`、`PhysicsSystems.cs:69-78`。`SetPhysicsFrame` を呼ぶのはテストだけ)。
  - float の入口: シーン JSON の `LogicalScalar` は float リテラルとして生成され(`SceneJsonSceneAnalyzer.cs:617-633`)、作者の置いた位置が 1e7 m 付近で 1 m 刻みに丸まる。`PlayerControl` は `MathF.Sin/Cos` をシミュレーションの入力にする。`Picking.TryCreateRay` は `MathF.Tan` を使い最大 2000 m。`FixedDelta = FromSingle(1f/60f)` と `State.FixedTickRate` の二重定義。`PhysicsDebug` はドメインの絶対座標を float にする。
  - 静的な Collider は親・回転・スケールを無視し、動的物体の親とスケール≠1 は拒否する(`PhysicsSystems.cs:21-48`)。`ComputeStateHash` に回転・角速度が入らず、物理のゴールデンハッシュは無い。
- ADR-0049 は今も「局所 Q32.32 剛体 Domain」と書いていて古い(ADR-0063 と M0067 で Q64.64 になっていた)。

## 決定

### 1. 土台の規則: 絶対座標は加算と減算にしか入れない
- 固定小数点の加減算には丸めが無い。この規則を守れば、(a) 位置が大きくても桁あふれせず、(b) シーン全体をどれだけ平行移動しても、相対的な結果がビット単位で一致する(試験で保証する)。乗算・除算・平方根・内積に絶対座標を入れない。

### 2. ペアごと・照会ごとの局所原点
- 狭域判定(`TryCreateContact`、`CollectMeshContacts`、`TryRaycastMesh`)の入口で、原点 `O` を引いた局所の形にして既存のカーネルを動かし、出た点に `O` を足して返す。`O` は平行移動と一緒にちょうど同じだけ動く量にする: 凸どうしは `First.Position`、凸とメッシュは凸の `Position`、`CollectContacts` は照会する形の `Position`、Raycast は候補ごとに光線がその候補の AABB に入る点(`DivideSaturating` のスラブ法)、Sweep は `Origin`、CCD は開始時の外接箱の中心 `Min + (Max − Min)/2`。
- 絶対座標への乗算・除算を監査して直す: CCD の `(Min+Max)/2`、`PhysicsShapeAssets.TriangleCenter` の `(A+B+C)/3`、`CharacterMotor.cs:281` の `(Free+Touching)*Half`、Vehicles・FootIk・OrbitCamera・Navigation。
- 局所化して結果が変わるのは絶対座標の中点だけで、原点の偶奇で偶数丸めの向きが変わるため差は最大 1 ulp(2^-64 m)。

### 3. 桁の縮め方 `PhysicsKernelScale`
- カーネルの入口で、相対量を 2 の冪で縮めて高次の積の桁あふれを消す。`Exponent(Magnitude) = Max(0, MagnitudeExponent + 1 − ExactBits)`、`ExactBits = 8`(最大成分が 2^8 m 未満なら縮めず、今の普通のシーンの結果とビット一致する)。縮めた後の値は 2^8 未満、ミンコフスキー差は 2^9 未満で、次数 6 でも 2^54 に収まる。精度は `2^(k−64) m`。`k` は相対量だけから決まるので、平行移動しても変わらない。
- 対象のカーネル: GJK/EPA(L⁶)、`ClosestPointOnTriangle`・`InsideTriangle`・`ClosestSegmentSegment`・`ClosestSegmentTriangle`(L⁴)、Möller–Trumbore(L³)。次数 2 以下のもの(箱どうしの SAT、球どうし、球と箱)は縮めない。
- `MagnitudeExponent` は 0 のとき `int.MinValue` になるので、大きさ 0 は `Exponent = 0` として呼ぶ前に特別扱いする(unchecked では `int.MinValue + 1 − 8` が巨大な正の数に巻き戻る)。
- 退化した三角形の判定(`PhysicsShapeAssets.cs:72`、`StaticMeshCollision.cs:66` の `Cross(...).LengthSquared.IsZero`)は L⁴ なので、しきい値を変えないために `TryLengthSquared` の結果の `IsZero` を使い、あふれたときは別に扱う。

### 4. 上限を契約に書く(`PhysicsLimits`、L4)
- 形 1 つの寸法(半辺・半径・半高さ・凸包の頂点・三角形の辺)は 2^28 m(約 2.7 億 m)以下、メッシュのローカル座標は 2^60 m 以下、光線は 2^62 m 以下、動的物体は 質量 × 寸法² ≤ 2^62。超える入力は `Upsert` が `InvalidShape`/`InvalidMass` で拒否する。
- 慣性は `Upsert`/`Synchronize` で 1 回だけ計算して `Body` に持たせる(今は力積を加えるたびに計算し直している)。計算があふれたら `InvalidMass`。

### 5. 広域判定
- `DeterministicAabbTree` のコストを表面積(L²)から半周長に替え、`UInt128` の生の値で計算する(各軸で `((UInt128)Max.Raw − (UInt128)Min.Raw) >> 2`、3 軸分を足す)。照会の結果はもともと StableId の順に並べ替えるので、木の形が変わっても結果は変わらない。
- `QueryRay(Origin, Direction, MaxDistance, Results)` を足す(スラブ判定は `SubtractSaturating`/`DivideSaturating`)。

### 6. 静的メッシュ
- 実行時の取り込みのまま、頂点に掛けるのは世界変換の線形部分 `L` だけにして、物体相対にする(`PhysicsShapeAsset.CreateTransformed(Source, L, Id)`)。物体の Position は平行移動、Rotation は Identity。`L` が単位行列なら元のアセットをそのまま使う。キャッシュの鍵は (資産, `L`)で、平行移動だけが違う配置は 1 つのアセットを共有する。
- 静的な Collider は `PhysicsBodyPlacement.TryResolve` で親・回転・スケールを尊重する(親が無ければ Transform の値をそのまま使う。親があれば `TryResolveWorld` の結果を `TryDecompose`)。Box は倍率を HalfExtents と Center に掛け、Sphere は 3 軸の |倍率| が等しいときだけ、Capsule は |R| = |F| のときだけ許す。Convex・Mesh は `CreateTransformed`。せん断や条件に合わないものは `InvalidScale`。動的物体の親・スケール≠1 の拒否は今のまま。
- 調理(ビルド時)の `cook-static-mesh` は採らない(SDK に Physics.Cli を同梱する作業と 4 テンプレートのパッケージ検証が増え、全スケール対応には要らない)。

### 7. 複数ドメインの同時進行
- `PhysicsDomainRegistry` から `Frame`/`SetFrame`/`Active` を消し、`Domain(ReferenceFrameId)`・`TryGetDomain`・`Domains`(StableId の順)・`Configure(Frame, PhysicsWorldSettings)` を持たせる。`PhysicsStep` は全ドメインを StableId の順に逐次進める(並列にしない。割り当て 0 の試験と両立しない)。同期は `Domain(Transform.Frame)` へ送る。違うドメインの物体どうしは接触しない。
- ドメインをまたぐとき(`TryTransfer`)、姿勢と速度は 2 tick のフレーム変換に通した体の位置の差から速度を求める(フレームが回っているとき `ω×r` の項が入るため、スナップショットの差 × TickRate は平行移動しか扱えない)。
- Bake とキャッシュはルートドメインだけ。

### 8. 桁あふれと 0 除算は物体ごとの Fault
- `Step` の最初に各物体の tick 開始の姿勢と速度を残し(事前確保、割り当て 0)、`ArithmeticException`(`OverflowException` と `DivideByZeroException`)を `ApplyCommands`・積分・`Broadphase.Upsert`(物体ごと)・`BuildContacts`(ペアごと)・`SolveContacts`・Raycast・Sweep で捕まえる。Fault になった物体は tick 開始の姿勢へ戻し、速度を 0 にして眠らせ、接触を無効にして Fault イベントを出す。`Synchronize` が毎回 `Faulted` を戻す処理(`:156`)はやめ、解除するのは `Upsert`・`Teleport`・`SetVelocity` の命令、`MotionType` か形の変更だけにする。`Step` と照会は例外を外へ出さない。
- `Physics.md` の「overflow は直前の有効な pose へ rollback して Fault event を発行する」は、積分と命令にしか当てはまっていない。実装と試験を足してから書き直す。

### 9. float の入口を Logical にする
- シーン JSON の `LogicalScalar` は `LogicalScalar.ParseInvariant("<十進>")`(既存の `SceneJsonDecimal.TryPositional` で指数表記を固定小数の表記にし、|x| < 2^63 を厳密に検査)として生成し、作者の値を丸めない。`SceneValueReader`(デザイナー)と `SceneJsonConverter` も同じ形を読み書きする。
- `PhysicsWorldSettings(TickRate, Gravity)`: `FixedDelta = One / FromInt64(TickRate)` で、キャラクターと車両と同じ値にする。標準重力は `ParseInvariant("9.80665")`。前後で全テンプレートの移動の値が変わりうるので、前後の表を M0122 の文書に残す。
- `PlayerControl` は `LogicalMath.TrySinCos` を使う(`DegreesToRadians` は `LogicalMath` へ)。`ViewAngles` と `CameraLag` は表示用なので float のまま。
- Picking の光線は Logical で作り(tan は `TrySinCos` と `TryDivide`)、2000 m の上限を消して `PhysicsLimits` の光線の上限を使う。`PickHit.Distance` は `LogicalScalar`。カメラのフレームのドメインを使う。
- `PhysicsDebug` は、要求で受け取った Origin(float を厳密に変換)からの相対値を返し、応答に Origin と Frame を含める(プロトコル 3)。

### 10. Numerics(L1)に足すもの
`LogicalScalar.MagnitudeExponent`・`TryScaleByPowerOfTwo`・`SubtractSaturating`・`DivideSaturating`、`LogicalVector3.TryLengthSquared`・`TryLength`・`MaximumMagnitude`・`IsZero`・頑健な `TryNormalize`(最大成分が [2^-16, 2^30) なら今の式とビット一致)、`LogicalQuaternion.TryFromRotationAxes`、`LogicalAffineTransform.TryDecompose`(せん断は false)、`LogicalMath.DegreesToRadians`。

## 範囲外
- 対数空間での物理(不採用)。Omni の Q95.96 への拡張。浮動原点(rebase。ドメイン座標が正本でなくなる)。double の局所座標(NUM001 と決定性に反する)。
- 箱・球・カプセル・凸の光線判定が外接 AABB だけであること、Sweep の近似、`ContinuousStatic` の外接箱、動的物体の親子・倍率・複合形状、GPU ソルバー、惑星のように向きが変わる重力(`Physics.md` の既知の制限に残す)。

## 検証
- `PhysicsTranslationInvarianceTests`: 6 基準場面 × オフセット(0、1.5e3、1.3e9、3.1e9、1e12、2^50、1234567890123.456、4.6e18、`MaxValue − 1e4`、`MinValue + 1e4`)× 4 方向(軸 3 つと斜め)で、原点からの相対状態ハッシュ・イベント・マニフォールド・照会の結果がビット一致する。Debug と Release の両方。修正前の失敗(例外の型と場所)を記録する。
- `PhysicsGoldenStateTests`(`ComputeStateHash(Origin)` に回転・角速度・SleepTicks・Faulted を含める。tick 1・60・最後を固定)、`PhysicsOverflowFaultTests`(あふれ、0 除算、極小方向の CCD)、`PhysicsMultiDomainTests`、`FixedPhysicsMathTests` の拡張、Numerics の新 API の試験、既存の `CharacterMotorTests`(25)・`ChaosVehicleTests`(101)・Physics の 9 ファイルの回帰。
- ベンチ: Physics.Cli の `bench`。オフセット 0 で分割前の基準の 1.10 倍以内、1e18 でオフセット 0 の ±5 %。
- 実走: ThirdPerson と Vehicle を 2^50 m ずらしても原点と同じ値。Stage `rigid.full-scale`。

## 関連する機能
- F-L5-047(Full-scale deterministic rigid bodies)、F-L5-003、F-L1-009、F-L6-002、F-L5-006
