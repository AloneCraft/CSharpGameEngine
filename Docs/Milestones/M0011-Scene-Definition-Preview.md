# M0011: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー

## 目的

Scene Authoring Phase A(M0010)で完成した`[SceneDefinition]`クラスを、コードエディタの右クリックメニューから「シーンプレビュー」で開き、実際に`CreateRuntime`を実行して得た本物のEntity階層・Component値を、既存の「シーン」3Dビューポートで(メッシュを持たないノードにはプレースホルダーを表示して)確認できるようにする。設計判断は[[ADR-0031-Scene-Definition-3D-Preview]]に記録した。

## 実装したこと

### 新規ブリッジCLI `tools/CSharpGameEngine.SceneAuthoring.Cli/`(net10.0)
- `SceneDefinitionSourceParser`: ソース.csの構文のみ解析(`CSharpSyntaxTree.ParseText`、Compilation不要)で`[SceneDefinition]`クラスの`Define`メソッドから宣言順(深さ優先)のノード名リストを復元。
- `SceneRuntimeInvoker`/`SceneAuthoringLoadContext`: Collectible `AssemblyLoadContext`+`AssemblyDependencyResolver`で対象アセンブリをロードし、`[SceneDefinition]`型の`CreateRuntime(World)`をReflectionで実際に実行。同名アセンブリ(`CSharpGameEngine.Entities`)は既定コンテキストの既ロード版を優先して型を統一する。
- `WorldInspector`: `CSharpGameEngine.Entities.csproj`への新規`InternalsVisibleTo`経由で`Archetype`/`ComponentTypeRegistry`等を歩き、Position/Rotation/Scaleを型の単純名一致で、`Parent`を型そのもので判定して読み取る。`ComponentColumn<T>.Items`(public `T[]`)を`FieldInfo`経由で読む(`GetSpan<T>`が返す`Span<T>`はReflection越しに返せないため)。
- `ScenePreviewBridgeWorker`/`Program.cs`: 既存3ブリッジ(Import.Gltf.Cli等)と同型の`--serve <pipeName>`/2引数one-shotの二重モード。共有ブリッジ基盤が「入力パス1つ+出力パス1つ」固定のため、`ScenePreviewRequest{ SourceFilePath, TargetAssemblyPath }`を一時JSONへ書いてそのパスを入力パスとして渡す間接化を行った。
- `tests/CSharpGameEngine.SceneAuthoring.Cli.Tests/`: 12件(`WorldInspector`のハンドビルドWorldに対する単体テスト、`SceneDefinitionSourceParser`のソーステキストスニペットテスト、`SceneRuntimeInvoker`+`WorldInspector`を`samples/SceneAuthoringDemo`の実ビルド出力に対して実行する統合テスト)。

### VSIX側新規ファイル `tools/CSharpGameEngine.VSExtension/`(net472)
- `PreviewSceneDefinitionCommand`: このリポジトリ初の、コードエディタコンテキストメニュー配置+`BeforeQueryStatus`によるVisible/Enabled動的切替コマンド。`EnvDTE.TextDocument`のライブバッファテキストを見て判定し(`IWpfTextView`/Roslyn Workspaceは使わない)、`Execute`でブリッジを呼び、結果を一時`.scene`へ変換して`dte.ItemOperations.OpenFile(...)`で開く。
- `SceneDefinitionTextDetector`: `[SceneDefinition]`存在の正規表現による軽量判定(Roslyn不使用)。
- `ProjectOutputAssemblyResolver`: `EnvDTE.Project`の実ビルド出力パスをTFM/RID非依存に解決(`OutputPath`/`OutputFileName`プロパティ読み取り、失敗時は`bin/`配下のnewest-by-write-timeグロブへフォールバック)。
- `ExternalScenePreviewLoader`: `ExternalBridgeProcess.RunAndReadJson`経由でブリッジを呼ぶ(`ExternalGltfLoader`と同型)。
- `ScenePreviewSceneFileAdapter`: ブリッジ応答(`EntityId`/`ParentEntityId`のフラット配列)を既存`SceneFile`(`Guid`/`ParentId`)へ変換。全ノード`MeshAssetPath = null`固定。
- `GizmoMeshFactory`: 固定サイズの立方体`SimpleMesh`をその場でメモリ生成(`ProceduralMeshFactory.CreateBox`と同じ頂点/法線構成だがRUF非依存、プレーンな`Vector3`で実装 — RUF抽象化はnet10.0専用でVSIXから参照できないため)。
- `SceneHwndHost.LoadIntoSurface`拡張: `MeshAssetPath is not null`フィルタを、メッシュ有り→`MeshLoader.Load`/メッシュ無し→`GizmoMeshFactory.Create()`の分岐へ変更し、両方とも描画対象に含める(既存`.scene`プレビューにも同じ恩恵、メッシュ0件でも即エラーにならなくなった)。
- `.vsct`: 新規Group(`IDM_VS_CTXT_CODEWIN`親)+Button(`DynamicVisibility`フラグ)。既存の「Toolsメニュー+ツールバー両方配置」規約とは意図的に外し、コンテキストメニューのみに配置。
- `tests/CSharpGameEngine.VSExtension.Tests/`: 新規17件(`GizmoMeshFactoryTests`6件、`ScenePreviewSceneFileAdapterTests`6件、`ProjectOutputAssemblyResolverTests`4件、`ExternalScenePreviewLoaderTests`1件 — 実ブリッジサブプロセスを`samples/SceneAuthoringDemo`向けに起動するVS非依存の統合テスト)。

### テストフィクスチャ `UserGameProject/Scene1.cs`(新規)
`[SceneDefinition]`、Root→MainCamera(Position/Rotation/Scale/CameraRig)/Player(Position/Rotation/Scale/PlayerTag/Health)/Lighting(Position、Sun/Ambientのグルーピングアンカー)、Lighting→Sun(Position/DirectionalLightData)/Ambient(Position/AmbientLightData)という3階層6ノード。`UserGameProject/Components.cs`の既存型のみ使用(新規Component型は追加していない)。`Program.cs`の実際のゲームループには組み込まない(`samples/SceneAuthoringDemo/MainScene.cs`と同じ、単独のプレビュー確認用フィクスチャという位置づけ)。

## 意図的に除外したこと

- VSIX側のRoslyn Workspaceによるライブ編集(Add/Delete/Rename/Reparent)。ADR-0030で既に「Phase B以降の別タスク」と整理済み。
- Collectible `AssemblyLoadContext.Unload()`の常駐ブリッジサーバー環境での繰り返し安定性の専用ストレステスト。
- `.glb`をブリッジ経由で都度生成してメッシュ参照させる方式(読み取り専用のプレビューコマンドの副作用でファイルを書き出したくないため不採用、代わりにその場限りの合成メッシュ`GizmoMeshFactory`を採用)。

## 変更したFeature ID

- `F-L7-014`(新規) — `CSharpGameEngine.SceneAuthoring.Cli`
- `F-L8-009`(新規) — Scene Definition 3Dプレビュー
- `F-L9-007`(新規) — `UserGameProject/Scene1.cs`

## 実行したコマンドと結果

```
dotnet test tests/CSharpGameEngine.Entities.Tests/...csproj -c Debug
  → 成功、61件(InternalsVisibleTo追加のみ、無回帰)

dotnet build tools/CSharpGameEngine.SceneAuthoring.Cli/...csproj -c Debug
  → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.SceneAuthoring.Cli.Tests/...csproj -c Debug
  → 成功、12件

dotnet run --project tools/CSharpGameEngine.SceneAuthoring.Cli -- <request.json> <response.json>
  → samples/SceneAuthoringDemo向け: 終了コード0
    応答JSON: Root(SceneNodeTagのみ)→MainCamera(Position[0,2,-10]、Camera)/Player(PlayerTag、Health)、
    MainScene.csの実値と完全一致
  → UserGameProject/Scene1.cs向け(net10.0-windows10.0.19041.0+win-x64という非標準TFM): 終了コード0
    応答JSON: Root→MainCamera/Player/Lighting、Lighting→Sun/Ambientの6ノード、期待どおりの階層・値

dotnet build tools/CSharpGameEngine.VSExtension/...csproj -c Debug
  → 成功、警告0/エラー0(.vsctコンパイル含む、IDM_VS_CTXT_CODEWIN・DynamicVisibilityとも初回で正常解決)
dotnet test tests/CSharpGameEngine.VSExtension.Tests/...csproj -c Debug
  → 成功、39件(既存22件+新規17件)

dotnet build UserGameProject/UserGameProject.csproj -c Debug
  → 成功、警告0/エラー0
  Generated/.../Scene1.SceneNodes.g.cs / Scene1.SceneRuntime.g.cs を目視確認、6ノード全て正確

dotnet build CSharpGameEngine.slnx -c Debug / -c Release
  → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx
  → 成功、14プロジェクト・409件全成功
```

## リバースエンジニアリングした挙動

- `WorldInspector`の設計当初、`Archetype.GetSpan<T>()`を使ってComponent値を読む案を検討したが、`Span<T>`はReflectionの`MethodInfo.Invoke`を跨いで返せない(ボックス化不可)ことに気付き、代わりに`ComponentColumn<T>`の`Items`フィールドが素の`public T[]`であることを確認した上で、`FieldInfo.GetValue`+`Array.GetValue(row)`による値取得へ切り替えた。これが無ければ、対象プロジェクトのComponent型をコンパイル時に一切知らない汎用的なリフレクション走査自体が成立しなかった。
- `SceneRuntimeEmitter`が深さ優先(Root→子)でノードごとに`CreateEntity`を呼んでから子へ再帰することを再確認した上で、`World.AllocateId`が`Destroy`が呼ばれない限り`nextId`を単調増加させるだけであることも確認し、「`Entity.Id`昇順ソート = コード上の宣言順」という前提が成立することを実際に`WorldInspector`のテスト(`Inspect_OrdersNodesByEntityIdAscending`)で検証した。これにより、Runtimeには残らないノード名を、ソース構文解析の結果と安全に位置対応付けできる。
- ブリッジの実行方式についてユーザーから直接確認を受けた: 「`Define`メソッドの中身をRoslynで解析しているのか」という問いに対し、名前復元のみが構文解析(実行しない)で、構造とComponentの値は実際に`CreateRuntime`を実行して得ていることを説明し、「実際に実行する(現行案)」で確定した。この設計は、`Set(...)`の引数がリテラルとは限らない(SCENE008診断は「new式であること」しか強制しない)という事実と直接結びついている — テキスト解析だけでは`X = SomeConstant`のような非リテラル値を正しく再現できない。
- `SceneHwndHost.LoadIntoSurface`のギズモフォールバック拡張を実装する過程で、既存の`.scene`(JSON)プレビューが「メッシュを持つノードが1つも無いと`SceneRenderer`のコンストラクタが`ArgumentException`を投げて即エラーになる」という既存の(ADR-0031の対象外の)制約を発見した。この拡張はこの制約も同時に解消する(退行ではなく改善)ため、既存`.scene`経路にも適用することにした — F-L8-003の実機確認済み挙動への変更である点は、ADR-0031で明示的に検討した上で採用している。
- `ProjectOutputAssemblyResolver`の設計時、`EnvDTE.Project.Properties.Item("FullPath")`がC#プロジェクトではプロジェクトの「フォルダ」を返す(ファイルパスではない)という既知のEnvDTEの癖を踏まえ、代わりに`Project.FullName`(常に.csprojファイルへのフルパスを返す、強く型付けされたプロパティ)を使う設計にした。
- `.vsct`のビルドがコンパイルエラー無く初回で成功したことで、`IDM_VS_CTXT_CODEWIN`(コードエディタのコンテキストメニュー識別子)と`DynamicVisibility`コマンドフラグの組み合わせが、この環境のVS SDKで問題なく解決することを確認した(ADR-0031の未解決リスクとして記録していた項目の1つが解消)。
- 実装中、`ExternalScenePreviewLoaderTests`の実ブリッジサブプロセス統合テストが一度異常に長時間(15分以上)応答しなかったが、複数の`dotnet build`/`dotnet run`を同時並行で実行していたことによるファイルロック競合が原因と判断した。実行中の他のdotnetプロセスが落ち着いた後に同じテストを単体で再実行したところ2秒で完走・成功したことから、この仮説を確認した。

## 意図と観測の一致

Conformance: Matches。[[ADR-0031-Scene-Definition-3D-Preview]]に記載した意図した設計と、実際のビルド・テスト・ブリッジ実行結果の間に既知の乖離は無い。実機VS Experimental Instanceでのコンテキストメニュー表示/非表示切替・3D描画の目視確認は、このリポジトリの既定の役割分担によりユーザー側の確認待ち。

## 残課題

- 実機VS Experimental Instanceでの確認(コンテキストメニューの表示/非表示切替、クリック後の3D描画、再クリックでの更新、既存`.scene`プレビューへの無影響)は未実施 — ユーザー側で実施予定。
- Collectible `AssemblyLoadContext.Unload()`が常駐ブリッジサーバー(15分アイドルタイムアウト)の中で繰り返しロード/アンロードしても安定するかは、専用の繰り返しテストで未検証。
- VSIX側のRoslyn Workspaceによるライブ編集(Add/Delete/Rename/Reparent)は引き続き未着手(ADR-0030から継続する既知の残課題)。
- Position/Rotation/Scaleという型の単純名一致による変換規則は、この規約を破るComponent構成(将来同名だが異なる形の型が現れた場合等)に対してはサイレントに変換対象外になる(エラーにはしない)。
