# 自律Feature Stage仕様

## 目的

`CSharpGameEngine.FeatureRunner`は、機能IDと自動検証シナリオを一対多で結び、対話操作なしで実行・再開・結果保存するL7ツールである。実装済み機能は実行可能なStageへ、未実装機能はOracleまで確定した`NotImplemented` Stageへ登録する。未実装・タイムアウト・基盤障害を成功として扱わない。

正本は`tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs`であり、この文書は人間向けの契約である。カタログの一意性、参照整合性、実装済み機能の実行可能Stage割り当て、未実装機能の`NotImplemented`割り当ては自動テストで固定する。

## Catalog v2 and non-physics registration

Catalog JSON is `csharp-game-engine-feature-catalog-v2`. Each Feature now publishes `track`, `dependencyLevel`, and `dependsOnFeatureIds` in addition to ID/name/layer/status. Run-result JSON remains `csharp-game-engine-feature-run-result-v1`; this change does not alter a completed run artifact.

The six non-physics tracks have 66 Roadmap rows registered one-to-one as Feature/target-Stage pairs. F-L0-008 and F-L3-019 remain Specified; F-L7-030, F-L3-018, F-L7-022, F-L2-005, F-L7-049, F-L6-004, and F-L8-031 are Implemented with executable target stages; F-L3-020 remains `Deferred`. The remaining target Stages are `NotImplemented` with an oracle and input paths. The two pre-existing stage IDs `foundation.d3d12-compute` and `foundation.deterministic-jobs` are reused; the remaining target Stage IDs are registered in the catalog.

| Track | Specification | Visual Pack policy |
|---|---|---|
| Rendering | [Rendering](Specifications/Rendering.md) | Rendering Pack with required WARP and optional hardware. |
| Asset / Content | [Asset / Content Pipeline](Specifications/Asset-Content-Pipeline.md) | Foundation, except VSIX asset authoring. |
| Editor / VSIX | [Editor / VSIX](Specifications/Editor-VSIX.md) | VSIX Pack; only requested visual aliases use the C# reference scene. |
| ECS / Runtime | [ECS / Runtime](Specifications/Ecs-Runtime.md) | Foundation headless contracts. |
| Audio / Input / Platform | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | Foundation headless contracts plus VSIX diagnostics. |
| Testing / CI / Release | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | Foundation headless contracts. |

The existing ten Packs remain unchanged: Foundation, Rendering, Rigid, Deformable, Fluid, Destruction, Celestial, ProbabilityQuantum, Coupling, and VSIX. Visual aliases for the new Rendering/VSIX reservations bind to existing C# reference scenes; no pack project was added.

Timeout defaults are 900 seconds for contract, 1800 for visual/integration, 3600 for Experimental Visual Studio, 7200 for performance/release, and 28800 for stress. A normal `changed`, `smoke`, `full`, `stress`, or `vsix` suite selects executable Stages only. Explicit `run` or `run-feature` on a future feature returns nonzero `NotImplemented` and records it in a v1 run-result artifact.

Release scope selects tracks and a reached dependency Level. Only executable Features in that closure are required verdicts; `Specified`/`Deferred` work remains visible but cannot become a skipped success.

## CLI

```text
list [--json]
run <stage-id> [options]
run-feature <feature-id> [options]
suite <changed|smoke|full|stress|vsix> [options]
resume <run-result.json> [options]
```

共通optionは`--configuration Debug|Release`、`--profile <name>`、`--adapter warp|hardware`、`--seed <uint>`、`--ticks <int>`、`--repository-root <path>`である。既定adapterはWARPであり、Hardwareは追加確認用であってWARP合格を置き換えない。

## 結果契約

結果は`.artifacts/FeatureStages/*.run-result.json`へ保存し、Git管理しない。schemaは`csharp-game-engine-feature-run-result-v1`で、各Stageについて次を保持する。

- Stage ID、Feature ID、Pack、Configuration、Profile、Adapter
- seed、tick数、Git commit、dirty状態
- 開始・終了UTC、所要時間
- state hash、event hash、assertion、error
- evidence payloadのSHA-256
- `Passed`、`Failed`、`Blocked`、`NotImplemented`、`TimedOut`、`InfrastructureError`

`NotImplemented`は終了コード非0である。`resume`は以前の結果から`Passed`以外だけを再選択する。

## 検証tier

| Suite | 目標時間 | 用途 |
|---|---:|---|
| changed | 約15分以内 | dirty pathとStage input pathの対応から影響Stageを選択 |
| smoke | 約15分以内 | 日常の主要CPU・WARP・VSIX契約 |
| full | 約2時間以内 | 全実装済みStageのDebug/Release相当検証 |
| stress | 約8時間以内 | 長時間・反復決定性・高負荷検証 |
| vsix | 約2時間以内 | VSIX model/packageとExperimental Instance候補 |

将来Stageはカタログには常在するが、実装されるまでsuiteの合格対象へ混ぜない。`run`または`run-feature`で明示実行すると`NotImplemented`になる。

## 既存機能Stage

| Stage ID | Pack | 対象Feature ID | Oracle |
|---|---|---|---|
| `foundation.codegen` | Foundation | F-L7-001/002/012/013/015/016/017 | Generator・Analyzerテストが全て成功 |
| `foundation.core-spatial` | Foundation | F-L1-001/002/003/005/006, F-L2-003, F-L0-006 | Core・Numerics契約が全て成功 |
| `geometry.import` | Foundation | F-L0-003/005, F-L2-002, F-L7-005 | fixtureと座標変換が期待artifactに一致 |
| `rendering.warp-triangle` | Rendering | F-L0-001/002/004/007, F-L4-002 | hidden WARP readback成功、D3D12 error 0 |
| `ecs.scene` | Foundation | F-L3-011/012/013 | 生成Archetype・Scene・起動lifecycle契約が成功 |
| `ecs.baseline` | Foundation | F-L7-030 | Entities/CodeAnalysis全テストで閉集合Archetype、Scene lifecycle、system-major、遅延構造変更のbaselineが成功 |
| `ecs.fixed-tick` | Foundation | F-L3-018 | 60 Hz、最大8 tick、backlog保持、pause/step、補間境界のScheduler/生成E2Eが成功 |
| `audio.windows` | Foundation | F-L1-007, F-L4-001 | Audio abstraction/XAudio2契約が成功 |
| `vsix.toolchain` | VSIX | F-L8-001/002/005/008 | command/package/bridgeテストが成功 |
| `vsix.scene-editor` | VSIX | F-L8-010/011/012/015/016/017 | 編集・同期・localization・routing契約が成功 |
| `spatial.runtime` | Foundation | F-L3-015/016/017 | Octree・snapshot・debug projection契約が成功 |
| `rigid.physics` | Rigid | F-L5-001/002/003, F-L3-018, F-L6-002, F-L7-020, F-L8-014 | deterministic state/message/physics/artifact/debug契約が成功 |
| `runner.contract` | Foundation | F-L7-021 | catalog・CLI選択・結果schemaテストが成功 |
| `stage-packs.contract` | Foundation | F-L9-008 | 10個の独立PackがC# `IScene`を生成・実体化・unloadし、安定state hashを出力 |

## 将来Simulation Stage

| Feature ID | Stage ID | Pack | 決定的Oracle概要 |
|---|---|---|---|
| F-L5-004 | `rigid.joints` | Rigid | pendulumとlocked jointの拘束drift |
| F-L5-005 | `rigid.ragdoll` | Rigid | pose handoff、joint limit、最終state hash |
| F-L5-006 | `rigid.character-controller` | Rigid | stair/slope sweep、penetration 0 |
| F-L5-007 | `rigid.vehicle` | Rigid | acceleration、brake、suspension、lap state |
| F-L5-008 | `rigid.dynamic-ccd` | Rigid | 高速対向BodyのTOI contactと非tunneling |
| F-L5-009 | `rigid.parallel-solver` | Rigid | single-threadとgraph-coloringのhash一致 |
| F-L5-010 | `rigid.gpu-solver` | Rigid | WARP computeとCPU referenceの誤差予算内一致 |
| F-L5-011 | `rigid.rollback` | Rigid | snapshot復元・command replayのhash一致 |
| F-L5-012 | `deformable.xpbd` | Deformable | distance constraint収束とenergy budget |
| F-L5-013 | `deformable.cloth` | Deformable | pin、非反転、vertex tolerance |
| F-L5-014 | `deformable.rope` | Deformable | endpoint、segment長、collision event |
| F-L5-015 | `deformable.soft-body` | Deformable | volume保存とcollision recovery |
| F-L5-016 | `deformable.fem` | Deformable | canonical beamの変位・応力誤差 |
| F-L5-017 | `deformable.mpm` | Deformable | mass・momentum budget |
| F-L5-018 | `fluid.sph-pbf` | Fluid | density誤差、mass保存、boundary impulse |
| F-L5-019 | `fluid.flip-apic` | Fluid | particle-grid transferのmass/energy |
| F-L5-020 | `fluid.eulerian-grid` | Fluid | divergence低減とscalar mass保存 |
| F-L5-021 | `destruction.runtime-topology` | Destruction | closed partition、stable ID、collision cook |
| F-L5-022 | `destruction.voxel-sdf` | Destruction | affected chunk限定更新とsurface hash |
| F-L5-023 | `celestial.kepler` | Celestial | 1周期closure、位置・energy誤差 |
| F-L5-024 | `celestial.direct-nbody` | Celestial | 2/3体referenceのenergy・momentum誤差 |
| F-L5-025 | `celestial.barnes-hut` | Celestial | Direct N-bodyに対するtheta依存誤差 |
| F-L5-026 | `celestial.fmm` | Celestial | Direct N-bodyに対するorder依存field誤差 |
| F-L5-027 | `celestial.hierarchical-frames` | Celestial | frame往復とrebaseのscale別精度 |
| F-L5-028 | `probability.gameplay` | ProbabilityQuantum | seed付きevent列と分布検定 |
| F-L5-029 | `probability.wavefunction-grid` | ProbabilityQuantum | norm、phase、解析解に対する誤差 |
| F-L5-030 | `probability.monte-carlo-quantum` | ProbabilityQuantum | provenance、再現性、信頼区間 |
| F-L5-031 | `coupling.multidomain` | Coupling | 翌tick bridgeのownership/order/unit/保存則 |

科学計算Stageは再現可能なreference実装と明記した誤差予算を検証する。研究成果、認証、現実世界での安全性を主張しない。

## 基盤Stageと64型上限

- `foundation.extended-ecs-mask` / F-L3-020は`Deferred`である。現行は1–64 Component型をSource Generator内の単一`long` maskで処理し、65型目でSYSGEN026を出す。生成Runtimeへmask照合は残らないため、64型以下に実行時penaltyはない。
- 将来実装は1–64を単一`ulong` path、65–128を二重`ulong` path、129以上を明示診断とする。既存1–64生成コードを変えないことを性能Oracleに含める。
- D3D12 compute/readback、deterministic job runtime、L4 adapter、field/graph artifact、pose bridge、volume renderer、thermal fieldにも独立Stageを予約する。

## 外部ライブラリ境界

- OS、native API、driver bindingはL0に置き、Win32/D3D12と同じ抽象度にする。
- FFT、sparse solver、geometry、compute等の任意providerはL4 adapterの背後に置く。
- L5 solverはengine-ownedのL4 contractだけを見る。外部型をpublic API、Component、Command/Event、artifactへ出さない。
- correctnessの必須経路はengine-owned CPU referenceとし、外部providerやGPUは追加backendである。

## Stage Pack分離

PackはFoundation、Rendering、Rigid、Deformable、Fluid、Destruction、Celestial、ProbabilityQuantum、Coupling、VSIXに分離する。分離理由はComponent mask上限ではなく、solver依存、native/GPU初期化、障害隔離、timeout、artifact量、再開単位である。Visual StageはC# `IScene`を正本とし、手書きMainが選択Sceneだけを固定tick実行する。`[StartupScene]`は使用しない。

10個の独立Pack実行プロジェクト、共有host、各PackのC# `IScene`、生成ECSの実体化・ownership・unload・state hash検証まで実装済みである。将来solver本体は、対応Featureが実装されるまで明示的な`NotImplemented` Stageとして維持する。


## M0060 executable shader stages

rendering.baseline fixes the WARP visual baseline. shadergraph.schema validates canonical graph contracts, shadergraph.compiler compiles all fixed entries, rendering.shader-artifacts validates the container and artifact-only Runtime, and vsix.shader-graph validates the editor/package surface. All commands run sequentially through Feature Runner in the requested configuration.

## M0061 external Subgraph evidence

The existing `shadergraph.schema`, `shadergraph.compiler`, `rendering.shader-artifacts`, and `vsix.shader-graph` stages now include canonical external dependency metadata, exact Stage HLSL composition, dependency-hash artifact reuse, production Subgraph packaging, and template/navigation tests. No new Feature or Stage ID is introduced.

## M0062 structured Region evidence

The existing shadergraph.schema stage now fixes semantic If/ForRange ownership, Then/Else/Body membership, bounded loops, typed Return, LoopIndex scope, branch value isolation, canonical hashing, and source maps. vsix.shader-graph covers built-in palette creation, exact typed connections, structured Region creation, Undo, localization, and VSIX packaging. shadergraph.compiler retains the production nine-entry DXC oracle; no Feature or Stage ID is added.
