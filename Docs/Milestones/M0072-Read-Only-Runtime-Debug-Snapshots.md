# M0072 — Read-only runtime debug snapshots

Date: 2026-09-01  
Feature: F-L8-027  
Stage: `vsix.debug-tools`

## Requirement

Render Graph, profiler counters, and runtime debug information must be inspectable from Visual Studio without creating another renderer, polling runtime state, issuing control commands, or turning an editor cache into another source of truth.

## Invariants

- The existing Preview Host session and its common Render Server remain the sole render/device owner.
- A snapshot request is explicit, versioned, request/session/sequence correlated, bounded, and read-only.
- Capturing a snapshot does not render or change device generation, completed fence, frame hash, scene state, or resource ownership.
- Only observed counters are reported. The L4 topology is labeled `DeclaredPlan`; no per-pass execution or timing is invented before the D3D12 graph executor exists.
- A capture failure consumes its protocol sequence but leaves the session usable.
- Malformed, oversized, null, stale, or envelope-inconsistent data never reaches presentation.
- Transport loss uses the existing Preview Host/fresh-surface recovery contract; no fallback renderer or second session is attached.
- Bounded diagnostic truncation is explicit.

## Architecture decision

Render Preview protocol v4 adds one empty-payload `Snapshot` command and a typed snapshot DTO containing device lifecycle identity, measured counters, a bounded declared graph projection, and bounded D3D12 diagnostics. `RenderServer` owns the canonical compatibility `RenderQualityPlan` for its current configured dimensions. `RenderPreviewRenderSession` maps engine state to the wire DTO without mutating it.

The net472 client validates protocol correlation, every DTO bound and enum, graph indices/lifetimes/transitions, diagnostic shapes, and envelope generation/fence consistency. A normal capture error is non-terminal; protocol corruption is terminal. Scene Designer performs one synchronous local request with a bounded response timeout and presents the immutable result. Existing Spatial and Physics snapshot overlays remain their respective debug visualizers.

## Adversarial review and fixes

- Refused to describe the current declared L4 graph as executed D3D12 graph evidence.
- Omitted fictitious frame time and per-pass timing; only existing counters are shown.
- Kept capture errors non-terminal so a read-only query cannot dispose runtime state.
- Added client rejection for null/oversized fields and response/snapshot generation or fence disagreement.
- Made wire validation safe for malicious nullable JSON fields.
- Added explicit diagnostic truncation state instead of silently presenting a partial set as complete.
- Kept one on-demand button and immutable presentation; no timer, Manager, cache, profiler clock, control command, renderer, or compatibility layer was added.

## Verification

- Protocol validation focused suite passes 11/11 in Debug.
- VSIX client/presentation/architecture/localization focused suite passes 19/19 in Debug.
- Host table, Named Pipe, common Render Server, and real WARP session focused suite passes 14/14 in Debug.
- The WARP session proves two snapshot captures leave device generation, completed fence, rendered-frame counter, and frame hash unchanged.
- Complete Contracts suites pass 14/14 in Debug and Release.
- Complete VSIX suites pass 970/970 in Debug and Release; the known test-asset VSIX copy lock retried once and then completed.
- Complete Windows/WARP suites pass 236/236 Debug and 235/235 Release.
- Debug `vsix.debug-tools`: Passed, result `20260901T111335750Z-single-29723561406e411ab222de69f27fc5f2`.
- Release `vsix.debug-tools`: Passed, result `20260901T111501042Z-single-6dab6c9310fc4412a2ea5c1232d0f70a`.

Experimental Instance human observation remains separately Pending and is not inferred from headless tests.
