# 仕様: 閉集合Archetype ECS + `GameSystemGenerator`(属性ゼロ、構造的推論)

このドキュメントは`CSharpGameEngine.Entities`(ランタイム)と`GameSystemGenerator`
(`CSharpGameEngine.CodeAnalysis`)が実装するECSの仕様を記録する。設計判断の理由と代替案は
[[ADR-0033-Static-Archetype-Ecs-Migration]]を参照。動的Archetype方式([[ADR-0029-Dynamic-Archetype-Soa-Ecs]])は
本仕様への移行に伴い削除された。

## 概要

ユーザーはComponent型(`struct`)と、`ISystem`を実装するstructの`Update`/`Spawn`メソッドだけを書く。
**属性は一切使わない**。Entity型・Archetype型・Query・foreach・ゲームループは一切書かない。
`GameSystemGenerator`はメソッド引数の書き方(位置と`ref`/`in`/値渡し)からすべての役割を推論し、
Archetype型・`World`の生成/移動API・単一のゲームループ(`GeneratedGameLoop`)を生成する。

Archetypeは**コンパイル時に閉じた集合として確定**する。実行時に任意のComponent集合を作ることはできず、
`Update`/`Spawn`で宣言した構成だけが存在する。

```csharp
using CSharpGameEngine.Entities;

public struct Position { public float X, Y, Z; }
public struct Velocity { public float X, Y, Z; }
public struct MovableTag { }

public readonly struct FrameContext
{
    public FrameContext(float deltaTime) { this.DeltaTime = deltaTime; }
    public float DeltaTime { get; }
}

internal struct MovementSystem : ISystem
{
    public bool Update(ref Position position, ref Velocity velocity, ref MovableTag _, in FrameContext frame, ref State state)
    {
        position.X += velocity.X * frame.DeltaTime;
        return true;
    }
}
```

実行可能な完全版は`UserGameProject/`(`Components.cs`・`Systems/Systems.cs`・`Program.cs`)を参照。
`Program.cs`を持たない構成(エントリポイントもSGが生成する)の実例は`UserEcsProject/`。

## 推論規則

パラメータの役割は、次の順で**構造的に**決定される。

| 位置・渡し方 | 役割 |
|---|---|
| 先頭・値渡し・型が`Entity` | Entityスロット(任意。省略可) |
| 末尾・`ref State` | Stateスロット(`Update`では必須) |
| それ以外で**`ref`**の非nullable struct | **Component**(Archetypeの構成要素) |
| それ以外(**`ref`でない**。structは`in`、参照型は値渡し) | **Context**(ゲームループの引数として転送) |

Componentは`ref`必須・Contextは`ref`禁止なので、ref-kindだけで曖昧性なく判別できる。

> **注意**: 0フィールドのタグComponent(`PlayerTag`等)も**`ref`で受ける**必要がある。`in`にすると
> Contextとして分類され、Archetype一致に参加しなくなる(コンパイルは通るため、サイレント障害になる)。

### メソッドの種類

| メソッド名 | 役割 |
|---|---|
| `Update` | Archetypeを登録し、**毎フレームのディスパッチ対象**になる。戻り値`bool`、末尾に`ref State`必須 |
| `Spawn` | **Archetype形状の宣言のみ**。ディスパッチされない。戻り値`void`、`ref` Component引数のみ(Entity/State不可) |

`Spawn`は「どの`Update`オーバーロードとも一致しない構成」を成立させるために使う。実例:
`UserGameProject`のPlayerは`Health`をどのSystemからも参照されないため、`PlayerSpawn.Spawn(...)`が
無いと8Component構成のArchetypeが生成されない。

## Componentの認識規則

- 非nullableな`struct`のみ(class・`Nullable<T>`は不可)。
- マーカー(属性・インターフェース)は不要。`Update`/`Spawn`の`ref`引数として現れた型がComponentになる。
- どのSystemからも参照されないstructは、宣言されていてもComponentにならない(何も生成されない)。
- 異なる名前空間に同じ単純名のComponentが2つあると、生成される`ArchetypeNN`のフィールド名が衝突するため
  `SYSGEN020`で弾く。

## Archetype

`Update`/`Spawn`の各オーバーロードのComponent集合と、`[SceneDefinition]`の各ノードの構成の**和集合**を
重複排除したものが、生成されるArchetypeの全体になる(オーバーロードをまたいだ和集合は作らない)。

- **命名**: 出現順の連番(`Archetype0`, `Archetype1`, …)。ユーザーコードから参照されない内部識別子。
- **フィールド/パラメータ順**: 最初にその構成を宣言した`Update`/`Spawn`の**引数順**。
- 生成物: `ArchetypeNN` struct(`Entity`+各Componentのpublicフィールド、明示コンストラクタ、
  `public static Entity Create(ref World, …)`、`public static Archetype Archetype => …`)。

## `World`

`Runtime.cs`が持つのは`EntityArchetypeIndices`(Entityの位置表)だけで、以下はすべて生成される。

| API | 用途 |
|---|---|
| `Entity Create(C0, C1, …)` | Entityを**即座に**生成する。Component型の組み合わせでオーバーロード解決されるため、Archetype名を書く必要が無い |
| `bool MoveArchetype(Entity, C0, C1, …, ref State)` | 別Archetypeへの移動を**キューへ積む**(即時ではない) |
| `void PlaybackMoveArchetypes(ref State)` | 積まれた移動をまとめて適用する。`RunFrame`の末尾で必ず呼ばれる |
| `ListArchetypeNN` | Archetypeごとの行リスト(`ChunkList<ArchetypeNN>`) |

`Detach`と`ApplyMoveArchetypeNN`は`private`。移動は「移動先へ追加 → 移動元からswap-back削除 →
位置表を修復」の順で適用され、swap-backで詰められたEntityの位置表も併せて修復される。

## 実行順序

**ソース上の記述順**がそのまま実行順になる。`Update`引数がすべて`ref`になったことでRead/Writeの区別が
無くなり、旧設計のWrite→Read自動トポロジカルソートは原理的に成立しないため、この単純な規則を採用した
(ADR-0033決定事項3)。

- Systemの実効的な順序値(`GameSystemExecutionOrder.OrderOf`): ①`[SystemOrder]`をSystem自身に
  直接付けた明示値 ②`[SystemOrder]`をfileマーカーへ付けたファイル既定値(いずれもADR-0036、
  宣言が無ければ0、①が②より優先)
- 整列キー(`GameSystemExecutionOrder.Compare`): ①上記の実効値 ②`SyntaxTree.FilePath`のOrdinal順
  (既存タイブレーク) ③`Location.SourceSpan.Start`
- 同一struct内の`Update`オーバーロード: 宣言順

Roslynがファイルを渡す順序は保証されないため、**順序が意味を持つSystemは1ファイルへ集約すること**が
基本だが、それが難しい場合は`[SystemOrder(int)]`(ADR-0036)で明示できる。2通りの付け方がある:

```csharp
// ①ISystem実装struct自身に直接 — そのSystemだけの順序
[SystemOrder(0)]
internal struct FooSystem : ISystem { ... }

// ②fileスコープのマーカークラスに — ファイル内で自分の順序を持たない全Systemの既定値
[SystemOrder(1)]
file sealed class Order
{
}
```

属性を付けなければ従来どおりファイルパスのOrdinal順にフォールバックする(後方互換)。
`UserGameProject/Systems/Systems.cs`(単一ファイル集約)と`UserEcsProject/`
(`Scenes/MainScene.cs`=`[StartupScene(Order = 0)]`から合成されるSystem、`Systems/RuntimeSystem.cs`=
fileマーカーに`[SystemOrder(1)]`)がその実例。

> 実行順序の正しさは記述する人間の責任になった。順序を誤ってもビルドは通り、症状(例: カメラが1フレーム
> 遅れる)は実行時にしか現れない。

## ディスパッチ(部分集合一致)

`GeneratedGameLoop.RunFrame`は、Archetypeごとにその行リストを**後方から**走査し
(`Detach`のswap-back削除に対して安全)、あるオーバーロードのComponent集合がそのArchetypeの集合の
**部分集合**であれば呼ぶ。

これにより1つのメソッドが複数のArchetypeへ自然に一致する。例: `{Position, Rotation, Scale,
RenderObjectRef}`を要求するメソッドは、静的プロップ(その構成そのもの)と可動プロップ(それに
`MovableTag`を加えた上位集合)の**両方**で呼ばれる。旧設計のQuery一致と同じ効果が、特別扱い無しで得られる。

戻り値が`false`のときは`continue`し、同じArchetype行に対する後続のオーバーロードをスキップする。

## 生成されるゲームループ

```csharp
internal static class GeneratedGameLoop
{
    internal static void RunFrame(ref State State, ref System0 system0, …, in FrameContext frameContext, StageRenderer stageRenderer, …);
    internal static void Run(ref State State, in FrameContext frameContext, …);
}
```

- System実体は`ref`で受け取る(可変structのフィールドがフレーム間で保持されるようにするため)。
- Context引数はコンパイル内で使われている型を重複排除し、FQN Ordinal順に並べる。パラメータ名は単純型名の
  先頭を小文字化したもの(衝突時は連番、C#予約語なら`@`エスケープ)。
- `RunFrame`の末尾で必ず`State.World.PlaybackMoveArchetypes(ref State)`を呼ぶ。
- `Run`は`while(State.Enabled)`で`RunFrame`を回すが、Context引数をループ全体で固定してしまうため、
  毎フレーム値が変わるContext(`FrameContext`等)を使う実アプリは`RunFrame`を直接呼ぶ。

## 生成されるエントリポイント(ADR-0035)

Context引数を1つも使わない実行可能プロジェクトでは、`Run`のシグネチャが`Run(ref State)`に確定するため、
Generatorが`Main`まで出力できる。`Program.cs`は不要になる。

```csharp
internal static class GeneratedEntryPoint
{
    private static void Main()
    {
        var World = new World();
        World.Create(new Bootstrap());          // 生成されたMainが作る唯一のEntity
        var State = new State(World) { Enabled = true };
        GeneratedGameLoop.Run(ref State);
    }
}
```

出力条件は①OutputKindがアプリケーション ②`Compilation.GetEntryPoint`がnull ③ディスパッチSystemが
1件以上 ④Context引数がゼロ、の4つすべて。④だけ落ちる場合はSYSGEN021で理由を報告する。

`Bootstrap`を`ref`で受け取る`Update`が「起動時に一度だけ走る処理」になり、そこから
`State.World.Create(...)`でウィンドウ・レンダラー・レベルを含む全Entityを組み立てる。エンジン
オブジェクト(`Win32Window`/`StageRenderer`)はComponentのフィールドとして持たせる — Contextにすると
ループ開始前に構築せざるを得ず、遅延生成が表現できないため。実例は`UserEcsProject/`。

順序について: 生成ループはArchetype順が外側・System順が内側で、Archetypeの出現順はSystemの並び順に
従う。したがって**1ファイルにフレーム順でSystemを並べれば、Archetypeループの順序もフレーム順になる**。
フレーム末尾に置きたい処理(Present等)は、最後のSystemが宣言する専用Componentの単独Entityにする。

## `[StartupScene]`/`[SceneEntity]`宣言(合成System)

`Bootstrap`を消費する起動処理を毎回`ISystem`として手書きする(`Bootstrap.Done`判定+
`State.World.Create(...)`)代わりに、Component初期値だけを宣言できる。

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

- `[StartupScene]`は`static partial class`が対象。`Order`は合成Systemの実効的な順序値で、
  `[SystemOrder]`をSystem自身へ直接付けた場合と同じ優先度(`GameSystemOrderContext.SystemOrderByFqn`)
  で扱われる。
- `[SceneEntity]`は`static`・getter付き・非indexer・非ref返しのプロパティが対象。戻り値が単一の
  非nullable structなら1 Componentのみのentity、`ValueTuple`(要素名必須)ならその各要素を持つentity。
- 1クラスにつき`Query = {Bootstrap}`・`Order = 属性値`の合成`ISystem`が1つ生成される
  (`{ClassName}__StartupSystem`、同じ名前空間)。中身は旧来の手書き`GameStartup`と同じ意味:
  `Bootstrap.Done`判定 → 各`[SceneEntity]`プロパティを1回だけ評価 → `state.World.Create(...)`。
- 合成Systemは他のSystemと**区別なく**同じ`GameSystemExecutionOrder`/`GameSystemIndexAssigner`/
  `GameSystemLoopEmitter`を通る(1つのGeneratorが自分自身の出力を同一実行内で再検出できないため、
  構文木からの再検出ではなく`GameSystemResult`を直接構築して合流させる。詳細は
  `StartupSceneSystemEmitter`のXMLコメント参照)。
- Entityの出力Component集合(上記の例なら`{AppHost, Level}`)は、既存Systemが同じ集合を宣言済みなら
  そちらが優先され、Archetypeの出現順は動かない(重複登録は`GameSystemIndexAssigner`のRegisterSetが
  黙って無視する)。どのSystemの引数にも現れない集合の場合だけ、既存System由来のArchetypeの後ろへ
  追加される。
- 診断IDは`STARTUP001`〜`010`(下記診断セクション参照)。
- 実例は`UserEcsProject/Scenes/MainScene.cs`(旧`GameStartup : ISystem`からの移行)。

## Scene Authoringとの統合

`[SceneDefinition]`クラスのノード構成も**同じArchetype空間**へ入る(単一の`partial struct World`/
`enum Archetype`を作るため、生成は1回しか行えない)。よって`GameSystemGenerator`が
`ISystem`実装structと`[SceneDefinition]`クラスの両方を1つのパイプラインで処理する。

生成される`CreateRuntime(ref World world)`は、ノードごとに`ArchetypeNN.Create(ref world, …)`を1回呼ぶ。
非Rootノードには`Parent`が、`Set`を1つも持たない空ノードには`SceneNodeTag`が自動的に付与される。

## `WorldSnapshot`(外部プロセス向け introspection)

`CSharpGameEngine.Entities`は共有バイナリではなく共有ソースなので(下記「消費方法」)、外部ツールは
対象プロセスの`World`と同じCLR型を共有できない。また閉集合方式には「実行時に全Archetypeを汎用列挙する」
surfaceが無い。

そのためGeneratorが`public static ArchetypeSnapshotEntry[] WorldSnapshot.Capture(in World)`を生成する。
全Archetypeの全行を走査し、`EntityId`/`ParentEntityId`/`HasParent`/`ArchetypeName`/
`object[] ComponentValues`(ボックス化)を返す。`CSharpGameEngine.SceneAuthoring.Cli`(VSIXの3Dプレビュー
ブリッジ)はこれをリフレクションで1回呼ぶだけで、対象の型を一切コンパイル時に知らない。

## 消費方法(重要)

`Runtime.cs`の`World`/`State`は`partial struct`で、Generatorが**同じコンパイル内**でそれを完成させる。
partial型はアセンブリ境界をまたげないため、`CSharpGameEngine.Entities`を`<ProjectReference>`することは
できない。消費側は**ソースとして取り込む**:

```xml
<Compile Include="..\src\CSharpGameEngine.Entities\*.cs" Link="Entities\%(Filename)%(Extension)" />
<ProjectReference Include="..\src\CSharpGameEngine.CodeAnalysis\CSharpGameEngine.CodeAnalysis.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Generatorは`CSharpGameEngine.Entities.ISystem`型がコンパイル内に実在する場合のみ出力する。これにより、
アナライザーパッケージを別目的で参照しているだけのプロジェクトを汚染しない。

## 診断(SYSGEN001〜020、SYSGEN009は欠番)

カテゴリ`CSharpGameEngine.Entities.CodeGen`、すべて`Error`。

| ID | 内容 |
|---|---|
| SYSGEN001〜003 | `ISystem`実装型(struct/非ジェネリック/非ネスト) |
| SYSGEN004〜007 | `Update`/`Spawn`メソッド自体(インスタンス/非private/`Update`は`bool`/非ジェネリック) |
| SYSGEN008 | Componentパラメータが1つ以上必要 |
| SYSGEN009 | **欠番**(到達不能。`ref`でないパラメータは壊れたComponentではなく正当なContextになるため) |
| SYSGEN010〜011 | Component型(非nullable struct必須、同一メソッド内での型重複禁止) |
| SYSGEN012 | 先頭`Entity`引数は値渡し・第1引数 |
| SYSGEN013 | `Update`は末尾に`ref State`が必要 |
| SYSGEN014 | `Entity`/`State`をComponentに使えない |
| SYSGEN015 | `Update`も`Spawn`も持たない`ISystem` |
| SYSGEN016〜017 | `Spawn`固有(`void`必須、Entity/State引数不可) |
| SYSGEN018〜019 | Context引数(`in`(struct)または値渡し(参照型)、同一メソッド内での型重複禁止) |
| SYSGEN020 | 異なるComponent型が同じ単純名を持つ(生成フィールド名が衝突する) |
| SYSGEN021 | Context引数があるためエントリポイントを生成できない(ADR-0035) |
| SYSGEN022〜025 | `[SystemOrder]`の誤用(fileマーカーが`file`スコープでない、1ファイル複数マーカー、複数ファイルが同一のファイル既定値、非ISystem structへの付与。ADR-0036) |

診断が1件でもある`ISystem` structは、そのstruct分の出力を丸ごとスキップする。

Scene Authoringの診断は`SCENE001`〜`008`(`SCENE009`はADR-0033で削除。旧
`World.CreateArchetype<T1..T8>`のジェネリック型引数上限に由来する規則で、閉集合方式には上限が無い)。

`[StartupScene]`/`[SceneEntity]`の診断は`STARTUP001`〜`010`(同じくカテゴリ`CSharpGameEngine.Entities.CodeGen`、
すべて`Error`)。

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
| STARTUP010 | Tuple要素に明示的な名前が無い(既定の`ItemN`のまま) |

診断が1件でもある`[StartupScene]`クラスは、そのクラス分の出力(合成System・Entity生成コード)を
丸ごとスキップする。

## 既知の制約

- Archetypeは`Update`/`Spawn`で宣言した閉集合に限られる。実行時に任意の構成を作ることはできない。
- 部分集合判定を`long`ビットマスクで行うため、1コンパイルあたりのComponent型は最大63種類。
- Archetype名は出現順の連番なので、Systemを増減すると既存Archetypeの番号がずれる(ユーザーコードは
  Archetype名を参照しないため実害は無いが、生成コードを名指しするテストは影響を受ける)。
- 実行順序は記述順依存。複数ファイルへ分割した場合、`[SystemOrder]`(ADR-0036)を使わなければ
  ファイルパスのOrdinal順に依存する。
- 汎用の`Add<T>`/`Remove<T>`/`Destroy`(旧`EntityCommandBuffer`)と除外条件(旧`[Without]`)は未実装。
- `ISystem`の検出に属性ベースの高速パス(`ForAttributeWithMetadataName`)が使えず、base listを持つ全型宣言に
  対して意味解析を行う。


## Spatial snapshot

When `Transform` and `SpatialBounds` share an Archetype, generated code scans that Archetype after structural playback and refreshes `World.GlobalSpatialIndex`. Systems therefore read the prior complete snapshot unless they explicitly refresh it as the sole writer.


## Windows Runtime source boundary

The generated ECS compilation imports `CSharpGameEngine.Runtime.Windows.projitems`. Runtime companion components remain in the generated archetype surface, while their implementation source is shared and precision-selected with the user compilation.

## System-major phase scheduling

Source GeneratorはSystem-majorでコードを生成し、Runtime/Editor Previewごとに永続`GeneratedSystemSet`を1つ所有する。`IWorldSystem.Update(ref State)`はcomponent queryを持たないWorld systemである。`SystemPhase`はFrameInput、FixedSimulation、PhysicsSimulation、PostPhysics、Presentationを定義し、`SystemOrder`後にphase内の決定順で実行する。既定ユーザーSystemはFixedSimulation、bootstrapはFrameInputである。
