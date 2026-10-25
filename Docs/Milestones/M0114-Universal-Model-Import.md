# M0114 — Universal model import on the content pipeline

- Date: 2026-10-25
- Status: Implementing
- Decision: [ADR-0080](../Decisions/ADR-0080-Universal-Model-Import.md) (Proposed)
- Scope: S0–S5 only; native shims and M0115–M0122 are excluded.

## Goal and stage evidence

Move source decoding into Cook workers and load artifacts at runtime without changing template geometry, materials, animation or the standard frame.

| Stage | State | Evidence and remaining gate |
|---|---|---|
| Design preservation | Implemented | The complete supplied design is committed as ADR-0080. Handoff decisions select local Windows execution, v1 preserve-facing and Assimp until M0115. |
| S0 oracle | Implemented | `ModelImportParityTests` enumerates 97 authoritative template GLB sources (excluding bin/obj), hashes vertex streams/indices/influences/textures, records material/node/rest-palette values, and samples all 60 source clips at 0/0.25/0.5 seconds against their skin or Quinn. The committed oracle is `assets/TestFixtures/Import/M0114-Source-Parity.json`; update requires `CGE_UPDATE_GOLDEN=1`. |
| S1 Contracts/Core | Implemented | NFC subasset identities, escaped selectors, 55 humanoid roles/15 required roles, importer catalog contract and signed basis conversion. Contracts 26/26 and Core 156/156 tests passed. |
| S2 Cooking | PartiallyImplemented | Interchange records, CGEX codec, validation/normalization, importer/processor contracts and shared Cooking/Contracts/Core module context. Remaining schema/encoding/limit coverage and the solution gate remain. |
| S3 worker/orchestrator | PartiallyImplemented | Worker discovery/import, compiler registry and dependency cache, `import-ir`/`inspect`/`explain`, cooperative cancellation with forced process-tree termination, and standalone runtime payload codecs exist. Compile/meta/serve, index/DAG integration, metadata migration, payload producers/consumers and full resource enforcement remain. |
| S4 runtime cutover | Planned | Built-in adapters, runtime/physics/navigation/distance-field/Unreal consumers and immediate old-reader removal remain. |
| S5 distribution | Planned | SDK/VSIX packaging, content targets and metadata writer unification remain. |

## Observed implementation

`SubAssetIdentity` hashes UTF-8 `cge.subasset/1\n<kind>\n<NFC name>`; identity is case-sensitive and independent of payload. Selectors preserve drive prefixes and escape reserved name characters. `ConventionConversion` validates signed axis bases, uses `Transpose(B)*M*B`, preserves scale signs and flips tangent handedness for mirrors.

CGEX wire v2 has a 44-byte little-endian header (magic, wire version, length, SHA-256) and binary scalar payload for document schema 1. Strings use length-prefixed UTF-8, arrays retain order, dictionaries use ordinal order, and negative zero is normalized. Reads reject wire v1, malformed collection lengths and noncanonical bytes. The configurable default total bound is 256 MiB. JSON is used only for in-memory projection, not the wire payload. This is an IR cache/worker format, not a runtime payload.

Normalization validates first, converts coordinates/units/UV origin once, assigns NFC names, keeps the first eight influence slots and renormalizes, checks joint closure/rest palettes, and merges compatible skins. Incompatible binds, nonidentity bind shape and unsupported avatar remapping fail closed. Cubic transform values retain the v1 linear approximation with IMP130. The collectible module context shares exactly Cooking, Contracts and Core; existing cooker tests pass.

## Verification

```text
dotnet test tests/CSharpGameEngine.Import.Tests -c Debug --filter FullyQualifiedName~ModelImportParityTests -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Contracts.Tests -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Core.Tests -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Cook.Tests -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test CSharpGameEngine.slnx -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
```

Targeted tests pass: Contracts 26, Core 156, Cook 29, and the focused old-reader parity test. Cook tests include real separate-worker byte equivalence, read sets (including absent dependencies), malformed input, unknown options, frozen defaults, postprocessor corruption, cooperative cancellation, diagnostic caps, retained previous output, source-write prevention and binary scalar encoding. Latest Cook log: `.artifacts/M0114-cook-latest.log`.

ContentCompiler 16/16 and Content 43/43 pass after the final dependency-hash/options changes. Coverage includes cache reuse without a worker, dependency invalidation, corruption/nondeterminism rejection, cancellation/forced termination, metadata pinning, real CLI subprocesses and runtime codec/reference round-trips.

The normal-permission foundations solution run completed with **exit code 0: 3,684 passed, two skipped**. VSIX passed 1,209 cases; Windows passed 870 executable cases (two optional radial evidence cases skipped). Log: `.artifacts/M0114-foundations-solution-test.log`. This snapshot predates the final options/hash/validation adjustments, which require focused verification and a later final solution gate after the remaining migration.

The earlier solution run stopped on five xUnit2000 assertions in the unowned dirty `PrefractureDebugServiceTests.cs`. The user explicitly authorized swapping only those five expected/actual pairs; they are fixed, and the related 13 tests and later full VSIX suite pass. The five-line fix remains within the other task's uncommitted file and is excluded from M0114 commits. Restricted execution also caused atomic replacement/owner-process failures; affected tests pass outside the sandbox. NuGet audit connectivity was resolved with normal restore, without disabling audit.

The first oracle included 156 bin/obj copies. Before any source-reader change, enumeration was corrected; the 97-source oracle was recaptured and then compared successfully without the update flag.

## Conformance and remaining work

Overall: **Partial**. Existing source readers remain active; F-L0-005 is not Removed. New worker-based import is not available to games yet. Standard-frame parity, four-template WARP/hardware, FeatureRunner integration, SDK verification and S3–S5 remain required.

Incomplete S2 coverage includes full rig extensions, exhaustive limits/adversarial cases and parity against new adapters (which do not exist yet). Custom/morph-in-between values are validated/transformed, and a provider-free type-graph test exists. The earlier JSON wire encoding gap is resolved by CGEX wire v2. Outstanding work is not an accepted scope reduction.

The direct worker rejects tool/nested/archive services with IMP diagnostics. The parent enforces a deadline and sends a cooperative cancellation request before forced process-tree termination. Job objects, network/process-resource policy and importer-isolation scheduling remain unfinished; no complete sandbox claim is made. Production glTF/FBX modules are not yet connected. Runtime POD codecs for kinds 8–12 are additive prototypes with float skin weights; no producer or runtime consumer is connected, and the planned packed weight representation remains unresolved.

Pre-existing destruction/physics/packaging/solution changes belong to another task and are excluded from M0114 commits. Measured physical memory usage was 46–63%; builds/tests use one MSBuild node. No Unity Editor was launched.
