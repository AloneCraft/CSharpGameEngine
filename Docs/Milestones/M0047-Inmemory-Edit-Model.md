# M0047: メモリ内編集モデルを正本にする(きびきび化)

## 目的

「UE5/Unity/Blenderのようにきびきび動かして。C#ソースを正とする考え方も辞めてよい」という
アーキテクチャ再考の要請を受けた。M0038(予測レイヤー)は編集ループにRoslyn往復
(250msデバウンス+全解析+全UI再構築)が残ったままの中途半端な対処であり、実機で
「まだ遅い」「Ctrl+Zを連打すると詰まる」「構造編集がCtrl+Zで戻せない」と報告された。

## 決定事項(ユーザー承認済み)

- 編集セッション中の正本を**メモリ内`SceneModel`**へ移す。C#ソースへは**保存操作でのみ**書き込む
- 将来的な保存形式のJSON化(System.Text.Json)は次段。今回はC#ソースへの保存を維持
- 外部編集(コードエディタでの直接書き換え)は、未保存編集が無ければ自動再読み込み、あれば確認
- 優先順位: エディタの即応性を先に出す

## アーキテクチャ

```
Before: ギズモ操作 → C#書き戻し → 250msデバウンス → Roslyn再解析 → SceneModel再構築 → UI全再構築
        Undo = テキストUndo → 同じ往復をもう一周(連打すると詰まる)

After:  ギズモ操作 → SceneEditDocument(メモリ内)を同期更新 → 即描画
        Undo = スタックをpop → 即描画(無制限・即時)
        保存 = セッション全履歴を実C#構文へ一度だけ再生 → 1回のVS Undoで書き戻し
```

### 中核: `SceneEditDocument`(新規)

`SceneModel`が完全にimmutableであることを使い、Undo/Redoは差分やインバース操作を持たず
`(Model,Projection)`スナップショットの列で成立する。`IsDirty`は保存時点モデルとの**参照比較**で
判定する(immutableなので、Undo/Redoでちょうど保存時点まで戻れば自動的にdirtyでなくなる)。

### `SceneModelEditApplier`の拡張(構造操作)

M0038時点は値編集(`SetTransform`/`SetComponentMember`)しか予測できなかった。今回
`AddNode`/`RenameNode`/`RemoveNode`/`ReparentNode`/`ReorderNode`/`DuplicateNode`を追加した。

`NodePath`はRootからの子インデックス列なので、構造操作は影響を受けた親の子リスト以下を
再計算する必要がある。同一性(`EditorNodeId`)は`SceneProjectionReconciler`が担うが、
**親を跨ぐReparentでは同一性が失われる**ことが実装中に判明した — Reconcilerは「同じ親の
兄弟間」でしか一致を探さない設計(ドキュメント記載どおり)であり、移動後の子は新しい親の下に
旧兄弟が無いため別IDを振ってしまう。専用の補正パス(`PreserveIdentityAfterMove`)で、
移動した部分木の旧Path→新Pathを並走させて対応するIDを差し戻すことで解決した。

### 保存: `SceneSourceEditor.ApplyForSave`(新規)

既存の`ApplyBatch`は、全操作の対象Nodeが**呼び出し時点の投影に既に存在すること**を要求する
(呼び出し冒頭で全操作のNodeIdを検証してから処理する設計)。これはセッション中に追加した
Nodeを後続の操作(Rename/Reparent等)が参照する一般的な流れ(例: Addしてすぐ名前を変える)に
対応できない。

`ApplyForSave`はDocumentの全履歴(`SceneUndoEntry`列)を順に1件ずつ再生し、`AddNodeOperation`を
処理するたびに、実際に作られた実Syntaxを検出してDocumentが使った**同じEditorNodeId**のまま
Annotation登録する(新しいIDを発行しない — Document側のReconcilerが払い出したIDと食い違うと、
後続の操作がそのIDを解決できなくなるため)。実在するNodeは、最初の構造編集がテキストをずらす
前に全て事前Annotateしておく(`ApplyBatch`自身の事前Annotateパスと同じ理由・同じ対策)。

### 副産物: `CreateNodeCreation`の潜在バグを発見・修正

`ApplyForSave`で「追加→再解析」の往復を初めてテストしたところ、`new Node{...}`が
`newNode{...}`(トリビア無しで1トークンに読める)として出力され、再解析すると追加した
Nodeが消えることが判明した。既存の`AddNode`テストは`Contains("Name=\"Node\"")`しか
確認しておらず、再解析までは検証していなかったため気付けなかった。`NormalizeWhitespace()`を
追加して修正し、既存テストも再解析まで確認するよう強化した。

## 変更したファイル

| ファイル | 変更 |
|---|---|
| `SceneModelEditApplier.cs` | 構造操作6種を追加。Reparent後のID同一性補正 |
| `SceneEditDocument.cs`(新規) | セッション中の正本。Undo/Redo・IsDirty |
| `SceneSourceEditor.cs` | `ApplyForSave`(新規)。`CreateNodeCreation`のtrivia修正 |
| `SceneEditorSessionService.cs` | `Apply`(同期)・`SaveAsync`・`TryUndo`/`TryRedo`(Document経由)・外部変更検知 |
| `SceneDesignerToolWindowControl.xaml.cs` | 非同期コミットチェーン(`PendingCommitWrite`/`CommitEditAsync`)を撤去。Ctrl+S配線・外部変更ダイアログ |
| `InspectorToolWindowControl.xaml.cs` | `CommitAsync`/`CommitInProgress`を撤去、同期`Session.Apply`へ |
| `CSharpGameEngineViewerPackage.cs` | Hierarchy編集をpin中Documentの投影基準・同期`Session.Apply`へ |
| `SceneHwndHost.cs` | Ctrl+S検知(M0045と同じ`GetKeyState`ポーリング経路)を追加 |
| `NativeMethods.cs` | `VK_S`追加 |

## 撤去した仕組み(もう不要)

- `SceneEditorSessionService.CommitAsync`/`TryPublishPrediction`/`RefreshPinnedSnapshotFromSourceAsync`
  (M0038〜M0046の予測配信+非同期直列コミットチェーン)
- `SceneDesignerToolWindowControl`の`PendingCommitWrite`直列化・`CommitEditAsync`
- `SceneHwndHost.TryFastUndo`/`TryFastRedo`の呼び出し(Document.TryUndo/Redoが同期即時配信するため、
  M0036由来の先読みプレビューは不要になった。メソッド自体は削除せず未使用のまま残している)

`SceneEditConflictMerger`は呼び出し元が無くなったが、既存テスト(`SceneEditConflictMergerTests.cs`)
と併せて未削除(次段の掃除対象)。

## 既知の未対応・制限事項

1. **保存トリガーはビューポートのCtrl+Sのみ**。コードエディタ側でCtrl+Sを押した場合の自動フラッシュ
   (`IVsRunningDocTableEvents`のOnBeforeSaveフック)は未実装 — VS標準のCtrl+SはScene Designerの
   未保存編集を含まずにC#ファイルを保存してしまう。今回はリスクの高いRDT統合を避け、確実な
   手動トリガーを優先した
2. **未保存インジケータ(タイトルの`*`等)は未実装**。`Session.IsDirty(Key)`は用意したが、UI表示は
   次段
3. **Duplicate(Alt+ドラッグ複製)で作った同一セッション内のNodeを、保存前にRename/Reparentすると
   保存に失敗する**可能性がある(`ApplyForSave`の新規Node登録は`AddNodeOperation`だけを見ており、
   `DuplicateNodeOperation`が作った新規Nodeは登録しない)。AddNodeの場合と異なりDuplicateされた
   Nodeを複製直後に構造編集する操作は考えにくいため許容したが、要実機確認
4. `SceneDiffEngine`はNode名の変化(Rename)を検出しない(既存の制限、変更なし) — レンダラーの
   増分更新判定には現状影響しない

## Evidence

- VSExtension.Tests 596/596(既存568+新規28、リグレッションなし)。契約テストとして
  `SceneModelEditApplierTests`(構造操作・Reparent同一性・Duplicate)・`SceneEditDocumentTests`
  (Undo/Redo・IsDirty)・`SceneSourceEditorApplyForSaveTests`(Add→Rename/Reparent同一セッション内
  参照の契約)を追加
- ソリューション全体: 全プロジェクトでリグレッション無し
- 実機検証は未実施(下記参照)

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | ギズモドラッグ確定が即座に反映される(待ちが体感で消えている) — 今回の主目的 |
| 2 | Ctrl+Zを連打しても遅延・詰まりが起きず、押した回数だけ即座に戻る |
| 3 | ヒエラルキーの追加・改名・削除・親変更・並べ替えがCtrl+Zで戻せる |
| 4 | Ctrl+Yで対称にRedoできる |
| 5 | ビューポートにフォーカスがある状態でCtrl+SするとC#ソースへ反映され、ステータスバーに結果が出る |
| 6 | 保存後のC#の内容(手書きコード・コメント・書式)が壊れていないこと。特に新規追加したNodeの書式(NormalizeWhitespaceにより周囲と異なる整形になる) |
| 7 | 未保存のままコードエディタでC#を直接書き換えると確認ダイアログが出て、Yesで破棄・再読み込みされること |
| 8 | Alt+ドラッグ複製がこれまでどおり動くこと(Duplicateの構造操作対応) |
| 9 | コードエディタにフォーカスがある時のCtrl+Zは従来どおりテキストUndoであること(回帰) |
| 10 | 複数Scene・複数回のpin切り替えでDocumentが正しく分離されること |
