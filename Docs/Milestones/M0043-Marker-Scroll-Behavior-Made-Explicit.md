# M0043: 再解析ではソースマーカーを絶対にスクロールさせない

## 目的

[[M0042-Non-Content-Node-Picking-Exclusion]]でピッキング対象の誤りは解消し、選択自体は正しく
なった(実機で`選択: SM_Cylinder6(26)`、マーク範囲も正しく498-515行)。しかし**ギズモで
ドラッグしてY座標を変えると、マークされた部位がスクロールで見えなくなる**症状が残っていた。
飛び先はM0042前は「末尾行」、M0042後は「1行目」へ変わった。

ユーザーの要望は一貫して明確: **編集しても、そのときのスクロール位置を維持してほしい。**

## 方針転換の理由

この症状に対して[[M0040-Source-Marker-Rescroll-Fix]](Hierarchy専用選択の誤参照)、
[[M0041-View-Position-Scroll-Conflict-Fix]](ビューポート先頭行の保存・復元)、
[[M0042-Non-Content-Node-Picking-Exclusion]](メッシュ無しノードの誤ピッキング)と3回修正を
入れ、**いずれも実在するバグではあったがこの症状の決定打ではなかった**。飛び先が毎回変わって
いることからも複数の要因が絡んでおり、今回も「なぜ飛び先が1行目なのか」を静的解析だけで
断定することはできなかった。

そこで**原因を1つに特定して潰す方針をやめ、ユーザーの要件そのものをコードの構造として保証する**
方針へ切り替えた。どの過渡状態が引き金であっても、再解析経路からはスクロールが起きえない形にする。

## 判定ロジックの構造的な欠陥(確認済みの事実)

修正前の`SceneSourceMarkerService.Update`:

```csharp
// 選択が実際に変わったときだけ先頭行へスクロールする。SceneModelChangedは再解析のたびに
// 同じ選択で呼ぶため、無条件にスクロールするとタイプ中に毎回ビューが飛んでしまう。
bool SelectionChanged=BufferChanged||!this.LastScrolledPaths.SequenceEqual(Paths);
```

コメントの意図(「選択が実際に変わったときだけ」)は正しいが、実装がそれを保証していなかった。
`SelectionChanged`は**選択の変化とは無関係な条件でtrueになる**:

- `BufferChanged=!ReferenceEquals(this.MarkedBuffer,Buffer)`は、`MarkedBuffer`が`null`のときも
  必ずtrueになる。`Clear()`は`MarkedBuffer=null`にする。
- `Clear()`は**編集サイクル中の過渡状態で普通に走る**: `Model?.Root is null||Model.HasErrors`
  (書き戻し直後の一瞬、Roslynが解析エラーを見る)、文書が開いていない、
  `OnSelectionChangedForHierarchy`のpin不一致経路が呼ぶ`Update(Model:null,Paths:[])`。

つまり**編集中に一度でも過渡状態を経由すると、その次の正常な再解析が「選択が変わった」と
誤認してスクロールする**。飛び先はそのときの`Ranges[0].Span.Start`次第で、過渡状態の内容に
よって変わる——実機で飛び先が毎回違った事実とも整合する。

## 実装したこと

**スクロールするかどうかを`Update`内部の推測に委ねるのをやめ、呼び出し側が明示する**方式へ
変更した。

```csharp
internal enum SceneSourceMarkerScrollBehavior {
    /// <summary>ユーザーが選択を変えた。マーク先頭をビュー最上部へスクロールしてよい。</summary>
    ScrollToSelection,
    /// <summary>再解析でマーク位置を貼り直すだけ。スクロール位置には一切触れない。</summary>
    PreserveScroll,
}
```

`SceneSourceMarkerScrollPolicy.ShouldScroll(Behavior,MarkerRangeCount)`(純粋static、既存の
`SceneDesignerUndoCommandPolicy`/`MarqueeSelectionPolicy`/`SceneSelectionPathResolver`と
同じ流儀)へ判定を委譲し、`Update`はBehavior引数を受け取るようにした。`LastScrolledPaths`
フィールドは判定に使わなくなったので削除。`BufferChanged`相当の判定はタグ再構築にのみ必要な
ので残した(スクロール判定からは完全に切り離した)。

呼び出し側(`CSharpGameEngineViewerPackage.cs`):

| 呼び出し元 | 意味 | Behavior |
|---|---|---|
| `OnHierarchySelectionChanged` | Hierarchyパネルでのクリック | `ScrollToSelection` |
| `OnSelectionChangedForHierarchy` | Viewport/Inspector起点の選択変更 | `ScrollToSelection` |
| `OnSelectionChangedForHierarchy`のpin不一致経路 | マーカーを消すだけ | `PreserveScroll` |
| **`OnSceneModelChangedForSourceMarkers`** | **再解析の着地** | **`PreserveScroll`** |

最後の1行が今回の要件そのもの。編集→書き戻し→再解析という経路は`PreserveScroll`しか通らない
ため、**過渡状態が何であってもスクロールは起きえない**。

選択変更経路は`SceneSelectionService.Select`が`SameSemanticSelection`で意味的に同一な選択を
既に弾いているため、`LastScrolledPaths`による重複抑制を失っても同じ選択で繰り返しスクロール
することはない。

### 受け入れる挙動変更

「Hierarchyで選択した状態で対象文書を後から開く」と、従来は`BufferChanged=true`によって自動
スクロールしていたが、今後はマークされるだけでスクロールしない。ファイルを開いた瞬間にビューが
飛ばなくなるので望ましい方向であり、意図的な「その場所へ飛ぶ」操作はHierarchyのダブルクリック
(`Navigate`、caretを明示的に動かす別経路)が引き続き担う。

## Evidence

新規`SceneSourceMarkerScrollPolicyTests.cs`(6件)。中心は
`ShouldScroll_PreserveScroll_IsAlwaysFalse`——マーク範囲の有無にかかわらず再解析経路からは
スクロールが起きないことを固定する。`SceneSourceMarkerService`本体・
`CSharpGameEngineViewerPackage`の配線はVS SDK(`IVsTextManager`/`IWpfTextView`/RDT)依存の
ため既存方針どおりユニットテスト対象外。ソリューション全体1054/1054(既存1048+新規6、
リグレッションなし)。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | SM_Cylinder6を選択 → ソースがマークされ、その位置へスクロールする(従来どおり) |
| 2 | **ギズモでドラッグしてY座標を変えても、コードエディタのスクロール位置が一切動かない**(今回の主目的) |
| 3 | 続けて何度ドラッグしてもスクロールしない |
| 4 | 別のノードを選択し直すと、正しくそのノードの位置へスクロールする(回帰確認) |
| 5 | Hierarchyパネルでのクリックでもスクロールする(回帰確認) |
| 6 | Hierarchyパネルのダブルクリックでのジャンプ(`Navigate`)が従来どおり動く(回帰確認) |
| 7 | コードエディタで直接タイプしている間、ビューが飛ばない(回帰確認) |
