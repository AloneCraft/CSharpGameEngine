# Asset and Content Pipeline specification

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0053](../Decisions/ADR-0053-Asset-Identity-And-Content-Artifacts.md)
- Target layers: L2, L4, L6, L7, L8

## Scope and observed baseline

This document defines the target engine-owned content path. Existing glTF/FBX/Unreal import work and current asset metadata are implementation evidence for the baseline only. They do not satisfy the target identity, CAS artifact, incremental publishing, runtime handle, or shipping-manifest contracts until the listed Features are implemented.

Source files remain authoritative. Artifacts and caches are reproducible derived data. Provider SDK objects never escape L7 import adapters or L4 optional runtime adapters.

## Identity and canonical metadata

```csharp
public readonly record struct AssetId(StableId Value);
public readonly record struct SubAssetId(Hash256 Value);
public readonly record struct AssetKey(AssetId AssetId,SubAssetId? SubAssetId);
public sealed class AssetHandle<T>;
```

The normal reference syntax is `asset://<asset-id>/<subasset-id>`. `AssetId` wraps the existing stable identity. `SubAssetId` is derived from SHA-256 over the canonical sub-asset identity and content boundary; it is not a provider GUID or an unstable array index.

`<asset>.meta` is UTF-8 canonical JSON: normalized field order, normalized line endings, no duplicate keys, and deterministic formatting. A one-shot migration preserves existing GUID values by converting them into the canonical `AssetId` representation, writes the new metadata, and deletes the old reader in the same feature change. No compatibility reader remains.

## Import boundary and artifact envelope

L7 importers such as SharpGLTF or Assimp convert input into engine-owned intermediate records. External model, node, image, material, or scene types cannot appear in artifacts or public APIs.

Each L6 artifact is a content-addressed object with a little-endian fixed header:

| Field | Requirement |
|---|---|
| magic and schema version | Reject unknown schema before allocation. |
| kind and byte length | Bound all reads and prevent type confusion. |
| provenance | Tool version, source URI/hash, importer identity, and options hash. |
| dependency list | Canonical ordered `AssetKey` values and hashes. |
| content hash | Hash of canonical payload. |
| checksum | Reject corruption independently of content identity. |

Artifacts are published into CAS only after a complete temporary write, checksum verification, and atomic rename. A failed or cancelled compile retains the last-known-good published artifact.

## Data flow and state

```text
source + canonical .meta
  -> AssetId / SubAssetId
  -> L7 provider adapter
  -> engine-owned intermediate representation
  -> dependency DAG and canonical L6 artifact
  -> atomic CAS publication
  -> AssetHandle<T> retain/load/release
  -> shipping manifest closure
```

The incremental compiler maintains a dependency DAG. Dirty detection follows source, import options, dependencies, and tool provenance. Publication is atomic; restart recovery removes incomplete temporary work, restores last-known-good artifact pointers, and performs reachability GC only from declared roots/manifests.

`AssetHandle<T>` is an asynchronous ownership contract. Retain starts or joins load work; completion exposes an immutable artifact-backed value; release decrements ownership and permits eviction after in-flight users drain. Cancellation detaches a requester but never deletes an artifact used by another requester. Runtime codecs are avoided by converting source to stream artifacts in L7; an optional codec/provider sits behind an L4 adapter.

## Failures and diagnostics

- URI parse, unknown identity, duplicate canonical metadata key, and stale schema failures identify the source path and canonical field.
- Import errors identify provider, source range when available, options hash, and engine-owned conversion phase.
- Artifact failures identify header/schema/provenance/dependency/content/checksum field; no damaged artifact is published.
- DAG cycles and unreachable cache roots are reported before GC. GC emits a root set and deletion candidates; it never treats a partially written artifact as reachable.
- Handle diagnostics include `AssetKey`, retain count, state, dependency chain, cancellation source, and last-known-good fallback status.

## Immediate migration and compatibility

Every feature replaces the previous metadata format, artifact format, or loader API in the same change that enables its writer. Tests regenerate fixtures. Old readers and compatibility bridges are removed. A file conversion is allowed only as a one-time source migration that produces canonical JSON and then closes the old format.

## Performance and shipping rules

Content performance records five warm-ups plus thirty cold/incremental samples, median/p95/MAD, cache state, dependency count, compiler version, and machine fingerprint. A shipping manifest contains only the reachable closure required by the selected product scope; it includes artifact hashes and provenance and is validated during package install/run/uninstall.

## Stage oracle matrix

| Feature | Level | Stage | Mode | Timeout | Oracle |
|---|---:|---|---|---:|---|
| F-L2-004 | 1 | `content.identity` | Headless / Foundation | 900s | Canonical URI and metadata round-trip. |
| F-L7-025 | 2 | `content.importer-conformance` | Headless / Foundation | 900s | Import fixture maps to engine-owned IR. |
| F-L6-005 | 3 | `content.artifact-schema` | Headless / Foundation | 900s | Envelope round-trip and corruption rejection. |
| F-L7-026 | 4 | `content.incremental-cook` | Headless / Foundation | 900s | DAG-only rebuild and atomic publish. |
| F-L7-027 | 4 | `content.cache-recovery` | Headless / Foundation | 900s | Restart, last-known-good, and reachability GC. |
| F-L4-008 | 5 | `content.runtime-load` | Headless / Foundation | 900s | Async retain/release ownership. |
| F-L6-006 | 5 | `content.package` | Headless / Foundation | 900s | Manifest is exactly dependency closure. |
| F-L8-019 | 6 | `vsix.asset-authoring` | Visual / VSIX | 1800s | Typed authoring leaves invalid source unchanged. |
| F-L7-028 | 6 | `content.performance` | Headless / Foundation | 7200s | Thirty-sample performance evidence. |
| F-L7-029 | 7 | `content.release` | Headless / Foundation | 7200s | Closure, schema, SBOM input, package evidence. |

All rows are `Specified` and presently return `NotImplemented` when explicitly run.
