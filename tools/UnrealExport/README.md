# Unreal FirstPerson Scene Import

`G:\UE\5.8\WFC\Content\FirstPerson\Lvl_FirstPerson.umap`を、一体型ステージGLBではなく
C# Scene DSLのノード群として一度だけ取り込む開発用ツールです。UEの`.umap`／`.uasset`は保存しません。

## 実行

初回:

```cmd
tools\UnrealExport\run_export.cmd
```

生成済みC#を明示的に置き換える場合だけ:

```cmd
tools\UnrealExport\run_export.cmd --force
```

取り込み後は`UserEcsProject/Scenes/FirstPersonStageScene.json`が配置の正本です。UEとの同期は行いません。

## 出力

- `UserEcsProject/Assets/Stage/Lvl_FirstPerson/Meshes/*.glb`: ソースStaticMeshと解決済みマテリアル構成で共有した個別描画資産。
- `UserEcsProject/Scenes/FirstPersonStageAssets.cs`: `[SceneAssets]`資産カタログ。
- `UserEcsProject/Scenes/FirstPersonStageScene.json`: 55 StaticMeshActor、DirectionalLight、SkyLight、Fog、PlayerStartの宣言。インポーターが書いたC#のSceneクラスを、スクリプトの最後で`CSharpGameEngine.SceneJson.Cli convert`がシーンJSONへ変換します(ADR-0070)。
- `.artifacts/UnrealExport/Lvl_FirstPerson/`: 座標変換照合用の一時レベルGLBとmanifest。Git／ランタイム対象外。

現在の受け入れ基準はStaticMeshActor 55体、DirectionalLight 1体、SkyLight／Fog／PlayerStart各1体です。
SkyAtmosphere、VolumetricCloud、Brush、Blueprint、GameMode、アニメーションは対象外です。

# Unreal ThirdPerson Template Import

UE5.8の`Templates\TP_ThirdPersonBP`と共有Content Pack(LevelPrototyping/Characters/Input)から、`Templates/CSharpGameEngine.Templates.ThirdPerson`のシーンJSON・C#と資産を生成する開発用ツールです(ADR-0067/0068/0069)。

- `Program Files`配下のUEには書き込みません。
- `prepare_thirdperson_project.ps1`が`.artifacts/UnrealExport/TP_ThirdPerson/`へステージング用プロジェクトを組み立て、そこで書き出します。

## 実行

初回:

```cmd
tools\UnrealExport\run_export_thirdperson.cmd
```

生成済みの資産・シーン・C#を明示的に置き換える場合だけ:

```cmd
tools\UnrealExport\run_export_thirdperson.cmd --force
```

取り込み後はテンプレートのシーンJSONとC#が正本です。UEとの同期は行いません。インポーターが書いたC#のSceneクラスは、スクリプトの最後でシーンJSONへ変換されます(ADR-0070)。

## 出力

- `Assets/Stage/Lvl_ThirdPerson/Meshes/*.glb`: レベルの描画資産。`M_PrototypeGrid`系のマテリアルは焼き付けず、ワールド空間のグリッド(`material.extras.CGE_world_grid`)へ書き換える(ADR-0075、下の「プロトタイプグリッド」)。
- `Assets/Characters/Mannequins/`: SKM_Quinn_Simpleとアニメーション6本。アニメーションはジョイント階層とアニメーションだけへ縮約します。
- `Scenes/Lvl_ThirdPerson.json`: 次を宣言します。
  - 環境ライティング
  - 各メッシュの`StaticMesh`と`StaticMeshCollider`
  - 太陽
  - GIボリューム
  - PlayerStart
- `Scenes/ThirdPersonAssets.cs`: `[SceneAssets]`資産カタログ。
- `Scenes/ThirdPersonCharacter.json`: BP_ThirdPersonCharacterの実値をmへ換算した移動設定、ViewAngles、OrbitCameraカメラ。
- 入力は生成しない。`Gameplay/PlayerActions.cs`(手書き、`[InputActions]`)が、UEのIMC_Default/IMC_MouseLookに相当する(ADR-0077)。
- `.artifacts/UnrealExport/Lvl_ThirdPerson/manifest.json`: 取り込み元の実値。Git／ランタイムの対象外です。

移植先の無いUEの機能が有効な値で来た場合、取り込みは黙って捨てずに失敗します。対象はカメララグ、OrbitCameraのオフセット、未対応の入力modifierなどです。

## プロトタイプグリッド(ADR-0075)

UEの`M_PrototypeGrid`(`MI_PrototypeGrid_Gray`など)は、ワールド座標へ投影した線テクスチャで床と壁にグリッドを描く。glTF書き出しの焼き付けでは再現できないため、エンジンはワールド空間で描き直す。

- 書き出し(`export_lvl_*.py`)がマニフェストの`materialGrids`へ材質のパラメーターを記録する。対応するのはGrid=真・ObjectAligned=偽・TopSurfaceColor=偽の構成だけで、それ以外は書き出しが失敗する。
- 取り込み後に`CSharpGameEngine.Unreal.SceneImporter worldgrid <manifest.json> <project-directory>`が、対象のglbを書き換える(両方の`run_export*.cmd`が自動で実行する)。焼き付けテクスチャを外し、`baseColorFactor`をSurfaceColorへ戻し、グリッドの値を`material.extras.CGE_world_grid`へ置く。書き換え済みのglbに再実行しても結果は同じ。
- UEを再実行せずに既存のglbへ適用するには、マニフェストへ`materialGrids`を追記してから`worldgrid`を実行する。

```cmd
set CSGE_GRID_LEVEL=/Game/ThirdPerson/Lvl_ThirdPerson
set CSGE_GRID_MANIFEST=%CD%\.artifacts\UnrealExport\Lvl_ThirdPerson\manifest.json
"C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" <staging.uproject> -ExecutePythonScript="%CD%\tools\UnrealExport\add_material_grids_to_manifest.py" -EnablePlugins=PythonScriptPlugin -stdout -unattended -NoSplash -nosound
```

- `inspect_prototype_grid.py`は、UE側の材質(インスタンスのパラメーター、親材質と関数の式、`T_GridChecker_A`のPNG)を`.artifacts/UnrealExport/Lvl_ThirdPerson/prototype_grid.json`へ書き出す調査用スクリプト。

## UE5.8のゲーム系テンプレートの参照プロジェクト(`G:\UE\5.8\`)

FirstPerson / TopDown / Vehicle の理想コード(`Templates/`)と、将来の取り込みの参照元として、UEの新規プロジェクトと同じ構成のプロジェクトを `G:\UE\5.8\` に作る。UE5.8のインストールには `Templates\` が無い(確認: 2026-10-22)ため、元はUE5.7の Blueprint 版テンプレート(`TP_FirstPersonBP`・`TP_TopDownBP`・`TP_VehicleAdvBP`)と共有コンテンツ(`TemplateResources`)で、`EngineAssociation` を 5.8 にしてある。5.7のアセットは5.8のエディターが読み込み時に5.8へ上げる(保存しない限り元ファイルは変わらない)。

| 作るプロジェクト | 元のテンプレート | 展開した共有パック | 起動マップ |
|---|---|---|---|
| `G:\UE\5.8\FirstPerson` | `TP_FirstPersonBP` | Characters、LevelPrototyping、Input | `/Game/FirstPerson/Lvl_FirstPerson` |
| `G:\UE\5.8\TopDown` | `TP_TopDownBP` | LevelPrototyping、Characters、Cursor | `/Game/TopDown/Lvl_TopDown` |
| `G:\UE\5.8\Vehicle` | `TP_VehicleAdvBP` | Vehicles、Input | `/Game/VehicleTemplate/Maps/Lvl_VehicleBasic` |

- 作る: `powershell -File tools\UnrealExport\create_template_project.ps1 -Name FirstPerson`(`TopDown`、`Vehicle`。既にあるときは `-Force`)。ジャンル別のバリアント(Shooter、Horror、Strategy、TwinStick、Offroad、TimeTrial など)は含めない(UEの新規プロジェクトの「バリアント: なし」と同じ)。
- 開けるかを確かめる: `tools\UnrealExport\run_probe.cmd FirstPerson`(`CGE_PROBE_MAP` に起動マップを渡す)。UE5.8のエディター(`UnrealEditor-Cmd`、描画なし)で起動マップを読み込み、アクターの種類とアセットの種類を `.artifacts\UnrealExport\probe_<名前>.json` へ書く。何も保存しない。
- 2026-10-22 の観測(3つとも UE5.8 で開けて、レベルが読み込めた):
  - FirstPerson: レベルのアクター 63(StaticMeshActor 55、PlayerStart 1、環境 6)。InputAction 4、AnimSequence 97、SkeletalMesh 2、WidgetBlueprint 2、Niagara 1。
  - TopDown: レベルのアクター 65(StaticMeshActor 55、NavMeshBoundsVolume 1、RecastNavMesh 1、PlayerStart 1)。InputAction 2、Niagara 4(カーソルの演出)、StaticMesh 16。ナビメッシュが前提のテンプレート。
  - Vehicle: レベルのアクター 38(Landscape 1、StaticMeshActor 24、RuntimeVirtualTextureVolume 1、PostProcessVolume 1)。Blueprint 14、InputAction 12、InputMappingContext 5、CurveFloat 2(トルク曲線)、PhysicalMaterial 2、WidgetBlueprint 4(HUD)、SkeletalMesh 2(車)。
  - 取り込み(2026-10-22): `run_export_level.cmd <FirstPerson|TopDown|Vehicle> [--force]` がステージを `Templates\CSharpGameEngine.Templates.<名前>` へ取り込む(静的メッシュを glb、レベルをシーン JSON、Vehicle 以外はワールドグリッドも)。キャラクターの値は `run_dump_blueprint.cmd` の出力から手で書く。マネキンのメッシュは `run_export_skeletal_mesh.cmd`、Vehicle の車の静的メッシュ(`SM_SportsCar`・`_Glass`・`_Wheel`)は `CGE_SK_STATIC=1` を付けた同じスクリプトで書き出す(`SKM_SportsCar` は物理用の骨格だけで、見た目ではない)。Vehicle のレベルの Landscape は glTF に書き出せないので、`export_level.py` が同じ大きさの 1 m の板を作業中のエディターのワールドにだけ足して代える(保存しない)。`probe_vehicle_level.py`(`run_probe_vehicle_level.cmd`)は、レベルのクラスの一覧・Landscape の位置・メッシュの配置を UE のログ(`G:\UE\5.8\Vehicle\Saved\Logs\Vehicle.log` の `[UnrealExport]` 行)へ出す。
  - **3つとも、音声アセット(SoundWave、SoundCue)は1つも無い。** 効果音は UE のテンプレートからは取り込めない。

## 材質を残した書き出し(2026-10-22)

- 事実: UE の glTF エクスポーターは、エディターに描画デバイスが無いと(`-nullrhi`)、材質を読めず空の材質(`pbrMetallicRoughness:{}`)を書く。Vehicle と TopDown の glb は `-nullrhi` で書き出していたため、車・壁・床・Manny が白い既定の材質になり、灰色に見えた。描画デバイスありで書き出すと、車は画像 8 枚(基本色・金属粗さ・遮蔽・法線)、壁やコーンは `baseColorFactor`(M_TintCol)が出る。
- 直し: `run_export_level.cmd` と `run_export_skeletal_mesh.cmd` から `-nullrhi` を外した(材質を読まない解析のスクリプトは付けたままでよい)。車は `run_export_vehicle_car.cmd [出力先]`(`export_vehicle_car.py`)で、本体・ガラス・タイヤを材質つきで書く。既定の出力先は Vehicle テンプレートの `Assets/Vehicles/SportsCar`。
- ナビメッシュの調査(M0106): `run_probe_nav.cmd <プロジェクト名>`(`probe_nav.py`)は、レベルの NavMeshBoundsVolume の範囲と、RecastNavMesh のアクターの値を `.artifacts\UnrealExport\logic\<プロジェクト名>\nav.json` へ書く(保存しない)。CellSize・CellHeight・AgentMaxStepHeight は Python のプロパティとして読めない(`ERR` と出る)ので、`DefaultEngine.ini` の `[/Script/NavigationSystem.RecastNavMesh]` と UE の `RecastNavMesh.h`(`FNavMeshResolutionParam`)から取る。TopDown の値は `Docs/Milestones/M0106-Navmesh-Bake-And-Path-Following.md` に書いた。
- 材質の調査: `run_probe_vehicle_materials.cmd`(`probe_vehicle_materials.py`)は、Vehicle のメッシュごとの材質の連鎖・テクスチャ・パラメーターと、M_TintCol の入力ノードを `CGE_PROBE_OUT` のファイルへ書く。この環境の Python のログは標準出力に出ないので、結果はファイルへ書く。
- UE の参照画像: `run_capture_vehicle_reference.cmd`(`capture_vehicle_reference.py`)が、車を置いた Vehicle のレベルをエンジンのチェイスカメラと同じ姿勢で撮る(`.artifacts\UnrealExport\Lvl_VehicleBasic\ue_reference_*`。PNG だが拡張子は付かない)。GUI のエディターが開く(Lumen に描画済みのフレームが要る)。`-ExecutePythonScript` は一度スクリプトを実行すると 3 tick で終了するので、tick の呼び戻しを使うこの撮影は `-ExecCmds="py <スクリプト>"` で起動する。
- 回帰の防止: `TemplateExportMaterialTests`(`CSharpGameEngine.Import.Tests`)が、4 つのテンプレートの `Assets` の glb で、メッシュがあるのに材質がすべて空のものを失敗にする。

## Blueprint の処理の解析(ADR-0078、M0102)

テンプレートの挙動を、推測ではなくノードの接続から読むための書き出し。結果は `Docs/Specifications/UE-Template-Behavior.md` にまとめてある。

```cmd
tools\UnrealExport\run_dump_blueprint_logic.cmd G:\UE\5.8\Vehicle .artifacts\UnrealExport\logic\Vehicle /Game/VehicleTemplate/Blueprints/SportsCar/BP_VehicleAdvSportsCar /Game/VehicleTemplate/Blueprints/BP_VehicleAdvPlayerController
dotnet run --project tools\CSharpGameEngine.Unreal.SceneImporter -- logic .artifacts\UnrealExport\logic\Vehicle
```

- `run_dump_blueprint_logic.cmd <プロジェクト> <出力先> <資産のパス...>` は、描画なしのエディターで資産ごとに次を書く(何も保存しない)。
  - `<名前>.t3d`: 資産のテキスト書き出しの全文。これが正本。
  - `.class.t3d`、`.defaults.t3d`: 生成クラスと既定オブジェクト。T3D は既定値からの差分だけを書く。
  - `.components.txt`: 既定オブジェクトと各コンポーネントの全属性。編集専用の属性は、エンジンのヘッダーの `UPROPERTY` から名前を拾って読む。
  - `.graphs.json`: ノードとピンの一覧。UE のロケールの文字列が入るので参考扱い。
  - `.dsl.txt`: EditorToolset の逆コンパイラの出力。ヘッドレスでは空になるので使わない。
  - `_summary.json`: 書いたものとエラー。
- Python のログは `-stdout` に出ない。`<プロジェクト>\Saved\Logs\<プロジェクト>.log` の `[UnrealExport]` 行を見る。
- `logic <出力先>` は、各 `.t3d` から疑似コード `<名前>.logic.txt` を書く。
  - 入口(イベント・入力・関数)から実行の流れをたどり、純粋なノードを式に展開する。
  - 状態機械は、状態・遷移・条件式にする。アニメーションのポーズは木にする。
- 出力先の `.artifacts/UnrealExport/logic/` はリポジトリに入れない。UE の版が変わったら、流し直して差分を見る。

## アルファの切り抜きのメッシュ(ADR-0078、M0105)

エンジンの Stage シェーダーにはアルファテストが無い(描画契約 `stage-v11` を変えない)。UE の `M_Cursor`(TopDown のクリック地点の目印 `SM_CursorMesh`)のような BLEND_Masked の材質は、矢印をテクスチャのアルファで切り抜くので、そのまま不透明に描くと帯全体が黒く出る。

```cmd
set CGE_SK_STATIC=1
tools\UnrealExport\run_export_skeletal_mesh.cmd TopDown /Game/TopDown/Cursor/SM_CursorMesh .artifacts\UnrealExport\cursor\SM_CursorMesh.glb
python tools\UnrealExport\cut_masked_mesh.py .artifacts\UnrealExport\cursor\SM_CursorMesh.glb Templates\CSharpGameEngine.Templates.TopDown\Assets\Cursor\SM_CursorMesh.glb 24
```

- `cut_masked_mesh.py <入力.glb> <出力.glb> [四角形の一辺の分割数]`: 帯の四角形(2 枚の三角形が辺を共有する)を分割数 × 分割数に割り、ベースカラーのテクスチャのアルファが材質の `alphaCutoff` 以上のセルだけを残す。残したテクセルの平均色を `baseColorFactor` にして、テクスチャは持たせない。`doubleSided` なら、向きと法線を反転した複製を足す(レンダラーは裏面を捨て、両面のフラグを持たない)。
- 結果は `Assets/Cursor/SM_CursorMesh.glb`(6,312 三角形、色は線形 (0.0023, 0.1283, 0.5353))。UE の Niagara と材質から読んだ値は `Docs/Milestones/M0105-Runtime-Spawn-And-Effects.md`。
- 四角形の組でないメッシュ、UV が軸に揃っていない四角形は、そのことを言って止まる。