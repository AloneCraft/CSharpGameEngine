# ADR-0096 — Project-selected Logarithmic or Identity depth

- Status: Accepted; CPU/GPU implementation test verified; authoring and distribution acceptance remain
- Date: 2026-11-02
- Features: F-L5-038, F-L1-012
- Supersedes: ADR-0084's prohibition of an explicit uncompressed perspective mode; default logarithmic behavior and its numerical rules remain valid.

## Decision

Implement the accepted M0128 choice as `CgeDepthMapping=Logarithmic|Identity`, independently of numeric storage. Default remains Logarithmic. The shared Runtime submits the selected camera source through conditional compilation. Identity uses ordinary perspective projection and raster depth, with no logarithmic distance mapping. Its CPU depth identity is distinct from orthographic Linear; its GPU packet deliberately selects the existing direct-depth path. No wrapper shader, duplicated Stage graph or numeric-library mode is introduced.

Keep the physical and reconstruction matrices identical for Identity. Validate a finite invertible perspective matrix, finite camera/exposure and matching basis; reject an orthographic or incompatible reconstruction. Retain source ownership for resize and device recovery. The game does not submit logarithmic observer insets in Identity mode. Internal GI capture and light-space shadow views retain their own explicit contracts.

Separate Identity game outputs and generated files before Microsoft.Common.targets freezes their paths. Numerical libraries and physics cache identifiers do not depend on projection. Package the same build setting and view API for SDK consumers. Invalid choices fail explicitly.

## Verification

CPU tests check normal perspective depth numerically, validation boundaries, curve independence and resize. The existing output-isolation test covers every numeric choice with both projection choices. Existing GPU view, resize and recovery cases are extended with Identity. Rendering 195, generator 393 and the ten WARP view cases pass in Debug. The Float/Identity Source game builds with zero warnings/errors. M0128 records task IDs and separates these checks from actual authoring/distribution acceptance.
