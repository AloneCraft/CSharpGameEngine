# M0017: `[StartupScene]`/`[SceneEntity]`による宣言的Startup Scene API

## 目的

ユーザーの依頼: 「現在`Systems/GameSystems.cs`の`GameStartup`が行っている起動Scene構築を、静的な
C#宣言からSource Generatorが生成する仕組みとして実装してほしい。現行の動作・Archetype順序・
1フレーム内の生成連鎖・Componentのmanaged参照保持は一切変更しないこと」。

Unity DOTSへの移行でもECS Runtimeの再設計でもなく、既存の閉集合Archetype ECS([[ADR-0033-Static-Archetype-Ecs-Migration]])・
生成エントリポイント([[ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components]])・実行順序モデル
([[ADR-0036-System-Order-Attribute]])はすべて正本のまま、手書きの`GameStartup : ISystem`を
Component初期値の宣言だけへ置き換える追加機能。設計判断は[[ADR-0038-Startup-Scene-Declarative-Api]]に記録した。

## 実装したこと

### ランタイム `src/CSharpGameEngine.Entities/`
- `StartupSceneAttribute.cs`(新規): `[AttributeUsage(AttributeTargets.Class)]`、`Order`は`init`プロパティ。
- `SceneEntityAttribute.cs`(新規): `[AttributeUsage(AttributeTargets.Property)]`のマーカー。

### `GameSystemGenerator`(`src/CSharpGameEngine.CodeAnalysis/`)
- `StartupSceneModel.cs`(新規): `StartupSceneComponentModel`/`StartupSceneEntityModel`/`StartupSceneClassResult`。
- `StartupSceneAnalyzer.cs`(新規): `[StartupScene]`クラスと`[SceneEntity]`プロパティを構造的に解析する
  (staticクラス/partial/非ジェネリック、プロパティのstatic/getter/非indexer/非ref返し、戻り値が
  非nullable structまたは名前付きTupleか)。`GameSystemAnalyzer.IsNonNullableStruct`を`internal`化して
  Component/Tuple要素の判定規則を1箇所に共有した。
- `StartupSceneSystemEmitter.cs`(新規):
  - `BuildSyntheticSystemResult`: `[StartupScene]`クラスを`Query = {Bootstrap}`・`Order`属性値の
    `GameSystemResult`として**直接構築**する(構文木の再検出には依存しない — Roslynの1つのGeneratorは
    自分自身が生成した構文木を同じ実行内で見られないため)。
  - `CollectComponentSets`: 各Scene Entityの出力Component集合を`AdditionalComponentSets`向けに列挙する。
  - `EmitHiddenSystem`: 合成`GameSystemResult`が指す実体(`{ClassName}__StartupSystem`)を生成する。
    中身は旧`GameStartup.Update`と同じ意味(`Bootstrap.Done`判定→各`[SceneEntity]`プロパティを
    1回だけ評価→`state.World.Create(...)`)。
- `GameSystemGenerator.cs`: 4本目の`ForAttributeWithMetadataName`プロバイダ(`StartupScenes`)を追加し、
  `combined`パイプラインへ組み込んだ。実際に検出されたSystem(`SystemResults`)と合成System
  (`ValidStartupSystems`)を1つの`AllSystemResults`へマージしてから`GameSystemIndexAssigner.Assign`/
  `GameSystemEntryPointEmitter.ShouldEmitPlan`へ渡す。`[StartupScene(Order = N)]`は
  `OrderContext.SystemOrderByFqn`へ合成Systemの完全修飾名として追加登録し、`[SystemOrder]`をSystem
  自身へ直接付けた場合と同じ優先度で扱う。
- `GameSystemLoopEmitter.cs`: `BuildLocalName`を`internal`化して`StartupSceneSystemEmitter`と共有
  (Tuple Entityのローカル変数名を、キーワードエスケープ・重複回避込みで同じ規則から作るため)。
- `StartupSceneDiagnostics.cs`(新規)/`AnalyzerReleases.Unshipped.md`: STARTUP001〜010を追加。

### `UserEcsProject/`
- `Scenes/MainScene.cs`(新規): `Systems/GameSystems.cs`の`GameStartup`を`[StartupScene(Order = 0)]`
  `internal static partial class MainScene`へ置き換えた。ウィンドウタイトル・サイズ・FBXパス・
  シェーダーパス・カメラ初期値・太陽光/点光源/スポットライトの初期値は1つも変更していない。
- `Systems/GameSystems.cs`を削除(内容は`Scenes/MainScene.cs`へ全面移行、二重生成を避けるため)。
- `Systems/RuntimeSystem.cs`/`Components.cs`: `GameStartup`/`GameStartupSystem`/`Systems/GameSystems.cs`
  への参照コメントを、`MainScene`/`Scenes/MainScene.cs`へ更新。`Systems/RuntimeSystem.cs`自体の
  System構成(`Window`/`LevelLoad`/`DirectionalLight`/`PointLight`/`SpotLight`/`TransformSync`/`Present`)、
  ファイル分割、`[SystemOrder(1)]`は無変更。

### テスト
- `StartupSceneGeneratorDiagnosticsTests.cs`(新規、17件): STARTUP001〜010の各1件+正常系(単一Component
  Entity・Tuple Entity)2件。
- `StartupSceneGeneratorEmissionTests.cs`(新規、8件): 隠しstructが`ISystem`を実装し
  `Bootstrap.Done`ガードが`World.Create`より前に出ること、単一Component Entityはローカル変数を経由せず
  直接渡すこと、Tuple Entityは1回だけローカル変数へ評価してから名前付き要素へアクセスすること、
  複数Scene Entityが宣言順に出力されること、`GameSystem.Loop.g.cs`から実際にディスパッチされること、
  **既存Systemが同じComponent集合を宣言済みならArchetype出現順が変わらないこと**(要件セクション9の
  中核検証、`Archetype0{Bootstrap}`→`Archetype1{AppHost}`→`Archetype2{AppHost,Level}`を生成コードで
  直接確認)、StartupScene単独でもエントリポイントが生成されること、決定性。
- `StartupSceneGeneratorEndToEndTests.cs`(新規、4件): 単一Component/Tuple Entityが実際に1回だけ生成され
  2フレーム目で重複しないこと、`MainScene`が撒いたEntityを後続の実Systemが同一フレーム内で読み取れること
  (即時`World.Create`の連鎖の実証)、Scene propertyが3フレーム通じて1回しか評価されないこと(副作用
  カウンターで検証)。
- `GameSystemGeneratorTestHelper.cs`: `StartupSceneSources`(`StartupSceneAttribute.cs`/
  `SceneEntityAttribute.cs`)を追加。
- テストcsprojに両ファイルのTestAssetsリンクを追加。

### ドキュメント
`ADR-0038`(新規)、`M0017`(本書)、`Docs/Specifications/Ecs-Soa-Systems.md`に
「`[StartupScene]`/`[SceneEntity]`宣言(合成System)」節とSTARTUP001〜010の診断表を追加、
`Docs/Implementation-Status.md`にF-L3-013/F-L7-017/F-L7-018を追加。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build src/CSharpGameEngine.Entities/CSharpGameEngine.Entities.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/... -c Debug
  → 99/99 成功(既存73件 + 新規26件、回帰なし)

dotnet build UserEcsProject/UserEcsProject.csproj -c Debug
  → 成功、警告0/エラー0
```

生成物の目視確認:
- `UserEcsProject/Generated/.../UserEcsProject.Scenes.MainScene.StartupSystem.g.cs`が旧`GameStartup.Update`
  と同じ意味のコード(`Bootstrap.Done`判定→`var application = MainScene.Application;`→
  `state.World.Create(application.App, application.Level);`)であることを確認した。
- `GameSystem.Types.g.cs`のArchetype出現順(`Archetype0{Bootstrap}` → `Archetype1{AppHost}` →
  `Archetype2{AppHost,Level}` → `Archetype3{DirectionalLight}` → `Archetype4{PointLight}` →
  `Archetype5{SpotLight}` → `Archetype6{Transform,Renderable}` → `Archetype7{FramePresent}`)が、
  移行前と**完全に同一**であることを確認した。
- `GameSystem.Loop.g.cs`で`mainScene__StartupSystem.Update(...)`が`Archetype0`ループの先頭
  (=1フレーム目の最初)で呼ばれ、`Archetype2`ループで`window.Update`/`levelLoad.Update`が続けて
  呼ばれる構造が維持されていることを確認した。
- `GameSystem.EntryPoint.g.cs`(`Main`)は無変更(`World.Create(new Bootstrap())`→`State`→
  `GeneratedGameLoop.Run(ref State)`のみ)。

実機: `UserEcsProject.exe`を`Start-Process`で起動し、3秒後もプロセスが生存していること
(未処理例外での即死が無いこと)、ウィンドウタイトルが`MainScene.Application.App.Title`の値
`"C# Game Engine - ECS FBX Rendering PoC"`と一致することを`MainWindowTitle`で確認した。

## 意図的に除外したこと(次マイルストーン候補として報告)

- Scene Entity間の親子関係、Entityハンドルの自動保存、Scene Unload、Prefab、遅延Scene切替(要件で
  明示的に対象外とされた)。
- `[SceneDefinition]`(ツリー構造のScene Authoring/3Dプレビュー)との統合。責務が異なると判断し、
  独立したAttribute/Analyzer/Emitterとして追加した(ADR-0038参照)。
- `RuntimeSystem.cs`冒頭コメントが参照する「ADR-0037」は本タスクと無関係な既存の欠落
  (`PointLight`/`SpotLight`の毎フレーム加算・リセットに関する文書化漏れ)であり、番号の混同を避ける
  ため本機能のADRは`0038`とした。この欠落自体の解消は別タスク。

## 残課題

- 特になし。要件の完了条件(手書き`GameStartup`の撤廃、Order=0の`{Bootstrap}`合成Systemとしての統合、
  `{AppHost,Level}`の単一生成、Archetype順序・1フレーム目連鎖の維持、managed参照の許容継続、
  `GeneratedGameLoop.Run(ref State)`の維持、Reflection/object[]/Boxing不使用)はすべて満たしている。
