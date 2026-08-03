# ADR-0014: CSharpGameEngine.CodeAnalysis ソースジェネレーター基盤

## ステータス
承認済み

## 背景

`Docs/Architecture.md`/ADR-0001が定義するL7(Static Compiler / Bake)に、Source GeneratorとAnalyzerが
計画済み機能として位置づけられているが、これまで一切実装されていなかった。ECS/Scene/Component/
GameLoop/Serializerに対する具体的なコード生成仕様はまだ確定していないため、今回は**仕様非依存の
配線検証のみを行う最小基盤**を追加する。将来Incremental Generator・Diagnostic Analyzer・Code Fixを
安全に追加できる土台を作ることが目的であり、具体的な生成機能そのものは意図的にスコープ外とした。

## 決定事項

### TargetFramework: `netstandard2.0`
Analyzer/Generatorアセンブリは、`devenv.exe`の設計時ビルド・`VBCSCompiler.exe`(ビルドサーバー)・
`dotnet build`単体実行など、実行中のSDKのTFMとは異なるコンパイラホストにin-processでロードされる。
`netstandard2.0`は`Microsoft.CodeAnalysis.CSharp`パッケージ自身が現在も維持する最低共通ベースラインで
あり、`tools/CSharpGameEngine.VSExtension`のnet472固定と同型の「コンパイラホストの制約が自分のSDKの
TFMより優先する」という既存ルールをRoslyn側に適用したものである。このリポジトリでnetstandard2.0を
使う初めてのプロジェクトになる。

### LangVersion: `14`(明示指定)
ルート`Directory.Build.props`の`LangVersion=default`は、net472/netstandard2.0のような.NET 5以前の
TFMではC# 7.3に解決されてしまい、Nullable注釈と両立しない。`tools/CSharpGameEngine.VSExtension`が
既に`LangVersion=14`で明示上書きしている前例と同じ対処を適用した。`14`は`global.json`が固定する
.NET SDK 10.0.302が既定でコンパイルするC#バージョンと一致させたものであり、新しい値を持ち込んでいない。

### パッケージ構成とバージョン選定
`Microsoft.CodeAnalysis.CSharp`/`Microsoft.CodeAnalysis.Analyzers`ともに`4.14.0`を採用した(いずれも
`PrivateAssets="all"`)。実装時に実際のNuGetを確認したところ、両パッケージの最新安定版は`5.6.0`
だったが、`5.6.0`は採用しなかった。理由: NuGet上の`Microsoft.CodeAnalysis.CSharp 4.14.0`の`.nuspec`に
記載されたリポジトリ情報(`branch="release/dev17.14"`)が、このリポジトリが既に`tools/
CSharpGameEngine.VSExtension`で参照している`Microsoft.VisualStudio.SDK`のバージョン系列
(`17.14.40265`)と直接一致することを確認した。Analyzer/GeneratorアセンブリはコンパイラホストのRoslyn
バージョンとin-processで整合する必要があり、ホストより新しすぎるAPI surfaceでビルドすると設計時解析
やVSでの読み込みに失敗するリスクがあるため、迷った場合は枯れたバージョンを優先するという方針のもと
`5.6.0`ではなく`4.14.0`(VS 17.14と同一ブランチ由来)を選んだ。両パッケージともnetstandard2.0
ターゲットグループを提供していることを`.nuspec`で確認済み。

`IsRoslynComponent=true`・`EnforceExtendedAnalyzerRules=true`を設定し、Roslynコンポーネント作成者向け
の拡張診断(RS####系)を有効化した。`TreatWarningsAsErrors=true`はルートから継承されるため、これらの
診断が発生すればビルドが失敗する状態だが、実際に`dotnet build`したところ警告0件で成功した(下記
検証結果参照)。

**Code Fix対応について**: `Microsoft.CodeAnalysis.CSharp.Workspaces`は今回追加しなかった。実際に使う
`CodeFixProvider`が存在しないため、追加すれば未使用パッケージ参照になる。TFM/`IsRoslynComponent`の
選択自体が将来の`CodeFixProvider`追加を妨げないため、「将来追加できる」という要件はプロジェクト設定
のみで満たされている。

### NuGetパッケージング
`analyzers/dotnet/cs`への`PackagePath`指定は、NuGetクライアントがパッケージをAnalyzerとして自動認識し、
`<PackageReference>`だけで`OutputItemType="Analyzer"`相当の配線を行う標準的な慣習である。
`IncludeBuildOutput=false`+`DevelopmentDependency=true`により、DLLが`lib/`にも重複して配置されたり、
このパッケージを導入したプロジェクトの通常の実行時依存グラフに紛れ込んだりしないようにした。
`NU5128`(lib/refアセットが無いことについての警告)は、Analyzer専用パッケージでは既知の想定内警告
のため抑制した。`dotnet pack -c Release`を実際に実行し、生成された`.nupkg`内に
`analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll`のみが含まれ、`lib/`配下には何も無いことを
`unzip -l`で確認済み(下記検証結果参照)。

### `CSharpGameEngine.CodeAnalysis.Abstractions`を作らない判断
DiagnosticID命名規則・Hint Name命名規則・生成コードヘッダー・安定ハッシュ等の共有ユーティリティは、
共有先となる「2つ目のGenerator」または「最初のDiagnosticAnalyzer」が実在して初めて実体を持つ。
現時点ではGeneratorは`CSharpGameEngineBootstrapGenerator`1つのみ・Analyzerはゼロであり、提案された
内容はいずれも呼び出し元を持たない投機的コードになる。SKILL.mdの「関係のない将来機能を実装しない」
原則に反するため、今回は作成しなかった。

**再検討のトリガー条件**: (1) 2つ目のGeneratorが追加され、Hint Name/生成コードヘッダーの共通化が
必要になった時、または(2) 最初の`DiagnosticAnalyzer`が追加され、DiagnosticID命名規則の共有が必要に
なった時。いずれかが発生した時点で、その具体的な呼び出し元を持つ内容から`CodeAnalysis.Abstractions`
を新規作成する。

### `CSharpGameEngine.CodeAnalysis.Testing`を作らない判断
Generator/Analyzerが1つずつ(実質Generatorのみ)しかない現状では、`Microsoft.CodeAnalysis.Testing`系
パッケージによるボイラープレート削減効果が、新規パッケージ依存を持ち込むコストを上回らない。
`CSharpGameEngine.CodeAnalysis.Tests`から`Microsoft.CodeAnalysis.CSharp`を直接参照し、生の
`CSharpCompilation`+`CSharpGeneratorDriver`でテストする方式を採用した。このリポジトリに
スナップショット/ゴールデンファイル系テストヘルパーの前例が無いことも踏まえた判断である。

### `CSharpGameEngineBootstrapGenerator`の設計
`Initialize`内は`context.RegisterPostInitializationOutput(...)`の1呼び出しのみとし、
`CompilationProvider`/`AnalyzerConfigOptionsProvider`を消費する追加パイプライン段は意図的に追加
しなかった。出力に使わなければ死んだコード、出力に使えば「ビルドごとに内容が変化しない」という
要件に反するため、どちらの道筋も正当化できない。「CompilationまたはAnalyzerConfigOptionsを受け取る」
という配線検証要件は、`CSharpGameEngine.CodeAnalysis.Tests`側で実際の`CSharpCompilation`を構築し
本物の`CSharpGeneratorDriver`に通すことで満たしている。

生成する文字列は`string[]`の各行を`"\n"`で結合して構築しており、生成元`.cs`ファイル自身の物理改行
コード(Windows開発機とLinux CIの`core.autocrlf`差異)に依存しない。`SourceText.From(source,
Encoding.UTF8)`でエンコーディングを明示し、`EnforceExtendedAnalyzerRules`下でのエンコーディング関連
診断のリスクを避けた。

## 根拠
- ADR-0001の「ランタイムL0-L6はRoslyn/VS SDKに依存してはならない」という既存境界を、
  `CSharpGameEngine.CodeAnalysis`をどの既存プロジェクトからも一切参照させないことで、実際に
  「Generatorが存在しなくてもRuntimeの設計が壊れない」という形で具体的に担保した。
- パッケージバージョンをVS 17.14と同一ブランチ由来の`4.14.0`に固定したことで、実際にこのリポジトリの
  VSIXが対象とするVSバージョンとのホスト互換性リスクを最小化した(推測ではなく`.nuspec`の実データで
  確認)。
- Abstractions/Testingプロジェクトを見送ったことで、呼び出し元の無い投機的コードや、前例の無い
  テストヘルパー依存を持ち込まずに済んだ。

## 検証結果(実際に実行して確認)
- `dotnet restore CSharpGameEngine.slnx`: 成功。
- `dotnet build CSharpGameEngine.slnx -c Debug`: 成功、警告0件・エラー0件(`EnforceExtendedAnalyzerRules`
  によるRS####系診断も発生しなかった)。
- `dotnet build CSharpGameEngine.slnx -c Release`: 成功、警告0件・エラー0件。
- `dotnet pack src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Release`:
  成功。`unzip -l`で`.nupkg`内容を確認し、`analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll`のみが
  含まれ、`lib/`配下には何も含まれないことを確認した。
- `dotnet test CSharpGameEngine.slnx`: 既存88件 + 新規3件(`BootstrapGeneratorTests`)の合計91件が
  すべて成功。既存テストに回帰は無かった。
- 決定性の証明: `RunGenerators_ProducesIdenticalOutputAcrossIndependentRuns`テストで、独立した2つの
  `CSharpCompilation`/`CSharpGeneratorDriver`ペアから同一Generatorを実行し、出力文字列が完全一致
  すること、かつユーザー提示のフォーマットと厳密一致することを確認した。生成コードは
  `CSharpSyntaxTree.ParseText`で再パースし、構文診断が0件であることも確認した(
  `GeneratedSource_ParsesWithoutDiagnostics`)。

## 未解決事項
- `CSharpGameEngine.CodeAnalysis`は今回どの既存プロジェクトからも参照していないため、実際のVisual
  Studio上でこのAnalyzerパッケージ/プロジェクト参照が正しくロードされることの実機確認(スクリーン
  ショット等)は行っていない。将来、実際にこのGenerator/Analyzerを何らかのプロジェクトから消費する
  時点で実施する。
- `CSharpGameEngine.CodeAnalysis.Abstractions`・`CSharpGameEngine.CodeAnalysis.Testing`の再検討
  トリガー条件は上記「決定事項」に記載の通り。

## 関連機能
- F-L7-001 — CSharpGameEngine.CodeAnalysisプロジェクト基盤
- F-L7-002 — CSharpGameEngineBootstrapGenerator
