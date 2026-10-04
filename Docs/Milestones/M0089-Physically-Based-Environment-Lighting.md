# M0089 — Physically based environment lighting for the ThirdPerson template
- Date: 2026-09-30
- Implementation: Complete for the chosen scope (sky, sky light, fixed and histogram auto exposure, filmic output, height fog, cascaded sun shadows, dynamic GI and SSAO); volumetric clouds were not in the scope and are not implemented
- Verification: TestVerified / WARP RuntimeVerified; Stage `rendering.environment-lighting`
- Decision: [ADR-0069](../Decisions/ADR-0069-Physical-Sky-Exposure-Shadows-And-Dynamic-GI.md)
- Intended contract: [Rendering — Environment lighting](../Specifications/Rendering.md#environment-lighting)
## Goal and scope
Light the ported UE5.8 `Lvl_ThirdPerson` the way Unreal does, as an engine system with scene Components, not template-specific tuning.
Implemented: environment Components, Hillaire atmosphere LUTs and sky composite, SH sky light, physical camera exposure, Unreal filmic output, exponential height fog, four sun cascades, a rasterized DDGI probe volume, SAO ambient occlusion of indirect light, and the ThirdPerson importer's emission of all of them. The importer also emits a GI volume fitted to the level.
Excluded: volumetric clouds, reflection probes, texture mipmaps, hardware-GPU certification and interactive VSIX verification.
Changed features: F-L4-014 (new), F-L5-001, F-L0-005.
## Observed behavior and fixes found by tests
- The first physical-sky frame was black. LUT readback showed the LUTs were correct. The Unreal importer passed the glTF light's travel direction as the direction toward the light, which put the sun below the horizon. The directional shadow camera also sat below the scene. Both were fixed with fail-first tests. An existing blocker-shadow test had been passing for the wrong reason, and its oracle was corrected.
- The CPU transmittance reference returned zero at altitude 0, because a ray starting exactly on the ground counted as blocked. Only rays below the local horizon can now hit the planet.
- After the sun fix, the Showcase's sun was 65000 lux on the legacy exposure and blew out; that value had only looked usable while the sun pointed into the sky. It is now 6 lux, balanced against the Showcase's EV100 0.5 camera and its ~10 lux local lights.
- The shader-maintenance script read the composed HLSL in text mode, which normalized CRLF and produced the wrong golden hash. It now reads bytes.
- Open-sky GI probes came out 7% brighter than the sky light. Clamping each sky sample's band-limited radiance at zero added the negative ringing toward the dark ground back as energy. Removing the per-sample clamp makes the open-sky test hold within 3%.
- The wall next to the central cylinder rendered at 30/255, where Unreal shows 118. The earlier hypothesis, missing Lumen bounce, was refuted. A ray cast of the exported level showed the surface is sunlit wall, and a shadow-less capture stayed dark. The cause was that the importer ignored `KHR_texture_transform`, so the baked atlas was sampled in its dark gutters. After the fix the wall renders at 109–111.
- Histogram auto exposure first metered a 100 cd/m2 emitter 1.0 stop low with compensation 1 and 1.7 stops low with 2; with compensation 0 it was correct. Before any histogram exists, the frame is exposed at the minimum EV100 (-10). The HDR target therefore clipped, the first histogram under-reported, and slow adaptation kept the error. Clipped pixels now fill a separate bin. The metered value keeps jumping to the target until a histogram without significant clipping arrives. The oracle now meters 9.64 ± 0.35 at every compensation.
- A first CPU-side design read the histogram back through a persistently mapped readback heap. On WARP one frame slot always read zero, and another design mapped a readback buffer per frame. Long WARP runs with the debug layer then sporadically reported DEVICE_REMOVED at a `Map` (up to 7 of 30 runs), with no debug-layer message and no DRED breadcrumb or page fault. Two sets of evidence isolated the cause:
  - `GetDeviceRemovedReason` after every frame stayed 0; the removal surfaced only at the next `Map`, even after an explicit GPU-idle wait, so `Map` reported a fault that had already happened on the GPU.
  - The removal also occurred without auto exposure (1 of 20 runs) in atmosphere scenes.
  The cause was `StageAtmosphere` rewriting the depth-SRV and HDR-UAV descriptors in the same shader-visible slots every frame, while the previous frame's sky composite could still be reading them. Writing them only when the targets change (and invalidating them on Resize) removed the failure: 0 failures in 16 runs of both long auto-exposure tests. Auto exposure itself moved entirely to the GPU, so the CPU no longer maps anything per frame.
- The first AO crease test point lay within 0.3 px of the box silhouette at half resolution. A readback showed correct occlusion elsewhere (a contact line of 0.46, open floor and sky at 1.0). The test now samples unambiguous points and checks the AO values directly.
## Commands and results
Commands executed from the repository root (WARP):
```text
dotnet test CSharpGameEngine.slnx -c Debug
dotnet test CSharpGameEngine.slnx -c Release
dotnet test tests/CSharpGameEngine.VSExtension.Tests -c Release
dotnet run --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj -c Debug -- run rendering.environment-lighting --configuration Debug --profile environment-lighting-m0089 --adapter warp
dotnet test tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~GlobalIllumination|FullyQualifiedName~AmbientOcclusion|FullyQualifiedName~EnvironmentComponent"
dotnet test tests/CSharpGameEngine.Import.Tests -c Debug -p:Platform=x64
python tools/ShaderGraphMaintenance/edit_stage_graph.py tools/ShaderGraphMaintenance/edits/<edit>.py
CGE_GRAPHICS_ADAPTER=warp CGE_AUTOMATION_FRAMES=80 CGE_AUTOMATION_CAPTURE=<png> CgeThirdPerson.exe
```
| Check | Debug | Release |
|---|---:|---:|
| Complete solution tests (before histogram auto exposure) | 2199 passed, 0 failed, 0 skipped | 2198 passed, 0 failed, 0 skipped |
| Complete solution tests (with histogram auto exposure) | 2204 passed, 0 failed, 0 skipped | Not run |
| Windows/WARP suite (included above) | 267 passed | 261 passed (before auto exposure) |
| VSIX suite (included above) | 1050 passed | 1050 passed |
| Stage `rendering.environment-lighting` | Passed | Not run |
| ThirdPerson capture, 80 frames on WARP | `d3d12Errors=0`, about 3 frames per second with 578 probes | Not run |
| ThirdPerson capture after importing Unreal's degenerate histogram range as AutoHistogram | 0 pixels differ from the fixed-exposure capture | Not run |
The first Release solution run failed to build `CSharpGameEngine.EditorPreview.Host` because another process held its `obj` DLL (CS2012, the lock already recorded by the audit). The VSIX suite, which depends on it, was then run on its own in Release and passed; every other suite passed in that first run.
## Unreal comparison (spawn camera, sRGB 0–255)
| Surface | Engine | Unreal 5.8 |
|---|---|---|
| Wall beside the cylinder | 109, 108, 105 | 118, 113, 106 |
| Wall front | 107, 107, 104 | 116, 110, 106 |
| Green platform near camera | 120, 206, 99 | 96, 221, 98 |
| Cylinder | 65, 179, 63 | 48, 189, 60 |
| Ramp top | 147, 144, 143 | 145, 140, 132 |
Remaining visible differences: Unreal's clouds; the fine grid of the world-aligned wall material (the baked texture aliases without mipmaps); and a slightly more saturated green in Unreal.
