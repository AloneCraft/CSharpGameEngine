# Editor and VSIX specification

## Typed light authoring
F-L8-024/025 now include [Typed Lighting](Typed-Lighting.md): symbol-checked physical-value controls, atomic range/cone/rectangle gizmos, light/Transform/shadow palette batches and 12 edit-time light conversions. The source and source/target default-contract fingerprint must still match at commit; dimension-changing conversions require explicit quantity input. One verified source write uses existing native Undo. No runtime type replacement or private history is introduced.

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0054](../Decisions/ADR-0054-Editor-Authoring-Host-And-Source-Synchronization.md), [ADR-0061](../Decisions/ADR-0061-Vsix-Experimental-Evidence-And-Automation.md)
- Related lower specification: [Scene-Editor-Design](Scene-Editor-Design.md)
- Target layer: L8 over standalone L4/L7 services

## Scope and observed baseline

The C# `IScene` source remains the sole authoring truth. The current Scene Designer implements a substantial baseline: live Roslyn scene projection, navigation/picking, Move/Rotate/Scale/Snap, predicted editing, source synchronization, native Undo/Redo integration, and typed inspector work. This specification does not downgrade that evidence; it defines the next acceptance contracts for WARP display, rebase, robust host lifecycle, typed asset workflows, and release verification.

Historical JSON Scene experiments are not a supported compatibility path. Any future persistence or protocol migration follows immediate cutover: write the new canonical form, regenerate derived data, and remove the old reader/writer in the same feature change.

## Authoring model

```text
C# IScene source
  -> Roslyn parse and SceneDocumentSnapshot
  -> predicted edit model while interaction is active
  -> exactly one native Visual Studio Undo transaction on commit
  -> minimal source edit
  -> Roslyn reparse
  -> converged immutable projection for hierarchy, inspector, and viewport
```

The predicted operation model is transient gesture state only. It is never an alternative source of truth and never avoids a reparse. Before the live-buffer write, the edited C# tree is compiled and reanalyzed; the canonical reanalysis model, not the separately mutated operation model, is published as the temporary landing projection. If parsing, conflict resolution, verification, or writeback fails, the source is unchanged and the prediction rolls back to the last reparse result with an actionable diagnostic.

Scene Designer is a Visual Studio multi-view document: document, hierarchy, inspector, palette, and viewport project the same snapshot/session identity. Visual hierarchies are projections rather than runtime ownership graphs.

## Host boundary and protocol

Preview, Import, and Bake use a single versioned out-of-process protocol. The VSIX owns UI, commands, and source edits; standalone engine/compiler services own render/import/bake execution. The protocol contains version, request ID, operation, source/artifact identity, cancellation capability, deadline, progress, result, and diagnostic envelope.

| State | Meaning | Legal exit |
|---|---|---|
| Idle | no request owns a host lease | request -> Starting |
| Starting | process and protocol handshake pending | ready -> Running; failure -> Failed |
| Running | request may emit progress and immutable snapshots | complete -> Completed; cancel -> Cancelling; deadline/crash -> Failed |
| Cancelling | cancellation was acknowledged or process is being terminated | cancelled -> Cancelled; crash -> Failed |
| Completed / Cancelled / Failed | terminal request evidence | acknowledgement -> Idle |

Protocol version mismatch fails before source mutation. Host crash and timeout are not auto-successfully retried. A fresh request may start after terminal cleanup.

## Editing contracts

- Rotate, Scale, snap, rebase, WARP display, and Undo/Redo are acceptance requirements layered on the existing transform baseline. Transform preview matrices are camera/frame-relative `float` UI projections only; the C# authoring source is unchanged until commit. A canonical Transform commit updates only local Logical position/rotation/scale members and must preserve its `ReferenceFrameId` member exactly.
- Inspector and palette use schema-aware editors for enums, vectors, asset references, add/remove, and reorder. The declared component-member type, not the right-hand expression type, selects the editor. Asset choices are limited to compatible immutable members of the referenced `[SceneAssets]` container. `double` and `decimal` remain read-only at the engine-owned authoring boundary; canonical real editing uses `float`. Invalid text, an undeclared/missing asset symbol, illegal topology, and unchanged choices create no source operation or native Undo entry. Physical asset discovery/import/existence belongs to F-L8-019 and is not duplicated in this Inspector contract.
- Source synchronization resolves the latest Roslyn context for every completed gesture, explicitly rebases supported stale deltas or rejects the operation, verifies one minimal C# edit by compile/reanalysis, and opens at most one Visual Studio native Undo transaction. Consecutive gestures may use a transient projection only as the stable-ID landing basis; C# and native text history remain the only authoring/Undo truths. Save persists the current live document and never performs deferred writeback.
- Bake/import UI reports the actual host result and artifact provenance. It may not enable a false “completed” state when a backend is unavailable.
- Asset Preview transports only bounded `AssetAuthoringRequest`/`AssetAuthoringResult` DTOs to the versioned net10 Content host. Only that host may validate or atomically publish canonical `<source>.meta`; the net472 VSIX may display identity/options/diagnostics and request save with the inspected SHA-256, but it may not parse metadata, change an existing `AssetId`, overwrite a concurrent edit, or create a second metadata truth.
- Render graph, profiler, and debug viewers request protocol-v5/debug-snapshot-v2 bounded read-only snapshots only on demand from the existing Preview Host session. Device generation, fence, frame/object/texture/error counters, bounded D3D12 diagnostics, process identity, bounded Input actions, and Audio lifecycle/mixer/recovery counters are observed copies. The isolated Preview Host reports `NotOwned` for runtime Input/Audio it does not own. The L4 graph is labeled `DeclaredPlan`, not executed-pass or timing evidence. A capture error leaves the session usable; malformed/correlated transport failure uses existing recovery. The viewers do not poll, attach a second renderer, acquire Input/Audio services, expose native handles, issue control commands, synthesize timings, or mutate runtime state.

## Failure diagnostics

Every visible failure includes a source/document identity, operation/request ID, protocol version, state transition, and recommended next action. Source edits include the originating snapshot version and conflict outcome. A failed validation, parse, host response, or artifact schema check rolls back predicted UI state only; it never rolls back unrelated text or creates an Undo entry that claims success.

## VSIX and runtime boundaries

Runtime L0-L6 cannot reference Roslyn or Visual Studio SDK. L7 compiler/bake work runs outside Visual Studio. L8 is a frontend over those services. The renderer used by Preview is the Render Server from [Rendering](Rendering.md); VSIX-specific rendering code is removed when F-L8-018 is implemented.

## Manual and automated evidence

Experimental Instance evidence remains explicit. `VSIX-Experimental-Baseline.json` permits Pending but validates owner, evidence existence, observation, and expiry before Passed can count. `CSharpGameEngine.Vsix.Automation` runs only against the exact `CgeAutomation` suffix, classifies exit code 2 as InfrastructureError, records package/build/installed DLL hashes, and never closes a pre-existing PID. The Experimental stages are `vsix` profile only and retain a 3600-second Feature Runner timeout.

## Stage oracle matrix

| Feature | Level | Stage | Mode | Timeout | Oracle |
|---|---:|---|---|---:|---|
| F-L8-018 | 5 | `vsix.rendering-preview` | Visual / VSIX | 1800s | Packaged WARP Preview Host, common Render Server presentation/recovery, and no VSIX renderer duplication. |
| F-L8-020 | 1 | `vsix.experimental-smoke` | Headless / VSIX | 3600s | Explicit pending/recorded Experimental Instance evidence. |
| F-L8-021 | 2 | `vsix.experimental-automation` | Headless / VSIX | 3600s | Launch, open, diagnostics, shutdown automation. |
| F-L8-022 | 3 | `vsix.host-lifecycle` | Headless / VSIX | 1800s | Protocol state machine, cancel, crash, mismatch. |
| F-L8-023 | 4 | `vsix.transform-tools` | Visual / VSIX | 1800s | Transform/WARP/rebase/Undo convergence. |
| F-L8-024 | 4 | `vsix.inspector-contracts` | Headless / VSIX | 1800s | Typed valid edits and no invalid mutation. |
| F-L8-025 | 5 | `vsix.source-sync` | Headless / VSIX | 1800s | One gesture, one native Undo, reparse convergence. |
| F-L8-026 | 5 | `vsix.bake-integration` | Headless / VSIX | 1800s | Host-backed import/bake diagnostics. |
| F-L8-027 | 6 | `vsix.debug-tools` | Headless / VSIX | 1800s | Read-only on-demand snapshots. |
| F-L8-028 | 6 | `vsix.compatibility` | Headless / VSIX | 3600s | net472/package/VS compatibility evidence. |
| F-L8-029 | 7 | `vsix.release` | Headless / VSIX | 7200s | Signed inputs, install, run, uninstall. |

F-L8-018 and F-L8-020 through F-L8-028 are Implemented with executable stages. F-L8-029 is Implementing with a fail-closed executable Stage. Its Release package is built once before observation and is never repacked afterward: strict 1033/1041 automated rows, six manual rows, package/build/installed DLL hashes, a dedicated `CgeRelease` install/uninstall lifecycle with zero residue, a recent `vsix.compatibility` prerequisite, and the canonical report must all bind the same package SHA-256. `vsix.transform-tools` binds the C# VSIX reference Scene, the transform/transaction/rebase/source contracts, and an isolated common Render Server WARP overlay pixel oracle. `vsix.inspector-contracts` fixes declared-type editor selection, compatible immutable asset choices, invalid/no-op non-mutation, vector/component structural edits, and source reparse behavior. `vsix.source-sync` fixes the one-shot latest-source planner, canonical verification reanalysis, Scene-scoped semantic-generation landing identity, native Undo routing, post-write non-cancellable closure, and absence of editor-owned authoring history. `vsix.bake-integration` fixes one request-correlated immutable presentation for Import/Bake, monotonic progress, cancellation, typed host diagnostics, partial output links, and false-success rejection. `vsix.debug-tools` fixes one bounded read-only query on the existing Preview Host session, typed/correlated validation, explicit declared-graph semantics, measured counters, non-terminal capture failures, and no polling or second runtime owner. `vsix.compatibility` makes `Docs/Verification/VSIX-Compatibility-Matrix.json` the immutable compatibility contract and validates it bidirectionally against the net472 project, amd64 Visual Studio 17.14+ source manifest, SDK/Roslyn pins, en-US/ja-JP package cultures, CgeFixed development hive, isolated CgeAutomation evidence hive, CI cells, detailed Experimental ledger, and manual summary. Pending evidence remains explicit and is accepted only for pre-release matrix validation, as is a Passed check whose observation has aged past its expiry; `--require-passing-evidence` fails every required non-passing or expired check. Automated WARP/package tests therefore cannot be misreported as human-observed Visual Studio, DPI, focus, or lifecycle evidence.


## Visual Shader Graph editor

F-L8-031 adds an IVsEditorFactory-backed WPF editor for .cgeshadergraph and .cgesubgraph. It uses the existing package, localization, Error List, Save, and native Undo/Redo boundaries. Typed invalid connections are rejected before mutation; save persists canonical source even when compilation fails, while the last-known-good artifact is not overwritten. Localized 1033/1041 item templates include root target selection and Subgraph creation. See [Shader Graph](Shader-Graph.md). External Subgraphs are listed in the editor and open on double-click. Stage templates copy and coherently remap the full dependency bundle. M0062 adds structured authoring. M0063 adds stable AutomationIds, TransformPattern layout editing, external-change debounce/retry, and Debug/Release 1041 Experimental evidence; F-L8-031 now Matches this scope.
