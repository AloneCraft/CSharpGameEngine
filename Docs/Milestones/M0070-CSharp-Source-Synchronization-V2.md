# M0070 — C# source synchronization v2

Date: 2026-09-01  
Feature: F-L8-025  
Stage: `vsix.source-sync`

## Requirement

C# `IScene` must remain the only authoring source. A completed Hierarchy, Inspector, or Viewport gesture must produce at most one minimal live-buffer edit and one native Visual Studio Undo transaction. Roslyn reparse, selection identity, external edits, Undo/Redo, cancellation, and Save must converge without an editor-owned editable document or private history.

## Invariants

- C# source is the only authoring truth; Visual Studio text history is the only Undo truth.
- Predicted state exists only within a gesture or as a transient landing projection.
- Every commit resolves the latest Roslyn context; stale input is explicitly rebased or rejected.
- Edited C# is compiled and reanalyzed before the live buffer is changed.
- The published landing model is that canonical reanalysis result, not the separately mutated verification model.
- One gesture opens at most one `ITextUndoTransaction`; failure before the write opens none.
- Save only persists the current live document. It never replays deferred editor history.
- Cancellation, failure, Undo, and Redo cannot leave a second source, watermark, or ownership ledger.
- Pending landing identity is scoped by Scene and canonical semantic signature; an older analysis
  cannot consume it, and a superseding external edit cannot carry it into a later generation.
- Once the live buffer has changed, transaction closure is non-cancellable; Session disposal cannot
  turn a successful source mutation into a retryable failure or resurrect disposed projection state.

## Architecture decision

The previous `SceneEditDocument` architecture was removed rather than adapted. `SceneSourceCommitPlanner` is a VS-independent one-shot boundary: merge latest input, apply immutable operations, edit syntax, verify by compile/reanalysis, derive minimal changes, and return one write plan. `SceneEditorSessionService` owns only immutable snapshots, reconcilers, one per-Scene write guard, and a transient pending landing identity basis. `SceneWorkspaceService` owns the Visual Studio live-buffer boundary and delegates the single write to `SceneTextBufferWriter`.

The first adversarial sequence test found a remaining representation split: `SceneModelEditApplier` and Roslyn reanalysis agreed semantically but retained different emit-expression text, causing a following gesture to see a false diff. `SceneSaveVerification.TryVerifyAndAnalyze` now returns the canonical reanalysis model, and the planner reconciles it against the operation projection solely to retain new node IDs.

The final lifecycle review found that one global awaiting-write key could be overwritten by another
Scene and that an older analysis could consume a pending identity before its write completed.
`PendingSourceLanding` now binds each Scene independently to the canonical structural signature and
an explicit pre-write/completed phase. A completed mismatching landing is treated as a superseding
external edit and discarded.

## Removed complexity

- `SceneEditDocument` and its session-long editable model
- `SceneTextUndoLedger` and buffer-version ownership checks
- text watermark/suffix/synchronization policies
- external-change and own-save landing state machines
- Save-time `ApplyForSave`/multi-step writeback
- multi-step Workspace writer and partial-write error resource
- custom Undo/Redo routing over mirrored history
- obsolete implementation-detail tests and helpers

## Verification

- Focused planner/landing/conflict/save/architecture contracts: 95/95 passed.
- Complete VSIX Debug suite: 950/950 passed.
- Complete VSIX Release suite: 950/950 passed.
- VSIX Debug and Release builds: succeeded with 0 errors; each observed one transient test-asset
  VSIX copy retry warning and then completed.
- Feature Runner tests: 71/71 passed in both Debug and Release.
- Solution/project/shared-project registration tests: 3/3 passed in both Debug and Release.
- `vsix.source-sync` Debug: Passed, result `20260901T093121651Z-single-32c53f432e4e412fad3f6a6a316fcf6f`.
- `vsix.source-sync` Release: Passed, result `20260901T093207194Z-single-9cdf32c1bd6e42d291cfdb09056a9100`.
- Coverage, manual-evidence, golden, CI-matrix, and failure/quarantine build gates: Passed.

Experimental Instance human observation remains separately Pending; automated evidence is not relabeled as manual observation.
