# Omni — radial (logarithmic) depth specification

- Status: Implemented (M0099, 2026-10-04); radial depth is the default since M0100 (2026-10-04). M0119 removes camera depth selectors, legacy matrix/preview compatibility and standalone Model/Triangle renderers. This file is the intended contract; [Implementation Status](../Implementation-Status.md) F-L5-038 and F-L5-039 track the evidence.
- Decision: [ADR-0076](../Decisions/ADR-0076-Native-Radial-Depth-Mapping.md); implementation migration [ADR-0084](../Decisions/ADR-0084-Logarithmic-Native-Depth.md) / M0119.
- Reference: the Unity asset Omni (`G:\Unity\6.6\Omni\Assets\Omni`), ported natively without its URP adapter.
- Target layers: L0 (StageRenderer), L4 (RenderServer), L5 (Runtime camera and observer), L6 (Stage shader ABI), L8 (editor runtime view, isolated preview, preview host protocol)

## Product outcome

One camera sees from millimetres to the Q64.64 range (about 9.2e18 m) with correct occlusion at every scale. The image is the standard image; only the depth differs. A second, optional view shows the compressed world from outside.

## Depth mapping per view

| Mapping | Where | Vertex | Depth |
|---|---|---|---|
| `Logarithmic` (perspective) | every scene Camera (game and editor runtime view); every perspective view of the editor's isolated preview (Scene view of an unbuilt game, Asset Preview) and the perspective thumbnails of scene cameras; every render camera made by `new Camera { ... }` or `Camera.CreateDefault`/`CreateFirstPerson`/`CreateBoundsFit` (the viewer, the test host, direct `StageRenderer` users) | `VSMain`: physical X/Y/W and conservative raster Z | native `PSMain` writes `SV_DepthGreaterEqual = min(G(r) · InverseMaximumG, 0.99999994)` |
| `Linear` | orthographic previews and card captures | native `VSMain` | linear raster depth |
| `Observer` | an observer inset (camera preview pass) | `ObserverVSMain` maps positions around the centre camera | logarithmic distance in the mapped geometry, through the same pixel entry |

## Curve

- `G(r) = log2(1 + r / T)`, `T = RadialTransitionMeters` (default 1 m, finite and positive). Omni's Smooth curve; no other curve exists.
- `log2(1 + x)` is evaluated with float arithmetic only, the same on the CPU (`RadialDepthCurve.Log2OnePlus`) and in the shader (`RadialLog2OnePlus`): `ln(1 + x) = 2 atanh(s)` by its odd series to `s⁹`, with `s = x / (2 + x)` below `√2 − 1` and, above it, `1 + x = m · 2^e` (bit split, `m` in `(√½, √2]`) and `s = (m − 1) / (m + 1)`. The shader never calls the hardware `log2`, an approximation (on an RTX 4060 Ti it put a 4 µm surface's depth 3 % off); `StagePresentationShaderSourceTests` keeps transcendental intrinsics out of the presentation entries.
- `r` is the length of the camera-relative physical position, computed after dividing by the largest component.
- `rMax = FarZ · sqrt(1 + tan²(fovY/2) · (1 + aspect²))` (the far-plane corner); `InverseMaximumG = 1 / G(rMax)`.
- Observer compression: `F(d) = T · G(d)` metres; the compressed world has radius `T · G(rMax)`.

## Invariants

1. Seven native frames are pinned by StageGoldenFrameTests. Before deleting camera selection, all seven passed the final standard comparison within one channel level and 1% changed pixels (wireframe: 1% changed pixels); evidence is in M0119.
2. Native colour uses physical shading in PSMain; changing the depth transition preserves the physical image. Clip distances preserve the camera's physical near/far coverage. Native frames and frame-slot/curve-update tests are the ongoing colour oracle.
2a. The radial main pass rasterizes the conservative depth `a − b/w`, below the encoded depth everywhere, and declares `SV_DepthGreaterEqual`, so the hardware keeps rejecting hidden fragments early. Each object's `(a, b)` (`ObjectCB.ConservativeRasterDepth`, `RadialDepthProjection.ConservativeDepthAt`) is tangent at the view depth of its mesh's bounds centre, taken from the view-projection's W, so rejection works at every distance (a single tangent rejected only near itself). It runs without depth clipping and clips with the standard projection's near/far distances (`SV_ClipDistance`).
3. Along every pixel ray, depth order equals physical distance order at all scales; radial depth stays below the 1.0 clear. Exact depth ties (a wireframe edge drawn from both triangles or coincident duplicates) may be settled by rounding and draw order; a shared wireframe edge can show either face's colour.
4. Every pass that writes a radial view's depth uses the same depth function.
5. Lighting, shadows, fog, GI and normals use physical values; no Jacobian is applied to normals.
6. Fragments beyond `FarZ · (1 + 1/1024)` of physical view depth are discarded (the margin keeps every fragment the projection's own far plane kept); NearZ is clipped by the standard projection.
6a. A view handed over as one perspective matrix (`SetViewProjection` with `RadialViewParameters`) is drawn radial from that matrix unchanged; only depth columns are rebuilt from its W, so its pixels and depth equal the radial camera's that built the matrix.
7. Passes that invert the camera view-projection (sky direction, sun cascades, AO reconstruction rays) receive in radial mode a finite projection with near `max(NearZ, min(0.01 m, FarZ/2))` and far `min(FarZ, near · 65536)`; for an ordinary range it is exactly the camera projection.
7a. AO receives physical distance from the native surface pass at its half-resolution sample positions. Its ray reconstruction uses the finite auxiliary projection without clipping geometry to that projection's far plane. The main distance/depth allocations are reused before the full-resolution colour pass; AO owns no depth target.
8. Shadow atlas and overlay passes keep their light/screen-space depth. DDGI perspective captures use logarithmic depth and write physical distance to a separate R32F target; convolution reads that distance directly.
9. Device recovery replays the depth mapping with the camera.
10. Picking stays the CPU physical ray: the visible surface of a radial view is the nearest physical hit.
11. Invalid radial parameters are rejected at the camera boundary with a typed error; nothing silently falls back to standard.

## Authoring and editor

- The scene `Camera` component has `RadialTransitionMeters` (default 1); its depth-mode selector and `CameraDepthMapping` are removed. Scene cameras render logarithmic depth. Existing scene JSON without the removed member keeps its native defaults. The generated scene schema contains the curve setting and no depth selector.
- `LevelLoad` copies the curve into the render camera on every frame, with the exposure, so a value edit (the Scene Designer's or the game's) shows on the next frame; an unusable value keeps the last usable projection (at load, the default one) and is named in `LevelRuntime.InvalidCameraProperty`. The editor runtime view follows the scene camera: a scene camera makes the editor runtime view radial, and its clip range covers both the editor's focus-based near/far and the scene camera's NearZ/FarZ (nearer near, farther far), so it sees what the game sees and what the designer frames.
- The isolated preview (Scene view of an unbuilt game, Asset Preview) draws every perspective view radial from the editor's own matrix (protocol 9: required FOV/near/far metadata and `RadialTransitionMeters`, without a depth selector). A camera thumbnail derives its depth from the scene projection (logarithmic for perspective, linear for orthographic) and writes native conservative depth through `PreviewCleanPSMain` with a zero raster bound. A radial thumbnail the host could not draw (a non-positive `RadialTransitionMeters`, a far plane whose frustum corner overflows float) shows an error on its card instead of being sent, because the protocol rejects the whole frame for one invalid camera. The renderer requires a validated `RenderViewDepth` per preview; an observer carries its centre in that packet. Bare perspective matrices are rejected; bare orthographic matrices use validated linear depth.

## Precision budget

With 32-bit float depth, two surfaces are distinguished once their relative distance exceeds about `2^-24 · ln2 · log2(rMax / T) · (1 + T / r)` (about 3e-6 for near 0.1 m and far 1e18 m). Below `T` the float's own relative precision applies. On a GPU the rasterizer adds error of its own: vertices snap to its sub-pixel grid, so the interpolated position moves by up to the distance gradient times 1/256 pixel. In the middle of a view the written depth is within 4 ulps of the double oracle on WARP and on an RTX 4060 Ti; near the edges of a 160 × 90 view the GPU reached 6 ulps at 20 km and 41 ulps at 4 µm (`RadialDepthPrecisionEvidence`, on demand). Standard depth is interpolated from the same snapped vertices.

## Observer view

- Authoring: a `RadialObserver` component (Enabled, inset rectangle as fractions of the surface, FovYRadians, NearZ, ShowCenterCamera) on a node whose Transform is the observer. `RadialObserverSync` turns each enabled one into a camera preview (at most three) while the active camera is radial; `EngineHost` submits the frame's list and clears it when none remain.
- Centre: the active radial camera `C`. Eye: the observer's Transform `E`, made camera-relative through the runtime's single Q64.64-to-float conversion.
- Clip position: `mul(float4(dir · F(|P − C|) − (E − C), 1), ObserverRotationProjection)`. Lighting uses the physical position and the physical observer eye; interpolation is perspective in the mapped space (an approximation for visualization).
- Projection far: `|E − C| + 1.05 · T · G(rMax)`.
- Drawn in the game runtime only, as one camera preview pass. With `ShowCenterCamera` the overlay pass draws the centre camera's view frustum (5 % of the compressed world's radius long) inside the inset with the observer's view-projection. Not drawn when an editor camera owns the previews.

## Non-goals

Omni's Threshold curve; vertex warping for ordinary cameras; mantissa/exponent constant packets; shader/material conversion and restore ledgers; BRG sidecars; culling (Stage draws every object; a future visibility path must test near/far on radial distance and keep undecidable objects visible); motion vectors (a future TAA needs current and previous depth-mapping packets); the orthographic log grid; an editor toggle; editor navigation at astronomical scale; full-screen observer views and picking or gizmos inside them; interplanetary shadows; effective Early-Z rejection for thumbnails (their conservative raster bound is zero).

## Verification

- CPU: `RadialDepthTests`.
- Standard regression: `StandardModeGoldenFrameTests` plus the existing exact WARP pixel tests.
- Shader ABI: `ShaderGraphCompilerTests`.
- GPU: `RadialDepthGpuIntegrationTests`, `ShaderArtifactHotReloadTests.RenderServer_DebugHotReload_RebuildsTheRadialPipelines`.
- Runtime and editor: `SceneCameraTests`, `SceneJsonRuntimeValuesTests`, `EditorRuntimeViewTests`, `RadialObserverTests`.
- Visual Stage `rendering.radial-depth` (WARP required).

## Native Stage contract (M0119)
Stage owns one vertex/pixel interface. `VSMain` emits physical X/Y/W, a conservative raster bound and near/far clip distances; `PSMain` writes colour, physical ray distance and logarithmic `SV_DepthGreaterEqual`. Presentation entries share it. The ABI has fourteen entries and FrameCB remains 960 bytes. There are no radial wrapper entries, alternative camera depth selectors or duplicate pipeline families.
`RenderViewDepth` in L4 validates and supplies `DepthEncoding0/1`. `DepthEncoding1.w` is 1 for the main conservative bound and 0 for thumbnails/captures; an observer stores its centre in the same packet. `RenderCameraPreview.Depth` is required. Bare matrices must be finite invertible orthographic views; perspective matrices require physical FOV, near/far and transition metadata. Protocol 9 carries that metadata without a selector, preserving a scene camera's original huge far value.
The native surface pass also feeds the half-resolution physical AO input and Lumen surface depth. Sky/AO consume SceneDistance, DDGI consumes CaptureDistance, and zero distance is background. Shadows keep light-space depth. The finite auxiliary projection supplies reconstruction directions without imposing its far plane on physical geometry.
The old standalone Model/Triangle renderers, graph targets, artifact producers and templates are removed. Whole-suite, independent depth, hardware, pinned-image and active-debug-layer template evidence is recorded in M0119. Four render paths and the wider rendering/physics layer migration remain M0120–M0123.
