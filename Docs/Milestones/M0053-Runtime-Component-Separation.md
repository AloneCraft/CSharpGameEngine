# M0053: 実行時状態のComponent分離と、パレット追加時の既定値埋め

## 目的

M0052のComponentパレットを実機確認したところ、追加したComponentがInspectorで「解析待ち…」のまま
値を1つも編集できなかった。原因を追うと2つの別々の問題が重なっていた。

1. **`TransformSync`のようなECS System構造体がパレットに紛れ込んでいた。** `ISystem`を実装する
   structは形の上では非nullable structなのでComponentの判定を通ってしまうが、per-Entityの状態を
   持たずフィールドも無いため、追加しても展開する中身が永遠に空になる。
2. **`AudioListener`のような正規のComponentも、追加時に`new T()`しか書かないため空だった。**
   `SceneComponent.Members`は「ソースに実際に書かれた代入」しか列挙しないので、
   保存しても何も現れない。

2を直そうとして「全フィールドに`default`を撒く」を検討した結果、より根本的な設計問題に行き着いた。

## 決定事項(ユーザー承認済み)

- **既定値はコンストラクタで表現する。`required`は採用しない。**
- **エンジンが実行時に埋めるフィールドは別のComponent構造体へ分離する**(属性ではなく型で表す)。
- **設計を先に確定させ、一括で実装する。**
- **パレットが撒く既定値は、パラメータなしコンストラクタの値を読んで写す。**
- 互換性は考慮不要(フレームワーク設計段階)。

設計判断の根拠は [[ADR-0044-Runtime-Component-Separation]] に記録した。

## 調査で確定した事実

- **Sceneで実際に宣言されているComponent**: `AppHost`/`Level`/`Transform`/`StaticMesh`/
  `PrimitiveMesh`/`DirectionalLight`/`AmbientLight`/`HeightFog`/`PlayerStart`/`Camera`のみ。
  `Renderable`/`FramePresent`/`PointLight`/`SpotLight`/`AudioSource`/`AudioListener`は
  **一度も宣言されておらず**、`State.World.Create()`で実行時に作られるだけだった。
  当初「全フィールドが実行時状態の型をどう割るか」が難所だと見ていたが、
  そもそも作者が書く型ではないと分かり、非genericの`IRuntimeComponent`を実装させるだけで済んだ。
- **Scene宣言は実行時フィールドを一切書いていない**(`Renderer=`/`Window=`/`Voice=`/`Loaded=`/
  `ObjectIndex=`のgrepが0件)。**`FirstPersonStageScene.cs`(1056行)は無変更で済んだ。**
- `AmbientLight`/`HeightFog`にも`Renderer`があった(当初の想定より対象が2型多い)。
- `Level.Path`/`Sun`/`PointLight`/`SpotLight`/`Listener`/`Source`は**宣言側でも実装側でも
  一切使われていない死んだフィールド**だったので削除した。`Level`の作者面は`ShaderPath`だけになった。
- **Archetype同定は完全一致**(`GameSystemModel.FindArchetype`)、**Systemディスパッチは部分集合一致**
  (`GameSystemLoopEmitter`の`(Archetype.Mask&MethodMask)!=MethodMask`)。この組み合わせにより、
  随伴Componentを自動付与しても既存Systemのクエリは壊れず、実行時状態を読むSystemだけが
  `ref`を1つ増やせばよい。

## 実装

### 1. マーカーインターフェース

`src/CSharpGameEngine.Entities/IRuntimeComponent.cs`(新規)。generic版は随伴先を宣言し、
非generic版は「単独で生成されるだけの実行時Component」を表す。
判定は`SceneTypePredicates.ImplementsRuntimeComponentInterface`/`TryGetAuthoredComponentType`。

**generic判定は完全修飾名の文字列比較では不可**。実装側では型引数が埋まる
(`IRuntimeComponent<DirectionalLight>`)ので定義側の名前と一致しない。
`Interface.OriginalDefinition`を`SymbolEqualityComparer`で比較する。

### 2. Archetype自動付与

`SceneInstantiateEmitter.BuildEffectiveComponents`へ`SceneRuntimeComponentMap`を**引数で**渡す。
`SceneComponentSetCollector.Collect`と`EmitNode`の両方が同じインスタンスを受け取る形になるので、
「1箇所を共有する」不変条件がむしろ強まった(渡し忘れがコンパイルエラーになる)。

導出順は「宣言順の各作者Component → その直後に随伴実行時Component → 末尾に`Parent`」。
初期値式は`null`ではなく`new {RuntimeFqn}()`を明示する(`SceneNodeTag`が`null`なのは空structだから。
実行時Componentは将来コンストラクタで既定値を持ちうる)。

対応表は`GameSystemGenerator`の4本目のプロバイダとして`(AuthoredFqn,RuntimeFqn)`の文字列ペアで流す。

### 3. 分離した型(`UserEcsProject/Components.cs`)

`AppHostRuntime{Window}` / `LevelRuntime{Renderer,AudioEngine,Loaded,RenderCamera}` /
`AmbientLightRuntime{Renderer}` / `HeightFogRuntime{Renderer}` / `DirectionalLightRuntime{Renderer}` /
`PointLightRuntime{Renderer}` / `SpotLightRuntime{Renderer}` / `AudioSourceRuntime{Voice}`。

形が同じでも共有しない(1対1であり、かつRootが`AmbientLight`と`HeightFog`を同時に持つため
共有型だと1枠に潰れる)。`Renderable`/`FramePresent`は分割せず非generic`IRuntimeComponent`を
実装させ、`[SceneComponentIcon]`を外してパレットから消した。

### 4. パレットの既定値埋め

`SceneComponentInitializerPolicy`(新規、純粋関数)が`new T { A=default, B=default }`を組み立てる。
パレット表示用の式はシンボルが生きている`SceneComponentCatalog.CreateEntry`で1回だけ作り、
`CreationExpression`として文字列で持ち回る(`ComponentPaletteEntry`が`INamedTypeSymbol`を
持たない既存方針を維持)。実際に追加するときは`CreatePlan`が式とメモリ内メンバーを同時に作る
(下記「実機報告による追加修正」参照)。

**コンストラクタ既定値を潰さないのが最重要**: `Transform`は`RotationW=1f,ScaleX/Y/Z=1f`を
コンストラクタで設定しているので、素朴に全フィールドへ`default`を撒くと回転・スケールが0になる。
パラメータなしコンストラクタ本体の`this.X=<定数>;`の並びを構文で読み、そのメンバーだけは
コンストラクタの式をそのまま書く。単純代入の並びとして読めないコンストラクタは解釈せず
`new T()`へ安全に縮退する(既定値を取り違えるくらいなら何も書かない)。

## 実機報告による追加修正(3件のバグ + 既定値整備)

### 1. ドロップするとノードの選択が外れる

`SceneProjectionReconciler`の同一性照合キーは「ノード名 + **Component型名の連結**」なので、
Componentが1つ増えただけでキーが変わり、対象Node(と配下)へ**新しい`EditorNodeId`が振られていた**。
選択・Reveal・未書き出し履歴が持つIDが宙に浮き、`BuildGroups`の`Projection.TryGetNode`が失敗して
Inspectorが空表示へ落ちる。コードエディタの選択マーカーも同時に消えていた。

`Reparent`だけは`PreserveIdentityAfterMove`でIDを保護していたが、Component増減は対象外だった。
同関数を`PreserveNodeIdentity`へ改名し、**Pathが変わらない操作(`AddComponent`/`RemoveComponent`)
でも元のIDを引き継ぐ**ようにした。回帰テストは追加・削除それぞれ1本。

### 2. 追加した直後は「解析待ち…」のまま編集できない

`TryApplyAddComponent`がメモリ内`SceneComponent`を常に`Members`空で作っていた。原因は
`AddComponentOperation`が文字列2つしか運ばず、適用側に型シンボルが無かったこと —
`SceneValue.Type`がnullだと`DetermineEditorKind`が全項目を読み取り専用に落とすため、
「名前だけ並べても編集できない」ので空にしていた。**設計上の必然ではなく実装の限界だった。**

`HandleComponentAddAsync`は非同期なので、`SceneWorkspaceService.ResolveComponentTypeAsync`で
対象Projectの`Compilation`から型シンボルを引けるようにした。`SceneComponentInitializerPolicy.CreatePlan`が
**書き込む式とメモリ内メンバーを同じ列挙から**作り、`AddComponentOperation.Members`で運ぶ。
シンボルを解決できなければ従来どおり空へ縮退する。

閉じたgeneric instantiation(`StatusEffect<PoisonKind>`)は`GetTypeByMetadataName`では引けない
(カタログが`Construct`で組み立てた型なので)ため、`SceneComponentCatalog.Build`の結果から探す。

**予測と真実のズレを防ぐ契約テスト**を追加した
(`ModelApply_AddComponent_PredictedMembersMatchTheReanalyzedTruth`)。先回りしてメンバーを載せる以上、
保存して再解析した結果とメンバー名・並び・ソース式・編集可否がすべて一致することを固定する —
ここがズレると「編集したのに保存すると値が違う」という最悪の壊れ方をする。

### 3. 既定値コンストラクタの整備

「追加した瞬間に見える/聞こえる」値を持たせた(`Transform`が既に確立していたパターンの横展開)。

| 型 | 既定値 |
|---|---|
| `AppHost` | `Title="C# Game Engine"`, 1280x720 |
| `AmbientLight` | 白・`Intensity=1` |
| `HeightFog` | 薄い灰青、`Density=0.02` |
| `DirectionalLight` | 真上から白、`Intensity=1` |
| `PointLight` | 白、`Intensity=1`, `Range=10` |
| `SpotLight` | 真下向き、白、`Range=10`, 円錐角0.3/0.6 |
| `AudioSource` | `Volume=1` |

方向ベクトルやRangeが零のままだと「追加したのに何も起きない」ため不具合に見える。
`Camera`は既に完備していた(`Active=true`, `FovY=π/4`, `NearZ=0.1`, `FarZ=300`)。

### 4. シーン デザイナーで開いているのにHierarchyが空になる

実機報告。ビューポートには正しく描画されているのに、Hierarchyだけが
「IScene を宣言する C# ファイルを開いてください。」になる。決め手はコードエディタのアクティブなタブが
`FirstPersonStageAssets.cs`(`[SceneAssets]`のみで`IScene`を宣言しない)だったこと。

`SceneWorkspaceService`は**文書切替のたびに無条件で`ModelChanged(null)`を発行する**
(`SceneWorkspaceService.cs:341`)。門番の`SceneHubPublicationPolicy.ShouldPublishAnalyzedModel`は
先頭が`if(!PinnedDocumentHasUnsavedEdits||...){ return true; }`だったため、
**pin中でも未編集ならnullが素通り**してHubが空になっていた。nullを塞ぐ分岐はその下にあり、
「未保存の編集があるとき」しか効いていなかった。

**判断材料を「未保存の編集の有無」にしたのが誤り。** 表示を消してよいかは「pin中かどうか」で
決まり、編集の有無とは無関係だった。M0051 Step 1-4が直したかったのは
「未保存の編集が再解析で上書きされる」という別の問題で、その修正のついでにnull発行の門番を
同じフラグへぶら下げてしまったのが取り違えの実体。

判定の軸を**pin中Sceneの表示を守る**へ組み替えた:

| 状況 | 判定 |
|---|---|
| 未pin | 流す(Sceneファイルを開いただけでHierarchyに出る入口を維持) |
| pin中 + 解析対象がnull | **流さない**(今回の修正の本体。編集の有無は見ない) |
| pin中 + 別Scene | 流さない(HierarchyとデザイナーがべつのSceneを指す状態を作らない) |
| pin中 + 同一Scene | 未保存の編集があるときだけ塞ぐ(M0051 Step 1-4の維持) |

既存テストが不具合を期待動作として固定していた(`NullAnalyzedModel_IsPublishedWhenThereAreNoUnsavedEdits`)
ので意味ごと反転させ、`NullAnalyzedModel_NeverClearsAPinnedSceneEvenWithoutUnsavedEdits`が
直接の回帰テストになる。`AnalyzedModel_IsNotPublishedOverUnsavedEditsOfTheSameScene`
(M0051 Step 1-4の回帰防止)は手を触れていない。

`ClearPin()`の呼び出し元はシーン デザイナー内の明示的な操作3箇所だけでタブ切替では呼ばれないため、
「シーン デザイナーを閉じたらHierarchyも空になる」はこの規則でも保たれる。

## Known deviations

- **`Renderer is null`の判別ロジックは消えていない。** 随伴付与により作者側Entityも
  `*Runtime`(`Renderer=null`)を持つため、`LevelLoad`の判別は`Runtime.Renderer is null`へ
  移っただけ。この分離で得たのは編集面の正しさであり、実行時ロジックの単純化ではない。
- **型シンボルを解決できないときだけ「解析待ち…」へ縮退する**(Compilationが取れない、
  カタログに無い型を要求された等)。通常はメモリ内Componentへ実体のメンバーが載るので、
  追加した直後から編集できる。
- 書き込み可能メンバーを持たない型(マーカー型)は従来どおり`new T()`。これは正しい状態なので
  特別なメッセージは出さない。

## Evidence

全1325件成功(VSExtension 818件、CodeAnalysis 104件、SceneModel 114件)。
ソリューション全体ビルドは0警告0エラー。生成コードで随伴付与を確認済み:

```csharp
// MainScene.Scene.g.cs
Archetype8.Create(ref World,
    new global::UserEcsProject.AppHost() { Title = "…", Width = 1280, Height = 720 },
    new global::UserEcsProject.AppHostRuntime(),
    new global::UserEcsProject.Level() { ShaderPath = "Shaders/Stage.hlsl" },
    new global::UserEcsProject.LevelRuntime())
```

Component型は18→20種。`MaxComponentTypes=64`(SYSGEN026)に対して十分な余裕がある。

## Next action(実機確認)

- パレットに`Renderable`/`FramePresent`/`*Runtime`/`*Sync`(ISystem)が**出ない**こと
- 型を追加して保存すると、Inspectorに**全作者フィールドが編集可能な状態で**並ぶこと
- **`Transform`を追加したとき`RotationW=1f`と`Scale=1f`が保たれること**(0になっていないこと)
- 実行時Componentを手で書くとSCENE119が出ること
- **`UserEcsProject`が起動して従来どおり描画・音響が動くこと**(Archetype構成が変わったため、
  ライト・フォグ・メッシュ・Presentの全経路を目視する)
