# M0012: 静的(閉集合)Archetype ECS PoC

## 目的

`UserGameProject`にユーザーが手書きした3ファイルのspec-by-example(`Runtime.cs`・`ユーザー定義コード.cs`・
`ユーザー定義コード.g.cs`)が要求する、Archetypeをコンパイル時に閉じた集合として列挙する新方式の
Source Generatorを、既存の本番ECS(`CSharpGameEngine.Entities`+`GameSystemGenerator`、ADR-0029/M0009)から
完全に独立させて実装し、3ファイルの入出力どおりに動くことを検証する。設計判断は
[[ADR-0032-Static-Archetype-Closed-Set-Ecs]]に記録した。`UserGameProject`統合・旧Generator置き換えは
次パス。

## 実装したこと

### 新ランタイム `src/CSharpGameEngine.StaticArchetype/`
- `Runtime.cs`(1ファイルのみ): `ISystem`/`IArchetype`(空マーカーinterface)、`Entity`(`record struct`、
  Generation無し)、`State`/`World`(`partial struct`、`EntityArchetypeIndices`フィールド)、
  `ChunkList<T>`(チャンク方式の可変長配列、swap-back削除)。グローバル名前空間、`internal`基調。
- `CSharpGameEngine.StaticArchetype.csproj`(net10.0、`IsPackable=false`)。`CSharpGameEngine.CodeAnalysis`
  をAnalyzerとして参照し、このプロジェクト単体の`dotnet build`が「System0件でもコンパイルできる」
  ことのスモークテストになる。

### `StaticArchetypeGenerator`(`src/CSharpGameEngine.CodeAnalysis/`)
- `StaticArchetypeGenerator.cs`: `IIncrementalGenerator`本体。属性を使わないため
  `CreateSyntaxProvider`でbase listを持つ型宣言を拾い、`ISystem`実装を意味解析で確認する。
  `compilation.GetTypeByMetadataName("ISystem")`によるゲート(`Runtime.cs`を取り込んだプロジェクトの
  みに出力を限定、実装中に発見した`UserGameProject`への汚染バグの修正 - ADR-0032決定事項2)。
- `StaticArchetypeAnalyzer.cs`/`StaticArchetypeModel.cs`: struct検証、`Update`オーバーロードの
  シグネチャ解析(先頭の任意`Entity`引数、`ref`Componentパラメータ列、末尾`ref State`)。
- `StaticArchetypeIndexAssigner.cs`: 純粋関数。FQNソート+宣言順走査によるComponentインデックス
  割り当て、Archetype集合の導出、出力順序(コンポーネント数昇順→インデックスタプル辞書式昇順)の確定。
- `StaticArchetypeTypesEmitter.cs`/`StaticArchetypeWorldEmitter.cs`/`StaticArchetypeLoopEmitter.cs`:
  `Archetype` enum+`ArchetypeNN` struct(`Create`静的ファクトリ含む)、`World`拡張
  (`MoveArchetype`/`Detach`/`PlaybackMoveArchetypes`/`ApplyMoveArchetypeNN`)、
  `GeneratedGameLoop.RunFrame`/`Run`を出力。
- `StaticArchetypeDiagnostics.cs`: `SARCH001`〜`SARCH015`(`AnalyzerReleases.Unshipped.md`に登録済み)。
- `tests/CSharpGameEngine.CodeAnalysis.Tests/`: `StaticArchetypeGeneratorTestHelper.cs`(`Runtime.cs`/
  `Components.cs`/`Systems.cs`をファイルからリンクして読み込み、テストとサンプル実体のドリフトを
  防ぐ)、`StaticArchetypeGeneratorDiagnosticsTests.cs`(15件)、`StaticArchetypeGeneratorEmissionTests.cs`
  (出力順序・System1のループ包含・決定性・宣言順序非依存性を含む6件)、
  `StaticArchetypeGeneratorEndToEndTests.cs`(Archetype間移動・swap-back・複数System/複数オーバーロード
  発火・診断による部分除外を実際に実行して検証する3件)。新規24件。

### `samples/StaticArchetypeSystemsDemo/`(新規)
- `Components.cs`/`Systems.cs`: `UserGameProject`のスクラッチフォルダから昇格(内容はそのまま)。
- `Program.cs`: 薄いトップレベルステートメントのエントリポイント。`ArchetypeNN.Create`で初期
  エンティティを生成し、`GeneratedGameLoop.RunFrame`を5回呼んでArchetypeごとのエンティティ数を出力。
- `StaticArchetypeSystemsDemo.csproj`: `Runtime.cs`を`<Compile Include Link>`でソースリンク
  (`<ProjectReference>`ではない、ADR-0032決定事項1)。

### スクラッチフォルダの後片付け
`UserGameProject/ECSの設計であるこの.g.csを生成するようなSGを作って/`(3ファイルとも)を、内容を
上記へ昇格した上で削除した。`UserGameProject/UserGameProject.csproj`の、存在しない旧フォルダ名
(`これをみてコード生成するようなSGを作って`)を指す壊れた`<Compile Remove>`/`<EmbeddedResource
Remove>`/`<None Remove>`エントリ(3行)も削除した。

### `CSharpGameEngine.slnx`
`/src/`に`CSharpGameEngine.StaticArchetype.csproj`、`/samples/`に`StaticArchetypeSystemsDemo.csproj`を
追加した。

## 意図的に除外したこと

- `UserGameProject`への統合(本設計での既存Systemの書き換え)は行っていない。
- `GameSystemGenerator`/`CSharpGameEngine.Entities`(旧・現行の動的Archetype実装)の削除は行って
  いない。
- グローバル名前空間の衝突リスク(ADR-0032未解決事項)への対応は行っていない。
