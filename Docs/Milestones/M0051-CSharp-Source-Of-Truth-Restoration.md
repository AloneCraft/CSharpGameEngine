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

> **【Step 1-5で訂正】この判断は誤りだった。** M0037の回帰の原因は「`OleMenuCommandService`への
> 標準コマンド登録」という手段に帰属しており、`GetKeyState`ポーリングの門番範囲が原因という記述は
> どこにも無い(`Conformance.md`はむしろ「M0045は今回の真因ではなかった」と記している)。
> この据え置きがそのまま「ヒエラルキーは取り消しできない」という次の報告になった。

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

---

# Step 1-5(追補): 保存後のUndoとHierarchyのUndo

Step 1-4をコミット後の実機確認で、Undoについて2件の報告を受けた。

> シーンビューの編集は取り消しも保存もできるが**保存した後も取り消しできるようにして**
> **ヒエラルキーは取り消しできない**のでできるようにして

## 報告1「HierarchyでUndoできない」: 門番が2箇所ともビューポート限定だった

`SceneHwndHost.TickUndoRedoIfNeeded`が`Focused`(=ビューポートのキーボードフォーカス)を要求し、
さらに`SceneDesignerToolWindowControl.TryUndoFromCommand`が
`SceneDesignerUndoCommandPolicy.CanExecute(IsViewportFocused,…)`で再チェックしていた。
**Hierarchy側の編集履歴はラベル付きで正しく積まれており**("Rename node"/"Remove node"/
"Reparent node")、欠けていたのは入力経路だけだった。Step 1-4がCtrl+Sだけを広げてUndo/Redoを
据え置いた判断(上記の訂正を参照)がそのまま残っていた形。

**修正**: 新規`SceneDesignerInputRoutingPolicy`(純粋クラス)へUndo/Redo/Saveの範囲判定を集約し、
3つとも同じ範囲にした。あわせて、Step 1-4が`SceneSaveTriggerPolicy`を純粋クラスとして切り出し
ながら**製品コードから呼んでいなかった配線漏れ**(`SceneHwndHost`が生の論理和で同等判定をして
いた)も是正した。テストは通るのに製品コードはポリシーを経由しない、という状態が残っていた。

### TextBox入力中の扱いはキーで分ける

門番を広げると新しい退行が生まれる。Hierarchyのインラインリネームも Inspectorの数値編集も
`TextBox`を使い、補助サーフェスのフォーカス判定は`IsKeyboardFocusWithin`ベースなので
**テキスト入力中もtrue**になる。

| キー | テキスト入力中 | 理由 |
|---|---|---|
| Ctrl+Z / Ctrl+Y | **引き受けない** | `TextBox`自身が文字単位のUndoを持つ。除外しないとリネーム途中のCtrl+Zが「文字の取り消し」ではなく「シーン全体のUndo」になる |
| Ctrl+S | **引き受ける** | `TextBox`に「保存」の意味は無い。ここで降りるとVS標準の保存が未反映の`.cs`をそのまま保存し、Step 1-4で直した不具合へ戻る |

## 報告2「保存後にUndoできない」: `ResetTo`が履歴を消していた

保存の着地(`TrustOwnSave`)で`SceneEditDocument.ResetTo`が`UndoStack`と`RedoStack`を消すため、
保存後のCtrl+Zにはメモリ内に戻す先が無かった。なお`MarkSaved`は履歴を消さないので、保存完了から
再解析着地までの約250ms+解析時間だけは効いていた — 実機の「保存後は効かない」はこの窓の外の話。

**履歴を保持し続けても解決しない。** `SceneEditDocument`は逆操作を一切持たず、
`SceneSourceEditor.ApplyForSaveSteps`は履歴を**前方向に再生**する設計だからで、保存点より前へ
戻せるのはC#テキストの履歴だけ。ADR-0040の「C#が唯一の正本」とも一致する。したがって
テキストUndoへの委譲は選択肢ではなく**必然**であり、承認済みの第2段の履歴設計を前倒しで実装した。

| 状態 | デザイナー面でのCtrl+Z |
|---|---|
| 未反映の編集がある | `SceneEditDocument`のスタックから1件戻す(メモリ内・即時) |
| 未反映の編集が無い | **テキストUndoへ委譲**(`SceneTextUndoDelegationPolicy`) |

委譲後の表示追従は既存機構がそのまま処理する。テキスト変更→再解析→`IsDirty==false`のため
`SceneExternalChangePolicy`が`FollowSilently`を返し、確認ダイアログ無しで自動追従する。

**Undo後はタブに`*`が付く**(ユーザー確認済みの仕様)。自動で保存し直さない — 通常のテキスト編集を
Undoしたときとまったく同じ挙動にする。ディスクへ反映するにはもう一度Ctrl+S。

### 委譲先を誤らないための所有判定

テキストUndo履歴には**ユーザーがコードエディタで手打ちした編集**も同じスタックに積まれている。
無条件に委譲すると、Ctrl+Zを押し続けたときデザイナー由来でない編集まで巻き戻す。

> **【実機修正】当初は`ITextUndoHistory.LastUndoTransaction.Description`に接頭辞を付けて判定する
> 設計だったが、これは成立しなかった。** 下の「実機で判明したVS SDKの制約」を参照。
> 現在は`SceneTextUndoLedgerPolicy`が「自分が積んだ単位数」と「自分が最後に触った直後のバッファ
> 版番号」で判定する。

これはVS標準`Edit.Undo`との二重発火に対する安全弁も兼ねる。`GetKeyState`ポーリングはキー入力を
消費しないため理屈の上ではVS側も同時に走りうるが、M0046の実機記録(ツールウィンドウがフォーカスを
持つとツールバーの履歴ドロップダウンが空になり、コードエディタのCtrl+Zが効かなくなる)は
**デザイナー面フォーカス中はEdit.Undoが不活性**であることを示しており、委譲はデザイナー面
フォーカス時にしか起きないので条件が揃わない。仮に実機で二重発火が観測されても、説明文判定に
より2回目は止まるため被害は1段に留まる。`ITextUndoHistory`の深さをフレーム跨ぎで監視して補償する
案は、「起きるか不明のバグ」を「タイミング依存で再現しないバグ」に変換するだけなので採らなかった。

## 保存を1操作=1 Undo単位にした

従来は保存1回=テキストUndo1単位だったため、3回編集してから保存すると保存後のCtrl+Zが3回分まとめて
戻ってしまう(確定仕様は「1操作=1 Undo単位」)。Step 1-3で用意した`ApplyForSaveSteps`の中間構文木を、
新規`SceneWorkspaceService.ApplyTextStepsAsync`がステップごとの独立トランザクションとして書き出す。

既存の`ApplyTextAsync`をループで呼べないのは、あちらが`Context.Text`を常に「適用前テキスト」として
使うため。2回目以降はバッファが既に変わっており`SceneTextBufferApplyGuard`が必ず不一致を返す。
ステップごとに`Before`を繰り上げる`SceneTextWriteStep`を導入した。各ステップの差分は
`SceneEditorSessionService.BuildWriteSteps`が**書き込み前にオフラインで検算**し、全ステップを
組み立て切ってから連続適用するため、途中で破綻する余地が無い。

## あわせて修正した潜在バグ: `ResetTo`が無関係なSceneまで巻き込んでいた

`AwaitingOwnSaveReanalysis`は**bool**だったが、再解析イベントは**1回で複数の`SceneModel`**を運ぶ。
そのため保存した1つと同じイベントに乗った**無関係なScene**まで`TrustOwnSave`と誤判定され、
`ResetTo`で未保存の編集とUndo履歴が消えていた。`SceneKey`で照合する
(`SceneOwnSaveLandingPolicy`)。保存後Undoを入れるとこの害が確実に顕在化する(Sceneを2つ開いて
片方を保存しただけでもう片方の履歴が黙って消える)ため、今回に含めた。

## テスト

新規29件(`SceneUndoRoutingPolicyTests` 15件、`SceneSaveStepsTests` 4件、
`SceneEditDocumentTests`と`SceneSavePolicyTests`への追加10件)。全1161件成功。

`SceneSaveStepsTests.Steps_ApplySequentiallyToReachTheFinalText`が本命で、`BuildWriteSteps`が
書き込み前に行うオフライン検算を同じ形で再現している。各ステップの置換範囲が全文に及んでいない
ことも確認する — 全文差分へ落ちると変更は「1個」のままspanがファイル全体を覆うため、
変更の**個数**ではなく**範囲の広さ**で見る必要がある。

## 既知の制限

`CompositionTarget.Rendering`は`SceneHwndHost`のサーフェスが生きている間しか回らないため、
**Scene Designerを閉じている/タブ裏にしている状態ではHierarchy単独のCtrl+Z・Ctrl+Sが効かない**
(Step 1-4で保存について記録した制限と同じ)。Hierarchy側に独立したティック源を持たせるのは、
第2段でツールウィンドウ構成が変わるためそこまで持ち越す。

委譲Undoの表示追従は再解析(250ms+)待ちのため、連打中は表示が遅れて見える(動作自体は正しい)。

> **【Step 1-6で解消】この遅さは実用に耐えないと実機で判断された**(「メモリ上のctrl+z,ctrl+yは早い。
> 保存すると遅くなる。これはよくない」)。委譲そのものをやめ、メモリ内スタックをセッション全体で
> 保持する方式へ変えた。下のStep 1-6を参照。

VS SDK依存部分(`ITextUndoHistory`操作・フォーカス配線)はCodeVerified止まり。**特に
「デザイナー面フォーカス中にVS標準のEdit.Undoが二重発火しないこと」は実機確認が必須**で、
M0046の確認項目8・M0047の項目9として挙げられたまま結果が記録されていない項目でもある。

## 実機で判明したVS SDKの制約: `VsUndoHistory`はスタックを覗けない

Step 1-5をコミット後の実機確認で、シーンビューのCtrl+Zが例外で落ちると報告された。

```
System.NotSupportedException: 指定されたメソッドはサポートされていません。
  Microsoft.VisualStudio.Editor.Implementation.Undo.VsUndoHistory.get_LastUndoTransaction()
  → SceneTextBufferWriter.GetLastUndoDescription
  → SceneEditorSessionService.TryDelegateToText → TryUndo
  → SceneHwndHost.TickUndoRedoIfNeeded
  → System.Windows.Media.MediaContext.AnimatedRenderMessageHandler
```

VSの`ITextUndoHistory`実装(`VsUndoHistory`、VSのOLE undo managerのシム)は
**スタックを覗く系のメンバーを実装していない**。使えるのは`CanUndo`/`CanRedo`/`Undo(n)`/`Redo(n)`/
`CreateTransaction`だけで、`LastUndoTransaction`/`LastRedoTransaction`/`UndoStack`/`RedoStack`は
`NotSupportedException`を投げる。**説明文で所有を判定する設計そのものが成立しなかった。**

これは`Document.CanUndo`が偽になった瞬間に必ず踏む — 保存後だけでなく、**戻すものが無い状態で
Ctrl+Zを押しただけ**でも落ちる、非常に到達しやすい経路だった。

> 副次的な確認: 検討して見送った「`UndoStack`の深さをフレーム跨ぎで監視して二重Undoを補償する」案は、
> `UndoStack`が同じく未実装であるため**そもそも実現不可能だった**。採らない判断は結果的に正しかったが、
> 理由は当時挙げた「タイミング依存のバグに変換するだけ」ではなく、単に不可能だったからである。

### 所有判定を台帳方式へ置き換えた

`SceneTextUndoLedgerPolicy`が使うのは実績のある2つだけ:

1. **保存が実際に積んだトランザクションの数**(`SceneLiveBufferWriteResult.AppliedUnitCount`。
   差分が空のステップはトランザクションを作らないので`Steps.Count`とは一致しないことがある)
2. **自分が最後にバッファを触った直後の`ITextSnapshot.Version.VersionNumber`**

版番号は単調増加するので、記録値と現在値の一致が「以降だれもバッファを触っていない」ことの証明に
なる。食い違えばユーザーがコードエディタで手打ちしたということで、スタックの一番上はユーザーの編集
だから委譲しない。自分の単位を使い切ったら、その下にあるユーザーの編集へは降りない。
保存を続けたときは累積するが、間に手打ちが挟まっていれば累積せず数え直す。

テキストバッファへ書き込むのは`SceneTextBufferWriter.TryApply`の**1箇所だけ**なので、版番号による
判定が他経路に汚されることはない。

`SceneUndoTransactionNaming`の接頭辞は残した。所有判定には使えないが、VSのUndoドロップダウンに
`Scene デザイナー: Rename node`と並んで人が判別できる利点はそのまま。

### レンダリングループを巻き込まないようにした

例外が`CompositionTarget.Rendering`のハンドラを貫通してWPFの描画ごと落としていた。
`TickUndoRedoIfNeeded`のイベント発火を`try`/`catch`で囲み、VS SDKの想定外は`ActivityLog`へ残して
**そのキーが何もしなかった**扱いに留める。ポーリングは毎フレーム回る場所なので、ここで例外を
逃がすと1回の失敗がツールウィンドウ全体の停止になる。

---

# Step 1-6(追補): 保存後のUndo/Redoを保存前と同じ体感にする

Step 1-5をコミット後の実機確認で次の指摘を受けた。

> メモリ上のctrl+z,ctrl+yは早い。保存すると遅くなる。これはよくない

Step 1-5は保存後のCtrl+Zをテキストへ**委譲**していた。委譲は表示を更新せず、追従をRoslyn再解析に
任せていたため、保存の前後で体感がはっきり変わっていた。

ユーザー確認済みの仕様:
- 保存後にCtrl+Zした瞬間、**C#ソースも一緒に戻る**(タブに`*`が付く)。表示だけ戻す案は選ばれなかった。
- 遅いと感じたのは**ギズモ操作**と**Hierarchyの追加・削除・親子変更**の両方。

## 遅延の内訳と、遅い以前に壊れていた経路(Stage A)

`ScheduleAnalysis`は呼ばれるたびに保留中のタイマを取り消して250msで張り直していた。VSの背景処理は
対象Sceneと無関係な理由でも頻繁にworkspace変更を起こすため、**デバウンスが際限なく延び続ける**。
その後の`GetCompilationAsync`/`GetSemanticModelAsync`(テキスト編集でCompilationは必ず無効化される)が
支配的。`SceneAnalysisDebouncePolicy`で最初に保留になった時点からの上限(750ms)を設けた。

さらに**遅いのではなく更新されない経路**があった。`AnalyzeAllAsync`の対象は`ActiveDocumentId`
ひとつだけで、`ActiveDocumentPath`はコードエディタでアクティブな文書からしか更新されない。
Scene Designerで作業中に別ファイルがアクティブだと、**Scene Designer自身が書き換えたテキストにすら
再解析が来ず表示が永久に追いつかない**。`SceneAnalysisTargetPolicy`で
「アクティブ文書 ∪ pin中Scene」を解析対象にした。

## 「古いスナップショットを配信すれば即時」は成立しない

素朴な案として「`ResetTo`でスタックを捨てず、保存前のスナップショットを配信する」が考えられるが、
これは**速くなるどころか遅くなる**。

`SceneProjectionReconciler`は同じ親の子の範囲内でName+Component一致 → Component構成のみ一致の
2パスで照合し、候補がちょうど1件のときだけIDを引き継ぐ。保存着地時の`Publish`が渡す引き継ぎ元は
**保存前の実解析投影**なので、セッション中に追加したノードには新IDが払い直される。古いDocument
リネージのスナップショットをそのまま配信するとID世代が混ざり、`SceneDiffEngine.Compare`が大量の
Add/Removeを返して`SceneIncrementalUpdatePolicy`が`Rebuild`(生スレッド+bridgeプロセス)へ落ちる。
`SceneSelectionService`が保持する`EditorNodeId`の解決先も食い違う。

## Stage B: ウォーターマーク方式

中心は`SceneEditDocument.TextWatermark`。不変条件は

> `0 <= TextWatermark <= History.Count` かつ「C#テキスト == `History[0..TextWatermark)` を再生した状態」

- **Undo/Redoは常にメモリ内スタックが駆動する**。テキストへの反映は「メモリ内Undoの副作用」。
- 保存は`UnwrittenHistory`(部分列)だけを書き出す。履歴を保持するようになったので、全体を毎回
  先頭から再生すると二重適用になる。
- `IsDirty`を参照比較(`LastSavedModel`)から**ウォーターマークとの距離**へ置き換えた。
  参照比較のままだと、保存済みの編集をUndoしたとき部分列が空なのにdirtyになり、
  `ApplyForSaveSteps`が`FinalRoot=null`を返して**「保存に失敗しました」になる**
  (`SceneSaveSuffixPolicy`が`AlreadyInSync`で受け止める)。

### Undo/Redoは非対称にした

| 状態 | Ctrl+Z | Ctrl+Y |
|---|---|---|
| `Count > W`(未保存の編集を操作) | メモリ内のみ | メモリ内のみ |
| `Count == W` かつテキスト同期可 | メモリ内 + テキストUndo | メモリ内 + テキストRedo |
| `Count == W` かつテキスト同期不可 | **何もしない**(理由をステータスバーへ) | **メモリ内のみ** |

Undoでメモリがテキストより**後ろ**へ下がる状態はウォーターマークで表現できないので止めるしかない。
Redoで**先**へ出る状態は「まだ保存していない編集がある」そのものなので、同期できなくても進める。
この非対称が無いと、メモリ内だけ戻した編集をRedoできなくなる。

前向きのテキスト編集で強制同期する案は採らない — 新しいトランザクションを積むことになり
「1操作=1 Undo単位」を壊し、ユーザーの編集と競合する。

### 保存の着地でDocumentを維持する(条件付き)

`TrustOwnSave`での`ResetTo`をやめ、Documentをそのまま維持する。往復検証
(`SceneSaveVerification`)が構造同値を保証しているので普通はそれで正しい。

**ただし無条件ではない。** `SceneDocumentLineagePolicy`がDocumentの投影と実解析の投影を比較し、
食い違えば従来どおり`ResetTo`へ縮退する(Stage Aの体感へ落ちるが壊れない)。食い違いは実在する:

1. **Name+Component構成が同一の兄弟**は`Candidates.Count==1`を満たさず両方に新IDが払われる
2. 書き込みから着地までの窓で**ユーザーがバッファを直接編集**した場合

検証が無いと、リネージがずれた瞬間に後続の保存が「保存時に対象Nodeの構文を特定できません」で
**失敗する**。これは追加したテスト(`Suffix_ResolvesNodesAddedByAlreadyWrittenEntries`)が
実際に再現した — 最初の実装では本当にこのエラーで落ちた。

リネージを一致させるため、**自分の書き込みの着地でだけ**`Publish`のID引き継ぎ元をDocumentの投影に
する(`IdentityBasis`)。pin時・外部編集の追従・単発解析は従来どおり実解析投影のまま。

### 判定の分離と台帳の縮小

`SceneExternalChangePolicy`から`IsOwnSaveLanding`/`TrustOwnSave`を外した。自分の書き込みの着地は
「外部変更かどうか」ではなく「リネージが一致しているか」で決まるため、別の判定へ分離した。

`SceneTextUndoLedger`は`SceneTextSyncState`/`SceneTextSyncPolicy`へ縮小した。`UndoableUnits`は
ウォーターマークと同じ量の別表現だったので削除(二重に持つと片方だけずれたときに壊れ方が読めない)。
`RedoableUnits`は残す — 「メモリ内だけ戻した」と「テキストごと戻した」はどちらも
`Count == W`に見えるため、ウォーターマークでは判別できない唯一の情報。テキストに触れない編集が
積まれたら`AfterInMemoryEdit`で無効化する。これを忘れると、メモリ内だけ戻した編集をRedoしたときに
**無関係な古いテキストRedoが走って乖離する**。

## 効果範囲(正直な見積もり)

**この変更が消すのは「保存の前後で体感が変わること」であって、「全部が即時になること」ではない。**

`SceneIncrementalUpdatePolicy.Classify`が`Rebuild`を返すケースは、**保存前のメモリ内Undoでも**
生スレッド+bridgeプロセスを使うため元々非同期:
ノードの追加/削除、親変更・並べ替え、Componentの追加/削除、`PrimitiveMesh`/`StaticMesh`ロールの変更、
およびRebuild飛行中に次のスナップショットが来た場合の連鎖。

即時のままなのは**Transformを含む非メッシュComponentの値編集のみ**。ユーザーが挙げた
「Hierarchyの追加・削除・親子変更」は保存前も保存後もbridge往復が残る。本Stepでこれらは
「保存前と同じ速さ」になるが「即時」にはならない。構造編集そのものの高速化
(`ResidentBridgeClient`の常駐活用)は**別課題として切り離す**。

## テスト

全1191件成功(VSExtension 689件)。Step 1-6で新規・書き換えたのは30件超。

`SceneProjectionIdentityTests`は引き継ぎが効くことに加えて、**区別できない兄弟でIDが失われる
既知の限界も明示的に固定している** — 限界を消したのではなく「検知して安全側へ縮退する」設計だと
記録するため。

## 実機確認

1. ギズモで移動 → Ctrl+S → Ctrl+Z。**保存前のCtrl+Zと体感が同じ**であること(本Stepの主目的)
2. 同じ操作でC#ソースも戻り、タブに`*`が付くこと
3. Hierarchyで追加・削除・親子変更 → Ctrl+S → Ctrl+Z。保存前と同じ速さになること
   (bridge往復ぶんの遅さは保存前と同様に残る — それが仕様)
4. **pin中Sceneの`.cs`をアクティブにせず**別ファイルを開いた状態でCtrl+Z → 表示が追いつくこと
5. 保存 → コードエディタで手打ち → Ctrl+Z → **何も起きず**ステータスバーに案内が出ること
6. Name+Component構成が同じ兄弟を持つSceneで、保存 → 編集 → 保存が失敗しないこと(縮退の確認)
7. **VS標準`Edit.Undo`の二重発火**の有無。Ctrl+Zがメモリとテキストの2箇所を同時に動かすため、
   二重発火するとテキストだけ2段戻る。リネージ検証が次の着地で拾って縮退するので自己修復するが
   1段の乖離は出る。M0046項目8・M0047項目9として未記録のまま残っている項目でもある
8. Scene 2つを開いて片方を保存 → もう片方の履歴とウォーターマークが壊れないこと

---

# Step 1-7(追補): Step 1-6が持ち込んだ回帰2件の修正

Step 1-6の実機確認結果は「項目1 OK / 項目2 ×(C#ソースが戻らない) / 項目3 OK」で、
加えて**Hierarchyのインラインリネームが確定しない**という報告を受けた
(`SM_Cube8`をF2 → `8`と入力 → Enter → 名前が変わらない。以前は確定できていた)。

## 不具合1: リネージ一致判定が構造的に必ず失敗していた

リネージ判定に`SceneDiffEngine.Compare(Document.Projection,RealProjection).IsEmpty`を使ったが、
`SceneDiffEngine`は**Rendererの増分更新分類**が目的でComponentの`EmitExpressionText`をOrdinal比較する。
一方`SceneModelEditApplier.WithMembers`は**この式テキストを古いまま引き継ぐ**
(同クラスのテストが「`ComponentChanged`のみ許容」として明文化している)。
そのためギズモ編集の着地では差分が必ず1件出て、**`KeepDocument`は実機で一度も返らなかった**。
毎回`Rebase`→`ResetTo`でウォーターマークも履歴もテキスト同期状態も消えており、
**Stage Bの高速経路そのものが死んでいた**。項目1がOKに見えたのは、リベース後に積み直した
未書き出しの編集を`InMemoryOnly`で戻していたためで、保存済みの編集を戻していたわけではない。

**Step 1-3で一度踏んで回避した罠と同じ**。そのとき新設した`SceneModelStructuralSignature`の
docコメントには「解析由来のModelと編集由来のModelのどちらでも同じ署名が得られるよう
`EmitExpressionText`を使わない」と理由まで書いてあったのに、再発させた。

### 判定を独立させ、2つの検査の論理積にした

新規`SceneLandingDecision`(VS SDK非依存):

| 条件 | 結果 |
|---|---|
| 自分の着地でない | `Rebase` |
| 実解析がnull / `HasErrors` / `Root`なし | **`Defer`**(何もしない) |
| ID対応の全単射が崩れている | `Rebase` |
| 構造署名が違う | `Rebase` |
| すべて通過 | `KeepDocument` |

- **ID対応**(新規`SceneProjectionIdentityComparer`)は「次の保存で部分列の`Operation.NodeId`が
  実投影上で解決できる」という本来の必要条件そのもの。**全単射で見る** — 片方向だと
  Document側にしか無いPathとReal側にしか無いPathの一方を取りこぼす。
- **構造署名**(`SceneModelStructuralSignature`)は`EmitExpressionText`を見ないのでギズモ編集でも一致する。
  ID対応だけでは、着地までの窓でユーザーが値を手打ちしたケースを検知できず
  その編集が表示から永久に落ちる。
- **`Defer`が要る理由**: 解析エラー時に`LastKnownGoodModel`へフォールバックして比較すると、
  前回の良い結果と一致して「問題なし」と誤判定し、届いたエラー状態を見逃す。

### 付随する脆さの是正

- `AwaitingOwnWriteKey`は**対象Sceneが載っているイベントでだけ**消費する。以前は無条件に消費しており、
  本当の着地が外部変更として扱われて`FollowSilently`→`ResetTo`でウォーターマークが飛んだ。
- `RebaseDocument`を**全か無か**にした。`ResetTo`をスキップしつつ`TextSyncStates.Remove`だけ実行する
  半端な縮退で、履歴は残るのに同期状態だけ消え、次のCtrl+Zが誤って案内を出していた。
- `TextSyncStates`の未登録既定を`SceneTextSyncPolicy.Empty`にした
  (`TryGetValue`のout既定`BufferVersion=0`は実在の版番号と偶然一致しうる)。

## 不具合2: リネーム中にツリーが作り直されていた

`Populate`は`SceneNodeViewModel.BuildWithSelection`でビューモデルを新規生成して`ItemsSource`を
差し替えるため、リネーム中のノードごと破棄され、Enterを受けるTextBoxが消える。
門番の`SceneHubPublicationPolicy`はDocumentがdirtyのときしか止めないが、
**リネーム確定前はまだ編集していないのでdirtyではなく素通りする**。

回帰の理由はStep 1-6 Stage A。**以前は「解析が来ない/いつまでも遅延する」欠陥が偶然
リネームを守っていた**。Stage Aは実バグの修正なので取り消さない —
「再解析は頻繁に来る」が正しい前提であり、Hierarchy側が耐えるべき。

新規`HierarchyPopulatePolicy`で2段構えにした:

- **Skip(冪等化)**: 内容署名(新規`HierarchyContentSignature`)が同じなら差し替えない。
  これでリネームに限らず**全ての一時状態**(ドラッグ中の掴み・選択・展開)が守られ、
  M0046のフォーカス強奪の温床も断てる。署名は「Scene未選択(null)」と「Rootが無い」を区別し、
  アイコン解決に効く`TypeSymbol is null`の有無も混ぜる。
- **Defer(保留)**: リネーム中は内容が変わっていても差し替えない。
  **ドラッグ中には掛けない** — `DoDragDrop`はモーダルループを回していつ終わるか読めない。
  ドラッグはSkip側で守れる。

保留の解除で流し込むのは**保留時のModelではなく、そのときHubが持つ最新**。保留時のModelを流すと
リネーム確定で作られた新しいDocumentモデルを上書きする(Step 1-4で直した症状の再来)。
解除は`CommitRename`の最後、`RequestHierarchyEdit`のあとに行う(`CommitRename`は先頭で
`CancelRename`を呼ぶため、順序を誤ると編集要求より前に解除が走る)。
保留中に対象ノードが消えていたら、確定しても`TryGetId`が失敗するので先に取り消して反映する。

## 再発防止

**2件とも「純粋関数のテストは通るが、引数の作り方が誤っている」型**だった。不具合1は
`Decide(true,false)→Rebase`という判定自体は正しく、第2引数を実機でどう作るかがテストの外に
あったため、ポリシーのユニットテストでは永久に捕まらない。

- `SceneLandingDecisionTests`は実Compilationから解析→Documentで編集→書き戻し→再解析まで通し、
  **判定材料の生成ごと**検証する。`GizmoMove_LandsAsKeepDocument`が本命。
  旧実装がこのケースで`ComponentChanged`(Transform)を1件返して`Rebase`に落ちることは、
  使い捨てのプローブで**実測して確認した**(テストに歯があることの裏取り)。
- `SceneLineageArchitectureTests`は実装が元の形へ逆戻りしないことを固定する
  (`SceneLandingDecision`が`SceneDiffEngine`に依存しない、セッション層で予測投影を第1引数にした
  `Compare`を使わない)。**`Compare`自体は禁止しない** — Renderer差分と実投影同士の比較は本来の用途。
- 不具合2は`Populate`にテストが1本も無く、判定がUIコードに埋まっていた
  (規約「判定は純粋関数の`*Policy`へ」の未適用箇所)。加えてStage Aの変更時に
  「解析が届く頻度が上がる」副作用として**UIの一時状態が破壊される**ことを影響範囲に挙げていなかった。

## テスト

全1216件成功(VSExtension 714件、Step 1-7で新規・書き換え28件)。

## 実機確認

1. ギズモ移動 → Ctrl+S → Ctrl+Z で**C#ソースも戻りタブに`*`が付く**こと(不具合1の本体)
2. さらにCtrl+Zを連打して、保存前の複数操作まで1回ずつ戻れること
3. Ctrl+Yで戻した分が進み、`*`が消えること
4. Hierarchyでリネーム → Enter で**確定する**こと(不具合2)
5. **F2 → 入力 → 1秒待つ → Enter** でも確定すること(再解析を挟んでも生き残る)
6. Hierarchyの削除・親子変更・ドラッグが従来どおり動くこと(Skip/Deferの副作用確認)
7. C#に構文エラーを入れた状態でCtrl+Z → 履歴と同期状態が壊れないこと(`Defer`の確認)
8. 同名同構成の兄弟を持つSceneで、保存 → 編集 → 保存が失敗しないこと(`Rebase`縮退の確認)

---

# Step 1-8(追補): 解析が「解析中」に張り付いてSceneを開けなくなる不具合

実機報告「シーンが開けなくなった」。右クリックメニューの
「シーン デザイナーで開く**(シーンを解析中)**」がグレーアウトしたままで、Hierarchyも空。
**VS再起動で直る**。原因はStep 1-6 Stage Aで入れたデバウンス上限のバグで、蓄積して壊れる型。

## 主因: デバウンス状態のライブロック

`FirstPendingAnalysisAt`を`null`へ戻すのは**`StartAnalysis`の成功経路の中だけ**だった。
その手前に3つのガード(`Disposed` / トークン取消 / `AnalysisGeneration.IsCurrent`不一致)があり、
`CancelPendingAnalysisNoLock`も`catch`節もこのフィールドに触れない。
`AnalysisGeneration`は`ScheduleAnalysis`が毎回インクリメントするので追い越された解析は必ず落ちる。

したがって**解析が一度も完走しないまま上限(750ms)を超えると、以後ずっと待ち時間が0**になり、
workspace変更のたびに「即発火 → 次の変更で即キャンセル」を繰り返して**デバウンスが消滅**する。
復帰条件は「解析1回分より長い静穏区間」だけだった。

**引き金は`ScheduleAnalysisNow`自身**。`Immediate:true`でも保留窓をセットする(クリアしない)。
呼び出し元(Ctrl+Z/Yのテキスト同期、保存の書き戻し)はどちらも直前にlive bufferを書き換えて
いるため`DocumentChanged`が確実に後続し、その即時解析はキャンセルされやすい。
**自分の書き込みが罠を仕掛けていた。**

症状も構造的に一致する。Hierarchyを更新する`ModelChanged`も解析の成功経路末尾でしか発火しないため、
「一度も完走しない」なら**メニューとHierarchyが同時に死ぬ**のが必然。

### 修正: 窓を閉じるのは「完走」ではなく「開始」

状態遷移を`SceneAnalysisDebouncePolicy.OnScheduled`/`OnStarted`として純粋関数にした。
上限が守る区間は「最初のスケジュール → タイマ発火」だけで、発火した瞬間その待ちは終わる。
追い越しは*新しい*窓の開始であり、その窓もまた上限で強制発火する。これで
**「上限ごとに1回は必ず解析が始まる」が繰り返し成立**し、一度きりで壊れて0msへ縮退しない。

`OnStarted`は`RunAsync`のラムダ内ではなく**同期部分**で呼ぶ(ラムダに入れるとスケジューリング
遅延の分だけクリアが遅れ、その間の`ScheduleAnalysis`が誤って「保留中」と判断する)。

時刻源を`DateTime.UtcNow`から**`Stopwatch.Elapsed`(単調)**へ変えた。NTP/DST補正で経過が
負になると上限判定が壊れるため。純粋関数側でも負をゼロへクランプする。

## 副因: Stage Aの解析対象拡張を撤回

`AppendPinnedSceneModelsAsync`はキャンセル判定・UIスレッド遷移・lockの**すべて手前**に置かれ、
フル解析1回分を挿入して**キャンセル可能区間をほぼ倍**にしていた。しかも
**その結果はメニューに一切使われない**(メニューはアクティブ文書側のスナップショットのみ)のに、
メニュー更新の直列ブロッカーになっていた。
「待ち時間0(コアレス消失)× 解析長2倍」の積が「どの解析も完走しない」を作った。

**メニュー更新の後ろへ動かす案ではなく撤回を選んだ**。追加解析の第2フェーズは
`AnalysisGeneration.IsCurrent`に守られるが`ScheduleAnalysis`は毎回generationを進めるので、
**この拡張が最も必要な状況(背景ビルドで`ProjectChanged`が多発している間)でほぼ届かなくなる**。
コストと状態を残したまま効果だけが目減りする。撤回はキャンセル可能区間を無条件に半減させ、
**コードを減らすrevert形の変更**になる。

**撤回で失うもの**(pin中Sceneの`.cs`がアクティブでない間に限る): リネージ検証が走らず
`AwaitingOwnWriteKey`が残留する、外部変更検知が遅延する。
**失わないもの**: 表示・履歴・ウォーターマーク(Step 1-6/1-7のメモリ内スタックが即時更新する)。
**保存の正しさも失わない** — `SceneSaveVerification.Verify`は書き込み**前**に完了している。
これはStage A以前に出荷していた挙動そのもので、Step 1-7の`ShouldConsumeAwaitingKey`は
着地待ちキーの残留を想定済み。

あわせて、`ScheduleAnalysis`が`Unavailable`を代入した直後に無条件で`Analyzing`を再代入して
潰していた箇所を`PendingMenuState`へ切り出して直した。

## 再発防止

**直近4回の不具合はすべて同じ型**: 純粋関数(`ResolveDelay`)は正しく、そのユニットテストも
当時すべて通っていたのに、**それへ渡す状態の管理側が誤っていた**。Step 1-7で結合テストと
アーキテクチャテストを入れたが、この状態遷移自体を純粋関数にしていなかったので素通りした。

対策は「判定だけでなく**状態遷移**も純粋関数にし、**遷移列をシミュレートする**テストを書く」こと。
`Debounce_DoesNotDegenerateToZeroWhenEveryAnalysisIsSuperseded`が本命で、100ms間隔・10秒の
変更列に対し、即発火が上限ごとに最大1回(14回以下)に収まることを表明する。
**旧意味論では100回中92回が即発火することを、使い捨てのプローブで実測して確認した。**
単発の入出力を見るテストでは決して捕まえられない型であることの裏取りでもある。

アーキテクチャテストで「状態をその場で組み立てない」「`OnStarted`が残っている」ことも固定した。

## テスト

全1220件成功(VSExtension 718件)。

## 実機確認

1. `.cs`を右クリック →「シーン デザイナーで開く」が**有効**で、Scene Designerが開くこと
2. しばらくタイピングした後・ビルドした後でも、右クリックメニューが有効なままであること
3. Hierarchyに木が表示され続けること
4. **Scene Designerを開いた状態で長時間作業しても開けなくならないこと**(本Stepの主目的)
5. ギズモ移動 → Ctrl+S → Ctrl+Z でC#も戻り`*`が付くこと(Step 1-7の確認項目、未確認のまま)
6. Hierarchyでリネーム → Enter で確定すること(Step 1-7の確認項目、未確認のまま)
7. Hierarchyの削除・親子変更・ドラッグが従来どおり動くこと

**もし再発したら**: ライブロック筋書きなら**ActivityLogは空**になる(OperationCanceledExceptionは
ログされない)。逆に`SceneWorkspaceService`のエラーが出ていれば別原因なので、まずそこを見る。

## 今回やらなかったこと(意図的)

- **pin中Scene専用の独立解析スロット**(SolutionExplorer方式)。正しい終着点だが、
  新しい状態・generation gate・CTSの追加は**まさに直近4回の回帰を生んだ形**。
  実害が具体的に観測されてから別ステップで。
- **`OnWorkspaceChanged`の対象フィルタ**。今は`Args.DocumentId`/`ProjectId`を見ておらず、
  無関係プロジェクトの`ProjectChanged`でも張り直す(ライブロックの「燃料」)。
  参照プロジェクト経由の意味変化を取りこぼす判断が必要で、独立した検証が要る。
- **メニューの安全網**(`Analyzing`でもコマンドを有効化)。`Analyzing`中は`Snapshot.Items`が
  空なので、複数Scene時の選択UIを起動後に持つ設計変更が要る。主因が直る以上コストに見合わない。
