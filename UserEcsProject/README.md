# UserEcsProject

`Box.fbx` をレベルとして読み込み、ECSエンティティの `Transform` を `TransformSyncSystem` で
`StageRenderer` に同期する最小のFBX描画PoCです。

**Program.cs はありません。** `Main` はSGが生成します
(`Generated/.../GameSystem.EntryPoint.g.cs`)。生成された `Main` が作るのは `World`・`State`・
`Bootstrap` シードEntityの3つだけで、ウィンドウ生成・FBX読み込み・`StageRenderer` 生成・
OSイベント処理・`RenderFrame`・後始末まで、すべてSystemが行います。

Systemは移設先で2つのファイルに分けてあります:

| ファイル | 中身 | 行き先 |
|---|---|---|
| `Systems/GameSystems.cs` | `GameStartupSystem` — ウィンドウのタイトル/サイズ、レベルのFBXパス、シェーダーのパス、初期カメラの宣言 | このプロジェクトに残る |
| `GameplayComponents.cs` | `PlayerStart`などゲーム固有Component | UserEcsProjectに残す |
| `src/CSharpGameEngine.Runtime.Windows/RuntimeSystems.cs` | Windows共通System群 | shared projitemsからImport |

共有Windows Runtime側にゲーム固有のリテラルはありません。すべて `AppHost`/`Level` の
Component値として `GameStartupSystem` から渡ります。

これを可能にしているのは、`Win32Window`/`StageRenderer` をContext引数ではなく **Componentの
フィールド** として持たせたことです。Contextはループ開始前に構築されて `Run` の全期間で固定される
ため、生成されたエントリポイントには渡す値を作る手段がありませんでした。Componentに載せることで
Systemの中で遅延生成でき、Context引数がゼロになって `GeneratedGameLoop.Run(ref State)` という
固定シグネチャが確定します(ADR-0033決定事項10の更新)。

フレームの構成と実行順序の仕組みは `src/CSharpGameEngine.Runtime.Windows/RuntimeSystems.cs`を参照してください。

`dotnet run --project UserEcsProject/UserEcsProject.csproj` で起動します。

移設先は `CSharpGameEngine.Entities` ではあり得ません(net10.0・ProjectReferenceゼロのプラット
フォーム非依存ソースのため)。`CSharpGameEngine.Runtime.Windows` のような新しい共有ソース
プロジェクトが必要になります — 詳細は `src/CSharpGameEngine.Runtime.Windows/RuntimeSystems.cs`と ADR-0035 を参照。


## Shared Windows Runtime

The project now owns only scene DSL, gameplay components (`GameplayComponents.cs`), assets, and the selected Logical precision. AppHost/Level/lights/audio, fixed hierarchy resolution, stage roster construction, and the Windows system scheduler are imported from `src/CSharpGameEngine.Runtime.Windows/CSharpGameEngine.Runtime.Windows.projitems`.

`PlayerStart` is an empty gameplay marker. Camera pose always comes from the active Camera entity Transform. Exactly one active Camera is required; environment lights, fog, and audio remain optional.