# M0042: メッシュ無しノード/SkySphereをピッキング対象から除外する

## 目的

M0041のスクロール修正を実機検証中、「ギズモで移動するとソースの先頭(Rootノードの宣言位置)が
表示される」という報告があった。調査の結果、これはM0040/M0041で扱った「マーカーの再スクロール」
バグとは別の、より根本的な原因だと判明した。あわせて、M0038計画時に保留していた別の実機報告
(「何もない空間をクリックしても選択解除されない」、原因は背景の`SM_SkySphere`がピッキング
対象に含まれていたこと)も同じ仕組みで解消した。

## 根本原因

`SceneModelRenderObjectBuilder.Build`はRootを含む全ノードに対して無条件で`SceneRenderObject`を
1つ作る。`StaticMesh`/`PrimitiveMesh`コンポーネントを持たないノード(Root自身や、構造整理用の
空グループノード)は`GizmoMeshFactory.Create()`が生成する**0.3ユニットの小さな立方体**を
代わりに描画する——これは今回に限らず、この一連の実機検証セッション全体でスクリーンショットに
写り続けていた「ギズモの中の小さなキューブ」そのものだった。

このプレースホルダーは`ScenePicker.TryPick`/`PickMarquee`が`CurrentRenderObjects`を無条件に
線形走査する対象に含まれており、クリック・ドラッグで選択できてしまう。Root(このシーンでは
`Components=[AmbientLight,HeightFog]`のみでTransform無し)や、Transformだけ持つ空の整理用
ノードは、ワールド原点付近(親が無い/Transform無しなら`Matrix4x4.Identity`)に描画されるため、
ステージの実オブジェクトのすぐ近くに紛れ込み、誤選択・誤ドラッグしやすい。誤って選択された
ノードの`Location.Span`はソース中のその宣言位置(Rootなら`Root=>new(){...}`の開始、ファイル
先頭付近)を指すため、ソースマーカーは「正しく」その位置へスクロールしていた——マーカー機構
自体(M0040/M0041)にはバグが無く、選択されているノードそのものが意図しないものだった。

これは「本来ユーザーが直接操作すべきでないオブジェクトが、通常のシーンコンテンツと同列に
ピッキング対象へ混ざっている」という点で、SkySphereの問題と同じ種類の穴だった。

## 実装したこと

`SceneRenderObject`(`SceneRenderer.cs`)へ、既存の`IncludeInBounds`(自動フレーミング用の除外
フラグ)と同じパターンで`IsPickable { get; init;}=true`を新設した。

`SceneModelRenderObjectBuilder.Build`での算出条件:

```csharp
bool HasDeclaredMesh=AssetPath is not null||PrimitiveMeshEnumMemberName is not null;
...
IsPickable=HasDeclaredMesh&&!IsSkyDomeAsset(AssetPath),
```

- メッシュ未宣言(Root・空グループノード): ピッキング対象外。実際のメッシュ解決が失敗して
  `GizmoMeshFactory`へフォールバックしたケース(`StaticMesh`/`PrimitiveMesh`は宣言されているが
  アセット解決に失敗)は、ユーザーが元々実コンテンツとして意図しているため対象外にしていない。
- SkySphere: 既存の`IsSkyDomeAsset`判定をそのまま再利用してピッキング対象外にした。

`IncludeInBounds`がたどる複製箇所と全く同じ4箇所で`IsPickable`も複製した(既存の確立済み
パターンをなぞるだけ): `SceneModelRenderObjectBuilder.cs`(算出・生成)、
`SceneIncrementalUpdate.cs`の`TryUpdateTransforms`(増分更新の複製)、`SceneHwndHost.cs`の
`TryStartDuplicatePreview`(複製プレビューの複製)、同じく`ApplyFinalTransformsToRenderObjects`
(コミット直後パッチの複製)。

`ScenePicker.cs`側は`TryPick`(単発クリック選択・Orbitアンカーの両方が使う)・`PickMarquee`
(矩形選択)で`IsPickable=false`のオブジェクトを`continue`でスキップし、`TryVertexSnapQuery`/
`TrySurfaceSnapQuery`は既存の`IsExcluded(Object.NodeId,ExcludedRoots)`チェックへ
`||!Object.IsPickable`を追加した(プレースホルダー立方体の頂点/面へスナップしても意味が無い
ため)。

Hierarchy経由の選択(`OnHierarchySelectionChanged`)は`ScenePicker`を一切経由しないため無影響
——メッシュを持たないノードもHierarchyパネルからは引き続き選択・編集できる(意図的)。
`TransformGizmo.TryHitTransform`(ギズモハンドル自体の当たり判定)も`SceneRenderObject`を
参照しない別系統のため無関係。

## Evidence

`ScenePicker`/`SceneModelRenderObjectBuilder`はGPU/HWND非依存の純粋関数のため、新規5件
(`ScenePickerTests.cs`に4件: `TryPick`/`PickMarquee`/`TryVertexSnapQuery`/
`TrySurfaceSnapQuery`それぞれの非ピッキング対象除外、`SceneModelRenderObjectBuilderTests.cs`に
1件: メッシュ未宣言ノードの除外、既存の`Build_SkySphereStaticMesh_IsExcludedFromAutomaticFraming`
も拡張)でユニットテストした。ソリューション全体1048/1048(既存1043+新規5、リグレッションなし)。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | 何もない空間(SkySphereの見えている部分を含む)をクリックすると選択が解除される(以前保留にしていた要望の解消) |
| 2 | ステージのどのオブジェクトも無い空間や、Rootノード付近をクリック・ドラッグしても何も選択されない・意図しない小さな立方体が動かない |
| 3 | 実際のステージオブジェクト(SM_CubeN等)はこれまでどおり選択・ドラッグできる |
| 4 | Hierarchyパネルからは、メッシュを持たないノード(整理用の空ノード等)も引き続き選択・編集できる |
| 5 | ギズモ移動後、ソースマーカーが正しく操作したオブジェクトの宣言位置を指し続ける(誤って別ノードを操作していないことの確認、M0041の検証項目と合わせて) |
