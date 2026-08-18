# M0045: Ctrl+Zの複数段階Undoを直す(標準コマンド登録の全廃)

## 目的

M0038/M0039で書き戻し自体は健全になったが、ユーザーから「1,2,3(スクロール位置・ちらつき・
ソース反映)はOKだが、Ctrl+Zで戻るのは1段階だけ。VSツールバーの「元に戻す」▼履歴も画像のように
登録されていない」と報告された。

## 切り分け(AskUserQuestion 2ラウンド)

1. C#ソースへの書き込みは3回連続で**正しく反映される**(書き込み自体は健全)
2. ツールバーの「元に戻す」▼ドロップダウンの履歴が**常に空**
3. コードエディタへフォーカスを移してCtrl+Zを連打しても**1段階も戻らない**
4. ギズモ移動直後、履歴ボタンが一瞬(0.5秒ほど)出てすぐ消える

証拠3が決定的だった。M0037の`SceneDesignerUndoCommandPolicy.ShouldHandle`はビューポートの
WPFフォーカスが無ければ`Supported=false`を返すはずで、コードエディタにフォーカスがある間は
このペインのコマンドは本来無関係になるはず。それでも症状が出るのは、**標準コマンドID
(`GUID_VSStandardCommandSet97`)をツールウィンドウの`OleMenuCommandService`へ再登録すること
自体が、ペインの表示・フォーカス状態に関わらずVS全体のUndo管理(ツールバー・ドロップダウン・
コードエディタのCtrl+Z)の接続を壊している**ことを示している。

加えて`MultiLevelUndo`の`BeforeQueryStatus`実装は`Supported`/`Enabled`の真偽値しか設定しておらず、
ドロップダウンの履歴文字列リストが要求する`OLECMDTEXT`を一切埋めていなかった — 証拠2の直接原因。

一方`SceneTextBufferWriter.TryApply`は標準的な`ITextUndoHistoryRegistry.RegisterHistory`→
`CreateTransaction(UndoDescription)`→`Buffer.Replace`→`Transaction.Complete()`という、
VSエディタの正規のUndo機構を使っており、ここ自体に問題は無かった(証拠1と整合)。
直すべきは**Ctrl+Zの「検知方法」**であって、Undo自体の実行経路ではないと判断した。

## 実装したこと

M0037の`SceneDesignerToolWindowPane.OnToolWindowCreated`が行っていた
`VSStd97CmdID.Undo`/`Redo`/`MultiLevelUndo`/`MultiLevelRedo`の登録を**全廃**した。

代わりに、WASDQEのFly移動やShift/Ctrl修飾検出が既に使っている
`CompositionTarget.Rendering`ティックごとの`NativeMethods.GetKeyState`ポーリング
(`SceneHwndHost.TickFlyIfNeeded`)と同じ経路でCtrl+Z/Ctrl+Yを検知する
(`SceneHwndHost.TickUndoRedoIfNeeded`、新規)。

`GetKeyState`はWM_KEYDOWN/UPメッセージの到達を必要とするが、VSのアクセラレータテーブルによる
「コマンドとしての消費」はメッセージ自体を破棄するわけではなく(同一UIスレッドがメッセージポンプ中に
読み取る)、届いた後の扱い(コマンド実行 or 通常ディスパッチ)を分岐させるだけなので、Ctrl+Zが
Edit.Undoとして消費されたかどうかに関わらず物理的な押下状態を正しく反映する。これにより
**標準コマンドテーブルへ一切触れずに**シーンビューでのCtrl+Z/Ctrl+Yを実現できる。

- `SceneUndoRedoKeyPolicy.IsRisingEdge(bool CurrentlyDown,bool WasDownLastTick)`(新規):
  押しっぱなしで毎フレーム発火しないための立ち上がりエッジ判定を切り出した純粋関数
- `SceneHwndHost`: `UndoRequested`/`RedoRequested`イベントと`IsViewportFocusedProvider`を追加、
  `TickUndoRedoIfNeeded`で毎フレームCtrl+Z/Ctrl+Yを検知。エッジ追跡自体はフォーカスと独立させ、
  フォーカスは発火の可否にのみ使う(ビューポート外で押しっぱなしのままフォーカスが戻ったときに
  継続押下を新規押下として誤検知しないため)
- `SceneDesignerToolWindowControl`: `Host.UndoRequested`/`RedoRequested`を既存の
  `TryUndoFromCommand`/`TryRedoFromCommand`へ配線(この2メソッドは内部で
  `SceneDesignerUndoCommandPolicy.CanExecute`を再チェックしているため無改造で流用できた)
- `NativeMethods`: `VK_Z`/`VK_Y`を追加
- `SceneDesignerToolWindowPane`: `OnToolWindowCreated`のオーバーライドと
  `RegisterUndoRedoCommand`を削除。今後同じ罠を踏まないよう、標準コマンドを登録しない設計判断を
  クラスコメントに残した

`Session.TryUndo()`/`Session.TryRedo()`/`Host.TryFastUndo()`/`Host.TryFastRedo()`/
`SceneDesignerUndoCommandPolicy`は無改造のまま流用した。

## Evidence

`SceneUndoRedoKeyPolicyTests.cs`(新規、4件): 押下→押下(継続、false)・非押下→押下(true)・
押下→非押下(false)・非押下→非押下(false)の4パターンを固定。

VSExtension.Tests 568/568(既存564+新規4、リグレッションなし)。

> **訂正(M0046で判明)**: M0045時点で報告した「564/564」は誤り。`dotnet test --no-build`を
> テストファイル作成**後**に実行したため、ビルド済みDLLに新規4件が含まれておらず、実際には
> 既存564件だけが走っていた。新規4件が実際に実行・合格したことはM0046のビルド後に確認した。
`SceneHwndHost.TickUndoRedoIfNeeded`本体・`OleMenuCommandService`関連の削除自体はVS SDK依存/
UI配線のため既存方針どおりユニットテスト対象外。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | シーンビューにフォーカスがある状態でギズモ編集を3回行い、Ctrl+Zを3回押して3段階とも正しく戻ること |
| 2 | ツールバーの「元に戻す」▼ドロップダウンに、行った編集の履歴が正しく並んで表示されること |
| 3 | コードエディタへフォーカスを移し、Ctrl+Zでそのファイルの通常の編集が問題なく戻せること(最重要の回帰確認) |
| 4 | Ctrl+Y(またはツールバーの「やり直し」)で正しくRedoされること |
| 5 | シーンビューでギズモ編集→フォーカスをビューポートに戻さないままコードエディタでCtrl+Zを連打しても、ビューポート側の表示とソースが食い違わないこと |
