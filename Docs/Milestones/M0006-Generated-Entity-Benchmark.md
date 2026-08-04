# M0006: 1宣言からのAoS/SoA同時生成とUserGameProjectでの実測ベンチマーク

## 目的
`[GenerateEntity]`を付けた**1つのユーザー宣言**から、AoS(1エンティティの全フィールドが
隣接する行指向レイアウト)とECS SoA(コンポーネント型ごとに列として並べるレイアウト)の
両方の物理ストレージを生成できるようにし、`UserGameProject`にエンドユーザー相当の模擬コードを
置いて、両者を同一プロセスで実測比較する。AoS/SoAはいずれも継承やヒープ上のオブジェクトグラフ
とは無関係な、純粋にメモリレイアウトの選択であることに注意(「オブジェクト指向 vs データ指向」
という対比は不正確)。
設計判断は[[ADR-0024-Generated-Entity-AoS-SoA]]に追記、仕様は[[Generated-Entity-Storage]]を更新した。

## 実装したこと

### `EntityStorageKind`の`[Flags]`化(1宣言→両バックエンド)
- `src/CSharpGameEngine.Generation.Abstractions/EntityStorageKind.cs`: `[Flags]`、
  `Aos = 1` / `EcsSoa = 2`。
- `GenerateEntityAnalyzer`: 属性引数の定数解決を`1`/`2`/`3`受理へ。`0`や範囲外は従来どおりENTGEN014。
- `GenerateEntityModel`: `GenerateEntityStorageKind`を`[Flags]`化し、`HasAos`/`HasSoa`/`IsCombined`を追加。
- `GenerateEntityEmitterSupport`: `StoreTypeName`/`ChunkViewTypeName`/`FacadeStoreTypeName`を追加。
  両指定時のみ`{Type}Aos*`/`{Type}Soa*`へ切り替え、単一指定時は従来の`{Type}Store`/`{Type}ChunkView`を維持。
- `GenerateEntityGenerator`: Common(Facade/CreateInfo/GeneratedMetadata)は両指定時も**1回だけ**出力し、
  `HasAos`/`HasSoa`それぞれでAos/Soa Emitterを呼ぶ。
- `GenerateEntitySoaEmitter`: 両指定時はFacadeを持たないため`Create`が`Entity`を返すモードを追加。
- 両Emitter: `Get{Name}Ref`を`internal`→`public`へ(SoA側の単体アクセス経路を他アセンブリへ開くため)。
- `GenerateEntityAnalyzer`: ENTGEN013の予約型名に`{Type}AosStore`/`SoaStore`/`AosChunkView`/`SoaChunkView`を追加。

### UserGameProjectのユーザー模擬コード(`UserGameProject/Entities/`、新規)
- `ParticleComponents.cs`: `LocalTransform`/`LinearVelocity`/`Durability`。
  親名前空間`UserGameProject`の`Position`/`Velocity`/`Health`とは意図的に別名にした。
- `Particle.cs`: **ユーザーが書く唯一の宣言**。`EntityGenerationSettings.Kind`は
  `Aos | EcsSoa`。partialプロパティ3つと、手書きの`Move`/`ApplyWear`/`IsWornOut`。
- `ParticleMotion.cs`: ドメインロジック本体を純粋staticとして**1回だけ**記述
  (既存の`CharacterMotion`と同じ設計方針)。単体経路とバッチ経路が同じ計算を実行することを保証する。
- `ParticleSystems.cs`: 生成される`Particle*ChunkView`は`internal`なので、バッチ経路を
  他アセンブリ(テスト・計測)から駆動するための`public`ラッパー。
  `MoveAll`/`ApplyWearAll`/`SumPositions`をAoS/SoA両方に用意。
- `BenchmarkHarness.cs` / `ParticleStorageBenchmark.cs`: ウォームアップ+複数試行+
  中央値/最小値レポートの最小ハーネスと、5シナリオの比較。
- `Program.cs`: 先頭に`benchmark`サブコマンド分岐を追加(既存`TestHost`の
  `self-test`/`triangle`と同じ動詞先頭規約。ウィンドウ生成より手前で`return`する)。
- `.vscode/launch.json`: `UserGameProject: benchmark`構成を追加。

### テスト
- `tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityCombinedKindTests.cs`(新規9件):
  1宣言→3ファイル生成、0エラー、両Store共存、Facadeが1回だけAoS束縛で出力、
  SoAの`Create`が`Entity`を返す、両Storeの`Get*Ref`がpublicかつref返し、
  生成コードにRuntime分岐なし、**単一指定時は従来の型名のまま**。
- `tests/UserGameProject.Tests/ParticleStorageTests.cs`(新規14件)・
  `BenchmarkHarnessTests.cs`(新規6件): AoS/SoAの往復・ユーザー定義メソッド・
  コピーハンドル・Destroy/stale handle拒否・Generation増分・swap-back・
  バッチと単体の一致・**両バックエンドの結果完全一致**・アーキタイプ共有・ハーネスの不変条件。

## 意図的に除外したこと
- BenchmarkDotNetの導入。ユーザー確認のうえ自前のStopwatchハーネスとした
  (リポジトリ初のNuGet追加を避け、`TreatWarningsAsErrors=true`・windows TFM・
  `RuntimeIdentifier`との相性リスクを回避)。統計的厳密さはBDNに劣る。
- 両指定モードでのSoA側Facade生成(言語制約により不可能。ADR-0024参照)。
- AoSバックエンドの生成型間ストレージ共有(M0005から継続の非目標)。

## 変更したFeature ID
- `F-L7-006` — `CSharpGameEngine.Generation.Abstractions`(更新: `[Flags]`化)
- `F-L7-007` — `GenerateEntityGenerator`(更新: 両指定モード追加)
- `F-L9-001` — Particle Entityベンチマーク(新規、Implemented/TestVerified)

## 実行したコマンドと結果
```
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...csproj    → 88件成功(既存79+新規9)
dotnet build UserGameProject/UserGameProject.csproj -c Debug       → 成功、警告0/エラー0
dotnet build UserGameProject/UserGameProject.csproj -c Release      → 成功、警告0/エラー0
dotnet test tests/UserGameProject.Tests/...csproj                   → 41件成功(既存21+新規20)
dotnet build CSharpGameEngine.slnx -c Debug                         → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release                       → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx                                   → 339件成功、失敗0
UserGameProject.exe benchmark --entities 500000 --iterations 51      → 下記の実測値
```

## 実測ベンチマーク結果

以下は実際に実行して得た値であり、推定値・捏造値は含まない。

- 環境: .NET 10.0.10 / Release ビルド / Windows 11 / エンティティ 500,000件 /
  ウォームアップ3回 + 計測51回、中央値で比較。
- 各行に適用する計算はAoS/SoAとも同じ`ParticleMotion`を呼ぶため、測定しているのは
  **メモリレイアウトの差のみ**。
- `Particle`のコンポーネント構成: `LocalTransform`(Vector3=12B) +
  `LinearVelocity`(Vector3=12B) + `Durability`(int=4B) = 1行あたり28B。

| シナリオ | AoS 中央値 | SoA 中央値 | 速い方 |
|---|---|---|---|
| Create(全件スポーン) | 12.695 ms | 21.488 ms | **AoS 1.69x** |
| バッチ更新: Transform += Velocity*dt(3個中2個に触る) | 0.435 ms | 0.459 ms | AoS 1.06x(ほぼ同等) |
| バッチ更新: Durability のみ(3個中1個) | 0.416 ms | 0.262 ms | **SoA 1.59x** |
| バッチ読み取り: Transform.Position 合計(3個中1個) | 0.485 ms | 0.276 ms | **SoA 1.76x** |
| ランダム単体アクセス(Location Table経由) | 0.558 ms | 0.576 ms | AoS 1.03x(ほぼ同等) |

観測された傾向はECSの一般論と整合している。

- **一部のコンポーネントだけを走査するバッチではSoAが有利**(1.59〜1.76x)。AoSは1行28Bを
  丸ごとキャッシュに載せてから4Bまたは12Bしか使わないのに対し、SoAは必要な列だけを連続に読む。
- **ほぼ全コンポーネントに触るバッチでは差が消える**(1.06x)。3個中2個=28B中24Bを使うため、
  AoSが運ぶ無駄が小さくなる。
- **CreateはAoSが有利**(1.69x)。SoAは`World`のアーキタイプ解決+列ごとの書き込みという
  間接を経由するのに対し、AoSは1行を書き込むだけで済む。
- **ランダム単体アクセスは実質同等**(1.03x)。どちらも同じ`Entity.Id`→Location Table→行/列の
  解決を通るため、レイアウトの差が支配的にならない。

注意: 自前ハーネスのため外れ値検出・統計処理は行っていない。試行回数を15回に減らすと
`Durability`シナリオの中央値が逆転する程度のばらつきがあった(min値ではSoAが一貫して速い)。
より厳密な数値が必要になった時点でBenchmarkDotNetの導入を検討する。

**本ベンチマークが測定していない要因**: Unity DOTSのような大幅な高速化は、SoAレイアウト単体
ではなく主に(1) Job Systemによるマルチコア並列化、(2) Burst CompilerによるSIMDベクトル化・
ネイティブコード生成に由来する。本ベンチマークは単一スレッド・通常のJITコンパイルであり、
上記2要因を含まない。実測した1.06〜1.76倍という控えめな差は、**キャッシュ局所性のみ**を
測定した結果として一貫している(この条件で数十倍の差が出た場合はむしろ測定ミスを疑うべき状況)。
「SoAが速い」という一般論は、SoAレイアウトが並列化・SIMD化を可能にすることによる相乗効果を
指しており、レイアウト単体の効果はここで実測した程度にとどまる。

## リバースエンジニアリングした挙動
- `[Flags]`の両指定に対して、Generatorは`{Type}.Common.g.cs`/`{Type}.Aos.g.cs`/`{Type}.Soa.g.cs`の
  3ファイルを出力する。Commonは1回だけで、`private readonly {Type}AosStore? _store;`として
  AoS側に束縛される。生成コードに`EntityStorageKind`のRuntime比較も`switch`も現れない。
- 単一指定時の生成物は`[Flags]`化前と完全に同一(型名・アクセシビリティ・本文)。既存79テストが
  無改変で通過することで確認した。
- `UserGameProject.Entities`は`UserGameProject`の子名前空間であり、親には`Scene1.cs`が宣言する
  別物の`Entity`/`World`が存在する。**compilation-unit levelの`using`エイリアスより親名前空間の
  メンバーが先に解決される**ため、実装中に実際に`CS1503`
  (`UserGameProject.Entity`→`CSharpGameEngine.Ecs.Entity`変換不可)が発生した。
  ECSの型は`global::`付き完全修飾で書くことで解消した(コード内にコメントで明記済み)。
- 両バックエンドは10フレーム分のバッチ更新後も浮動小数の合計値まで完全に一致した
  (`BothBackends_ProduceIdenticalResults_AfterTheSameOperationSequence`)。ストレージ方式の
  切り替えが観測可能な結果を一切変えないことの実証。

## 意図と観測の一致
Conformance: Matches。ADR-0024(追記分を含む)に記載した意図した設計と、実際のビルド・テスト・
生成コード目視確認・実測ベンチマークで観測した挙動の間に既知の乖離は無い。

## 残課題
- BenchmarkDotNetによる統計的に厳密な計測は未実施(自前ハーネスのばらつきは上記のとおり)。
- 両指定モードではSoA側にFacadeが生成されない(言語制約。ADR-0024に記録)。
- `Particle`のコンポーネント数が3個と少ないため、コンポーネント数が増えた場合の
  AoS/SoA差の変化は未計測。
- `Scene1.cs`(死んだプロトタイプ)の整理は未着手。整理すれば`UserGameProject.Entities`で
  `global::`完全修飾が不要になる。
