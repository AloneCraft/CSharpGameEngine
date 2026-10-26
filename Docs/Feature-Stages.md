# 自律Feature Stage仕様

## 目的

`CSharpGameEngine.FeatureRunner`は、機能IDと自動検証シナリオを一対多で結び、対話操作なしで実行・再開・結果保存するL7ツールである。実装済み機能は実行可能なStageへ、未実装機能はOracleまで確定した`NotImplemented` Stageへ登録する。未実装・タイムアウト・基盤障害を成功として扱わない。

正本は`tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs`であり、この文書は人間向けの契約である。カタログの一意性、参照整合性、実装済み機能の実行可能Stage割り当て、未実装機能の`NotImplemented`割り当ては自動テストで固定する。

## Catalog v2 and non-physics registration

Catalog JSON is `csharp-game-engine-feature-catalog-v2`. Each Feature now publishes `track`, `dependencyLevel`, and `dependsOnFeatureIds` in addition to ID/name/layer/status. Run-result JSON remains `csharp-game-engine-feature-run-result-v1`; this change does not alter a completed run artifact.

The six non-physics tracks have 65 Roadmap rows registered one-to-one as Feature/target-Stage pairs. Every target now has an executable Stage; F-L8-029 remains explicitly `Implementing` until its environment evidence passes, while no non-physics target remains `NotImplemented`. `rendering.performance` records five warm-ups and thirty isolated frame plus 100,000-instance samples, treats WARP as correctness, and applies shipping timing gates only to Release. `vsix.host-lifecycle` executes the shared host/session tests and the net472 resident-client submission-boundary tests. `vsix.inspector-contracts` executes declared-type editor, typed choice, component palette/mutation, and source-reparse contracts. `vsix.source-sync` executes the latest-source one-shot planner, canonical verification reanalysis, Scene-scoped semantic-generation landing, stable identity, typed stale rebase/reject, native Undo routing, source-only Save, product-caller, and no-private-history architecture contracts. `vsix.bake-integration` executes immutable Import/Bake presentation, request/progress/result correlation, cancellation, typed host diagnostics, partial artifact links, localization, and false-success rejection together with the shared host session/wire/server contracts. `vsix.debug-tools` and `vsix.platform-debug` were removed with F-L8-027 and F-L8-030 in M0097. `platform.compatibility` validates the exact executable prerequisite closure against mandatory Debug/Release solution, Windows-TFM, and WARP CI cells while hardware Audio/GPU stay optional ledger references. `vsix.compatibility` executes the versioned matrix validator plus focused manifest/package/localization/layout/automation tests; normal validation preserves explicit Pending evidence, while the separate release mode rejects it. Experimental Visual Studio commands run only in the `vsix` profile and map declared exit code 2 to InfrastructureError.

`platform.release` re-executes `platform.compatibility` with a release-only prerequisite profile and emits a configuration/Git/Warp-matched canonical Platform closure report; it does not create a product package or immutable product manifest.

`rendering.release` re-executes `rendering.performance` and `vsix.rendering-preview` under dedicated profiles and emits one configuration/Git/Warp-matched canonical report over the actual raw capture, both performance reports, the configuration-qualified Shader Artifact, and fixed Rendering source/toolchain closure. It is a track input, not a package or product manifest.

`ecs.performance` builds four isolated generated compilations at 1, 64, 65, and 128 Component types. Each performs exactly five warm-ups and thirty generated write-query samples over 100,000 live entities with Tiered JIT disabled. Independent validation recomputes the deterministic state hash and rehashes/reinspects persisted generated source, fixes the one-word/two-word boundary, and requires zero allocation. Release gates 6 ms median and 8.33 ms p95; the separately compiled 64-type path also has a 20-percent cross-process regression ceiling from the one-type direct path.

`ecs.release` reruns `foundation.deterministic-jobs`, `ecs.schedule-diagnostics`, `ecs.snapshot`, and `ecs.performance` under four dedicated profiles and emits one configuration/Git/Warp-matched canonical ECS-track report over their results, all raw/common performance evidence, and a fixed twenty-two-input source/toolchain closure. Creation independently verifies generated output; later report validation does not promote derived `obj` to retained truth. It is not a package or product manifest.

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
export-showcase --output <path>
verify-platform-compatibility [--repository-root <path>]
verify-platform-release [--repository-root <path>] [--configuration Debug|Release]
verify-rendering-performance [--repository-root <path>] [--configuration Debug|Release] [--adapter warp|hardware]
verify-rendering-release [--repository-root <path>] [--configuration Debug|Release]
run <stage-id> [options]
run-feature <feature-id> [options]
suite <changed|smoke|full|stress|vsix> [options]
resume <run-result.json> [options]
```

共通optionは`--configuration Debug|Release`、`--profile <name>`、`--adapter warp|hardware`、`--seed <uint>`、`--ticks <int>`、`--repository-root <path>`である。既定adapterはWARPであり、Hardwareは追加確認用であってWARP合格を置き換えない。

## 結果契約

結果は対象リポジトリ(`--repository-root`、無ければカレントディレクトリを含むリポジトリ)の`.artifacts/FeatureStages/*.run-result.json`へ保存し、Git管理しない。リリース証拠のStageは同じリポジトリのこの結果を探す。schemaは`csharp-game-engine-feature-run-result-v1`で、各Stageについて次を保持する。

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
| `ecs.snapshot` | Foundation | F-L6-007 | schema fingerprint、canonical cell delta、atomic restore、corruption/transient rejectionが成功 |
| `audio.windows` | Foundation | F-L1-007, F-L4-001 | Audio abstraction/XAudio2契約が成功 |
| `vsix.toolchain` | VSIX | F-L8-001/002/005/008 | command/package/bridgeテストが成功 |
| `vsix.scene-editor` | VSIX | F-L8-010/011/012/015/016/017 | 編集・同期・localization・routing契約が成功 |
| `spatial.runtime` | Foundation | F-L3-015/016/017 | Octree・snapshot・debug projection契約が成功 |
| `rigid.physics` | Rigid | F-L5-001/002/003, F-L3-018, F-L6-002, F-L7-020, F-L8-014 | deterministic state/message/physics/artifact、generated headless Scene Bake、debug契約が成功 |
| `rigid.character-controller` | Rigid | F-L5-006 | 休止・ジャンプ頂点・6 m/s歩行と制動・壁滑り・30°坂/40 cm段の登坂と50°/50 cmの拒否・完全再現、SpringArmの壁/床での短縮、ThirdPersonテンプレートのWARP実走(W→Spaceでカメラが水平に1 m以上移動、0.5 m以上上昇して着地、床抜けとD3D12エラーなし) |
| `destruction.prefracture` | Destruction | F-L5-049 | ボロノイの調理のバイト一致・閉じた破片・対称な面積の結合・つながったクラスター、強さちょうどで切れる結合、アンカー・上限・寿命・`BondBroken`の試験、破壊の見本のWARP実走(W・F・Eの自動入力で6個すべてが壊れる、破片が床より上で0.2 m/s以下に静まる、D3D12エラーなし)、焼き2回と再生のキャッシュのバイト一致(M0113) |
| `runner.contract` | Foundation | F-L7-021 | catalog・CLI選択・結果schemaテストが成功 |
| `stage-packs.contract` | Foundation | F-L9-008 | 10個の独立PackがC# `IScene`を生成・実体化・unloadし、安定state hashを出力 |

## 将来Simulation Stage

| Feature ID | Stage ID | Pack | 決定的Oracle概要 |
|---|---|---|---|
| F-L5-004 | `rigid.joints` | Rigid | pendulumとlocked jointの拘束drift |
| F-L5-005 | `rigid.ragdoll` | Rigid | pose handoff、joint limit、最終state hash |
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
| F-L5-027 | `celestial.frame-relative-runtime` | Celestial | frame往復とrebaseのscale別精度 |
| F-L5-028 | `probability.gameplay` | ProbabilityQuantum | seed付きevent列と分布検定 |
| F-L5-029 | `probability.wavefunction-grid` | ProbabilityQuantum | norm、phase、解析解に対する誤差 |
| F-L5-030 | `probability.monte-carlo-quantum` | ProbabilityQuantum | provenance、再現性、信頼区間 |
| F-L5-031 | `coupling.multidomain` | Coupling | 翌tick bridgeのownership/order/unit/保存則 |

科学計算Stageは再現可能なreference実装と明記した誤差予算を検証する。研究成果、認証、現実世界での安全性を主張しない。

## 基盤Stageと512型上限

- `foundation.extended-ecs-mask` / F-L3-020は`Executable`である。1–64 Component型はSource Generator内のlow `ulong`だけで処理し、65–128はlow/high二語、それ以上は64型ごとに1語を足す(ADR-0079で上限を128から512へ)。生成Runtimeへmask照合は残らない。
- Stageは64、65、128、129、200、512型の生成と高いindexのComponentのqueryの実行、513型のSYSGEN026を検証する。
- D3D12 compute/readback と deterministic job runtime は独立 executable Stage で検証する。L4 adapter、field/graph artifact、pose bridge、volume renderer、thermal field は予約済み NotImplemented Stage を維持する。

## 外部ライブラリ境界

- OS、native API、driver bindingはL0に置き、Win32/D3D12と同じ抽象度にする。
- FFT、sparse solver、geometry、compute等の任意providerはL4 adapterの背後に置く。
- L5 solverはengine-ownedのL4 contractだけを見る。外部型をpublic API、Component、Command/Event、artifactへ出さない。
- correctnessの必須経路はengine-owned CPU referenceとし、外部providerやGPUは追加backendである。

## Stage Pack分離

PackはFoundation、Rendering、Rigid、Deformable、Fluid、Destruction、Celestial、ProbabilityQuantum、Coupling、VSIXに分離する。分離理由はComponent mask上限ではなく、solver依存、native/GPU初期化、障害隔離、timeout、artifact量、再開単位である。Visual StageはC# `IScene`を正本とし、手書きMainが選択Sceneだけを固定tick実行する。`[StartupScene]`は使用しない。

10個の独立Pack実行プロジェクト、共有host、各PackのC# `IScene`、生成ECSの実体化・ownership・unload・state hash検証まで実装済みである。将来solver本体は、対応Featureが実装されるまで明示的な`NotImplemented` Stageとして維持する。


## M0060 executable shader stages

## Level 5 executable Content ownership stages

- `content.runtime-load` / F-L4-008 verifies typed shared retain/load/release, bounded dependency reads, caller-cancellation isolation, atomic last-known-good manifest updates, fault cleanup, and teardown races.
- `content.package` / F-L6-006 verifies deterministic exact root closure, deep non-recursive graph validation, hash/provenance binding, CAS-address deduplication, atomic clean publication, and rejection of missing, extra, corrupt, cyclic, or non-canonical package state.

rendering.baseline fixes the WARP visual baseline. shadergraph.schema validates canonical graph contracts, shadergraph.compiler compiles all fixed entries, rendering.shader-artifacts validates the container and artifact-only Runtime, and vsix.shader-graph validates the editor/package surface. All commands run sequentially through Feature Runner in the requested configuration.

## M0061 external Subgraph evidence

The existing `shadergraph.schema`, `shadergraph.compiler`, `rendering.shader-artifacts`, and `vsix.shader-graph` stages now include canonical external dependency metadata, exact Stage HLSL composition, dependency-hash artifact reuse, production Subgraph packaging, and template/navigation tests. No new Feature or Stage ID is introduced.

## M0062 structured Region evidence

The existing shadergraph.schema stage now fixes semantic If/ForRange ownership, Then/Else/Body membership, bounded loops, typed Return, LoopIndex scope, branch value isolation, canonical hashing, and source maps. vsix.shader-graph covers built-in palette creation, exact typed connections, structured Region creation, Undo, localization, and VSIX packaging. shadergraph.compiler retains the production nine-entry DXC oracle; no Feature or Stage ID is added.

## M0064 visible Showcase evidence

`platform.boundaries`, `platform.window-input`, and `input.actions` are executable Foundation stages. `showcase.visible-progress` is an executable Rendering/Visual stage that requires WARP, starts only its owned Showcase PID, verifies four pages plus Pause/Step/progress controls, and records evidence under `.artifacts/Showcase/`. `export-showcase` emits a canonical `csharp-game-engine-showcase-manifest-v1` with separate Feature implementation and Stage executable measurements.

## M0066 shared Preview Host evidence

`vsix.rendering-preview` / F-L8-018 is executable Visual/VSIX evidence. It runs the bound C# VSIX reference Scene, the versioned Preview protocol, common Render Server/session/process/pipe tests, Scene/Asset Preview client and fresh-HWND recovery tests, package-closure/legacy-removal tests, and the package-extracted apphost on WARP with the D3D12 debug layer. Since ADR-0070 it also runs the runtime view: the game bridge, the host's runtime session, the editor camera conversion, and the packaged host opening UserEcsProject's world and applying live scene JSON. Neither this Stage nor the later automated F-L8-023 Stage converts the Pending Experimental Instance `scene-designer` ledger entry into a human-observed pass.

## M0067 canonical numeric adversarial evidence

`world.logical-scalar` / F-L1-009 runs the independent fixed-width arithmetic/text/serialization oracle and the canonical numeric analyzer suite. Debug and Release executions include oversized decimal wraparound rejection, member-scoped adapter enforcement, inferred external-result rejection, full-width ties-to-even property coverage, and zero-allocation multiply/divide hot-path checks. Full solution builds remain the repository-wide proof that every product compilation runs NUM001 enforcement.

## M0068 typed Inspector and Component Palette evidence

`vsix.inspector-contracts` / F-L8-024 is executable Headless/VSIX evidence. Its single filtered VSIX command covers declared-type editor selection; enum and compatible immutable `[SceneAssets]` choices; canonical `float`/integer formatting; read-only `double`/`decimal`; nested vectors; text and boolean controls; component catalog, initializer, add, remove, reorder, and drop policy; source editing; and Roslyn reparse convergence. Unknown and unchanged choices return without a `SceneEditCommitRequest`, so they cannot mutate C# or add a native Undo unit.

## M0069 Transform visual conformance evidence

`vsix.transform-tools` / F-L8-023 is executable Visual/VSIX evidence with WARP required and hardware optional. It runs the bound C# VSIX reference Scene, Move/Rotate/Scale/Snap and surface-snap mathematics, the edit transaction and conflict/rebase state machines, one-history-step source writing and reparse verification, Preview Scene overlay transport, and the common Render Server session. The dedicated WARP oracle holds camera/object state constant, proves that overlay geometry alone changes captured pixels, then clears the overlay and requires the exact baseline frame hash to return. Canonical Transform source editing also proves that `ReferenceFrameId` remains unchanged.

## M0071 Bake, asset, and diagnostic UI evidence

`vsix.bake-integration` / F-L8-026 is executable Headless/VSIX evidence. Its net472 command verifies the immutable presentation state, Physics Bake request/progress/cancel/failure/partial-output orchestration, Asset Preview inline progress/diagnostics, artifact links, resident-client typed result preservation, architecture boundaries, and localization parity. Its net10 command verifies the shared authoring-host session, wire message, bounded framing, server cancellation, and terminal result contracts. A successful UI state requires a correlated 100% terminal progress item newer than the last observed progress, exit code zero, and no error diagnostic.

## M0084 product package and SBOM evidence

`release.packages` / F-L7-045 is executable Headless/Foundation evidence. It publishes one deterministic Windows x64 portable archive, independently reopens its exact manifest and canonical SPDX 2.3 dependency/file closure, verifies restored NuGet package bytes and external signing inputs, safely extracts the package to a unique directory, executes the packaged viewer for one hidden WARP readback frame with zero D3D12 validation errors, recomputes the installed byte closure, and uninstalls with zero residue. The Stage consumes the exact prebuilt localized VSIX without rebuilding or repacking it; F-L7-046 through F-L7-048 retain compatibility, RC, and immutable-manifest ownership. In Release that VSIX is the frozen input named by `Docs/Verification/VSIX-Release-Input.json`, whose bytes live in the local `.artifacts/VsixReleaseInputs` store: a fresh checkout fails the Stage, naming the missing package, until a Release VSIX validated in Visual Studio is frozen with `freeze-vsix-release-input`.

## M0085 product release compatibility evidence

`release.compatibility` / F-L7-046 is executable Headless/Foundation evidence. It validates one strict Windows 10 2004+ x64/.NET 10/net472/WARP scope over the exact CI, Platform, VSIX, optional-hardware, and executable contract-Stage references, then runs four focused fail-closed tests in a Stage-isolated build root. The aggregate deliberately stores no dynamic evidence status; detailed matrices, ledgers, and configuration-qualified Stage results remain the only owners of observations and verdicts.

## M0086 Level-gated Release Candidate evidence

`release.candidate` / F-L7-047 is executable Release-only Headless/Foundation evidence. `run-release-candidate` derives the exact product Feature and Stage closure from Catalog track, reached Level, lifecycle, and dependency edges, then executes every prerequisite under one dedicated Release/WARP/current-Git profile. The canonical RC report binds every complete Stage result by run-result file SHA-256 and artifact SHA-256. Specified, Deferred, F-L7-047, and F-L7-048 cannot satisfy the prerequisite closure.

## M0087 immutable formal release evidence

`release.manifest` / F-L7-048 is executable Release-only Headless/Foundation evidence. It binds a passing RC Stage/report, validated Rendering/Content/ECS/Platform/VSIX reports, and the exact package archive, signing inputs, inner package manifest, and SPDX hashes. Publication is an atomic SHA-256 content-addressed FormalRelease directory with non-expiring retention and rollback identity. Same-identity publication preserves the original bytes and timestamp; invalid existing or partial output is never overwritten into success.

### Virtual directional shadows (M0115)
`rendering.virtual-shadows` (F-L4-020, Rendering / Visual, WARP required) runs `VirtualDirectionalShadowTests` and `VirtualShadowGpuTests` plus the Rendering Stage Pack scene lifecycle. It checks page reuse/invalidation/exhaustion, scaled geometric normals, Standard/Radial shadows and comparison/recovery. Interactive sample instructions: `UserEcsProject/VirtualShadows.md`.

## Asset workspace contract stage

`vsix.asset-workspace` runs Content inventory/migration/file-operation tests and net472 query/catalog/reference/data-edit tests for F-L8-035. It is an executable contract oracle for a PartiallyImplemented feature. It does not turn missing M0114 services or unverified Experimental Instance operations into a pass. See M0123 for those remaining acceptance gates.

## Asset editor windows stage

`vsix.asset-editors` runs the net472 registry/placement-policy/camera/model/preview-level tests, the preview protocol validation tests (lighting and skeletal inputs) and the WARP preview-host renders (template lighting, a skinned glTF with a clip pose and bone lines) for F-L8-036. It is an executable contract oracle for a PartiallyImplemented feature. It does not turn real floating/docking gestures, the DPI and high-contrast matrix or live orbit input into a pass. See M0127 for the remaining acceptance gates.
