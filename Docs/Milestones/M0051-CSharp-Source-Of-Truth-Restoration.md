# M0051 第1段: Scene正本のC#復帰と書き戻しの堅牢化

## 目的

M0050の実機確認でScene DesignerのJSON編集・保存が動作することを確認した直後、ユーザーから
新しい要望が出た:

> シーンビューとテキストを排他的にタブで切り替えたい(WPFデザイナーとXAMLと同じイメージ)。
> C#宣言型を正本にする。**作業1つ1つに同期せず、タブ切り替えで同期する**。
> どちらを編集しても「*」がつく。閉じるとき・ソリューションを閉じるときに保存警告を出す。
> **履歴処理はちゃんとやること。**

確認により、**C#宣言型を正本へ戻しJSONは完全撤廃**、Scene Designerは**VSのドキュメント
エディタ化**(「*」と保存警告をVS標準機能に委ねる)、作業は**2段階**に分ける、と決定した。
本ドキュメントは第1段(形式のC#復帰と書き戻しの堅牢化)を記録する。第2段(ドキュメント
エディタ化・タブ切替同期)は未着手。

## C#へ戻す判断を裏付けた実測

M0050の実機テストで残っていた未コミット差分を調べたところ、**ギズモで1ノードのYを動かした
だけなのに565行が書き換わっていた**。`SceneJsonSourceWriter`が全文再生成する設計のため、
無関係な値まで`1.0`→`1`、`0.2`→`0.200000003`(float32の10進展開)と表記が変わっていた。
C#経路は`SyntaxAnnotation`ベースの最小差分なのでこの種のノイズが構造的に発生しない。
ユーザーが求めるIntelliSense・Roslyn警告・補完に加え、**差分の綺麗さでもC#が優る**ことが
実データで確認できた。

## Step 1-1: 形式のC#復帰

M0049(コミット`c04fc1d`)以前のファイルがgit履歴からそのまま復元できることを確認し、大半を
`git show c04fc1d^:<path>`/`git checkout c04fc1d^ --`で戻した。

- **復元**: `UserEcsProject/Scenes/`の`MainScene.cs`(24行)・`FirstPersonStageScene.cs`
  (1053行、58ノード)・`FirstPersonStageAssets.cs`(17行)・`PrimitiveShowcaseScene.cs`(41行)、
  `GameSystemGenerator.cs`のC#構文プロバイダー、テスト5本、`Runtime.cs`のジェネリック制約
- **削除**: `*.scene.json`4本、`SceneJsonAnalyzer.cs`(301行)、`SceneJsonModel.cs`、
  `SceneJsonAnalyzerTests.cs`、`InMemoryAdditionalText.cs`、SCENE200-204診断、
  `System.Text.Json`参照、`<AdditionalFiles>`

**検証(重要)**: M0049直前のコミットを一時的にチェックアウトして生成物を採取し、復元後の生成物と
比較した結果、**生成コード10ファイルすべてがバイト単位で完全一致**した。復元が正確であることの
機械的な証拠になる。

## Step 1-2: VSIXのJSON撤廃

- **削除**: `SceneJsonModelBuilder.cs`(314行)、`SceneJsonSourceWriter.cs`(105行)、
  `OpenSceneJsonCommand.cs`(69行)、JSON専用テスト3本、`ActiveDocumentTrackerTests.cs`
  (`IsSceneCandidatePath`が消えるため)
- **復元**: `SceneWorkspaceService`/`SceneEditorSessionService`/`SceneDesignerContextMenuPolicy`/
  `ActiveDocumentTracker`/`SceneSourceLocation`/`.vsct`をM0050直前へ
- **削除**: `SceneDiagnosticsErrorListSync.cs`(M0050 Phase 4)。C#経路では`SceneDslAnalyzer`が
  赤線とエラー一覧の両方にSCENE1xxを出すため完全な重複になり、ADR-0040の「二重スクイグル防止」
  原則に反する。追加した動機(JSONにはアナライザーが無く診断が見えない)自体が消滅した。

テスト件数がM0049直前と一致(CodeAnalysis 101、VSExtension 601)。

## Step 1-3: 書き戻しの往復検証(省略不可と位置づけた部分)

M0049の動機はユーザーの「c#に反映されていない」という報告だった。当時の書き戻しは
「適用して書き込んで終わり」で結果を誰も確認しておらず、壊れたことに気付けるのは実機で目視した
ときだけだった。第2段ではタブ切替のたびに書き戻すため実行回数が大幅に増える。先に堅牢化した。

### 往復検証(round-trip verification)

- `src/CSharpGameEngine.SceneModel/SceneModelStructuralSignature.cs`(新規、純粋クラス):
  `SceneModel`からID・ソース位置に依存しない正規化文字列を再帰生成する。
- `tools/CSharpGameEngine.VSExtension/SceneSaveVerification.cs`(新規): 書き戻した構文木を
  `Compilation.ReplaceSyntaxTree`で差し替えて`SceneDeclarationAnalyzer`で**読み直し**、
  再構築した`SceneModel`の署名を`Document.Model`の署名と比較する。不一致なら
  **テキストへ一切書き込まずに中止**する(壊れた結果を保存するより保存できないほうがましという判断)。
- `SceneEditorSessionService.SaveAsync`へ関門として組み込み。`SceneWorkspaceEditContext`に
  再解析用の`Compilation`を追加した。

### 履歴エントリごとの中間構文木(第2段への土台)

`SceneSourceEditor.ApplyForSaveSteps`(新規)は`ApplyForSave`と同じ再生を行うが、**履歴エントリ
1件ごとの中間構文木**を順に返す。第2段でこれを1件ずつ別々のVS Undoトランザクションとして
書き戻すことで、デザイナーの操作履歴とテキストのUndo履歴が1対1で対応する。差分は
`Steps[i-1]`→`Steps[i]`で直接計算できるため、バッファの再パースが要らない。
既存の`ApplyForSave`はこの新APIのラッパーにした(呼び出し元は無変更)。

### 検証の追加によって発見した既存バグ2件(M0047から存在)

往復検証のテストを書いた過程で、**実際に壊れていた経路**が2つ見つかった。どちらも
「Spanで構文を探す」ことが原因で、他の操作が既に`SyntaxAnnotation`で追跡していたのに
この2箇所だけ取り残されていた。

1. **`ApplySetTransform`**: `Transform.Location.Span`との一致だけで構文を探していたため、
   同一Nodeに対して**先にRenameしてから移動**するとRenameでテキスト長が変わってSpanがずれ、
   「Transformのobject creationがSourceに見つかりません」で保存が失敗していた。
   「移動→Rename」「移動→移動」は成功するという**非対称な壊れ方**だったため気付きにくかった。
2. **`ApplyRename`**: Nameの右辺を`NameValueLocation.Span`で探していたため、同一Nodeを
   **2回Rename**すると同じ理由で失敗していた。

どちらもAnnotation経由でNodeのobject creationを特定してから、その**内側から**目的の構文を
引く方式へ変更した(型名/メンバー名での検索はSpanと違い前段の編集でずれない)。
操作順序4パターンの回帰テストを追加した。

**これはまさに往復検証を入れた目的そのものが機能した実例**であり、M0049の「壊れやすい」という
評価が妥当だったことの裏付けでもある。

### 実装中に発見したもう1つの事実

署名の実装中、`SceneModelEditApplier.WithMembers`がメンバーを差し替えても
`SceneComponent.EmitExpressionText`(Component全体の`new T { … }`式)を**古いまま引き継ぐ**ことを
実測で発見した。解析直後のModelでは正しいが、セッション中に編集されたModelでは陳腐化している。
これで比較すると「正しく書き戻したのに不一致」という誤検知になるため、署名は個々の
`SceneComponentMember`から組み立てる方式にした(理由をXMLコメントに記録)。

## テスト

新規17件:
- `SceneModelStructuralSignatureTests`(8件): 書式・コメントの違いでは署名が変わらないこと、
  値/enum/名前/構成/位置の変化は確実に捉えること、定数式は畳み込み後の値が同じなら等価とみなすこと
- `SceneSaveVerificationTests`(9件): 正しい書き戻しが検証を通り手書きコメントが保たれること、
  **壊れた書き戻しが検証で止まること**(これが無いと常にtrueを返す実装でもテストが通ってしまう)、
  `ApplyForSaveSteps`が履歴と1対1で対応すること、操作順序4パターンの回帰

全1112件成功(リグレッションなし)。

## 第2段(未着手)

ドキュメントエディタ化(`IVsEditorFactory`+マルチビュー)、タブ切替での遅延同期、
履歴のテキスト側一本化、「*」と保存警告のVS標準委譲。計画は
`C:\Users\kazuh\.claude\plans\`のプランファイルに記録済み。着手前にVS SDKのスパイク
(`IVsCodeWindow`のホスティング可否、`SetDocDataDirty`の実挙動など)が必要。

## 検証

- `dotnet build CSharpGameEngine.slnx`: 0エラー・0警告
- `dotnet test CSharpGameEngine.slnx --no-build`: 全8プロジェクト成功、1112件中失敗0
- 生成コード10ファイルがM0049直前とバイト単位で完全一致することを実測確認
- **未実施(実機確認が必要)**: `.cs`のScene編集でIntelliSense・SCENE1xx赤線が出ること。
  既存ToolWindowのScene Designerでギズモ編集→Ctrl+S→`.cs`へ正しく反映されること。
  ビルド・F5でゲームが従来どおり起動・描画されること。

---

# Step 1-4(追補): Hierarchy保存の不具合修正とディスク保存

第1段をコミット後の実機確認で2件の報告を受けた。

> 1. ヒエラルキーで編集してもctrl+sでc#ソースを正本として再構築されて取り消される
> 2. シーンビューで編集→ctrl+s→csにメモリ上で反映して`*`になるが、**そうではなくさらにそれを保存して**

## 報告1の原因は3つ重なっていた

### 原因A: Ctrl+Sが3Dビューポートのフォーカスで門番されていた

`SceneEditorSessionService.SaveAsync`へ到達する経路は`SceneHwndHost.TickUndoRedoIfNeeded`の
`GetKeyState`ポーリング**1本だけ**で、条件が`IsViewportFocusedProvider`
(=`RenderHostGrid.IsKeyboardFocusWithin`)だった。Hierarchyツリーで編集した直後はフォーカスが
Hierarchy側にあるため**Ctrl+Sは保存へ一切届かず**、VS標準の保存が未変更の`.cs`をそのまま保存して
いた。ギズモ編集だけが動いていたのは操作直後にビューポートへフォーカスが残るためで、報告された
非対称性と完全に一致する。

**修正**: 新規`SceneSaveTriggerPolicy`(純粋クラス)と、`SceneEditorHub`への補助サーフェス
(Hierarchy/Inspector)のフォーカス登録を追加し、**保存だけ**引き受け範囲を広げた。
Undo/Redoは範囲を広げていない — M0037で「ペインを標準コマンドとして登録したらVS全体のUndo管理が
壊れた」実機回帰を起こしM0045で撤退した経緯があり、誤って別ドキュメントのUndoを走らせる危険が
あるため。

### 原因B: 再解析がHierarchyの表示を未編集モデルで上書きしていた(「取り消される」の実体)

`CSharpGameEngineViewerPackage`が`SceneWorkspace.ModelChanged`を`SceneEditorHub.PublishSceneModel`へ
**直結**していた。保存起因の再解析が発火するとHierarchyはC#由来の未編集モデルから木を再構築する。
一方`SceneExternalChangePolicy`はテキストが実際には変わっていないため`Ignore`を返し
`PublishDocument`を呼ばないので、Hubには未編集モデルが残り続ける。M0048でDocument→Hubの橋渡しを
追加した際、この古い直結を残したことによる競合。

**修正**: 新規`SceneHubPublicationPolicy`(純粋クラス)で選別してから発行する。未保存編集を持つ
pin中Sceneと同じ対象、および解析対象が無くなった(null)通知は流さない。別Sceneや未pinのときは
従来どおり流す(Hierarchyはpinしていない文書のSceneも表示するため)。

### 原因C-F: 構造編集の保存自体が壊れていた(テストで発見)

**構造編集を履歴再生経路(`ApplyForSave`)で通すテストが1本も無かった**ことが判明したため、
新規テスト10件で穴を埋めたところ、4件の不具合が再現した。

1. **`SceneModelEditApplier.TryFindByIdentity`が`ReferenceEquals`で探していた**。直前の
   `TryRemoveChildAt`が木を関数的に作り直すため新親は必ず別インスタンスになり、この関数は常に
   falseを返していた。結果、**親を跨ぐReparentは`Document.TryApply`の時点で必ず失敗**し、
   Hierarchyのドラッグ&ドロップでの親子変更が一切効かなかった。XMLコメントが記していた本来の意図
   (Name+Component構成での照合)どおりに実装し直した。
2. **`ApplyForSaveSteps`の事前Annotateが操作対象の親を注釈していなかった**(`ApplyBatch`は元から
   注釈している)。Remove/Reorder/Reparentの親がSpan一致フォールバックへ落ち、履歴2件目以降
   (兄弟をRenameしてからDelete等)で親のSpanがずれて保存が中止されていた。
3. **`ApplyAddNode`の一意名採番が投影(保存開始時点の実C#)の子を見ていた**。1セッションで同じ親へ
   2回AddChildすると`SceneModelEditApplier`側は`Node`/`Node (1)`、ソース側は`Node`/`Node`となり
   往復検証が必ず不一致になっていた。書き戻し中の構文木から集めて採番規則を一致させた。
4. 3に伴い子要素の列挙を追加。`TryAppendChild`と同じ3形態(コレクション式/明示配列/暗黙配列)に対応。

### 原因D: 例外時にフラグが立ったまま残る

`SaveAsync`の`finally`が`SavesInFlight`しか戻さないため、`ApplyTextAsync`が例外を投げると
`AwaitingOwnSaveReanalysis`がtrueのまま残り、次の**無関係な**再解析が`TrustOwnSave`と誤判定されて
`Document.ResetTo`で未保存の編集が全消去されうる。例外経路でも必ず戻すようにした。

## 報告2: ディスク保存の実装

`SaveAsync`は`ApplyTextAsync`→`SceneTextBufferWriter.TryApply`→`Buffer.Replace`で終わっており、
**リポジトリ全体にファイル保存の呼び出しが1件も無かった**(M0047が「保存トリガーはビューポートの
Ctrl+Sのみ」と明記した既知の設計)。

`SceneWorkspaceService.TrySaveDocumentToDisk`を追加し、`IVsRunningDocumentTable.SaveDocuments`
(`RDTSAVEOPT_SaveIfDirty`)で**対象Sceneの1ファイルだけ**を保存する(Save All相当にはしない)。
docCookieは既存の`FindAndLockDocument`が破棄していた最後のoutから取る。保存はバッファ内容を
変えないため`RealContentActuallyChanged==false`となり、追加の再解析が来ても`Ignore`に落ちる
(M0048の不変条件をそのまま利用)。書き戻しが成功していれば編集は失われないので、ファイル保存の
失敗は警告に留める。

## テスト

新規20件(`SceneSourceEditorStructuralSaveTests` 10件、`SceneSavePolicyTests` 10件)。
特に**構造編集を履歴再生経路で通すテスト**は今回が初めてで、原因C-Fはすべてこれで再現・固定した。
全1132件成功。

VS SDK依存部分(フォーカス配線・`SaveDocuments`・Hub発行の実配線)はCodeVerified止まりで実機確認が必須。
