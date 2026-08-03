# ADR-0015: アーキタイプ型ECS静的ランタイムと`EcsArchetypeGenerator`

## ステータス
承認済み

## 背景

`[EcsArchetype]`属性を付けたpartial structから、コンポーネント正規化・SoAストレージ・
`World.Create<T>()`・`ref`アクセサを自動生成するRoslyn Source Generatorの実装依頼があった。
このリポジトリにはECS関連コード(`World`/`Entity`/`Archetype`概念)が一切存在せず、完全新規実装である。

直近のコミットで追加された`Docs/EditorSpec.md`は、現行マイルストーンとしてGodot式ノード木
(`CSharpGameEngine.Scene`、retained scene graph)を採用し、107行目で「将来アーキタイプECSへの
移行が必要になった場合は、その時点で改めてADRとして決定する」と明記していた。本ADRはその
「将来の決定」に相当するが、`EditorSpec.md`のシーン編集ロードマップ(Hierarchy/配置/C#アタッチ/Play)
を置き換えるものではない。`CSharpGameEngine.Ecs`はエディタのシーングラフとは独立した、L3
(ECS / Job Runtime)相当の並行するランタイム層として追加する。

ユーザーに確認済みの決定事項:
1. 名前空間は`MyEngine.Ecs`ではなく`CSharpGameEngine.Ecs`(既存の`CSharpGameEngine.*`命名規則に統一)。
2. スコープは静的アーキタイプのみ。`AllowAddComponent`/`AllowRemoveComponent`による動的構造変更
   (エンティティのアーキタイプ間移動)は今回実装しない。

## 決定事項

### ランタイム: `src/CSharpGameEngine.Ecs`(新規、net10.0)
`Entity`(Id+Generation)、`World`(エンティティアロケータ+`Dictionary<ArchetypeSignature,
ArchetypeStorage>`)、`ArchetypeStorage`/`ComponentColumn<T>`(SoA列、コンポーネント型ごとに
ただ1種類の汎用実装)、`IEcsArchetype<TSelf>`(`static abstract`メンバーによるCRTP契約)を追加した。
`CSharpGameEngine.CodeAnalysis`への参照は追加していない(ADR-0001: ランタイムはRoslyn/VS SDKに
依存してはならない)。

### コンポーネント正規化: 完全修飾名(FQN)によるOrdinal文字列ソート
グローバルなComponentTypeIdレジストリは作らず、各アーキタイプが自分の宣言したコンポーネント型の
完全修飾名を`string.CompareOrdinal`でソートするだけで正規化順を決定する
(`EcsArchetypeAnalyzer.Analyze`)。コンパイル境界を跨いでも各アーキタイプが自己完結して同じ順序を
導出できるため、中央集権的なID割り当て機構が不要になる。

### ストレージ共有: 実行時の`Type[]`シーケンス一致
`ArchetypeSignature`(internal)は`Type[]`の要素シーケンス一致で`Equals`/`GetHashCode`を実装する。
`World.GetOrCreateStorage<T>()`はこのシグネチャで`Dictionary`検索するため、正規化後の構成が
一致する複数の名前付きアーキタイプ(宣言順だけが異なる等)は、Generator側に一切比較ロジックを
持たせることなく、実行時に自然に同一の`ArchetypeStorage`を共有する。コンパイルエラーにもしない
(ユーザー確定要件)。

### `world.Create<T>()`実現方式: 拡張メソッド + CRTP + マーカーインターフェース制約
`World`は`Create`という名前のインスタンスメンバーを一切持たない。1つでも持つと、その名前に対する
拡張メソッド解決がすべて隠蔽されるC#の規則(拡張メソッドは通常のメンバー検索で候補が見つかった
時点で一切考慮されなくなる)を回避するため。
- 引数なし版: `WorldEcsExtensions.Create<T>(this World world) where T : struct, IEcsArchetype<T>`
  という全アーキタイプ共通の唯一の実装。
- 名前付き引数版(例: `world.Create<Bullet>(position: ..., velocity: ..., damage: ...)`):
  アーキタイプごとに`{StructName}EcsFactory.Create<T>(this World world, ...)
  where T : struct, IEcsArchetype<T>, {StructName}.IMarker`という拡張メソッドを生成する。
  `{StructName}.IMarker`はGeneratorが各アーキタイプに生成する空のネストされたマーカー
  インターフェースで、そのアーキタイプ以外は実装しない。正規化後の構成が完全に一致する2つの
  アーキタイプ(`MovingObjectA`/`MovingObjectB`、パラメータ名まで同一)であっても、明示的型引数
  `<T>`に対する制約充足性チェックにより、対応する1つの拡張メソッドだけが適用可能候補として残り、
  曖昧にならない。

この設計はC#の言語仕様上の挙動に強く依存するため、実装前に独立したスパイクプロジェクトで
実際にコンパイル・実行して検証した(検証結果参照)。

### 診断: `ECSGEN001`〜`ECSGEN009`(カテゴリ`CSharpGameEngine.Ecs.CodeGen`、すべて`Error`)
このリポジトリで初めてDiagnosticDescriptorを持つGeneratorとなるため、`ECSGEN###`という診断ID
規則を新規に確立した。

| ID | 内容 |
|----|------|
| ECSGEN001 | `[EcsArchetype]`を付けた型は`partial struct`でなければならない |
| ECSGEN002 | アーキタイプ構造体はジェネリックであってはならない |
| ECSGEN003 | アーキタイプ構造体はネストされた型であってはならない |
| ECSGEN004 | アーキタイプは1個以上のコンポーネントプロパティを宣言しなければならない |
| ECSGEN005 | プロパティは`ref`または`ref readonly`でコンポーネント型を返さなければならない |
| ECSGEN006 | コンポーネント型は非nullableな値型(struct)でなければならない |
| ECSGEN007 | 同一アーキタイプ内で同じコンポーネント型を複数のプロパティに使うことはできない |
| ECSGEN008 | `[EcsReadOnly]`を付与したプロパティは`ref readonly`を返さなければならない |
| ECSGEN009 | `ref readonly`を返すプロパティには`[EcsReadOnly]`を付与しなければならない |

当初の設計では「プロパティはget-onlyでなければならない」という診断(ECSGEN002相当)も予定して
いたが、実装前にRoslyn 4.14.0の実際の挙動を検証したところ、`ref`/`ref readonly`を返すプロパティに
`set`アクセサを付けること自体がC#自身のコンパイルエラー(CS8147)であり、`ref`/`ref readonly`を
返さないプロパティは別途ECSGEN005が検出するため、専用の診断は実際には到達不能(既存の言語制約と
ECSGEN005で完全にカバーされる)と判明した。投機的な到達不能診断を持ち込まないため、この項目は
削除し、残りを001から009まで連番で振り直した。

### `[EcsReadOnly]`のスコープ
Query/System基盤が存在しない現時点では、(1)`ref readonly`宣言との整合性をコンパイル時に強制
(ECSGEN008/009)、(2)将来のQuery機能のためのメタデータとして温存(ユーザー記述側の宣言に
そのまま残るため、通常のリフレクションで再取得可能)の2点のみに限定する。

### `CSharpGameEngine.CodeAnalysis.Abstractions`を作らない判断(継続)
ADR-0014は「2つ目のGeneratorが追加された時点で再検討する」というトリガーを記録していたが、
実際には`CSharpGameEngineBootstrapGenerator`(固定文字列出力のみ)と`EcsArchetypeGenerator`
(シンボル解析・正規化・診断・コード生成)の間に共有できるロジックが存在しない。作成しても
呼び出し元が`EcsArchetypeGenerator`1つだけの投機的コードになるため、今回も見送る。
再検討トリガーを更新する: 3つ目のGenerator、または最初の非Generator `DiagnosticAnalyzer`が
実際に必要とする時点。

## 代替案として検討したもの

- **グローバルComponentTypeIdレジストリ**: コンパイル全体を横断してコンポーネント型に初出順で
  整数IDを割り当てる方式。複数プロジェクトに分散した宣言を横断できず、決定性の証明も複雑になる
  ため不採用。
- **`Bullet.Create(world, ...)`という具象型静的ファクトリ**: 確実に動作するが、依頼された
  `world.Create<Bullet>(...)`という呼び出し構文そのものを変えてしまうため不採用。
- **同一構成のアーキタイプをコンパイルエラーにする案(`ECS0004`相当)**: ユーザーが明示的に
  「同一構成は別名ビューとして扱う方が柔軟」と判断していたため不採用。

## 帰結

- `CSharpGameEngine.Ecs`(net10.0)と`CSharpGameEngine.CodeAnalysis`への`EcsArchetypeGenerator`
  追加により、`CSharpGameEngine.CodeAnalysis`は初めて実際にコンシューマー
  (`UserGameProject`、`OutputItemType="Analyzer"`)を持つ状態になった。
- `EcsArchetypeAnalysisResult`/`EcsComponentPropertyModel`は`record`ではなく通常の`struct`として
  実装した。`CSharpGameEngine.CodeAnalysis`は`netstandard2.0`をターゲットとしており、
  `record`/`record struct`が要求する`System.Runtime.CompilerServices.IsExternalInit`をBCLが
  提供しないため。
- これらのモデルは`ImmutableArray<T>`フィールドを持つため、既定の等価性は要素比較ではなく
  参照比較になる。`IncrementalValuesProvider`のキャッシュとして使う場合、内容が同じでも
  「変化した」と判定され再生成が余分に走ることがあるが、生成結果の正しさには影響しない
  (v1で許容する既知の制約)。
- 診断が1件でもあるアーキタイプは、`EcsArchetypeEmitter`がソース生成そのものをスキップする
  (壊れた部分実装による二次的なコンパイルエラーを避けるため)。

## 検証結果(実際に実行して確認)

- 実装前に、独立したスパイクコンソールプロジェクト(`net10.0`)で、partial refプロパティ・
  `static abstract`インターフェースメンバー・マーカーインターフェース制約による拡張メソッド
  曖昧性解消・実行時ストレージ共有のすべてを手書きコードで再現し、実際に`dotnet run`して
  全項目が想定通り動作することを確認した。
- `Microsoft.CodeAnalysis.CSharp 4.14.0`の実際のシンボルAPIを別スパイクで検証し、
  `IPropertySymbol.IsPartialDefinition`/`PartialImplementationPart`が実装前(ユーザー側の
  定義宣言のみで実装宣言が無い状態、CS9248が出ている状態)でも正しく解析できること、
  `ref readonly`を返すプロパティの`RefKind`が`RefKind.RefReadOnly`ではなく`RefKind.In`である
  ことを確認した上で`EcsArchetypeAnalyzer`を実装した。
- `dotnet build CSharpGameEngine.slnx -c Debug`: 成功、警告0件・エラー0件。
- `dotnet build CSharpGameEngine.slnx -c Release`: 成功、警告0件・エラー0件。
- `dotnet test CSharpGameEngine.slnx`: 全125件成功(内訳: 既存91件 + 新規`CSharpGameEngine.Ecs.Tests`
  17件 + `CSharpGameEngine.CodeAnalysis.Tests`への新規追加17件、同プロジェクトの既存3件と合わせて
  20件)。既存テストに回帰は無かった。
- `dotnet pack src/CSharpGameEngine.CodeAnalysis -c Release`: 成功。`unzip -l`で`.nupkg`内容を
  確認し、`analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll`のみが含まれることを確認済み
  (ADR-0014のNU5128抑制方針を継続)。
- `dotnet build UserGameProject/UserGameProject.csproj`: 成功。`EmitCompilerGeneratedFiles=true`で
  実際に生成された`MyGame.Archetypes.Player.g.cs`等4ファイルを目視確認し、`Player`の
  `ComponentTypes`配列が宣言順(`PlayerTag,Velocity,Health,Rotation,Position`)ではなく
  正規化順(`Health,PlayerTag,Position,Rotation,Velocity`)で出力されていることを確認した。
- `EcsArchetypeGeneratorEndToEndTests`: ユーザー提示の`Player`サンプル一式と、名前付き引数版
  `Bullet`+正規化後に同一形状となる`MovingObjectA`/`MovingObjectB`を、実際に
  `EcsArchetypeGenerator`に通してコンパイル・`Emit`・`Assembly.Load`・リフレクション実行し、
  実行時の値が期待通りであることを確認した(拡張メソッド曖昧性解消が実際のGenerator出力で
  機能することの最終証明)。
- `RS2008`(Analyzer release tracking)が実際にビルドエラーとして発生したため、
  `AnalyzerReleases.Shipped.md`/`AnalyzerReleases.Unshipped.md`を新規に追加し
  `AdditionalFiles`として登録した。このリポジトリで初めてAnalyzer release trackingファイルを
  導入した。

## 未解決事項

- `AllowAddComponent`/`AllowRemoveComponent`による動的構造変更(エンティティのアーキタイプ間
  移動)は未実装。実装する場合、任意の(静的に宣言されていない)構成のアーキタイプストレージを
  実行時に動的生成する汎用機構が別途必要になる。ビュー構造体は行番号をキャッシュせず常に
  `World`に問い合わせる設計にしてあるため、将来この機能を追加する余地は残している。
- Query/System基盤は未実装(`[EcsReadOnly]`はコンパイル時整合性検証とメタデータ温存のみ)。
- `EcsArchetypeAnalysisResult`等のキャッシュモデルが`ImmutableArray<T>`の参照等価性に起因して
  過剰に再生成される可能性がある(正しさには影響しないパフォーマンス上の既知の制約)。
- プロパティ名がC#予約語と衝突する場合(例: プロパティ名`Object`→パラメーター名`object`)の
  識別子エスケープ(`@`プレフィックス)は未対応。ユーザー提示のサンプルには該当ケースが無いため
  今回はスコープ外とした。
- `CSharpGameEngine.CodeAnalysis.Abstractions`/`CSharpGameEngine.CodeAnalysis.Testing`の
  再検討トリガー条件は上記「決定事項」に記載の通り。

## 関連機能
- F-L3-001 — Entity / World基盤
- F-L3-002 — Archetypeストレージ共有
- F-L3-003 — `IEcsArchetype<TSelf>`ランタイム契約
- F-L7-003 — `EcsArchetypeGenerator`
- F-L7-004 — `EcsArchetypeGenerator`診断(ECSGEN001〜009)
