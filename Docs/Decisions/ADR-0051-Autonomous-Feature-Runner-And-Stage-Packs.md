# ADR-0051 — Autonomous Feature Runner and Stage Packs

- Status: Accepted
- Date: 2026-08-25

## Context

既存テストはproject単位で実行できるが、Feature ID、visual scene、deterministic seed/tick、WARP、再開可能な結果artifactが一つの契約になっていなかった。将来Simulation/Physics機能はsolver・artifact・GPU・Editorを跨ぐため、未実装を成功扱いせず機能単位で追跡できる実行面が必要である。

静的ECSのComponent maskはSource Generator内でのみ使われ、現行上限は64型である。65型以上への拡張を今すぐ行う理由はなく、既存pathへpenaltyを入れてはならない。

## Decision

1. L7に`CSharpGameEngine.FeatureRunner`を置き、C# catalogをFeature/Stage対応の正本にする。
2. CLIは`list`、`run`、`run-feature`、`suite`、`resume`を提供する。
3. 結果をversioned JSONとして`.artifacts/FeatureStages`へ保存する。未実装、失敗、timeout、基盤障害を区別する。
4. 既存48機能は実行可能Stageへ割り当てる。将来28機能はOracleを持つ`NotImplemented` Stageへ割り当てる。
5. Visual検証の必須adapterはWARP、実GPUは任意の追加passとする。
6. PackはFoundation/Rendering/Rigid/Deformable/Fluid/Destruction/Celestial/ProbabilityQuantum/Coupling/VSIXへ分ける。
7. Visual Stageの正本はC# `IScene`とし、手書きMainで選択Sceneだけを実行する。`[StartupScene]`を使わない。
8. 外部native bindingはL0、任意algorithm providerはL4 adapterへ置く。L5とartifactへ外部型を漏らさず、engine-owned CPU referenceを必須correctness経路とする。
9. ECSは当面64型上限を維持する。F-L3-020で将来、1–64単一mask、65–128二重mask、129以上診断を実装する。

## Alternatives considered

- xUnit categoryだけを正本にする: Visual process、adapter、seed/tick、artifact、resumeを表現できない。
- 一つの巨大Stage executableへ全Domainを入れる: native/GPU障害、timeout、依存、再開単位を隔離できない。
- Component mask拡張を先行する: 現在のComponent数に必要なく、今の単一mask pathを変更するリスクが勝る。
- 外部solverをL5から直接参照する: public contractとartifactへprovider型が漏れ、置換性とdeterministic referenceを失う。

## Consequences

- Feature ID重複はcatalogテストで検出できる。
- 未実装Stageも選択・結果保存できるが合格にはならない。
- full suiteは実装済みStageだけを対象にでき、将来項目の存在を隠さない。
- Pack executableと各solverはcatalog/Oracleに従って段階追加する必要がある。
- Hardware結果だけではVisual Stageの必須検証を満たさない。

## Verification

- `dotnet test tests/CSharpGameEngine.FeatureRunner.Tests/CSharpGameEngine.FeatureRunner.Tests.csproj --configuration Debug`
- `dotnet run --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj -- list --json`
- `dotnet run --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj -- run-feature F-L5-004`

## Related features

- F-L7-021 — Feature Runner
- F-L9-008 — Feature Stage Packs
- F-L3-020 — Extended static ECS component mask
- F-L5-004〜F-L5-034 — Future Simulation/Physics capabilities
