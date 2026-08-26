# ADR-0053 — Asset Identity and Content Artifacts

- Status: Accepted
- Date: 2026-08-25

## Context

Import providers and legacy metadata cannot be allowed to define public identity, artifact schemas, or runtime ownership. Incremental compilation requires reproducible identity and atomic publication.

## Decision

Use `AssetId(StableId)`, SHA-256-derived `SubAssetId`, canonical `asset://` URIs, UTF-8 canonical JSON metadata, engine-owned importer IR, and little-endian content-addressed artifacts. Runtime loading is through asynchronous `AssetHandle<T>`; shipping uses reachable manifest closure.

## Consequences

- Existing GUID values are preserved only through a one-shot source migration.
- Old metadata/artifact readers are removed with their target feature, not maintained as bridges.
- Provider SDK types stay inside L7 importer or L4 optional adapter boundaries.
- Atomic CAS publication, last-known-good recovery, and reachability GC become required contracts.

## Migration and verification

Identity, artifact, DAG, recovery, handle, and manifest Oracles are specified by `content.*` Stages in [Asset-Content-Pipeline](../Specifications/Asset-Content-Pipeline.md).

## Related features

F-L2-004, F-L7-025 through F-L7-029, F-L6-005/006, F-L4-008, F-L8-019.
