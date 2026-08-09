# ADR-0033: 静的(閉集合)Archetype ECSへの全面移行とSource Generatorの統合

## ステータス

承認済み。[[ADR-0029-Dynamic-Archetype-Soa-Ecs]]の動的Archetype SoA ECSを全面的に置き換え、
[[ADR-0032-Static-Archetype-Closed-Set-Ecs]]で第一段階として独立検証した閉集合Archetype方式を本番へ
統合した。Scene Authoring([[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]/
[[ADR-0031-Scene-Definition-3D-Preview]])も同じ変更の中で新方式へ作り直した。

## 背景

ユーザーから次の3点の依頼があった:

1. `StaticArchetypeSystemsDemo/Program.cs`のような起動コードもSGで生成する
2. ソリューション内の複数のSGを`GameSystemGenerator`に統一する(名前は`GameSystemGenerator`を残し、
   ECS生成の中身は完成度の高い`StaticArchetypeGenerator`の方式を移植する)
3. 他のSGプロジェクトを削除する

調査の結果、当初の想定より影響範囲が広いことが判明した:

- **`SceneDefinitionGenerator`は「別機能」ではなかった**。`SceneRuntimeEmitter`が
  `world.CreateEntity(world.CreateArchetype<T...>())`/`world.GetRef<T>(...)`という旧APIを直接生成して
  おり、ランタイム差し替えでビルド不能になる。
- **VSIXの3Dプレビューブリッジ(`WorldInspector.cs`)は動的Archetype方式に本質的に依存**していた。
  `world.ArchetypeCount`/`GetArchetype(i)`/`ComponentTypeIds`/`GetColumn()`という「実行時に全Archetypeを
  汎用列挙する」内部API(`InternalsVisibleTo`付き)をリフレクションで叩いており、閉集合方式には対応物が
  存在しない。
- **`[Context]`が本番で多用されていた**。9つの`[Update]`メソッド中8つが`Win32Window`/`FrameContext`/
  `StageRenderer`の注入に依存していたが、ADR-0032の設計には対応機能が無かった。
- **`[CurrentEntity]`/`[Commands]`/`[Without]`は本番で使用ゼロ**(grep確認済み)。
- 複数Archetype一致に依存していたのは`RenderSyncSystem.SyncProp`の1メソッドのみで、新設計の
  部分集合ディスパッチが自然に代替する。

ユーザーに上記を提示した上で「SceneDefinitionGeneratorもVSIXブリッジも新設計に合わせて作り直す」
「ECS関連サンプル4つはすべて削除する」ことを確認した。

## 決定事項

### 1. `CSharpGameEngine.Entities`は共有バイナリではなく「共有ソース」になる

`Runtime.cs`は`partial struct World`/`partial struct State`を宣言し、Generatorが同じ`partial struct
World`に`ListArchetypeNN`/`Create`/`MoveArchetype`/`Detach`/`PlaybackMoveArchetypes`を追加生成する。
**C#のpartial型は同一コンパイル内でしか合成できない**ため、コンパイル済みDLLを`<ProjectReference>`する
方式は原理的に成立しない。

`src/CSharpGameEngine.Entities/`はソースの**正本置き場**となり、消費側(`UserGameProject`・各テスト
プロジェクト)は`<Compile Include="..\src\CSharpGameEngine.Entities\*.cs" Link="Entities\..." />`で
自分のコンパイルへ取り込む。同プロジェクト単体の`dotnet build`は「System 0件でも空の`Archetype` enumと
空の`partial struct World`が生成され、`Runtime.cs`がコンパイルできる」ことのスモークテストとして残す。

**帰結**: 各消費プロジェクトが独自の`World`/`Entity` CLR型を持つ。VSIXブリッジが対象プロセスの
`World`を自分の型として扱う旧手法は成立しなくなった(決定事項6)。

### 2. Component/Systemの判別は完全に構造的推論(属性ゼロ)

`ISystem`マーカーinterfaceを実装するstructがSystem。`Update`/`Spawn`という名前のメソッドの引数から、
すべての役割を判別する:

| 位置・渡し方 | 役割 |
|---|---|
| 先頭・値渡し・型が`Entity` | Entityスロット(任意) |
| 末尾・`ref State` | Stateスロット(`Update`では必須) |
| それ以外で**`ref`**の非nullable struct | **Component** |
| それ以外(**`ref`でない**。structは`in`、参照型は値渡し) | **Context** |

Componentは`ref`必須・Contextは`ref`禁止なので、ref-kindだけで曖昧性なく判別できる。これが旧
`[Context]`属性の代替であり、「属性を一切使わない」という設計方針を保ったまま本番の要求(9メソッド中
8メソッドがContextに依存)を満たす。

**移行上の注意**: 旧コードの`in PlayerTag _`のようなタグ引数は`ref PlayerTag _`に変える必要がある。
`in`のままだとContextとして分類され、Archetype一致に参加しなくなる(サイレント障害)。同様に読み取り
専用だった`in Position`もすべて`ref Position`になる。

### 3. 実行順序は「1ファイルに集約した記述順」

Component引数がすべて`ref`になった結果、旧設計の`in`=Read/`ref`=Writeという区別が消え、
`GameSystemScheduler`のWrite→Read自動トポロジカルソートは**原理的に成立しなくなった**
(旧設計も`[Update(Order = n)]`という手動指定を第一キーとして併用してはいた)。

代替として`Order`定数も属性も導入せず、**ソース上の記述順**をそのまま実行順とする。Roslynがファイルを
渡す順序は保証されないため、順序が意味を持つSystemは1ファイルへ集約する運用とし、
`UserGameProject/Systems/Systems.cs`がその実例になっている。Generatorの整列キーは
`(SyntaxTree.FilePath のOrdinal順, Location.SourceSpan.Start)`で、ファイルパスは複数ファイルにまたがった
場合の決定性を担保するタイブレークとしてのみ使う。

**失ったもの**: 実行順序の正しさが人間の責任になった。カメラが1フレーム遅れる類の不具合は自動検出
できないため、実機確認が必要になる(「検証結果」参照)。

### 4. Archetype名は出現順の連番、`Create`のパラメータ順は宣言順

ADR-0032の「Component集合のインデックスを連結する」命名(`Archetype0001020304050613`のような形)は、
読めない上に**どこかのSystemへ引数を1つ足すだけで全Archetypeの名前が変わる**ため破棄し、単純な出現順の
連番(`Archetype0`, `Archetype1`, …)にした。

同時に、生成される`World.Create`/`World.MoveArchetype`のパラメータ順を、**最初にその構成を宣言した
`Update`/`Spawn`の引数順**に合わせた。これによりArchetype名は完全に内部識別子となり、ユーザーコードは
Component型の組み合わせによるオーバーロード解決だけでEntityを生成・移動できる。

### 5. Archetype形状の宣言専用メソッド`Spawn`

Archetypeは`Update`/`Spawn`の引数からのみ導出される(`World.Create`の呼び出し側からは推論しない)。
`Update`のどのオーバーロードとも一致しない構成 — 実例として`UserGameProject`のPlayerは
`Health`をどのSystemからも参照されていないため8Component構成が導出されない — は、`Spawn`という名前の
メソッドで宣言する。`Spawn`は`ref` Component引数のみを取り(先頭`Entity`・末尾`ref State`は不可)、
戻り値は`void`、**メインループのディスパッチ対象から完全に除外**される。

### 6. VSIXプレビューは生成された`WorldSnapshot`をリフレクションで読む

決定事項1により、CLIブリッジは対象プロセスの`World`と同じCLR型を共有できなくなった。加えて閉集合方式には
「実行時に全Archetypeを汎用列挙する」surfaceが存在しない(各Archetypeはコンパイル時に確定した個別の
struct型であり、共通のコレクションに入っていない)。

そこでGeneratorが`WorldSnapshot.Capture(in World)`を生成する。全`ArchetypeNN`のリストを走査し、
`ArchetypeSnapshotEntry[]`(`EntityId`/`ParentEntityId`/`HasParent`/`ArchetypeName`/
`object[] ComponentValues`)を返す。CLIはこれをリフレクションで1回呼ぶだけになり、
`InternalsVisibleTo`の付与も、`CSharpGameEngine.Entities`への参照そのものも不要になった。
ALC境界を越えてデリゲートを渡す必要が無いよう、コールバックではなく戻り値ベースにしてある。
`WorldSnapshot`はこのランタイムで唯一意図的に`public`な生成物である。

`World`がstructになったため`CreateRuntime(World)`は**`CreateRuntime(ref World)`**へ変わった。

### 7. SceneDefinitionはGameSystemGeneratorへ統合する

SceneのノードとSystemの`Update`/`Spawn`は**同一のArchetype空間**へ入る必要がある(両方が単一の
`partial struct World`/`enum Archetype`を作るため、生成は1回しか行えない)。2つのGeneratorが独立に
同じレジストリを再導出する案は、ズレたときに「型が見つからない」という診断困難なビルド破壊になるため
不採用とし、`GameSystemGenerator`が2つのSyntaxProvider(`ISystem`実装structと`[SceneDefinition]`クラス)を
持つ単一のエントリポイントになった。`SceneDefinitionGenerator.cs`は削除し、`SceneDefinitionAnalyzer`/
`SceneNodeSymbolEmitter`/`SceneRuntimeEmitter`はこのパイプラインから呼ばれる部品として存続する。

`SceneRuntimeEmitter`の出力は、旧「`CreateEntity` + Componentごとの後付け`GetRef`代入」から、ノード
ごとに`ArchetypeNN.Create(ref world, …)`を1回呼ぶ形へ簡潔化された。

### 8. 出力は`ISystem`型が実在するコンパイルに限定する

`CSharpGameEngine.CodeAnalysis`は1つのアナライザーパッケージに複数のGeneratorを同居させる規約
(ADR-0014)のため、このECSを使う意図が無いプロジェクトでも参照だけで本Generatorが走る。無条件に出力すると
`CSharpGameEngine.Entities`名前空間へ空の`World`等が紛れ込み、実際にビルドを壊す(ADR-0032の実装中に
`UserGameProject`で確認済みの回帰)。`compilation.GetTypeByMetadataName("CSharpGameEngine.Entities.ISystem")`が
非nullの場合に限って出力する。

### 9. 診断は`SYSGEN001`〜`SYSGEN020`へ全面的に振り直す(SYSGEN009は欠番)

属性ベースだった旧`SYSGEN001`〜`020`と、ADR-0032の`SARCH001`〜`015`をいずれも破棄し、構造的推論ベースの
新しい規則へ振り直した。`AnalyzerReleases.Shipped.md`が空(未出荷)なので「Removed Rules」の管理は不要。

`SYSGEN009`(「Componentパラメータは`ref`でなければならない」)は**到達不能のため欠番**とした。決定事項2で
「`ref`ならComponent、`ref`以外ならContext」と定義した以上、`ref`でないパラメータは壊れたComponentでは
なく正当なContextになる。`out`のような不正な渡し方はContext側の規則である`SYSGEN018`が捕捉する
(ADR-0015でも到達不能と判明した診断を同様に削除した前例がある)。

`SCENE009`(1ノードのComponent数が8以下)も削除した。この規則は旧`World.CreateArchetype<T1..T8>`の
ジェネリック型引数の上限に由来するもので、閉集合方式では組み合わせごとに専用structを生成するため上限
自体が存在しない。

### 10. 起動コードもComponent + ISystemで書く

ユーザーの依頼1(起動コードのSG生成)は、「Program.csをSGが出力する」形ではなく「**起動処理そのものを
ECSで書けるようにする**」形で満たした。`UserGameProject`では`SceneSpawnRequest`コンポーネントを1つ持つ
エンティティをProgram.csが生成し、`SceneSpawnSystem`がそれを消費してステージ・ライト・Playerをすべて
`State.World.Create(...)`で生成する。Program.csに残るのは、Generatorが知りようのないもの
(ウィンドウ/レンダラー/GLB読み込みの初期化と、このエンジン固有の毎フレーム処理)だけになった。

メインループ本体は生成しない。`window.PumpEvents()`/`renderer.RenderFrame()`/`FrameContext`の毎フレーム
再計算はエンジン固有であり、生成された`Run`(内部で`while(State.Enabled)`を回す)はContext引数をループ
全体で固定してしまうため使えない。Program.csは手書きのループから`GeneratedGameLoop.RunFrame(...)`を
毎フレーム呼ぶ(`Run`自体は将来のヘッドレス用途に生成だけしておく)。

### 11. `[CurrentEntity]`/`[Commands]`/`[Without]`は実装しない

- `[CurrentEntity]`: 新設計は先頭`Entity`引数を無標識で提供済み。上位互換のため不要。
- `[Commands]`: 本番使用ゼロ。閉集合方式で必要な構造変更(Archetype間移動)は`World.MoveArchetype` +
  `PlaybackMoveArchetypes`が既にフレーム末尾遅延適用でカバーしている。汎用`Add<T>`/`Remove<T>`/`Destroy`は
  需要が出た時点で設計する。
- `[Without]`: 本番使用ゼロ。閉集合方式に除外プリミティブが無く、Archetypeが有限列挙である以上
  「意図する組み合わせを直接宣言する」方が自然。

## 代替案として検討したもの

- **Archetype名にComponent集合を連結する(ADR-0032の方式)**: 実際に生成させたところ
  `Archetype0001020304050613`となり、可読性・安定性ともに実用に耐えないため破棄(決定事項4)。
- **`Spawn`宣言System名からArchetypeの別名を生成する**: 決定事項4でArchetype名が内部識別子に徹する
  設計になったため不要と判断。
- **2つのGeneratorが独立にArchetypeレジストリを再導出する**: 決定事項7の理由で不採用。
- **VSIXブリッジで対象と同じCLR型を共有し続ける**: 決定事項1により原理的に不可能。
- **`samples/SceneAuthoringDemo`を書き換えて存続させる**: 当初のプランではそうする予定だったが、
  ユーザーの判断でECS関連サンプル4つをすべて削除した。VSIXブリッジのE2E検証は、テストプロジェクト自身が
  `[SceneDefinition]`フィクスチャを持つ形へ移した(クロスTFM参照もグラフィックス依存も無くなり、
  かえって堅牢になった)。

## 帰結

### 得られたもの
- 属性を一切使わない、純粋な構造的推論だけで動くECSになった。
- Archetype名がユーザーコードに現れなくなった(`World.Create`/`MoveArchetype`のオーバーロード解決)。
- 起動時のエンティティ生成までECSの中で書けるようになり、`UserGameProject`が新方式の実例そのものになった。
- Scene生成コードが簡潔になった(後付けの`GetRef`代入パスが消滅)。
- VSIXブリッジから`InternalsVisibleTo`と`CSharpGameEngine.Entities`への参照が両方とも不要になった。

### 失ったもの
- **Write→Readからの自動実行順序決定**(決定事項3)。実行順序の正しさは記述順を書く人間の責任になった。
- **動的な構造変更の自由度**。実行時に任意のComponent集合を作ることはできず、Archetypeは
  `Update`/`Spawn`で宣言した閉集合に限られる。
- `EntityCommandBuffer`の汎用`Add`/`Remove`/`Destroy`と`[Without]`(決定事項11、いずれも本番未使用)。
- ECS関連サンプル4プロジェクト(`SoaSystemsDemo`/`SceneAuthoringDemo`/`AosSoaGeneratedExpansion`/
  `StaticArchetypeSystemsDemo`)。`UserGameProject`が唯一の実証場所になった。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build src/CSharpGameEngine.Entities/CSharpGameEngine.Entities.csproj -c Debug
  → 成功(System 0件でも空のArchetype enum/partial struct Worldが生成されRuntime.csがコンパイルできる)

dotnet build UserGameProject/UserGameProject.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build CSharpGameEngine.slnx -c Debug   → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release → 成功、警告0/エラー0

dotnet test CSharpGameEngine.slnx
  → 全プロジェクト成功(内訳は M0013 参照)
```

生成された`UserGameProject/Generated/.../GameSystem.Loop.g.cs`を直接読み、次を目視確認した:

- Playerの8Component構成のArchetypeが実在し、そのループ内のディスパッチ順が
  `inputSystem` → `playerControlSystem` → `cameraSystem` → `renderSyncSystem`になっている
  (`Systems.cs`の記述順どおり)。
- 可動プロップのArchetypeのループが`movingPropSystem` → `renderSyncSystem`の順になっている。
- `RenderSyncSystem`の`{Position,Rotation,Scale,RenderObjectRef}`オーバーロードが、静的プロップと
  可動プロップ**両方**のループに現れている(部分集合ディスパッチが旧Query一致を代替できている証拠)。

### 実機確認(ユーザーによる目視、限定的)

D3D12ウィンドウの描画内容はビルド/テストからは判定できないため、ユーザーに
`dotnet run --project UserGameProject`での目視を依頼した。結果:

- **移行前と同じ見え方・同じ操作感**であることを確認した。すなわちプロップの変換行列が正しく計算されて
  `StageRenderer`へ届いており、入力も効いている。
- ただし**このステージは移行前からZファイティングで描画が乱れている**(本移行とは無関係の既存問題。
  ECSは変換行列を渡すところまでしか関与せず、深度/描画順の問題)。そのため「描画が完全に正しい」ことの
  厳密な確認はできていない。

決定事項3で自動順序決定を失ったことによる最大のリスク(`CameraSystem`が`PlayerControlSystem`より先に
走るとカメラがちょうど1フレーム遅れる)については、実機の体感に加えて**生成コードの側で構造的に確認**した:
`GameSystem.Loop.g.cs`のPlayer用Archetypeループの順序が
`inputSystem → playerControlSystem → cameraSystem → renderSyncSystem`となっており、これは旧
`GameSystemScheduler`のWrite→Read依存解析が導出したはずの順序と完全に一致する
(InputStateを書く→読む、Position/CameraRigを書く→読む、Cameraを書く→読む)。

**未確認のまま残っている項目**: ライティング/フォグ/アンビエントが実際に適用されているか
(「0件一致=既定値のまま」と「配線漏れで動いていない」は、自動テストでも既存のZファイティング越しの
目視でも区別できない)。

## 未解決事項

- 実行順序が記述順依存になったため、複数ファイルにSystemを分割すると順序がファイルパスのOrdinal順に
  依存する。1ファイル集約を運用で担保しているだけで、Generatorが強制はしていない。
- Archetype名は出現順の連番なので、Systemを追加・削除すると既存Archetypeの番号がずれる。ユーザーコードは
  Archetype名を参照しないため実害は無いが、生成コードを直接読むテスト(`Entities.Tests`が
  `ListArchetype0`等を名指ししている箇所)は影響を受ける。
- `ISystem`実装の検出には`ForAttributeWithMetadataName`のような専用の高速パスが無く、base listを持つ
  全型宣言に対して意味解析を行う。大規模プロジェクトでのコンパイル時間が問題になった場合、マーカー属性の
  追加を再検討する。
- `WorldSnapshot.Capture`は全Componentをボックス化するため、巨大なWorldでは相応のコストがかかる。
  現状の唯一の消費者はVSIXプレビュー(1回きりの呼び出し)なので許容している。

## 関連機能

- F-L3-012 — `CSharpGameEngine.Entities`(`Runtime.cs`: `ISystem`/`IArchetype`/`Entity`/`World`/`State`/
  `ChunkList<T>`、共有ソース化)
- F-L7-015 — `GameSystemGenerator`(Analyzer/Model/IndexAssigner/Types・World・Snapshot・Loop各Emitter、
  SceneDefinition統合)
- F-L7-016 — `GameSystemGenerator`診断(SYSGEN001〜020、SYSGEN009欠番)
- F-L7-014 — `CSharpGameEngine.SceneAuthoring.Cli`(`WorldSnapshot`ベースのリフレクション専用ブリッジへ改修)
- F-L9-003・F-L9-004 — `UserGameProject`のSystems集約と起動処理のECS化
