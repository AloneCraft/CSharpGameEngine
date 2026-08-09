# ADR-0032: 静的(閉集合)Archetype ECSと`StaticArchetypeGenerator`(第一段階)

## ステータス

第一段階として承認・実装済み。本番統合は[[ADR-0033-Static-Archetype-Ecs-Migration]](M0013)で完了した。

本ADRが対象としたのは、既存の本番ECS(ADR-0029/M0009)に一切手を入れずに設計を独立検証することであり、
そのために作った`src/CSharpGameEngine.StaticArchetype`と`samples/StaticArchetypeSystemsDemo`は
ADR-0033で役割を終えて削除された(内容は`src/CSharpGameEngine.Entities`と`GameSystemGenerator`へ吸収)。
`StaticArchetypeGenerator`もADR-0033決定事項7の統合により`GameSystemGenerator`へ改称・吸収されている。

**ADR-0033で変更された本ADRの決定事項**:
- Archetypeの命名(本ADR決定事項4の「インデックス連結」)は、実際に生成させると
  `Archetype0001020304050613`となり実用に耐えないため、出現順の連番へ変更された。
- 全型がグローバル名前空間(本ADR決定事項8)だったのは、`CSharpGameEngine.Entities`名前空間へ移された。
- `[Context]`相当・`Spawn`によるArchetype宣言・`WorldSnapshot`は、いずれも本ADRには無かった機能として
  ADR-0033で追加された。

本設計は[[ADR-0015-Archetype-Ecs-Static-Runtime]](旧`CSharpGameEngine.Ecs`、CRTP`IEcsArchetype<TSelf>`+
プロパティベースのComponent宣言、Superseded済み)とは無関係の、別物の新設計である。

## 背景

ユーザーが`UserGameProject/ECSの設計であるこの.g.csを生成するようなSGを作って/`に3ファイル
(`Runtime.cs`・`ユーザー定義コード.cs`・`ユーザー定義コード.g.cs`)を手書きし、「この入出力を実現する
Source Generatorを書いてほしい」というコードによる仕様(spec-by-example)を示した。セッション中に
ユーザー自身がこのサンプルを2度書き直しており、最終形を実装の正本として扱った。

現行の本番ECS(`CSharpGameEngine.Entities` + `GameSystemGenerator`、[[ADR-0029-Dynamic-Archetype-Soa-Ecs]])は、
Archetypeを実行時に動的生成する設計だが、このサンプルが要求しているのは真逆の、Archetypeをコンパイル時に
閉じた集合として列挙する設計である。ユーザーに確認したところ、これは意図的な方針転換で、将来的には
本設計が`GameSystemGenerator`を完全に置き換える予定である。ただし今回のパスでは、置き換え本体
(`UserGameProject`移行、旧Generator削除)は行わず、新SG+新ランタイムを完全に独立させて実装し、
3ファイルの入出力どおりに動くことを検証するところまでを目的とすることをユーザーに確認済み。

この設計のComponent/Systemには一切属性を使わない。System判定は`ISystem`マーカーinterface
(`Runtime.cs`にユーザー自身が定義済み)の実装で行い、Component判定は「Systemの`Update`引数として
参照されるstruct」という構造的推論のみで行うことをユーザーに確認した(検討の途中、`Component2`が
どのSystemからも参照されていないのに`Archetype2`一式が生成されているように見える版が一時的に
あったが、これはユーザーの保存漏れによる一時的な状態で、最終版では`Component2`は
`System0`の4番目のオーバーロードで実際に参照されている。「宣言されているだけで自動的にArchetypeを
持つ」という特別ルールは無い)。サンプルの`ユーザー定義コード.g.cs`はメインループが`System0`しか
呼んでいないが、これは手書きサンプルの書き漏れであることをユーザーに確認済みなので、実装した
Generatorは`ISystem`を実装する全Systemをループに含める。

## 決定事項

### 1. `Runtime.cs`は「参照するライブラリ」ではなく「共有ソース」として配布する

`Runtime.cs`は`partial struct World`/`partial struct State`を宣言し、Generatorが同じ`partial struct
World`に`ListArchetypeNN`/`MoveArchetype`/`Detach`/`PlaybackMoveArchetypes`等を追加で生成する。C#の
partial型は同一コンパイル内でしか合成できないため、`Runtime.cs`を通常の`.csproj`にビルドして
`<ProjectReference>`で参照する方式は使えない(コンパイル済みDLLのpartial structに、別コンパイルの
SG出力を後から継ぎ足すことはできない)。

`Runtime.cs`は`src/CSharpGameEngine.StaticArchetype/`に正本として1つだけ置くが、消費側
(`samples/StaticArchetypeSystemsDemo`)はこれを`<ProjectReference>`ではなく`<Compile Include="...\
Runtime.cs" Link="..."/>`でソースとして自分のコンパイルに取り込む。Generatorの出力も同じコンパイルに
入るので、`partial struct World`が正しく合成される。`src/CSharpGameEngine.StaticArchetype`自体は
「Runtime.csの正本置き場+単体スモークビルド」の役割のみで、他プロジェクトから通常のバイナリ参照は
されない。

### 2. `StaticArchetypeGenerator`は「コンパイル内に`ISystem`型が実在する場合に限り」出力する(実装中に発見した必須の安全策)

`CSharpGameEngine.CodeAnalysis`は1つのアナライザーパッケージに複数のGeneratorを同居させる既存規約
([[ADR-0014-CodeAnalysis-Generator-Foundation]])のため、`UserGameProject`のように本設計を使う意図が
一切ないプロジェクトでも、パッケージ参照(`GameSystemGenerator`目的での`OutputItemType="Analyzer"`
参照)だけで本Generatorが実行されてしまう。`Archetype` enum/`World`スタブを無条件に出力する初期実装は、
`UserGameProject`のグローバル名前空間に空の`World`型を紛れ込ませ、`using CSharpGameEngine.Entities;`
経由で使われている本物の`CSharpGameEngine.Entities.World`を陰から覆い隠してしまう回帰を実際に
引き起こした(C#の名前解決はグローバル名前空間の型を`using`で有効化された名前空間の型より優先する
ため、`UserGameProject/Program.cs`の`World`が生成された空の`global::World`に解決されてしまい、
`CreateEntity`/`GetRef`/`CreateArchetype`等が軒並み見つからないというビルドエラーで顕在化した)。

この回帰を`dotnet build UserGameProject/UserGameProject.csproj`で実際に検出し、修正した:
`context.CompilationProvider`と結合し、`compilation.GetTypeByMetadataName("ISystem")`が非nullの場合
(=`Runtime.cs`を意図的に取り込んだプロジェクトの場合)に限って`Archetype`/`World`/`GeneratedGameLoop`
を出力するようゲートした。`ISystem`を実装するstructごとの診断報告(`RegisterSourceOutput(systems, ...)`)
はこのゲートの影響を受けない(そちらは個々のstructが実際に`ISystem`を実装している場合にしか発火
しないため、元々安全)。

### 3. Component番号割り当てとArchetype集合の導出は「Update引数からの構造的推論」

コンパイル全体から`ISystem`実装structを完全修飾名の英字順にソートし、各Systemの`Update`オーバー
ロードを宣言順、各オーバーロードのComponentパラメータを宣言順に走査して、初めて出現したComponent型に
順番に0,1,2...を割り当てる(ユーザー確認済み: 「Archetypeの番号は0からの連番を割り当てるだけ」)。
Archetypeは、コンパイル全体の全`Update`オーバーロードが要求するComponent集合(順序無視)の和集合を
重複排除したもの(「Systemから一切参照されないComponentは何も生成しない」)。

### 4. Archetypeの命名と出力順序は別ルールで決める

命名は`"Archetype" + ソート済みインデックスの連結`(例: `{0}`→`Archetype0`、`{0,1}`→`Archetype01`、
2桁以上の衝突を避けるため最大インデックス桁数でゼロ埋め)。出力順序(enum定義順・`ListArchetypeNN`
フィールド順・`MoveArchetype`オーバーロード順・`Detach`/`PlaybackMoveArchetypes`のswitch順・メイン
ループのfor順)は「コンポーネント数昇順→インデックスタプル辞書式昇順」で並べる。これは発見順とは
異なる規則で、最終サンプルの`Archetype0,Archetype1,Archetype2,Archetype01`という並び(単一コンポー
ネント3つが先、2コンポーネントの組み合わせが最後)が、`System0`のソース宣言順`{0},{1},{0,1},{2}`とは
一致しないことから逆算して判明した。

### 5. エンティティ生成は`ArchetypeNN.Create(ref World, ComponentX...)`静的ファクトリのみ

`World.CreateEntity()`のような汎用メソッドは無い。各`ArchetypeNN` structが
`public static void Create(ref World World, ComponentX...)`を持ち、`EntityArchetypeIndices.Count`を
そのままEntity IDとして使う単純な単調増加割り当て(Generation無し)を行う。この設計により
「エンティティがどのArchetypeにも属していない」という中間状態が原理的に発生しないため、
`Archetype.None`のようなセンチネル値は不要(`Detach`の`switch`は`default: throw`だけで済む)。

### 6. ループのSubset一致でディスパッチする

各Archetypeのループでは、`ISystem`を実装する全struct(FQN順)の全`Update`オーバーロード(宣言順)の
うち、そのオーバーロードのComponent集合がArchetypeのComponent集合の部分集合であるものをすべて呼ぶ
(`Archetype01`のループが`System0`の3オーバーロードを呼ぶ一方、`Archetype2`のループは`System0`の
`{Component2}`オーバーロード1つだけを呼ぶのがこの規則)。部分集合判定は`long`ビットマスクで行う
(1コンパイルあたり最大63コンポーネントまでを実用上の上限として明記するのみで、過剰な設計はしない)。
サンプルの手書き`.g.cs`はメインループで`System0`しか呼んでいないが、ユーザー確認済みの書き漏れなので、
実装したGeneratorは`ISystem`を実装する全struct(この場合`System1`も)を対象に含める。

### 7. メインループは「呼び出し可能なメソッド」として生成する(トップレベルステートメントにしない)

サンプルの`.g.cs`前半はトップレベルステートメントとして書かれているが、これは「生成後の型をどう
使うか」の例示であり、SG自体がトップレベルステートメントとして出力するものではない。1コンパイルに
書けるトップレベルステートメントを含むファイルは1つまで(`CS8802`)という制約があり、SGがそれを安全に
保証する方法がないため。`samples/SoaSystemsDemo`の既存の前例(`GeneratedGameLoop.Update(...)`を
`DemoScenario.Run()`から呼ぶ形)とも一致させ、`internal static class GeneratedGameLoop`に
`RunFrame(ref State, ref System0, ref System1, ...)`(1フレーム分)と`Run(ref State)`(System実体を
生成し`while(state.Enabled)`で`RunFrame`を回す)を生成する。

### 8. アクセシビリティ・識別子スタイルはサンプルに忠実に従う

`ArchetypeNN` struct・`MoveCommandHeader`・`MoveToArchetypeNNCommand`・`Archetype` enum・
`World`/`State`/`System*`/`Component*`は`internal`。`ArchetypeNN`のフィールド・`Create`静的ファクトリ・
`Archetype`静的プロパティ・`World.MoveArchetype`・`World.PlaybackMoveArchetypes`は`public`。`Detach`と
`ApplyMoveArchetypeNN`は`private`。識別子(引数名・ローカル変数名含む)はPascalCaseで統一する
(リポジトリの`.editorconfig`はcamelCaseを`suggestion`重要度で推奨しているが、ビルドを失敗させる
強制力は無く、サンプルが明示的にこのスタイルを選んでいるため踏襲した)。全型はグローバル名前空間
(`namespace`宣言なし)。`Update`メソッドのアクセシビリティは`System0`が`public`、`System1`が
`internal`と混在しているため、診断は「`public`必須」ではなく「`private`でなければ良い」というゆるい
規則にした。

## 代替案として検討したもの

- **トップレベルステートメントとしてメインループを生成する**: `CS8802`(1コンパイルにつきトップ
  レベルステートメントを含むファイルは1つまで)により、Generatorが安全に保証できないため不採用。
- **`Runtime.cs`を通常の`<ProjectReference>`で配布する**: partial型はコンパイル境界を跨いで合成
  できないため技術的に不可能(決定事項1)。
- **`Archetype.None`センチネル+汎用`World.CreateEntity()`**: 初期検討時点(旧サンプル)ではエンティティ
  生成方法が固まっておらず、この構成を検討していたが、ユーザーが`ArchetypeNN.Create`静的ファクトリ
  方式へサンプルを書き直したことで、そもそも「未所属エンティティ」という状態が発生しない設計になり
  不要と判明した(決定事項5)。
- **同一Component集合・引数順違いを曖昧として弾く追加診断**: 検討したが、C#の通常のオーバーロード
  解決で実害なく解決される(引数の型が異なれば呼び出し側で一意に解決できる)ため採用しなかった。
  既存の`System0`の3オーバーロードが同時に発火する挙動自体が仕様どおりであり、これを「曖昧」として
  弾く理由が無い。
- **`Archetype`/`World`スタブを無条件出力する(初期実装)**: `UserGameProject`のビルドを壊す実際の
  回帰を引き起こしたため、`ISystem`型の実在確認によるゲートに変更した(決定事項2)。

## 帰結

### 得られたもの
- 属性を一切使わない、純粋に構造的推論(`ISystem`実装+Update引数)だけでArchetypeの閉じた集合を
  コンパイル時に導出するGeneratorを新規実装した。
- `samples/StaticArchetypeSystemsDemo`で実際に`dotnet run`し、5フレームにわたりエンティティが
  `Archetype0`/`Archetype1`から`Archetype01`へ移動し、以降定常状態(`0=0 1=0 2=1 01=3`)を保つことを
  実機確認した。
- 既存の`GameSystemGenerator`/`SceneDefinitionGenerator`/`CSharpGameEngine.Entities`/`UserGameProject`
  には(決定事項2のゲート追加以外)一切手を加えていない。

### 失われたもの・持ち越したもの
- `UserGameProject`への統合、`GameSystemGenerator`の置き換え・削除は本ADRの対象外(次パス)。
- Model構造体(`StaticArchetypeSystemResult`等)は`ImmutableArray<T>`フィールドを持つため、既定の
  等価性は要素比較ではなく参照比較になる(ADR-0015の既知の制約と同じ、許容する)。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build src/CSharpGameEngine.StaticArchetype/CSharpGameEngine.StaticArchetype.csproj -c Debug
  → 成功、警告0/エラー0(System0件でもArchetype enum(空)が生成されRuntime.csがコンパイルできることの証明)

dotnet build samples/StaticArchetypeSystemsDemo/StaticArchetypeSystemsDemo.csproj -c Debug
  → 成功、警告0/エラー0

dotnet run --project samples/StaticArchetypeSystemsDemo -c Debug
  → frame 0〜4: "0=0 1=0 2=1 01=3" (frame 0でArchetype0/Archetype1の各1体がArchetype01へ移動、
    Archetype2は不変、以降定常状態)

dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj -c Debug
  → 成功、76件(既存52件+新規24件: 診断15件・Emission6件・EndToEnd3件)、失敗0・回帰0

dotnet build UserGameProject/UserGameProject.csproj -c Debug
  → 成功、警告0/エラー0(決定事項2のゲート追加前は、グローバル名前空間の`World`型衝突により
    実際にビルド失敗することを確認済み。ゲート追加後に解消したことも実際に再ビルドして確認)

dotnet build samples/SoaSystemsDemo/SoaSystemsDemo.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build samples/SceneAuthoringDemo/SceneAuthoringDemo.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build CSharpGameEngine.slnx -c Debug
  → 成功、警告0/エラー0(全プロジェクト)

dotnet build CSharpGameEngine.slnx -c Release
  → 成功、警告0/エラー0(全プロジェクト)

dotnet test CSharpGameEngine.slnx
  → 成功、全プロジェクト合計433件(内訳: CodeAnalysis.Tests 76件(既存52件+新規24件)、
    Entities.Tests 61件、Spatial.Tests 68件、Graphics.D3D12.Tests 60件、VSExtension.Tests 39件、
    UserGameProject.Tests 21件、Import.Gltf.Tests 20件、Core.Tests 13件、SceneAuthoring.Cli.Tests
    12件、Bridge.Hosting.Tests 8件、Scene.Tests 8件、Import.Fbx.Tests 10件、Compiler.Hlsl.Tests
    7件)、失敗0・回帰0

dotnet pack src/CSharpGameEngine.CodeAnalysis -c Release
  → 成功。unzip -lで.nupkg内容を確認し、analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dllのみが
    含まれ、lib/配下には何も含まれないことを確認した(ADR-0014のNU5128抑制方針を継続)。
```

## 未解決事項

- グローバル名前空間の`Entity`/`World`/`State`/`Archetype`/`ISystem`/`IArchetype`は、将来
  `UserGameProject`へ統合する際、`UserGameProject/Scene1.cs`が既に持つ独自の`Entity`/`World`等の
  語彙(ADR-0030由来)と衝突するリスクがある。決定事項2のゲートは「無関係なプロジェクトを汚染しない」
  ことは保証するが、「本設計を実際に採用するプロジェクト内でのグローバル名前空間の衝突」は別問題
  であり、次パスで名前空間の割り当てを再検討する必要がある。
- `ISystem`実装の検出には`ForAttributeWithMetadataName`のような専用の高速パスが無く、base listを
  持つ全型宣言に対して意味解析を行う(属性ベースの他Generatorより設計上キャッシュ効率が劣る)。
  大規模プロジェクトでのコンパイル時間が問題になった場合、将来的にマーカー属性の追加を再検討する。
- `UserGameProject`統合、`GameSystemGenerator`の置き換え・削除は次パスで扱う。

## 関連機能

- F-L3-012 — `CSharpGameEngine.StaticArchetype`(`Runtime.cs`: `Entity`/`World`/`State`/`ISystem`/
  `IArchetype`/`ChunkList<T>`、新規)
- F-L7-015 — `StaticArchetypeGenerator`(Analyzer/Model/IndexAssigner/Types・World・Loop各Emitter、新規)
- F-L7-016 — `StaticArchetypeGenerator`診断(SARCH001〜015、新規)
- F-L9-008 — `samples/StaticArchetypeSystemsDemo`(新規)
