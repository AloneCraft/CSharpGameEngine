# M0010: Scene Authoring Phase A — C#コードをSceneの正本とする基盤

## 目的

ユーザーが貼り付けた仕様見本(`samples/SoaSystemsDemo/シーン構築見本.cs`)を実装の正本として扱い、
C#のScene定義コードをSceneの唯一の正本にする基盤を作る。同じScene定義を(1) Source Generatorが
`SceneNodeSymbol`実装と`CreateRuntime`(Entity生成コード)へ変換し、(2) 将来的にはHierarchy Docking
WindowがRoslynで同じコードを解析・編集する(Phase B以降)、という2用途で使えるようにする。設計判断は
[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]に記録した。

## 実装したこと

### 新ランタイム型 `src/CSharpGameEngine.Entities/`
- `SceneDefinitionAttribute`(`[AttributeUsage(AttributeTargets.Class)]`のみ)。
- `SceneNodeId`(`readonly record struct(ulong Value)`)、`SceneNodeSymbol`
  (`readonly record struct(SceneNodeId Id, string Name)`)。
- `SceneBuilder`/`SceneNodeBuilder`: `Define`をコンパイル可能にするためだけのAuthoring API。
  `Create`/`Set`の本体はほぼ空実装で、SceneDefinitionGeneratorが`Define`の構文木を静的解析する際は
  実行されない。
- `Parent`(`struct { Entity Value; }`) — Unity DOTSの`Unity.Transforms.Parent`と同じ位置づけの
  汎用Hierarchy基盤。既存Componentが全て消費側プロジェクトに置かれる慣習からの唯一の意図的な逸脱
  として`CSharpGameEngine.Entities`本体に追加(ユーザー確定)。
- `SceneNodeTag`(0フィールドのタグComponent) — `Set`呼び出しを持たない空ノードを
  `World.CreateArchetype`(0型引数オーバーロード無し)で成立させるために、実装中に必要性が判明し
  追加。
- `tests/CSharpGameEngine.Entities.Tests/SceneAuthoringSpec.cs`: 9件。

### `SceneDefinitionGenerator`(`src/CSharpGameEngine.CodeAnalysis/`)
- `SceneDefinitionDiagnostics`: `SCENE001`〜`009`(見本のSCENE001〜006・008・009を踏襲、
  見本のSCENE007「循環検出」はSCENE005が構造的に包含するため不採用、見本のSCENE010「削除時の
  被参照チェック」はPhase B以降の対象のため不採用、Phase A固有の`SCENE009`(8型引数上限超過)を
  新設)。
- `SceneDefinitionModel`: `ComponentSetModel`(Component型FQN+`Set`引数式のテキスト)、
  `SceneNodeModel`(プロパティ名・NodeId・子ノードの木構造)、`SceneDefinitionClassResult`
  (Namespace・ClassName・元ファイルのusingディレクティブ複製・Root・診断)。
- `SceneDefinitionAnalyzer`: `[SceneDefinition]`クラスの`partial SceneNodeSymbol`プロパティ一覧化、
  `Define`メソッドの構文木を`Create`(親子関係)/`Set`(Component)呼び出しのネストとして解析。
  レシーバー名(`Root.Create(...)`の`Root`)は意味解析ではなく現在のスコープ名とのテキスト一致のみで
  検証し、クロージャ経由で外側ノードのBuilderを誤って参照するケースも検出する。NodeIdは
  ノードを表すpartialプロパティの完全修飾シンボル名からFNV-1a 64bitハッシュとして決定論的に導出。
- `SceneDefinitionGenerator`: `ForAttributeWithMetadataName`、`.Collect()`は不使用(生成物は
  クラスごとに独立、solution横断の結合出力が不要なため)。
- `SceneNodeSymbolEmitter`: 各`private static partial SceneNodeSymbol X { get; }`の実装本体を生成。
- `SceneRuntimeEmitter`: `CreateRuntime(World world) : Entity`を生成。深さ優先でRoot→子の順に
  `World.CreateArchetype`+`CreateEntity`+`GetRef`呼び出しを組み立て、非Rootノードには`Parent`を、
  空ノードには`SceneNodeTag`を自動付与する。`Set`引数式は構文木からテキストとしてそのまま生成コード
  へ貼り付け、元ファイルのusingディレクティブも生成ファイル先頭へ複製することで
  `Vector3.One`のような非完全修飾参照も解決できるようにしている。
- `tests/CSharpGameEngine.CodeAnalysis.Tests/`: `SceneDefinitionGeneratorTestHelper.cs`、
  `SceneDefinitionGeneratorDiagnosticsTests.cs`(SCENE001〜009各1件+正常系1件、計11件)、
  `SceneDefinitionGeneratorEmissionTests.cs`(3件、テキスト形状+決定性)、
  `SceneDefinitionGeneratorEndToEndTests.cs`(2件、`CompileAndLoad`で実`World`上に構築・検証)。

### `samples/SceneAuthoringDemo/`(新規)
見本のRoot/MainCamera/Player構成を、`LocalTransform`ではなく`Position`/`Rotation`/`Scale`の
3分割`Set`呼び出しへ書き換えて再現。`DemoScenario.Run()`が`MainScene.CreateRuntime(world)`を実行し、
Rootが`SceneNodeTag`のみで`Parent`を持たないこと、MainCamera/Playerがそれぞれ`Parent.Value == Root`
を持つこと、各Component値がDefine本体のとおりであることを`EntityQuery`経由で検証して"OK"を返す
(`CreateRuntime`はRootのEntityしか返さないため、MainCamera/PlayerはそれぞれCamera/PlayerTagを
持つ唯一のArchetypeとしてQueryで見つける)。`SoaSystemsDemo`と同じ`EmitCompilerGeneratedFiles`設定
により、生成された`*.SceneNodes.g.cs`/`*.SceneRuntime.g.cs`を実ファイルとして確認できる。
`CSharpGameEngine.slnx`へ登録。

## 意図的に除外したこと

- VSIX側のRoslyn WorkspaceによるHierarchy Docking Windowのライブ編集(Add/Delete/Rename/Reparent)。
  このリポジトリに前例が無い新規領域であり、実機VSデバッグが必須な部分が多いため、次回以降の
  別タスクとする([[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]決定事項1)。
- JSON版Scene実装(`src/CSharpGameEngine.Scene`、`SceneFile.cs`等)の変更・置き換え。今回は
  無改修のまま並行して残す。
- `SceneNodeId`のRename間安定化(見本自身が明示的に未解決とした問題)。
- 8型引数を超えるノードへの`EntityCommandBuffer`経由の段階的Component追加。SCENE009で
  コンパイルエラーにするのみで、回避策は提供しない。

## 変更したFeature ID

- `F-L3-011`(新規) — Scene Authoring基盤型(`Parent`/`SceneNodeTag`/`SceneNodeSymbol`系)
- `F-L7-012`(新規) — `SceneDefinitionGenerator`(Analyzer/Model/Emitter)
- `F-L7-013`(新規) — `SceneDefinitionGenerator`診断(SCENE001〜009)
- `F-L9-006`(新規) — `samples/SceneAuthoringDemo`

## 実行したコマンドと結果

```
dotnet build src/CSharpGameEngine.Entities/CSharpGameEngine.Entities.csproj -c Debug
  → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.Entities.Tests/...csproj -c Debug
  → 成功、61件(SceneAuthoringSpec.cs 9件新規)

dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0(SceneDefinitionDiagnostics追加直後はAnalyzerReleases.Unshipped.md未更新で
    RS2000が9件発生、追記して解消)
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...csproj -c Debug
  → 成功、52件(SceneDefinitionGenerator関連16件新規、既存GameSystemGenerator関連36件は無影響)

dotnet run --project samples/SceneAuthoringDemo
  → "OK"

dotnet build CSharpGameEngine.slnx -c Debug / -c Release
  → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx
  → 成功、13プロジェクト・380件全成功
```

## リバースエンジニアリングした挙動

- `SceneDefinitionAnalyzer`のCreate/Setレシーバー名検証は、意味解析ではなく「現在のスコープ名
  (1つ外側のCreateラムダの引数名、最上位ではDefineのSceneBuilderパラメータ名)とのテキスト一致」
  のみで行う設計にした。これにより、あるノードのラムダ本体からクロージャ経由で外側ノード
  (祖先)のBuilderを誤って参照する(構文上はそのノードの子に見えるが、実際に実行すれば別ノードの
  子になってしまう)ケースも、レシーバー名の不一致として正しく検出できる。
- `SceneDefinitionGeneratorEndToEndTests`で、実際に`CompileAndLoad`した生成コードを実行し、
  `World`上にRoot(`SceneNodeTag`のみ、`Parent`無し)→MainCamera/Player(いずれも`Parent.Value ==
  Root`)というEntity階層と、Define本体に書いたとおりのComponent値が構築されることを、
  `EntityQuery`によるArchetype検索を介して確認した(`CreateRuntime`がRootのEntityしか返さない
  設計のため、MainCamera/Playerは固有のComponent(Camera/PlayerTag)を持つArchetypeとして
  見つける必要があった)。
- `SceneRuntimeEmitter`の生成コードは、`Set`呼び出しの引数式(`new Camera { FieldOfView = 60f }`
  等)を構文木からテキストとしてそのまま生成ファイルへ貼り付け、元ファイルの`using`
  ディレクティブも生成ファイル先頭へ複製する設計にした。実際に`samples/SceneAuthoringDemo`の
  生成ファイル(`Generated/.../SceneAuthoringDemo.MainScene.SceneRuntime.g.cs`)を確認し、
  `using CSharpGameEngine.Entities;`が複製された上で`new Position { X = 0f, Y = 2f, Z = -10f }`
  のような式がそのまま埋め込まれ、かつ`world.GetRef<global::CSharpGameEngine.Entities.Parent>
  (MainCamera) = new global::CSharpGameEngine.Entities.Parent { Value = Root };`のように
  Parentだけは完全修飾で自動生成されていることを確認した。
- Runtime Emitterの実装中に、`Set`呼び出しを1つも持たないノード(見本自身の`Root`)を
  `World.CreateArchetype`(0型引数オーバーロード無し)でどう成立させるかという問題に直面し、
  `SceneNodeTag`(0フィールドタグComponent)を`CSharpGameEngine.Entities`へ追加する判断を
  Step1完了後に行った(計画段階の「不明点」として仮称のまま残っていた設計を、実装中に確定した)。
- Analyzer実装を先行して単体で検証するため、Generatorの骨格(診断のみ報告、Emitterはまだ無し)を
  先に組み、`SceneDefinitionGeneratorDiagnosticsTests.cs`をEmitter実装前に一通り書いて実行した。
  SCENE001〜009・正常系の11件が実装1回目で全てグリーンになり、Emitter実装(SceneNodeSymbolEmitter/
  SceneRuntimeEmitter)を後続で追加した際も既存の診断テストに回帰は無かった。

## 意図と観測の一致

Conformance: Matches。[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]に記載した意図した設計と、
実際のビルド・テスト・`dotnet run`結果の間に既知の乖離は無い。見本からの唯一の意図的なAPI形状差分
(`LocalTransform`ではなく`Position`/`Rotation`/`Scale`の3分割)はユーザー確定済みの変更であり、
ADR-0030に記録済み。

## 残課題

- VSIX側のRoslyn WorkspaceによるHierarchy Docking Windowのライブ編集(Add/Delete/Rename/Reparent)は
  未着手。別タスクとして扱う。
- `SceneNodeId`はRenameのたびに再計算され、Rename前後で値が変わる。ノードIDをまたいで安定させる
  必要のある消費者(セーブデータでのノード参照等)が生まれた時点で、IDE管理のhidden annotationや
  `.scene.meta`方式を別途検討する。
- JSON版Scene実装(`src/CSharpGameEngine.Scene`、`tools/CSharpGameEngine.VSExtension/SceneFile.cs`)
  は今回変更しておらず、既存の乖離(`SceneFile.cs`に`Components`フィールドが無い等)もそのまま残る。
- `World.CreateArchetype`の型引数8個という上限(M0009から引き継いだ既知の制約)は、Scene
  Authoring側では`SCENE009`で静的に検出するのみで、`EntityCommandBuffer`経由の回避策は
  提供していない。
