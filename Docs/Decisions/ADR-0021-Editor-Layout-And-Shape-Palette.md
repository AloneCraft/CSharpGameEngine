# ADR-0021: エディタ既定レイアウト(GameEngineレイアウト)とシェイプパレット、`.scene`拡張子修正

## ステータス
承認済み

## 背景

ADR-0020で実装した「シーンのダブルクリックプレビュー」を実機で試したところ、**ダブルクリックしても
何も開かなかった**。原因はADR-0020自身が「未解決事項」として記録していたリスクそのもの -
`.scene.json`はドットが2つの複合拡張子で、`ProvideEditorExtension`はこれを単一拡張子として扱えない。
つまり機能は「未実装」だったのではなく、登録した拡張子が一度もVSに一致していなかった。

これに加えて、ユーザーから次の3点の要望があった:
1. 「ゲーム」ではなく「シーン」というパネル名にする(Unityで言うScene view=編集する場所。Game view相当は
   本システムでは`OpenGameCommand`によるビルド実行であり、ツールウィンドウではない)。
2. シーンの描画先はダブルクリックで開く**ドキュメントタブ**のまま(ADR-0020の方式を維持)。旧
   「GameEngine Viewer」ツールウィンドウ(ハードコードした`Box.glb`表示専用)は役割が重複するため削除する。
3. スクリーンショットで示された「シェイプパレット」(基本プリミティブのボタン群)のうち、基本的な形状
   (ボックス・球形・シリンダー・カプセル・コーン・トーラス・矩形・ディスク)は実際に動作させ、クリックで
   シーンにノードを追加する。
4. VSを起動した時点でツールバー・ヒエラルキー・シェイプ・シーンが既定配置で表示されている
   「GameEngineレイアウト」を用意し、仕様書にも記載する。

## 決定事項

### 1. `.scene.json` → `.scene`(根本原因の修正)

`[ProvideEditorExtension(typeof(SceneFileEditorFactory), ".scene", 50)]`に変更した。
`UserGameProject/DefaultScene.scene.json`は`UserGameProject/DefaultScene.scene`にリネーム(中身の
JSONスキーマは無変更)。単一拡張子はVSに未登録なので他ハンドラと競合しない。Unityの`.unity`/UEの
`.umap`と同じ発想。ドキュメントタブの識別のため`SceneFileEditorFactory.CreateEditorInstance`の
`pbstrEditorCaption`に`"シーン"`を設定した(タブは`DefaultScene.scene [シーン]`と表示される)。

ADR-0020自体は歴史的決定ログなので書き換えていない - この訂正は本ADRに記録する。

### 2. 旧「GameEngine Viewer」ツールウィンドウの削除

`ViewerToolWindowPane`/`ViewerToolWindowControl`/`ViewerHwndHost`/`ShowViewerToolWindowCommand`を
削除した。ハードコードされた`Box.glb`を表示するだけの機能で、シーン描画が「シーン」ドキュメントタブ
(`SceneWindowPane`/`SceneHwndHost`/`SceneRenderer`)に一本化された今、役割が完全に重複するため。
`VorticeRenderer`(単一メッシュ用)は`AssetPreviewHwndHost`(ADR-0013の`.glb`/`.fbx`プレビュー)が
引き続き使うため無改修のまま残した。`OpenViewerCommand`(別プロセスで`CSharpGameEngine.Extension`を
起動、ADR-0006)もツールウィンドウとは別物なので削除対象外とした。

### 3. `src/CSharpGameEngine.Geometry`(新規プロジェクト)にプリミティブ生成ロジックを集約

ADR-0019で確立した「メッシュデータは必ずアウトオブプロセスのブリッジ経由でVSIXに入る」原則を維持し、
かつ[[feedback-consistent-implementation-strategy]]に従ってメッシュ生成の数学をnet472側と
net10.0側に二重実装しないため、既存`src/CSharpGameEngine.Graphics.D3D12/ProceduralMeshFactory.cs`
(`ProceduralMeshData`/`CreatePlane`/`CreateBox`、元々`System.Numerics`のみでD3D12/TerraFX型を
一切使っていなかった)を新規`src/CSharpGameEngine.Geometry`(net10.0、グラフィックス非依存)へ
移設し、`CreateSphere`/`CreateCylinder`/`CreateCapsule`/`CreateCone`/`CreateTorus`/`CreateDisk`を
追加した(「矩形」は既存`CreatePlane`を再利用)。

**net10.0の`Graphics.D3D12`本体からは実は未参照**(`StageRenderer`等どこからも呼ばれていなかった -
唯一の呼び出し元は`StageGpuIntegrationTests.cs`)だったため、`Graphics.D3D12`自体には
`ProjectReference`を追加せず、`tests/CSharpGameEngine.Graphics.D3D12.Tests.csproj`から直接参照する
「テスト専用エッジ」とした(同csprojが既に`Import.Gltf`に対して採用している前例と同じパターン)。

各プリミティブ生成は退化三角形を出さないよう設計した(球はポールを単一頂点にしてファン、円柱/円錐/
カプセルは半径>0を検証済みなので幾何学的に退化しない)。`tests/CSharpGameEngine.Geometry.Tests`で
全8種について頂点/インデックス範囲・法線の単位長・退化三角形の不在を検証(30件、初回実行で全て成功)。

### 4. `tools/CSharpGameEngine.Primitive.Cli`(新規ブリッジ)

`CSharpGameEngine.Import.Gltf.Cli`(ADR-0019)と同型のnet10.0コンソールアプリ。引数
`<PrimitiveName> <output.glb>`で、`CSharpGameEngine.Geometry`が生成した頂点データを
`SharpGLTF.Toolkit`(`MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>`+
`SceneBuilder.SaveGLB`、`tests/UserGameProject.Tests/SceneLoaderTests.cs`で実証済みのGLB書き出し
パターンを踏襲)で`.glb`として書き出す。SharpGLTF.Toolkitは完全マネージド(ネイティブDLLなし)なので
`RuntimeIdentifier`は不要。8種すべて実際に`dotnet run`で生成し、生成物を
`CSharpGameEngine.Import.Gltf.Cli`で読み直せることを確認した(ブリッジ間の往復動作証明)。

`ExternalBridgeProcess`(ADR-0019)に`RunProducingFile`メソッドを追加した - `RunAndReadJson`と
同じプロセス起動ロジックを共有しつつ、出力をJSONとして読み返さず存在確認のみ行う(バイナリの
`.glb`はこの後`MeshLoader`/`ExternalGltfLoader`経由で別途読み込まれるため)。

### 5. シェイプパレットからのノード追加フロー

`ShapePaletteToolWindowPane`(新規ツールウィンドウ、Caption「シェイプ」)+`ShapePaletteControl`
(WPF、2列グリッド)。有効化した8ボタン(ボックス・球形・シリンダー・カプセル・コーン・トーラス・
矩形・ディスク)は`PrimitiveSpawner.SpawnAsync(primitiveKind)`を呼ぶ。残り10ボタン(矢印・階段・
CubeGrid・ポリゴン/パスの押し出し・回転・スプライン系)は対話的ギズモ操作が前提でEditorSpec §6の
スコープ外に踏み込むため`IsEnabled="False"`のプレースホルダーとした。

`PrimitiveSpawner`の処理:
1. `UserGameProject/Assets/Primitives/<Name>.glb`が無ければ`ExternalBridgeProcess.RunProducingFile`
   でブリッジCLIを起動して生成(2回目以降はキャッシュされたファイルを再利用、生成中は
   `IVsStatusbar`にメッセージ表示)。
2. `SceneEditorHub.ActiveScene`(現在「シーン」タブで開いているシーン)が無ければ案内メッセージを
   出して終了。
3. 新規`SceneFileNode`(名前は`Sphere`/`Sphere_1`...と重複回避、親は`SceneFile.FindFirstRootNodeId()`、
   Transform単位行列、`MeshAssetPath`はシーンファイルのディレクトリからの相対パス)を
   `SceneFile.AddNode`(新規、`SceneFile`は不変オブジェクトなので新しいインスタンスを返す)で追加し、
   `SceneFile.Save`(新規、`CSharpGameEngine.Scene.SceneGraph.ToJson()`と同一スキーマを出力)で
   書き戻す。
4. `SceneEditorHub.RequestReload`(新規イベント)を発火。「シーン」タブ(`SceneHwndHost`)がこれを
   購読して`SetScenePath`で自分自身を再読み込みし、`SceneEditorHub.PublishSceneOpened`経由で
   ヒエラルキーも連動更新される。

シーンJSONスキーマ自体は無変更 - スポーンされたプリミティブは他の`meshAssetPath`付きノードと
区別のない、通常のシーンノードとして扱われる。

net472の相対パス計算に`Path.GetRelativePath`(.NET Core 2.0+/netstandard2.1専用、net472の
BCLには存在しない)は使えないため、`Uri.MakeRelativeUri`を使った旧来の.NET Framework向け
ワークアラウンドを実装した。

### 6. 既定ドッキング配置

`[ProvideToolWindow(typeof(HierarchyToolWindowPane), Style = VsDockStyle.Linked, Window =
ToolWindowGuids80.SolutionExplorer, Orientation = ToolWindowOrientation.Left, DockedWidth = 300)]`、
`[ProvideToolWindow(typeof(ShapePaletteToolWindowPane), Style = VsDockStyle.Tabbed, Window =
ToolWindowGuids80.SolutionExplorer)]`。「シーン」はドキュメントタブなので配置指定は不要
(ドキュメント領域に自動的に入る)。

### 7. 初回起動時のみレイアウトを適用し、以後はVS標準の復元に任せる

ユーザー要望は「前回開いていたウィンドウを復元」だったため、毎回強制的に開く実装は避けた。
`Transient = false`(既定)のツールウィンドウはIDE終了時に表示されていれば次回起動時に自動復元
されるというVSの標準動作をそのまま利用する。そのため:

- `CSharpGameEngineViewerPackage.TryAutoOpenUserGameProjectAsync`(ADR-0012の自動ソリューション
  オープン処理)の直後、`GameEngineLayout.WasInitialLayoutAppliedAsync`でユーザースコープの
  設定ストア(`SVsSettingsManager`、コレクション`CSharpGameEngine`、プロパティ
  `InitialLayoutApplied`)を確認し、**未適用の場合のみ**`GameEngineLayout.ApplyAsync`を実行して
  フラグを立てる。2回目以降の起動では何もしない。
- `GameEngineLayout.ApplyAsync`は、ツールバー表示(§8)・ヒエラルキー/シェイプの表示・
  `dte.ItemOperations.OpenFile`によるデフォルトシーンのオープン(ADR-0013で確認済みの
  「確実にエディタファクトリ経由で開く」手段)を行う。

### 8. ツールバーの既定表示

VSCT.xsdおよびMicrosoft自身が出荷している`.vsct`ファイル群を調査したが、新規ツールバーを
**既定で表示**させる宣言的なフラグは存在しない(`DefaultDocked`はドッキング/フローティングの
区別であって表示/非表示ではなく、ADR-0011の実地確認結果とも一致)。代わりに
`EnvDTE.DTE.CommandBars`(型は`System.Object`で返るため`Microsoft.VisualStudio.CommandBars.CommandBars`
への明示キャストが必要)から名前(`"C# Game Engine"`)でツールバーを取得し
`CommandBar.Visible = true`を呼ぶプログラム的な方法を採用した。`Microsoft.VisualStudio.CommandBars`
NuGetパッケージを`ExcludeAssets="runtime"`で追加(SDKメタパッケージと同じパターン - 実体のDLLは
`devenv.exe`自身が提供する)。

### 9. 「GameEngine レイアウトを適用」コマンド(独自コマンドで代替)

VS 2015+の「ウィンドウ > ウィンドウ レイアウトの適用」(名前付きレイアウト、Ctrl+Alt+1〜9)を
プログラムから登録する公開APIはVS SDKに存在しないことを実装前に調査で確認した(実際の実装
`ILayoutManagementServiceInternal`/`IWindowLayoutStore`は`Microsoft.VisualStudio.Platform.WindowManagement.dll`
という非公開の内部アセンブリにのみ存在し、SDK参照アセンブリには含まれない)。そのため独自の
Tools-menu/ツールバーコマンド`ApplyGameEngineLayoutCommand`(CommandId `0x0106`)を新設した。

配置の強制には`IVsWindowFrame.SetFramePos`(公開API、`VSSETFRAMEPOS.SFP_fDock | SFP_fDockLeft`
などのフラグ+`ToolWindowGuids80.SolutionExplorer`を基準ウィンドウとして指定)を使う。これは
`[ProvideToolWindow]`属性の既定配置が「そのハイブでツールウィンドウが初めて生成される時だけ」
効くこと(以後はVSが`.winprf`に保存した実レイアウトを優先する)への対処でもある - 実機の
Expハイブに既に両ツールウィンドウのエントリが書き込まれていることを`.winprf`の内容から確認済み。

`ShowShapePaletteToolWindowCommand`(CommandId `0x0107`)も新設(`ShowHierarchyToolWindowCommand`
と同型の手動表示コマンド)。`.vsct`から`ShowViewerToolWindowCommandId`(`0x0101`)を削除し、
`ApplyGameEngineLayoutCommandId`/`ShowShapePaletteToolWindowCommandId`を追加した。

### 10. `Docs/EditorSpec.md`の更新

[[project-editor-spec-roadmap]]の方針(競合ドキュメントを増やさず1本に集約)に従い、新規ADRを
書くだけでなく仕様書そのものも更新した: §2.1として本レイアウトの説明を新設、§3の拡張子表記を
`*.scene.json`から`*.scene`に修正(理由の注記付き)、§1/§2/Phase 2の記述をADR-0020/0021時点の
実装状態に合わせて更新(旧`ViewerToolWindowPane`への言及を削除、Hierarchy/シーン描画/
シェイプパレットが実装済みである旨を反映)。

## 検証結果(実際に実行して確認)

- `dotnet build tools/CSharpGameEngine.VSExtension/...csproj`: 警告・エラーなし(VSCT コンパイル
  含む、`TreatWarningsAsErrors=true`下、VS threading analyzerのVSTHRD010指摘は
  `ThreadHelper.ThrowIfNotOnUIThread()`追加で解消)。
- `dotnet build tools/CSharpGameEngine.Primitive.Cli/...csproj` および 8種全てのプリミティブを
  `dotnet run`で実際に生成、終了コード0を確認。生成した`.glb`を`CSharpGameEngine.Import.Gltf.Cli`
  で読み直せることも確認(ブリッジ間往復)。
- `dotnet test tests/CSharpGameEngine.Geometry.Tests/...csproj`: 30件全て成功(初回実行)。

## 未解決・保留事項

- VS実験用インスタンスでの実機確認(ダブルクリックでシーンタブが開く、ヒエラルキー/シェイプの
  既定配置、選択ハイライト、シェイプクリックでの実際の追加描画)は本ADR執筆時点でまだ実施できて
  いない。次回セッションで[[feedback-vsix-verification]]の手順により確認する。
- `dte.CommandBars`のキャスト(`Microsoft.VisualStudio.CommandBars.CommandBars`)は型としては
  ビルドを通ったが、実際にツールバーが表示されるかは実機未確認。
- ドラッグ&ドロップによる任意アセット配置、Inspectorパネル、Transform数値編集はEditorSpec Phase 2の
  残りとして引き続き未着手。

## 追記: 初回起動時のツールウィンドウ作成がデッドロックしていた(実機デバッグで発見・修正)

ユーザーが実際にVisual StudioでこのVSIXをF5デバッグしたところ、初回起動シーケンスで**ハング**した。
Parallel Stacksで確認したところ、`WindowManagerService.CreateContentPane`(`Package.CreateToolWindow`
の内部呼び出し)が`JoinableTaskFactory.WaitSynchronouslyCore`で同期的にブロックしており、その待機先
(`AsyncPackage.FindToolWindowAsync`)は`CSharpGameEngineViewerPackage.InitializeAsync`自身の
完了を待っていた - 典型的な自己参照デッドロックだった。

**原因**: `GameEngineLayout.ApplyAsync`(ヒエラルキー/シェイプツールウィンドウを`ShowToolWindowAsync`
で初めて生成する処理)を、`TryAutoOpenUserGameProjectAsync`から**インライン`await`で**呼んでいた。
`TryAutoOpenUserGameProjectAsync`自体は`InitializeAsync`から`await`されているため、ツールウィンドウ
生成処理全体が`InitializeAsync`の同一非同期呼び出しチェーンにネストされたまま実行されていた。
`ProvideAutoLoad`(「ソリューションなし」状態での自動ロード)というVSライフサイクルの非常に早い
段階では、既存の2秒ディレイ(ADR-0012、ソリューション/MainWindow状態向け)を経てもなお、
ウィンドウ管理系のシェルサービスがこの呼び出しパターンに対応できる状態になっていないと見られる。

**修正**: レイアウト適用処理(`TryApplyInitialLayoutAsync`、新規)を`TryAutoOpenUserGameProjectAsync`
から完全に分離し、`InitializeAsync`から`JoinableTaskFactory.RunAsync(...)`で**待機せずに**起動する
独立したトップレベルJoinableTaskにした。これにより`InitializeAsync`自身は(ソリューションを開く
処理だけを終えて)先に完了でき、レイアウト適用は別の実行コンテキストで走る。念のため既存の2秒より
長い3秒ディレイも追加したが、根本的な修正は呼び出しチェーンの分離である(ディレイ延長だけでは
チェーンがネストされたままなので直らない可能性が高いと判断)。

**検証状況**: ビルドは成功したが、この修正がデッドロックを実際に解消したかどうかはVS実機での
再デバッグで確認できていない(修正は静的な推論に基づく - JoinableTaskFactory内部の正確な挙動を
このセッションからは直接観測できないため)。次回のVS実機検証で最優先に確認する。
