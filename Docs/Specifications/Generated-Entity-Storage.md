# 仕様: Generated Entity Storage(AoS / ECS SoA)

このドキュメントは`GenerateEntityGenerator`(`CSharpGameEngine.CodeAnalysis`)が生成するEntityの
仕様を記録する。設計判断の理由と代替案は[[ADR-0024-Generated-Entity-AoS-SoA]]を参照。

## 概要

ユーザーは`[CSharpGameEngine.Generation.GenerateEntity(EntityGenerationSettings.Kind)]`を付けた
`partial struct`(例: `Character`)と、その中の`partial`プロパティ(Component宣言)だけを書く。
`EntityGenerationSettings.Kind`という**1つの`const`**を`Aos`または`EcsSoa`に
切り替えるだけで、対象となる全Entity型の物理ストレージ実装が一括で切り替わる。切り替えは
コンパイル時にのみ発生し、生成コードにRuntime分岐(`if`/`switch`)は一切残らない。

具体例は`samples/AosSoaGeneratedExpansion/AosSoaGeneratedExpansion.SingleFile.cs`(見本、
デモ専用でAoS/SoA両方を1ファイルに含む)を参照。

## ユーザーコードの形

```csharp
[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Character
{
    public partial LocalTransform Transform { get; set; }
    public partial Velocity Velocity { get; set; }
    public partial Health Health { get; set; }

    public void Move(float deltaTime) { /* TransformRef/VelocityRefを使う */ }
    public static Character Spawn(CharacterStore store, ...) { /* store.Create(...)を使う */ }
}

public static class EntityGenerationSettings
{
    public const CSharpGameEngine.Generation.EntityStorageKind Kind =
        CSharpGameEngine.Generation.EntityStorageKind.EcsSoa;
}
```

`EntityGenerationSettings`は`CSharpGameEngine.Generation.Abstractions`が提供する契約ではなく、
ユーザーが自分のプロジェクトへ書く慣習(命名パターン)である。Generatorは
このクラス名を一切参照せず、属性引数の**解決済みの値**だけを見る。

## Componentプロパティの認識規則

次の条件を**すべて**満たすプロパティだけをComponentとして認識する。

- 非`static`
- `partial`
- 通常プロパティ(indexerではない)
- `get`と`set`の両方を持つ(`init`のみは非対応)
- 型が非nullableな`unmanaged`構造体(`ITypeSymbol.IsUnmanagedType`)

`partial`が付いていないが`get`/`set`を両方持つ非static・非indexerプロパティは、
「Componentのつもりで`partial`を付け忘れた」可能性が高い形として`ENTGEN005`を報告する
(黙って無視しない)。それ以外の通常メソッド・staticメンバー・constは一切Componentとして
数えず、診断も出さない。

対応範囲はtop-levelまたはnested `partial struct`(nestedの場合、containing型の連鎖すべてが
`partial`であること)。非genericのみ対応。

## constによる一括切り替え

属性引数(`EntityStorageKind`)はRoslynの`AttributeData.ConstructorArguments`から取得する。
C#は属性引数にコンパイル時定数を要求するため、ソース側が`EntityGenerationSettings.Kind`という
`const`フィールド参照であっても、Roslynが属性バインディング時点で解決済みの値を返す。
Generator側で`SemanticModel.GetConstantValue()`を手動で呼び出す必要はない。

`GenerateEntityGenerator`はこの解決済み値によって、**Generator自身の内部**でどちらの
Emitterを呼ぶか分岐する。出力される生成ソースコードには、選択されなかったBackendの
Hintファイルが一切生成されず、`EntityStorageKind`を比較するRuntime分岐も一切現れない。

### 両指定(比較・計測モード)

`EntityStorageKind`は`[Flags]`(`Aos = 1`、`EcsSoa = 2`)であり、両方を指定すると
**1つの宣言から両バックエンドが生成される**。AoSとSoAを同一プロセスで比較計測するためのモード。

```csharp
public const EntityStorageKind Kind =
    EntityStorageKind.Aos | EntityStorageKind.EcsSoa;
```

生成物は次のように変わる。

| 生成物 | 単一指定 | 両指定 |
|---|---|---|
| `{Type}CreateInfo` / `{Type}GeneratedMetadata` | 1回 | 1回(共通) |
| Facade(ユーザーのpartial struct本体) | 選択Backendに束縛 | **AoS側にのみ**束縛、1回だけ出力 |
| Store | `{Type}Store` | `{Type}AosStore` / `{Type}SoaStore` |
| ChunkView | `{Type}ChunkView` | `{Type}AosChunkView` / `{Type}SoaChunkView` |
| 行/アーキタイプ型 | `{Type}AosGeneratedData` / `{Type}SoaGeneratedArchetype` | 同左(元から接尾辞付き) |
| `Store.Create`の戻り値 | `{Type}`(Facade) | AoS: `{Type}` / SoA: `Entity` |

**両指定時にSoA側へFacadeが生成されない理由**は、設計の選好ではなくC#の言語制約である。

- `partial`プロパティは実装宣言を**厳密に1つ**しか持てない。
- Facadeの`_store`は**具体クラス型のフィールド**であり、型を1つしか取れない。

したがってSoA側の単体アクセスは`{Type}SoaStore.Get{Name}Ref(entity)`で行う
(この理由から`Get{Name}Ref`は`public`)。バッチ経路(`ChunkView`)はFacadeを使わないため、
両バックエンドで完全に対等に扱える。

単一指定時の型名・アクセシビリティ・挙動は`[Flags]`化の前後で一切変わらない(後方互換)。

## Facadeのコピー意味(両Backend共通)

公開される`Character`は、AoS/SoAどちらの場合も同一の軽量ハンドルである。

```csharp
public partial struct Character : IEquatable<Character>
{
    private readonly CharacterStore? _store;
    private readonly CSharpGameEngine.Ecs.Entity _entity;
}
```

`Character`をコピーすることは、常にこのハンドル(Store参照+Entity)をコピーすることであり、
Component値そのものをコピーすることはない。したがって`copy.HealthRef.Value = 10;`のような
変更は、コピー元・コピー先の区別なく同じEntityへ反映される。

`Entity`の型は既存の`CSharpGameEngine.Ecs.Entity`(Id+Generation)をそのまま再利用する
(新規の`EntityId`型は作らない)。

各Componentには2つのアクセサが生成される。

- `{Name}`(get/set): 値のコピーを返す/書き込む通常プロパティ。
- `{Name}Ref`(ref): Store内の実データへの直接参照。in-place変更に使う。

Store側にも`public ref {ComponentType} Get{Name}Ref(Entity)`が生成される。Facadeの`{Name}Ref`は
これを呼んでいるだけであり、両指定モードでFacadeを持たないSoA側の単体アクセス経路でもある。

## AoSの正本

`{Type}Store`が内部に保持する`CSharpGameEngine.Ecs.AosEntityStore<{Type}AosGeneratedData>`が
唯一の正本。`{Type}AosGeneratedData`(internal struct、宣言順フィールド)の密配列+
Location Table(`Entity.Id -> 行番号`)を持ち、削除はswap-backで行う。

`{Type}Store`はAoSでは`CharacterStore`のようにコンストラクタ引数を取らない
(AoSにはアーキタイプ共有の概念がなく、生成型ごとに専用のストレージを持つため)。

## SoAの正本

既存の`CSharpGameEngine.Ecs.World`/`ArchetypeStorage`/`ComponentColumn<T>`をそのまま正本として
再利用する。`{Type}Store`のコンストラクタは`World`を受け取り、`World.CreateEntityCore<T>()`/
`World.GetComponentRef<T>()`/`World.Destroy()`を直接呼び出す。

**単一Chunk方式**: このエンジンのSoAストレージはページ化されたChunkを持たない
(`ArchetypeStorage`は正規化済みコンポーネント集合ごとに1つだけ存在する、伸長可能なフラット配列)。
`World.TryGetArchetypeChunk<T>`は、その正規化済み集合に対応する`ArchetypeStorage`全体を
1つの`ArchetypeChunk`として返す。物理的なページ分割は行わない(ADR-0024参照)。

**アーキタイプ共有の挙動**: `ArchetypeSignature`は`Type[]`シーケンス一致だけで
`ArchetypeStorage`を検索する。したがって、正規化後のComponent集合が同じであれば、
**異なる生成型**(例: `Character`と`CharacterAlias`)であっても同一の`ArchetypeStorage`
(=同一のChunk)を共有する。これは`World.CollectViews<T>`が既に持つ挙動と同じであり、
バグではなく意図された設計である。`store.TryGetChunk(out view)`が返す`view`には、
その正規化済み集合を持つ**すべての**生成型の行が含まれ得る。

## Entity・Generation・Locationの分離

`Entity.Index`はComponent配列の直接Indexではない。常に次の経路で解決する。

```text
Entity.Id -> Generation/Alive判定 -> Location Table -> 行(AoS) / Chunk内の行(SoA)
```

削除後にIdを再利用する場合はGenerationを増分し、古いハンドルへのアクセスを
`InvalidOperationException`で拒否する。Swap-back removalで別Entityが移動した場合は、
移動したEntityのLocation Tableだけを更新し、Entity自体(Id・Generation)は変更しない。

## Component Signatureの正規化

コンポーネント型の完全修飾名(FQN)を`StringComparer.Ordinal`でソートして正規化順を決定する
(`EcsArchetypeAnalyzer`と同じ規約)。ソース宣言順は正規化順の根拠にしない。

- ユーザー向け生成メンバー(`CreateInfo`のフィールド、`Character`のpartialプロパティ実装、
  `XxxRef`、`ChunkView`の各プロパティ)は**宣言順**で出力する。
- `{Type}GeneratedMetadata.NormalizedSignature`、SoAマーカー構造体の`ComponentTypes`/
  `CreateColumns()`、`World.GetComponentRef<T>`へ渡す`columnIndex`は**正規化順**を使う。

## 単体アクセスとバッチアクセス

- 単体: `character.Transform`/`character.TransformRef`(Facade経由、内部でLocation Table参照)。
- バッチ: `store.TryGetChunk(out {Type}ChunkView view)` → `view.Rows`(AoS)または
  `view.Transform`/`view.Velocity`/...(SoA、コンポーネントごとの`Span<T>`)。
  バッチループ内ではEntityごとのLocation Table参照・Facade構築・Dictionary検索・
  Reflection・boxing・interface dispatchのいずれも発生しない。

## 生成対象外メンバー

通常のインスタンスメソッド・staticメソッド・const・補助型はComponentとして数えず、
本体も書き換えない。これらは生成された`TransformRef`/`CharacterStore`/`CharacterCreateInfo`/
`Create`/`Destroy`/`Entity`/`IsAlive`などの生成メンバーをそのまま参照できる。

## 未対応構文(このマイルストーンでは実装しない)

- generic Entity型、継承ベースEntity
- Componentの動的Add/Remove、Archetype Migration
- AoS側でのアーキタイプ共有(SoAのみ対応。プロンプト自身が許容する「per-type storeを
  第一段階として実装」の扱い)
- 固定容量ページングによる複数物理Chunk(単一Chunk方式を採用)
- 両指定モードでのSoA側Facade生成(言語制約。上記「両指定(比較・計測モード)」参照)
- Query/Systemフィルタ基盤、Job Scheduler、自動並列化
- Prefab、Scene serialization、Network replication、Undo/Redo

## Diagnostic一覧

`ENTGEN001`〜`ENTGEN015`(カテゴリ`CSharpGameEngine.EntityGeneration.CodeGen`、すべて`Error`)。

| ID | 内容 |
|----|------|
| ENTGEN001 | 対象が`partial struct`でない |
| ENTGEN002 | 対象がgeneric(未対応) |
| ENTGEN003 | nestedな対象のcontaining型が`partial`でない |
| ENTGEN004 | 認識対象コンポーネントプロパティが0件 |
| ENTGEN005 | get+set・非static・非indexerの候補プロパティが`partial`でない |
| ENTGEN006 | `partial`コンポーネントプロパティが`static` |
| ENTGEN007 | `partial`コンポーネントプロパティがindexer |
| ENTGEN008 | `partial`コンポーネントプロパティにgetまたはsetが無い |
| ENTGEN009 | setterが`init`のみ |
| ENTGEN010 | コンポーネント型がnon-nullable unmanaged structでない |
| ENTGEN011 | 同一コンポーネント型を複数プロパティで使用(重複) |
| ENTGEN012 | ユーザー定義メンバーが生成メンバー名(`Entity`/`IsAlive`/`Destroy`/`Count`/`Entities`/
  `{Name}Ref`)と衝突 |
| ENTGEN013 | 生成型名(`{Type}CreateInfo`/`Store`/`GeneratedMetadata`/`ChunkView`/
  `AosGeneratedData`/`SoaGeneratedArchetype`)が既に宣言済み |
| ENTGEN014 | 属性の`EntityStorageKind`引数が定数解決できない、または未定義の値 |
| ENTGEN015 | 対象コンパイルの`LanguageVersion`がpartial property未対応(C# 13未満) |

## 既知の制約

- AoSバックエンドは生成型ごとに専用の`AosEntityStore<TRow>`を持つため、SoAと異なり
  異なる生成型間でストレージを共有しない。
- SoAバックエンドの`ArchetypeChunk`は単一の(ページ化されていない)ストレージ全体を指すため、
  見本の`ChunkCapacity`ベースの複数Chunk生成とは物理的な形が異なる。
