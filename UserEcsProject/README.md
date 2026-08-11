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
| `Systems/RuntimeSystem.cs` | `WindowSystem` / `LevelLoadSystem` / `TransformSyncSystem` / `PresentSystem` | いずれエンジンの共有ソースへ |

`RuntimeSystem.cs` 側にゲーム固有のリテラルは1つもありません。すべて `AppHost`/`Level` の
Component値として `GameStartupSystem` から渡ります。

これを可能にしているのは、`Win32Window`/`StageRenderer` をContext引数ではなく **Componentの
フィールド** として持たせたことです。Contextはループ開始前に構築されて `Run` の全期間で固定される
ため、生成されたエントリポイントには渡す値を作る手段がありませんでした。Componentに載せることで
Systemの中で遅延生成でき、Context引数がゼロになって `GeneratedGameLoop.Run(ref State)` という
固定シグネチャが確定します(ADR-0033決定事項10の更新)。

フレームの構成と実行順序の仕組みは `Systems/RuntimeSystem.cs` の冒頭コメントを参照してください。

`dotnet run --project UserEcsProject/UserEcsProject.csproj` で起動します。

移設先は `CSharpGameEngine.Entities` ではあり得ません(net10.0・ProjectReferenceゼロのプラット
フォーム非依存ソースのため)。`CSharpGameEngine.Runtime.Windows` のような新しい共有ソース
プロジェクトが必要になります — 詳細は `Systems/RuntimeSystem.cs` の冒頭コメントと ADR-0035 を参照。
