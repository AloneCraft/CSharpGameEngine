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
