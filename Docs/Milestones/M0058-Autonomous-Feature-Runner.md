# M0058 — Autonomous Feature Runner and Stage Packs

## Goal

既存機能と将来Simulation/Physics機能を、機能IDから非対話で選択・実行・再開できる基盤を作る。未実装を成功扱いせず、WARP、seed/tick、Oracle、結果hashを機械可読にする。

## Implemented

- `CSharpGameEngine.FeatureRunner` executableと`CSharpGameEngine.FeatureRunner.Tests`を追加し、slnxへ登録した。
- 既存48機能を10個の実行可能Stageへ割り当てた。
- 将来28機能、ECS拡張mask、compute/job/adapter/artifact/bridge基盤を`NotImplemented` Stageとして登録した。
- `list`、`run`、`run-feature`、`suite`、`resume`を実装した。
- changed path選択、process timeout、WARP引数、Git provenance、state/event hash、SHA-256付きversioned JSON結果を実装した。
- Foundation、Rendering、Rigid、Deformable、Fluid、Destruction、Celestial、ProbabilityQuantum、Coupling、VSIXの10個の独立Pack executableと共有C# `IScene` hostを実装した。
- 64型以下の単一mask契約を維持し、65型以上をF-L3-020へ延期した。
- ローカル変数に`var`を使う構文規約を共通ルールへ追加した。

## Deliberately excluded

- 65–128 Component型の二重mask実装。
- F-L5-004以降のsolver本体。
- Hardware adapterをWARPの代替合格にすること。

## Feature status changes

- F-L7-021: `Planned` → `Implemented`, `TestVerified` in Debug/Release
- F-L9-008: `Planned` → `Implemented`, `RuntimeVerified` in Debug/Release
- F-L3-020: `Planned` → `Deferred`, `Unverified`
- F-L5-004〜F-L5-031: `Deferred`, machine-readable stage design added

## Verification

- Debug/Release FeatureRunner tests: 11 passed per configuration, 0 failed, 0 skipped.
- 10個の独立Stage Pack project: Debug/Release build成功、警告0、エラー0。
- `stage-packs.contract`: Debug/Releaseとも10 Packすべてが生成ECSの実体化・ownership・unload・state hash検証に成功。
- 全ソリューションテスト: 通常の並列Debug/Release実行で各1,587 passed、0 failed、0 skipped。登録整合性テストを含む。
- 登録整合性テストはWPFビルド中だけ存在する`*_wpftmp.csproj`をsource projectとして誤検出しない。
- IDE0007 var-style verification: 0 diagnostics after correction.
- `list --json`: exit 0 and versioned catalog emitted.
- `run-feature F-L5-004`: `NotImplemented` result emitted and process remained non-success.

## Reverse-engineered behavior

Catalog validation rejects duplicate Feature/Stage IDs, unknown feature references, executable stages without commands, visual stages without WARP, invalid seed/tick/timeout, and features without stages. Suite selection excludes deferred stages; explicit selection records them as`NotImplemented`. Result files include current Git provenance and are ignored by Git.

## Conformance

FeatureRunner foundationと独立Stage Pack基盤はADR-0051に適合する。将来solver機能はPack基盤の合否から分離され、実装されるまで`NotImplemented`を維持する。

## Next action

Missing v1 rigid-body/cooking/cache/editor pathを完成させてから将来solverを有効化する。
