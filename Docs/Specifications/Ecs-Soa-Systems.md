# 仕様: SoA ECS + `GameSystemGenerator`(動的Archetype、引数推論Query/System)

このドキュメントは`CSharpGameEngine.Entities`(ランタイム)と`GameSystemGenerator`
(`CSharpGameEngine.CodeAnalysis`)が実装するECSの仕様を記録する。設計判断の理由と代替案は
[[ADR-0029-Dynamic-Archetype-Soa-Ecs]]を参照。旧`CSharpGameEngine.Ecs`
([[ADR-0015-Archetype-Ecs-Static-Runtime]])・旧`CSharpGameEngine.Generation.Abstractions`/
`GenerateEntityGenerator`([[ADR-0024-Generated-Entity-AoS-SoA]])はいずれも本仕様への移行に
伴い削除された。

## 概要

ユーザーはComponent型(`struct`)と、`[GameSystem]`を付けた`static class`内の`[Update]`を付けた
`static`メソッドだけを書く。Entity型・Archetype型・Query・foreach・Chunk走査・ゲームループは
一切書かない。`GameSystemGenerator`はメソッドの引数の書き方(`ref`/`in`/属性)からQuery条件・
Read/Writeアクセスを推論し、SoAループ・依存順序・単一のゲームループ(`GeneratedGameLoop`)を
生成する。

内部データレイアウトはSoAのみ(AoSの選択肢は無い)。Archetypeはコンパイル時に型として宣言せず、
実行時にEntityへ実際に付けたComponent集合から動的に生成される(Unity DOTS方式)。

```csharp
public struct Position { public float X, Y, Z; }
public struct Velocity { public float X, Y, Z; }
public readonly struct MovableTag { } // インスタンスフィールド0個 = タグ

public readonly struct FrameContext
{
    public float DeltaTime { get; init; }
}

[GameSystem]
public static class MovementSystems
{
    [Update]
    public static void Move(ref Position position, in Velocity velocity, in MovableTag _, [Context] in FrameContext frame)
    {
        position.X += velocity.X * frame.DeltaTime;
    }
}
```

実行可能な完全版は`samples/SoaSystemsDemo/`を参照(元の依頼サンプルをほぼそのまま再現)。

## 推論規則

| 引数の書き方 | 意味 |
|---|---|
| `ref X x`(`X`は非nullable struct) | Query必須+Write |
| `in X x`(`X`は非nullable struct、フィールド1個以上) | Query必須+Read |
| `in X x`(`X`はインスタンスフィールド0個の"タグ"struct) | Query必須のみ。列を持たず、`X x = default;`を行ループ外に1回だけホイストする |
| `[Context] in X x`(`X`はstruct) | Query無関係。ゲームループの引数としてそのまま`in`転送 |
| `[Context] X x`(`X`はclass) | Query無関係。ゲームループの引数としてそのまま値渡し転送 |
| `[CurrentEntity] Entity x` | Query無関係。`archetype.Entities[row]` |
| `[Commands] EntityCommandBuffer x` | Query無関係。`world.Commands` |
| メソッド属性`[Without(typeof(X))]` | QueryのNone(除外)集合に`X`を追加。**唯一の明示的な例外** - 除外条件は引数の書き方では表現できないため |

`[Context]`/`[CurrentEntity]`/`[Commands]`は同一パラメータに同時に付けられない。`ref`/`in`
Component引数は同一メソッド内で型が重複してはならない。

## Componentの認識規則

- 非nullableな`struct`のみ(class・`Nullable<T>`は不可)。
- インスタンスフィールドが0個の`struct`は「タグ」として扱われ、`ref`は禁止(`in`のみ)。

## 診断(SYSGEN001〜020)

カテゴリ`CSharpGameEngine.Entities.CodeGen`、すべて`Error`。

| ID | 内容 |
|---|---|
| SYSGEN001〜003 | `[GameSystem]`クラス自体(static/非ジェネリック/非ネスト) |
| SYSGEN004〜008 | `[Update]`メソッド自体(static/void/非ジェネリック/非private/Component引数1つ以上) |
| SYSGEN009〜012 | Component引数個々(ref/in必須、非nullable struct必須、型重複禁止、タグは`ref`禁止) |
| SYSGEN013〜017 | `[Context]`/`[CurrentEntity]`/`[Commands]`個々の検証・重複禁止 |
| SYSGEN018 | 同一クラス内での`[Update]`メソッド名重複禁止(オーバーロード非対応) |
| SYSGEN019 | 循環依存(下記「実行順序」参照) |
| SYSGEN020 | `[Without]`引数の妥当性(非nullable structかつ未使用の型であること) |

診断が1件でもある`[GameSystem]`クラスは、そのクラス分のExecutor生成を完全にスキップする。

## Archetype(動的生成)

Archetypeはコンパイル時に型として宣言しない。`World.CreateArchetype<T1..T8>()`(最大8型引数)
または構造変更によって、実行時のComponent集合(昇順ソート済み`int[]`)から
`World.GetOrCreateArchetype`が動的に生成する。既に同じ集合のArchetypeが存在すれば再利用する。

Archetypeリストは**append-only**(生成後は破棄されない)。これに`EntityQuery`の差分スキャンが
依存する。

`ComponentType<T>.Id`は型`T`ごとの`static readonly`フィールドで、初回アクセス時に列ファクトリ
デリゲート(`() => new ComponentColumn<T>()`)を1回だけ登録する。動的にArchetypeを生成する際も
このデリゲート配列を添字で呼ぶだけで済み、`MakeGenericType`等のReflectionを一切使わない。

## Query(`EntityQuery`)

`GameSystemGenerator`はメソッドごとに`private static readonly EntityQuery Query`を1つ生成する。
`EntityQuery.Update(World)`は`World.ArchetypeCount`(append-onlyなので単調増加する)を前回スキャン
時の件数と比較する差分スキャンで、新Archetypeが1件も生まれていないフレームは`int`比較1回で
完了する。新Archetypeを発見した場合のみ、その1回だけ列インデックスを2分探索する
(Query一致後の行ループ内で列インデックスの解決は発生しない)。

## 構造変更(Component追加・削除)

実行時のComponent追加・削除はArchetype間のEntity移動として実装する。

1. 移動先の行を`AddRow`で先に確保する。
2. 両Archetypeに存在する列だけを、2つの昇順`int[]`のマージ走査でO(k)コピーする
   (`CopySharedColumns`)。移動先のみの列は既定値にする。
3. 元のArchetypeからswap-back削除し、移動が発生した場合は移動したEntityの位置テーブルを
   修復する。
4. `World`の位置テーブルを新しいArchetype/行へ更新する。

2を3より先に行わないと、swap-backで再利用された行にコピー元の残骸が残る。

即時APIは`internal`であり、`[Update]`メソッドからは直接呼べない
(`EntityCommandBuffer`経由のみ)。Query一致後の行ループ中に列配列が`Array.Resize`されて
`Span<T>`が無効化される事故を設計で防ぐため。

## `EntityCommandBuffer`

`Add<T>(Entity)`/`Add<T>(Entity, in T)`/`Remove<T>`/`Destroy`/`CreateEntity`をFIFOで記録し、
`Playback(World)`で1件ずつ適用する。

- 既存Component + 値なし`Add<T>`: no-op(既存値を潰さない)。
- 既存Component + 値付き`Add<T>`: 値だけ上書き(Archetype移動なし)。
- 存在しないComponentへの`Remove<T>`: no-op。
- 死んでいるEntityへの操作: no-op(例外を投げない)。
- `CreateEntity`は負のIdを持つ一時ハンドル(`Entity.Temporary`)を返す。`World.IsAlive`は
  `Id>=0`を先に見るため、Playbackで実体化されるまで自動的に「生存していない」扱いになる。
  同一バッファ内で`CreateEntity`直後に`Add<T>(その一時ハンドル, value)`を積める。

`GameSystemGenerator`は`GeneratedGameLoop.Update`の最後に必ず`world.Commands.Playback(world)`を
生成する。構造変更は「安全な地点」= 1フレームの全`[Update]`メソッドが完了した後、にまとめて
適用される。

## 実行順序(`GameSystemScheduler`)

単一スレッド前提で、実行順序の決定のみを行う(並列実行の有効化は対象外)。

- **Write→Read**: AがComponent CをWrite・BがCをRead(A≠B)なら A→B(生産者が先)。唯一の
  意味的に確実な制約。
- **Write→Write**: A・BがともにCをWriteなら、baseline順(下記)の小さい方→大きい方
  (決定的なタイブレーク)。
- **Read→Write**: エッジを張らない。「書き込み前を見たいか後を見たいか」は引数からは
  推論不能で、張ると容易に循環するため。

baseline順は`(Order, ClassFullyQualifiedName ordinal, MemberIndex, MethodName ordinal)`。
`Order`は`[Update(Order = n)]`(既定0)。**ファイルパスは使わない**(マシン依存でビルド間の
バイト同一性が崩れるため)。

Kahn法+「利用可能な中でbaseline順最小を優先」のタイブレークで安定トポロジカルソートする。
循環を検出した場合はSYSGEN019をErrorとして報告した上で、`GeneratedGameLoop`自体はbaseline順で
出力する(既存2ジェネレータの「診断が1件でもあれば出力しない」ゲートからの意図的な逸脱。
ループを丸ごと省くと、呼び出し側で無関係な「型が見つからない」という二次エラーが大量発生する
ため)。

## `GeneratedGameLoop`

`.Collect()`でコンパイル内の全`[GameSystem]`クラスを集約し、単一の
`CSharpGameEngine.Generated.GeneratedGameLoop.Update(World world, ...)`を生成する。

引数は`World world`に続けて、コンパイル内で使われている`[Context]`型の**重複排除した集合**を
FQN Ordinal順に並べたもの。パラメータ名は単純型名の先頭を小文字化した名前(衝突時は連番、
C#予約語なら`@`エスケープ)なので、名前付き引数で安全に呼べる:

```csharp
GeneratedGameLoop.Update(world, stageRenderer: renderer, win32Window: window, frameContext: frame);
```

本体は実行順序どおりに各`{Class}_{Method}_Executor.Execute(world, ...)`を呼び、最後に
`world.Commands.Playback(world)`を呼ぶ。

## 生成されるExecutor

メソッドごとに`internal static class {ClassName}_{MethodName}_Executor`という、ユーザーの
`[GameSystem]`クラスと同じ名前空間の**独立した型**を生成する(クラスに何も追加しない)。
クラス名を含めるのは、メソッド名の重複はクラス内でのみSYSGEN018が禁止しており、異なる
`[GameSystem]`クラスが同名メソッド(例: 複数クラスの`Move`)を持つのは正当なため
(クラス名を含めない設計だと同一名前空間で型名が衝突するバグになる。実装時に発見・修正した)。

`Execute`メソッドは`EntityQuery.Update`→マッチしたArchetypeごとに`Span<T>`/`ReadOnlySpan<T>`を
取得→行ループでユーザーメソッドを呼ぶ、という構造。行ループ内には`typeof(`/`Has<`/
`.Contains(`/`Dictionary`/`GetType()`が一切現れない(テストで機械的に検証)。

## 既知の制約

- `World.CreateArchetype<T1..T8>()`は最大8型引数(9個以上のComponentを持つEntityは、
  一部を`EntityCommandBuffer`経由の構造変更で後付けする必要がある)。
- `ComponentMask`は固定256bit(4×`ulong`)。Component型は256種類まで
  (`ComponentTypeRegistry.MaxComponentTypes`)。
- Component型IDはプロセスローカルで静的初期化順に依存する。ID値に依存する外部可視の挙動を
  作ってはならない(テストでIDや列インデックスを直接assertしない)。
- `[Context]`の型集合が変わるとゲームループのシグネチャも変わる。サイレントな破綻ではなく
  明示的なコンパイルエラーになる。
