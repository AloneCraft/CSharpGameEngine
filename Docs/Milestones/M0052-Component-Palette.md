# M0052: Componentパレットと、Inspectorのグルーピング/展開

## 目的

「コンポーネントをツールウィンドウからヒエラルキー、インスペクターのいずれかにドロップしたい。
インスペクターにコンポーネントを追加したらそれを展開して内部の値を設定したい」という要請。

適用経路の3層は先行して完成していた。今回はそこへ繋がるUIを作る。

| 層 | 状態 |
|---|---|
| `SceneComponentCatalog.Build(Compilation)` — 候補列挙(generic対応) | 先行実装済み |
| `SceneModelEditApplier.TryApplyAddComponent` — メモリ内即時適用 | 先行実装済み |
| `SceneSourceEditor.ApplyAddComponent` — 保存時のC#書き戻し | M0020 Phase 6 |
| **UI(Inspector展開・パレット・D&D)** | **本マイルストーン** |

## 決定事項(ユーザー承認済み)

- ドラッグ元は**新規作成のComponentパレット**(既存ツールウィンドウの流用ではない)
- ドロップ先の対象: **Hierarchy=ドロップしたNode / Inspector=選択中Node**
- **Inspectorのグルーピング/展開を先に作る**(「展開して値を設定」を成立させるため)
- **まずD&Dだけ実装し、実機確認してから代替経路を判断する**

## 設計で判明した、要求成立を阻む2点

### 追加直後のComponentは中身が空

`TryApplyAddComponent`は引数なしの`new Foo()`に対応するmember空の`SceneComponent`を挿す。
つまり**追加した瞬間に展開しても設定すべき行が1つも無い**。memberが埋まるのはRoslyn再解析が
着地してからで、そこにはデバウンス+解析時間がある。

→ 展開の意図を**保留(`PendingRevealKey`)として着地まで持ち越す**設計にした。
グループ本体には「解析待ち…」を出し、`Members.Count>0`になるまで保留を解除しない
(`InspectorRevealPolicy.ShouldClearPending`)。

### Step 1-7の再構築問題が確実に再来する

従来の`TryRefresh`のガードは`IsKeyboardFocusWithin`だけだった。これはタイプ入力中のTextBoxは守るが、
**Expanderの開閉はマウスだけで行うためフォーカスはパネル外にあることが多く**、再解析のたびに
`ItemsSource`が差し替わって展開状態もスクロール位置も飛ぶ。Hierarchyのインラインリネームが
壊れたのとまったく同じ構図。

→ **内容署名によるSkipを土台に置いた**(`InspectorRefreshPolicy`/`InspectorContentSignature`)。
これは任意の最適化ではなく必須要件。フォーカスによるDeferはその上の追加防御。

## Step 2-1: Inspectorのグルーピング/展開

- `SceneInspectorComponentGroup`と`SceneInspectorModelBuilder.BuildGroups`を追加。
  既存の`Build`は**シグネチャを変えず**`BuildGroups(...).SelectMany(G=>G.Members)`へ委譲するので、
  既存テストは1件も壊れない。両者が一致することをテストで固定している。
- XAMLは外側`ItemsControl`(グループ)→`Expander`→内側`ItemsControl`(行)の入れ子。
  `InspectorEditorKindVisibilityConverter`による4コントロール重ね配置は`MemberRowTemplate`として
  そのまま維持した。`TreeView`は採らない — 独自の選択/フォーカス意味論が`TextBox`のcommit経路
  (`LostFocus`)と衝突するため。
- 展開状態は`CollapsedKeys`(既定は展開、畳んだものだけ記憶)。キーは
  **Node + Component完全修飾名**(`InspectorExpansionKey`) — Component名だけだと、あるNodeで
  畳んだ状態が別Nodeの同名Componentへ伝染して驚く。
- **`Expander.Expanded`/`Collapsed`は購読しない。** `IsExpanded`をTwoWayバインドし、再構築の直前に
  生きた`ItemsSource`から読み戻す(`SceneNodeViewModel.IsExpanded`と`CaptureCollapsedPaths`と同じ方式)。
  これらのイベントは初期のデータバインド代入自体でも発火するため、状態更新の起点にすると
  「確定→再構築→再バインド→再確定」のループを招く。
- 型名の短縮は`SceneInspectorTypeName.Simplify`。`LastIndexOf('.')`ではgeneric instantiationを
  誤って切る(`A.Pair<A.Kind>`→`Kind>`)ので、区切り記号ごとに最後のドット以降を採る。

## Step 2-2: Componentパレットと自動展開

- 新規: `ComponentPaletteToolWindowPane` / `ComponentPaletteToolWindowControl` /
  `ShowComponentPaletteToolWindowCommand`(**CommandId=0x010B**) / `ComponentPaletteEntry` /
  `ComponentPaletteFilterPolicy`。
- **Inspectorとタブ結合させない**(`Orientation=Bottom`)。ドラッグ中に隠れてしまうドロップ先は
  成立しないため、両方が同時に見えている必要がある。
- 候補は`SceneWorkspaceService.GetProjectComponentCatalogAsync`が組み立てる。対象Projectの決め方と
  `Project.Version`キャッシュは`GetProjectSceneCatalogAsync`と同じ流儀。
  **キャッシュするのは`SceneComponentCatalogEntry`ではなく`ComponentPaletteEntry`** —
  前者は`INamedTypeSymbol`を持つため、UIが持ち続けると`Compilation`全体をピン留めしてしまう。
- 表示は「DisplayName(太字)+ 名前空間(淡色)」。generic instantiationや同名型が名前だけでは
  判別できないので名前空間は必須。検索は初版に含めた — genericは1定義あたり最大50件へ展開されるため。
- 適用はパレットが直接行わず`SceneEditorHub.RequestComponentAdd`へ要求を出す。
  `CSharpGameEngineViewerPackage.HandleComponentAddAsync`が唯一の適用点で、Undoラベル
  ("Add component")と成功後のReveal発行をそこに閉じ込めている。
- **ダブルクリック/Enterでの追加をここで出荷した。** これだけで「追加→展開→値設定」が完結するので、
  未検証のD&Dが動かなくても機能がゼロにならない。

## Step 2-3: ドラッグ&ドロップ

- `ComponentDragPayload`は**トップレベルのinternal record**。Node Reparent用の
  `HierarchyDragPayload`はprivate nestedで他ツールウィンドウから参照できず、運ぶ意味も違う。
  ドロップ側は`DataObject`の型で2種類のドラッグを見分ける。
- `ComponentDropPolicy.Evaluate`が`Accept`/`RejectNoScene`/`RejectNoTarget`/`RejectDuplicate`/
  `RejectNoComponentsList`を返し、**`DragOver`のカーソルと`Drop`後の説明文を同じ関数から作る** —
  プレビューと結果が食い違うのが一番わかりにくいため。
- `HierarchyEditKind`への相乗りは採らない。`HierarchyEditRequest.Paths`は`NodePath`の列しか運べず、
  追加するComponent型を伝えられない。`ComponentAddRequest`は`NodePath`と`EditorNodeId`の両方を
  受け取り、Package境界の1か所で解決する。

## 実機報告で発覚した3件の追加修正

### Inspectorの文字色(ダークテーマで判読できない)

`InspectorToolWindowControl.xaml`の行ラベル(`MemberName`)とExpanderヘッダ(`Header`)に
明示的な`Foreground`が無く、ルートの`UserControl`からの継承任せになっていた。ダークテーマ背景に
対して事実上黒文字で描画され、「フィールド名が見えない」「Component名(Transform)が表示されない」
という報告になった。`HierarchyToolWindowControl.xaml`は同種のリスクを`TreeViewItem`の
Style Setterで回避していたが、今回新設したExpander/内側`ItemsControl`の構造にはそれが無かった。
両方に`Foreground="{DynamicResource {x:Static vsshell:VsBrushes.WindowTextKey}}"`を明示して解決した
(新設したネスト用テンプレートにも最初から適用済み)。

### ネストしたObjectCreation値の展開表示

`Vector3といった構造体フィールド名もネストで子を表示するようにして`という要求。`SceneValue`が
`ObjectCreation`種別で`Members`(初期化子)/`Arguments`(位置引数)を既に再帰的に持っていたため、
`SceneInspectorRow`(判別共用体: `SceneInspectorMemberRow`葉/`SceneInspectorNestedRow`入れ子)を
新設し、Component直下でもネスト直下でも同じ`BuildRow`関数で再帰変換する形にした。

**表示専用に限定した**: `SceneModelEditApplier.TryApplySetMember`はComponentのトップレベルMembersでしか
名前一致を探さず、見つからなければ黙って新規メンバーをComponent直下に追加する
(`SceneModelEditApplier.cs:476-497,603-610`)。ネストした子を安易に編集可能へ倒すと
`Transform { …, X=5f }`のような存在しない偽メンバーが生える実害あるバグになるため、
ネスト内の葉は`SceneValue.IsEditable`の値によらず**強制的に`InspectorEditorKind.ReadOnly`**で
構築する(`BuildRow`の`AllowEdit`パラメータ)。編集対応は実データが出てから独立した段階として検討する。

位置引数(`Arguments`)はパラメータ名情報を持たないため、`Value.Type`の公開コンストラクタを
アリティ一致で探索し、1つに絞れれば実パラメータ名を、絞れなければ`[0]`等の合成ラベルを使う
(`ResolveArgumentLabels`、Inspector層だけで完結、`SceneModel`/ADR-0040のwhitelistには触れない)。

XAMLは`DataType`ベースの暗黙`DataTemplate`(`InspectorMemberViewModel`用/
`InspectorNestedRowViewModel`用)を`UserControl.Resources`へ登録し、`ItemTemplate`を指定しない
`ItemsControl`が実行時型で自動的に正しいテンプレートを選ぶ形にした。既存の「Expander+内側
ItemsControl」パターンがそのまま自己参照的に再帰する。展開状態キー(`InspectorExpansionKey`)・
内容署名(`InspectorContentSignature`)もパス込みで再帰対応させ、ネストしたExpanderの開閉状態が
再解析を跨いで保持されるようにした(Step 1-7・Step 2-1と同種の再構築問題の再発を防ぐ)。

### `ComponentPaletteEntry`のWPFバインド内部トラップ

`DisplayName`/`Namespace`/`Icon`を`internal`のままXAMLでバインドしていたため、WPFが例外も警告も
出さず黙って失敗し、アイコンだけ既定値で出て文字が全部空になっていた。3プロパティを`public`へ変更し、
`InspectorMemberViewModelTests`と同じ理由でアクセシビリティ契約をテストで固定した
(`ComponentPaletteFilterPolicyTests.PropertiesBoundFromXamlArePublicSoWpfCanResolveThem`)。

### Hierarchy選択がpin無しでは効かない

実機報告「Hierarchyで選択してもInspectorに何も出ない」を追ったところ、原因はInspector固有ではなく
**Pinの仕組み全体**だった。`SceneEditorSessionService.Apply`は`Documents`辞書にキーが無いと何もしない
(silent no-op)。`Documents`辞書を作るのは`Pin`(=シーン デザイナーを開いたときだけ呼ばれる)であり、
Hierarchyは`Pin`に依存せず`ActiveSceneModel`だけで表示できるため、**シーン デザイナーを一度も
開かずにノードを選ぶ経路が普通に起きる**。

この結果、以下がシーン デザイナー未オープン時に**無言で失敗していた**:
- Inspectorへの選択伝播(`OnHierarchySelectionChanged`)
- Hierarchyの構造編集(`HandleHierarchyEditAsync`: AddChild/Rename/Remove/Reparent/Reorder)
- 今回追加したComponent追加(`HandleComponentAddAsync`)

後者2つは「読み取り専用の一時Snapshot」(`GetSnapshotForModel`)を作ってから`Apply`を呼んでいたが、
そのSnapshotのキーは`Documents`辞書に無いため`Apply`が常にfalseを返していた——つまり**M0052以前から
Hierarchyの構造編集自体が同じ理由で壊れていた**可能性がある(実機未確認のまま気づかれなかった)。

共通ヘルパー`CSharpGameEngineViewerPackage.EnsurePinned(Session,Model)`を追加し、3箇所すべてを
「pin済みならそのSnapshot、そうでなければpinしてから返す」に統一した。pinは`SceneEditDocument`を
(無ければ)作るだけで、Viewport/シーン デザイナーのウィンドウは開かず、Undo履歴やdirty状態も汚さない。
既にそのSceneへpin済みなら再pinしない(Hierarchy選択のたびの無駄な再publishを避けるため)。

## Known deviations

- **Hierarchyの`DragOver`プレビューは best-effort。** Hierarchyは`SceneEditorHub.ActiveSceneModel`
  (実解析の結果)しか持たず、Documentの予測モデル(未保存の追加を含む)を見られない。そのため
  未保存の追加が絡む一瞬だけ重複判定が甘くなることがある。**Inspectorは同じ予測投影を見るので
  判定は適用側と必ず一致する。** どちらの場合も最終判断は`TryApplyAddComponent`が行い、
  拒否理由はステータスバーに出るので、誤った結果が黙って通ることはない。
- 重複型をパレットの候補から**外さない**。ドロップ先が事前に確定しないため。代わりに`DragOver`で
  不可カーソルにして離す前に伝える。
- Expanderヘッダに削除(×)ボタンを実装した(`ComponentRemoveRequest`→`HandleComponentRemoveAsync`、
  `ComponentAddRequest`と同じ単一適用点パターン)。**実機報告で発覚し追加修正**: 既定のButtonクロムでは
  "×"の文字がダークテーマ背景に対してほぼ見えず、当たり判定も不明瞭だったため、`Border`+`TextBlock`の
  自前`ControlTemplate`(`InspectorRemoveButtonStyle`)へ差し替えて確実に描画されるようにした。
  削除はHierarchyの削除確認(`HierarchyToolWindowControl.RequestRemove`)と同じ`MessageBox.Show`
  (Yes/No、Warning)を経由するようにした。加えて、Componentのヘッダ行を選択した状態での`Key.Delete`
  でも同じ確認→削除経路(`RequestRemoveComponent`共通メソッド)を通せるようにした
  (`HandleInspectorKey`)。
- `CanRemove`のような未使用のプロパティはグループに持たせていない。

## 未検証の前提

**カスタムWPFツールウィンドウ間の`DragDrop.DoDragDrop`は、このリポジトリに前例も検証記録も無い。**
唯一の近縁事例ADR-0022(Solution Explorerからの`FileDrop`受け)はM0020 Phase 0で削除済みで、
そのADR自身が「実機確認は未実施」と記していた。別`HwndSource`をまたぐDnDが正しく動くかは不明。
Step 2-3を独立させ、Step 2-2でダブルクリック追加を先に出荷したのはこのため。

## Evidence

全1308件成功(VSExtension 806件)。ソリューション全体ビルドは0警告0エラー。
VS SDK依存部分はCodeVerified止まりで**実機確認が必須**。

## Next action(実機確認)

- **pin修正**: シーン デザイナーを一度も開かずにHierarchyでノードを選び、Inspectorに反映されること /
  同じ状態でHierarchyの子ノード追加・リネーム・削除・Reparentが効くこと(以前は無言失敗していた可能性)。
- **文字色修正**: ダークテーマでComponent名(Expanderヘッダ)・フィールド名が判読できること。
- **ネスト表示**: `Vector3`等のネストした値を持つComponentで、展開すると子(X/Y/Z等)が個別行として
  見える / 子行は編集不可(グレー表示、入力コントロールが出ない) / 再解析を跨いでネストの展開状態が
  保持される / 位置引数形式・初期化子形式・両者の混在すべてで正しく分解される。
- **Step 2-1**: 再解析を跨いで展開状態が残る / タイプ入力中に値が消えない / スクロール位置が保持される /
  ギズモドラッグ中にちらつかない。
- **Step 2-2**: 型が出る / 検索が効く / ダブルクリックで追加できる / Inspectorが自動展開する /
  **解析着地後に値が編集できる**(「解析待ち…」→行が出るまで) / Ctrl+Zで消える / 保存でC#に書かれる /
  `Components=[…]`未記述ノードで拒否メッセージ。
- **Step 2-3(D&D検証チェックリスト)**: コードレビューではロジック上の不備は見つからなかった
  (Hierarchyの既存Reparentドラッグと同じ「しきい値越えてから`DoDragDrop`」の作法、
  `ComponentDropPolicy.Evaluate`はプレビューと実判定を同じ関数から作りテスト済み)。未検証なのは
  純粋にOS/WPFレベルの`DragDrop.DoDragDrop`が別ツールウィンドウ(別`HwndSource`)間で実際に
  ペイロードを届けるかという、コードでは検証しきれない部分のみ。以下をF5(Experimental Instance)で
  実際に辿って確認する:
  1. **起動**: `F5`でExperimental Instanceを起動し、Componentを持つSceneを開き、Scene Designerを
     pinした状態にする(pin無しでは追加/削除が無言失敗する既知の問題があるため — `EnsurePinned`)。
  2. **パレットを開く**: Component パレットツールウィンドウを開き、Hierarchy/Inspectorと同時に
     見える位置にドッキングする(Inspectorとタブ結合させない設計)。
  3. **パレット→Hierarchyの任意ノード**: 型をHierarchy上の既存ノードへドラッグ。ドラッグ中に
     カーソルが「コピー」アイコンになること、離した時点でComponentが追加されること、Inspectorが
     自動展開されて値が見えることを確認する。
  4. **パレット→Hierarchyの空白**: 型をHierarchyの空白部分(ノードが無い場所)へドラッグ。
     Root直下に新規Entityが作られ、そのComponentを持つことを確認する(`AcceptCreateRootChild`経路)。
  5. **パレット→Inspector**: Hierarchyでノードを選択した状態で、型をInspectorパネルへドラッグ
     &ドロップ(パネル内のどこに落としても選択中ノードが対象になる)。Componentが追加されることを
     確認する。
  6. **重複型のフィードバック**: 既に持っているComponent型を同じノードへドラッグし、不可カーソルが
     出ること、離してもComponentが増えないこと、ステータスバーに拒否理由が出ることを確認する。
  7. **既存のノードReparentドラッグが無傷**: Hierarchy内でのノード同士のReparentドラッグが、
     Componentドラッグ実装追加後も変わらず機能することを確認する(`Tree_DragOver`/`Tree_Drop`が
     `ComponentDragPayload`と`HierarchyDragPayload`を型で見分けているため干渉が無いはずだが要確認)。
  8. **Sceneが開かれていない状態でのドラッグ**: Scene Designerを閉じた状態でドラッグした場合、
     `RejectNoScene`のフィードバック(不可カーソル・拒否理由)が出ることを確認する。
