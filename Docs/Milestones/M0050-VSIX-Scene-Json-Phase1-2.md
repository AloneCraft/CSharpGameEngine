# M0050: VSIX Scene DesignerのJSON対応(第1段: 検出・表示・編集・保存)

## 目的

M0049でSceneのRuntimeコンパイルパイプラインは`IScene`宣言的C# DSLから`*.scene.json`へ完全移行した。
VSIXのScene Designer(ギズモ・Hierarchy・Inspector)はC# `IScene`実装クラスをRoslyn構文解析で検出
する方式のままだったため、この時点でSceneを一切検出できず使用不能になっていた
([[M0049-Scene-Json-Persistence]]の既知の受け入れ事項)。

ユーザーから「ソリューションエクスプローラーで`.scene.json`を右クリックしたときに『デザイナーで
表示』『JSONで表示』の2項目を出してほしい」との要望があり、続けて「デザイナーで表示」の挙動を
確認したところ「Scene DesignerをJSON対応させるところまで含める」ことを選択した(プレースホルダー
ではなく実際に動く状態まで作る)。

## 調査で判明した直接原因

- `SceneWorkspaceService.AnalyzeAllAsync`が`Workspace.CurrentSolution.GetDocument(DocumentId)`
  (通常のC# `Document`専用API)のみを呼んでおり、`*.scene.json`はcsprojの`<AdditionalFiles>`登録
  によりRoslyn上`AdditionalDocument`扱いになるため`GetDocument`が`null`を返し、
  `InvalidOperationException`で即座に落ちていた。
- `GetProjectSceneCatalogAsync`は`Compilation`のシンボル列挙+`IsGeneratedSceneFile`
  (`.g.cs`ヘッダー判定)で生成コードを意図的に除外しており、M0049以降JSON Sceneの実体クラスは
  Source Generatorが生成した`.g.cs`そのものなので、このフィルタが構造的にJSON由来のSceneを
  締め出していた。
- `OnWorkspaceChanged`は`DocumentChanged/Added/Removed/Reloaded`のみ処理し、
  `AdditionalDocument`系のRoslynイベントを一切拾わなかった。

## 設計方針と朗報だった前提

実装前の調査で以下を確認し、当初想定より単純な設計に収まった:

- `src/CSharpGameEngine.SceneModel/`の`SceneModel`/`SceneComponent`/`SceneComponentMember`/
  `SceneValue`のシンボル系プロパティ(`ClassSymbol`/`TypeSymbol`/`MemberSymbol`/
  `ReferencedSymbol`/`Type`)は全て`?`付き(nullable)だった。C#解析でも「型未解決」を表現する
  ために元々null許容だったため、**型を1本化でき、JSON専用の並行モデルは不要**だった。
- M0047の編集モデル(`SceneEditDocument`/`SceneModelEditApplier`/`HierarchyToolWindowControl`/
  `EditTransactionController`)は`NodePath`/`EditorNodeId`ベースの純粋関数層で、Roslyn構文木への
  依存がほぼゼロ。**この層は無改造で動く**(実装中に確認、変更不要だった)。
- 保存経路の最終段`SceneTextBufferWriter.TryApply`/`SceneWorkspaceService.ApplyTextAsync`は
  `ITextBuffer`とSourceTextの差分適用だけを行う汎用層で、C#/JSONに依存しない。**無改造で流用**。
- リフレクションでのスパイク確認により、Roslyn `Workspace`クラスに`WorkspaceChangeKind.
  AdditionalDocumentAdded/Removed/Reloaded/Changed`と`OpenAdditionalDocument`/
  `CloseAdditionalDocument`が実在すること、`IsDocumentOpen`/`GetOpenDocumentIds`は
  Document/AdditionalDocumentを区別しない汎用APIであることを確認した。
- 過去に削除された`.scene`専用`SceneFileEditorFactory`の「複合拡張子`.scene.json`が
  `ProvideEditorExtension`でマッチしない」バグは、今回**新規EditorFactoryを作らない**設計
  (コンテキストメニューコマンドの文字列比較のみ)のため無関係だった。

## 実装内容

### 1. SceneModel構築のJSON対応

- `src/CSharpGameEngine.CodeAnalysis/SceneJsonModel.cs`(POCO: `SceneJsonDocument`/
  `SceneJsonNode`/`SceneJsonComponent`)を`src/CSharpGameEngine.SceneModel/`へ移設した
  (`git mv`+名前空間を`CSharpGameEngine.SceneAnalysis`へ変更)。この移設に伴い、単独ビルド検証用の
  `CSharpGameEngine.SceneModel.csproj`(バイナリ参照されない共有ソース束、`Directory.Build.props`の
  `netstandard2.0`+net472両対応要件)に`System.Text.Json`の`PackageReference`が新たに必要になり
  追加した(Version 9.0.0、消費側の`CSharpGameEngine.CodeAnalysis`/`CSharpGameEngine.VSExtension`
  とバージョンを揃える必要はない — このcsproj自体はバイナリ参照されないため)。
- `tools/CSharpGameEngine.VSExtension/SceneJsonModelBuilder.cs`(新規): Runtime側
  `SceneJsonAnalyzer.cs`と同じ値解釈ロジック(宣言型で判定、enumは文字列でメンバー名解決、
  `Compilation.GetTypeByMetadataName`での型解決、readonlyフィールド除外)を移植したが、出力先が
  `SceneEmitModel`(文字列専用)ではなく`SceneModel`/`SceneNode`/`SceneComponent`/
  `SceneComponentMember`/`SceneValue`である点が異なる。診断は`SceneDslDiagnostics`のSCENE2xxを
  Runtime側とそのまま共有する。
- `src/CSharpGameEngine.SceneModel/SceneSourceLocation.cs`に`FromJsonFile(string FilePath)`
  ファクトリを追加(ファイルパスだけを持つ軽量版。正確なテキスト位置追跡は次マイルストーンへ)。
- `SceneWorkspaceService.AnalyzeAllAsync`: `GetDocument`が`null`のとき`GetAdditionalDocument`を
  試し、`*.scene.json`なら新設の`AnalyzeJsonAsync`(1ファイル=1シーンとして単一の
  `SceneDocumentTypeRange`を返す)へルーティングする。
- `GetProjectSceneCatalogAsync`(Scene切り替えComboBox用)に`Project.AdditionalDocuments`から
  `*.scene.json`を列挙する経路を並走させた。
- `OnWorkspaceChanged`に`WorkspaceChangeKind.AdditionalDocumentChanged/Added/Removed/Reloaded`の
  4ケースを追加。
- `SceneDesignerContextMenuPolicy.Create`のガード条件を`.cs`または`.scene.json`の両対応に拡張
  (JSON1ファイル=1シーン前提なら既存のswitch文だけで自然に`Direct`モードへ収束する)。

### 2. 保存(JSON書き戻し)

- `SceneWorkspaceEditContext`を拡張: `Root`を`SyntaxNode?`に変更し`SceneSourceKind{CSharp,Json}`
  を追加(専用コンストラクタをJSON用に追加)。
- `ResolveSceneAsync`/`ResolveSceneEditContextAsync`: C#側で型が見つからなければ
  `Project.AdditionalDocuments`から`*.scene.json`を探す共通ヘルパー`FindJsonSceneAsync`
  (`(TextDocument,FilePath,SceneModel,SourceText)`のタプルを返す)へフォールバックする。
- `tools/CSharpGameEngine.VSExtension/SceneJsonSourceWriter.cs`(新規): `SceneModel`を
  `System.Text.Json.Nodes.JsonObject`ツリーへ変換し、丸ごとシリアライズし直す。C#の
  `SceneSourceEditor.ApplyForSave`(1024行、`SyntaxAnnotation`で対象追跡する部分差分書き換え)
  ほどの複雑さは不要と判断した — JSONファイルは数百行規模で全文再構築のコストは無視できる。
  `TypeFullyQualifiedName`の`global::`プレフィックスは除去して書き出す(既存サンプルの形式に合わせる)。
- `SceneEditorSessionService.SaveAsync`をJSON分岐: `Document.Model`(編集セッションの最終結果、
  既にpinから今までの全編集を適用済み)を直接`SceneJsonSourceWriter.Serialize`へ渡し、
  `SceneTextChangePlanner.Plan(SourceText,SourceText)`(既存の粗い全文diff経路)で最小差分化してから
  `ApplyTextAsync`で書き戻す。C#版と異なり「最新の実ファイルへHistoryを再生する」手順は不要と
  判断した(Document.Modelが既に最終状態そのものであるため)。
- `ApplyTextAsync`内の`Workspace.OpenDocument`を`Context.Kind`で`OpenDocument`/
  `OpenAdditionalDocument`に分岐(`IsDocumentOpen`/`TryGetLiveBuffer`は元々種別非依存なので無改造)。

### 3. コンテキストメニュー「JSON で表示」

- `tools/CSharpGameEngine.VSExtension/OpenSceneJsonCommand.cs`(新規): コマンドID`0x010B`
  (空きIDから採番)。`BeforeQueryStatus`は`FilePath.EndsWith(".scene.json")`の文字列比較のみ
  (Roslyn解析不要)。実行は`SceneSourceMarkerService.Navigate`と同じパターン
  (`Dte.ItemOperations.OpenFile(FilePath)`)で標準テキスト/JSONエディタへ委譲する。
- `OpenViewerCommandPackage.vsct`に`OpenSceneJsonCommandId`のButton/IDSymbolを追加
  (`SceneDesignerContextMenuGroup`配下、既存の「シーン デザイナーで開く」の直後)。
- `CSharpGameEngineViewerPackage.InitializeAsync`に`OpenSceneJsonCommand.InitializeAsync`の
  呼び出しを追加。

## 変更しなかったもの(計画通り)

- `SceneComponentCatalog.cs`(Inspectorのコンポーネント追加パレット、`Compilation`だけから型を
  列挙する設計でScene宣言の構文に依存しないため無改造)。
- `SceneModelEditApplier.cs`/`HierarchyToolWindowControl.xaml.cs`/`EditTransactionController.cs`
  (NodePath/EditorNodeIdベースの純粋層、Roslyn非依存のため無改造)。
- `SceneTextBufferWriter.cs`(ITextBuffer差分適用の汎用層、無改造で流用)。
- C#の`IScene`解析経路(`SceneDeclarationAnalyzer`/`SceneValueReader`/`SceneDslAnalyzer`、
  `SceneSourceEditor.cs`)自体は削除しない。移行期の混在プロジェクトや将来の後方互換のため残す。

## 段階分割(次マイルストーンへ回したもの)

- **Phase 3(次マイルストーン)**: `Utf8JsonReader`ベースの正確なJSON内テキスト位置追跡、
  `SceneSourceLocation.FromJsonSpan`実装。現状は`FromJsonFile`(ファイルパスのみ)止まりのため、
  Hierarchyのノードをダブルクリックしてもファイル先頭へジャンプするだけ(クラッシュはしない)。
- **Phase 4(次マイルストーン)**: SCENE200番台診断のVSIXエラーリスト連携、
  `FirstPersonStageScene.scene.json`(58ノード)規模でのパフォーマンス計測、移行期の
  旧C# `IScene`実装との共存確認。

## テスト

新規14件、いずれも既存踏襲パターン(純粋関数/独立コンパイル単位はユニットテスト、VS SDK依存部分
はCodeVerified止まり):

- `SceneDesignerContextMenuPolicyTests.cs`(+2件): `.scene.json`がDirectモードへ収束すること、
  無関係な`.json`が非表示のままであること。
- `SceneJsonModelBuilderTests.cs`(新規6件): リテラル値の宣言型別解決、enumメンバー名解決、
  複数子ノードのNodePath、不正なJSON(SCENE200)、未知の型(SCENE107)、未知のメンバー(SCENE201)。
- `SceneJsonSourceWriterTests.cs`(新規3件): 往復(読み込み→書き出し→再読み込みで意味的に一致)、
  enumがメンバー名で出ること、Component型が`global::`無しで出ること。

## 検証

- `dotnet build CSharpGameEngine.slnx`: 0エラー・0警告。
- `dotnet test CSharpGameEngine.slnx --no-build`: 全8プロジェクト成功、1117件中失敗0
  (VSExtension.Tests 612件、うち新規11件 — コンテキストメニュー2件は本ドキュメントの
  テスト節と合わせて実質14件のうち、独立ファイルの11件がこの数に対応)。
- **未実施(実機確認が必要、次のアクション)**: Experimental Instanceでの実際の動作確認
  — `.scene.json`右クリック→「デザイナーで表示」でHierarchy/ギズモ描画が出ること、
  「JSON で表示」で標準テキストエディタが開くこと、Hierarchy編集(リネーム・削除・D&D親変更・
  並べ替え)・ギズモ移動・Inspector数値編集がそれぞれCtrl+Sで実際に`.scene.json`へ反映され
  Ctrl+Zで戻せること、保存後にRuntimeビルド(`UserEcsProject`)を実行してScene Designerでの
  編集結果がゲーム実行に正しく反映されること。
- **未検証の技術的前提**: `FindDocument`(`GetDocumentIdsWithFilePath`)が実機のVisual Studio
  WorkspaceでAdditionalDocumentのIDを実際に返すか、`WorkspaceChangeKind.AdditionalDocument*`が
  実機で本当に発火するか — いずれもAPIの型シグネチャはリフレクションで確認済みだが、実際の
  VS ワークスペースでの挙動(特にAdditionalDocumentの変更検知の伝播経路)は未確認。もし実機で
  発火しないことが判明した場合は、`AssetMetaWatcher.cs`/`FabDownloadWatcher.cs`と同じ
  `FileSystemWatcher`ベースのフォールバックへ切り替える設計変更が必要。

## 実機フィードバック1件目とその修正(コミット後)

ユーザーが`FirstPersonStageScene.scene.json`を右クリックしたところ、「シーン デザイナーで開く」
「JSON で表示」のどちらも一切表示されないと報告(スクリーンショット添付)。

**根本原因**: `tools/CSharpGameEngine.VSExtension/ActiveDocumentTracker.cs`に、`SceneWorkspaceService`
のRoslyn解析よりさらに手前の入り口で`.cs`拡張子だけをハードコードで許可しているフィルタが
2箇所(`OnSelectionChanged`のソリューションエクスプローラー選択判定、`SetDocumentPath`の
アクティブなコードエディタ文書判定)あり、この見落としを本マイルストーンの実装時に発見できて
いなかった。`SceneDesignerContextMenuPolicy.Create`自体は`.scene.json`対応済みだったが、
そこへ渡る`FilePath`自体がこの手前の層で`null`にされていたため、Roslyn解析に一切依存しない
`OpenSceneJsonCommand`まで巻き込まれて表示されていなかった。

**修正**: 両箇所を共通の`internal static bool IsSceneCandidatePath(string Path)`
(`.cs`または`.scene.json`)へ置き換えた。回帰テスト`ActiveDocumentTrackerTests.cs`(新規4件)を
追加。VSExtension.Tests 616/616(既存612+新規4、リグレッションなし)。

この修正により「JSON で表示」は確実に表示されるようになる見込みだが、「シーン デザイナーで
開く」はさらに先の`SceneWorkspaceService`のAdditionalDocument解決経路(`FindDocument`→
`AnalyzeAllAsync`)が実機で正しく機能するかにまだ懸かっており、これも実機確認が必要。

## 実機フィードバック2件目とその対応

「シーン デザイナーで開く」をクリックすると「シーンの解析またはデザイナー表示に失敗しました。
詳細は ActivityLog.xml を確認してください。」という汎用エラーが出た。実際にユーザーの
Visual Studioプロセスを調べたところ、テスト対象は`devenv.exe /RootSuffix Exp`(F5デバッグの
Experimental Instance)ではなく通常起動のメインhiveで、かつ**Visual Studioは`/Log`スイッチ
付き起動でない限りActivityLog.xmlへ実質書き込まない**ため、`ActivityLog.LogError`は呼ばれて
いたはずでも詳細を確認する手段が無かった。

ログが読めない状態での対応として、以下の防御的改善を行った(コミット`847ea3b`):

1. `SceneWorkspaceService.AnalyzeAllAsync`を`Solution.ContainsAdditionalDocument(DocumentId)`
   で種別を先に確定させてから分岐する順序に変更し、`Solution.GetDocument`がAdditionalDocument
   のIDに対して本当にnullを返す(例外を投げない)という未検証の前提への依存を除去した。
2. `AnalyzeJsonAsync`内の`SceneJsonModelBuilder.Build`呼び出しを`try/catch`で囲み、万一の
   未処理例外を`SceneModel.Failed`(診断に例外種別+メッセージを含む)へ変換するようにした。
3. `OpenSceneDesignerCommand.OpenScene`のステータスバーメッセージに`Ex.GetType().Name`と
   `Ex.Message`を直接含めるようにした。ActivityLogが機能しない環境でも次回は具体的な原因が
   その場で分かる。

根本原因を確証を持って特定できていないため、複数の防御的改善を積み重ねる形になった。

## 実機フィードバック3件目とその修正(真の根本原因)

上記の防御的改善により、ステータスバーへ具体的な例外情報が表示されるようになった結果、
実際のエラーが判明した:「InvalidOperationException: Scene Designer の対象にできるのは
ソリューション内の C# Document だけです。」

**根本原因**: `SceneEditorSessionService.Pin(SceneModel Model)`が呼ぶ
`SceneWorkspaceService.TryCreateSceneKey(SceneModel Model)`が、`AnalyzeAllAsync`とは**別の
独立したメソッド**として`Solution.GetDocument(Id)`(C#専用API)だけを使っており、
`AnalyzeAllAsync`のときに行った`ContainsAdditionalDocument`優先化の修正が及んでいなかった。
`.scene.json`に対しては常に`null`を返すため、`AnalyzeFileNowAsync`でSceneModelの取得自体は
成功していても、その後の`Pin`呼び出しで確実に例外になっていた。

**修正**: `TryCreateSceneKey`と、同じ脆弱性を持っていた`GetProjectSceneCatalogAsync`内の
`ActiveProjectId`解決を、共通ヘルパー`ResolveProjectIdString(DocumentId)`
(`ContainsAdditionalDocument`優先)へ統合した。`SceneWorkspaceService.cs`/
`SceneEditorSessionService.cs`全体を`grep`で再点検し、他に`.cs`専用のまま残っている
`Solution.GetDocument`呼び出しが無いことを確認した(C#分岐内で既にJSON分岐と別れている
箇所を除く)。

**教訓**: `.scene.json`対応は「検出経路」(`AnalyzeAllAsync`)を直せば済むわけではなく、
Pin・保存・ComboBox表示など、後続の各経路が個別に同種のC#専用ロジックを持っていた。
1箇所の修正では終わらず、`Solution.GetDocument`/`.EndsWith(".cs")`のような判定パターンを
リポジトリ全体で横断的に洗い出す必要があった。

## Phase 4の実施(診断のエラーリスト連携・パフォーマンス計測)

上記の調査を踏まえ、「ActivityLog.xmlに頼らず原因を確認できるようにする」ことの延長として、
当初Phase 4に位置づけていた項目を本マイルストーン内で前倒し実施した:

- `tools/CSharpGameEngine.VSExtension/SceneDiagnosticsErrorListSync.cs`(新規):
  `Microsoft.VisualStudio.Shell.ErrorListProvider`/`ErrorTask`を使い、Scene Designerが実際に
  Pinしている(開いている)Sceneの解析診断(SCENE1xx/2xx、Error severityのみ)をVisual Studioの
  エラー一覧ウィンドウへ反映する。`SceneEditorSessionService.PinnedSnapshotChanged`を購読し、
  Pinが変わるたびにタスクを再構築する。タスクのNavigateで`Dte.ItemOperations.OpenFile`により
  対象ファイルを開く。`CSharpGameEngineViewerPackage`のライフサイクルに組み込み、他の
  サービス(`SourceMarkerService`等)と同じ`Start`/`Dispose`パターンに従う。
- `SceneJsonModelBuilderPerformanceTests.cs`(新規): 実際に出荷されている最大規模のScene
  (`FirstPersonStageScene.scene.json`、58ノード)を実ファイルから読み込み、
  `SceneJsonModelBuilder.Build`が例外なく完走し1秒未満で終わることを固定した。テスト用
  Compilationには実際の`UserEcsProject`のComponent型が無いため型解決は全滅するが、これは
  本テストの対象外(処理が例外なく完走し、診断が実際に生成されることだけを検証する)。

Evidence: VSExtension.Tests 617/617(既存616+新規1、リグレッションなし)。
`SceneDiagnosticsErrorListSync`はVS SDK型(`ErrorListProvider`)に強く依存するためCodeVerified
止まりで、実機での表示・ダブルクリック挙動は未確認。

## Phase 3(次マイルストーンへ持ち越し、判断根拠を記録)

`Utf8JsonReader`ベースの正確なJSON内テキスト位置追跡は、`SceneJsonModelBuilder`の現在の
`JsonSerializer.Deserialize<SceneJsonDocument>`ベースの実装(位置情報を一切保持しないDOM
デシリアライズ)を、自前の`Utf8JsonReader`再帰下降パーサー(各トークンのUTF8バイトオフセットを
追跡し、UTF16文字インデックスへの変換ロジックも自前で書く必要がある)へ全面的に置き換える
大規模な書き換えを要する。M0050の基盤機能(Scene Designerでの検出・表示)自体がまだ実機で
確証を得られていない状態でこの規模のリスクを追加するのは優先順位として見合わないと判断し、
今回は着手を見送った。次マイルストーンで、M0050基盤の実機確認が取れた後に着手する。
