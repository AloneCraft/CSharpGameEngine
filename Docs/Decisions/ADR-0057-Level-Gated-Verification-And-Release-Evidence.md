# ADR-0057 — Level-Gated Verification and Release Evidence

- Status: Accepted
- Date: 2026-08-25

## Context

FeatureRunner already runs executable stages and explicitly reports deferred stages. The expanded roadmap needs machine-readable track/Level/dependency metadata without treating future work as passing or requiring all future work for every release.

## Decision

Use catalog schema v2 with `Specified`, `FeatureRoadmapTrack`, dependency Level, and dependency IDs. Keep run-result schema v1. Require each future Feature to have an oracle-bearing `NotImplemented` Stage and limit release gates to executable Features in the selected track/Level closure.

## Consequences

- `run` and `run-feature` return nonzero `NotImplemented` for future work.
- Normal changed/smoke/full/stress/vsix suite selection includes executable stages only.
- WARP/Windows Debug/Release cells are mandatory; hardware and Experimental Instance are supplemental.
- Retry, quarantine, performance, retention, package, SBOM, and immutable release-manifest rules become shared release contracts.

## Migration and verification

Catalog validation and the `verification.*`/`release.*` Stage matrix in [Testing-CI-Release](../Specifications/Testing-CI-Release.md) are the source of acceptance.

## Related features

F-L7-036 through F-L7-048, F-L7-021, F-L9-008.
