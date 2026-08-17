# M0038: 編集レイテンシの根本解消(予測モデル層)

## 目的

実機で「まだ編集の速度が遅い」と報告された。[[M0030-SceneView-Gizmo-Transform-Editing]]で
コミット直後の即時パッチ、M0036で先読みUndo/Redo、M0037でCtrl+Zの到達性と、3回にわたって
高速化を重ねてきたが、**いずれも見た目の巻き戻りを隠していただけ**で、待ち時間そのものは
一切短縮していなかった。指摘は正確である。

ユーザーからは「初期表示時にソースコードから編集用データ構造を構築し、シーンビューではその
データ構造を編集し、保存したときだけソースへ反映する」というアーキテクチャ変更が提案された。
調査の上で**今回は土台のみ**(メモリ上のモデルへ即時適用して画面を即更新する層)を作り、
実機で体感を測ってから保存方式へ進むか判断する方針で合意した。ADR-0040の
「C#ソースが唯一の正本」は維持する。

## 実測した遅さの原因

ドラッグ中は速く(純粋な行列計算のみ、I/O無し)、**手を離した瞬間に遅い**。

| # | 原因 | 場所 |
|---|---|---|
| 1 | **コミット完了まで次の操作がブロックされる**(最大の体感要因) | `EditTransactionController`が`Committing`のまま、`CompleteEditCommit`まで`Idle`へ戻らない |
| 2 | **プロジェクト全体のコンパイル取得が2回** | `SceneWorkspaceService.ResolveSceneEditContextAsync`(コミット前)と`AnalyzeAllAsync`(再解析)。Source Generatorも都度再実行 |
| 3 | 250msの固定デバウンス | `SceneWorkspaceService.DebounceDelay` |
| 4 | ファイル全文の文字列化 | `SceneEditorSessionService`の`Edited.Root.ToFullString()` |
| 5 | 全型の線形走査(キャッシュ無し、2回) | `Compilation.Assembly.GlobalNamespace` |

glTF再読込は`MeshLoader`のプロセス寿命キャッシュで既に回避済みで**原因ではない**。GPUリソース
全再生成(D3D12デバイス+HLSL実行時コンパイル+全頂点バッファ再アップロード)はTransform編集では
発火しない(増分パスに乗る)が、構造編集では発火する。

## 実装したこと

### 中核: 予測は「表示専用の先読み」であり、正本はソースのまま

M0030/M0036で既に3回使われている idiom(どこにも書き込まない表示専用の先読み)を**モデル層まで
引き上げた**。ソース書き戻しも再解析も今までどおり走る。違いは「再解析の結果を待たずに、
編集後のモデルを自前で計算して先に配る」点だけ。

- **予測が当たれば** 再解析時のdiffが実質no-opになり、**再解析がそのまま予測の自動検証になる**。
- **予測が外れた/できなかった場合** 再解析が真実で上書きするので自己修復する。実害は
  「今日と同じ速度で反映される」だけ。
- そのため**全操作種別を対応する必要が無い**。扱えない操作は素直に諦めて従来経路へ落とす。

### 1. `SceneModelEditApplier`(新規・純粋static)

`TryApply(Base, Projection, Operations, out Predicted)`。immutableな`SceneModel`ツリーへ操作を
関数的に適用する。**1つでも扱えない操作があれば`false`を返して丸ごと諦める**(部分適用の禁止 —
中途半端な予測はHierarchy表示とソースの食い違いを生むため)。

| 操作 | 出所 | 対応 |
|---|---|---|
| `SetTransformOperation` | ギズモのMove/Rotate/Scale/SurfaceSnap | 完全対応 |
| `SetComponentMemberOperation` | Inspector・プロパティギズモ | リテラル式のみ(定数式はセマンティックモデルが要るため辞退) |
| 構造編集(Add/Remove/Reparent/Reorder/Rename/Duplicate/Component増減) | Hierarchy等 | **今回は非対応**。従来どおりの往復になる |

Transformのメンバー語彙・既定値・数値フォーマット・「値が変わらないメンバーは書かない」判定・
`.g.cs`除外は`SceneSourceEditor`側を`internal`へ上げて**共有**し、二重定義を最小化した。

### 2. 予測スナップショットの発行

`SceneEditorSessionService.TryPublishPrediction(Request)`。`SceneDocumentSnapshot`が
Renderer/Hierarchy/Inspector/ソースマーカーの**唯一の配信点**なので、予測モデルの snapshot を
1回 publish するだけでUI全体が下流無改造で即時更新される。

**予測は解析結果の履歴(`Snapshots[Key]`)には入れない。** あれは`SceneEditConflictMerger`と
次回diffの基準になる「実際に解析された事実」の列であり、予測を混ぜると書き戻しの競合解決が壊れる。
配るのは購読者向けの表示用snapshotだけ。`EditorNodeId`のID空間は実解析と共有する(別インスタンスに
すると同じNodeへ別IDが振られうる)。

### 3. 次の操作をブロックしない

`OnEditCommitRequested`が予測を配信できたら、その場で`CompleteEditCommit(true,null)`を呼んで
トランザクションを`Idle`へ戻す。ソース書き戻しは背後で継続する。

書き戻しは**直列**を維持する(`PendingCommitWrite`のチェーン)。並行させると後続コミットが前の
コミットを含まない古いソースを基準にリベースし、直前の編集を打ち消しかねない。

書き戻しが失敗した場合はソースが変わっていないので、次の再解析が真実(=編集前の状態)を配信して
予測を自動的に打ち消す。既存の`CommitAsync`の2回リトライと`SceneTextBufferApplyGuard`の全文一致
チェックがそのまま効く。ユーザーへは`ReportCommitFailure`とステータスバーで通知する。

### 4. 書き戻し中のUndoを抑止(この変更で新たに生じた危険への対処)

予測で編集がローカル確定してから書き戻しが landするまでの窓でCtrl+Zを通すと、テキストUndo
(まだ今回の編集が積まれていない履歴を1つ戻す)と先読みUndo(今回の編集を表示上戻す)が食い違い、
**ソースと表示がずれて壊れる**。`SceneDesignerUndoCommandPolicy.CanExecute`を新設して飛行中は
実行させない。

`Supported`(=`ShouldHandle`)は落とさない。落とすとCtrl+Zがシェルの通常ルーティングへ戻り、
**別のドキュメントのUndoが走ってしまう**ため、`Enabled`だけを落として無効化する。

### やらなかったこと: 重複コンパイルの排除

計画では任意項目として`ResolveSceneEditContextAsync`のコンパイル再取得省略を挙げていたが、
**実装しないと判断した**。ここは`Model`/`Root`/`Text`を同一世代で取るためのもので、`Text`は
`SceneTextBufferApplyGuard`がライブバッファと全文比較する基準になっている。キャッシュすると
`Text`が古くなりガード不一致→リトライ増加で**かえって遅くなる**。UIはもう予測で更新済みなので
体感への利得も無い。

250msデバウンスも変更していない。UIが予測で先に正しくなるため再解析の遅延は体感に影響せず、
外部編集の取り込み挙動を変えるリスクを負う理由が無い。

## Evidence

新規`SceneModelEditApplierTests.cs`(12件)と`SceneDesignerUndoCommandPolicyTests.cs`追補(3件)、
計17件を追加。ソリューション全体**1037/1037**(既存1020、リグレッションなし)。

最重要は**契約テスト**`ModelApply_MatchesSourceApplyThenReanalyze`。モデル層とソース層で編集
セマンティクスが二重定義になるため、「モデルへ適用した結果」と「ソースを書き換えて再解析した結果」が
一致することを`SceneDiffEngine.Compare`で固定した。componentの`EmitExpressionText`だけは予測では
作り直せない(解析層のemit規則の再実装になる)ため`ComponentChanged`のみ許容し、それ以外の差分が
出ないことと、実際に描画・Inspectorが読む値そのものの一致を要求している。

併せて`PredictedDiff_StaysOnTheIncrementalRenderPath`で、予測snapshotのdiffが増分更新へ分類される
ことを固定した。ここがRebuildへ落ちると予測のたびにD3D12デバイス再生成が走り、高速化どころか
今より遅くなる。`PredictedProjection_KeepsStableNodeIds`は、予測後に発行する編集操作が別Nodeを
指してソースを壊さないことを守る。

`TryPublishPrediction`・コミット経路の配線・書き戻しの直列化はVS SDK/HWND依存のためユニット
テスト対象外(既存方針を踏襲)。**体感速度は実機でしか測れない。**

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | ギズモで移動して手を離した直後、**待たずに次の操作を開始できる** |
| 2 | 手を離した瞬間に3Dビュー・Hierarchy・Inspectorの値が即座に一致する |
| 3 | Inspectorで数値を打ち込んだときも即座に3Dビューへ反映される |
| 4 | しばらく操作した後、C#ソースが正しく全編集を反映している(予測と真実が一致している) |
| 5 | 編集を高速に連打しても表示が壊れない・ソースが壊れない |
| 6 | Hierarchy編集(追加/削除/Reparent)は従来どおりの速度(今回は対象外、劣化していないことの確認) |
| 7 | Ctrl+Z/YがM0037どおり動き続けている(回帰確認) |
| 8 | 編集直後の連打Ctrl+Zでソースと表示がずれない(書き戻し中は無効化される) |
| 9 | コードエディタでソースを直接編集したときも従来どおり反映される(回帰確認) |
| 10 | コードエディタにフォーカスがある通常のCtrl+Zが今までどおり動く(M0037からの継続回帰確認) |

## 次にやること(決定済み・未着手)

- **保存方式への移行**(編集を保存まで貯める)。土台の体感を実機で測ってから判断する。
  進める場合の決定事項は確定済み: 未保存の保護は**Ctrl+S参加 + 未保存表示 + 閉じる時確認**、
  外部ソース変更は**既存`SceneEditConflictMerger`で自動リベース**。
  必要な新規実装: dirty状態管理(現状コード皆無)、ツールウィンドウのVS保存参加
  (`IVsPersistDocData`相当、現状皆無)、`VSStd97CmdID.SaveSelectedItems`登録(M0037の
  `OleMenuCommandService`登録パターンを流用可)、Undoの操作単位化。ADR-0040決定事項1を
  部分Supersedeする新ADRが必要。
- **構造編集の予測対応と高速化**。`Rebuild()`によるD3D12デバイス全再生成の回避が本体。
- **空クリックでの選択解除**。原因は「空だと思った場所が背景の`SM_SkySphere`に当たっていた」ことと
  判明済みで、選択解除の配線自体は正常。ピッキング対象からsky domeを除外する小さな修正。
