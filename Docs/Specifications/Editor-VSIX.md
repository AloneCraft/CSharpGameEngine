# Editor and VSIX specification

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0054](../Decisions/ADR-0054-Editor-Authoring-Host-And-Source-Synchronization.md)
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

The predicted model is transient UI state only. It is never an alternative source of truth and never avoids a reparse. If parsing, conflict resolution, or writeback fails, the source is unchanged and the prediction rolls back to the last reparse result with an actionable diagnostic.

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

- Rotate, Scale, snap, rebase, WARP display, and Undo/Redo are acceptance requirements layered on the existing transform baseline.
- Inspector and palette use schema-aware editors for enums, vectors, asset references, add/remove, and reorder. Invalid text, missing assets, or illegal component topology produce diagnostics and leave C# source unchanged.
- Source synchronization batches one completed gesture into one Visual Studio native Undo unit and must converge to the Roslyn result even when external edits arrive.
- Bake/import UI reports the actual host result and artifact provenance. It may not enable a false “completed” state when a backend is unavailable.
- Render graph, profiler, and debug viewers request read-only versioned snapshots only on demand. They do not attach a second renderer or mutate runtime state.

## Failure diagnostics

Every visible failure includes a source/document identity, operation/request ID, protocol version, state transition, and recommended next action. Source edits include the originating snapshot version and conflict outcome. A failed validation, parse, host response, or artifact schema check rolls back predicted UI state only; it never rolls back unrelated text or creates an Undo entry that claims success.

## VSIX and runtime boundaries

Runtime L0-L6 cannot reference Roslyn or Visual Studio SDK. L7 compiler/bake work runs outside Visual Studio. L8 is a frontend over those services. The renderer used by Preview is the Render Server from [Rendering](Rendering.md); VSIX-specific rendering code is removed when F-L8-018 is implemented.

## Manual and automated evidence

Experimental Instance evidence remains explicit. Required automated cells validate models, protocols, package content, and WARP aliases. The Experimental Instance manual/automation stages have a 3600-second timeout; unrecorded manual checks remain pending and cannot count as passing release evidence.

## Stage oracle matrix

| Feature | Level | Stage | Mode | Timeout | Oracle |
|---|---:|---|---|---:|---|
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

All rows are `Specified` and registered as `NotImplemented`. Existing Scene Designer test stages remain executable baseline evidence.


## Visual Shader Graph editor

F-L8-031 adds an IVsEditorFactory-backed WPF editor for .cgeshadergraph and .cgesubgraph. It uses the existing package, localization, Error List, Save, and native Undo/Redo boundaries. Typed invalid connections are rejected before mutation; save persists canonical source even when compilation fails, while the last-known-good artifact is not overwritten. Localized 1033/1041 item templates include root target selection and Subgraph creation. See [Shader Graph](Shader-Graph.md). Dedicated external Subgraph navigation and automated Experimental Instance interaction remain partial conformance.
