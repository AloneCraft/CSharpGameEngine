# M0004: UE5.8 FirstPersonステージのECS移植

## 目的
`G:\UE\5.8\WFC\Content\FirstPerson\Lvl_FirstPerson.umap`(UE5.8 FirstPersonテンプレート系マップ)
を解析し、`UserGameProject`に同等のステージをECS駆動で再現する。静止/可動オブジェクトの区別・
ライト・カラーテクスチャ・ノーマルマップ・ラフネス・ECSとして実装した(プレイヤーに結びついた)
入力抽象化を要件とした(Docs/20260803-2.txt)。詳細な設計判断は
[[ADR-0017-UE-Level-Export-Pipeline]]・[[ADR-0018-Textured-Stage-And-Ecs-Systems]]に記録した。

## 実装したこと

### エクスポートパイプライン(UE側、C#ソリューション外)
- `tools/UnrealExport/export_lvl_firstperson.py`(新規): UEエディタをPython経由で操作し、
  レベル全体を1つのGLB(`UGLTFExporter.ExportToGLTF(World, ...)`)としてエクスポート。
  SkyLight/ExponentialHeightFog/PlayerStart/Mobilityタグは補助`scene_meta.json`に出力。
- `tools/UnrealExport/run_export.cmd`・`README.md`(新規)。
- `UserGameProject/Assets/Stage/Lvl_FirstPerson/{Lvl_FirstPerson.glb, scene_meta.json}`
  (新規、コミット対象のランタイムアセット)。
- `UserGameProject/Content/`(新規、UE Contentフォルダの生コピー・141MB・.gitignore対象、
  実行時には読まない参照用ミラー)。

### インポータ拡張
- `src/CSharpGameEngine.Import.Gltf/GltfImporter.cs`(変更、既存`Import()`は無改修):
  `ImportScene(string)`を追加。glTFノードグラフ全体を歩いて`ImportedScene`
  (Nodes/Meshes/Materials/Lights)を返す。TANGENT欠落時はLengyel法で生成。
- 新規: `ImportedScene.cs`・`ImportedSceneNode.cs`・`ImportedMeshData.cs`・
  `ImportedPbrMaterial.cs`・`ImportedLight.cs`・`GltfSceneImportResult.cs`。

### レンダラー拡張
- `src/CSharpGameEngine.Graphics.D3D12/StageRenderer.cs`(変更、in-place進化):
  メッシュ/マテリアル/オブジェクトを分離、Tangent+UV頂点属性、マテリアル毎3テクスチャの
  SRVディスクリプタテーブル、`SetDirectionalLight`/`SetAmbientLight`/`SetFog`を追加。
  旧`Create(IReadOnlyList<MeshCreateInfo>)`はアダプタとして残存。
- `src/CSharpGameEngine.Graphics.D3D12/StageRenderInputs.cs`(新規):
  `StageMeshCreateInfo`/`StageMaterialCreateInfo`/`StageObjectCreateInfo`/`FogParams`。
- `assets/Shaders/Stage.hlsl`(変更): データ駆動ディレクショナルライト+アンビエント、
  タンジェント空間ノーマルマッピング、GGX Cook-Torrance、簡易Exponential Height Fog。
- `src/CSharpGameEngine.Graphics.D3D12/Camera.cs`(変更): `CreateFirstPerson`ファクトリ、
  `Distance==0`時の`GetView()`退化を解消、一人称用ピッチクランプ範囲(±85°)。

### ECS拡張
- `src/CSharpGameEngine.Ecs/World.cs`(変更): `CollectViews<T>`(完全一致アーキタイプの
  エンティティ列挙、クエリ基盤ではない)。
- `src/CSharpGameEngine.Ecs/ArchetypeStorage.cs`(変更): `internal Entity GetEntity(int row)`。
- `UserGameProject/Components.cs`(変更): `Scale`・`RenderObjectRef`・`VerticalMotion`・
  `InputState`・`CameraRig`・`DirectionalLightData`・`AmbientLightData`・`FogData`を追加。
- `UserGameProject/Archetypes.cs`(変更): `StageProp`・`MovableStageProp`・`SunLight`・
  `AmbientLight`・`FogVolume`を追加、既存`Player`に`VerticalMotion`/`CameraRig`/`InputState`を追加。
- `UserGameProject/Systems/`(新規フォルダ): `InputSystem`・`PlayerControlSystem`・
  `CameraSystem`・`MovingPropSystem`・`RenderSyncSystem`。

### シーンロードとゲームループ
- `UserGameProject/SceneDocument.cs`(新規): `scene_meta.json`用DTO。
- `UserGameProject/SceneLoader.cs`(新規): `GltfImporter.ImportScene`の結果と
  `scene_meta.json`を合成し、`StageRenderer`入力+ECSスポーン情報を返す。
- `UserGameProject/Program.cs`(全面書き換え): `SceneLoader.Load`→`StageRenderer.Create`→
  mobility別スポーン→`InputSystem`/`PlayerControlSystem`/`CameraSystem`/`MovingPropSystem`/
  `RenderSyncSystem`を毎フレーム順次実行するループ。
- `UserGameProject/UserGameProject.csproj`(変更): `CSharpGameEngine.Import.Gltf`への
  `ProjectReference`追加、`Assets\Stage\**`を`CopyToOutputDirectory`対象に追加。

### テスト
- `tests/CSharpGameEngine.Import.Gltf.Tests/GltfImporterSceneTests.cs`(新規、5件)。
- `tests/CSharpGameEngine.Ecs.Tests/WorldSpec.cs`(変更、`CollectViews`用5件追加)。
- `tests/CSharpGameEngine.Graphics.D3D12.Tests/StageGpuIntegrationTests.cs`(変更、
  マルチマテリアル・ライティングAPIのWARP実描画テスト2件追加)。
- `tests/CSharpGameEngine.Graphics.D3D12.Tests/CameraTests.cs`(変更、一人称カメラ3件追加)。
- `tests/UserGameProject.Tests/SceneDocumentTests.cs`・`PlayerControlSystemTests.cs`・
  `SceneLoaderTests.cs`(新規、計10件)。

## 意図的に除外したこと
- `.uasset`の直接C#パース(CUE4Parse方式) — プロシージャルマテリアルのベイクにUE本体が必須
  (ADR-0017参照)。
- ガンマ補正/sRGBパイプライン — 既存の`ModelRenderer`/バックバッファが一貫して非対応のため、
  今回のテクスチャ追加でも導入しない(ADR-0018参照)。
- 本格的なQuery/System基盤・動的Add/Remove(構造変更) — `CollectViews`は完全一致アーキタイプの
  列挙のみで、複数アーキタイプにまたがるフィルタ・マッチングは行わない。
- 衝突判定・本格物理エンジン — `CharacterMotion`のY<=0クランプのみ(ADR-0016から変更なし)。
  プレイヤーは配置オブジェクトをすり抜け、ランプを登れない。
- UE本格Exponential Height Fog(第2フォグ層・inscatteringキューブマップ等) — 簡易な
  距離+高度減衰のみ実装。
- `WorldPartitionEditorSubsystem`によるセル強制ロード — 今回のレベル規模(55アクタ)では
  発生しなかったため未実装。
- `InputHub`シングルトンアーキタイプ — `InputState`はPlayerアーキタイプに直接持たせた
  (要件6「プレイヤーに結び付ける」に合わせた設計判断、ADR-0018参照)。

## 変更したFeature ID
- `F-L0-001` — `Camera`(更新、一人称モード追加、TestVerified)
- `F-L0-002` — `StageRenderer`(更新、テクスチャ・PBR・ライティング・フォグに拡張、TestVerified)
- `F-L0-005` — `GltfImporter.ImportScene`(新規、Implemented/TestVerified)
- `F-L3-004` — `World.CollectViews<T>`(新規、Implemented/TestVerified)
- `F-L7-005` — UEレベルエクスポートパイプライン(新規、Implemented/RuntimeVerified)
- `F-L9-003` — `SceneLoader`とステージスポーン(新規、Implemented/TestVerified+RuntimeVerified)
- `F-L9-004` — ECS入力抽象化とゲームプレイSystems群(新規、Implemented/TestVerified+RuntimeVerified)

## 実行したコマンドと結果
```
UnrealEditor-Cmd.exe ... -ExecutePythonScript=export_lvl_firstperson.py ...  → 成功(3回目、scene_meta.json/GLB生成確認)
dotnet build src/CSharpGameEngine.Import.Gltf/...csproj              → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.Import.Gltf.Tests/...csproj        → 18件成功(新規5件含む)
dotnet build src/CSharpGameEngine.Graphics.D3D12/...csproj            → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.Graphics.D3D12.Tests/...csproj     → 71件成功(新規5件含む)
dotnet build src/CSharpGameEngine.Ecs/...csproj                       → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.Ecs.Tests/...csproj                → 22件成功(新規5件含む)
dotnet build UserGameProject/UserGameProject.csproj                   → 成功、警告0/エラー0
dotnet test tests/UserGameProject.Tests/...csproj                     → 21件成功(新規10件含む)
dotnet build CSharpGameEngine.slnx                                    → 成功、警告1(既存のMSIL/AMD64不一致、無関係)/エラー0
dotnet test CSharpGameEngine.slnx                                     → 全184件成功、失敗0
dotnet run --project UserGameProject                                  → 起動しウィンドウ表示、
  10秒以上Responding=True・CPU時間蓄積(=毎フレーム動作)を確認後、検証のため終了
```

## リバースエンジニアリングした挙動
- `unreal.GLTFExporter.export_to_gltf`のPythonバインディングは、C++シグネチャの5引数
  (末尾が非const参照の`OutMessages`)に反して4引数までしか受け付けず、戻り値も本来の`bool`
  ではなく`OutMessages`自体になる。UE公式ドキュメントに明記が無く、実行時エラーメッセージ
  (`takes at most 4 arguments`→`cannot unpack non-iterable ... object`)から逆算して
  呼び出し形状を確定させた(ADR-0017参照)。
- `PunctualLight.LocalDirection`(SharpGLTF)は**インスタンスプロパティではなく静的定数**
  (`(0,0,-1)`固定、KHR_lights_punctualの規約通り)であるとリフレクションで確認し、
  ライトの実際のワールド方向はノードの`WorldMatrix`で変換して導出する設計にした。
- `Node.VisualChildren`はトップレベルの62件がScene直下、残り1件がネストした子ノードで
  あることを実際のエクスポート結果で確認し(理由は未特定)、`ImportScene`は
  「トップレベルのみ」ではなく全階層を再帰的に歩く実装にした。
- `Material.FindChannel("MetallicRoughness")?.GetFactor("MetallicFactor"/"RoughnessFactor")`
  という名前付きファクター取得APIの存在を、実際のパッケージDLLをリフレクションして確認した
  (`MaterialChannel.GetFactor(string key)`)。
- `MI_FirstPersonColorway`は実際には大半のインスタンスがフラットカラーへのベイク
  (テクスチャ無し・ファクターのみ)であり、当初想定していたグリッド柄のベイク崩れリスクは
  実質的に発生しなかった(ADR-0017参照)。

## 意図と観測の一致
Conformance: Matches。ADR-0017/ADR-0018に記載した意図した設計(UWorldレベル全体エクスポート
方式・StageRenderer in-place進化・GGXシェーディング・CollectViewsのスコープ限定・
InputStateをPlayerに一本化する判断)と、実際にビルド・テスト・`dotnet run`での実行確認との間に
既知の乖離は無い。一人称カメラでのマウス/キーボード操作感そのものは自動検証していない
(ADR-0016から引き継いだ制約)。

## 残課題
- 一人称カメラの前後左右の直感一致、実際のマウスルック操作感のユーザー自身による確認。
- 衝突判定・物理エンジン・カプセルメッシュ(ADR-0016から未解決のまま)。
- 本格的なQuery/System基盤(次にECSで多数アーキタイプを扱う要件が出た時点で再検討)。
- ガンマ補正/sRGBパイプライン。
- `WorldPartitionEditorSubsystem`によるセル強制ロード(より大規模なレベルを移植する際に
  必要になる可能性)。
- `ExponentialHeightFogComponent`の実際の色プロパティ名の特定(現状`scene_meta.json`の
  フォグ色は実測値ではなく固定のニュートラルグレー)。
