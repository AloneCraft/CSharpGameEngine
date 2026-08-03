# M0001: Source Generator/Analyzer基盤の追加

このリポジトリで初めて作成するMilestone文書です。`.github/skills/CSharpGameEngine-Documentation/
SKILL.md`のMilestone update rulesに従って記述します。

## 目的
L7(Static Compiler / Bake)で計画済みだったSource Generator/Analyzer機能について、具体的な生成仕様
(ECS/Scene/Component/GameLoop/Serializer等)に依存しない、最小の配線検証用基盤を確立する。将来
Incremental Generator・Diagnostic Analyzer・Code Fixを安全に追加できる土台を作ることが目的。

## 実装したこと
- `src/CSharpGameEngine.CodeAnalysis/`: netstandard2.0・`LangVersion=14`のRoslyn Analyzer/Generator
  プロジェクト。`IsRoslynComponent`/`EnforceExtendedAnalyzerRules`を有効化し、`Microsoft.CodeAnalysis.
  CSharp`/`Microsoft.CodeAnalysis.Analyzers` 4.14.0(VS 17.14と同一ブランチ由来、`PrivateAssets="all"`)
  を参照。`analyzers/dotnet/cs`規約でRelease向けにNuGetパッケージ化可能。
- `CSharpGameEngineBootstrapGenerator`: 唯一の最小Incremental Generator。`RegisterPostInitialization
  Output`で、コンパイル内容によらず常に同一の`CSharpGameEngineBootstrap.g.cs`(`CodeAnalysisBootstrap.
  GeneratorName`定数)を出力する。
- `tests/CSharpGameEngine.CodeAnalysis.Tests/`: `Microsoft.CodeAnalysis.CSharp`を直接参照し、生の
  `CSharpCompilation`+`CSharpGeneratorDriver`で(a)生成テキストの厳密一致、(b)再パースでの構文診断
  0件、(c)独立2回実行での出力完全一致(決定性)、の3点を検証する単体テスト3件。
- `CSharpGameEngine.slnx`に新規2プロジェクト・ADR-0014・本Milestone文書・`Implementation-Status.md`
  を登録。
- `Docs/Decisions/ADR-0014-CodeAnalysis-Generator-Foundation.md`(TargetFramework/LangVersion/
  パッケージバージョン選定の根拠、Abstractions/Testing非作成の判断とトリガー条件を記録)。
- `Docs/Implementation-Status.md`(このリポジトリ初のFeature ledger、`F-L7-001`/`F-L7-002`の2件)。

## 意図的に除外したこと
- `CSharpGameEngine.CodeAnalysis.Abstractions`: 呼び出し元(2つ目のGeneratorまたは最初のAnalyzer)が
  存在しないため、投機的コードになるとして作成しなかった(ADR-0014に再検討トリガー条件を記録)。
- `tests/CSharpGameEngine.CodeAnalysis.Testing/`: Generator/Analyzerが1つしかない現状ではボイラー
  プレート削減効果がコストを上回らないため作成しなかった。
- ECS/Scene/Component/GameLoop/Serializerに対する具体的なコード生成ロジック: 生成仕様が未確定のため
  スコープ外。
- `DiagnosticAnalyzer`/`CodeFixProvider`の実クラス: プロジェクト設定のみ将来対応可能にし、実装はしていない。
- 既存プロジェクト(Runtime側)への組み込み: `CSharpGameEngine.CodeAnalysis`はどの既存csprojからも
  参照していない(`OutputItemType="Analyzer"`も含め、参照ゼロ)。「Generatorが存在しなくてもRuntimeの
  設計が壊れない」という要件を、実際に何も参照させないことで満たした。
- `Docs/Reverse-Engineered-Spec.md`/`Docs/Conformance.md`のリポジトリ全体への新規導入: 今回のタスクは
  この2ドキュメント新規作成をユーザーと確認しておらず、スコープ外とした(Implementation-Status.mdと
  Milestone文書のみ、ユーザーとの確認の上で新規作成した)。

## 変更したFeature ID
- `F-L7-001` — CSharpGameEngine.CodeAnalysisプロジェクト基盤(新規、Implemented/TestVerified)
- `F-L7-002` — CSharpGameEngineBootstrapGenerator(新規、Implemented/TestVerified)

## 実行したコマンドと結果
```
dotnet restore CSharpGameEngine.slnx                                                  → 成功
dotnet build CSharpGameEngine.slnx -c Debug                                           → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release                                         → 成功、警告0/エラー0
dotnet pack src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Release
                                                                                        → 成功
dotnet test CSharpGameEngine.slnx                                                     → 91件成功(88件既存+3件新規)、失敗0
```
`.nupkg`の内容を`unzip -l`で確認し、`analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll`のみが
含まれ`lib/`配下が空であることを確認した。

## リバースエンジニアリングした挙動
- `CSharpGameEngineBootstrapGenerator`は`Compilation`/`AnalyzerConfigOptionsProvider`のいずれの
  パイプライン入力も購読していない設計であり、その代わりにテスト側(`BootstrapGeneratorTests`)が実際に
  `CSharpCompilation`を構築して`CSharpGeneratorDriver`に通すことで、`IIncrementalGenerator`パイプライン
  全体との配線を実証している。
- 生成される`CodeAnalysisBootstrap.GeneratorName`定数の文字列は、独立した2回のGenerator実行間で
  完全に一致し(`RunGenerators_ProducesIdenticalOutputAcrossIndependentRuns`で確認)、ユーザー提示の
  フォーマットとも厳密一致する。
- `EnforceExtendedAnalyzerRules=true`を有効化した状態でも、この最小Generatorの実装はRS####系の拡張
  診断を一切発生させなかった(Debug/Release両ビルドで警告0件)。

## 意図と観測の一致
Conformance: Matches。ADR-0014に記載した意図した設計(TargetFramework選定・パッケージバージョン選定・
Abstractions/Testing非作成・Generator設計方針)と、実際にビルド・パック・テストで確認した挙動の間に
既知の乖離は無い。

## 残課題
- `CSharpGameEngine.CodeAnalysis`をどの既存プロジェクトからも参照していないため、実際のVisual Studio
  上でこのAnalyzerパッケージ/プロジェクト参照が正しくロードされることの実機確認(スクリーンショット等)
  は本マイルストーンでは行っていない。将来、実際にこのGenerator/Analyzerを何らかのプロジェクトから
  消費するマイルストーンで実施する。
- `CSharpGameEngine.CodeAnalysis.Abstractions`/`CSharpGameEngine.CodeAnalysis.Testing`の再検討トリガー
  条件はADR-0014に記録済み(2つ目のGenerator、または最初のDiagnosticAnalyzerの追加時)。
- 次のマイルストーンに進む前提条件: 具体的な生成対象の仕様(ECS/Scene等)が確定すること。
