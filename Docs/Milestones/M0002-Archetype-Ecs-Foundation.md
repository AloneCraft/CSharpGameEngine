# M0002: アーキタイプ型ECS静的ランタイムと`EcsArchetypeGenerator`の追加

## 目的
`[EcsArchetype]`属性を付けたpartial structから、コンポーネント正規化・SoAストレージ・
`World.Create<T>()`・`ref`アクセサを自動生成するRoslyn Source Generator(`EcsArchetypeGenerator`)と、
それが対象とする静的アーキタイプ用ランタイム(`CSharpGameEngine.Ecs`)を新規に実装し、
`UserGameProject`にそれを使うサンプルコードを配置する。詳細な設計判断は
[[ADR-0015-Archetype-Ecs-Static-Runtime]]に記録した。

## 実装したこと
- `src/CSharpGameEngine.Ecs/`(新規、net10.0): `Entity`(Id+Generation)、`World`
  (エンティティアロケータ+アーキタイプストレージ辞書、`CreateEntityCore<T>`/`GetComponentRef<T>`/
  `IsAlive`/`Destroy`)、`ArchetypeStorage`/`ComponentColumn<T>`(SoA列)、`ArchetypeSignature`
  (`Type[]`シーケンス一致によるストレージ共有キー)、`IEcsArchetype<TSelf>`(`static abstract`
  メンバーによるCRTP契約)、`EcsArchetypeAttribute`/`EcsReadOnlyAttribute`、
  `WorldEcsExtensions.Create<T>()`(引数なし版の唯一の汎用実装)。
- `src/CSharpGameEngine.CodeAnalysis/`に`EcsArchetypeGenerator`(`IIncrementalGenerator`、
  `ForAttributeWithMetadataName`ベース)を追加。`EcsArchetypeAnalyzer`(シンボル解析・
  完全修飾名Ordinalソートによる正規化・診断)、`EcsArchetypeEmitter`(partialプロパティ実装・
  `IEcsArchetype<T>`静的メンバー・マーカーインターフェース・名前付き引数版ファクトリの生成)、
  `EcsArchetypeDiagnostics`(`ECSGEN001`〜`ECSGEN009`)を新規ファイルとして追加。
  `AnalyzerReleases.Shipped.md`/`AnalyzerReleases.Unshipped.md`を新規追加(RS2008対応、
  このリポジトリで初のAnalyzer release tracking導入)。
- `tests/CSharpGameEngine.Ecs.Tests/`(新規): Entity等価性、`World`のエンティティ生成/破棄/
  ID再利用/容量拡張、`ComponentColumn<T>`、そしてユーザー要件の核心である
  「正規化後に同一構成となる2つのアーキタイプが同一`ArchetypeStorage`を共有する」ことを検証する
  `ArchetypeStorageSharingSpec`を含む17件。
- `tests/CSharpGameEngine.CodeAnalysis.Tests/`に`EcsArchetypeGenerator`用テスト17件を追加:
  正規化順の一致(emission)、`ECSGEN001`〜`ECSGEN009`各診断の発生確認(diagnostics)、そして
  実際にコンパイル・`Emit`・`Assembly.Load`・リフレクション実行して実行時の値を検証する
  end-to-endテスト2件(`Player`サンプル、名前付き引数版`Bullet`+正規化後に同一形状となる
  `MovingObjectA`/`MovingObjectB`)。
- `UserGameProject/Components.cs`/`Archetypes.cs`(新規): ユーザー提示のサンプル
  (`MovingObject`/`DamageableObject`/`Player`、`MyGame.Components`/`MyGame.Archetypes`名前空間)
  をそのまま配置し、名前付き引数コンストラクタの実演として`Bullet`も追加した。
  `UserGameProject.csproj`に`CSharpGameEngine.Ecs`への通常`ProjectReference`と
  `CSharpGameEngine.CodeAnalysis`への`OutputItemType="Analyzer"`参照を追加した。
- `CSharpGameEngine.slnx`・`UserGameProject.slnx`双方に`src/CSharpGameEngine.Ecs`・
  `src/CSharpGameEngine.CodeAnalysis`(および本ADR/Milestoneファイル)を登録した。

## 意図的に除外したこと
- `AllowAddComponent`/`AllowRemoveComponent`による動的構造変更(エンティティのアーキタイプ間移動)。
  ユーザーとの事前確認により、まず静的アーキタイプのみを今回のスコープとした。
- Query/System一括イテレーション基盤。`[EcsReadOnly]`はコンパイル時整合性検証と将来のための
  メタデータ温存のみ。
- `CSharpGameEngine.CodeAnalysis.Abstractions`の新規作成(ADR-0015参照、再検討トリガーを継続)。
- `UserGameProject`への`Program.cs`/実行可能エントリポイントの追加(README記載の
  「エンジンとの統合は将来のマイルストーン」というスコープを超えるため)。正しさの検証は
  `CSharpGameEngine.CodeAnalysis.Tests`のend-to-endテストで担保し、`UserGameProject`側は
  `dotnet build`が通ることの実証に限定した。
- プロパティ名がC#予約語と衝突する場合の識別子エスケープ(未対応、ユーザー提示のサンプルに
  該当ケースが無いため)。

## 変更したFeature ID
- `F-L3-001` — Entity / World基盤(新規、Implemented/TestVerified)
- `F-L3-002` — Archetypeストレージ共有(新規、Implemented/TestVerified)
- `F-L3-003` — `IEcsArchetype<TSelf>`ランタイム契約(新規、Implemented/TestVerified)
- `F-L7-003` — `EcsArchetypeGenerator`(新規、Implemented/TestVerified)
- `F-L7-004` — `EcsArchetypeGenerator`診断ECSGEN001〜009(新規、Implemented/TestVerified)

## 実行したコマンドと結果
```
dotnet build src/CSharpGameEngine.Ecs/CSharpGameEngine.Ecs.csproj -c Debug        → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.Ecs.Tests/...csproj                            → 17件成功
dotnet build src/CSharpGameEngine.CodeAnalysis/...csproj -c Debug                 → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...csproj                   → 20件成功(既存3+新規17)
dotnet build UserGameProject/UserGameProject.csproj -c Debug                      → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Debug                                       → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release                                     → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx                                                 → 125件成功(既存91+新規34)、失敗0
dotnet pack src/CSharpGameEngine.CodeAnalysis/...csproj -c Release                → 成功
```
`dotnet build UserGameProject/UserGameProject.csproj -c Debug -t:Rebuild
-p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=GeneratedFiles`を実行し、
実際に生成された`MyGame.Archetypes.Player.g.cs`等4ファイルを目視確認した(確認後、この一時出力は
削除した)。`.nupkg`の内容は`unzip -l`で`analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll`
のみが含まれることを確認した。

## リバースエンジニアリングした挙動
- `EcsArchetypeGenerator`は`ForAttributeWithMetadataName("CSharpGameEngine.Ecs.EcsArchetypeAttribute",
  ...)`でアーキタイプ構造体を検出し、`EcsArchetypeAnalyzer.Analyze`がコンポーネントプロパティを
  完全修飾名のOrdinal文字列でソートして正規化する。実際に`UserGameProject`をビルドして確認したところ、
  `Player`(宣言順`PlayerTag,Velocity,Health,Rotation,Position`)は正規化後
  `Health,PlayerTag,Position,Rotation,Velocity`の順で`ComponentTypes`配列・`CreateColumns()`・
  partialプロパティの列インデックスすべてに反映されていた。
- 正規化後に同一構成となる`MovingObjectA`/`MovingObjectB`は、`World`が`Type[]`のシーケンス一致で
  `ArchetypeStorage`を検索するため、実行時に同一のストレージインスタンスを共有する
  (`ArchetypeStorageSharingSpec`で確認)。コンパイルエラーにはならない。
- 名前付き引数版`world.Create<Bullet>(position:...,velocity:...,damage:...)`は、
  `World`自身に`Create`インスタンスメンバーが無いことと、アーキタイプごとに生成される
  ネストされた空のマーカーインターフェース(`{StructName}.IMarker`)への型制約により、
  正規化後の引数形状が完全に一致する複数アーキタイプが存在しても曖昧にならずに解決されることを、
  実際にコンパイル・実行するend-to-endテストで確認した。
- 診断が1件でもあるアーキタイプは、`EcsArchetypeEmitter`がそのアーキタイプのソース生成を
  完全にスキップする(他の正しいアーキタイプの生成には影響しない)。
- 実装時に、当初予定していた「プロパティはget-onlyでなければならない」という診断は、
  `ref`/`ref readonly`を返すプロパティへの`set`アクセサ付与がC#自身のコンパイルエラー(CS8147)
  であるため実際には到達不能と判明し、削除した(診断は最終的にECSGEN001〜009の9件)。

## 意図と観測の一致
Conformance: Matches。ADR-0015に記載した意図した設計(正規化方式・ストレージ共有方式・
`Create<T>`実現方式・診断設計・Abstractions非作成の判断)と、実際にビルド・テスト・
生成コード目視確認・end-to-end実行で確認した挙動の間に既知の乖離は無い。

## 残課題
- 動的Add/Remove(`AllowAddComponent`/`AllowRemoveComponent`)は未実装。実装するには、
  静的に宣言されていない任意の構成のアーキタイプストレージを実行時に動的生成する汎用機構が
  別途必要になる(ADR-0015参照)。
- Query/System基盤は未実装。
- `EcsArchetypeAnalysisResult`等のキャッシュモデルが`ImmutableArray<T>`の参照等価性に起因して
  過剰に再生成される可能性がある(正しさには影響しないパフォーマンス上の既知の制約)。
- `UserGameProject`への`Program.cs`追加、および実際のVisual Studio上でのAnalyzer動作の実機確認
  (スクリーンショット等)は本マイルストーンでは行っていない。
- 次のマイルストーンに進む前提条件: 動的構造変更またはQuery/System機能の具体的な要件が
  確定すること。
