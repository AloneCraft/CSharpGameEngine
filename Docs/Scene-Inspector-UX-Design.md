# Scene Inspector UX 設計(提案・一部実装済み)

> **状態**: 提案。**段階1のうちComponentグループ化と展開だけをM0052で実装した**(下記「段階分け」参照)。
> 残りは未承認・未着手で、承認後にADR化とマイルストーン分割を行う。
> **前提**: M0051 Step 1-7 / 1-8 の実機確認が通っていること。この設計は選択・セッション配線に
> 触れるため、そこが安定してから着手する。

## 目的

現在のInspector(`SceneInspectorModel.cs` ほか計730行)は、**編集できる値だけを平坦に並べた表**である。
値ごとに「型の単純名.メンバー名」というラベルを前置し、右に140px固定の入力欄を置くだけで、
グループ化も入れ子も複数選択もナビゲーションも無い。

一方、解析層の`SceneValue`は値1つごとに次を保持している。

| 保持している情報 | 現在のInspectorでの扱い |
|---|---|
| `SourceExpressionText`(書いたままの式) | 数値の表示にのみ使用 |
| `EmitExpressionText`(完全修飾・using非依存) | **未使用** |
| `Location`(FilePath + TextSpan + LineSpan) | **未使用** |
| `Type`(`ITypeSymbol`) / `ReferencedSymbol`(`ISymbol`) | 型振り分けにのみ使用 |
| `Members` / `Arguments` / `Elements`(再帰的な値の木) | **未使用**(ReadOnly扱いで潰している) |

**この設計の主題は「捨てている情報を全部UIへ出す」ことに尽きる。**
C#ソースが正本であるという性質を、汎用のプロパティ表ではなく専用UIで活かす。

---

## 中核となる設計判断

### 判断1: 平坦な表ではなく、`SceneValue`の再帰をそのまま辿る**木**にする

`SceneValue`は`Members`/`Arguments`/`Elements`を持つ再帰構造である。木にすれば
`new Vector3(0,1,0)`の分解編集(現Inspector最大の制約)が構造的に片付き、
`Camera.CreateFirstPerson(…)`の引数も`[ … ]`の要素も同じ仕組みで扱える。

```
▾ 🔷 Transform
    PositionX            9f
    PositionY            0f
  ▾ Rotation             new Quaternion(…)
      X                  0f
      W                  1f
▾ 🔶 StaticMesh
    AssetPath            Assets.Floor          ↗
  ▸ Material             Materials.Create(…)
```

### 判断2: 読み取り専用を「壊れているもの」として見せない

現在は`IsEditable`が偽の値を**すべて灰色のTextBlock**にしている。しかし
`12f*MathF.PI/180f`(定数式)も`Assets.Floor`(アセット参照)も`Camera.CreateFirstPerson(…)`
(ファクトリ呼び出し)も、**ユーザーが意図して書いたもの**であって異常ではない。
灰色一色にすると「編集できない＝何か失敗している」と読めてしまう。

種別ごとに見せ方を変え、**本当に異常なのは`Unsupported`だけ**であることを視覚的に区別する。

### 判断3: すべての行がソースとシンボルへ辿れる

`Location`があるので行から`.cs`の該当スパンへ、`Type`/`ReferencedSymbol`があるので
型・メンバーの定義へ飛べる。**これがプロパティウィンドウでは原理的にできない差分**であり、
専用ツールを選ぶ理由そのもの。

### 判断4: コピーは「テキスト」と「C#」の2系統、それぞれ2形

`SourceExpressionText`(書いたまま)と`EmitExpressionText`(完全修飾・どこへ貼っても通る)の
両方を持っているので、両方出す。これによりInspectorが**転送手段**になる —
あるノードのComponentをコピーして別ノードのソースへ貼る、という使い方ができる。

---

## 画面構成

```
┌────────────────────────────────────────────────┐
│ SM_Cube8                        Transform ·StaticMesh │  ← ヘッダ(名前＋Componentチップ)
│ 🔍 [ filter…                                  ] │  ← 絞り込み
├────────────────────────────────────────────────┤
│ ▾ 🔷 Transform                              ↗  │  ← Componentグループ
│      PositionX          [ 9f          ]        │
│      PositionY          [ 0f          ]        │
│    ▾ Rotation           new Quaternion(…)      │
│        X                [ 0f          ]        │
│ ▾ 🔶 StaticMesh                             ↗  │
│      AssetPath          Assets.Floor        ↗  │  ← 静的参照はリンク
│      Scale              12f*MathF.PI/180f      │
│                         = 0.209         (読取専用)│  ← 定数式は計算値を併記
└────────────────────────────────────────────────┘
```

- **ヘッダ**: 選択ノード名と、そのノードが持つComponentのチップ。チップクリックでその
  グループへスクロール。複数選択時は「3個のノードを選択中」。
- **絞り込み**: メンバー名・Component型名の部分一致。ノードのComponentが増えると必須になる。
- **Componentグループ**: 折りたたみ可能。アイコンはHierarchyで既に使っているSVGを再利用する
  (フォントサイズ追従の仕組みごと)。行末の`↗`でその**Component型の定義**へジャンプ。
- **列**: 名前列は可変、値列は右寄せで最小幅を持つ(現在の140px固定は狭すぎて式が読めない)。

---

## 値の種別ごとの見せ方

`SceneValueKind`のwhitelistがそのままUIの分類になる。

| Kind | 表示 | 編集 | 行の操作 |
|---|---|---|---|
| `Literal`(数値) | 入力欄。リテラル接尾辞を保持(`9f`) | ○ | **ラベルのドラッグで値を増減**(後述) |
| `Literal`(真偽) | チェックボックス | ○ | |
| `Literal`(文字列) | 入力欄(引用符なしで表示) | ○ | |
| `EnumMember` | ドロップダウン | ○ | 定義へジャンプ |
| `Default` | `default` を淡色・斜体で表示。クリックで値の入力を開始しリテラル化 | ○ | |
| `ConstantExpression` | 式をそのまま表示し、**計算結果を併記**(`12f*MathF.PI/180f` → `= 0.209`) | ×(理由をツールチップ) | |
| `StaticMemberRef` | **リンク**として表示(`Assets.Floor`) | × | 定義へジャンプ / ソースへ移動 |
| `FactoryCall` | 折りたたみ。ヘッダに`Camera.CreateFirstPerson(…)`、子に引数 | ×(引数は将来) | 定義へジャンプ |
| `ObjectCreation` | 折りたたみ。ヘッダに型名、子にメンバー | 子は○ | 型定義へジャンプ |
| `Collection` | 折りたたみ。ヘッダに要素数(`[3 items]`)、子に要素 | 子は○ | |
| `Unsupported` | **警告アイコン**＋生のソーステキスト | × | ツールチップで「Scene DSLの許可構文外」と理由を示す |

**要点**: `ConstantExpression`〜`FactoryCall`は淡色にせず**通常の文字色**で出す。
淡色は`Default`と`Unsupported`にだけ使う。「編集できない」と「異常」を混同させない。

### 数値のドラッグスクラブ

**ラベル**を左右にドラッグして値を増減する(Unity/Blenderの慣習)。入力欄そのものではなく
ラベルを掴ませるのは、クリックでのキャレット移動と衝突させないため。

- 修飾なし = 通常の刻み、`Shift` = 粗く、`Ctrl` = 細かく
- 1ドラッグ = **1 Undo単位**。`SceneHwndHost`が既に使っている`EditTransactionController`を
  そのまま流用する(現在Inspectorは未使用)
- 型ごとの刻み幅は`SceneInspectorValueFormat`の型情報から決める(整数型は1刻み)

これが「Inspectorが気持ちいいかどうか」を最も左右する。現状は入力欄へ数字を打つしかない。

---

## 操作

### キーボード

| キー | 動作 |
|---|---|
| `Enter` | 確定して次の行へ |
| `Esc` | 取り消して元の表示へ |
| `Tab` / `Shift+Tab` | 次/前の**編集可能な**行へ(読み取り専用行は飛ばす) |
| `F12` | その行の型/シンボルの定義へ |
| `Ctrl+F12` | その行を書いている`.cs`のスパンへ |
| `Ctrl+C` | 選択行をテキストとしてコピー |
| `Ctrl+Shift+C` | 選択行をC#としてコピー |
| `↑`/`↓` | 行移動 |
| `←`/`→` | 折りたたみ/展開 |

**Ctrl+Z / Ctrl+S は現状どおり**、`SceneEditorHub.SetSurfaceFocus("Inspector", …)` 経由で
デザイナーへルーティングする。Step 1-7の「テキスト入力中はCtrl+Z/Yを譲る」除外もそのまま効く。

### コピー

| コマンド | 出力 |
|---|---|
| テキストとしてコピー | `PositionX\t9f` のタブ区切り。複数行可 |
| **C#としてコピー(書いたまま)** | `PositionX=9f,` / Component選択時は `new Transform { PositionX=9f, … },` |
| **C#としてコピー(完全修飾)** | `EmitExpressionText`基準。usingに依存せずどこへ貼っても通る |

Componentグループを選んでコピーすれば、そのComponent宣言まるごとが得られる。
ノードヘッダを選べば`new Node { … }`まるごと。**Inspectorがノード間の転送手段になる。**

### ナビゲーション

- 行の`↗`アイコン、または`F12`で定義へ。飛び先は`ReferencedSymbol` ?? `Type`。
- `Ctrl+F12`で`Location`のスパンへ。既存の`SceneSourceMarkerService`と同じ移動経路を使う。
- **逆方向**(コードエディタのキャレット → 対応するInspector行のハイライト)も同じ`Location`で
  実現できる。ノード単位では既に実装済みなので、その延長。

---

## 複数選択

`SceneSelection.NodeIds`は既に複数を保持しているが、現在のInspectorはPrimaryしか見ていない。

- **共通するComponent・メンバーだけ**を表示する(積集合)
- 値が異なる欄は `—` で表示し、編集すると**全選択ノードへ同じ値を書く**
- ヘッダは「3個のノードを選択中」＋共通Componentのチップ
- 1操作 = 1 Undo単位(複数ノードへの書き込みをまとめる)。Hierarchyの複数選択操作が既に
  この方針なので揃える

---

## 更新と編集中の保護

**Step 1-7でHierarchyが踏んだ穴を最初から避ける。**

- 再構築の判定は純粋関数(`InspectorPopulatePolicy`)へ切り出す。UIコードへ埋めない
- **内容署名が同じなら作り直さない**(冪等化)。Hierarchyへ入れた`HierarchyContentSignature`と同型
- **編集中(入力欄にフォーカス、またはドラッグスクラブ中)は作り直さない**。解除時に
  そのとき最新のモデルで再構築する(保留したモデルを流すと編集結果を上書きする)
- **展開状態は`NodePath`+メンバー経路をキーに保持**して再構築を跨ぐ。Hierarchyの
  `CollapsedPaths`と同じ方式

### ギズモドラッグ中の追従

現在は確定(マウスアップ)まで数値が動かない。動かないと「壊れている」ように見える。

`SceneHwndHost`はドラッグ中に`Renderer.UpdateObjectTransform`でレンダラだけ更新している。
同じタイミングで**プレビュー値だけをInspectorへ push する軽量チャンネル**を足す
(対象は1ノードのTransformメンバーのみ)。Documentへは commit しないので、
Undo履歴もウォーターマークも汚さない。確定時に通常の経路で正式な値が来て上書きされる。

---

## 段階分け

| 段階 | 内容 | 得られるもの | 状態 |
|---|---|---|---|
| **1** | 木構造化 + Componentグループ + 値種別ごとの見せ方 | `Vector3`等の分解編集、読み取り専用が「異常」に見えなくなる | **Componentグループ化・展開・ネストしたObjectCreation値の分解表示(読み取り専用)はM0052で実装。分解編集と値種別ごとの見せ方は未着手** |
| **2** | ナビゲーション(F12 / ソースへ移動)+ コピー2系統 | **専用ツールにする理由の本体** | 未着手 |
| **3** | ドラッグスクラブ + キーボード操作の整備 | 「気持ちよさ」 | 未着手 |
| **4** | 複数選択 + 絞り込み | 規模が大きいシーンでの実用性 | 未着手 |

段階1と2は独立して価値があるので、ここで一度実機確認を挟める。

> **M0052で実装した範囲**: `SceneInspectorComponentGroup` / `BuildGroups` によるComponent単位の
> グルーピング、`Expander`による展開/折りたたみ、`InspectorExpansionKey`による展開状態の保持、
> `InspectorRefreshPolicy`+`InspectorContentSignature`による再構築の冪等化、追加直後のComponentの
> 自動展開(`InspectorRevealPolicy`)。加えて実機フィードバックを受け、`SceneInspectorRow`
> (`SceneInspectorMemberRow`/`SceneInspectorNestedRow`)による**ネストした`ObjectCreation`値の
> 分解表示**(`new Vector3(1,2,3)`等をExpanderの子行として展開)も実装した。詳細は
> `Docs/Milestones/M0052-Component-Palette.md`。
>
> **分解編集はまだ含まれていない** — ネストの子行は`SceneValue.IsEditable`によらず常に読み取り専用。
> `SceneModelEditApplier`の適用経路がComponentのトップレベルMemberでしか名前一致を探さず、
> 見つからなければ黙って新規メンバーを生やしてしまうため、安全に対応するには
> `SetComponentMemberOperation`のメンバーパス拡張(または新規operation)と
> `SceneModelEditApplier`/`SceneSourceEditor`双方のパス対応が要る。実データがまだ無いことも
> あり、需要が出てから独立した段階として着手する。

---

## 今回やらないこと(意図的)

- **Component / メンバーの追加・削除UI**。`Component.Members`は「ソースに実際に書かれた
  メンバー」しか列挙しないため、追加は初期化子へ新しい要素を書く`SceneSourceEditor`側の
  仕事になる。UIより先に書き戻しの設計が要る。
- **`ConstantExpression`の編集**。`12f*MathF.PI/180f`の「意図」を壊さずに値だけ差し替えるのは
  一般には不可能。読み取り専用のまま、計算結果の併記で実用性を補う。
- **`FactoryCall`の引数編集**。段階1で表示だけ行い、編集は`SceneSourceEditor`に
  引数の書き換えを足してから。
- **プロパティウィンドウへの同時公開**。`ITrackSelection`で併存はできるが二重メンテになる。
  専用UIを選ぶなら片方に寄せる。

---

## 再利用するもの

| 既存 | 用途 |
|---|---|
| `SceneInspectorModel.cs`の型振り分け・`SceneInspectorValueFormat`・`SceneInspectorCommitPlanner` | そのまま(木構造化で呼び出し方だけ変わる) |
| `SceneInspectorCommitPlanner`が`OriginalValueExpression`をnullで渡す判断 | **維持**。絶対値入力に差分リベースは誤り |
| `EditTransactionController` | ドラッグスクラブの1ドラッグ=1Undo単位 |
| HierarchyのSVGアイコン + フォントサイズ追従 | Componentアイコン |
| `HierarchyContentSignature`と同型の署名 | 再構築の冪等化 |
| `SceneSourceMarkerService` | ソースへの移動経路 |
