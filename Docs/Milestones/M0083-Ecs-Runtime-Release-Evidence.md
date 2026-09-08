# M0083 - ECS Runtime Release Evidence

## Outcome

F-L7-033 completes the ECS / Runtime Roadmap with one canonical self-validating report per configuration. The `ecs.release` Stage reruns deterministic replay, generated schedule diagnostics, schema-fingerprinted snapshots, and the four-boundary ECS performance Stage under four dedicated profiles, then binds their complete results, four raw captures, four common performance reports, twenty-two source/toolchain inputs, Windows x64 machine identity, and Git provenance.

This is an ECS-track input for later product-wide RC and immutable manifest aggregation. It does not own or produce a package, SBOM, signature, installer, compatibility matrix, or competing product manifest.

## Bound truths

The exact prerequisite profiles are:

- `foundation.deterministic-jobs` / `ecs-release-replay`
- `ecs.schedule-diagnostics` / `ecs-release-schedule`
- `ecs.snapshot` / `ecs-release-snapshot`
- `ecs.performance` / `ecs-release-performance`

Every result must be recent, configuration/Git/WARP matched, fully passing, assertion-complete, and artifact-hash valid. The performance result must publish exactly four raw and four common reports for 1, 64, 65, and 128 Component types.

The release producer independently rehashes and inspects persisted generated output while it is available immediately after the performance Stage. The completed release report does not depend on derived `obj` state: later validation reopens the immutable Stage/raw/performance files and the canonical generator source/toolchain input closure. A focused test deletes the generated `obj` tree before revalidating the report.

## Canonical report

`EcsReleaseEvidence` writes `csharp-game-engine-ecs-release-v1` as `.artifacts/EcsRelease/<unique>/ecs-release.json`. The payload SHA-256 covers every field except the report hash. Bytes are canonical UTF-8 with one trailing newline, publication is atomic, and the evidence directory must contain exactly one file.

The report binds:

- four Stage result file hashes and result artifact hashes;
- four raw report file hashes, source/state hashes, query, entity/worker count, and one-word/two-word shape;
- four common performance report file/payload hashes, raw/source/state linkage, statistics, and verdicts;
- twenty-two exact ECS, generator, benchmark, performance, release, Stage, specification, and toolchain inputs;
- Windows 10 2004+ x64 target, privacy-preserving machine fingerprint, commit, and dirty state.

## Failure contract

Generation and validation fail for missing, stale, ordinary-profile, wrong configuration/Git/adapter, incomplete, failed, modified, escaped, or reparse Stage evidence; missing/duplicate/extra raw or performance boundaries; raw/performance/source/state disagreement; invalid performance integrity, machine, timing, baseline, or budget; changed input; report hash or canonical-byte disagreement; extra evidence files; or non-x64 capture. Failed generation removes its partial evidence directory.

## Adversarial review

The first passing implementation re-opened `obj/.../generated` during every later report validation. That made a correct release report invalid after a normal clean. The final design performs independent generated-output verification at report creation, then makes the immutable report self-validating from retained Stage/raw/performance evidence and the generator input closure. This preserves the independent source-shape check without promoting `obj` to long-lived truth or copying a second generated-source archive.

A real Debug closure then exposed a second contradiction: documentation said Debug timing was diagnostic, but the auxiliary 64-to-1 p95 comparison still gated Debug. A noisy Debug run measured 5.90 ms p95 for one type and 7.62 ms for 64 and failed only that relative gate. The final contract disables all elapsed-time baselines in Debug while retaining exact workloads, source/state integrity, and zero allocation. Release alone owns the 6 ms median, 8.33 ms p95, and 20-percent 64-to-1 comparison.

Overengineering review retained one ECS-specific report rather than creating a generic release framework or another product manifest.

## Verification

- Focused performance plus release contracts: 8/8 Debug.
- Final-specification Debug `ecs.release`: `20260901T155824288Z-ecs-release-closure-final-docs-9decb27cb9594bab977e17954337c348`; Stage artifact SHA-256 `44b3d82551520a4dfc8fd148fb16b05d0fb641127e9bfccb4c11942ba4fdd8aa`.
- Final-specification Release `ecs.release`: `20260901T160015524Z-ecs-release-closure-final-docs-79075f8a369741f0b9895e55380cadfb`; Stage artifact SHA-256 `a05015c1adf277cad4c9c01e0638980400d941e84be1b5f1fb5ab0060a4632d6`.
- Debug release report SHA-256: `6c5e522848b335b271653ddd3550a6de097ddb13268f993608499e257f2b0ead`.
- Release release report SHA-256: `adae41f5bded563b12f6ac447b48f12237f80a5a8ffe2ae155fe2bec490bb02c`.
- Both reports bind the same machine fingerprint `3085d0ff0b36619f65edccbfe5a39f0dfe5b6a14ccc264a0faa129fd06502e57`.

The final Release closure measured:

| Component types | Median | p95 | Allocation p95 |
|---:|---:|---:|---:|
| 1 | 484,600 ns | 645,700 ns | 0 |
| 64 | 526,750 ns | 695,600 ns | 0 |
| 65 | 638,900 ns | 929,600 ns | 0 |
| 128 | 563,850 ns | 646,500 ns | 0 |

All ECS / Runtime Roadmap Features are now Implemented. Product-wide packaging, compatibility, RC, and immutable manifest ownership remains with F-L7-045 through F-L7-048.
