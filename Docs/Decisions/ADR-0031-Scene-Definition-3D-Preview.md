# ADR-0031: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー

## ステータス
承認済み

## 背景

Scene Authoring Phase A(ADR-0030/M0010)で、`[SceneDefinition] static partial class`のC#コードから`SceneDefinitionGenerator`が`CreateRuntime(World)`を生成する仕組みが完成した。ユーザーは次のステップとして、`[SceneDefinition]`が付いたクラスをコードエディタで開いた状態で右クリックすると、コンテキストメニューに「シーンプレビュー」が現れ、クリックすると既存の「シーン」3Dビューポート(現在`.scene` JSONファイルの表示に使われているもの)と同等のプレビューが表示される機能を要望した。

3択(フル3Dプレビュー/ヒエラルキーツリー表示のみ/配線のみの最小デモ)を提示したところ、ユーザーは最も完成度の高い**フル3Dプレビュー**(実際に`CreateRuntime`を実行して得た本物の値を使い、メッシュを持たないノードにもプレースホルダーを描画する)を選んだ。続けて、ブリッジCLIの解析方式について「`Define`メソッドの中身をRoslynで解析しているのか、それとも実際に実行しているのか」という確認があり、以下を明示的に説明した上で「実際に実行する(現行案)」を確定した:

- ノード**名**の復元だけはRoslynの構文解析(実行はしない)で行う — `CreateRuntime`の実行結果である`Entity`/`World`にはノード名という情報がそもそも残らないため(`SceneNodeId`/`SceneNodeSymbol`はコンパイル時限りの概念、ADR-0030)。
- ノードの**構造**(親子関係)と**Componentの値**は、実際に生成済みの`CreateRuntime`をビルド済みアセンブリからReflection経由で実行して得る。`Set(...)`の引数がリテラルとは限らない(定数参照や計算式もSCENE008診断の下では許容される)ため、テキスト解析だけで値を正しく再現するのは非現実的で、実行して結果を読む方が常に正確。

3回の並列調査(VSIXコマンド基盤、Sceneプレビューのレンダリングパイプライン、Roslyn連携+`UserGameProject/Scene1.cs`の履歴)と1回のPlan Agentによる詳細設計を経て、以下の制約が判明した:

- 既存の「シーン」3Dビューポート(`SceneRenderer`)は`MeshAssetPath`を持たないノードを完全に無視し、メッシュを持つノードが1つも無いと`ArgumentException`で即エラーになる。C# Scene Authoringのノード(Position/Rotation/Scale等のECS Component)にはメッシュの概念が無いため、プレースホルダー(ギズモ)描画を新規実装しなければ何も見えない。
- コードエディタのコンテキストメニューコマンドはこのリポジトリに前例が無く、`BeforeQueryStatus`によるVisible/Enabled切り替えも前例が無い。
- VSIX(net472)から`CSharpGameEngine.Entities`(net10.0)は直接参照できないが、このリポジトリには全く同じ制約を解決した実証済みの仕組み(`ExternalBridgeProcess`/`ResidentBridgeClient`/`BridgeServerHost`、ADR-0010/0019/0021/0023)がある。

## 決定事項

### 1. 既存の「シーン」プレビュー基盤をそのまま流用する

新しい`WindowPane`/`IVsEditorFactory`は作らない。C#シーンを解析・実行して得たノード情報を、一時的な合成`.scene`ファイル(`ScenePreviewSceneFileAdapter`)へ変換して書き出し、`dte.ItemOperations.OpenFile(...)`(`GameEngineLayout.ApplyAsync`に実証済みの前例あり、ADR-0013)で開く。`.scene`プレビュー(F-L8-003)は実機確認済みで最もリスクが低く、新規のWindowPane/EditorFactory登録という最大のリスク要因を避けられ、ヒエラルキーツリー表示と選択ハイライトも無料で手に入る。

### 2. 新しいブリッジCLIプロジェクト`CSharpGameEngine.SceneAuthoring.Cli`(net10.0)を作る

`CSharpGameEngine.Entities`を直接参照できる(net10.0同士のため、TFMの壁が無い)。VSIXは`ExternalBridgeProcess.RunAndReadJson`経由でこれを呼ぶ(`ExternalGltfLoader`と全く同じパターン)。既存のブリッジ共有コード(`ExternalBridgeProcess`/`ResidentBridgeClient`/`BridgeServerHost`)は「入力パス1つ+出力パス1つ」の契約に固定されているため変更せず、VSIX側は入力2つ(ソース.csパス、対象ビルド済みアセンブリパス)を1つのリクエストJSON(`ScenePreviewRequest`)にまとめて一時ファイルへ書き、そのパスを「入力パス1つ」として渡す。

### 3. ブリッジCLIは対象アセンブリを`AssemblyLoadContext`でロードし、`CreateRuntime`を実際に実行する

`CSharpGameEngine.Entities.csproj`に`InternalsVisibleTo`を`CSharpGameEngine.SceneAuthoring.Cli`へ追加し、`Archetype`/`ComponentTypeRegistry`等の`internal`なArchetype走査APIへアクセスする(公開APIには「このArchetypeが持つComponent型を列挙する」手段が意図的に存在しない — 通常の`[GameSystem]`/`[Update]`コードは必要としないため)。`ComponentColumn<T>.Items`は`Span<T>`ではなく素の`public T[]`フィールドなので、`FieldInfo`経由で読める(`Span<T>`は`MethodInfo.Invoke`を跨いで返せないため、これが無ければ汎用的なリフレクション走査自体が成立しなかった)。

Position/Rotation/Scaleという型の単純名一致で変換規則を適用する(ADR-0030 決定事項3で確立済みの規約)。`Parent`は型そのもの(`clrType == typeof(Parent)`)で判定し、直接キャストして値を取り出す(このブリッジ自身が`CSharpGameEngine.Entities`を直接参照しているため、`Parent`型はブリッジと対象アセンブリの間でCLR型として統一される)。

ノード名はRuntimeには残らないため、対象ソース.csファイルの構文のみの解析(`CSharpSyntaxTree.ParseText`のみ、`Compilation`/`SemanticModel`不要)で宣言順のノード名リストを別途取得する。`SceneRuntimeEmitter`はRoot→子の深さ優先でノードごとに`CreateEntity`を呼んでから子へ再帰するため、`Entity.Id`は生成順に単調増加し(`Destroy`が呼ばれないため)、`Entity.Id`昇順ソート = コード上の宣言順と一致する。この2つのリストを要素数が一致する場合にのみ位置対応で結合する(不一致なら名前なしのまま返す — 誤った対応付けより安全)。

### 4. メッシュを持たないノードには`GizmoMeshFactory`でその場限りの立方体メッシュを生成する

`CSharpGameEngine.Geometry.ProceduralMeshFactory.CreateBox`は経由しない — そのメソッドはRUF空間抽象化(`CSharpGameEngine.Spatial.Abstractions`/`.Unity`、いずれもnet10.0)を通るため、net472のVSIXから直接参照できない(`CSharpGameEngine.Entities`と全く同じTFMの壁)。固定サイズの軸並行立方体は複雑な生成アルゴリズムではなく定数幾何であり、ADR-0023が「ワイヤープロトコルのDTO形状は複製する、対象のアセンブリは複製しない」とした判断と同じ理由で、この1形状だけをVSIX側にも(RUFではなくプレーンな`System.Numerics.Vector3`として)複製することにした。ブリッジ経由で`.glb`を生成する案は、読み取り専用のプレビューコマンドの副作用としてファイルを書き出したくないという理由で不採用。

`SceneHwndHost.LoadIntoSurface`の`MeshAssetPath is not null`フィルタを、メッシュ有り→`MeshLoader.Load`/メッシュ無し→`GizmoMeshFactory.Create()`という分岐へ拡張し、**両方とも**`SceneRenderObject`として描画対象に含める。`SceneRenderer`自体は無改修(`SceneRenderObject{ Mesh, World, NodeId }`という既存の入力契約のまま)。これは既存の`.scene`(JSON)プレビューにも同じ恩恵が及ぶ副作用がある(メッシュ0件のシーンが、これまでの`ArgumentException`即エラーではなくギズモ表示になる)。メッシュ有りノードの挙動は完全に不変であり、実機確認済み(F-L8-003)の既存動作を壊すものではないため、これも一体で実施することにした。

### 5. コードエディタのコンテキストメニューに、このリポジトリ初のVisible/Enabled動的切替コマンドを追加する

`.vsct`に新規Group(`IDM_VS_CTXT_CODEWIN`親)+新規Button(`DynamicVisibility`フラグ付き)を追加し、既存の「Toolsメニュー+ツールバー両方に置く」規約とは意図的に外す(アクティブなファイルに紐づく操作のため)。`PreviewSceneDefinitionCommand`の`BeforeQueryStatus`は、`EnvDTE.TextDocument`経由でライブバッファのテキストを取得し(`IWpfTextView`/Roslyn Workspaceは使わない)、`SceneDefinitionTextDetector`(正規表現による軽量判定)で`[SceneDefinition]`らしさを判定する。`Execute`は`ProjectOutputAssemblyResolver`(`EnvDTE.Project`の実際のビルド出力を、TFM/RIDを問わず`OutputPath`/`OutputFileName`プロパティから解決— `UserGameProject`のような`net10.0-windows10.0.19041.0`+`win-x64`という非標準TFMでも、フォルダ名を決め打ちする方式では解決できないため)でビルド済みdllパスを求め、ブリッジを呼び、結果を`.scene`へ変換して開く。

## 実装

新規ブリッジCLI`tools/CSharpGameEngine.SceneAuthoring.Cli/`(`SceneDefinitionSourceParser`/`SceneRuntimeInvoker`/`WorldInspector`/`ScenePreviewBridgeWorker`)、VSIX側新規ファイル(`PreviewSceneDefinitionCommand`/`SceneDefinitionTextDetector`/`ProjectOutputAssemblyResolver`/`ExternalScenePreviewLoader`/`ScenePreviewSceneFileAdapter`/`GizmoMeshFactory`、および両側のDTOミラー`ScenePreviewRequest`/`ScenePreviewBridgeDocument`)、`SceneHwndHost.LoadIntoSurface`のギズモフォールバック拡張、`.vsct`/パッケージ登録、`CSharpGameEngine.Entities.csproj`への`InternalsVisibleTo`追加を実装した。テスト用フィクスチャとして`UserGameProject/Scene1.cs`(`[SceneDefinition]`、Root→MainCamera/Player/Lighting、Lighting→Sun/Ambientの3階層6ノード、既存`Components.cs`の型のみ使用、Program.csには組み込まない)を新規作成した。

## 帰結

### 得られたもの
- C#コードで定義したSceneを、実際に実行した結果に基づいて3Dプレビューできる最初の仕組みが完成した。
- 既存`.scene`(JSON)プレビューが、メッシュ0件でも即エラーにならずギズモ表示できるようになった(副次的な改善)。
- net472 VSIXから、実行時にnet10.0側の任意のプロジェクトの出力を「実際にビルド・実行して結果を読む」という、ADR-0010/0019とは異なる新しい種類のブリッジ利用パターン(対象アセンブリを動的にロードするCollectible `AssemblyLoadContext`)が確立した。

### 保留したもの
- VSIX側のRoslyn Workspaceによるライブ編集(Add/Delete/Rename/Reparent)。ADR-0030で既に「Phase B以降の別タスク」と整理済みで、本ADRのスコープでも変わらず対象外。
- Collectible `AssemblyLoadContext.Unload()`の常駐ブリッジサーバー環境での繰り返し安定性の専用ストレステスト(未実施、既知のリスクとして記録)。
- Position/Rotation/Scaleの型名一致規約を破るComponent構成での挙動保証(現状のADR-0030の規約に依存)。

## 検証結果(実際に実行して確認)

```
dotnet test tests/CSharpGameEngine.Entities.Tests/...           → 成功、61件(無変更、回帰確認のみ)
dotnet build tools/CSharpGameEngine.SceneAuthoring.Cli/...      → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.SceneAuthoring.Cli.Tests/... → 成功、12件
dotnet run --project tools/CSharpGameEngine.SceneAuthoring.Cli -- <request> <response>
  → samples/SceneAuthoringDemo向け: 終了コード0、JSON応答がMainScene.csの実値(Position [0,2,-10]等)と完全一致
  → UserGameProject/Scene1.cs向け: 終了コード0、JSON応答が6ノード全て(Root→MainCamera/Player/Lighting、
    Lighting→Sun/Ambient)期待どおりの階層・値で一致
dotnet build tools/CSharpGameEngine.VSExtension/...              → 成功、警告0/エラー0(.vsctコンパイル含む)
dotnet test tests/CSharpGameEngine.VSExtension.Tests/...         → 成功、39件(新規17件: GizmoMeshFactory6、
  ScenePreviewSceneFileAdapter6、ProjectOutputAssemblyResolver4、実ブリッジサブプロセスを起動する
  ExternalScenePreviewLoader統合テスト1)
dotnet build UserGameProject/...                                  → 成功、警告0/エラー0
  生成された Generated/.../Scene1.SceneNodes.g.cs / Scene1.SceneRuntime.g.cs を目視確認、6ノード全て正確
dotnet build CSharpGameEngine.slnx -c Debug / -c Release          → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx                                  → 成功、14プロジェクト・409件全成功
```

実機VS Experimental Instanceでのコンテキストメニュー表示/非表示切替・3D描画の目視確認・既存`.scene`プレビューへの無影響確認は、このリポジトリの既定の役割分担(F-L8-006〜008等と同じ、実機確認はユーザー側)によりユーザーに委ねる。

## 関連機能

- F-L7-014(新規) — `CSharpGameEngine.SceneAuthoring.Cli`(実行ベースのブリッジ: 汎用World走査、ソース構文のみのノード名復元)
- F-L8-009(新規) — Scene Definition 3Dプレビュー(コンテキストメニューコマンド、ギズモフォールバック拡張)
- F-L9-007(新規) — `UserGameProject/Scene1.cs`(プレビュー機能のテストフィクスチャ)
