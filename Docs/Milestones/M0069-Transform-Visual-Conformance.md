# M0069 - Transform visual conformance

- Status: Completed
- Date: 2026-09-01

## Outcome

F-L8-023 is implemented as the executable Visual/VSIX `vsix.transform-tools` Stage. Existing Move, Rotate, Scale, local/world, numeric snap, vertex snap, and surface snap behavior remains one edit state machine: any number of drag previews replace derived camera/frame-relative `float` matrices without touching C#, and mouse-up produces one final commit request.

Canonical authoring remains frame-local. A Transform commit rewrites only selected Logical position, rotation, or scale expressions and preserves the existing `ReferenceFrameId`. Equivalent Roslyn landing keeps the predicted document; foreign edits select the explicit rebase path. History emission retains one source step per completed gesture, and `SceneTextBufferWriter` remains the only native `ITextUndoTransaction` owner.

The visual oracle uses the shared out-of-process Preview Host and common Render Server. With camera, object, selection, and presentation inputs fixed, adding only the transparent gizmo triangle changes the captured D3D12 WARP pixels. Clearing only that overlay restores the exact baseline frame hash and reports no debug-layer errors. No renderer or graphics-device owner was added back to the VSIX.

## Verification

- Focused VSIX transaction and source tests: 38/38 Debug.
- Isolated common Render Server overlay test: 1/1 Debug on D3D12 WARP with the debug layer.
- FeatureStageCatalog F-L8-023/visual/Ledger contracts: 4/4 Debug; coverage validation passed.
- `vsix.transform-tools` Debug: Passed.
- Debug result: `.artifacts/FeatureStages/20260901T074232540Z-single-c74a34c1e56740288dc291b590cfc9a9.run-result.json`.
- `vsix.transform-tools` Release: Passed.
- Release result: `.artifacts/FeatureStages/20260901T074329047Z-single-efae16a35cc544c396ff760cc80838a3.run-result.json`.
- Full solution tests: 2,047/2,047 Debug and 2,046/2,046 Release.
- Full solution builds: Debug and Release succeeded with zero errors. Both configurations observed the existing transient first-retry `MSB3026` warning while the concurrently used VSIX test asset was replaced; the retry succeeded.

## Adversarial result

The previous WARP test changed overlay and camera-preview state in the same frame, so a hash difference did not isolate the gizmo path. M0069 adds an overlay-only test and an exact clear-to-baseline check. It also adds explicit `ReferenceFrameId` preservation and repeated-preview single-commit tests. The first full Release run exposed an unrelated non-deterministic generator E2E: both the three-host-frame Bootstrap path and the real-time fixed scheduler could stop the same loop. The test now keeps the `Position` archetype consumer but makes it side-effect-free, leaving the Bootstrap host-frame contract as the only stop condition; CodeAnalysis then passed 146/146 and the full Release suite passed. The result does not claim that WARP automation is a human Experimental Instance observation; the `scene-designer` manual ledger row remains Pending.

## Next dependency

F-L8-025 may now consume one completed transform and one completed typed Inspector boundary to implement cross-edit source synchronization v2. It must reuse the existing edit document, conflict decision, and native writer rather than introduce a second source or Undo truth.
