# M0040: ソースマーカーが再解析着地のたびに無関係な位置へ再スクロールする不具合の修正

## 目的

実機からスクリーンショット2枚で報告: シーンビューでオブジェクトを選択すると対応するC#ソースが
正しくマーク・スクロールされ画面上部に見えるが、ドラッグして編集を確定すると、その直後に
スクロールされて編集箇所が画面から見えなくなる。再スクロール先はファイルの1053行目付近(ほぼ末尾)
などの、対象ノードとは無関係な位置。再現条件はユーザー確認済みで「選択・編集するたびに毎回」
——100%決定論的に再現する不具合だった。

## 根本原因

`CSharpGameEngineViewerPackage.cs`のソースマーカー更新経路のうち、`OnSceneModelChangedForSourceMarkers`
(`SceneEditorHub.SceneModelChanged`——編集確定後のRoslyn再解析が着地するたびに毎回発火——の
購読先)だけが、Hierarchyパネル自身が最後に発行した選択(`SceneEditorHub.HierarchySelection`、
`PublishHierarchySelection`経由でHierarchy起点のクリックでしか更新されない静的フィールド)を
参照していた。

ビューポートでオブジェクトを選択・ドラッグ編集しても`HierarchySelection`は一切更新されない
(`OnSelectionChangedForHierarchy`は`PublishSelectedNodePath`しか呼ばない)。そのため:

1. ビューポートで選択 → `OnSelectionChangedForHierarchy`が正しくマーク・スクロール。
2. ドラッグして確定 → 250ms+後、実際のRoslyn再解析が着地 → `SceneModelChanged`発火 →
   `OnSceneModelChangedForSourceMarkers`が、実機検証セッション中どこかで一度でも
   Hierarchyパネルをクリックしていれば、そのとき止まったままの古い(今の選択とは無関係な)
   `HierarchySelection.Paths`でマーカーを上書きし、無関係な位置へ再スクロールする。

これが「選択・編集するたびに毎回」(再解析は編集のたびに必ず走る)「無関係な位置へ飛ぶ」
(セッション中最後にHierarchyパネルでクリックしたノードの位置)の両方を過不足なく説明する。
M0038/M0039とは無関係の、M0020/M0032時点から存在していた既存不具合(Hierarchy起点の再マークと
Viewport起点の再マークで、異なる「今の選択」の参照元を使っていた非対称性)。

## 実装したこと

`OnSceneModelChangedForSourceMarkers`を、Hierarchy固有の`HierarchySelection`ではなく**統一選択
サービス(`SceneEditorSessionService.Selection.CurrentSelection`)から今の選択を都度解決する**
よう修正した。解決ロジック(`NodeIds`→pin中snapshotの`Projection`→`NodePath`)は
`OnSelectionChangedForHierarchy`が既に持っていたのと同じなので、
`HierarchySourceSync.cs`へ`SceneSelectionPathResolver.Resolve(SceneSelection?,SceneKey,
SceneProjection?)`という純粋関数として切り出し、両方の呼び出し元から使うようにした
(重複排除、かつ将来また非対称にならないようにする)。

`Model`引数(`SourceMarkerService.Update`の第1引数)は既存の`OnSelectionChangedForHierarchy`と
同じ流儀で`SceneEditorHub.ActiveSceneModel`のまま使う。`Paths`はpin中snapshotの`Projection`
(1ティック遅れうる)から解決するが、`EditorNodeId`はセッション寿命で安定でNodePathは構造編集が
無ければ再解析を跨いでも一致するため、通常のTransform編集では問題にならない(既存の他経路も
同じ前提で動いている)。

## Evidence

`SceneSelectionPathResolver`は`SceneSelection`/`SceneProjection`/`EditorNodeId`/`NodePath`という
VS SDK非依存の型だけで完結する純粋関数のため、新規`SceneSelectionPathResolverTests.cs`(6件)で
ユニットテストした: 選択なし・Projectionなし・SceneKey不一致(別Scene切替後の残骸)のいずれも
空リストを返すこと、解決可能なNodeIdだけをNodePathへ変換し解決不能なIDはスキップすること、
複数選択で入力順を保持すること。ソリューション全体1043/1043(既存1037+新規6、リグレッションなし)。
`CSharpGameEngineViewerPackage`内の実際の配線(イベント購読・呼び出し)はVS SDK依存のため
既存方針どおりユニットテスト対象外。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | ビューポートでオブジェクトを選択 → 対応するソースが正しくマークされ上部に見える |
| 2 | Move/Rotate/Scaleでドラッグして確定 → 再解析が着地した後も**マーク位置が動かず**、編集した箇所がそのまま見え続ける(今回の主目的) |
| 3 | 編集直後に無関係な行(ファイル末尾付近等)へ飛ばない |
| 4 | Hierarchyパネルでクリックしたときのマーク・スクロールは従来どおり動く(回帰確認) |
| 5 | Hierarchy選択 → ビューポート選択 → 編集、の順で操作しても正しい位置がマークされ続ける |
