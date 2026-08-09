# M0013: 静的(閉集合)Archetype ECSへの全面移行

## 目的

[[ADR-0032-Static-Archetype-Closed-Set-Ecs]](M0012)で独立検証した閉集合Archetype方式を本番へ全面適用し、
動的Archetype方式([[ADR-0029-Dynamic-Archetype-Soa-Ecs]]/M0009)を置き換える。Source Generatorを
`GameSystemGenerator`1つへ統合し、Scene Authoring・VSIXプレビューブリッジも新方式へ作り直す。
設計判断は[[ADR-0033-Static-Archetype-Ecs-Migration]]に記録した。

## 実装したこと

### ランタイム `src/CSharpGameEngine.Entities/`
- 旧動的Archetype実装19ファイル(`Entity`/`World`/`Archetype`/`EntityArchetype`/`ComponentType`/
  `ComponentMask`/`ComponentTypeSet`/`IComponentColumn`/`ComponentColumn`/`EntityQuery`/`QueryMatches`/
  `EntityCommandBuffer`/`ComponentValueStore`と属性6ファイル)を削除。
- `Runtime.cs`(新規): `ISystem`/`IArchetype`(空マーカー)、`Entity`(`record struct`、Generation無し)、
  `World`/`State`(`partial struct`)、`ChunkList<T>`。名前空間は`CSharpGameEngine.Entities`のまま維持。
- `Parent.cs`/`SceneNodeTag.cs`/`SceneBuilder.cs`/`SceneNodeBuilder.cs`/`SceneNodeSymbol.cs`/
  `SceneNodeId.cs`/`SceneDefinitionAttribute.cs`は存続(`SceneNodeTag`のコメントのみ更新)。
- csprojを**ソース正本**化: `InternalsVisibleTo`2件を削除、Analyzer参照と
  `EmitCompilerGeneratedFiles`を追加。消費側は`<Compile Include Link>`で取り込む(ADR-0033決定事項1)。

### `GameSystemGenerator`(`src/CSharpGameEngine.CodeAnalysis/`)
- 旧属性ベースの`GameSystem{Generator,Analyzer,Model,Scheduler,ExecutorEmitter,LoopEmitter,Diagnostics}.cs`
  7ファイルと`SceneDefinitionGenerator.cs`を削除。
- M0012の`StaticArchetype*.cs`8ファイルを`GameSystem*.cs`へリネームし、次を追加実装:
  - `GameSystemAnalyzer.cs`: `Spawn`メソッド認識、Context引数の構造的分類(ref-kindのみで判別)。
  - `GameSystemModel.cs`: `Role`(Dispatch/SpawnOnly)、`ContextParameters`、`CallOrder`、宣言位置。
  - `GameSystemIndexAssigner.cs`: Update/Spawn/SceneDefinitionノードの3ソースから形状を受け取る。
    Archetype名を出現順の連番へ変更し、`ComponentIndices`を宣言順で保持する。
  - `GameSystemTypesEmitter.cs`: `Create`の戻り値を`Entity`へ変更。
  - `GameSystemWorldEmitter.cs`: `World.Create`オーバーロードを追加(Archetype名を隠蔽)。
  - `GameSystemLoopEmitter.cs`: 記述順ディスパッチ、Spawn除外、Contextのスレッディング。
  - `GameSystemSnapshotEmitter.cs`(新規): `WorldSnapshot.Capture`/`ArchetypeSnapshotEntry`。
  - `GameSystemGenerator.cs`: `ISystem`実装structと`[SceneDefinition]`クラスの2つのSyntaxProviderを持つ
    統合エントリポイント。
- `SceneRuntimeEmitter.cs`: ノードごとに`ArchetypeNN.Create(ref world, …)`を1回呼ぶ形へ書き換え、
  `CreateRuntime(ref World)`へシグネチャ変更。`SceneDefinitionAnalyzer`から8引数上限検証を削除。
- 診断を`SYSGEN001`〜`020`へ全面的に振り直し(`SYSGEN009`は到達不能のため欠番)。`SCENE009`を削除。

### `tools/CSharpGameEngine.SceneAuthoring.Cli/`
- `SceneRuntimeInvoker.cs`: `CSharpGameEngine.Entities`への参照を捨て、完全リフレクション化。
  `Activator.CreateInstance`+by-ref`Invoke`で`CreateRuntime(ref World)`を呼び、`WorldSnapshot.Capture`を
  リフレクションで取得する。ALCの「既定コンテキストのアセンブリを優先」する再解決も削除(共有バイナリが
  存在しなくなったため意味が無い)。
- `WorldInspector.cs`: スナップショット配列をリフレクションで読む形へ全面書き換え。
- csprojから`CSharpGameEngine.Entities`への`<ProjectReference>`を削除。

### `UserGameProject/`
- `Systems/{InputSystem,PlayerControlSystem,CameraSystem,MovingPropSystem,RenderSyncSystem}.cs`5ファイルを
  `Systems/Systems.cs`1ファイルへ集約(記述順=実行順、ADR-0033決定事項3)。全て
  `internal struct X : ISystem`+`public bool Update(..., ref State)`形式へ書き換え、タグを含む全Component
  引数を`ref`に、`[Context]`を`in`/値渡しに変更。
- `SceneSpawnSystem`(新規): 起動時のステージ・ライト・Player生成をECS内で行う。
- `PlayerSpawn`/`MovableStagePropSpawn`(新規): どの`Update`とも一致しないArchetype構成の宣言。
- `Components.cs`: `SceneSpawnRequest`を追加、`MovableTag`を`readonly struct`から`struct`へ
  (`ref`で受ける必要があるため)。
- `Program.cs`: エンティティ生成を`world.Create(new SceneSpawnRequest { Scene = scene })`の1行へ縮小。
  メインループは`GeneratedGameLoop.RunFrame(...)`を毎フレーム呼ぶ形。
- csproj: `<ProjectReference>`を`<Compile Include Link>`へ変更、`UserGameProject.Tests`への
  `InternalsVisibleTo`を追加。`Scene1.cs`は無変更。

### サンプルの削除
`samples/{SoaSystemsDemo,SceneAuthoringDemo,AosSoaGeneratedExpansion,StaticArchetypeSystemsDemo}/`と
`src/CSharpGameEngine.StaticArchetype/`をディレクトリごと削除し、`CSharpGameEngine.slnx`から除去した。
ECS関連サンプルは0になり、`UserGameProject`が唯一の実証場所になった。

### テスト
- `CSharpGameEngine.Entities.Tests`: 削除された型のSpec6ファイルを削除、`WorldSpec`を新API向けに全面
  書き換え、`ChunkListSpec`を新規追加、`SceneAuthoringSpec`のWorld依存部分を書き換え。csprojをソース
  リンク方式へ。**22件**。
- `CSharpGameEngine.CodeAnalysis.Tests`: 旧`GameSystemGenerator*Tests`3ファイルと`StaticArchetype*Tests`
  4ファイルを削除し、`GameSystemGenerator{TestHelper,DiagnosticsTests,EmissionTests,EndToEndTests}.cs`を
  新規作成(診断20件・出力形状・記述順・部分集合ディスパッチ・Context・Spawn除外・決定性・E2E実行)。
  `SceneDefinitionGeneratorTestHelper`を`GameSystemGenerator`駆動へ変更、E2Eを`WorldSnapshot`ベースへ
  書き換え、SCENE009テストを「9個でも通る」回帰テストへ差し替え。**56件**。
- `UserGameProject.Tests`: `PlayerControlSystemTests`をインスタンスstruct+`ref State`形式へ。**21件**。
- `CSharpGameEngine.SceneAuthoring.Cli.Tests`: `TestScene.cs`(新規、`[SceneDefinition]`フィクスチャ)を
  追加してテストアセンブリ自身を対象にする方式へ変更。`WorldInspectorTests`をスナップショット配列
  ベースへ書き換え、`RepoBuildOutputLocator`をTFM/RID非依存の再帰探索へ。**13件**。
- `CSharpGameEngine.VSExtension.Tests`: `ExternalScenePreviewLoaderTests`の対象を削除済みサンプルから
  上記フィクスチャアセンブリへ変更。**39件**。

### ドキュメント
`ADR-0033`(新規)、`M0013`(本書)、`ADR-0029`をSupersededへ更新、`ADR-0032`に本番統合済みである旨と
変更された決定事項を追記、`Docs/Specifications/Ecs-Soa-Systems.md`を新方式へ全面改訂、
`Docs/Implementation-Status.md`の該当項目を更新。

## 検証結果(実際に実行して確認)

```
dotnet build CSharpGameEngine.slnx -c Debug     → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release   → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx              → 全14プロジェクト成功
```

テスト内訳(合計345件、失敗0):
Spatial 68、Graphics.D3D12 60、CodeAnalysis 56、VSExtension 39、Geometry 30、Entities 22、
UserGameProject 21、Import.Gltf 20、Core 13、SceneAuthoring.Cli 13、Import.Fbx 10、
Bridge.Hosting 8、Scene 8、Compiler.Hlsl 7。

加えて、生成された`UserGameProject/Generated/.../GameSystem.Loop.g.cs`を直接読み、Playerの8Component
Archetypeの存在・ディスパッチ順(Input→PlayerControl→Camera→RenderSync)・`SyncProp`相当の
オーバーロードが静的/可動プロップ両方のループに現れることを目視確認した。

## 意図的に除外したこと

- `[CurrentEntity]`/`[Commands]`/`[Without]`相当の機能(ADR-0033決定事項11、いずれも本番使用ゼロ)。
- メインループ本体の生成(ADR-0033決定事項10)。
- 実行順序の自動決定の代替手段(`Order`定数等)。記述順という単一の規則に絞った。

## 残課題

- **実機描画の確認は限定的**。ユーザーが`dotnet run --project UserGameProject`で目視し、移行前と
  同じ見え方・同じ操作感であることを確認した(=変換行列が正しく描画へ届き、入力も効いている)。
  ただし**このステージは移行前からZファイティングで描画が乱れている**(本移行とは無関係の既存問題)ため、
  描画の厳密な正しさは確認できていない。ライティング/フォグの適用有無は未確認のまま。
  カメラの1フレーム遅れについては、生成された`GameSystem.Loop.g.cs`のディスパッチ順が
  `Input → PlayerControl → Camera → RenderSync`であることを構造的に確認済み(旧`GameSystemScheduler`の
  Write→Read解析が導出したはずの順序と一致)。詳細はADR-0033「実機確認」を参照。
- **Zファイティングの解消**(移行前からの既存問題、本パスのスコープ外)。深度バッファ精度・描画順・
  近接面の重なりのいずれかが原因と思われるが未調査。
- 実行順序が記述順依存のため、Systemを複数ファイルへ分割すると順序がファイルパス順に依存する
  (1ファイル集約を運用で担保しているだけで、Generatorは強制していない)。
- `Entities.Tests`が`ListArchetype0`のようにArchetype名を名指ししている箇所は、Systemの増減で番号が
  ずれると修正が必要になる。
