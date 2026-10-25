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
| S3 worker/orchestrator | PartiallyImplemented | `Cook.Cli describe` and one-shot `import-worker` exist, with deterministic discovery, processor order, path-confined read sets, diagnostics and atomic output. Compiler commands, registry/cache/index, payload codecs, metadata migration and full process lifetime/resource enforcement remain. |
| S4 runtime cutover | Planned | Built-in adapters, runtime/physics/navigation/distance-field/Unreal consumers and immediate old-reader removal remain. |
| S5 distribution | Planned | SDK/VSIX packaging, content targets and metadata writer unification remain. |

## Observed implementation

`SubAssetIdentity` hashes UTF-8 `cge.subasset/1\n<kind>\n<NFC name>`; identity is case-sensitive and independent of payload. Selectors preserve drive prefixes and escape reserved name characters. `ConventionConversion` validates signed axis bases, uses `Transpose(B)*M*B`, preserves scale signs and flips tangent handedness for mirrors.

CGEX v1 has a 44-byte little-endian header (magic, schema, length, SHA-256) and property-sorted canonical UTF-8 JSON. Array order is preserved; negative zero is normalized. Reads reject unknown members and require byte-identical re-encoding. The configurable default total bound is 256 MiB. This is an IR cache/worker format, not a runtime payload.

Normalization validates first, converts coordinates/units/UV origin once, assigns NFC names, keeps the first eight influence slots and renormalizes, checks joint closure/rest palettes, and merges compatible skins. Incompatible binds, nonidentity bind shape and unsupported avatar remapping fail closed. Cubic transform values retain the v1 linear approximation with IMP130. The collectible module context shares exactly Cooking, Contracts and Core; existing cooker tests pass.

## Verification

```text
dotnet test tests/CSharpGameEngine.Import.Tests -c Debug --filter FullyQualifiedName~ModelImportParityTests -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Contracts.Tests -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Core.Tests -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Cook.Tests -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test CSharpGameEngine.slnx -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
```

Targeted tests pass: Contracts 26, Core 156, Cook 26, and the focused old-reader parity test. Cook tests include real separate-worker byte equivalence, read sets (including absent dependencies), malformed input, unknown options, postprocessor corruption, cooperative cancellation, diagnostic caps, retained previous output, and source-write prevention.

The normal-permission solution run completed with **exit code 1**: 2,443 tests passed and two were skipped across the executed test assemblies, but VSIX tests did not compile. Five pre-existing xUnit2000 errors in the unowned dirty `tests/CSharpGameEngine.VSExtension.Tests/PrefractureDebugServiceTests.cs` lines 90–94 require swapping Assert.Equal expected/actual arguments. Windows tests passed 870/870 executable cases (two optional radial evidence cases skipped). This run used the earlier 13-test Cook snapshot; the final expanded 26-test Cook suite was verified separately. The complete gate is not green and is not presented as final-code solution verification. Log: `.artifacts/M0114-S2-solution-test.log`.

The restricted run additionally had atomic replacement/owner-process failures. Outside the sandbox, all four WorldGrid and two affected bridge tests pass, proving environment dependence for those cases. NuGet audit connectivity was resolved with normal restore, without disabling audit. The five unowned assertion lines have not been changed; a user decision about modifying them is pending.

The first oracle included 156 bin/obj copies. Before any source-reader change, enumeration was corrected; the 97-source oracle was recaptured and then compared successfully without the update flag.

## Conformance and remaining work

Overall: **Partial**. Existing source readers remain active; F-L0-005 is not Removed. New worker-based import is not available to games yet. Standard-frame parity, four-template WARP/hardware, FeatureRunner integration, SDK verification and S3–S5 remain required.

Incomplete S2 coverage includes full rig extensions, exhaustive limits/adversarial cases and parity against new adapters (which do not exist yet). Custom/morph-in-between values are now validated/transformed, and a provider-free type-graph test exists. CGEX currently uses canonical JSON under its LE header rather than binary scalar encoding; this must be reconciled with ADR-0080's LE encoding requirement before S2 is complete. These are outstanding work, not accepted scope reductions.

The current direct worker deliberately rejects tool/nested/archive services with IMP diagnostics. Job objects, network/process-resource policy, timeout/forced cancellation and importer-isolation scheduling still belong to unfinished S3; no complete sandbox claim is made. Production glTF/FBX modules are not yet connected.

Pre-existing destruction/physics/packaging/solution changes belong to another task and are excluded from M0114 commits. Measured physical memory usage was 55–63%; builds/tests use one MSBuild node.
