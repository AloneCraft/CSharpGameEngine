# Asset and Content Pipeline specification

## Typed imported lighting
ImportedLight is a color-only abstract base with directional illuminance and point/spot luminous-intensity variants. Missing range remains null. ImportedSpotCone preserves source half radians, including glTF's legal zero inner angle; conversion to the stricter authored cone requires explicit resolution for that case. See [Typed Lighting](Typed-Lighting.md). The Unreal directional emitter preserves lux/color and emits a separate LightShadow.

- Status: Partially implemented
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

The incremental compiler maintains a dependency DAG. Dirty detection follows source, import options, dependencies, tool provenance, and full dependency-envelope CAS addresses. A request compiles only the reachable root closure in dependency waves and bounds concurrency explicitly. Each compiled envelope is validated against the node provenance and ordered dependency references before CAS publication. The last-known-good state document is replaced atomically only after the complete request succeeds.

Restart recovery removes only compiler/CAS temporary publications. Invalid graphs and corrupt compiler state stop before compiler callbacks execute. Failed or cancelled work may leave immutable orphan objects in CAS, but cannot advance the published state. Reachability collection first atomically narrows state to the declared root closure, then moves validated unreachable objects to an explicit quarantine directory. Quarantine is recoverable and is not an irreversible delete operation.

`AssetHandle<T>` is an asynchronous ownership contract implemented by `RuntimeAssetStore`. The active `ShippingContentManifest` resolves one exact reference and address per key; typed retains join one bounded shared load and retain the dependency-first artifact closure. Completion exposes an immutable artifact-backed value. Final release or host teardown evicts the typed value and its closure exactly once. Caller cancellation detaches only that requester, while store teardown owns the shared cancellation. A candidate manifest becomes active only after every envelope validates; failure or cancellation leaves the previous manifest as the single last-known-good truth. The commit itself is the last point that observes the caller's cancellation, so a cancel that arrives while the last artifact is read still keeps the previous manifest. Runtime codecs are avoided by converting source to stream artifacts in L7; an explicit optional `RuntimeAssetCodec<T>` is the only L4 adapter.

## Failures and diagnostics

- URI parse, unknown identity, duplicate canonical metadata key, and stale schema failures identify the source path and canonical field.
- Import errors identify provider, source range when available, options hash, and engine-owned conversion phase.
- Artifact failures identify header/schema/provenance/dependency/content/checksum field; no damaged artifact is published.
- DAG cycles, missing dependencies, corrupt compiler state, and unreachable cache roots are reported before mutation. GC emits the root set and quarantine candidates; it never treats a partially written artifact as reachable.
- Handle diagnostics include `AssetKey`, retain count, state, dependency chain, cancellation source, and last-known-good fallback status.

## Immediate migration and compatibility

Every feature replaces the previous metadata format, artifact format, or loader API in the same change that enables its writer. Tests regenerate fixtures. Old readers and compatibility bridges are removed. A file conversion is allowed only as a one-time source migration that produces canonical JSON and then closes the old format.

## Performance and shipping rules

Content performance records five warm-ups plus thirty cold/incremental samples, median/p95/MAD, cache state, dependency count, compiler version, and machine fingerprint. A canonical shipping manifest contains only the reference-exact closure reachable from the selected product roots. It records content hashes, envelope addresses, provenance, kind, length, and canonical dependencies. Package publication writes a clean sibling temporary directory, validates it, and atomically renames it; envelopes shared by different AssetKeys are stored once by CAS address. Opening a package rejects missing, extra, corrupt, cyclic, non-canonical, or reparse-point state before publishing a runtime reader.

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

F-L2-004, F-L7-025, F-L6-005, F-L7-026, F-L7-027, F-L4-008, F-L6-006, F-L8-019, F-L7-028, and F-L7-029 are `Implemented` with executable stages. `content.release` first produces configuration-qualified F-L7-028 reports, then publishes and reopens a canonical runtime-loadable content package and emits one canonical SHA-bound report over the exact package closure, owned schema sources, bounded project/toolchain SBOM inputs, and those performance reports. It rejects missing, extra, stale, relocated, reparse-point, non-canonical, or modified evidence. It does not duplicate the SPDX/sign/install responsibilities of F-L7-045 or the formal immutable product manifest owned by F-L7-048.

## 型付きアセット参照(`Assets.*`、ADR-0077)

ゲームプロジェクトの `Assets/` の下のファイルは、Source Generator が `internal static class Assets` として型付きの参照にする(フォルダごとに入れ子のクラス、ファイルごとに静的な読み取り専用フィールド)。

- 種類は拡張子で決まる: `.wav` → `SoundAsset`、`.glb`/`.gltf`/`.fbx` → `ModelAsset`、`.png`/`.jpg`/`.jpeg` → `TextureAsset`(`CSharpGameEngine.Authoring`。中身はプロジェクト相対のパスで、文字列への暗黙の変換を持つ)。`Assets/Editor` は対象外。
- 使うのは、ファイルの一覧(`Game.props` が AdditionalFiles に加える)だけで、中身は読まない。ファイルを足す・消すと、参照のコンパイルエラーとして現れる。
- シーン JSON が `"Assets/…"` と書いたパスに対応するファイルが無ければ SCENE208(エラー)。
- シーン JSON のメンバーは文字列のまま。`ModelAsset` は静的メッシュ・骨格・クリップを区別しない。
