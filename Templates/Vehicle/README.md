# Vehicle(理想コード・提案)

UE5.8 の Vehicle テンプレート(`TP_VehicleAdv`)相当。**このフォルダのコードはビルドできない理想コード**(仕組みは [`../README.md`](../README.md))。
ビルドできる実装は [`../CSharpGameEngine.Templates.Vehicle`](../CSharpGameEngine.Templates.Vehicle)(Chaos 相当の車。M0109)。

- `Code/VehicleControls.cs`: 操作を `VehicleIntent`(`Throttle`・`Brake`・`Steering`・`Handbrake`・`Reset()`)へ写す。エンジン・変速・サスペンションは `Vehicle` が解く。実装と同じ形。
  カメラの切り替え: 理想は `CameraRig.Next()`。実装は、前と後ろのカメラに付けたマーカー `VehicleCameraSwitch` の `Camera.Active` を Tab で反転する(UE の FlipFlop と同じ結果。描画は、表示中のカメラが無効になると有効な別のカメラへ切り替える)。
- `Code/Speedometer.cs`: HUD(子の `ScreenText`)に速度とギアを出す。車(親)の状態を `Parent<VehicleState>` で読む。実装は数字ごとに 2 つの System(`SpeedometerSpeed` は `ForwardSpeed` × 3.6 を 3 桁、`SpeedometerGear` は `Gear` を R/N/数字)。
- `Code/EngineSound.cs`: エンジン音は「状態に追従する音」なので、`AudioSource` の音程と音量を回転数・アクセルに結ぶ(`VehicleState.EngineRpm` と `EngineThrottle` は実装にある)。**未実装**: 音の仕組みは一発の再生だけで、ループと音程の変更が無い。UE のテンプレートにも音の資産は無い。
- `Scenes/SportsCar.json`: 車輪・エンジン・ギア比を `Vehicle` に宣言する。実装との違い:
  - エンジン・変速・操舵・車輪とサスペンションの値は、シーンではなくデータアセット `[DataAsset] VehicleSetup`(`Assets/Vehicles/SportsCar/SportsCarSetup.cgeasset`)。シーンからデータアセットを参照する仕組みはまだ無いので、ゲームのコード(`UseSportsCarSetup`)が `Vehicle.Setup` に入れる。
  - シーンの `Vehicle` は車輪の取り付け(重心から見た位置と `Front`/`Rear`)だけを持つ。車輪の見た目は、子の車輪に付けた `VehicleWheelVisual`(何番目の車輪か)が、回転・舵角・サスペンションの上下で動かす。
  - 車体は `Collider`(箱)と `RigidBody`(動的、1500 kg、角速度の減衰 3)の剛体で、根の位置は重心。
  - ブレーキランプは `MeshEmissive`(マテリアルの発光の倍率)、ヘッドライトは光の隣の `LightVisibility`。

未実装の機能(エンジン側の課題):
- 地形(`Terrain`。UE は Landscape。M0112)。
- エンジン音(ループと音程の変わる音)。
- UE の制約のサスペンション(ソルバーの中のばねとハードストップ)。今は UE の力のサスペンションで、縮みきると車体の箱が床に当たって支える。
