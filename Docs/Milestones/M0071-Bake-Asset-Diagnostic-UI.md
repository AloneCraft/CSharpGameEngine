# M0071 — Bake, asset, and diagnostic UI

Date: 2026-09-01  
Feature: F-L8-026  
Stage: `vsix.bake-integration`

## Requirement

Import and Bake must expose progress, cancellation, terminal outcome, actionable typed diagnostics, and generated artifacts without giving the VSIX another build implementation or another source of truth. A UI success must mean the same thing as a valid shared host success.

## Invariants

- The versioned host request/progress/result/diagnostic envelope is the execution truth.
- One request ID owns one immutable UI projection; operation state is not duplicated by booleans.
- Progress protocol and request ID match, sequence and fraction are monotonic, and fraction is finite.
- A success requires exit code zero, correlated terminal progress at 100%, and no error diagnostic.
- Cancellation is explicit; late progress cannot change `Cancelling` into failure or success.
- Typed diagnostic severity, code, and message survive resident transport and UI projection.
- A later failure/cancel does not erase already completed artifact outputs.
- C# source, exact host build, artifact publication, Render Server, and retry ownership remain in their existing boundaries.

## Architecture decision

`AuthoringOperationSnapshot` is a small immutable validation/presentation value, not an operation manager. Physics Bake remains executed by `PhysicsSceneBakeService` and its exact external host. Asset format loading continues through the existing importer/bridge boundary and common Render Server Preview Host. The VSIX only maps their shared protocol and outputs into progress, diagnostics, cancellation controls, and artifact actions.

`ResidentBridgeClient.TryRunDetailed` preserves the typed `AuthoringHostResult`; the compatibility string-returning method formats that result only for legacy callers. `ExternalBridgeProcess` throws `AuthoringHostOperationException` after submission so the UI can retain diagnostic codes without retrying or flattening execution identity.

## Adversarial review and fixes

- Replaced the Physics Bake running boolean with the immutable operation state.
- Prevented late progress from being applied after cancellation begins.
- Applied terminal progress correlation/finite/monotonic validation to failure and cancel results, not only success.
- Accepted and safely cloned nullable/untrusted diagnostic arrays.
- Preserved resident host diagnostics instead of reducing them to one string.
- Rejected resident `Succeeded`/exit-zero results that still contain an Error diagnostic before
  the typed result can be discarded or a local success can be synthesized.
- Preserved and displayed partial output artifacts after later failure/cancellation.
- Added an actionable selected-artifact path without adding an artifact cache or second build model.

## Verification

- Focused VSIX presentation, orchestration, architecture, resident-client, and localization tests pass.
- Shared authoring-host session, wire, and server tests pass 22/22 in Debug and Release.
- Complete VSIX suites pass 963/963 in Debug and Release. Both builds have zero errors; the known
  test-asset VSIX copy lock retried once where observed and then completed.
- Feature Runner passes 71/71 in Debug and Release; feature coverage, manual evidence, golden,
  CI-matrix, and failure/quarantine gates pass.
- Solution/project/shared-project registration passes 3/3 in Debug and Release.
- `vsix.bake-integration` Debug: Passed, result
  `20260901T103047385Z-single-6faa5ae37bd14cdca7493d171b329129`.
- `vsix.bake-integration` Release: Passed, result
  `20260901T103042452Z-single-91698bd6802f47df9ab2a826c0e7d55a`.

Experimental Instance human observation remains separately Pending and is not inferred from headless tests.
