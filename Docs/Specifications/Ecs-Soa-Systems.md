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

- Systemの整列キー: `(SyntaxTree.FilePath のOrdinal順, Location.SourceSpan.Start)`
- 同一struct内の`Update`オーバーロード: 宣言順

Roslynがファイルを渡す順序は保証されないため、**順序が意味を持つSystemは1ファイルへ集約すること**
(ファイルパスは複数ファイルにまたがった場合の決定性を担保するタイブレークとしてのみ使う)。
`UserGameProject/Systems/Systems.cs`がその実例。

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

診断が1件でもある`ISystem` structは、そのstruct分の出力を丸ごとスキップする。

Scene Authoringの診断は`SCENE001`〜`008`(`SCENE009`はADR-0033で削除。旧
`World.CreateArchetype<T1..T8>`のジェネリック型引数上限に由来する規則で、閉集合方式には上限が無い)。

## 既知の制約

- Archetypeは`Update`/`Spawn`で宣言した閉集合に限られる。実行時に任意の構成を作ることはできない。
- 部分集合判定を`long`ビットマスクで行うため、1コンパイルあたりのComponent型は最大63種類。
- Archetype名は出現順の連番なので、Systemを増減すると既存Archetypeの番号がずれる(ユーザーコードは
  Archetype名を参照しないため実害は無いが、生成コードを名指しするテストは影響を受ける)。
- 実行順序は記述順依存。複数ファイルへ分割した場合の順序はファイルパスのOrdinal順に依存する。
- 汎用の`Add<T>`/`Remove<T>`/`Destroy`(旧`EntityCommandBuffer`)と除外条件(旧`[Without]`)は未実装。
- `ISystem`の検出に属性ベースの高速パス(`ForAttributeWithMetadataName`)が使えず、base listを持つ全型宣言に
  対して意味解析を行う。
