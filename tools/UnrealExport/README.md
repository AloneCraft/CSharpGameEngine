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

UE5.8の`Templates\TP_ThirdPersonBP`と共有Content Pack(LevelPrototyping/Characters/Input)から、`templates/CSharpGameEngine.Templates.ThirdPerson`のシーンJSON・C#と資産を生成する開発用ツールです(ADR-0067/0068/0069)。

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
- `Scenes/ThirdPersonCharacter.json`: BP_ThirdPersonCharacterの実値をmへ換算した移動設定、PlayerController、SpringArmカメラ。
- `Input/ThirdPersonInput.cs`: IMC_Default/IMC_MouseLookの入力マップ。
- `.artifacts/UnrealExport/Lvl_ThirdPerson/manifest.json`: 取り込み元の実値。Git／ランタイムの対象外です。

移植先の無いUEの機能が有効な値で来た場合、取り込みは黙って捨てずに失敗します。対象はカメララグ、SpringArmのオフセット、未対応の入力modifierなどです。

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
