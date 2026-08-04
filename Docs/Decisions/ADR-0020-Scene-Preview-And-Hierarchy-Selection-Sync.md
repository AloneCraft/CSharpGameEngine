# ADR-0020: シーンのダブルクリックプレビューとヒエラルキー(選択連動)

## ステータス
承認済み

## 背景

`Docs/EditorSpec.md` は「Hierarchyツールウィンドウ」をPhase 1として既に設計していたが、その設計は
「パッケージロード時に`*.scene.json`を走査して表示する読み取り専用ツリー」であり、ダブルクリック
起点でも選択連動でもなかった。ユーザーからの実際の要求は次の2点で、Phase 1の設計と異なる:

1. ソリューションエクスプローラーでシーンファイルをダブルクリックすると、3Dモデルプレビュー
   ([[ADR-0013-Asset-Preview-Editor-Factory]])と同じ仕組みで「シーン」プレビュードキュメント
   ウィンドウが開く。
2. 新規「ヒエラルキー」ツールウィンドウでノードをクリックすると、対応するオブジェクトが
   「シーン」プレビュー側でハイライトされる(Unityのヒエラルキーの基本操作感)。

対象ファイルは、既存のUE移植ステージ(`scene_meta.json`+`.glb`、M0004、
[[ADR-0017-UE-Level-Export-Pipeline]])ではなく、`Docs/EditorSpec.md` §3で設計済みの汎用
`*.scene.json`(`SceneNode`: Id/Name/ParentId/Transform/MeshAssetPath/Components)を新規実装する
方針とした(ユーザー確認済み)。M0004のステージパイプライン(`SceneDocument`/`SceneLoader`/
`Lvl_FirstPerson.glb`)には一切手を加えていない。

また、この機能は`.glb`/`.gltf`ファイルを読む必要があるため、[[ADR-0019-VSIX-Gltf-OutOfProcess-Bridge]]
(本ADRの直前に実施した、VSIXのglTF読み込みをFBXと同じアウトオブプロセス方式に統一するリファ
クタリング)を前提にしている。

## 決定事項

### 1. `src/CSharpGameEngine.Scene`(新規、net10.0)にシーンの正規モデルを置く
`Transform`(位置/回転/スケール、`System.Numerics`)、`SceneNode`(Id/Name/ParentId/Transform/
MeshAssetPath/Components、`Id`/`ParentId`は`CSharpGameEngine.Core.StableId`)、`SceneGraph`
(ノードの平坦なリスト+JSON往復+`BuildTree()`)、`SceneNodeTree`/`SceneNodeTreeItem`(親子解決済みの
木)を実装した。型名は仕様書の`Scene`ではなく`SceneGraph`とした - `CSharpGameEngine.Scene`という
名前空間の末尾セグメントと同名の型は紛らわしいため。

JSON形式は`Docs/EditorSpec.md` §3のスキーマ例(`schemaVersion`/`nodes[].{id,name,parentId,
transform:{position,rotation,scale},meshAssetPath,components}`)と完全一致させ、内部DTO
(`SceneJsonDocument`/`SceneJsonNode`/`SceneJsonTransform`)経由で変換する(`StableId`/`Vector3`/
`Quaternion`用のカスタム`JsonConverter`は書かず、素の文字列/`float[]`との相互変換を明示的に書く
方が単純だと判断した)。

`SceneNodeTree.Build`は循環参照・解決不能な親参照を検出して例外を投げる。各ノードの親は
高々1つなので、真の循環(親を辿ると自分自身に戻る)を構成するノード群は構造上どのノードも
「ルート」になり得ず、ルートから辿るツリー構築処理がそもそもそこへ到達しない - そのため
「到達済みノード数 != 全ノード数」を検出条件として使う(個々のノードを訪問中フラグで
ガードする必要がない、この構築アルゴリズム固有の性質)。

`tests/CSharpGameEngine.Scene.Tests`: JSON往復(仕様書のサンプルJSONそのもの)・親子解決・
複数ルート・循環参照検出・実際に同梱した`UserGameProject/DefaultScene.scene.json`の読み込み、
計8件。

### 2. net472のVSExtensionは`CSharpGameEngine.Scene`を参照せず、独立した薄いパーサーを持つ
[[project-ecs-net472-boundary]]で確認済みの通り、net10.0専用プロジェクトはnet472の
`CSharpGameEngine.VSExtension`から`ProjectReference`できない(TFM非互換で参照自体が失敗する、
`CSharpGameEngine.Ecs`固有の問題ではなく一般的な制約)。そのため`tools/CSharpGameEngine.VSExtension/
SceneFile.cs`に、同じ`*.scene.json`スキーマを読む**独立した**`SceneFileNode`/`SceneFile`/
`SceneFileNodeTreeItem`を実装した(`Id`/`ParentId`は`StableId`ではなく素の`Guid`)。既存の
`SceneDocument.cs`(M0004用DTO)と同じ「VSIX側は薄い専用DTOを持つ」慣習に合わせている。
`SceneFile`は`BuildTree()`に加えて`ComputeWorldTransforms()`(親チェーンを辿ってワールド変換を
解決、`BuildTree`とは独立に循環参照をガードする「訪問中」集合を持つ - レンダリング経路は
`BuildTree`を経由するとは限らないため)を持つ。

`tests/CSharpGameEngine.VSExtension.Tests/SceneFileTests.cs`は`CSharpGameEngine.Scene.Tests`と
**同一のサンプルJSON文字列**を使い、2つの独立実装が同じ入力に対して食い違わないことを確認する。

### 3. ダブルクリックプレビューは`GlbFbxPreviewEditorFactory`と同型の`IVsEditorFactory`
`SceneFileEditorFactory`(`IVsEditorFactory`)を`*.scene.json`拡張子に対して登録した:
```
[ProvideEditorFactory(typeof(SceneFileEditorFactory), 111, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(SceneFileEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(SceneFileEditorFactory), ".scene.json", 50)]
```
`SceneWindowPane`(`WindowPane`+`IVsPersistDocData`+`IPersistFileFormat`、読み取り専用)・
`SceneWindowControl`(WPF、`SceneHwndHost`を1つ載せるだけ)は、[[ADR-0013-Asset-Preview-Editor-Factory]]の
`AssetPreviewWindowPane`/`AssetPreviewControl`と構造的に同一。ドキュメントタブの表示名はVS標準の
ファイル名表示のまま(`pbstrEditorCaption`は空文字)、エラーダイアログ等の表示名文字列のみ
「シーン」/"C# Game Engine Scene"とした。

**未検証のリスク**: `.scene.json`という複合(ドット2つ)拡張子が`ProvideEditorExtension`で実際に
VS側で単一の拡張子として認識されるかは、実験用インスタンスでの実地確認をまだ行っていない
(ADR-0013自身も単一拡張子ですら「選択だけでは開かない」という未確認事項を残している)。もし
複合拡張子が期待通り機能しない場合のフォールバックは、`.json`全般を登録した上でファイル内容
(`schemaVersion`+`nodes`プロパティの有無)で判定する方式に切り替えること。

### 4. `SceneRenderer`は`VorticeRenderer`を改修せず、新規の複数オブジェクト対応クラスとして追加
既存の`VorticeRenderer`(単一メッシュ、`ViewerHwndHost`/`AssetPreviewHwndHost`が使用中)は
無改修のまま維持した。新規`SceneRenderer`はオブジェクトごとに頂点/インデックスバッファを
持ち、定数バッファは256バイトアライン済みスロットをオブジェクト数分持つ1つのアップロード
バッファ(`TransformConstants`に`Mvp`/`BaseColorFactor`に加えて`Selected`(float)を追加)。
カメラのバウンディングスフィアは全オブジェクトのワールド空間頂点から算出する。

選択ハイライトは`SetSelectedNode(Guid? nodeId)`が該当オブジェクトの`Selected`定数を1に
設定するだけの単純なタイント合成(`PSMain`で`shaded + Selected * SelectionTint`、
`SelectionTint = (0.6, 0.35, 0.05)`のオレンジ系加算)。移動/回転/スケールハンドルを伴う
ギズモ操作ではないため、`Docs/EditorSpec.md` §6の「ビューポート内ギズモ操作」スコープ外
条項には抵触しない。

既存のシングルメッシュ経路(`ViewerHwndHost`/`AssetPreviewHwndHost`)への回帰リスクを避ける
ため、意図的に別クラスとして追加した(将来的な`VorticeRenderer`との統合はEditorSpec Phase 2
まで持ち越す)。

### 5. `SceneEditorHub`が「シーン」プレビューと「ヒエラルキー」を仲介する
`SceneEditorHub`(static、`SceneOpened`/`SelectedNodeChanged`イベント+直近シーンのキャッシュ)が、
`SceneHwndHost`(シーンを読み込んだら`PublishSceneOpened`を呼ぶ)と`HierarchyToolWindowControl`
(`SceneOpened`を購読してツリーを再構築、`TreeView.SelectedItemChanged`で選択ノードIdを
`PublishSelection`する)を疎結合に繋ぐ。選択連動は**一方向のみ**(ヒエラルキー→シーンの
ハイライト)。シーン側オブジェクトをクリックしてヒエラルキーの選択状態を変える逆方向は
実装していない(ユーザー確認済みのスコープ)。

複数シーンドキュメントが同時に開かれた場合、`SceneEditorHub.ActiveScene`は「直近に開かれた
ものが常に勝つ」(アクティブなドキュメントタブの追跡はしない)。現状リポジトリには
`DefaultScene.scene.json`が1つしかなく、`Docs/EditorSpec.md` §6の「複数シーン間遷移は
スコープ外」とも整合するため、既知の制約として許容した。

### 6. ヒエラルキーはパッケージロード時に常時表示、シーンオープンで内容が更新される
`HierarchyToolWindowPane`(`[ProvideToolWindow]`)は`CSharpGameEngineViewerPackage.InitializeAsync`
内で`ShowToolWindowAsync(..., create: true, ...)`により常時表示にした(Unityのヒエラルキーが
常にドッキングされている体感に合わせた)。ロード直後は空のツリーで、`*.scene.json`が開かれた
時点で内容が入る。手動での表示コマンド(`ShowHierarchyToolWindowCommand`、`ShowViewerToolWindowCommand`
と同型)も残した。

これはEditorSpec Phase 1の「パッケージロード時にシーンファイルを走査して表示」とは異なる設計
(本ADRでは「ドキュメントとして開かれたシーンに反応する」)だが、Unity的な体感によりよく
合致すると判断し、そちらを採用した。

### 7. `UserGameProject/DefaultScene.scene.json`(新規、動作確認用の最小シーン)
Root(メッシュ無し)+子ノード2つ(`Cube_A`: position [-1.5,0,0]、`Cube_B`: position [1.5,0,0])、
両方とも`UserGameProject/Assets/DefaultScene/Box.glb`(`assets/TestFixtures/Gltf/Box.glb`の
コピー)を参照する。EditorSpec Phase 1は「ルートのみの空シーン」を想定しているが、ダブル
クリックプレビューと選択ハイライトを実際に確認可能にするため意図的に2ノード追加した。
`UserGameProject.csproj`に`<None Include="DefaultScene.scene.json" />`を追加(ADR-0002の
「オーサリングされたSource」原則に倣い、`CopyToOutputDirectory`は付けない - VSIX側は
ソースツリーのパスを直接読むため)。`UserGameProject`側の`ProjectReference`
(`CSharpGameEngine.Scene`)は追加していない - ランタイム統合(EditorSpec Phase 4/Play)は
今回のスコープ外。

## 検証結果(実際に実行して確認)

- `dotnet test CSharpGameEngine.slnx`: 全9テストアセンブリ・196件成功(新規
  `CSharpGameEngine.Scene.Tests` 8件、`CSharpGameEngine.VSExtension.Tests`の追加分含む9件を含む)。
- `dotnet build CSharpGameEngine.slnx`: 警告・エラーなし(`TreatWarningsAsErrors=true`下)。
- `CSharpGameEngine.VSExtension`(net472、`.vsix`込み)のビルド成功を個別にも確認。

## 未解決・保留事項

- §3に記載の「複合拡張子`.scene.json`が`ProvideEditorExtension`で機能するか」は、VS実験用
  インスタンスでの実機確認(ダブルクリックで「シーン」ウィンドウが開く様子)がまだ行えていない。
  次回セッションで[[feedback-vsix-verification]]の手順(`NativeWindowHandle`経由の
  スクリーンショット)により確認する。
- ヒエラルキーでの選択→シーン側ハイライトの実際の見た目(オレンジ系タイントが視認できるか)も
  同様に実機未確認。
- `HierarchyToolWindowControl`の`TreeView`は`ItemsSource`の単純な差し替えのみで、選択状態の
  復元やスクロール位置の維持は行わない(シーン再読み込みのたびに選択が失われる) - 今回の
  スコープでは許容範囲と判断した。
