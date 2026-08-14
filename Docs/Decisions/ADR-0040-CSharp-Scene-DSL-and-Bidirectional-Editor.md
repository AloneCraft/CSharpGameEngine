# ADR-0040: C# Scene DSLとScene Editorの双方向同期基盤

## ステータス

承認済み。Phase 1〜9は実装済み(回顧録: [[M0019-CSharp-Scene-DSL]])。Editor→Code方向の配管
(決定事項5)は実装済みだが製品コードからの呼び出しは未配線であり、その配線と入力・ギズモを含む
インタラクション全体は[[ADR-0041-Scene-Editor-Interaction-Architecture]] / M0020で行う。

本文書の初版(41行・英語見出し)は、日本語部分が全て `?` に置換された文字化け状態でコミットされて
おり(コミット394ff04時点で既に破損、git履歴からの復元不能を確認)、当初「ADR-0040自体の文書化は
Phase 11で行う」と先送りしていた正式文書化を2026-08に前倒しして全面改稿した。決定内容そのものは
初版から変更していない。

## 背景

シーンの正本は歴史的に3段階で移り変わってきた。

1. `.scene` JSON時代([[ADR-0020-Scene-Preview-And-Hierarchy-Selection-Sync]]、
   [[ADR-0021-Editor-Layout-And-Shape-Palette]]、[[ADR-0022-Hierarchy-Drag-Drop-Asset-Placement]]):
   `*.scene`(JSON、Guid ID)をダブルクリックでドキュメントタブに3Dプレビューし、ヒエラルキーへの
   ドラッグ&ドロップで`.scene`へ書き込んでいた。
2. C#正本への転換([[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]):
   `[SceneDefinition] static partial class`をSource Generatorで処理し、C#宣言をシーンの正本とした。
   3Dプレビューは[[ADR-0031-Scene-Definition-3D-Preview]]で「生成された`CreateRuntime`を別プロセスで
   reflection実行してWorldSnapshotを読む」実行ベース方式だった。
3. `IScene`/`Node`宣言API([[ADR-0038-Startup-Scene-Declarative-Api]]):
   `internal partial class X : IScene { public Node Root => new(){...}; }`というobject-initializer
   グラフへ移行し、旧機構(`[SceneDefinition]`、`SceneDefinitionGenerator`)はM0019 Phase 5で削除した。

この時点で残っていた問題が本ADRの動機である。

- 実行ベースのプレビュー(ADR-0031)は、対象プロジェクトのビルドが通るまで何も表示できず、
  タイピング中のシーンが見えない。またSource Generatorと別系統の解析を二重管理していた。
- VSIX(net472)は`CSharpGameEngine.Entities`(net10.0)をバイナリ参照できない(ADR-0034の共有ソース
  設計)。そもそもScene DSLは実行されない前提であり、`Node`のコンストラクタは意図的に
  `NotSupportedException`を投げる(宣言専用のトリップワイヤ)。
- Scene Editor(ヒエラルキー・ビューポート・将来のインスペクタ)からの編集をどこへ書き戻すか、
  Undo/Redo・コメント保持・外部編集との整合をどう成立させるかが未決定だった。

## 決定事項

### 1. 正本はC#テキスト。編集は必ずテキスト編集として表現する一方向ループ

Visual SceneとCode Sceneを別々の真実として持たない。エディタのあらゆる編集は最終的にC#ソースへの
テキスト変更として表現し、表示は常にそのテキストの再解析から導出する。

```text
SceneEditOperation
  -> SceneSourceEditor(Roslyn syntax node編集)
  -> VS TextBuffer(1ユーザー操作 = 1 VS Undoトランザクション)
  -> VisualStudioWorkspace(WorkspaceChanged)
  -> SceneDeclarationAnalyzer(250msデバウンス、バックグラウンド)
  -> SceneModel -> SceneDocumentSnapshot / SceneProjection / SceneDiff
  -> Hierarchy / Inspector / Viewport
```

エディタがモデルを直接書き換えてからコードへ「エクスポート」する方式は採らない。書き込みと表示が
同じ解析パイプラインを通ることで、外部エディタやAIによる`.cs`変更も、エディタ自身の編集も、
まったく同じ経路で画面へ反映される。

### 2. `SceneModel`は共有ソース(バイナリ参照しない)

`src/CSharpGameEngine.SceneModel/`(namespace `CSharpGameEngine.SceneAnalysis`、全型`internal`)は
プロジェクト参照されず、次の2ホストへ`<Compile Include>`でソースごと取り込まれる。

| ホスト | TFM | 用途 |
|---|---|---|
| `src/CSharpGameEngine.CodeAnalysis`(Source Generator) | netstandard2.0 | `SceneModelBridge`経由で`SceneEmitModel`化しコード生成 |
| `tools/CSharpGameEngine.VSExtension`(VSIX) | net472 | ライブRoslynでの表示・編集 |

このため`SceneModel`プロジェクトではnetstandard2.0に`IsExternalInit`が無いことから`record`/`init`を
禁止し、全型を手書きの`sealed class`/`readonly struct`で書く(csprojコメントに明記)。

`SceneDeclarationAnalyzer`は純粋なsyntax+semantic解析であり、コードを実行もビルドもしない。
タイピング中の不完全な構文で毎キーストローク呼ばれるため**絶対に例外を投げない**契約とし、
`SceneValueKind`は9種の許可式形式だけを通すホワイトリスト(default-deny、対象外は`Unsupported`)
とする。SGとVSIXの解析同値性は`tests/CSharpGameEngine.VSExtension.Tests/SceneAnalysisParityTests.cs`
で固定する。

### 3. 識別モデルは3層(`NodePath` / `EditorNodeId` / `SceneKey`)

| 識別子 | 寿命 | 役割 |
|---|---|---|
| `NodePath`(共有ソース) | 解析1回分 | Rootからの子インデックス列。Scene DSLノードの唯一の解析上の同一性。ピッキング・描画オブジェクトのキー |
| `EditorNodeId`(VSIX) | Designerセッション | リネームやスパン移動を跨いで安定。`SceneProjectionReconciler`が割当 |
| `SceneKey`(VSIX) | セッション | `(Roslyn ProjectId, クラス完全修飾名)`。ファイルパスやクラス単純名は使わない(同名partial・複数プロジェクトで衝突するため) |

`SceneProjectionReconciler.Reconcile`は兄弟照合を意図的に2パスで行う: 第1パスは
`Name + Component型完全修飾名列`の完全一致、第2パス(リネーム検出)はComponent構成のみの一致で、
かつ候補が**ちょうど1件**残る場合だけIDを引き継ぐ。区別不能な同一兄弟のIDは決して推測しない
(`SceneEditorContractsTests.Reconcile_DoesNotGuessAnIdentityForIndistinguishableSiblings`)。
1パスで済ませると、後方にある完全一致の兄弟をリネーム候補として誤認するため2パスに分けている。

`ComponentId`は導入しない。解析器が「1ノードに同型Componentは1つ」を診断で強制しているため、
Component型完全修飾名がそのままComponentの同一性になる。メンバーは`(Component型完全修飾名,
MemberName)`のフラットな対で特定する(実Componentが`PositionX`等のフラットfloatで宣言されている
ため、入れ子の`PropertyPath`は不要)。

### 4. `SceneDocumentSnapshot`: Last Known GoodとSceneDiff

`SceneEditorSessionService`は`SceneKey`でpinしたSceneごとにimmutableなsnapshotを発行する。
static global stateでの購読(旧`SceneEditorHub`のGuidチャンネル)はPhase 9で廃止した。

- タイピング中のsyntax/semantic/DSL形状エラー(blocking error)では、直前の
  `LastKnownGoodModel`/`Projection`を保持したままsnapshotを発行し、ビューポートを空白にしない。
  アセットエラーはblockingにせず、Renderer側のノード単位フォールバックで処理する。
- `SceneDiffEngine`が旧Projectionとの意味差分(`NodeAdded`/`NodeRemoved`/`NodeReparented`/
  `NodeReordered`/`ComponentAdded`/`ComponentRemoved`/`ComponentChanged`/`MemberChanged`)を毎回
  算出する。**現時点でこのSceneDiffを消費する側は存在せず**、ビューポートは全再構築している。
  Transform変更だけでmesh再取得を走らせない増分更新はADR-0041決定事項11(M0020 Phase 7)で行う。

### 5. Editor→Code配管: `SceneEditOperation`とtrivia保持・1 Undoトランザクション

編集操作の語彙は`SceneEditOperationKind`として11種を先に確定した(SetComponentMember /
SetTransform / RenameNode / AddNode / RemoveNode / DuplicateNode / ReparentNode / ReorderNode /
AddComponent / RemoveComponent / AssignAsset)。Phase 9完了時点の実装は
`SetComponentMemberOperation`と`RenameNodeOperation`の2種で、残りはADR-0041のフェーズ割当に従う。

- `SceneSourceEditor.Apply`は対象ノードを`EditorNodeId`で解決し、値のRHS式だけを
  `Root.ReplaceNode(old, ParseExpression(new).WithTriviaFrom(old))`で置換する。周辺コード・
  コメント・改行は構造上保持される(ファイル全体の再生成はしない)。`.g.cs`への書き込みは拒否する。
- `SceneSourceEditor.ApplyLatest`は、操作起票時の`DocumentVersion`とコミット時のsnapshotが
  ずれていた場合、セッション内で安定な`EditorNodeId`により**一回だけ**リベースして適用し、
  `WasRebased`で報告する。解決できなければ無変更で失敗する。
- `SceneTextBufferWriter`は`SourceText`前後差分(`SceneTextChangePlanner.Plan`、開始位置降順)を
  live `ITextBuffer`へ適用し、全体を1つの`ITextUndoTransaction`で括る。**1ユーザー操作=1 Undo単位**。
- Undo/RedoはVSのテキストUndoに一本化する。`SceneModel`独自の編集履歴は持たない。Ctrl+Zでテキストが
  戻れば、決定事項1のループが表示も自動的に戻すため、「SceneとCodeの両方が元に戻る」が構造として
  成立する。

### 6. 選択契約

`SceneSelection`は複数選択(`NodeIds` + `PrimaryNodeId`)とComponent/Member単位の選択を最初から
モデル化し、`SceneSelectionOrigin { Hierarchy, Viewport, Inspector, Source,
ProgrammaticSourceSync }`で発生源を持ち回る(エコー抑制は発生源boolではなく`CorrelationId`で行う)。
Phase 9時点で発行されるのは`Hierarchy`のみで、`Viewport`はビューポートピッキング実装
(ADR-0041決定事項10)のための予約である。`SceneSelectionService`は意味的に同一の選択を重複発行
しない。

### 7. スレッド規律

このリポジトリで実際に踏んだ障害(VSIX JoinableTaskデッドロック、UIスレッド滞留)に基づく規律を
本基盤の契約とする。

- Roslyn解析・asset decodeはバックグラウンド。TextBuffer・WPF・D3Dサーフェスへの接触はUIスレッド。
- 解析は250msデバウンスし、旧`CancellationTokenSource`をキャンセルした上で世代ゲート
  (`SceneAnalysisGenerationGate`)により遅い解析結果が新しい結果を上書きしないようにする。
- `Thread.Join`/`.Result`/入れ子のJTF waitをUIスレッドで行わない。`AsyncPackage.InitializeAsync`の
  連鎖の内側でウィンドウ管理呼び出しをawaitしない。

### 8. Edit ModeはECS Worldを持たない

編集中のシーンに対して実行時の`World`を構築しない。Scene EditorはC#宣言(の解析結果)だけを読む。
Play時の挙動はSource Generatorが生成する`Instantiate`の責務であり、編集時表示と実行時生成が同じ
`SceneDeclarationAnalyzer`ソースを共有することで一致を担保する。

### 9. Gizmoドラッグはプレビューのみ、コミットはMouseUpで1回

ドラッグ中の連続的な値変化を毎フレームテキストへ書き込むとUndo履歴が破壊されるため、ドラッグ中は
表示側のみのプレビューとし、MouseUpで初めて決定事項5の経路により1つのUndoトランザクションとして
コミットする。この決定は本ADRで確定し、入力・ギズモ・プレビューAPIの具体設計は
[[ADR-0041-Scene-Editor-Interaction-Architecture]]が引き継ぐ。

## 既知の制約(ADR-0041 / M0020へ委譲)

- `SceneModelRenderObjectBuilder`はTransformを`global::UserEcsProject.Transform`のハードコードで
  認識するが、実シーンのTransformは`global::CSharpGameEngine.Runtime.Windows.Transform`であるため、
  実シーンが全ノードIdentity(原点固まり)で描画される実バグがある(M0020 Phase 0で修正)。
- 階層Transform合成(親子の`ToWorld`)は未実装(ランタイム側の既存ギャップに合わせた)。
- `SceneRenderer`は再解析のたびに破棄・再生成され、描画オブジェクト0件で例外を投げる。カメラ等の
  編集状態をRenderer内に持てない。
- ビューポート入力は皆無(`RenderSurfaceWindow`は`WM_SIZE`/`WM_MOUSEACTIVATE`/`WM_SETFOCUS`のみ)。
- ドラッグ&ドロップ配置とシェイプパレットは`.scene`書き込み経路(`SceneNodeAdder`/`SceneFile`)が
  発行元を失い恒久エラーになる、Phase 9受け入れ済み退行。`.scene`残骸コードの削除もM0020 Phase 0。

## 代替案として検討したもの

- **実行ベースプレビューの継続**(ADR-0031方式): 対象プロジェクトのビルド成功が前提で、タイピング
  中のシーンが見えない。生成コードのreflection実行はSG本体と別系統の解析を二重管理することになる。
  ライブRoslyn化により「入力中に見える」「SGと同一の解析ソースを共有する」の両方を得たため廃止。
- **`.scene` JSONの並行維持**: Visual SceneとCode Sceneの二重正本になり、同期そのものが恒久的な
  バグ源になる。C#テキスト単一正本に統一した。
- **`SceneModel`を直接編集可能な正本にする**(テキストを経ない): Undo/Redo・コメントと手書き整形の
  保持・外部エディタ/AI編集との整合を全て自前で再発明することになる。VSのテキストUndoと
  Workspaceイベントに乗る方が堅い。
- **`MSBuildWorkspace`の別ロード**: VS内には既に`VisualStudioWorkspace`があり、ソリューションの
  二重ロードはメモリ・整合性の両面で不利。`IComponentModel`経由で既存Workspaceを取得する。
- **ドラッグ中の逐次テキスト書き込み**: 1ドラッグでUndo履歴が数百件に汚染される。決定事項9の
  プレビュー/コミット分離で回避。

## 帰結

### 得られたもの

- C#テキストが唯一の正本になり、Git diff・コードレビュー・Ctrl+Z/Ctrl+Yがそのままシーン編集の
  履歴管理になる。
- タイピング中のシーンがビルド無しで250ms程度で描画に反映される。blocking error中も直前の正常表示が
  維持される。
- SGとエディタが同一の解析ソース(`SceneDeclarationAnalyzer`)を共有し、「エディタで見えるもの」と
  「実行時に生成されるもの」の乖離が構造的に起きない。
- 書き込み側の部品(trivia保持置換・リベース・1 Undoトランザクション)がユニットテスト済みで
  揃っており、ADR-0041はUI配線に集中できる。

### 失ったもの

- `.scene`ダブルクリックプレビュー(ドキュメントタブ方式)と、それに依存したドラッグ&ドロップ
  配置・シェイプパレット配置が一時的に機能しない(既知の受け入れ済み退行、M0020 Phase 0で
  Roslyn書き込みへ移行)。
- 実行ベースプレビューが持っていた「実行時と完全同一のWorld状態を見る」性質。編集時表示は解析からの
  導出であり、ランタイム描画(StageRenderer)とは別実装のプレビュー品質に留まる。

## 検証結果(実際に実行して確認)

本文書は2026-08の全面改稿(文書整備のみ、実装コード無変更)。改稿時に以下を実行して確認した。

```
git log --all --follow --oneline -- Docs/Decisions/ADR-0040-*.md
  → 394ff04 の1コミットのみ。git show 394ff04:… も既に文字化けしており、履歴からの復元は不能
    (本文はコード・テスト・コミット履歴から再構成した)

dotnet build CSharpGameEngine.slnx -c Debug → 成功(文書整備前後で不変)
dotnet test → 全件成功(実行結果はM0020開始前の基準値として[[M0019-CSharp-Scene-DSL]]に記録)
```

決定事項を固定している既存テスト(いずれもPhase 1〜9で追加済み):

- `SceneAnalysisParityTests.cs` — SG(net10.0側)とVSIX(net472側)の解析同値性
- `SceneEditorContractsTests.cs` — Reconcilerの2パス照合(並べ替え・一意リネームでのID維持、
  区別不能兄弟でのID非推測)、SceneDiffのMember単位検出
- `SceneSourceEditorTests.cs` — RHSのみ置換とtrivia保持、不正式の拒否、SceneKey不一致の拒否、
  `ApplyLatest`の1回リベース
- `SceneTextChangePlannerTests.cs` — テキスト差分の開始位置降順適用
- `SceneModelRenderObjectBuilderTests.cs` — Transform/PrimitiveMesh値抽出とNodePath同一性

## 関連機能

- F-L8-010 — Scene デザイナー: `.scene`からSceneModel(ライブRoslyn)描画への移行
- F-L4-002 — `CSharpGameEngine.Runtime.Windows`(`PrimitiveMesh`/`PrimitiveType`/`RenderServer`)

関連: [[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]、[[ADR-0031-Scene-Definition-3D-Preview]]、
[[ADR-0038-Startup-Scene-Declarative-Api]]、[[ADR-0041-Scene-Editor-Interaction-Architecture]]、
[[M0019-CSharp-Scene-DSL]]、[[M0020-Scene-Editor-Interaction]]
