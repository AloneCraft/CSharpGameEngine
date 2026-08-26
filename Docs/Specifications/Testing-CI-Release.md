# Testing, CI, and Release specification

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Feature Stages](../Feature-Stages.md)
- Decisions: [ADR-0057](../Decisions/ADR-0057-Level-Gated-Verification-And-Release-Evidence.md)
- Target layer: L7, with L8 Experimental Instance evidence

## Catalog and Feature metadata

The Feature catalog is version 2. Each `FeatureDefinition` has a lifecycle status, `FeatureRoadmapTrack`, dependency Level, and explicit dependency Feature IDs. The run-result JSON remains version 1 because its observable result contract is unchanged.

Validation rejects duplicate IDs, unknown dependency IDs, self dependency, dependency cycle, and an implementation dependency on a higher delivery Level. It also requires an executable Stage for `Implemented`, `PartiallyImplemented`, or `Implementing` Features, and an oracle-bearing `NotImplemented` Stage for `Specified` or `Deferred` Features. A future stage has no command and therefore cannot enter a normal successful suite.

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

Required automated cells are Windows x64 Debug and Release, `net10.0`, Windows target frameworks, and `net472` VSIX. WARP is mandatory for visual certification. Hardware GPU/audio and Visual Studio Experimental Instance runs are supplemental passes with separately recorded evidence; their absence does not convert a failed required headless/WARP cell into success.

Tests are classified as unit, integration, system/visual, artifact, performance, or release. Debug and Release results are separate cells. A clean build validates restore/build/test/artifact results without stale outputs or undeclared local state.

## Retry, quarantine, and failure policy

Only `InfrastructureError` may retry once. Assertion failures, normal process failures, `NotImplemented`, and timeout never retry into success. Quarantine requires an owner, linked issue/evidence, reason, and expiration no later than fourteen days. Expired quarantine is a normal required failure.

## Golden, performance, and stress policy

Golden artifacts must have source/provenance, deterministic regeneration, reviewable diff, and an explicit tolerance contract. Performance runs execute five warm-ups and thirty samples, saving median, p95, MAD, configuration, workload, and machine fingerprint. Stress uses a 28,800-second default timeout and stores a deterministic reproduction envelope.

## Artifact retention and package gate

PR artifacts retain for 14 days, nightly for 30 days, RC for 90 days. Formal release manifests and package payloads are immutable. The package gate validates dependency closure, SPDX JSON SBOM, signing inputs, install, run, and uninstall from the actual package.

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

All rows are `Specified` and currently registered as `NotImplemented`; the existing FeatureRunner contract and ten Pack executables remain executable baseline evidence.
