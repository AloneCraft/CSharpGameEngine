# Editor and VSIX specification

## Typed light authoring
F-L8-024/025 now include [Typed Lighting](Typed-Lighting.md): symbol-checked physical-value controls, atomic range/cone/rectangle gizmos, light/Transform/shadow palette batches and 12 edit-time light conversions. The source and source/target default-contract fingerprint must still match at commit; dimension-changing conversions require explicit quantity input. One verified source write uses existing native Undo. No runtime type replacement or private history is introduced.

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0054](../Decisions/ADR-0054-Editor-Authoring-Host-And-Source-Synchronization.md), [ADR-0061](../Decisions/ADR-0061-Vsix-Experimental-Evidence-And-Automation.md)
- Related lower specification: [Scene-Editor-Design](Scene-Editor-Design.md)
- Target layer: L8 over standalone L4/L7 services

## Scope and observed baseline

A scene's JSON document (`Scenes\**\*.json`, ADR-0070) is the sole authoring truth; C# scene classes are no longer built. The current Scene Designer implements a substantial baseline: live Roslyn scene projection, navigation/picking, Move/Rotate/Scale/Snap, predicted editing, source synchronization, native Undo/Redo integration, and typed inspector work. This specification does not downgrade that evidence; it defines the next acceptance contracts for WARP display, rebase, robust host lifecycle, typed asset workflows, and release verification.

The JSON Scene experiments of M0049/M0050 are not a supported compatibility path. The ADR-0070 move from C# scene classes to scene JSON was an immediate cutover (M0093): `CSharpGameEngine.SceneJson.Cli convert` rewrote every scene, and a remaining C# scene class is a build error (SCENE206) pointing at that converter. Any future persistence or protocol migration follows the same immediate cutover: write the new canonical form, regenerate derived data, and remove the old reader/writer in the same feature change.

## Authoring model

```text
Scenes/*.json live text buffer
  -> designer view: the equivalent C# class, analyzed in the project's Roslyn compilation
     (SceneJsonDesignerSource) -> SceneDocumentSnapshot
  -> predicted edit model while interaction is active
  -> edited model written back as canonical scene JSON (SceneJsonConverter, node ids kept)
  -> re-read and verified, then the changed lines only, as exactly one native Visual Studio Undo transaction
  -> Roslyn reanalysis of the buffer
  -> converged immutable projection for hierarchy, inspector, and viewport
```

The predicted operation model is transient gesture state only. It is never an alternative source of truth and never avoids a reparse. Before the live-buffer write, the new JSON is read back through the designer view; that canonical model, not the separately mutated operation model, is published as the temporary landing projection. If parsing, conflict resolution, verification, or writeback fails, the source is unchanged and the prediction rolls back to the last reparse result with an actionable diagnostic.

Scene Designer is a Visual Studio multi-view document: document, hierarchy, inspector, palette, and viewport project the same snapshot/session identity. A scene JSON (`Scenes\**\*.json`) opens by double-click in the Scene Editor (ADR-0071): one tab with a Design view (the Scene Designer) and a JSON view (the Visual Studio JSON editor over the same text buffer), switched by tabs at the bottom. The extension adds nothing to the code-editor or Solution Explorer context menus. Visual hierarchies are projections rather than runtime ownership graphs.

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

Every helper process the VSIX starts (the Preview Host and the resident bridges) is owned by the Visual Studio process that started it. It receives `--parent-process-id` and exits when that process exits, so it never keeps the extension's install folder locked against the next deployment. Resident bridge pipes are per owner: a Visual Studio never connects to another instance's bridges. Both servers read each request with their own lifetime and a 30-second receive deadline (`BridgeWireMessage.ReadRequestAsync`), so a client that stops mid-request neither keeps a stopping helper alive nor holds the Preview Host's single pipe instance; such a connection is dropped without a response.

## Editing contracts

- Rotate, Scale, snap, rebase, WARP display, and Undo/Redo are acceptance requirements layered on the existing transform baseline. Transform preview matrices are camera/frame-relative `float` UI projections only; the scene document is unchanged until commit. A canonical Transform commit updates only local Logical position/rotation/scale members and must preserve its `ReferenceFrameId` member exactly.
- Inspector and palette use schema-aware editors for enums, vectors, asset references, add/remove, and reorder. The declared component-member type, not the right-hand expression type, selects the editor. Asset choices are limited to compatible immutable members of the referenced `[SceneAssets]` container. `double` and `decimal` remain read-only at the engine-owned authoring boundary; canonical real editing uses `float`, and the engine's `LogicalScalar` members (and the R/U/F rows of a member's RUF vector) are edited as `float` because scene JSON reads them through `float`. Enum choices and strings write back to the scene JSON like numbers; deeper nesting such as `Rotation.EulerRuf` stays read-only. A component written with constructor arguments (the scene JSON names a constructor's parameters, as for a positional record) shows each argument as a read-only row named by its key; it is edited in the JSON view. A number inside an `[Angle]` member shows and takes degrees (the row name ends in ` (°)`) and is written back in the member's unit; the scene JSON stores it in turns (ADR-0074). Invalid text, an undeclared/missing asset symbol, illegal topology, and unchanged choices create no source operation or native Undo entry. Physical asset discovery/import/existence belongs to F-L8-019 and is not duplicated in this Inspector contract.
- Source synchronization resolves the latest Roslyn context for every completed gesture, explicitly rebases supported stale deltas or rejects the operation, verifies one minimal JSON edit by re-reading it, and opens at most one Visual Studio native Undo transaction. No project compile is on the gesture path. Consecutive gestures may use a transient projection only as the stable-ID landing basis; the JSON buffer and its native text history remain the only authoring/Undo truths. Nothing is written to disk per gesture: Save persists the current live document and never performs deferred writeback.
- Bake/import UI reports the actual host result and artifact provenance. It may not enable a false “completed” state when a backend is unavailable.
- Asset Preview transports only bounded `AssetAuthoringRequest`/`AssetAuthoringResult` DTOs to the versioned net10 Content host. Only that host may validate or atomically publish canonical `<source>.meta`; the net472 VSIX may display identity/options/diagnostics and request save with the inspected SHA-256, but it may not parse metadata, change an existing `AssetId`, overwrite a concurrent edit, or create a second metadata truth.
- The Scene View of a JSON scene whose game is built is the game's runtime view (ADR-0070 decision 6): the preview host runs the built game's world and renderer from a temporary copy of the build, and the VSIX sends the scene's live JSON (unsaved edits included) with every scene update. Value edits show at once. During a gizmo drag nothing is written yet, so each frame also carries the dragged roots' parent-relative transforms and the game's objects move with the gizmo; Esc resends the document. Without a built game, the scene preview and the Asset Preview draw perspective views with radial depth from the editor's own matrix (protocol 9, M0119); orthographic views use linear depth and camera thumbnails derive depth from their projection. Perspective cameras require their field of view, physical near/far range and positive transition length; they have no depth selector. Thumbnail clip planes are supplied from the scene camera, preserving a huge far plane that cannot be recovered from a rounded float projection. The same host process also runs the editor world (ADR-0047, M0096): the scene's `[ExecuteInEditor(Realtime=true)]` systems tick at 15 Hz beside the view, and the Spatial and Physics debug overlays ask this process. A structural edit shows "build to see this change" and is never drawn wrong; a new build reopens the view without another edit. Before the first build, or if the runtime view cannot open (the reason is shown), the Scene View shows the scene preview.
- The runtime diagnostics viewers (render graph, profiler and debug snapshot of protocol 5 and 6) were retired in M0097: protocol 7 removed the snapshot command, and Visual Studio's own debugger and profiler take their place.

## Failure diagnostics

Every visible failure includes a source/document identity, operation/request ID, protocol version, state transition, and recommended next action. Source edits include the originating snapshot version and conflict outcome. A failed validation, parse, host response, or artifact schema check rolls back predicted UI state only; it never rolls back unrelated text or creates an Undo entry that claims success.

## Diagnostic tools (M0097)

Extensions > C# Game Engine > Diagnostic Tools, and the same dropdown on the C# Game Engine toolbar, call Visual Studio's own tools for a game started as an exe. They replace the retired runtime diagnostics snapshot (F-L8-027, F-L8-030).

- The candidates are the running processes named like a game project's assembly in the open solution. A game started from a build already has this Visual Studio's debugger, so Attach skips the processes it debugs.
- **Attach Debugger** attaches the managed CoreCLR engine through the process's own transport, matching engines by id because their names are localized. Adding the native engine made Visual Studio stop responding when attaching to an engine process, so native debugging stays with Visual Studio's Attach to Process dialog.
- **Measure CPU Usage** and **Measure .NET Allocations** run the standard collector `VSDiagnostics.exe` beside the running Visual Studio with `CpuUsageBase.json` or `DotNetObjectAllocBase.json`. The same command then stops the measurement and opens the `.diagsession` from the solution's `.artifacts/Diagnostics`. A measurement also stops when the game exits.
- **Measure GPU Usage** has no collector configuration, so it opens the Performance Profiler (`Debug.DiagnosticsHub.Launch`) for the user to check GPU Usage and pick the game as the analysis target.
- The toolbar carries every command of the Extensions menu, Options included.

## VSIX and runtime boundaries

Runtime L0-L6 cannot reference Roslyn or Visual Studio SDK. L7 compiler/bake work runs outside Visual Studio. L8 is a frontend over those services. The renderer used by Preview is the Render Server from [Rendering](Rendering.md); VSIX-specific rendering code is removed when F-L8-018 is implemented.

## Game project context and bundled tools (ADR-0065, ADR-0068)

**Game project.** The editor works on a game project: a csproj that imports `CSharpGameEngine.Game.props` (SourceMode) or references the `CSharpGameEngine.Sdk` package (PackageMode). `CgeProjectContext` resolves it in this order:
1. From the Scene, model or asset being edited.
2. From the open solution, which the package records on solution events.
3. From the repository the VSIX was built from (development only).

**Paths.** Scene asset paths, `Assets/`, primitives, Fab downloads and the game assembly (`bin/<Configuration>/net10.0-windows10.0.19041.0/win-x64/<Assembly>.dll`) are relative to the project directory. Bridges run in the engine repository when there is one, otherwise in the project directory.

**Bundled tools.** The VSIX carries its tools and a bridge built into the VSIX takes precedence over a repository build:
- `Bridges/` holds the glTF, FBX, asset-authoring, primitive and shader-compiler bridges.
- `PreviewHost/` holds the Preview Host.
- `Packages/` holds the engine SDK package.
- `ProjectTemplates/` holds the Third Person, First Person, Top Down and Vehicle templates, and `ProjectTemplateSources/<name>` their sources (ADR-0077/M0101 stage 9).

**SourceMode-only features.** Physics Scene Bake and the legacy model viewer need the engine repository. Physics Bake reports this instead of failing.

**New game.** The four templates (Third Person, First Person, Top Down, Vehicle) create a PackageMode game through `GameProjectTemplateWizard`; the template is named by the `.vstemplate` file that started the wizard:
- The bundled package goes to `%LOCALAPPDATA%\CSharpGameEngine\packages\<version>`.
- The wizard writes the sources of `ProjectTemplateSources/<name>` with the new namespace (the template's `Cge<name>` is replaced) and a `nuget.config`.
- The level opens after creation: `Scenes/Lvl_<name>.json`, which for Vehicle is `Scenes/Lvl_VehicleBasic.json`.

## Manual and automated evidence

Experimental Instance evidence remains explicit. `VSIX-Experimental-Baseline.json` permits Pending but validates owner, observation and expiry before Passed can count; release validation also requires the evidence file (kept under the untracked `.artifacts` root) and unexpired evidence. `CSharpGameEngine.Vsix.Automation` runs only against the exact `CgeAutomation` suffix, classifies exit code 2 as InfrastructureError, records package/build/installed DLL hashes, and never closes a pre-existing PID. The Experimental stages are `vsix` profile only and retain a 3600-second Feature Runner timeout.

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
| F-L8-028 | 6 | `vsix.compatibility` | Headless / VSIX | 3600s | net472/package/VS compatibility evidence. |
| F-L8-029 | 7 | `vsix.release` | Headless / VSIX | 7200s | Signed inputs, install, run, uninstall. |

F-L8-018 and F-L8-020 through F-L8-028 (F-L8-027 was retired in M0097) are Implemented with executable stages. F-L8-029 is Implementing with a fail-closed executable Stage. Its Release package is built once before observation and is never repacked afterward: strict 1033/1041 automated rows, six manual rows, package/build/installed DLL hashes, a dedicated `CgeRelease` install/uninstall lifecycle with zero residue, a recent `vsix.compatibility` prerequisite, and the canonical report must all bind the same package SHA-256. `vsix.transform-tools` binds the C# VSIX reference Scene, the transform/transaction/rebase/source contracts, and an isolated common Render Server WARP overlay pixel oracle. `vsix.inspector-contracts` fixes declared-type editor selection, compatible immutable asset choices, invalid/no-op non-mutation, vector/component structural edits, and source reparse behavior. `vsix.source-sync` fixes the one-shot latest-source planner, canonical verification reanalysis, Scene-scoped semantic-generation landing identity, native Undo routing, post-write non-cancellable closure, and absence of editor-owned authoring history. `vsix.bake-integration` fixes one request-correlated immutable presentation for Import/Bake, monotonic progress, cancellation, typed host diagnostics, partial output links, and false-success rejection. `vsix.debug-tools` fixes one bounded read-only query on the existing Preview Host session, typed/correlated validation, explicit declared-graph semantics, measured counters, non-terminal capture failures, and no polling or second runtime owner. `vsix.compatibility` makes `Docs/Verification/VSIX-Compatibility-Matrix.json` the immutable compatibility contract and validates it bidirectionally against the net472 project, amd64 Visual Studio 17.14+ source manifest, SDK/Roslyn pins, en-US/ja-JP package cultures, CgeFixed development hive, isolated CgeAutomation evidence hive, CI cells, detailed Experimental ledger, and manual summary. Pending evidence remains explicit and is accepted only for pre-release matrix validation, as is a Passed check whose observation has aged past its expiry; `--require-passing-evidence` fails every required non-passing or expired check. Automated WARP/package tests therefore cannot be misreported as human-observed Visual Studio, DPI, focus, or lifecycle evidence.


## Visual Shader Graph editor

F-L8-031 adds an IVsEditorFactory-backed WPF editor for .cgeshadergraph and .cgesubgraph. It uses the existing package, localization, Error List, Save, and native Undo/Redo boundaries. Typed invalid connections are rejected before mutation; save persists canonical source even when compilation fails, while the last-known-good artifact is not overwritten. Localized 1033/1041 item templates include root target selection and Subgraph creation. See [Shader Graph](Shader-Graph.md). External Subgraphs are listed in the editor and open on double-click. Stage templates copy and coherently remap the full dependency bundle. M0062 adds structured authoring. M0063 adds stable AutomationIds, TransformPattern layout editing, external-change debounce/retry, and Debug/Release 1041 Experimental evidence; F-L8-031 now Matches this scope.


## Solution Explorer and Assets (ADR-0081)

**Asset editors (ADR-0089, M0127).** Double-clicking a model, texture, sound, font or `.cgeasset` opens a dedicated editor document; the shader graph uses the same placement. They float front-most by default (Tools > Options > C# Game Engine > Asset Editors can switch to document tabs), can be docked, and are not re-floated afterwards or on restore. The scene stays a central document. The asset preview host speaks protocol 10 (per-frame lighting, background, ambient/sky, floor, and skinned glTF that the host loads itself, ADR-0090). Skinned models get a skeleton tree and an animation timeline. See [Asset Editors](Asset-Editors.md).

[Asset Workspace](Asset-Workspace.md) defines the asset UX and acceptance contracts. The implementation provides the public Solution Explorer filter, selection following/location locking, a virtualized bottom tile/details view, search/provider/state filters, saved locations/searches, explicit reveal, asset Inspector and typed model/reference drops. Initial ShowNoActivate runs after package load and only once for a game project; normal startup does not reapply Game Engine Layout. Output-tab docking is registered with a 280 DIP default.

DataAsset edits validate with the shared scene analyzer and edit the live text buffer. Organize actions show complete before/after text, validate source/sidecar/read-set/compilation snapshots and use linked native Undo plus a physical-file primitive. The source in the active scene remains its placement session. Runtime UI verification, importer integration and remaining organize cases are recorded in [M0123](../Milestones/M0123-Asset-Workspace.md); registration and automated tests alone are not evidence of actual docking, focus or Undo gestures.
