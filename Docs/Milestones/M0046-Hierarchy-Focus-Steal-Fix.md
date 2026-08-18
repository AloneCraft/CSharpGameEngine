# M0046: Hierarchyが再解析のたびにフォーカスを奪う不具合の修正

## 目的

M0045(標準コマンド登録の全廃 + GetKeyStateポーリング)を実機投入した結果、ユーザー報告:

1. **Undo履歴が全く残らない**(M0045では直らなかった)
2. **新症状**: ソースエディタで"abc"と素早く入力している間は問題ないが、**1秒ほど入力を止めると
   フォーカスがソースエディタからヒエラルキーへ移る**

症状2が決定的な手がかりであり、**症状1の原因でもあった**。

## 真因

`HierarchyToolWindowControl.Tree_SelectedItemChanged`が無条件に`this.Tree.Focus()`を呼んでいた。

このハンドラは矢印キーでのツリー内移動の受け口だが、**再解析による`ItemsSource`差し替えの後に
WPFが遅延生成した`TreeViewItem`へスタイルSetter経由で`IsSelected`が入ったタイミングでも発火する**。

XAMLの`TreeViewItem`スタイルに
`<Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}" />`があり、
このSetterが効くのはコンテナが生成された時点 — `ItemContainerGenerator`はコンテナを遅延生成する
ため、これは`Populate`の同期ブロックが終わった**後**の測定・配置パスで起きる:

```
Populate:
  SuppressSelectionPublish=true;
  try {
      Tree.ItemsSource=Roots;      // コンテナ未生成。SelectedItemChangedはまだ出ない
      ApplySelectionToTree();      // ← 内部のfinallyがフラグをfalseへ戻す(欠陥2)
  } finally { SuppressSelectionPublish=false; }
  → return

（次のレイアウトパス）
  コンテナ生成 → Style Setter適用 → TreeViewItem.IsSelected=true
  → TreeView.SelectedItemChanged 発火（このときフラグは false）
  → Tree.Focus()  ★フォーカス強奪
```

つまり`SuppressSelectionPublish`ガードは**同期ブロックでしか張られていない**のに、守るべき
イベントは**非同期(レイアウトパス)**で来るため、構造的にすり抜ける。

**欠陥2**: `ApplySelectionToTree`の`finally`が`SuppressSelectionPublish=false`を無条件に代入し、
呼び出し元の値を復元していなかった。`Populate`が`true`にした直後に呼ぶため、`Populate`のtry
ブロック内にいるにも関わらずフラグは既に`false`へ戻っていた。

### これが症状1(Undo履歴)も説明する

再解析は入力の250msデバウンス後に走るため、「1秒ほど入力を止める」とちょうど着地する。
着地するたびにフォーカスがヒエラルキーへ移るので:

| 報告されていた症状 | 説明 |
|---|---|
| ツールバーの▼履歴が空 | アクティブなVSウィンドウがヒエラルキー(Undo管理を持たないツールウィンドウ)になっており、ツールバーはそこを見ている |
| コードエディタでCtrl+Zが効かない | 押した時点でフォーカスは既にエディタから抜けている |
| ギズモ移動直後、履歴ボタンが一瞬(0.5秒)出て消える | 編集直後はまだエディタ文脈 → 再解析着地 → フォーカス強奪 → ボタン消滅 |
| Undoが1段階しか効かない | 最初の1回は強奪前に間に合うことがある |

クラスのdocコメント自身が「source focus is never changed by those requests」と書いており、
ADR-0041の「ツールウィンドウはフォーカスを奪わない」原則にも反していた —
**実装が自分の文書化した契約に違反していた**。

## 実装したこと

### 1. フォーカスを「奪う」のをやめ、「持っていること」を条件にする

矢印キーでのツリー内移動は`SelectedItemChanged`経由でしか届かないため、ハンドラごと消すと
キーボード操作での選択発行が壊れる。そこで判定を反転させた:

- ユーザーがツリー内で操作しているなら`Tree.IsKeyboardFocusWithin`は**既にtrue**
- 再解析による再構築中はユーザーはコードエディタにいるので**false**

`Tree.Focus()`は`Tree_SelectedItemChanged`から削除し、**マウス操作の2箇所だけ残した**
(左クリック・右クリック) — これらはユーザーがツリーを直接クリックした本物のジェスチャーであり、
フォーカスが移って当然。

### 2. `HierarchyFocusPolicy`(新規)

判定を純粋関数として`HierarchySourceSync.cs`へ切り出した
(`HierarchySelectionPlanner`・`SceneSelectionPathResolver`と同じファイル・同じ流儀):

```csharp
internal static bool ShouldPublishSelectionChange(
    bool SuppressSelectionPublish,bool TreeHasKeyboardFocus)=>
    !SuppressSelectionPublish&&TreeHasKeyboardFocus;
```

### 3. `ApplySelectionToTree`のガードを保存・復元へ

`bool Previous=this.SuppressSelectionPublish;` … `finally { this.SuppressSelectionPublish=Previous; }`。
手順1で致命的ではなくなるが、呼び出し元のガードを黙って解除する潜在バグなので同時に閉じた。

## M0045の扱い

M0045(標準コマンド登録の全廃)は**維持する**。`MultiLevelUndo`が`OLECMDTEXT`を埋めていなかった
のは実在の欠陥であり、ツールウィンドウのペインへ標準コマンドIDを登録する設計自体も筋が悪い。
**ただしM0045は今回の真因ではなかった** — 4つの症状はすべてフォーカス強奪で説明できる。

## Evidence

`HierarchyFocusPolicyTests.cs`(新規、4件): ガードON/OFF × フォーカス有/無の4パターン。
本命は「ガードOFF かつ フォーカス無し」= false(再解析着地でのコンテナ遅延生成ケース)。

VSExtension.Tests 572/572(既存568+新規4、リグレッションなし)。

WPFのコンテナ遅延生成そのものはVS/WPF依存のため既存方針どおりユニットテスト対象外。

> **M0045の集計訂正**: M0045で報告した「564/564」は誤りだった。`dotnet test --no-build`を
> テストファイル作成**後**に実行したため、ビルド済みDLLに新規4件が含まれていなかった。
> M0046のビルド後に4件とも実行・合格を確認した(正しくは568/568)。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | ソースエディタで"abc"と入力し、1秒以上放置してもフォーカスがヒエラルキーへ移らない(今回の主目的) |
| 2 | ツールバーの「元に戻す」▼ドロップダウンに編集履歴が並ぶこと |
| 3 | コードエディタでCtrl+Zを連打して複数段階戻せること |
| 4 | シーンビューでギズモ編集を3回 → Ctrl+Z 3回で3段階とも戻ること |
| 5 | ヒエラルキーを**クリック**したときは従来どおりフォーカスが移り、選択がビューポート/ソースへ同期すること(回帰確認) |
| 6 | ヒエラルキーにフォーカスがある状態で**矢印キー**を押すと選択が移動し、ビューポート/ソースへ同期すること(手順1で壊しやすい経路) |
| 7 | F2リネーム・Delete・右クリックメニュー・ドラッグ&ドロップが従来どおり動くこと(回帰確認) |
| 8 | シーンビューにフォーカスがある状態でCtrl+Zを押したとき、コードエディタ側の無関係な編集まで同時にUndoされないこと(M0045のポーリングとVS標準Edit.Undoの二重発火確認) |
