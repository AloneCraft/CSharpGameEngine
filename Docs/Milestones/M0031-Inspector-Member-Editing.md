# M0031: Inspector 数値編集 UI

## 目的

[[M0020-Scene-Editor-Interaction]]が「意図的に除外したこと」として明示的に将来へ送った項目を実装する。

> 数値編集UIは`SetComponentMember`配線(Phase 3)後に小さく足せる状態になるが、本マイルストーンには
> 含めない。

調査の結果この記述は正確だった。難所(ソース書き戻し、競合マージ、1 Undoコミット、Component/Member
単位の選択)は全てM0020で実装済みで、欠けていたのはツールウィンドウ本体(Pane/WPF/コマンド/vsct登録)
と値の種類ごとのエディタ選択ロジックだけだった。新しい編集パイプラインは作らず、既存の
`SetComponentMemberOperation`→`SceneEditorSessionService.CommitAsync`経路にそのまま乗せた。

M0030に続く番号として**M0031**を使う(M0021〜M0029は[[M0030-SceneView-Gizmo-Transform-Editing]]の
時点で欠番確定済み)。

## 実装したこと

### 対象範囲

選択中ノードの、C#ソースに**既に書かれている**Component memberのうち編集可能な種別
(`SceneValueKind.Literal`/`EnumMember`/`Default`)を一覧し、値の型に応じたエディタで書き換える。

| CLR型 | エディタ | commit時の書式 |
|---|---|---|
| bool | CheckBox | `true`/`false` |
| enum | ComboBox(型の全constメンバーを列挙) | 完全修飾シンボル参照(`global::Ns.T.Member`) |
| 数値(sbyte〜decimal、9種) | TextBox | 型ごとの`TryParse`+接尾辞(f/d/m/L/UL/U、それ以外は接尾辞なし) |
| string | TextBox | `SymbolDisplay.FormatLiteral`でエスケープ済み文字列リテラル |
| それ以外(定数式・静的参照・ファクトリ呼び出し・入れ子オブジェクト生成・コレクション・未解決) | 読み取り専用テキスト | — |

### 純粋層の拡張(`SceneInspectorModel.cs`)

- `InspectorEditorKind`(`Number`/`Boolean`/`Enum`/`Text`/`ReadOnly`)と、それを`SceneValue`から
  判定する`DetermineEditorKind`。`IsEditable`で読み取り専用種別を先に弾き、残りをCLR型
  (`SpecialType`/`TypeKind.Enum`)で振り分けるだけで済んだ。
- `SceneInspectorMember`を拡張し、`EditorKind`/`ConstantValue`/`NumericType`/`EnumCandidates`を追加。
  既存の`MemberName`/`SourceExpression`/`IsEditable`は変更していない(既存テスト2件が無改修で通る)。
- `SceneInspectorValueFormat.TryFormatNumber`: **対象の正確なCLR型の`TryParse`に直接投げる**
  (`short.TryParse`/`byte.TryParse`等)。一度`int`へ丸めてから振り分ける実装だと、例えば`short`宛ての
  `"99999"`がオーバーフローしたまま素通りし、生成ソースがコンパイルエラーになる。この回帰は
  変異テストで実際に確認した(下記「テストが空虚でないことの確認」)。
- `SceneInspectorCommitPlanner.CreateCommit`: `SetComponentMemberOperation.OriginalValueExpression`を
  **常に`null`で渡す**。これを渡すと`SceneEditConflictMerger.MergeMember`が差分リベース
  (`Current + (Final - Original)`)を行う — `PropertyGizmo`のドラッグ(相対ジェスチャ)には正しいが、
  Inspectorのタイプ入力(絶対値指定)には誤りである。「5にする」と打ったら5になるべきで
  「+2する」ではない。

### WPFレイヤー(新規)

- `InspectorMemberViewModel.cs`: バインド対象は全て`public`(`SceneNodeViewModel`と同じ理由 —
  WPFのバインディングはpublicプロパティしか解決せず、internalは黙って失敗する)。
  `INotifyPropertyChanged`は実装していない — 行の再構築は選択変更/commit完了時だけで、
  キー入力のたびではないため不要。
- `InspectorToolWindowPane.cs`/`InspectorToolWindowControl.xaml(.cs)`: `SceneDesignerToolWindowPane`の
  静的`SessionProvider`パターンをそのまま踏襲。`Session.PinnedSnapshotChanged`+
  `Session.Selection.SelectionChanged`を購読する。
- `InspectorVisualConverters.cs`: `InspectorEditorKindVisibilityConverter`で1行に4種のエディタ
  (TextBox/CheckBox/ComboBox/読み取り専用TextBlock)を重ねて置き、対応する種別のものだけ見せる。

### 発見したWPFの罠(実装中に判明、設計を変更した箇所)

`CheckBox.IsChecked`/`ComboBox.SelectedItem`を素朴に`TwoWay`バインドし`Checked`/`SelectionChanged`を
commitの起点にすると、**初期のデータバインド代入そのものがそれらのイベントを発火させる**。
選択変更→行構築→バインド→イベント発火→commit→`TryRefresh(Force:true)`→行再構築→再バインド→
イベント再発火→…という無限ループになる(実装中に気づき、コミット前に設計変更した)。

対策: `IsChecked`/`SelectedItem`は`Mode=OneWay`にし、commitは`Click`(CheckBox)/`DropDownClosed`
(ComboBox)という**ユーザー操作でしか発火しないイベント**を起点にする。

### 編集中のフォーカス保護

選択中のSceneはコード編集のたびに再解析される。素直に行を再構築するとタイプ中のTextBoxの入力が消える
(Hierarchyの再構築対応と同じ問題クラス)。Inspectorでは`IsKeyboardFocusWithin`が真の間は再構築を
スキップする単純な方式で足りた(Hierarchyのパス単位の展開状態保持ほど複雑な仕組みは不要 —
Inspectorの再構築頻度は選択変更時とcommit完了時だけで、キー入力のたびではないため)。
Enter/Escapeキーはこのフォーカス保護の外側で、`Keyboard.ClearFocus()`を明示的に呼んで確定・破棄する。

Enterキーはコミット後に`Keyboard.ClearFocus()`を呼ぶため`LostFocus`も連鎖して発火する。これが
同じ値をもう一度commitして1操作が2 Undoになるのを防ぐため、`SuppressNextLostFocusCommit`フラグで
連鎖1回だけ抑制する(Hierarchyの`SuppressSelectionPublish`と同じ抑制パターン)。

### VS登録

- コマンドID `0x010A`(`ShowInspectorToolWindowCommandId`)。`0x0107`/`0x0108`は退役済みで再利用禁止、
  `0x0109`は`OpenSceneDesignerCommandId`が既に使用しているため衝突を避けて次の空きを割り当てた。
- `InitialLayoutToolWindowPlan`にHierarchyと並べて追加した。Scene Designerと違い、InspectorはD3D12/
  HWNDコストの無い素のWPFパネルであり、「ノードを選択してください」という空状態はHierarchyの
  「シーンファイルを開いてください」と同格に安全に見せられるため、初回起動時から
  Solution Explorerの右にドッキングする(Hierarchyは左)。

## 検証結果(実際に実行して確認)

```
dotnet build CSharpGameEngine.slnx -c Debug
  ビルドに成功しました。0 個の警告 / 0 エラー

dotnet test CSharpGameEngine.slnx -c Debug --no-build
  CSharpGameEngine.Entities.Tests        合格 42
  CSharpGameEngine.Import.Tests          合格 30
  CSharpGameEngine.Bridge.Hosting.Tests  合格 8
  CSharpGameEngine.Core.Tests            合格 122
  CSharpGameEngine.Windows.Tests         合格 87
  CSharpGameEngine.CodeAnalysis.Tests    合格 101
  CSharpGameEngine.SceneModel.Tests      合格 104
  CSharpGameEngine.VSExtension.Tests     合格 474
  合計 968件、失敗 0
```

VSExtension.Testsは[[M0030-SceneView-Gizmo-Transform-Editing]]完了時点の448件から**474件**(+26)
へ増えた。

### テストが空虚でないことの確認(変異テスト)

`SceneInspectorValueFormat.TryFormatNumber`のInt16分岐を、意図的に`short.TryParse`から
`int.TryParse`へ戻す(修正前の設計に相当するオーバーフロー混入バグ)と、
`TryFormatNumber_OutOfRangeForTargetType_FailsInsteadOfOverflowingSilently`だけが失敗し
(18合格/1失敗)、他のテストは影響を受けなかった。復元後は19件全て合格。

## 意図的に除外したこと

- **入れ子`ObjectCreation`(`new Vector3(...)`等)の分解編集。** 読み取り専用として表示するに留めた。
- **ソース未記載メンバーへの新規書き込み(Add Member的なUI)。** `Component.Members`は初期化子に
  実際に書かれたメンバーだけを列挙するため、既存の`SceneInspectorModelBuilder.Build`はそもそも
  未記載メンバーを返さない。列挙対象をComponentの型シンボル全体へ広げて「まだ値の無いメンバー」も
  表示する設計は、値のシード(CLRデフォルト値)・挿入位置・UXの決定を要する別機能であり、
  「小さく足す」という目的から外れるため対象外にした。`SceneSourceEditor.ApplyInsertComponentMember`
  (メンバー未記載時の挿入経路)は既存のまま温存されているが、本Milestoneのビルダーからは
  到達しない。
- **アセット参照(`StaticMemberRef`)のピッカー。** ドラッグ&ドロップ配置(ADR-0041 §6が将来送り)と
  同種の別課題。
- **複数選択の一括編集。** primaryノードのみ表示・編集する(既存の`Selection.PrimaryNodeId`解決を
  そのまま使う)。
- **ADRの新規作成。** [[ADR-0041-Scene-Editor-Interaction-Architecture]]が既に編集トランザクション
  設計を規定しており、本Milestoneは既存の`SetComponentMemberOperation`経路に乗るだけでScene・Asset・
  Behavior・Render Graph・Omniのセマンティクスを変えていない。

## 残課題

- **実機VS Experimental Instanceでの目視確認(未実施 — ユーザー側で実施)。** F5起動
  (`/rootsuffix Exp UserEcsProject.slnx`)で`FirstPersonStageScene.cs`を開き、ノード選択→
  インスペクター行表示→数値/真偽値/Enumの編集→Ctrl+Zでの復元→編集中の他ウィンドウ操作で入力が
  消えないことを確認する。[[M0030-SceneView-Gizmo-Transform-Editing]]の未実施8項目とあわせて
  同じセッションで消化できる。
- 上記の実機確認が完了するまで、台帳`F-L8-012`のVerificationは`TestVerified`のままとし、
  `Verified`へ上げない。
