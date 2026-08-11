# M0016: `[SystemOrder]`属性によるソースコード間の実行順序解決

## 目的

ユーザーの依頼「ランタイムに属性を作り、`file`スコープの空クラスへ付けることでそのファイル全体の
実行順序を指定できるようにしてほしい(ファイル内は記述順のまま)」から始まり、実装後の追加依頼
「file orderではなくsystem orderにしてほしい。Systemに付けてもいいしマーカーに付けてもいい。
ソースコード間の実行順序を決められるようにしてほしい」で最終形に拡張した。

[[M0015-Generated-Entry-Point]]の`UserEcsProject`分割で生じた実行順の問題(`GameSystems.cs`→
`RuntimeSystem.cs`の順序が「ファイル名の`G<R`」という偶然に依存していた)を解決する。
設計判断は[[ADR-0036-System-Order-Attribute]]に記録した。

## 実装したこと

### ランタイム `src/CSharpGameEngine.Entities/`
- `SystemOrderAttribute.cs`(新規): `[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]`の
  `SystemOrderAttribute(int order)`。`ISystem`実装struct自身に直接(そのSystem1つだけの順序)、または
  `file`スコープのマーカークラスに(ファイル既定値)、どちらにも付けられる。
  ADR-0033決定事項2(属性ゼロの構造的推論)からの唯一の例外。

### `GameSystemGenerator`(`src/CSharpGameEngine.CodeAnalysis/`)
- `GameSystemModel.cs`: `GameSystemOrderTargetKind`(`System`/`FileMarker`)、`GameSystemOrderResult`、
  `GameSystemOrderContext`(System単体マップとファイル既定値マップを束ねる)を追加。
- `GameSystemAnalyzer.cs`: `ComputeClassFullyQualifiedName`を追加(`GameSystemResult.ClassFullyQualifiedName`
  と同じ規則でSystemの完全修飾名を作る共有ヘルパー。2箇所が別々の規則で名前を作るとキーがズレるため)。
- `GameSystemOrderAnalyzer.cs`(新規、`GameSystemFileOrderAnalyzer.cs`から改名): `ForAttributeWithMetadataName`で
  `[SystemOrder]`付きの型(struct/class)を収集する。対象がstructなら`ISystem`実装かを確認し
  (実装していなければSYSGEN025)`Kind=System`、classなら`file`スコープを検証し
  (でなければSYSGEN022)`Kind=FileMarker`とする。`BuildMap`が集約段で「1ファイル複数マーカー」
  (SYSGEN023)・「複数ファイルが同じファイル既定値」(SYSGEN024、System単体の重複は対象外)を検出し、
  `GameSystemOrderContext`を作る。
- `GameSystemExecutionOrder.cs`(新規): `GameSystemIndexAssigner`と`GameSystemEntryPointEmitter`が
  共有する唯一の比較関数。`OrderOf`が①System単体の明示値 ②ファイル既定値(いずれも既定0)の順で
  実効値を決め、比較キーは①その値 ②`FilePath`Ordinal(既存タイブレーク、後方互換) ③`DeclarationPosition`。
- `GameSystemIndexAssigner.Assign`/`GameSystemEntryPointEmitter.ShouldEmitPlan`: 独自ソートを
  `GameSystemExecutionOrder.Compare`呼び出しへ置き換え、`GameSystemOrderContext`を新しい引数として受け取る。
- `GameSystemGenerator.cs`: `systemOrders`という3本目の`ForAttributeWithMetadataName`プロバイダ
  (述語は`TypeDeclarationSyntax`でstruct/class両対応)を追加し、`combined`パイプラインへ組み込む。
- `GameSystemDiagnostics.cs`/`AnalyzerReleases.Unshipped.md`: SYSGEN022〜025を追加。

### `UserEcsProject/`
- `Systems/GameSystems.cs`: `GameStartupSystem`に`[SystemOrder(0)]`を**直接**付けた(System単体指定の実例)。
- `Systems/RuntimeSystem.cs`: `file`スコープのマーカーに`[SystemOrder(1)]`を付けた(ファイル既定値の実例)。
  「`G<R`だから」という偶然頼みのコメントを削除し、明示的な宣言に置き換えた。

### テスト
- `GameSystemOrderTests.cs`(新規、9件、`GameSystemFileOrderTests.cs`から改名・拡張):
  - `CrossSourceExecutionOrder_FollowsSystemOrderValue_NotAlphabeticalFileName`: ファイル名を
    アルファベット順にわざと逆らわせ(System直接指定とfileマーカーの両方を混在させて)、
    生成された`Main`を実行して実行順が`[SystemOrder]`どおりになることを証明する中核テスト。
  - `SystemOrderOnTheSystemItself_OverridesTheFileMarkerDefault`: System単体の明示値がファイル既定値
    より優先されることを実行して証明。
  - `FilesWithoutSystemOrder_StillTieBreakByFilePathOrdinal`: 属性を使わない既存挙動の後方互換回帰。
  - SYSGEN022(非`file`マーカー)・SYSGEN023(1ファイル2マーカー)・SYSGEN024(2ファイル同一既定値)・
    SYSGEN025(非ISystem structへの付与)の診断テスト。
  - `TwoSystemsWithTheSameExplicitOrder_ReportsNothing`: System単体の明示値同士の重複は許容される
    (ファイル既定値の重複とは扱いが違う)ことの回帰。
  - `UnattributedFilesSharingTheImplicitZero_ReportsNothing`: 暗黙の0同士はSYSGEN024の対象外である回帰。
- `GameSystemGeneratorTestHelper.cs`: `SystemOrderAttributeSource`を追加。ファイルパスを明示できる
  `CreateCompilation(IEnumerable<(string? Path, string Source)>)`/`CreateApplicationCompilation(...)`/
  `RunGenerator(...)`オーバーロードを追加(既定の`ParseText`はパス無し=空文字列になり、`file`スコープの
  型を含むソースはコンパイル全体でパスが一意でないとCS9068になるため)。
- テストcsprojに`SystemOrderAttribute.cs`のTestAssetsリンクを追加。

### ドキュメント
`ADR-0036`(新規、実装途中の要望変更を反映して`System-Order-Attribute`に改題)、`M0016`(本書)、
`ADR-0035`の未解決事項を実際に採用した方式に合わせて更新。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...
  → 73/73 成功(既存64件 + 新規9件)

dotnet build UserEcsProject/UserEcsProject.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build CSharpGameEngine.slnx -c Debug   → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx            → 全9プロジェクト成功、382件・失敗0
```

生成物の目視確認: `UserEcsProject/Generated/.../GameSystem.Loop.g.cs`のArchetype出現順
(`Archetype0{Bootstrap}` → … → `Archetype4{FramePresent}`)が、`[SystemOrder]`導入の前後で
**完全に同一**であることを確認した(偶然の`G<R`と同じ結論を、今度は明示的な宣言で再現している)。

実機: `UserEcsProject.exe`(apphost)を起動し、`PrintWindow`でクライアント領域をキャプチャした。
ウィンドウタイトル「C# Game Engine - ECS FBX Rendering PoC」、クリア色`#598CD9`
(=`StageRenderer.ClearColor`)を確認、標準出力/標準エラーへの出力なし(未処理例外なし)。

## 意図的に除外したこと

- System単体の明示値同士の重複チェック(ファイル既定値だけの規則。意図的な「同着」を許すため)。
- `[SystemOrder]`をComponentへ付ける経路(要望が無く、意味も無い)。

## 残課題

- 特になし。ADR-0033/ADR-0035で積み残していた「実行順序のファイル跨ぎ」問題はこれで解決した。
