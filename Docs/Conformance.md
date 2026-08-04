# Conformance

このファイルは`.github/skills/CSharpGameEngine-Documentation/SKILL.md`が定義するConformanceモデル
(意図した仕様 ↕ 実装 ↕ リバースエンジニアリングした観測挙動 ↕ テスト/実行検証)に従う。

このファイルは**ADR-0024(Generated Entity Source Generator)由来のエントリのみ**を含む。
既存機能(ADR-0001〜ADR-0023相当)への遡及的なBackfillは行っていない
(`Docs/Implementation-Status.md`がADR-0014から遡及なしで開始した前例に倣う)。

---

### RE-L3-005 — `AosEntityStore<TRow>` / `EntityAllocator`

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Ecs/AosEntityStore.cs
  - src/CSharpGameEngine.Ecs/EntityAllocator.cs
  - tests/CSharpGameEngine.Ecs.Tests/AosEntityStoreSpec.cs、EntityAllocatorSpec.cs
- Observed behavior:
  - `Entity`は行配列の直接Indexではなく、常にLocation Table経由で行番号へ解決される。
  - 削除はswap-backで行い、移動したEntityのLocationだけが更新される。
  - Id再利用時にGenerationが増分され、古いハンドルは`IsAlive`が`false`を返し、
    `GetRowRef`/`Destroy`が`InvalidOperationException`を投げる。
- Intended behavior:
  - Docs/Specifications/Generated-Entity-Storage.md「AoSの正本」「Entity・Generation・
    Locationの分離」節と同一。
- Result: Matches

### RE-L3-006 — `World.TryGetArchetypeChunk<T>` / `ArchetypeChunk`

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Ecs/World.cs、ArchetypeChunk.cs、ArchetypeStorage.cs
  - tests/CSharpGameEngine.Ecs.Tests/ArchetypeChunkSpec.cs
- Observed behavior:
  - 該当アーキタイプのEntityが1件も無ければ`false`(`Count=0`、空の`Entities`)。
  - 1つの`ArchetypeStorage`全体がそのまま1つの`ArchetypeChunk`として返る
    (固定容量ページングは行われない)。
  - 正規化後の構成が同じ異なる`IEcsArchetype<T>`型は同一のChunkを共有する
    (`ArchetypeSignature`のType[]シーケンス一致による、既存`CollectViews<T>`と同じ挙動)。
- Intended behavior:
  - Docs/Specifications/Generated-Entity-Storage.md「SoAの正本」節と同一。見本
    (`AosSoaGeneratedExpansion.SingleFile.cs`)の`ChunkCapacity`ベースの物理的な複数Chunk
    生成は、ADR-0024で単一Chunk方式へ意図的に置き換えている。
- Result: MatchesWithDocumentedExtension(単一Chunk方式は意図した設計上の差異であり、
  ADR-0024・本Implementation-Statusの両方に明記済み)

### RE-L7-006 — `CSharpGameEngine.Generation.Abstractions`

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Generation.Abstractions/*.cs
  - src/CSharpGameEngine.Generation.Abstractions/CSharpGameEngine.Generation.Abstractions.csproj
- Observed behavior:
  - net10.0、`PackageReference`なし、`Microsoft.CodeAnalysis.*`にも`CSharpGameEngine.Ecs`にも
    依存しない。`EntityStorageKind`(enum)と`GenerateEntityAttribute`のみを公開する。
- Intended behavior:
  - ADR-0024「`CSharpGameEngine.Generation.Abstractions`を新設し、`EntityGenerationSettings`は
    同梱しない」節と同一。
- Result: Matches

### RE-L7-007 — `GenerateEntityGenerator`: コンパイル時Backend選択とFacade統一

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityGenerator.cs、GenerateEntityAnalyzer.cs、
    GenerateEntityCommonEmitter.cs、GenerateEntityAosEmitter.cs、GenerateEntitySoaEmitter.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorEmissionTests.cs、
    GenerateEntityGeneratorConstSwitchTests.cs、GenerateEntityAosRuntimeTests.cs、
    GenerateEntitySoaRuntimeTests.cs、GenerateEntityConformanceTests.cs
- Observed behavior:
  - 属性引数の`EntityStorageKind`は`const`フィールド参照であってもRoslynが解決済みの値を
    渡す。選択されなかったBackendの`AddSource`は一度も呼ばれず、生成ソースに
    `EntityStorageKind`のRuntime比較・`switch`は一切現れない
    (`GenerateEntityGeneratorConstSwitchTests`でテキスト検索により確認)。
  - `{Type}`/`{Type}CreateInfo`の公開メンバー形状はAoS/SoA間でreflection比較により完全一致
    (`PublicCharacterApi_IsIdenticalBetweenAosAndSoa`)。
  - 実際にコンパイル・`Emit`・`Assembly.Load`・リフレクション実行して、Create/get・set/
    refアクセサ/ユーザー定義`Move`・`ApplyDamage`・`IsDead`/コピーハンドルの同一性/Destroy/
    stale handle拒否/swap-back/ChunkViewバッチループ/2生成型間のアーキタイプ共有が
    両Backendで機能することを確認した。
- Intended behavior:
  - ADR-0024「公開Facadeを両Backendで統一する」「物理ストレージだけをコンパイル時に
    切り替える」「本番では選択Backendだけを生成する」「Runtime分岐を残さない」節と同一。
- Result: Matches

### RE-L7-007 — `GenerateEntityGenerator`: `[Flags]`による1宣言からの両バックエンド生成

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Generation.Abstractions/EntityStorageKind.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityEmitterSupport.cs、GenerateEntityGenerator.cs、
    GenerateEntitySoaEmitter.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityCombinedKindTests.cs
- Observed behavior:
  - 両フラグ指定時、1つの宣言から`{Type}.Common.g.cs`/`{Type}.Aos.g.cs`/`{Type}.Soa.g.cs`の
    3ファイルが出力される。Commonは1回だけで、Facadeは`{Type}AosStore`に束縛される。
  - Store/ChunkViewは`{Type}Aos*`/`{Type}Soa*`へ改名され衝突しない。SoA側の`Create`は
    Facadeを持たないため`Entity`を返す。
  - 単一指定時の生成物は`[Flags]`化前と完全に同一(既存79テストが無改変で通過)。
  - 生成コードに`EntityStorageKind`のRuntime比較も`switch`も現れない。
- Intended behavior:
  - ADR-0024「`EntityStorageKind`を`[Flags]`とし、両指定は比較・計測モードとする」節、
    およびDocs/Specifications/Generated-Entity-Storage.md「両指定(比較・計測モード)」節と同一。
- Result: MatchesWithDocumentedExtension(両指定時にSoA側へFacadeが生成されないのは
  C#の言語制約(partialプロパティの実装宣言は1つ、`_store`は具体クラス型)に起因する意図的な
  非対称であり、ADR-0024・仕様書の双方に理由とともに明記済み)

### RE-L9-001 — Particle Entityベンチマークの実測挙動

- Confidence: High
- Evidence:
  - UserGameProject/Entities/*.cs
  - tests/UserGameProject.Tests/ParticleStorageTests.cs、BenchmarkHarnessTests.cs
  - Docs/Milestones/M0006-Generated-Entity-Benchmark.md(実測値)
- Observed behavior:
  - 1宣言から生成された両バックエンドが、10フレーム分のバッチ更新後も合計値まで完全に一致する。
  - 実測(500,000件・51試行・中央値、Release): 部分コンポーネント走査でSoAが1.59〜1.76倍速く、
    全コンポーネント走査では差が消え、CreateはAoSが1.69倍速く、ランダム単体アクセスは同等。
- Intended behavior:
  - ADR-0024「Hot loopにReflection/Dictionary検索/Location lookup/boxing/interface dispatchなし」
    「SoAはSpanまたは配列列を直接処理」「AoSはData row配列を直接処理」というコード構造上の保証。
    観測された性能特性はこの構造から予期されるとおりのもの。
- Result: Matches(ただし数値は自前Stopwatchハーネスによる相対比較であり、統計的厳密さは
  BenchmarkDotNetに劣ることをM0006に明記済み)

### RE-L7-008 — `GenerateEntityGenerator`診断(ENTGEN001〜015)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityDiagnostics.cs、
    GenerateEntityAnalyzer.cs、AnalyzerReleases.Unshipped.md
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorDiagnosticsTests.cs
- Observed behavior:
  - `ENTGEN001`〜`015`(15件)がそれぞれ対象コードで報告され、`ValidCharacterSource_
    ProducesZeroDiagnostics`(正しいCharacter宣言)ではいずれも報告されないことを確認した。
  - 診断が1件でもある型は、その型のソース生成が完全にスキップされる。
- Intended behavior:
  - Docs/Specifications/Generated-Entity-Storage.md「Diagnostic一覧」節と同一。
- Result: Matches
