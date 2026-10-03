# Omni — radial (logarithmic) depth specification

- Status: Implemented (M0099, 2026-10-04). This file is the intended contract; [Implementation Status](../Implementation-Status.md) F-L5-038 and F-L5-039 track the evidence.
- Decision: [ADR-0076](../Decisions/ADR-0076-Native-Radial-Depth-Mapping.md)
- Reference: the Unity asset Omni (`D:\Unity\6.6\Omni\Assets\Omni`), ported natively without its URP adapter.
- Target layers: L0 (StageRenderer), L4 (RenderServer), L5 (Runtime camera and observer), L6 (Stage shader ABI), L8 (editor runtime view)

## Product outcome

One camera sees from millimetres to the Q64.64 range (about 9.2e18 m) with correct occlusion at every scale. The image is the standard image; only the depth differs. A second, optional view shows the compressed world from outside.

## Depth mapping per view

| Mapping | Where | Vertex | Depth |
|---|---|---|---|
| `Radial` (default) | every scene Camera unless it chooses Standard (game and editor runtime view); every render camera made by `Camera.CreateDefault`/`CreateFirstPerson`/`CreateBoundsFit` (the viewer, the test host, direct `StageRenderer` users); a camera preview that asks for it | standard `VSMain` | `SV_Depth = min(G(r) · InverseMaximumG, 0.99999994)` |
| `Standard` | an explicit choice: a scene Camera with `DepthMapping = Standard`, the editor's isolated previews and camera thumbnails (they hand the renderer a matrix), and the pinned standard frames | standard `VSMain` | hyperbolic depth of the perspective projection |
| `RadialObserver` | an observer inset (camera preview pass) | `RadialObserverVSMain` maps positions around the centre camera | raster depth of the mapped triangles |

## Curve

- `G(r) = log2(1 + r / T)`, `T = RadialTransitionMeters` (default 1 m, finite and positive). Omni's Smooth curve; no other curve exists.
- `log2(1 + x)` is evaluated as `u = 1 + x; u == 1 ? x / ln2 : log2(u) · x / (u − 1)` on the CPU (`RadialDepthCurve.Log2OnePlus`) and in the shader.
- `r` is the length of the camera-relative physical position, computed after dividing by the largest component.
- `rMax = FarZ · sqrt(1 + tan²(fovY/2) · (1 + aspect²))` (the far-plane corner); `InverseMaximumG = 1 / G(rMax)`.
- Observer compression: `F(d) = T · G(d)` metres; the compressed world has radius `T · G(rMax)`.

## Invariants

1. Standard views are bit-identical to the pre-M0099 renderer (`StandardModeGoldenFrameTests`).
2. A radial view's colour comes from the unchanged `PSMain`; wherever the same surface wins, the pixel equals the standard pixel bit for bit on triangles that need no clipping, and within one 8-bit level on clipped triangles (the radial main pass clips with clip distances).
2a. The radial main pass rasterizes the conservative depth `a − b/w`, tangent to the encoded depth at `8·max(T, NearZ)` and below it everywhere, and declares `SV_DepthGreaterEqual`, so the hardware keeps rejecting hidden fragments early. It runs without depth clipping and clips with the standard projection's near/far distances (`SV_ClipDistance`).
3. Along every pixel ray, depth order equals physical distance order at all scales; radial depth stays below the 1.0 clear. Exact depth ties (a wireframe edge drawn from both of its triangles, exactly coincident duplicates) are settled by draw order in the standard mode and by rounding in the radial mode, so a shared wireframe edge may show its other face's colour.
4. Every pass that writes a radial view's depth uses the same depth function.
5. Lighting, shadows, fog, GI and normals use physical values; no Jacobian is applied to normals.
6. Fragments beyond `FarZ · (1 + 1/1024)` of physical view depth are discarded (the margin keeps every fragment the projection's own far plane kept); NearZ is clipped by the standard projection.
6a. A view handed over as one perspective matrix (`SetViewProjection` with `RadialViewParameters`) is drawn radial from that matrix unchanged; only depth columns are rebuilt from its W, so its pixels and depth equal the radial camera's that built the matrix.
7. Passes that invert the camera view-projection (sky direction, sun cascades, AO prepass) receive in radial mode a finite projection with near `max(NearZ, min(0.01 m, FarZ/2))` and far `min(FarZ, near · 65536)`; for an ordinary range it is exactly the camera projection.
8. Shadow atlas, GI capture and overlay passes are standard in every mode.
9. Device recovery replays the depth mapping with the camera.
10. Picking stays the CPU physical ray: the visible surface of a radial view is the nearest physical hit.
11. Invalid radial parameters are rejected at the camera boundary with a typed error; nothing silently falls back to standard.

## Authoring and editor

- The scene `Camera` component has `DepthMapping` (`Radial` default, `Standard`) and `RadialTransitionMeters` (default 1). Scene JSON stores only changed values, so a scene without the member renders radial; `"DepthMapping": "Standard"` keeps the hyperbolic depth.
- `LevelLoad` copies them into the render camera on every frame, with the exposure, so a value edit (the Scene Designer's or the game's) shows on the next frame; an unusable value keeps the last usable projection and is named in `LevelRuntime.InvalidCameraProperty`. The editor runtime view follows the scene camera: a radial scene camera makes the editor view radial, and its clip range covers both the editor's focus-based near/far and the scene camera's NearZ/FarZ (nearer near, farther far), so it sees what the game sees and what the designer frames.
- Camera thumbnails are drawn only by the isolated preview of an unbuilt game, which stays standard (non-goal); the renderer's per-preview depth mapping serves the observer view.

## Precision budget

With 32-bit float depth, two surfaces are distinguished once their relative distance exceeds about `2^-24 · ln2 · log2(rMax / T) · (1 + T / r)` (about 3e-6 for near 0.1 m and far 1e18 m). Below `T` the float's own relative precision applies.

## Observer view

- Authoring: a `RadialObserver` component (Enabled, inset rectangle as fractions of the surface, FovYRadians, NearZ, ShowCenterCamera) on a node whose Transform is the observer. `RadialObserverSync` turns each enabled one into a camera preview (at most three) while the active camera is radial; `EngineHost` submits the frame's list and clears it when none remain.
- Centre: the active radial camera `C`. Eye: the observer's Transform `E`, made camera-relative through the runtime's single Q64.64-to-float conversion.
- Clip position: `mul(float4(dir · F(|P − C|) − (E − C), 1), ObserverRotationProjection)`. Lighting uses the physical position and the physical observer eye; interpolation is perspective in the mapped space (an approximation for visualization).
- Projection far: `|E − C| + 1.05 · T · G(rMax)`.
- Drawn in the game runtime only, as one camera preview pass. With `ShowCenterCamera` the overlay pass draws the centre camera's view frustum (5 % of the compressed world's radius long) inside the inset with the observer's view-projection. Not drawn when an editor camera owns the previews.

## Non-goals

Omni's Threshold curve; vertex warping for ordinary cameras; mantissa/exponent constant packets; shader/material conversion and restore ledgers; BRG sidecars; culling (Stage draws every object; a future visibility path must test near/far on radial distance and keep undecidable objects visible); motion vectors (a future TAA needs current and previous depth-mapping packets); the orthographic log grid; an editor toggle; editor navigation at astronomical scale; full-screen observer views and picking or gizmos inside them; interplanetary shadows; Early-Z for radial camera thumbnails (they still write plain `SV_Depth`).

## Verification

- CPU: `RadialDepthTests`.
- Standard regression: `StandardModeGoldenFrameTests` plus the existing exact WARP pixel tests.
- Shader ABI: `ShaderGraphCompilerTests`.
- GPU: `RadialDepthGpuIntegrationTests`, `ShaderArtifactHotReloadTests.RenderServer_DebugHotReload_RebuildsTheRadialPipelines`.
- Runtime and editor: `SceneCameraTests`, `SceneJsonRuntimeValuesTests`, `EditorRuntimeViewTests`, `RadialObserverTests`.
- Visual Stage `rendering.radial-depth` (WARP required).
