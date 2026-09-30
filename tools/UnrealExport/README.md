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

取り込み後は`UserEcsProject/Scenes/FirstPersonStageScene.cs`が配置の正本です。UEとの同期は行いません。

## 出力

- `UserEcsProject/Assets/Stage/Lvl_FirstPerson/Meshes/*.glb`: ソースStaticMeshと解決済みマテリアル構成で共有した個別描画資産。
- `UserEcsProject/Scenes/FirstPersonStageAssets.cs`: `[SceneAssets]`資産カタログ。
- `UserEcsProject/Scenes/FirstPersonStageScene.cs`: 55 StaticMeshActor、DirectionalLight、SkyLight、Fog、PlayerStartの宣言。
- `.artifacts/UnrealExport/Lvl_FirstPerson/`: 座標変換照合用の一時レベルGLBとmanifest。Git／ランタイム対象外。

現在の受け入れ基準はStaticMeshActor 55体、DirectionalLight 1体、SkyLight／Fog／PlayerStart各1体です。
SkyAtmosphere、VolumetricCloud、Brush、Blueprint、GameMode、アニメーションは対象外です。

# Unreal ThirdPerson Template Import

UE5.8の`Templates\TP_ThirdPersonBP`と共有Content Pack(LevelPrototyping/Characters/Input)から、`templates/CSharpGameEngine.Templates.ThirdPerson`のC#と資産を生成する開発用ツールです(ADR-0067/0068/0069)。

- `Program Files`配下のUEには書き込みません。
- `prepare_thirdperson_project.ps1`が`.artifacts/UnrealExport/TP_ThirdPerson/`へステージング用プロジェクトを組み立て、そこで書き出します。

## 実行

初回:

```cmd
tools\UnrealExport\run_export_thirdperson.cmd
```

生成済みの資産とC#を明示的に置き換える場合だけ:

```cmd
tools\UnrealExport\run_export_thirdperson.cmd --force
```

取り込み後はテンプレートのC#が正本です。UEとの同期は行いません。

## 出力

- `Assets/Stage/Lvl_ThirdPerson/Meshes/*.glb`: レベルの描画資産。
- `Assets/Characters/Mannequins/`: SKM_Quinn_Simpleとアニメーション6本。アニメーションはジョイント階層とアニメーションだけへ縮約します。
- `Scenes/Lvl_ThirdPerson.cs`: 次を宣言します。
  - 環境ライティング
  - 各メッシュの`StaticMesh`と`StaticMeshCollider`
  - 太陽
  - GIボリューム
  - PlayerStart
- `Scenes/ThirdPersonAssets.cs`: `[SceneAssets]`資産カタログ。
- `Scenes/ThirdPersonCharacter.cs`: BP_ThirdPersonCharacterの実値をmへ換算した移動設定、PlayerController、SpringArmカメラ。
- `Input/ThirdPersonInput.cs`: IMC_Default/IMC_MouseLookの入力マップ。
- `.artifacts/UnrealExport/Lvl_ThirdPerson/manifest.json`: 取り込み元の実値。Git／ランタイムの対象外です。

移植先の無いUEの機能が有効な値で来た場合、取り込みは黙って捨てずに失敗します。対象はカメララグ、SpringArmのオフセット、未対応の入力modifierなどです。
