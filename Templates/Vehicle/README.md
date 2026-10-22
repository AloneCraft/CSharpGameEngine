# Vehicle(理想コード・提案)

UE5.8 の Vehicle テンプレート(`TP_VehicleAdv`)相当。**このフォルダのコードはビルドできない理想コード**(仕組みは [`../README.md`](../README.md))。
ビルドできる実装は [`../CSharpGameEngine.Templates.Vehicle`](../CSharpGameEngine.Templates.Vehicle)(簡易の車両モデル。M0101 段階 9)。実装との違い: 車両は `Vehicle` の車輪の配列ではなく、
ホイールベース・トラック・最高速度・加速度などの値で宣言する。操作は `Stick Drive` と `Button Handbrake` の 2 つ。速度計は `ScreenText` ではなく `Hud`(タイトルバー)。エンジン音と
カメラ切り替えは未実装。

- `Code/VehicleControls.cs`: 操作を `VehicleIntent` へ写す。エンジン・変速・サスペンションは `Vehicle` Component が解く。カメラ切り替えは `CameraRig.Next()`。
- `Code/Speedometer.cs`: HUD(子の `ScreenText`)に速度とギアを出す。車(親)の状態を `Parent<VehicleState>` で読む。
- `Code/EngineSound.cs`: エンジン音は「状態に追従する音」なので、イベントではなく `AudioSource` の音程と音量を回転数・スロットルに結ぶ。
- `Scenes/SportsCar.json`: 車輪・エンジン・ギア比を `Vehicle` に宣言する。`Wheels` の `Steer` / `Drive` / `Handbrake` が役割。

未実装の機能(エンジン側の課題):
- 車両物理(車輪・サスペンション・トルク・変速)。
- 画面上の文字(`ScreenText`)。
- 地形(`Terrain`。UE は Landscape)。
- `Axis` 入力(トリガーのアナログ値)。
