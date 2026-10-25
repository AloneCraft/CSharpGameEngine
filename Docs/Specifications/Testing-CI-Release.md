# Testing, CI, and Release specification

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Feature Stages](../Feature-Stages.md)
- Decisions: [ADR-0057](../Decisions/ADR-0057-Level-Gated-Verification-And-Release-Evidence.md)
- Target layer: L7, with L8 Experimental Instance evidence

## Catalog and Feature metadata

The Feature catalog is version 2. Each `FeatureDefinition` has a lifecycle status, `FeatureRoadmapTrack`, dependency Level, and explicit dependency Feature IDs. The run-result JSON remains version 1 because its observable result contract is unchanged.

Validation rejects duplicate IDs, unknown dependency IDs, self dependency, dependency cycle, and an implementation dependency on a higher delivery Level. It also requires an executable Stage for `Implemented`, `PartiallyImplemented`, or `Implementing` Features, and an oracle-bearing `NotImplemented` Stage for `Specified` or `Deferred` Features. A future stage has no command and therefore cannot enter a normal successful suite.

`verify-coverage` additionally checks the implementation ledger projection and every Visual Scene binding against the canonical Catalog. Managed Roadmap Features must declare the exact lifecycle Status, Stage ID, and dependency set recorded by the Catalog. The Feature Runner build invokes this verification, so metadata drift is a build failure rather than a release-time discovery.

## Release scope and Level gates

A release manifest selects target tracks and a reached delivery Level. Required verdicts are limited to executable Features within that scope and Level closure. `Specified`/`Deferred` Features remain visible as planned work and fail only when explicitly run, returning `NotImplemented`; they do not silently become skipped passes or block a release whose scope has not reached them.

```text
track + reached Level
  -> feature dependency closure
  -> required executable stages only
  -> evidence collection
  -> immutable release manifest
```

The integration order is Level 1 through Level 7 across tracks. The dependency graph is the machine-readable source for ordering; Roadmap tables are the human index.

## CI matrix and evidence separation

Required automated cells are Windows x64 Debug and Release, `net10.0`, Windows target frameworks, and `net472` VSIX. WARP is mandatory for visual certification. Hardware GPU/audio are supplemental. Visual Studio Experimental runs are selected only by the `vsix` profile and use separately recorded evidence; they are not injected into normal `changed` or `full` headless CI.

`Docs/Verification/CI-Matrix.json` is the required-cell source of truth. It owns eight `windows-2022` x64 cells: canonical solution, `net10.0-windows10.0.19041.0` tests, `net472` VSIX build, and WARP certification, each in Debug and Release. `.github/workflows/ci.yml` mirrors those exact IDs and fields, validates the matrix before executing a workload, and selects the repository SDK through `global.json`. `suite visual` selects every and only executable Visual Stage; every selected Stage declares WARP required and hardware optional. A required job may not use `continue-on-error`; hardware remains a separate supplemental pass and cannot substitute for WARP. External artifact publication and retention are not implied by this matrix and remain owned by the later package/release artifact contract.

Tests are classified as unit, integration, system/visual, artifact, performance, or release. Debug and Release results are separate cells. A clean build validates restore/build/test/artifact results without stale outputs or undeclared local state.

`Docs/Verification/Manual-Evidence.json` is the versioned manual evidence ledger. Normal verification permits an explicit `Pending` state with owner and reason. `Passed` requires an existing evidence path and a future expiry. Release verification uses `verify-manual-ledger --release` and rejects every non-passing or expired entry; missing evidence is never converted to success.

F-L7-046 aggregates compatibility membership without creating another evidence truth. `Docs/Verification/Release-Compatibility-Matrix.json` fixes one Windows 10 2004+ x64 product scope and references the exact eight required CI cells, six required Platform cells, eight required VSIX release checks, two supplemental hardware cells, and four executable contract Stages. CI, Platform, VSIX, and manual ledgers continue to own their detailed declarations and dynamic observations. The aggregate contains no status, evidence path, observation time, or expiry; missing, duplicate, extra, promoted-hardware, source-drift, target, or non-executable-Stage contradictions fail closed. F-L7-047 evaluates actual Stage evidence against this scope.

## Retry, quarantine, and failure policy

Only `InfrastructureError` may retry once. A Stage command may declare infrastructure exit codes; `CSharpGameEngine.Vsix.Automation` reserves 2 for missing/unlocked desktop, Visual Studio discovery/install/provenance, or UI Automation infrastructure, while 1 remains an Oracle failure. A retry is recorded as a successful policy assertion and the terminal attempt remains explicit; a second infrastructure failure stays `InfrastructureError`. Assertion/oracle failure, cancellation, `NotImplemented`, and timeout never retry into success. `Docs/Verification/Quarantines.json` is the only quarantine source. Every entry is scoped by Stage, Debug/Release/Any, WARP/Hardware/Any, and failure signature; it requires an owner, HTTP(S) issue or existing repository evidence, and reason. `expiresUtc` must be after `createdUtc`, no more than fourteen days later, and still in the future. Unknown or `NotImplemented` Stages, duplicate scope, expired entries, and missing ownership/evidence are build failures. Quarantine is metadata only: it never rewrites a Stage result to `Passed`.

## Golden, performance, and stress policy

Golden artifacts must have source/provenance, deterministic regeneration, reviewable diff, and an explicit tolerance contract. `Docs/Verification/Golden-Artifacts.json` is the only golden registry. A file whose name contains `.golden.` or whose path contains a `Golden` or `Goldens` segment is rejected unless registered. Each entry pins the payload, every source, the generated diff, and a `csharp-game-engine-golden-review-v1` record by SHA-256; declares an explicit generator working directory, arguments, environment, and `deterministic=true`; and declares a diff format plus current and maximum reviewed tolerance. The review record cross-pins artifact hash, diff hash, tolerance ceiling, reviewer role, and change reference, so artifact or tolerance edits cannot reuse stale approval. Repository-relative paths may not escape through `..`, symbolic links, or junctions. `ExactBytes` permits only zero tolerance. The validator streams payload hashing, compares decimal tolerance strings without binary floating-point state, and rejects a current tolerance above the reviewed ceiling. M0064 and VSIX observational baseline ledgers are evidence records, not generated golden payloads, and are therefore outside this filename convention. Performance runs execute five warm-ups and thirty samples, saving median, p95, MAD, configuration, workload, and machine fingerprint.

Stress execution is bounded by 28,800 seconds and emits `csharp-game-engine-stress-v1`. Its plan fixes a nonzero seed, positive tick count, Debug/Release configuration, adapter, explicit workload and fault-injection metadata, and an exact reproduction command. Each completed tick contributes its canonical state and event SHA-256 to separate rolling aggregates. The first callback exception, cancellation, timeout, contract violation, or declared workload failure terminates execution and records its tick, signature, and detail; later symptoms cannot replace the first failure. The atomic report binds that result to Git commit/dirty state and the privacy-preserving performance machine fingerprint. Reading the report revalidates payload hash, fingerprint, plan bounds, aggregate hashes, pass verdict, and first-failure boundary. The included deterministic probe is intentionally short; long-running leak, soak, recovery, and fault scenarios use the same envelope rather than a separate evidence format.

## Artifact retention and package gate

Performance evidence uses `csharp-game-engine-performance-v1`. Every workload has explicit string metadata and configuration, exactly five unmeasured warm-ups, and thirty measured samples. Elapsed time is stored as integer nanoseconds and process-wide allocation as integer bytes, so worker-thread allocation is not hidden; the shared calculation fixes median to the integer midpoint, p95 to nearest rank, and MAD to the integer median absolute deviation. Absolute budgets and baseline deltas are separate verdicts; a baseline without a p95 compares the median only. Baseline regression is compared in integer basis points with `Int128` intermediates, so the verification layer introduces neither `double` state nor overflow. A missing, duplicate, non-positive-time, or negative-allocation sample is `Unavailable` and cannot pass. The report pins Git provenance and a privacy-preserving hash of OS/runtime/architecture/CPU/memory/processor-count/Stopwatch/GC characteristics; it does not persist the machine name. The serialized payload is SHA-256 bound, statistics and verdicts are recomputed from samples when read, fingerprint integrity is rechecked, and evidence is saved atomically under `.artifacts/Performance`.

F-L7-029 is the content-scoped release boundary. Its canonical report revalidates one runtime-loadable shipping package, the owned content schema sources, a bounded project/toolchain dependency set used as later SBOM input, and configuration-matched F-L7-028 reports. It is not the formal product release manifest and does not generate SPDX, signing inputs, or install/run/uninstall evidence; those remain exclusively owned by F-L7-045 and F-L7-048.

PR artifacts retain for 14 days, nightly for 30 days, RC for 90 days. Formal release manifests and package payloads are immutable. The package gate validates dependency closure, SPDX JSON SBOM, signing inputs, install, run, and uninstall from the actual package.

F-L7-045 publishes one deterministic Windows x64 portable archive. Its deployable closure is the framework-dependent .NET 10 viewer CLI, the sole packable CodeAnalysis analyzer NuGet, the exact already-built VSIX with en-US/ja-JP templates and Preview Host, and the Assimp native payload. PDB files and development paths are excluded. The outer archive has canonical ordinal entry order, fixed ZIP timestamps, no directory/escaping/duplicate entries, an exact SHA-256 file manifest, and a canonical SPDX 2.3 file/dependency document. Restored NuGet package bytes, rather than package names alone, supply dependency hashes. External signing inputs bind the archive, manifest, and SBOM without pretending that a signature has already been applied.

Installation for this portable format means safe extraction into one unique isolated directory. The gate runs the extracted CLI for one hidden WARP frame with readback verification and zero D3D12 validation errors, recomputes the installed byte closure from the archive, deletes the isolated directory in success and failure paths, and requires zero residue. The package gate never rebuilds or repacks the VSIX; F-L8-029 is therefore able to bind its UI and lifecycle observations to the same VSIX bytes later. The formal product release manifest and retention/rollback identity remain exclusively owned by F-L7-048.

## Timeout classes

| Class | Default |
|---|---:|
| Contract | 900s |
| Visual / integration | 1800s |
| Experimental VS | 3600s |
| Performance / release | 7200s |
| Stress | 28800s |

## Stage oracle matrix

| Feature | Level | Stage | Timeout | Oracle |
|---|---:|---|---:|---|
| F-L7-036 | 1 | `verification.coverage` | 900s | Ledger/Catalog/Stage/Visual binding agreement. |
| F-L7-037 | 1 | `verification.manual-ledger` | 900s | Owned, expiring manual evidence only. |
| F-L7-038 | 2 | `verification.changed-selection` | 900s | Executable affected stages only. |
| F-L7-039 | 2 | `verification.clean-build` | 900s | Hermetic Debug/Release clean build. |
| F-L7-040 | 3 | `verification.golden-policy` | 900s | Provenance and reviewed regeneration. |
| F-L7-041 | 4 | `verification.performance` | 7200s | 5 warm-ups + 30 samples + fingerprint. |
| F-L7-042 | 4 | `verification.ci-matrix` | 7200s | Explicit required Windows/TFM/WARP cells. |
| F-L7-043 | 5 | `verification.failure-policy` | 900s | One infrastructure-only retry; expiring quarantine. |
| F-L7-044 | 5 | `verification.stress-contract` | 28800s | Reproducible stress envelope. |
| F-L7-045 | 6 | `release.packages` | 7200s | Closure, SPDX SBOM, sign/install/run/uninstall. |
| F-L7-046 | 6 | `release.compatibility` | 7200s | Scoped compatibility matrix. |
| F-L7-047 | 7 | `release.candidate` | 7200s | Level-gated executable verdict. |
| F-L7-048 | 7 | `release.manifest` | 7200s | Immutable formal manifest. |

F-L7-036 through F-L7-048 are `Implemented` and executable. F-L7-047 derives the exact executable product-track closure from the Catalog, accepts only recent Release/WARP/current-Git prerequisite results, and publishes one canonical SHA-bound RC report. F-L7-048 consumes a passing RC Stage/report, every track release report, and the exact archive/signing/package-manifest/SPDX closure, then atomically publishes one content-addressed FormalRelease directory. Repeating the same identity preserves the original bytes and timestamp; an invalid existing identity is never overwritten.

## Template runtime diagnostics (M0119)
`verify-thirdperson-character` (ThirdPerson, FirstPerson, TopDown and Vehicle) explicitly sets `CGE_D3D12_DEBUG_LAYER=1` in each game subprocess. This overrides a caller's disabled diagnostic setting for verification. It leaves normal game startup defaults unchanged. A report with zero errors from an ordinary run without the debug layer is movement/rendering evidence, not debug-layer validation. The additive `d3d12DebugLayerEnabled` field in the runtime report records the actual active state. Each template evaluator rejects a false or missing field, including the native fallback where Graphics Tools is unavailable. Record diagnostic settings with the run provenance.
