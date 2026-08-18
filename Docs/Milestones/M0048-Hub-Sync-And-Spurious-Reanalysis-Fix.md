# M0048: M0047配線の実機退行を2件修正する

## 目的

M0047配線を実機投入した結果:

- ヒエラルキーの改名・削除・親変更・並べ替えができない(シーンビューは消えるがノードは
  消えない、等)
- ギズモ移動→Undo→Ctrl+Yで**Redoできない**
- ビューポートでCtrl+Sしても保存されず、**C#ソースから再読み込みされる**(編集が消える)

ユーザーからは「C#への反映を直すより、JSON保存へ進むほうがよい」との方針も示されたが、
今回発見した2件はいずれも**保存形式(C# / JSON)とは無関係な、セッション中の編集そのものの
不具合**であり、JSON化しても残るため先に修正した。

## 根本原因1: Hierarchyが`SceneEditDocument`の更新を一切知らない

`HierarchyToolWindowControl`は`SceneEditorHub.SceneModelChanged`/`ActiveSceneModel`を表示に使う。
このHubは`SceneWorkspace.ModelChanged+=SceneEditorHub.PublishSceneModel`(Package初期化時に配線)
— つまり**実Roslyn再解析だけ**で更新される。M0047で編集ループから毎回の再解析を無くした結果、
Hierarchyの表示は最初にpinした瞬間の内容のまま固まった。

一方Designerビューポートは`Session.PinnedSnapshotChanged`(Documentの更新)を直接購読しており
正しく更新される。ここに表示のずれが生まれ、「シーンビューは消えるがヒエラルキーのノードは
消えない」という報告と一致する。Hierarchyからの改名・削除・親変更・並べ替えは、この古い表示の
`NodePath`を`Session.PinnedSnapshot.Projection`(Documentの最新投影)へ照会するため、
既に構造が変わっていれば解決に失敗していた。

**修正**: `CSharpGameEngineViewerPackage`が`Session.PinnedSnapshotChanged`を購読し
(`OnPinnedSnapshotChangedForHub`)、`SceneEditorHub.PublishSceneModel(Snapshot.CurrentModel)`を
呼んで、Documentの更新をHub経由でHierarchy(および同じHubを見る`OnSceneModelChangedFor
SourceMarkers`)へ伝播するようにした。発火はギズモ確定・Hierarchy操作・Undo/Redoのたびに1回
(ドラッグ中の毎フレームではない)なので、Hierarchyの全木再構築コストは許容範囲。

## 根本原因2: 無関係な再解析がUndo/Redo履歴を黙って消す

`SceneEditorSessionService.OnWorkspaceModelsChanged`は`!Document.IsDirty`を「失うものが無い」の
代理指標として使い、trueなら`Document.ResetTo(...)`(UndoStack/RedoStackを丸ごとClear)を
無条件に呼んでいた。

しかし**`IsDirty`は「保存時点のモデルと今のモデルが違うか」の指標であり、「Undo/Redo履歴が
空かどうか」ではない**。ギズモ移動→Undoで保存時点まで戻ると、その時点で`IsDirty=false`になるが
**RedoStackには戻せる履歴がまだ残っている**。ここでVSの背景解析が(実際のテキスト変更とは無関係に)
たまたま発火すると、`!IsDirty`だけを根拠に`ResetTo`が呼ばれ、Redo履歴が消えていた。これが
「Undo後にRedoできない」の直接原因と特定した。

「Ctrl+Sで再読み込みされる」も同じ設計の裏返しで説明できる: 未保存編集がある間に無関係な
再解析が発火すると`ExternalChangeDetected`が発火し確認ダイアログが出る。実際には何も外部編集
されていないのに紛らわしいダイアログが出ていた可能性が高い。

**根本の設計不備**: 再解析イベントが発火した事実だけを見ており、**実際に対象Sceneの内容が
変わったかどうかを一切確認していなかった**。VSの背景解析は無関係な理由でも頻繁に発火しうる。

**修正**: 再解析の直前と直後で「実Projectionを比較して本当に変わったか」を
`SceneDiffEngine.Compare(...).IsEmpty`で判定し、変わっていなければDocumentに一切触れず無視する
ようにした。判定を純粋関数`SceneExternalChangePolicy.Evaluate`へ切り出した:

```csharp
internal enum SceneExternalChangeAction { Ignore, FollowSilently, AskUser, TrustOwnSave }
```

`OnWorkspaceModelsChanged`は`Publish()`を呼ぶ**前**に直前の実投影を退避し、`Publish()`後の
新しい実投影と`SceneDiffEngine.Compare`で比較して`RealContentActuallyChanged`を求める。
`Ignore`ならDocument/Undo/Redoスタックへ一切触れない。

## 変更したファイル

| ファイル | 変更 |
|---|---|
| `SceneExternalChangePolicy.cs`(新規) | 上記の純粋関数 |
| `SceneEditorSessionService.cs` | `OnWorkspaceModelsChanged`を実投影diffベースへ書き換え |
| `CSharpGameEngineViewerPackage.cs` | `Session.PinnedSnapshotChanged`を購読し`SceneEditorHub.PublishSceneModel`へ橋渡し |

## 変更しないもの

- `SceneEditDocument`のUndo/Redoスタック実装自体(単体テストで正しさ確認済み、無罪と判明)
- `SceneModelEditApplier`/`SceneSourceEditor.ApplyForSave`(M0047基盤、今回の2件とは無関係)
- 保存形式(C#のまま)。JSON化はこの2件の実機確認後に着手する

## Evidence

`SceneExternalChangePolicyTests.cs`(新規、5件): own-save優先・内容不変は常にIgnore(2パターン)・
内容変化+未保存無しはFollowSilently・内容変化+未保存有りはAskUser。

VSExtension.Tests 601/601(既存596+新規5、リグレッションなし)。ソリューション全体で
リグレッション無し。

`SceneEditorSessionService.OnWorkspaceModelsChanged`本体・Hierarchy⇔Hub配線はVS SDK依存の
ため既存方針どおりユニットテスト対象外。実機確認が必須。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | ヒエラルキーでの改名・削除・親変更(ドラッグ&ドロップ)・並べ替えが正しく反映され、ビューポート・ヒエラルキー両方が同じ内容を示すこと |
| 2 | ギズモで移動→Ctrl+Zで戻す→Ctrl+Yで**Redoできる**こと(複数回) |
| 3 | ビューポートでCtrl+Sすると、想定外の「外部で変更されました」ダイアログが出ずにC#へ正しく保存されること |
| 4 | 何もしていない状態でしばらく待っても(背景解析が起きても)編集内容・Undo/Redo履歴が消えないこと |
