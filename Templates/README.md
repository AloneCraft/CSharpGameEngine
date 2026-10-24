# templates — 次世代テンプレートの理想コード(提案・未実装)

UE5.8 の基本テンプレートと同じ名前のテンプレート群。`ThirdPerson` / `FirstPerson` / `TopDown` / `Vehicle`。
これらは「新規プロジェクトのテンプレート」であり、同時に既定のサンプルでもある。

既存の `CSharpGameEngine.Templates.ThirdPerson`(現行の出荷物。VSIX のプロジェクトテンプレートの原本)とは内容が重複する。
重複は承知の上で、役割が「テンプレート」なのでこの場所に置く。移行が終わったら、現行のフォルダを置き換えて名前を寄せる。

**ここにあるコード(`Templates/ThirdPerson` など、`CSharpGameEngine.Templates.ThirdPerson` 以外)は、まだビルドできない。** 「ユーザーがこう書けたらいい」と思えるコードを先に書いたもので、コンパイルエラーになってよい。
流れは次のとおり。

1. 理想コードを書く(このフォルダの `ThirdPerson` など4つ)。
2. ユーザーが確認し、理想コードを書き直す。
3. 書き直された理想コードから逆算して、生成器(Roslyn)とランタイムを作り替える。この段階で csproj・slnx 登録・ADR を足す。

このフォルダには意図的に `.csproj` を置いていない(`SolutionProjectRegistrationSpec` の対象外、既存のビルドに影響なし)。

## 方針

UE と Unity から引き継いだ仕組みと名前を清算し、**ECS だから**、**Roslyn だから**できる書き方に寄せる。

- ユーザーが書くのは「Component(データ)」と「System(1つの責務だけの関数)」と「シーンJSON」。
- 順序・入力ID・資産パス・初回フラグなど、機械が決められることは書かせない。
- 1つのSystemは、主に3〜5行。コメントは「なぜ」だけ。

## 仕組み(理想コードが前提にするもの)

| # | 仕組み | 理想コードでの現れ方 | 根拠 |
|---|---|---|---|
| 1 | 型付きアセット `Assets.*` | `Audio.Play(Assets.Sounds.Jump,…)`。`Assets/Sounds/Jump.wav` が無い・種類が違えばコンパイルエラー。JSON のパスもビルドで検査 | Roslyn が `Assets/**` を AdditionalFiles で読める |
| 2 | 入力を構造体で宣言 `[InputActions]` | `PlayerActions` の `Move` / `Look` / `Jump`。GUID・文字列キー・据え付けフラグが消える。`Button.Pressed/Held/Released` | Roslyn が宣言から入力マップと ID(メンバー名由来)を生成。ECS のシングルトンとして毎フレーム渡す |
| 3 | 使うものだけを引数に取る | `ref State` を書かない。`Audio` / `Physics` / `Spawner` は型で受け取る | 引数の型と ref の種類で役割が決まる(ADR-0033 の延長) |
| 4 | 親の Component を型で読む `in Parent<T>` | `CharacterAnimation`、`ArmsFollowView`、`Speedometer` | ECS のリレーション。生成器が親の Archetype を解決する |
| 5 | イベントを型で受ける | `Update(in Landed Event,Audio Audio)`。イベント1件ごとに呼ばれる | ECS の1フレームだけ存在するデータ。送り手→受け手の順序は自動 |
| 6 | フェーズと順序をデータの流れから推論 | `[SystemPhase]` / `[RunsAfter]` を書かない | `ref`=書く、`in`=読むから Roslyn が並べる。曖昧なときだけ診断 |
| 7 | `Start` メソッド | `ReturnToGround.Start`。初回フラグ不要 | Entity が条件に初めて合った時を ECS が検出 |
| 8 | ユーザーコードに `TryToSingle` を書かない | `Locomotion.Speed=…Length` | 境界の変換をエンジンが担う |
| 9 | シーンJSONの簡素化 | `className`/`namespace` はファイル位置から。Component は短い型名のキー。`Instance` で別シーンを配置し、上書きもできる | Roslyn が型名を解決し、曖昧なら診断 |
| 10 | 実行時スポーンの型付き参照 | `Spawner.Spawn<CursorMarker>(Hit.Point)`(提案は `Spawner.Spawn(Scenes.CursorMarker,Hit.Point)` だったが、Scene のクラスの名前空間が `Scenes` なので値の形は書けない) | シーンJSONごとに型を生成 |
| 11 | エンジン標準の Component | `Lifetime`(自然に消える)、`CameraRig`、`ScreenText`、`AudioSource.Sound` | 繰り返し書くコードをデータに寄せる |

### シーンJSONの書式(提案)

```json
{
  "$schema": "../.cge/scene.schema.json",
  "Startup": true,
  "GameWindow": { "Title": "Third Person", "Size": [1280, 720] },
  "Children": {
    "Stage": { "Instance": "Lvl_ThirdPerson" },
    "Player": { "Instance": "Character", "Transform": { "Position": [0, 3.02, 0], "Rotation": [0, 0.25, 0] } }
  }
}
```

- 1ノード = オブジェクト。キーは Component の短い型名(`Transform` / `Mesh` / `Camera` …)と `Children`。
- ベクトルは `[R, U, F]`。回転は `[R, U, F]` 軸まわりの turns(ADR-0074)。
- 資産は拡張子なしの相対パス(`Stage/SM_Cube`)。生成された `Assets.*` と同じ名前で、存在をビルドで検査する。
- 既定値と同じ項目は書かない(`CharacterMotion` は変えたい項目だけ)。

## UE/Unity 由来の名前の清算

| 現在 | 理想 | 理由 |
|---|---|---|
| `CharacterMovement` | `CharacterMotion` | UE の CMC の項目名(`MaxWalkSpeed`、`JumpZVelocity`、`WalkableFloorAngle` …)をやめ、`WalkSpeed` / `JumpSpeed` / `MaxSlope` へ |
| `CharacterMovementState` | `CharacterMotionState`(`OnGround` / `Falling`) | |
| `CharacterInput` | `MoveIntent`(`Direction` / `Jump()` / `MoveTo(Point)`) | 入力ではなく「やりたいこと」 |
| `PlayerController` + `PlayerControl` | `ViewAngles`(turns、`Turn()` / `Planar()`) | Controller という UE の概念をやめる |
| `SpringArm` | `OrbitCamera` | |
| `LocomotionAnimator` | `Locomotion` | |
| `AppHost` + `Level` + GameMode シーン | `GameWindow`(`Game.json`) | |
| `IA_*` / `IMC_*` | `PlayerActions` のメンバー | |
| `BP_ThirdPersonCharacter` | `Character.json` | |
| `CgeThirdPerson` | `ThirdPerson` | |

実装状況(M0101 段階 2): `CharacterMotion`、`CharacterMotionState`、`MoveIntent`(`Direction` / `Jump()`)、`ViewAngles`、`OrbitCamera`、`Locomotion` とそれぞれのフィールドの改名は実装済み(現行の `CSharpGameEngine.Templates.ThirdPerson` はこの名前で動く)。未実装: `GameWindow` への統合、`ViewAngles` の turns 化と `Turn()` / `Planar()`、`IA_*` の `PlayerActions` 化、`CharacterMotion.Capsule` の入れ子。

### 実装状況(M0101、2026-10-22 時点)

現行の `CSharpGameEngine.Templates.ThirdPerson`(ビルドできる出荷物)は、次の仕組みで動いている。このフォルダの `ThirdPerson` などの理想コードとの差は、シーン JSON の書式(`Game.json`、短い型名)と、型付きの資産の指し方だけ。

| # | 仕組み | 状態 |
|---|---|---|
| 1 | 型付きアセット `Assets.*` | 実装済み(`Assets.Sounds.Jump`。シーン JSON の資産は文字列のまま、存在だけを SCENE208 で検査) |
| 2 | 入力を構造体で宣言 `[InputActions]` | 実装済み(`Stick`/`Look`/`Button`/`Axis`/`Pointer`。ゲームパッドの列挙は `Pad`(トリガー・肩ボタンを含む)、スティックの片方の軸は `StickAxis`、マウスは `MouseAxis`(`DeltaX`/`DeltaY` は片方だけ)/`MouseButton`。`Button.HeldSeconds` が押していた時間、`[Gamepad]` の `DeadZone`/`Scale`/`Invert`、`[NegativeKeys]`。M0108)。`Touch` は未実装 |
| 3 | 使うものだけを引数に取る | 実装済み(`ref State` 省略、`Audio` などのサービスは EngineHost のプロパティの型から) |
| 4 | `in Parent<T>` | 実装済み(値のコピーと親 Entity) |
| 5 | イベント | 実装済み(`in EventWriter<T>` で送り、`in T` で 1 件ずつ受ける。`Jumped`/`Landed`/`Footstep`) |
| 6 | フェーズと順序の推論 | 一部(入力を読む System は FrameInput で `Window` の後、イベントの受け手は送り手の後)。データの流れ全般の推論はしない |
| 7 | `Start` メソッド | 見送り(ADR-0077) |
| 8 | `TryToSingle` を書かない | 一部(`Turn(Look)`/`Planar(Stick)`。`Locomotion` の入力は float のまま) |
| 9 | シーン JSON の簡素化 | 未実装(読み手が 3 つあり、Designer の書き戻しもあるため) |
| 10 | 実行時スポーンの型付き参照 | 実装済み(M0105。`Spawner.Spawn<TScene>(位置)`。根に `Transform` がある Scene だけが渡せる。Scene は呼んだフレームの終わりに現れる) |
| 11 | エンジン標準の Component | 一部(`AudioListener`、`ScreenText`・`ScreenImage`(M0104)、`Lifetime`・`ParticleBurst`(M0105)はある。`CameraRig` は未実装) |
| 名前の清算 | `CharacterMotion` など | 実装済み(`GameWindow` への統合、`ViewAngles` の turns 化、`Capsule` の入れ子は未実装) |
| 音 | `Audio.Play`(XAudio2 のワンショット、`.wav` を直接読む) | 実装済み。素材は UE5 の Epic 教材のコンテンツから取り込んだ(`tools/UnrealExport/import_template_sounds.ps1`。テンプレート自身には音声が無い)。Vehicle は無音 |

移植済みのテンプレート(ビルドできる出荷物。WARP の `verify-thirdperson-character --template <名前>` で検証済み):

| テンプレート | プロジェクト | 理想コードとの差 |
|---|---|---|
| ThirdPerson | `Templates/CSharpGameEngine.Templates.ThirdPerson` | シーン JSON の書式、資産の指し方。M0107 で、歩きと走りの 8 方向(`Locomotion` の 16 本のクリップ)、足の IK(`FootIk`)、`KillZ` を足した |
| FirstPerson | `Templates/CSharpGameEngine.Templates.FirstPerson` | 同上。ステージは ThirdPerson のコピー(UE の配置が同じで、FirstPerson から書き出すと緑が白くなったため)。`Gameplay/` は ThirdPerson と同じ形(カメラは `OrbitCamera` の `Distance` 0)。M0107 で、体(`CharacterMesh0`。カメラに見えず影だけを落とす)と腕(`FirstPersonMesh`。一人称の FOV 70・縮尺 0.6 で描き、影は落とさない)の 2 つの Manny、`FirstPersonAnimation`(`Locomotion` を体へ渡す)、`PlayerControls` の体の Yaw(`bUseControllerRotationYaw`)、視点の上下の制限 −70°〜80°(`BP_FirstPersonCameraManager`)、目の位置(`Eyes` の `Pivot`)を足した |
| TopDown | `Templates/CSharpGameEngine.Templates.TopDown` | `Touch` は未実装。クリックした地面に目印(`CursorMarker`。`ParticleBurst` と `Lifetime` で 0.7 秒)を `Spawner.Spawn<CursorMarker>(Cached.Point)` で出す(M0108 から、UE のとおり短押しの離した瞬間。押していた時間 0.5 秒未満が短押しで、その離した瞬間に `MoveTo` と目印。長押しは押している間カーソルの下の地面へ直接歩き、離すと止まり、目印は出ない。`ClickToMove` と、UE の `CachedDestination` に当たる `ClickDestination` Component)。`Cursor.Ray` は `Picking.TryPick(Cursor)` に置き換え。経路探索は Navigation プラグイン(`plugins/CSharpGameEngine.Navigation`。csproj の `<CgePlugins>Navigation</CgePlugins>`、M0106)。`Scenes/TopDownNavigation.json` の `NavMeshBounds`(UE の NavMeshBoundsVolume)の領域で、ビルドがレベルの静的な衝突形状から `Assets/Navigation/Lvl_TopDown.cgenav` を焼き、キャラクターの `NavAgent` が `MoveIntent.MoveTo` の目的地への経路(台のスロープを下り、壁の端を回る)に沿って歩く。`Lvl_TopDown.json` は UE から書き出した生成物なので、領域は別のシーンに置いた。M0107 で、カメラの遅れ追従(`OrbitCamera.Lag` 3)、キャラクターのアニメーション(`TopDownAnimation`)、平面への拘束(UE と同じ値で、法線 0 のため拘束にならない)、`KillZ` と `RespawnPlayer`(壊れたら無作為の PlayerStart に `TopDownCharacter` を出す)を足した |
| Vehicle | `Templates/CSharpGameEngine.Templates.Vehicle` | UE の Chaos Vehicle 相当の車(M0109)。車体は動的な剛体で、車輪ごとの光線のサスペンション、トルク曲線と 5 速の自動変速、アッカーマン操舵、タイヤの摩擦(路面の摩擦を読む)、空気抵抗、転倒の判定とリセットを、エンジンのプラグイン Vehicles(`plugins/CSharpGameEngine.Vehicles`。csproj の `<CgePlugins>Vehicles</CgePlugins>`、ADR-0079)が UE の式で解く。値はデータアセット `Assets/Vehicles/SportsCar/SportsCarSetup.cgeasset`(`[DataAsset] VehicleSetup`。ゲームのコード `UseSportsCarSetup` が車へ入れる)。操作は `VehicleControls`(アクセル・ブレーキ・ハンドルはアナログの `Axis`、ハンドブレーキ、リセット)、カメラは `CameraToggle`(Tab で前と後ろ)と `LookAround`(見回し。離すと戻る)、灯りは `BrakeLightControl`(テールランプの発光 `MeshEmissive`)と `HeadlightToggle`(L で左のヘッドライト。UE と同じく右は点かない)。速度とギアは画面の文字(M0104)。`KillZ`(−50 m)より下へ落ちた車は最初の PlayerStart に出し直す(M0107)。エンジン音は作らない(UE のテンプレートに音の資産が無い。M0112 の決定)。レベルの地面は M0112 から UE のとおり: Landscape は高さから作った静的な三角形メッシュ `Landscape_Terrain.glb`(126 m 四方、中央に高さ 2.8 m の丘)、Landscape Spline の `SM_Track_10M` 16 個は 1 つに曲げた路面 `Track_Road.glb`(衝突あり)と縁石 `Track_Curbs.glb`(描くだけ)で、`tools/UnrealExport/build_vehicle_ground.py` が UE の読み出しから作る |

4 つのレベルは M0110 から、UE と同じ雲(`VolumetricCloud`)とグローバル距離場(`GlobalDistanceField`。M0111 の GI と反射の土台で、画面は変えない)を持ち、ThirdPerson・FirstPerson・TopDown は空の球(`SkySphere`。UE の SM_SkySphere)も持つ。ビルドは glb ごとにメッシュの距離場(`Assets/…/X.glb.cgesdf`)を作る(`CgeGenerateMeshDistanceFields`、既定で有効)。材質テクスチャは全ミップと異方性フィルターで描く。M0111 から、実機の GPU では Lumen 相当の GI と反射(High 段)で描く。WARP は既定で DDGI のプローブボリューム(Medium 段。M0110 と同じ画面)で、`CGE_SCALABILITY_GI=high` を付けると Lumen で描く(`CGE_LUMEN_VISUALIZE=diffuse|reflections|surfacecache` で中身を見る)。

4 つとも VS の新規プロジェクトに出る(M0101 段階 9)。このフォルダの `Vehicle` は理想コードのままで、ビルドできる出荷物は上の表のプロジェクト。

### プラグインを作る(ADR-0079)
- 機能(Component、System、サービス、データの型)を足すときはプラグインにする。値だけを足すときはアセット。作り方の全体は `Docs/Specifications/Plugins-And-Assets.md`。
- いちばん簡単な形は、ゲームの `Plugins/<名前>/` に置くプロジェクトのプラグイン。`samples/Plugins/Weather` をコピーして、名前を変えれば始められる。
- 置いたフォルダは次のとおり。
  - `<名前>.cgeplugin`: マニフェスト。
  - `<名前>.plugin.props`: 有効かを決める。名前を 2 か所変える。
  - `<名前>.plugin.items`: 取り込む項目。
  - `Runtime/`: ソース。
  - `Assets/`: アセット。コードからは `Assets.Plugins.<名前>.*` で指す。
  - `Scenes/`: シーン。
- 配るときは NuGet パッケージにする(`samples/Plugins/CSharpGameEngine.Samples.Lightning`)。ECS の型を使わない処理は、`lib/` の DLL に置いてよい。
- 動作の確認: `CSharpGameEngine.FeatureRunner verify-plugin-package`。SDK パッケージから作ったゲームに、2 つの見本を入れてビルドし、動かす。

参照プロジェクト: UE5.8 のインストールには Templates が無いため、UE5.7 の Blueprint 版テンプレートから `G:\UE\5.8\FirstPerson`・`TopDown`・`Vehicle` を作った(`tools/UnrealExport/create_template_project.ps1`)。3 つとも UE5.8 のエディターで開けて、起動マップが読み込めることを確認した(`run_probe.cmd`。観測は `tools/UnrealExport/README.md`)。理想コードの名前と構成は、これらのプロジェクトのアセット(InputAction、Blueprint、レベル)を参照して決める。FirstPerson と TopDown は、ステージ・キャラクターの値・マネキンのメッシュを取り込んで移植した(`tools/UnrealExport/README.md`)。Vehicle もステージ(Landscape は同じ大きさの板で代用)と車の静的メッシュを取り込んだ。3 つとも音声アセットは無いので、効果音は UE のテンプレートからは取り込めない。


残すもの: ステージ名(`Lvl_ThirdPerson`)と UE の資産ファイル名(出典を追えるように)。照明の Component(`SkyAtmosphere` など物理量の名前)は今回は対象外。

## 実現上の論点(確認したい点)

実現性を実コード(生成器・ランタイム)に照らしてレビューした結果。**どれも実装は可能**。ただし全項目が System の引数の役割表(ADR-0033 §2)の作り直しを前提にする。

### 前提: 引数の役割表を作り直す
- 現状は `in` で受けた struct がすべて Context(引数で固定)に分類される。`[ReadOnlyComponent]` は本番で使用 0 件、エンジンの System 37 個はすべて `ref` で受けている。
- `Audio` / `Time` / 入力シングルトンを単純に Context として足すと、生成 Main(SYSGEN021)・Bake ホスト・エディタープレビューの3つが「Context は `EngineHost` 1個だけ」の前提で同時に壊れる。
- 案: 先頭 `Entity` → 末尾 `ref State`(任意) → `in IEvent`(イベント) → `in Parent<T>`(リレーション) → `in [InputActions]`(リソース) → `EngineHost`/`State` の公開プロパティの型(サービス) → `in` struct(読み取り) → `ref` struct(書き込み) → 参照型(従来の Context)。
- 罠: Context のつもりで書いた `in` struct が Archetype のフィルタになり、何にも一致せず**黙って走らない**。「一致する Archetype が 0 のメソッド」の警告診断を必須にする。
- ADR-0033 §2 の改訂(`in`=読み取り。`[ReadOnlyComponent]` は廃止)と、§3「Write→Read 推論は原理的に成立しない」の撤回が要る。

### 仕組みごとの判定

| 仕組み | 判定 | 主な論点 |
|---|---|---|
| `ref State` の任意化・`Time`・`Audio` | 容易 | SYSGEN013 を削除。型名 `Audio` は名前空間 `CSharpGameEngine.Audio` と衝突するので `CSharpGameEngine.Runtime` 以下に置く。固定フェーズでは 1 フレームに 0〜8 回鳴り、Bake 中も鳴るので「Audio は Presentation 専用」の診断を入れ、エディタープレビュー中はミュート |
| `[InputActions]` | 可能 | 別 Generator にはできない(Generator 同士は出力を見られない)。`GameSystemGenerator` 内で合成 System を作る(前例: `SceneStartupSystemEmitter`)。ID は `StableId.FromName` を実行時に評価(ハッシュの再実装を避ける)。L1 に `Key`/`Button`/`Stick` の enum を追加。入力を読む System は FrameInput に固定(固定フェーズだと tick 0 回のフレームで `Pressed` が失われ、複数 tick で `Look` が倍になる) |
| `in Parent<T>` | 可能 | 閉集合の Archetype が有利(親の Archetype を生成時に列挙)。`PlayerControllerQueryRegistration` / `HierarchyQueryRegistration` の Spawn ハックを削除できる。同じメソッドで `ref T` と `in Parent<T>` を併用すると結果が順序に依存するので診断で禁止。ADR-0040 を改訂 |
| イベント | 可能(新規 ADR が必要) | 生産側は `EventWriter<T>` をシグネチャに出す(出ないと順序を推論できない)。寿命は固定フェーズの消費者なら tick 終了まで、Presentation の消費者ならフレーム内の全 tick 分をためて Presentation 後に破棄。Presentation が走らない経路(Bake・エディター)ではバッファが無限にたまるので tick 先頭で破棄。`Event.Position` は階層フレームでは意味を持たないので `Event.Source`(Entity)にして位置は Audio 側で解決。GLB のクリップにアニメ通知は無く、`Footstep` は歩幅(移動距離)から作る |
| フェーズ・順序の推論 | **縮小して可能** | 全面的なデータフロー推論は不可(エンジン System が全部 `ref`、`TryGetComponent` の読みはシグネチャに出ない、ReturnToGround と CharacterMotion は同一フェーズで循環)。縮小案: フェーズは規則表(入力を読む→FrameInput、`Audio` を使う→Presentation、他は固定フェーズで、読む T の生産より後に置く)。辺は「書き手→エンジンの `in` 読み手」とイベントの生産→消費だけ。曖昧なら警告して `[RunsAfter]` を提案。明示した属性が常に優先。前提作業としてエンジンの System 37 個の読みを `in` へ移す |
| `Start` | **v1 では削る** | サンプルでの唯一の用途(入力の据え付けフラグ)は `[InputActions]` が吸収する。実装するなら「到着バージョン」列・処理済みバージョンの World への保持(スナップショットに含める。`GeneratedSystemSet` だと復元時に二重発火)・`MoveArchetype` 後の再発火の定義が必要 |
| 改名 | 可能 | 影響は JSON 4・C# 6〜8・UE インポーター・アイコン SVG。ADR-0070 §7 は並行リーダーを禁止するので、別名の互換ではなく `SceneJson.Cli` の一括変換(schemaVersion 4)で移行。型名変更でスキーマ指紋が変わり、既存のスナップショットと物理キャッシュは読み込みを拒否される。`Level` は `GameWindow` へ統合。`Velocity.Planar.Length` には `LogicalVector3` に `Planar` / `Length` の追加が要る(現状は `LengthSquared` と `TrySquareRoot` だけ。演算はオーバーフローで例外) |
| 型付き `Assets` | 可能(後回し推奨) | `.glb` は静的メッシュ / 骨格メッシュ / クリップの3種があり得て拡張子で決まらない。Generator にバイナリを読ませず、MSBuild タスクで分類したマニフェストを AdditionalFile にする。ADR-0070 §2 の「struct は JSON オブジェクト」と衝突し、文字列参照を許すには JSON 解析・スキーマ・VSIX デザイナー・プレビュー適用の4つの読み手を直す。SCENE208(アセット不在)を新設、`[SceneAssets]` は廃止 |
| 音 | 可能(ADR-0056 に沿う) | ビルド時に CLI で WAV→48kHz/2ch の `.cgeaudio`(`PcmAudioStreamArtifact` は既にある)。`XAudio2Backend.SubmitBuffer` は呼ぶたびに `NativeMemory.Alloc` しハンドルを 1 つしか持たず、複数バッファのキューでリークか解放後使用になるので、`GetState` のバインディングとバッファのリングを追加。`AudioMixer.Mix` は Present の前にメインスレッドで回す(サイレントバックエンドでも回してヘッドレス検証を保つ)。`AudioSource` にクリップを持たせ、ボイスは EngineHost の Entity→Voice 表で管理 |

### 理想コードを調整する提案(レビューの指摘)
- `Look` の型: `Stick`(状態値)と `Delta`(今フレームの量)を分ける。マウスの相対軸はそのまま、スティックの絶対軸は ×dt。今の `Stick Look` に「レート入力は dt を掛ける」を暗黙に持たせるのは曖昧で、`Move` は dt を掛けてはいけない。
- `Landed` / `Jumped` / `Footstep` は `Event.Position` ではなく `Event.Source`(Entity)にする。
- `Start`(`Respawn.cs`)は v1 では外す案。
- `Assets.*` の型付けは後回しでも、サンプルは文字列パスで動く。
- `Footstep` を v1 から外す案もある。

### エンジンに存在しないもの
- (実行時に作った `SkeletalMesh` は M0107 から描かれる。メッシュの組を足すときはパレットのリングを作り直すので GPU の待ちが入る。消えた Entity の組はプールへ戻って使い回す。)StaticMesh と PrimitiveMesh は、M0105 から後で作っても描かれる。
- `Terrain` のコンポーネントは作らない(M0112 の決定)。Vehicle の Landscape は、UE から高さを読んで作った静的な三角形メッシュ(`StaticMesh` と `StaticMeshCollider`)で表す。Vehicle の Chaos 相当の車両物理は M0109 で実装済み(UE の制約のサスペンション(ハードストップ)は作らない。エンジン音も作らない: UE のテンプレートに音の資産が無い)。(TopDown の経路探索は M0106 で実装済み。ナビメッシュの無いゲームでは、`MoveTo` は従来どおり直線で歩く。)

### 共通コード
`PlayerControls` は ThirdPerson と FirstPerson でほぼ同一(FirstPerson だけ、体の向きを視点の Yaw に合わせる 1 行がある)。エンジンが標準の「歩くキャラクター操作」を提供するか、サンプルごとに明示するかは確認したい。

### 実装順の案(各段階でリポジトリがビルドできる状態を保つ)
1. 役割表の作り直し・`ref State` の任意化・サービスの解決
2. 改名と JSON の一括変換・`Level` の統合
3. `Parent<T>`・登録ハックの削除・`Locomotion` の LogicalScalar 化
4. `[InputActions]`
5. 音(artifact・ストリーミング再生・`Audio.Play`)
6. イベント(`Jumped` と `Landed`)
7. アセットのマニフェスト・SCENE208・型付きクリップ
8. エンジンの読みを `in` に移行してからフェーズ推論を入れ、テンプレートから属性を外す

サンプルは 7 まで属性付きで書き、8 で理想形にする。
