# ADR-0038: `[StartupScene]`/`[SceneEntity]`による宣言的Startup Scene API

## ステータス

承認済み。[[ADR-0033-Static-Archetype-Ecs-Migration]]の「属性を一切使わない構造的推論」原則は
維持したまま、[[ADR-0036-System-Order-Attribute]]と同じ「構造から導出できない情報にだけ最小限の
属性を許す」基準に沿って、起動時Entity生成を宣言的に書けるAPIを追加する。既存の`GeneratedGameLoop.Run(ref State)`
シグネチャ・閉集合Archetype・実行順序モデル・Runtime Systemの責務分担は一切変更しない。

## 背景

[[ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components]]により、生成された`Main`は
`World.Create(new Bootstrap())`という単一のシードEntityを作るだけになった。残りの起動処理
(ウィンドウ設定・レベルパス・初期カメラ・光源初期値の宣言と`state.World.Create(...)`の呼び出し)は、
`Bootstrap`を`ref`で受け取る`ISystem`をユーザーが手書きする形で表現されていた
(`UserEcsProject/Systems/GameSystems.cs`の`GameStartup`)。

```csharp
[SystemOrder(0)]
internal struct GameStartup : ISystem
{
    public bool Update(ref Bootstrap bootstrap, ref State state)
    {
        if (bootstrap.Done) return true;
        bootstrap.Done = true;
        state.World.Create(new AppHost { ... }, new Level { ... });
        return true;
    }
}
```

この定型部分(`Bootstrap.Done`判定、`ISystem`実装、`Update`シグネチャ、`state.World.Create`呼び出し)は
どのゲームでも同じ形になり、ユーザーが実際に書きたいのは`AppHost`/`Level`の**値**だけである。
ユーザーからの要望は「Entity/Component初期値の宣言だけを書けば、起動時Systemの配線はSGへ任せたい」
というものだった。

## 決定事項

### 1. 2つの新規Attribute(`CSharpGameEngine.Entities`に手書き)

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StartupSceneAttribute : Attribute
{
    public int Order { get; init; }
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SceneEntityAttribute : Attribute
{
}
```

`SystemOrderAttribute`/`Bootstrap`/`Parent`と同じく`src/CSharpGameEngine.Entities/`へ手書きで置き、
消費側csprojのワイルドカード(`*.cs`)で自動伝播する(csproj変更不要)。`SceneEntity`という名前にした
理由は、ランタイムに存在する`Entity`型との名前上の衝突・混同を避けつつ、Scene宣言であることを
明確にするため(セクション5参照)。

### 2. 宣言規則

```csharp
[StartupScene(Order = 0)]
internal static partial class MainScene
{
    [SceneEntity]
    internal static (AppHost App, Level Level) Application => (
        new AppHost { Title = "...", Width = 1280, Height = 720 },
        new Level { Path = "...", ShaderPath = "..." });
}
```

- `[StartupScene]`対象は`static partial class`。`Order`は合成Systemの実効的な順序値。
- `[SceneEntity]`対象は`static`・`get`アクセサ付き・非indexer・非`ref`返しのプロパティ。
- 戻り値が単一の非nullable structなら1 Componentのみのentity、`ValueTuple`(要素名必須)なら
  その各要素を持つentity。
- 1クラスに複数の`[SceneEntity]`プロパティを書ける。生成順はプロパティの宣言位置による決定論的な順序。
- Componentにmanaged参照を含んでよい(`Win32Window?`/`StageRenderer?`等)。ADR-0033の
  「非nullable structであること」という制約はComponent**型自体**に対するものであり、フィールドの
  参照型保持を禁じるものではない — この解釈は変更しない。

### 3. 合成System方式(既存Systemモデルへの統合)

Roslynの1つのGeneratorは、自分自身が生成した構文木を同じ実行内で再検出できない
(`SyntaxProvider`は元のユーザーコンパイルしか見ない)。したがって「`[StartupScene]`から`ISystem`
実装structを生成し、既存の`GameSystemAnalyzer`による再検出に期待する」という設計は取れない。

代わりに`StartupSceneSystemEmitter.BuildSyntheticSystemResult`が、実際に検出されたSystemと**同じ
`GameSystemResult`モデルを直接構築**する(`Query = {Bootstrap}`、`Method.Components = [Bootstrap]`、
`CallOrder = [Component, State]`)。この合成`GameSystemResult`を、`GameSystemAnalyzer`が検出した
実際のSystem群と同じ`List`へ合流させたうえで、既存の`GameSystemIndexAssigner.Assign`・
`GameSystemLoopEmitter.Emit`・`GameSystemEntryPointEmitter`へそのまま渡す。これらのEmitterは
「合成か実際に検出されたものか」を一切区別しない。

`StartupSceneSystemEmitter.EmitHiddenSystem`は、その合成`GameSystemResult`が指す実体
(`{ClassName}__StartupSystem`という隠しstruct、元クラスと同じ名前空間)を生成する。中身は旧来の
手書き`GameStartup`と同じ意味(`Bootstrap.Done`判定 → 各`[SceneEntity]`プロパティを1回だけ評価 →
`state.World.Create(...)`)。この隠しstructのソースと、それを指す合成`GameSystemResult`の両方を
**同じGenerator実行内**で作るため、Roslynの「他Generatorの出力を再検出できない」制約に抵触しない
(生成された隠しstructの実ソースは、最終的な`csc`コンパイル時に他の生成コードと一緒に解決される)。

`[StartupScene(Order = N)]`の明示値は、実際に検出されたSystemの`SystemOrderByFqn`マップへ合成Systemの
完全修飾名を追加登録することで、`[SystemOrder]`をSystem自身へ直接付けた場合と**同じ優先度**として
扱う(`GameSystemExecutionOrder.Compare`を変更する必要が無い)。

### 4. Archetype登録順序を壊さない

Entityの出力Component集合(例: `{AppHost, Level}`)は、Systemの`Update`/`Spawn`引数集合と同じ
`AdditionalComponentSets`経由で`GameSystemIndexAssigner.Assign`へ渡す。`Assign`は実際に検出された
(合成含む)Systemの引数集合を**先に**登録してから`AdditionalComponentSets`を登録するため、既存Systemが
同じ集合を既に宣言していれば重複登録は黙って無視され(`RegisterSet`のキー重複排除)、Archetypeの出現順は
一切動かない。`UserEcsProject`では`LevelLoad.Update(ref AppHost, ref Level, ref State)`が既に
`{AppHost, Level}`を登録しているため、`MainScene`の`Application`が同じ集合を宣言しても新しい
Archetypeは増えない。

### 5. `UserEcsProject`への適用

`Systems/GameSystems.cs`の`GameStartup : ISystem`を`Scenes/MainScene.cs`の`[StartupScene(Order = 0)]`
宣言へ置き換えた。ウィンドウタイトル・サイズ・FBXパス・シェーダーパス・カメラ初期値・太陽光/点光源/
スポットライトの初期値は1つも変更していない。`Systems/RuntimeSystem.cs`(`Window`/`LevelLoad`/
各ライトSystem/`TransformSync`/`Present`)は無変更。

## 診断: STARTUP001〜010

| ID | 内容 |
|---|---|
| STARTUP001 | `[StartupScene]`の対象が`static partial class`でない |
| STARTUP002 | `[StartupScene]`クラスがジェネリック |
| STARTUP003 | `[SceneEntity]`プロパティが`static`でない |
| STARTUP004 | `[SceneEntity]`がindexerに付いている |
| STARTUP005 | `[SceneEntity]`プロパティに`get`アクセサが無い |
| STARTUP006 | `[SceneEntity]`プロパティが`ref`/`ref readonly`で返している |
| STARTUP007 | 戻り値が非nullable structでも`ValueTuple`でもない |
| STARTUP008 | Tuple要素が非nullable structでない |
| STARTUP009 | 同一Tuple内でComponent型が重複している |
| STARTUP010 | Tuple要素に明示的な名前が無い |

`[SceneEntity]`がプロパティ以外に付く場合(`AttributeTargets.Property`)、同一クラス内でEntity論理名
(プロパティ名)が衝突する場合(C#の重複宣言禁止)、生成型名/Hint Nameが衝突する場合(元クラスのFQNから
1:1に導出するため構造的に起こり得ない)は、言語仕様または命名規則により到達不能なので専用診断を
設けていない(`SYSGEN009`と同じ「欠番の説明をコードコメントに残す」方針)。

## 既存`[SceneDefinition]`との関係(統合しなかった理由)

`[SceneDefinition]`(ADR-0030/0031)は、複数ノードから成る**ツリー構造**をC#コードで宣言的に記述し、
コードエディタから対話的にプレビューし、将来的にはHierarchy編集UIで視覚的に編集する「Scene Authoring
ツール」である。生成される`CreateRuntime(ref World world)`は技術的にはArchetype/`World.Create`と
完全統合されているが、**呼び出し口が起動シーケンスに配線されていない**(`GeneratedEntryPoint.Main`は
関与せず、唯一の呼び出し元はVSIXの3Dプレビューコマンドがリフレクションで実行するエディタ機能のみ)。

これに対し`[StartupScene]`/`[SceneEntity]`は「起動時に**自動的に**1個以上のEntityを**Component
初期値だけ**で宣言する軽量API」であり、ツリー構造・親子関係・プレビューUI・ノードシンボル・
`SceneDefinitionAnalyzer`の複雑な構文解析(`Create`/`Set`呼び出しのネスト解析)は不要で、むしろ
Order 0のSystemとして自動的に実行されることが本質的要件である。したがって責務が異なると判断し、
既存`[SceneDefinition]`を拡張・統合するのではなく、別の軽量なAttribute・Analyzer・Emitterとして
追加した。再利用したのは以下のパターンのみ:

- `GameSystemGenerator.cs`内の`Combined`パイプラインへの新規`IncrementalValuesProvider`追加という
  統合ポイント(`Scenes`/`SystemOrders`と並ぶ4本目の`Collect().Combine(...)`)。
- Component集合を`AdditionalComponentSets`として同じArchetypeレジストリへ登録するパターン
  (`SceneRuntimeEmitter.CollectComponentSets`と同型の`StartupSceneSystemEmitter.CollectComponentSets`)。
- `GameSystemAnalyzer.IsNonNullableStruct`を`internal`化して両Analyzerから共有(Component/Tuple要素の
  構造判定規則を1箇所に保つ)。

## 代替案として検討したもの

- **`[SceneDefinition]`を拡張して起動時Entityも表現する**: ツリー構造・`Create`/`Set`のネスト構文解析
  という複雑さが、単一Entity宣言には不要なオーバーヘッドになる。またSceneDefinitionは意図的に
  起動シーケンスへ配線されていない(エディタプレビュー専用)ため、「起動時に自動実行される」という
  StartupSceneの本質的要件と設計意図が食い違う。
- **Reflectionで起動時に`[StartupScene]`型を列挙して呼び出す**: `object[]`・Boxing・実行時型検索が
  必要になり、ADR-0033の「Reflectionを使わない静的解析」路線から外れる。またVSIXの3Dプレビュー
  ブリッジ(`CSharpGameEngine.SceneAuthoring.Cli`)のような、意図的にReflectionを使う別レイヤーとの
  区別が付かなくなる。
- **`[StartupScene]`から`ISystem`実装structのソースだけを生成し、既存`GameSystemAnalyzer`による
  同一実行内の再検出に期待する**: Roslynの1つのGeneratorは自分自身が生成した構文木を同じ実行内で
  見られないため、この設計は成立しない(採用した「`GameSystemResult`を直接構築する」方式の理由)。
- **Sceneが撒くComponent集合を`AllSystems`のSystem引数登録より先に登録する**: `UserEcsProject`の
  `{AppHost, Level}`は既にLevelLoadが登録しているため、先に登録するとArchetype出現順が
  `{Bootstrap}, {AppHost, Level}, {AppHost}, ...`のように変わってしまう。既存Systemの登録を
  先に完了させてからAdditionalComponentSetsとして追加登録する現行の`Assign`の順序をそのまま使う
  ことで、この問題を避けた。

## 帰結

### 得られたもの

- 手書きの`GameStartup : ISystem`(定型的な`Bootstrap.Done`判定+`ISystem`配線)が不要になり、
  ユーザーはComponent初期値の宣言だけを書けばよくなった。
- 合成Systemが既存の実行順序モデル・Archetypeレジストリ・ディスパッチループへ「実際に検出された
  Systemと区別なく」合流するため、`GameSystemLoopEmitter`/`GameSystemEntryPointEmitter`に
  一切変更を加えていない。
- Archetypeの出現順・1フレーム目の生成連鎖(`{Bootstrap}` → `{AppHost, Level}` → 各ライト →
  `{Transform, Renderable}` → `{FramePresent}`)は変更前と完全に同一。

### 失ったもの

- ADR-0033の「属性を一切使わない」原則に、`[StartupScene]`/`[SceneEntity]`という2つの新規属性が
  加わった。ただしADR-0036と同じく完全にopt-inであり、使わなければ何も変わらない。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build src/CSharpGameEngine.Entities/CSharpGameEngine.Entities.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/... -c Debug
  → 99/99 成功(既存73件 + 新規26件: Diagnostics 17件・Emission 8件・EndToEnd 4件、
    うち「StartupSceneOutputArchetype_DoesNotDisturbExistingSystemDerivedOrder」が
    Archetype0〜2の出現順が変わらないことを生成コードで直接検証)

dotnet build UserEcsProject/UserEcsProject.csproj -c Debug
  → 成功、警告0/エラー0
```

`UserEcsProject/Generated/.../UserEcsProject.Scenes.MainScene.StartupSystem.g.cs`を目視確認し、
旧`GameStartup.Update`と同じ意味のコードが生成されていることを確認した。
`GameSystem.Types.g.cs`のArchetype出現順(`Archetype0`〜`7`)が変更前と完全一致することを確認した。
`GameSystem.Loop.g.cs`で`mainScene__StartupSystem.Update(...)`が`Archetype0`ループの先頭で
呼ばれていることを確認した。`GameSystem.EntryPoint.g.cs`(`Main`)は無変更。

実機では`UserEcsProject.exe`を起動し、3秒後もプロセスが生存していること、ウィンドウタイトルが
`MainScene.Application.App.Title`の値(`"C# Game Engine - ECS FBX Rendering PoC"`)と一致することを
確認した(`Start-Process`+`MainWindowTitle`)。

## 関連機能

- F-L3-013 — `CSharpGameEngine.Entities`(`StartupSceneAttribute.cs`/`SceneEntityAttribute.cs`の追加)
- F-L7-017 — `GameSystemGenerator`(`StartupSceneAnalyzer`/`StartupSceneSystemEmitter`の追加)
- F-L7-018 — `GameSystemGenerator`診断(STARTUP001〜010の追加)

関連: [[ADR-0033-Static-Archetype-Ecs-Migration]]、[[ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components]]、[[ADR-0036-System-Order-Attribute]]、[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]
