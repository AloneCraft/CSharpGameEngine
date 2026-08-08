# ADR-0024: Generated Entity Source Generator(AoS / ECS SoA)

## ステータス
Superseded by ADR-0029(動的Archetype SoA ECSへの全面移行、M0009)。本ADRが導入した
`CSharpGameEngine.Generation.Abstractions`/`GenerateEntityGenerator`(AoS/SoA選択生成)は
削除済み。SoA一本化に伴い、本ADR・M0006が記録していたAoS-vs-SoA比較ベンチマークは
再実行不能になった(実測値自体はM0006に記録として残る)。

## 背景

`Docs/Implement-AoS-SoA-Entity-SourceGenerator-Prompt.md`により、ユーザーがオブジェクト指向的な
`partial struct`を書くだけで、`[GenerateEntity(EntityGenerationSettings.Kind)]`属性の`const`引数
1つの切り替えで、AoS(Array of Structs)またはECS SoA(Struct of Arrays)の物理ストレージ実装を
Source Generatorが選択・生成する仕組みの実装依頼があった。実行可能な設計仕様として
`samples/AosSoaGeneratedExpansion/AosSoaGeneratedExpansion.SingleFile.cs`(依頼時点では
`UserGameProject/`直下にあった)が提示された — この見本はAoS/SoAを1ファイルで比較する
デモ専用実装であり、Runtime `if`分岐で両方を切り替えている。本ADRは、これを本番用に
「選択したBackendだけを生成し、Runtime分岐を残さない」形へ実装するための決定を記録する。

このリポジトリには既に、ADR-0015で追加した`CSharpGameEngine.Ecs`(`World`/`Entity`/
`ArchetypeStorage`/`ComponentColumn<T>`/`IEcsArchetype<TSelf>`)と`EcsArchetypeGenerator`
(`CSharpGameEngine.CodeAnalysis`)が存在し、Generation方式のstale-handle検出・swap-back・
アーキタイプ共有は実装・テスト済みである。本機能はこれらを可能な限り再利用し、
3つ目のGenerator(`GenerateEntityGenerator`)として追加する。

ユーザーに確認済みの決定事項:
1. SoAのChunk方式は、既存`ArchetypeStorage`を単一の暗黙Chunkとして再利用する
   (固定容量ページングは実装しない)。
2. 見本ファイルは、専用のサンプルコンソールプロジェクト(`samples/AosSoaGeneratedExpansion/`)へ
   移動する。

## 決定事項

### 公開Facadeを両Backendで統一する
`{Type}`(例: `Character`)は、AoS/SoAどちらの場合も`private readonly {Type}Store? _store`+
`private readonly CSharpGameEngine.Ecs.Entity _entity`だけを持つ軽量ハンドルとして生成する
(`GenerateEntityCommonEmitter`)。この1つのEmitterが両Backend共通の生成物
(`{Type}CreateInfo`/`{Type}GeneratedMetadata`/`{Type}`のFacade実装)を担当し、`{Type}Store`の
公開/internal API(`Create`/`IsAlive`/`Destroy`/`Get{Name}Ref`)だけを参照するため、
両Backendで一字一句同じソースが生成される。

### 物理ストレージだけをコンパイル時に切り替える
属性引数の`EntityStorageKind`はRoslynが属性バインディング時点で解決済みの
`TypedConstant`として渡ってくる(ソース側が`const`フィールド参照でも同じ)。
`GenerateEntityAnalyzer.Analyze`がこの値を読み取り、`GenerateEntityGenerator`内部の
switch文(`Aos` → `GenerateEntityAosEmitter`、`EcsSoa` →
`GenerateEntitySoaEmitter`)で分岐する。

### 本番では選択Backendだけを生成する / Runtime分岐を残さない
選択されなかったBackendの`AddSource`呼び出しは一切行われない
(`GenerateEntityGeneratorConstSwitchTests`で、生成Hintファイルの集合とソーステキストの
両方から確認)。生成ソースには`EntityStorageKind`を比較するコードも`switch`文も一切現れない。

### `Entity`型を再利用する(新規`EntityId`型を作らない)
見本は`EntityId`(Index+Generation)という独自の`record struct`を定義しているが、
既存`CSharpGameEngine.Ecs.Entity`(Id+Generation)が全く同じ役割を既に果たしているため、
二重実装を避けてそのまま再利用する。`Character.Entity`プロパティの型は
`CSharpGameEngine.Ecs.Entity`である。

### 同じ値を二重保持しない
Facadeは常にStore(AoS)またはWorld(SoA)側の実データへ`_store`経由でアクセスし、
Component値そのものを保持しない。

### ユーザー定義メソッドを書き換えない
`GenerateEntityAnalyzer`は宣言レベルのシンボル(型・プロパティ)だけを見て、メソッド本体には
一切触れない。生成前のCompilationにメソッド本体由来の一時的なエラー
(未生成の`TransformRef`/`CharacterStore`参照等)があっても、Analyzerはクラッシュしない
(`ForAttributeWithMetadataName`が宣言シンボルのみを対象にするため自然に成立する)。

### AoS: `AosEntityStore<TRow>`/`EntityAllocator`を`CSharpGameEngine.Ecs`に置く
AoSバックエンドには行ベースのストレージが既存Runtimeに存在しなかったため、汎用の
`AosEntityStore<TRow>`(public)と、内部で使う`EntityAllocator`(internal、Id+Generationの
確保/解放のみ)を新規追加した。`Entity`という識別子インフラの一部であり、Archetype専用ではない
ため`CSharpGameEngine.Ecs`に置く — これによりAoS側の生成コードもSoA側と同じ1つの依存先
(`CSharpGameEngine.Ecs`)だけで完結する。`EntityAllocator`は`World`の既存private
free-listロジックとは意図的に共有せず複製した(`World`の既存テスト済み挙動への回帰リスクを
避けるための明示的なトレードオフ)。

### SoA: 既存`ArchetypeStorage`を単一Chunkとして再利用し、ページングは実装しない
`World`に`TryGetArchetypeChunk<T>`を1メソッドだけ追加し(既存の公開API・`CollectViews<T>`の
挙動は無変更)、`ArchetypeStorage`に`GetEntitiesSpan()`/`GetColumnSpan<T>()`を追加した。
新設した`ArchetypeChunk`(public readonly struct、コンストラクタはinternal)がこれらを
Span化して公開する。`TryGetArchetypeChunk<T>`は`CollectViews<T>`と同じく「完全一致する
1アーキタイプだけ、複数アーキタイプ横断のQueryではない」制約を踏襲する。

この設計により、`Character`(SoA)は`{Type}SoaGeneratedArchetype`という`IEcsArchetype<T>`実装を
生成し、既存の`World.CreateEntityCore<T>()`/`GetComponentRef<T>()`をそのまま呼び出す。
`ArchetypeSignature`が`Type[]`シーケンス一致だけでストレージを解決するため、正規化後の
Component集合が同じ異なる生成型(例: `Character`/`CharacterAlias`)は無償で同一の
`ArchetypeStorage`(=同一のChunk)を共有する — `ArchetypeStorageSharingSpec`が
`EcsArchetypeGenerator`について既に証明している挙動をそのまま享受する。

### `CSharpGameEngine.Generation.Abstractions`を新設し、`EntityGenerationSettings`は同梱しない
`EntityStorageKind`/`GenerateEntityAttribute`は、ユーザーコードとGeneratorの両方から
参照される必要があり、かつAoSバックエンド利用者は`CSharpGameEngine.Ecs`(SoA専用の
World/Archetype機構)への依存を必要としない。既存の`EcsArchetypeAttribute`は
`CSharpGameEngine.Ecs`に直接置かれているが、それはSoA専用機能だったためであり、
AoS/SoA両対応の本機能をそこに置くのは筋が悪い。net10.0・PackageReferenceなしの
新規プロジェクトとして追加した(netstandard2.0にしなかった理由: `netstandard2.0`が
必要なのはRoslynコンパイラホストへ直接ロードされる`CSharpGameEngine.CodeAnalysis`
自身だけであり、`Generation.Abstractions`は通常の実行時参照アセンブリであるため)。

`EntityGenerationSettings`はプロンプト自身が「ユーザーが書く」ものとして例示する1つの
`const`クラスであり、Generatorもその名前を一切参照しない(属性引数の値だけを見る)ため、
Abstractionsパッケージに含めない。正準サンプルは`samples/AosSoaGeneratedExpansion/`に
実在する。

### `EntityStorageKind`を`[Flags]`とし、両指定は「比較・計測モード」とする(2026-08-05追記)
ベンチマーク(M0006)のため、AoSとSoAを**同一プロセス**で比較する必要が生じた。`Kind`は型ごとの
属性引数なので、当初は「型を2つ宣言する」か「アセンブリを2つに分ける」しか手段がなかった。

ユーザーからの提案により`[Flags]`化(`Aos = 1`、`EcsSoa = 2`)し、両方を指定すると
**1つの宣言から両バックエンドを生成**できるようにした。ただし次の言語制約があるため、
完全対称にはできない。

- `partial`プロパティは**実装宣言を厳密に1つしか持てない**。
- Facadeの`_store`フィールドは**具体クラス型**であり、型を1つしか取れない。

したがって両指定モードでは:

| 生成物 | 単一指定 | 両指定 |
|---|---|---|
| Facade(ユーザーのpartial struct本体) | 選択したBackendに束縛 | **AoS側にのみ**束縛(1回だけ出力) |
| Store | `{Type}Store` | `{Type}AosStore` / `{Type}SoaStore` |
| ChunkView | `{Type}ChunkView` | `{Type}AosChunkView` / `{Type}SoaChunkView` |
| `Store.Create`の戻り値 | `{Type}`(Facade) | AoS: `{Type}` / SoA: `Entity` |

`{Type}CreateInfo`・`{Type}GeneratedMetadata`・Facadeは両バックエンドで文字通り同一ソースに
なるため、両指定時も**1回だけ出力**する(ユーザー提案どおり重複排除)。これにより
`CS0102`/`CS0111`の重複定義は発生しない。

SoA側はFacadeを持たないため、単体アクセスは`Store.Get{Name}Ref`で行う。これに伴い
`Get{Name}Ref`を`internal`から`public`へ変更した(Facadeの`{Name}Ref`が既に同じ参照を
publicに公開しており、隠蔽している情報は無いため)。

**単一指定時の型名・アクセシビリティ・挙動は一切変更していない**(既存79テストは無改変で通過)。
両指定は比較・計測のためのモードであり、本番のゲームコードは単一指定を使う — この前提のもとで、
「公開APIを両Backendで統一する」という上記の決定は単一指定モードに対して引き続き有効とする。

### 診断ID規約: `ENTGEN`プレフィックス(`ECSGEN`と衝突させない)
カテゴリは`CSharpGameEngine.EntityGeneration.CodeGen`。`ENTGEN001`〜`ENTGEN015`
(詳細は[[Generated-Entity-Storage]]仕様書)。

### `{Type}Store`/`{Type}ChunkView`等のアクセシビリティ
`{Type}`(Facade)と`{Type}CreateInfo`は`public`(見本と同じ、外部呼び出し慣習
`Character.Spawn(CharacterStore store, ...)`のような公開静的ファクトリを自然に書けるようにする
ため`{Type}Store`も`public`にした)。`{Type}GeneratedMetadata`/`{Type}AosGeneratedData`/
`{Type}SoaGeneratedArchetype`/`{Type}ChunkView`は`internal`(実装詳細)。この構成は、
生成コード内で`internal`型を`public`メンバーのシグネチャに露出させるとCS0051/CS0053に
なるため、アクセシビリティの一貫性を保つ上でも必要だった(実装時に実際にこのビルドエラーへ
遭遇して発見した)。

## 代替案として検討したもの

- **AoSでも`ArchetypeStorage`を再利用する案**: AoSの物理レイアウト(1エンティティ1行の
  隣接データ)はSoAの列指向ストレージと根本的に異なるため、既存`ArchetypeStorage`
  (列ごとに独立した配列)の上に構築すると本末転倒になる。不採用。
- **SoAで最初から固定容量ページングによる複数物理Chunkを実装する案**: 見本によりの
  忠実だが、既存`ArchetypeStorage`(ページングなし)を書き換えるか、並行する別ストレージ型を
  追加する必要があり、コア変更のリスク・実装量に見合わないと判断(ユーザー確認済み)。
- **`EntityId`という新しい型を作る案**: 既存`Entity`と機能的に完全に重複するため不採用。

## 帰結

- `GenerateEntityAnalysisResult`/`GenerateEntityComponentModel`/`GenerateEntityContainingTypeModel`は
  `record`ではなく通常の`struct`として実装した(ADR-0014/ADR-0015と同じ理由:
  netstandard2.0が`IsExternalInit`を提供しないため)。
- Componentの並び順を「宣言順」(ユーザー向け生成メンバー)と「正規化順」
  (`NormalizedSignature`・列Index)の2系統で管理する。`World.GetComponentRef<T>`へ渡す
  `columnIndex`を誤って宣言順のIndexにすると静かに誤ったComponentを参照するバグになるため、
  Emitter実装時に特に注意が必要だった(実装時にテストで検証済み)。
- SoAの`TryGetArchetypeChunk<Character>()`は、正規化済み集合が同じであれば`CharacterAlias`
  経由で作られた行も含む。バグではなく`ArchetypeStorage`共有の直接的帰結であり、
  `GenerateEntitySoaRuntimeTests`で明示的にテストしている。

## 検証結果(実際に実行して確認)

- `dotnet build CSharpGameEngine.slnx -c Debug` / `-c Release`: いずれも成功、警告0件・エラー0件
  (`CSharpGameEngine.VSExtension`(net472)を含む全プロジェクト)。
- `dotnet test CSharpGameEngine.slnx`: 全310件成功、失敗0件
  (`CSharpGameEngine.Ecs.Tests`39件(既存22+新規17)、`CSharpGameEngine.CodeAnalysis.Tests`79件
  (既存20+新規59)を含む)。既存テストに回帰は無かった。
- `dotnet pack src/CSharpGameEngine.CodeAnalysis -c Release`: 成功。`ENTGEN`診断追加後も
  analyzers-onlyパッケージング(NU5128抑制方針)は変わらない。
- `dotnet build samples/AosSoaGeneratedExpansion/AosSoaGeneratedExpansion.csproj -c Debug`:
  成功。見本ファイル自身は内容をほぼ変更せず(`ReadOnlySpan<string>`を返す式形式プロパティが
  現在のコンパイラでCS9203になったため、バッキングフィールドを追加する最小修正のみ実施)、
  単独で`dotnet run`可能な状態を維持した。
- `dotnet build UserGameProject/UserGameProject.csproj -c Debug`: 成功。
  `CSharpGameEngine.Generation.Abstractions`への新規参照追加後も既存ゲームループは無変更で
  ビルドが通ることを確認した。
- `GenerateEntityAosRuntimeTests`/`GenerateEntitySoaRuntimeTests`/`GenerateEntityConformanceTests`:
  実際に生成されたコードを`CSharpCompilation`+`CSharpGeneratorDriver`でコンパイル・`Emit`・
  `Assembly.Load`し、リフレクションで`TestEntry`静的メソッドを実行して、Create/get・set/
  refアクセサ/ユーザー定義`Move`・`ApplyDamage`・`IsDead`/コピーしたハンドルの同一性/Destroy/
  stale handle拒否/swap-back後のLocation正しさ/ChunkViewバッチループ/2つの生成型間の
  アーキタイプ共有のすべてが、両Backendで実際のRuntime上で機能することを確認した。

## 検証結果(2026-08-05追記: 両指定モードとベンチマーク)

- `dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests`: 88件成功(既存79件 +
  `GenerateEntityCombinedKindTests` 9件)。`[Flags]`化・型名切替・アクセシビリティ変更のいずれも
  単一指定時の既存テストに回帰を起こしていない。
- 実際に`EmitCompilerGeneratedFiles=true`でスクラッチプロジェクトをビルドし、
  1つの`[GenerateEntity(Aos | Soa)] partial struct Particle`宣言から
  `Particle.Common.g.cs` / `Particle.Aos.g.cs` / `Particle.Soa.g.cs`の3ファイルが
  0エラーで生成され、`ParticleAosStore`/`ParticleSoaStore`が共存することを目視確認した。
- `UserGameProject`に実利用(`UserGameProject/Entities/Particle.cs`、1宣言)を追加し、
  `dotnet build -c Release`成功後、`UserGameProject.exe benchmark`を実行して実測値を取得した
  (数値はM0006を参照。捏造値は一切含まない)。
- `dotnet test tests/UserGameProject.Tests`: 41件成功(既存21 + 新規20)。両バックエンドが
  同一の操作列に対して同一の結果を返すこと(`BothBackends_ProduceIdenticalResults_
  AfterTheSameOperationSequence`)を含む。

## 未解決事項

- SoAの`ArchetypeChunk`は単一の(ページ化されていない)ストレージ全体を指すため、
  見本の`ChunkCapacity`ベースの物理的な複数Chunk生成は再現していない
  (「複数Chunk作成」テストは「単一Chunkが多数のCreate/Destroy後も密なまま正しく成長する」
  ことの検証に読み替えた)。
- AoSバックエンドは生成型ごとに専用の`AosEntityStore<TRow>`を持つため、SoAと異なり
  異なる生成型間でストレージを共有しない(見本互換のper-type storeとして今回は許容)。
- `[GenerateEntity]`の実利用サンプルは`UserGameProject`に追加していない
  (`UserGameProject/Scene1.cs`が同じ`UserGameProject`名前空間に独自の`Entity`/`World`/
  `Archetype`語彙を宣言しており、`Program.cs`に既に`using World = CSharpGameEngine.Ecs.World;`
  という衝突回避エイリアスがあるため、同種の衝突リスクを新規に作らなかった)。実証は
  `CSharpGameEngine.CodeAnalysis.Tests`の`CompileAndLoad`ベースのテストで行った。
- 動的Add/Remove、Archetype Migration、Query/System基盤、Job Scheduler、自動並列化は未実装
  (ADR-0015と同じ非目標を継続)。

## 関連機能
- F-L3-005 — `AosEntityStore<TRow>` / `EntityAllocator`
- F-L3-006 — `World.TryGetArchetypeChunk<T>` / `ArchetypeChunk`
- F-L7-006 — `CSharpGameEngine.Generation.Abstractions`
- F-L7-007 — `GenerateEntityGenerator`
- F-L7-008 — `GenerateEntityGenerator`診断(ENTGEN001〜015)
