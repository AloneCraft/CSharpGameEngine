# M0126 — Image and model interpretation

- Date: 2026-10-26
- Status: PartiallyImplemented
- Feature: F-L7-058, related F-L8-035
- Intended contract: ADR-0088 and Specifications/Image-And-Model-Interpretation.md.

## Implemented boundaries

L2 output settings/identity and distinct ImageAsset references; L6 encoded Image and processed Texture payloads/runtime codecs; L7 PNG/JPEG conversion, advisory Normal detection, per-profile CAS reuse and composite FBX; L8 Inspector/settings/output drag and semantic reference review; standalone authoring-host and CLI commands. Full-filename sidecars remain canonical schema 1 and source bytes are unchanged. Registration is explicit; builds/browsing only read metadata.

FBX preserves hierarchy, meshes, material roles, shared skeleton/skin and source Takes; additional frame ranges interpolate boundary samples. Generic is the supported default. Humanoid/retarget/root-motion/collider/light-camera generation and external extraction are deliberately not auto-generated. M0114 global artifact-only runtime cutover remains separate.

## Verification

Commands run through `.artifacts/Integration/run_guarded.py` with serial MSBuild, no node reuse/shared compilation and physical-memory monitoring. The common background CLI acquired resources but returned Unavailable before starting a child. The user explicitly authorized the existing noninteractive CLI fallback. No OS-input injection or user-Editor termination is used.

- `dotnet test tests/CSharpGameEngine.Import.Tests/CSharpGameEngine.Import.Tests.csproj -c Debug -p:Platform=x64 -m:1 -nr:false -p:UseSharedCompilation=false --no-restore`: 87 passed, 0 failed in the approved normal-user environment. Sandbox execution failed four atomic-replace operations (including two existing WorldGrid cases); the same command passed outside that restriction. Logs: `.artifacts/ImageImport/final-import-tests-user.log`.
- Content.Tests equivalent: 67 passed, 0 failed, including reviewed metadata edit/identity/Undo/Redo. Log: `.artifacts/ImageImport/final-content-tests.log`.
- AssetCatalog focused suite before final regression: 9 passed. Full CodeAnalysis in solution regression: 339 passed.
- Full Debug solution run without restore found NETSDK1047 for Vsix.Automation net472/win-x86. This is an incomplete restore, not a passing solution result. Final restored result will be recorded separately.

## Observed behavior and remaining evidence

Real PNG tests establish raw bytes, multiple output kinds, channel mapping, green inversion/normal mips, stable IDs, CAS reuse after display/order changes and last-success retention. Real Box.fbx imports and publishes Model/Mesh without inventing a rig/clip. Synthetic Assimp scenes verify shared skin, two Takes, animation-only and a frame-range clip. Existing file recovery and semantic refactoring contracts are reused.

Native linked multi-document/file Undo, external typed drag/drop, the full DPI/theme matrix and real skinned/multi-Take FBX fixtures remain runtime verification gates. Compilation and unit tests must not be described as evidence for these interactions.


## Final verification and reconciliation

- Full Debug solution snapshot: 22 test projects, 2691 passed, 0 failed tests, 2 explicit GPU measurements skipped. Windows.Tests contributed 900 passed and 2 skipped (16m59s). The overall command exited 1 solely because Vsix.Automation had not been restored for net472/win-x86; it is not reported as a successful full-solution command.
- `dotnet restore CSharpGameEngine.slnx -p:Platform=x64 -m:1 -nr:false -p:UseSharedCompilation=false` then succeeded. The restored solution built all projects and reran affected suites with the same Debug/serial options and a FullyQualifiedName filter: Import 87, Content 67, CodeAnalysis 339, Bridge.Hosting 36, Rendering 193 and Core/registration 52 passed. Its initial VSIX localization failures were fixed in source/resources, not suppressed.
- Final full VSIX command: `dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj -c Debug -p:Platform=AnyCPU -m:1 -nr:false -p:UseSharedCompilation=false --no-restore --logger trx --results-directory .artifacts/ImageImport/VerifiedTestResults`. **1234 passed, 0 failed**, exit 0; no compiler warning/error was introduced. `final-vsix-verified.log` and `.memory.json` record the final build/test result (peak physical memory 66%).
- Final Import rerun also verifies ImageData/TextureOutputData through RuntimeAssetStore, type rejection, last-attempt failure state and success clearing. Metadata edit tests prove immutable IDs and exact native-operation Undo/Redo. Image output semantic rewrite includes Default properties and leaves ordinary labels unchanged. Read-only/shared project ownership is rejected before refactoring.
- Packaged-host-equivalent CLI smoke: image and model `inspect / register / import`, six successful operations, source hashes unchanged, inspect creates no sidecar, Normal DX suffix/green inversion honored and registered image ID preserved on reimport. Log: `.artifacts/ImageImport/cli-smoke-verified.log`. Inspection IDs are nonpersistent candidates; the successful script correctly compares IDs after registration, not across independent unregistered inspections.
- `git diff --check` succeeded; Git only printed existing EOL normalization notices on two unrelated files. Before-snapshot ZIP retains 118 preexisting dirty/new files; preservation checks found no missing baseline files. No commit, push, reset or stash was performed.

Observed core behavior matches the interpretation contract: persisted settings win, outputs have stable per-kind/key identities, data textures remain linear, encoded Image is distinct, FBX owns composite payloads and the previous manifest survives import failure. Overall conformance remains **Partial** because full M0114 integration, advanced optional model processors, command-line code-reference migration and actual shell/real rigged-file acceptance are not completed by this milestone. Existing referenced images should use the Inspector's semantic registration review; register-tree does metadata/cooking only. No accepted scope reduction or runtime acceptance is inferred from these tests.
