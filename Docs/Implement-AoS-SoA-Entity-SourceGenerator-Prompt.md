# C# Game Engine — AoS / ECS SoA Entity Source Generator 実装プロンプト

C# Game Engineに、ユーザーがオブジェクト指向的な`partial struct`を記述し、Source Generatorが属性の定数値に応じてAoSまたはECS SoAの実装を生成する仕組みを実装してください。

## 最初に行うこと

まずリポジトリ全体を確認し、次の文書とコードを読んでください。

- `AGENTS.md`
- `CLAUDE.md`
- `.github/copilot-instructions.md`
- `.github/skills/CSharpGameEngine-Documentation/SKILL.md`
- `Docs/Architecture.md`
- `Docs/Implementation-Status.md`
- `Docs/Conformance.md`
- `Directory.Build.props`
- `Directory.Packages.props`
- 既存のECS、World、Entity、Archetype、Chunk、CodeAnalysis関連コード
- 既存のADRとMilestone文書

次に、リポジトリ内から以下の見本ファイルを検索して読んでください。

```text
AosSoaGeneratedExpansion.SingleFile.cs
```

ファイル名が変更されている場合は、次の識別子を含む見本コードを検索してください。

```text
EntityGenerationSettings
EntityStorageKind
GenerateEntityAttribute
CharacterAosGeneratedStorage
CharacterSoaGeneratedStorage
```

この見本コードを、この機能の**実行可能な設計見本および期待動作の仕様**として扱ってください。

ただし、見本コードをそのまま本番Runtimeへコピーしないでください。見本にはAoSとSoAの両実装を1ファイルで比較するためのデモ専用インターフェイスやRuntime分岐が含まれます。本番実装ではSource Generatorが選択された実装だけを生成してください。

既存設計を無視して全面的に作り直さないでください。  
既存のビルドとテストを壊さないでください。

---

## 目的

ユーザーは次のような、オブジェクト指向的なコードだけを書きます。

```csharp
[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Character
{
    public partial LocalTransform Transform { get; set; }

    public partial Velocity Velocity { get; set; }

    public partial Health Health { get; set; }

    public void Move(float deltaTime)
    {
        ref LocalTransform transform = ref TransformRef;
        ref Velocity velocity = ref VelocityRef;

        transform.Position +=
            velocity.Value * deltaTime;
    }

    public static Character Spawn(
        CharacterStore store,
        Vector3 position,
        Vector3 velocity,
        int initialHealth = 100)
    {
        return store.Create(new CharacterCreateInfo
        {
            Transform = new LocalTransform
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = 1f
            },
            Velocity = new Velocity
            {
                Value = velocity
            },
            Health = new Health
            {
                Value = initialHealth
            }
        });
    }

    public static void ApplyDamage(
        Character target,
        int damage)
    {
        ref Health health = ref target.HealthRef;
        health.Value = Math.Max(
            0,
            health.Value - Math.Max(0, damage));
    }
}
```

ユーザーは物理ストレージ、Location Table、Chunk、Component列、Factoryのボイラーコードを書きません。

次の一つの`const`を変更するだけで、対象となる全Entity型の生成方式を一括切り替えられるようにしてください。

```csharp
public static class EntityGenerationSettings
{
    public const EntityStorageKind Kind =
        EntityStorageKind.EcsSoa;
}
```

AoSへ切り替える場合:

```csharp
public static class EntityGenerationSettings
{
    public const EntityStorageKind Kind =
        EntityStorageKind.ObjectOrientedAos;
}
```

すべての対象型は同じ定数を属性へ渡せます。

```csharp
[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Character;

[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Enemy;

[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Projectile;
```

---

## 最重要仕様

### 1. 切り替えはコンパイル時に行う

`EntityGenerationSettings.Kind`は`const enum`です。

Source Generatorは属性のコンストラクタ引数をRoslynのSemantic Modelから定数値として取得してください。

本番生成コードへ、次のようなRuntime分岐を残さないでください。

```csharp
if (EntityGenerationSettings.Kind == ...)
{
}
```

次も本番Hot pathへ残さないでください。

```csharp
switch (kind)
{
}
```

AoS選択時はAoS実装だけを生成してください。  
SoA選択時はSoA実装だけを生成してください。

見本ファイルにある`ICharacterGeneratedStorage`は、AoSとSoAを1ファイルで同時に比較するためのデモ専用です。本番生成物では原則として不要です。

### 2. 公開されるユーザーAPIは両方式で同じにする

`Character`のコピー意味をAoSとSoAで変えないでください。

次のようにしないでください。

```text
AoS:
Character自身がComponent値を内包する

SoA:
CharacterがWorld + EntityIdハンドルになる
```

これでは`Character`の代入や引数渡しの意味がモードで変化します。

両方式とも、公開される`Character`は軽量なFacade／Handleとして生成してください。

概念:

```csharp
public partial struct Character
{
    private readonly CharacterStore _store;
    private readonly EntityId _entity;
}
```

実データの正本は常にStoreまたはWorld側に一つだけ置いてください。

```text
ObjectOrientedAos
→ CharacterData[]が正本

EcsSoa
→ Archetype ChunkのComponent列が正本
```

同じComponent値をFacadeとStoreへ二重保存しないでください。

### 3. ユーザー定義メソッドは変更しない

ユーザーが書いた通常メソッド、staticメソッド、定数、補助型を生成対象のComponentとして数えないでください。

SGがComponent宣言として認識するのは、初期仕様では次の条件を満たすプロパティだけです。

```text
非static
partial
通常プロパティ
indexerではない
getとsetを持つ
Componentとして許可された型
```

ユーザー定義メソッドの本体をSource Generatorが書き換えないでください。

ユーザー定義メソッドが、次の生成メンバーを参照できるようにしてください。

```text
CharacterCreateInfo
CharacterStore
TransformRef
VelocityRef
HealthRef
Create
Destroy
Entity
IsAlive
```

Generator実行前のCompilationに、これら未生成型・メンバーに由来する一時的なエラーが存在しても、Generatorがクラッシュしないようにしてください。

### 4. EntityIdは安定したIDにする

EntityIdをComponent配列の直接Indexとして使わないでください。

正しい概念:

```text
EntityId.Index
→ Generation / Alive Table
→ Location Table
→ RowまたはChunk + Row
```

AoS:

```text
EntityId
→ Location Table
→ CharacterData[row]
```

SoA:

```text
EntityId
→ Location Table
→ Archetype
→ Chunk
→ Row
→ Component columns
```

削除後にIndexを再利用する場合はGenerationを増やし、古いEntityIdを無効化してください。

Swap-back removalで別Entityが移動した場合は、移動したEntityのLocation Tableだけを更新してください。EntityId自体は変更しないでください。

### 5. Component集合を正規化する

プロパティのソース記述順をアーキタイプ同一性や物理列順の唯一の根拠にしないでください。

次の2つは同じComponent集合です。

```csharp
public partial LocalTransform Transform { get; set; }
public partial Velocity Velocity { get; set; }
public partial Health Health { get; set; }
```

```csharp
public partial Health Health { get; set; }
public partial LocalTransform Transform { get; set; }
public partial Velocity Velocity { get; set; }
```

既存エンジンにStable `ComponentTypeId`がある場合は必ず再利用してください。

存在しない場合は、このマイルストーンに必要な最小の決定的ID方式を実装してください。ただし、次をStable IDへ使用しないでください。

```text
GetHashCode()
RuntimeTypeHandle.GetHashCode()
reflection enumeration order
Roslyn symbol enumeration order
buildごとに変わる連番
```

生成順だけを決める場合は、完全修飾Metadata Nameを`StringComparer.Ordinal`で並べても構いません。永続IDとして使う場合は、衝突検出を含む決定的方式を設計し、ADRへ記録してください。

同じComponent型を異なるプロパティ名で複数宣言した場合は、初期仕様ではAnalyzer Errorにしてください。

---

## 作成または更新するプロジェクト

既存構成に合わせて調整してください。概ね次の責務が必要です。

```text
src/
  CSharpGameEngine.Generation.Abstractions/
  CSharpGameEngine.CodeAnalysis/
  CSharpGameEngine.ECS/
  または既存Runtime/ECSプロジェクト

tests/
  CSharpGameEngine.CodeAnalysis.Tests/
  CSharpGameEngine.ECS.Tests/
```

### `CSharpGameEngine.Generation.Abstractions`

ユーザーコードから参照する最小契約を置いてください。

```csharp
public enum EntityStorageKind
{
    ObjectOrientedAos,
    EcsSoa
}

[AttributeUsage(
    AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class GenerateEntityAttribute : Attribute
{
    public GenerateEntityAttribute(
        EntityStorageKind kind)
    {
        Kind = kind;
    }

    public EntityStorageKind Kind { get; }
}
```

既存の同等契約がある場合は重複作成しないでください。

この契約AssemblyへRoslyn依存を入れないでください。

### `CSharpGameEngine.CodeAnalysis`

Incremental Source GeneratorとAnalyzerを実装してください。

要件:

```text
IIncrementalGenerator
ForAttributeWithMetadataNameを優先
決定的出力
nullable有効
mutable static stateなし
ファイルI/Oなし
Networkアクセスなし
Runtime Reflectionなし
CancellationToken伝播
```

Generator AssemblyはRuntimeの通常参照にしないでください。

ゲームプロジェクトからはAnalyzer参照として利用してください。

```xml
<ProjectReference
    Include="..\CSharpGameEngine.CodeAnalysis\CSharpGameEngine.CodeAnalysis.csproj"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

### Runtime / ECS

既存の次の型があれば再利用してください。

```text
World
EntityId
EntityLocation
Archetype
ArchetypeSignature
Chunk
ComponentTypeId
Query
```

既存Runtimeがまだない場合だけ、見本コードの動作を成立させる最小実装を追加してください。

Source Generator内へWorld、Chunk、Entityの実行時状態を持たせないでください。

---

## Source Generator入力モデル

各対象型から、少なくとも次を抽出してください。

```text
namespace
containing types
type name
accessibility
struct modifiers
storage kind
component property name
component type symbol
component nullability
component ordering key
```

初期仕様では、対応範囲を明確に限定して構いません。

推奨する最初の対応範囲:

```text
top-levelまたはnested partial struct
非generic Entity型
非static partial component property
get/set
unmanaged Component
```

未対応構文を黙って誤生成せず、Diagnosticを出してください。

Generator内部ではRoslyn Symbolを長寿命staticへ保存しないでください。

Syntax/Symbolから、比較可能な不変モデルへ早い段階で変換してください。

概念例:

```csharp
internal readonly record struct EntityGenerationModel(
    string Namespace,
    string TypeName,
    EntityStorageKind StorageKind,
    ImmutableArray<ComponentModel> Components);
```

巨大な将来用抽象階層を先回りして作らないでください。

---

## 共通生成コード

AoS／SoAのどちらでも、必要に応じて次を生成してください。

### `CharacterCreateInfo`

```csharp
public struct CharacterCreateInfo
{
    public LocalTransform Transform;
    public Velocity Velocity;
    public Health Health;
}
```

### `CharacterGeneratedMetadata`

```csharp
internal static class CharacterGeneratedMetadata
{
    internal static ReadOnlySpan<ComponentTypeId> Signature =>
    [
        ComponentTypeId.Of<Health>(),
        ComponentTypeId.Of<LocalTransform>(),
        ComponentTypeId.Of<Velocity>()
    ];
}
```

### `Character`のpartial実装

概念:

```csharp
public partial struct Character
{
    private readonly CharacterStore _store;
    private readonly EntityId _entity;

    internal Character(
        CharacterStore store,
        EntityId entity)
    {
        _store = store;
        _entity = entity;
    }

    public EntityId Entity => _entity;

    public bool IsAlive =>
        _store is not null &&
        _store.IsAlive(_entity);

    public partial LocalTransform Transform
    {
        get => _store.GetTransformRef(_entity);
        set => _store.GetTransformRef(_entity) = value;
    }

    public ref LocalTransform TransformRef =>
        ref _store.GetTransformRef(_entity);
}
```

全Componentについて同様に生成してください。

Property getterが値コピーを返すことと、`XxxRef`が直接変更用であることを文書化してください。

生成コードには次を含めてください。

```text
// <auto-generated/>
#nullable enable
```

Hint Nameは完全修飾型名から決定的に生成し、同名型やnested typeで衝突しないようにしてください。

---

## AoS選択時の生成

`EntityStorageKind.ObjectOrientedAos`の場合、AoS実装だけを生成してください。

概念的な物理配置:

```text
CharacterData[]
┌──────────────────────────────────────┐
│ Transform0 │ Velocity0 │ Health0     │
├──────────────────────────────────────┤
│ Transform1 │ Velocity1 │ Health1     │
└──────────────────────────────────────┘
```

生成対象の概念例:

```csharp
internal struct CharacterGeneratedData
{
    internal LocalTransform Transform;
    internal Velocity Velocity;
    internal Health Health;
}
```

Storeは少なくとも次を持ちます。

```text
CharacterGeneratedData[]
EntityId[] row entities
EntityId.Index -> row Location Table
Count
Create
Destroy
IsAlive
GetComponentRef
batch operation entry
```

削除は初期実装ではSwap-backで構いません。

AoSでもEntityIdを`CharacterGeneratedData[]`の直接Indexとして固定しないでください。

理由:

```text
削除による穴
Swap-back
将来のcompact
将来のpartition
```

AoSのバッチ処理では、`CharacterGeneratedData[]`を直接走査できる経路を用意してください。

---

## ECS SoA選択時の生成

`EntityStorageKind.EcsSoa`の場合、SoA実装だけを生成してください。

概念的な物理配置:

```text
Archetype
└ Chunk[]
   ├ EntityId[]
   ├ LocalTransform[]
   ├ Velocity[]
   └ Health[]
```

既存ECSにWorld／Archetype／Chunkが存在する場合、型ごとに独立した偽物のECS Runtimeを生成しないでください。

その場合、SGが生成するものは主に次です。

```text
正規化済みArchetype Signature
typed factory
typed handle facade
typed query
typed chunk view
typed row view
component ref accessors
```

既存ECSがまだない場合は、見本コードと同等の最小ChunkストレージをRuntime側へ実装してください。

生成するChunk Viewの概念:

```csharp
public readonly ref struct CharacterChunkView
{
    public int Count { get; }

    public Span<EntityId> Entities { get; }

    public Span<LocalTransform> Transforms { get; }

    public Span<Velocity> Velocities { get; }

    public Span<Health> Health { get; }
}
```

SystemのHot loopでは次を可能にしてください。

```csharp
foreach (CharacterChunkView chunk
         in query)
{
    Span<LocalTransform> transforms =
        chunk.Transforms;

    Span<Velocity> velocities =
        chunk.Velocities;

    for (int row = 0;
         row < chunk.Count;
         row++)
    {
        transforms[row].Position +=
            velocities[row].Value *
            deltaTime;
    }
}
```

この経路ではEntityごとの次の処理を行わないでください。

```text
Location Table lookup
Dictionary lookup
Reflection
interface dispatch
boxing
Facade construction
```

単体操作は`Character.Transform`などのFacade API、バッチ処理はChunk Viewという二経路にしてください。

---

## アーキタイプの共有

既存ECSが同じComponent集合を持つEntityを同じRuntime Archetypeへ統合する設計なら、生成型名ごとに別アーキタイプを作らないでください。

次の2型が同じComponent集合を持つ場合:

```csharp
[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Character
{
    public partial LocalTransform Transform { get; set; }
    public partial Velocity Velocity { get; set; }
}

[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct MovingProp
{
    public partial Velocity Velocity { get; set; }
    public partial LocalTransform Transform { get; set; }
}
```

Runtime Archetype Signatureは同一にしてください。

ただし、`Character`と`MovingProp`という型付きFacade／Factoryは別に生成して構いません。

この共有が現マイルストーンでは既存Runtime構造上困難な場合は、制約を文書化し、見本互換のper-type storeを第一段階として実装してください。黙って両者を同一アーキタイプと主張しないでください。

---

## Diagnostic Analyzer

少なくとも次を診断してください。IDは既存規約へ合わせてください。

```text
対象型がpartial structではない
対象型がgenericで初期仕様の対応外
Componentプロパティがpartialではない
Componentプロパティがstatic
Componentプロパティがindexer
getまたはsetが不足
init-onlyが未対応
同じComponent型の重複
Component型がmanagedで未対応
生成メンバー名との衝突
生成型名との衝突
Storage Kindが未対応
partial propertyを使える言語バージョンではない
```

対象外コードでは診断を出さないでください。

ユーザーエラーでGeneratorを例外終了させず、Diagnosticを出してその型の生成だけを安全に中止してください。

---

## 生成名の衝突規則

次のようなユーザー定義メンバーとの衝突を検出してください。

```text
Entity
IsAlive
TransformRef
CharacterCreateInfo
CharacterStore
Create
Destroy
```

初期仕様では、衝突時に自動改名するよりAnalyzer Errorを推奨します。

生成型の完全修飾名とHint Nameは決定的でなければなりません。

nested typeと同名型をテストしてください。

---

## テスト

`CSharpGameEngine.CodeAnalysis.Tests`でGenerator Driverまたは適切なRoslyn Testing基盤を使用し、最低限次を検証してください。

### Generator共通テスト

- 最小のユーザーコードから生成コードが作られる
- 生成後CompilationにErrorがない
- 同じ入力から同じ出力が生成される
- Hint Nameが決定的
- `// <auto-generated/>`がある
- `#nullable enable`がある
- ユーザー定義instance methodがコンパイルできる
- ユーザー定義static methodがコンパイルできる
- 生成された`TransformRef`等をユーザーメソッドから参照できる
- property記述順を変えてもSignatureが同じ
- static methodやconstをComponentとして誤認しない

### const切り替えテスト

同じユーザーコードで、定数だけを変更してください。

AoS:

```csharp
public const EntityStorageKind Kind =
    EntityStorageKind.ObjectOrientedAos;
```

SoA:

```csharp
public const EntityStorageKind Kind =
    EntityStorageKind.EcsSoa;
```

検証:

```text
AoSではAoS出力だけが存在
SoAではSoA出力だけが存在
AoS出力にSoA Chunk型がない
SoA出力にAoS Data Row型がない
生成コードにRuntime Kind switchがない
公開Character APIが両方式で同じ
```

### AoS Runtimeテスト

- Create
- property get/set
- ref accessor
- user-defined Move
- static ApplyDamage
- copyしたHandleが同じEntityを指す
- Destroy
- stale handle rejection
- swap-back後に移動EntityのLocationが正しい
- batch MoveAll
- 配列が密に保たれる

### SoA Runtimeテスト

- Create
- 複数Chunk作成
- property get/set
- ref accessor
- user-defined Move
- static ApplyDamage
- copyしたHandleが同じEntityを指す
- Destroy
- stale handle rejection
- swap-back後に移動EntityのLocationが正しい
- Chunk Viewから列へ直接アクセスできる
- batch MoveAll
- EntityIdをComponent配列の直接Indexに使っていない

### Analyzerテスト

各Diagnosticについて、対象コードで出ることと対象外コードで出ないことを確認してください。

### 見本コードとのConformanceテスト

`AosSoaGeneratedExpansion.SingleFile.cs`を直接本番コードへ参照するのではなく、見本の観測可能な動作をテストへ移してください。

最低限:

```text
Character.Spawn
Character.Move
Character.ApplyDamage
Character.IsDead
CharacterStore.MoveAll
EntityId Generation
stale handle rejection
AoS physical semantics
SoA physical semantics
normalized signature
```

---

## パフォーマンス設計

このマイルストーンではBenchmark結果を捏造しないでください。

ただし、次をコード構造で保証してください。

```text
Hot loopにReflectionなし
Hot loopにDictionary型検索なし
Hot loopにEntityごとのLocation lookupなし
Hot loopにboxingなし
Hot loopにvirtual/interface dispatchなし
SoAはSpanまたは配列列を直接処理
AoSはData row配列を直接処理
```

単体のFacadeアクセスはLocation Tableを引いて構いません。  
バッチ処理ではChunk／Rowを直接走査してください。

必要なら後続MilestoneとしてBenchmarkDotNetベンチマークを提案してください。この作業内で、根拠なく「高速化した」と報告しないでください。

---

## Source Generator実装方針

`IIncrementalGenerator`を使用してください。

推奨パイプライン:

```text
ForAttributeWithMetadataName
→ 対象型と属性値を取得
→ component property symbolsを抽出
→ immutable generation modelへ変換
→ validate
→ stable sort
→ storage kind別にemit
```

注意:

```text
CompilationProviderを無目的に全処理へ流さない
Syntax Tree全件収集をしない
Symbolをstatic cacheしない
Environment値を出力へ混ぜない
日時を出力へ混ぜない
絶対パスを出力へ混ぜない
ファイルI/Oしない
```

出力は、小さな責務別ファイルへ分けて構いません。

例:

```text
MyGame.Character.Common.g.cs
MyGame.Character.Aos.g.cs
```

または:

```text
MyGame.Character.Common.g.cs
MyGame.Character.Soa.g.cs
```

選択されていないBackendのHint Fileは生成しないでください。

---

## 依存方向

次を守ってください。

```text
Game project
├ Runtime / ECS contracts
├ Generation.Abstractions
└ CodeAnalysisをAnalyzer参照

CodeAnalysis
├ Microsoft.CodeAnalysis
└ 必要ならRoslyn非依存の小さな共通契約

Runtime / ECS
× Microsoft.CodeAnalysis参照禁止

Generation.Abstractions
× Microsoft.CodeAnalysis参照禁止

VSIX
× Generator Assemblyを通常Runtime参照しない
```

Source GeneratorはL7相当です。  
生成されたECSコードはL3のRuntime契約だけを参照してください。

---

## 見本ファイルの扱い

`AosSoaGeneratedExpansion.SingleFile.cs`は設計見本として残してください。

ただし、本番プロジェクトで生成型と名前衝突する場合は、次のいずれかにしてください。

```text
Docs/Examplesへ移動
専用Sampleプロジェクトへ移動
main Runtime compile itemから除外
namespaceをExample専用へ変更
```

無断で削除しないでください。

見本ファイルには両Backendを同時に含める必要がありますが、本番SG出力には選択されたBackendだけを含めてください。

---

## このマイルストーンで実装しないもの

次は先回りして実装しないでください。

```text
Componentの動的Add/Remove
完全なArchetype Migration
Prefab
Scene serialization
Network replication
Undo/Redo
Inspector UI
VSIX Tool Window
Code Fix
Refactoring Provider
Job Scheduler
自動並列化
GPU storage
Managed Component
Buffer Component
Shared Component
Enableable Component
Generic Entity declaration
継承ベースEntity
Runtime storage kind切り替え
AoSとSoAの二重保持
```

既存エンジンに既にある場合は壊さず統合してください。

---

## ドキュメント

既存番号体系に従い、次を作成または更新してください。

```text
Docs/
├ Architecture.md
├ Implementation-Status.md
├ Conformance.md
├ Specifications/
│  └ Generated-Entity-Storage.md
├ Decisions/
│  └ ADR-XXXX-Generated-Entity-AoS-SoA.md
└ Milestones/
   └ MXXX-Generated-Entity-AoS-SoA.md
```

仕様書には最低限、次を記録してください。

```text
ユーザーコードの形
partial propertyの認識規則
constによる一括切り替え
AoSの正本
SoAの正本
Facadeのコピー意味
EntityIdとLocationの分離
Generationの役割
Component Signature正規化
単体アクセスとバッチアクセス
生成対象外メンバー
未対応構文
Diagnostic一覧
```

ADRには次を記録してください。

```text
公開Facadeを両Backendで統一する
物理ストレージだけをコンパイル時に切り替える
本番では選択Backendだけを生成する
Runtime分岐を残さない
EntityIdをComponent列Indexにしない
同じ値を二重保持しない
ユーザー定義メソッドを書き換えない
```

---

## 実行検証

リポジトリルートから実際に実行してください。

```text
dotnet restore
dotnet build
dotnet test
```

AoSとSoAを別々に検証してください。

既存の仕組みに合わせて、たとえば次を利用できます。

```text
dotnet test -p:EntityStorage=Aos
dotnet test -p:EntityStorage=Soa
```

またはテスト内で2つのCompilationを作成して定数を切り替えてください。

すべての警告と失敗を報告してください。  
実行していないコマンドを実行済みと報告しないでください。

---

## 完了条件

次をすべて満たした場合のみ完了としてください。

- `[GenerateEntity]`を検出するIncremental Generatorが存在する
- 属性引数の`const`値をSemantic Modelから取得する
- AoS選択時はAoSコードだけを生成する
- SoA選択時はSoAコードだけを生成する
- 生成コードにRuntime storage switchがない
- ユーザーのpartial propertiesが実装される
- ユーザー定義instance methodがそのまま動く
- ユーザー定義static methodがそのまま動く
- CreateInfoが生成される
- Factory／Storeが生成される
- Component Signatureが決定的に正規化される
- EntityIdが安定Handleである
- Generationによりstale handleを検出できる
- Location TableがAoS／SoAで正しく更新される
- SoAに型付きChunk Viewがある
- Hot loopがComponent列を直接処理できる
- AoS／SoAでCharacterの公開APIとコピー意味が同じ
- Analyzerが不正宣言を診断する
- Generator／Analyzer／Runtimeテストが成功する
- 見本コードの観測可能な動作を満たす
- RuntimeへRoslyn依存が漏れていない
- Architecture、ADR、Milestone、Implementation Statusが更新される

---

## 完了報告

最後に次を報告してください。

1. 読み取った見本ファイルの場所
2. 作成・変更したファイル
3. 追加・変更したプロジェクト
4. プロジェクト間の依存方向
5. GeneratorのTarget Framework
6. 使用したRoslynパッケージ
7. 属性値の定数解決方法
8. 生成対象プロパティの認識規則
9. AoS生成物
10. SoA生成物
11. 共通生成物
12. EntityId／Generation／Location設計
13. Signature正規化方式
14. 生成したDiagnostic一覧
15. Generatorテスト件数
16. Analyzerテスト件数
17. AoS Runtimeテスト件数
18. SoA Runtimeテスト件数
19. 実行したコマンド
20. Build結果
21. Test結果
22. 見本コードとのConformance結果
23. 未対応の構文・機能
24. 次のMilestone候補

未確定事項を推測で確定しないでください。  
次のMilestoneへ勝手に進まないでください。
