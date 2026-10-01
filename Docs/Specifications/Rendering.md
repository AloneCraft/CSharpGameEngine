# Rendering specification

## Typed light submissions
[Typed Lighting](Typed-Lighting.md) and ADR-0064 replace unitless/kind-dependent public light payloads with DirectionalLightSubmission, PointLightSubmission, SpotLightSubmission and RectAreaLightSubmission. Rectangle submissions include forward and up so Transform roll is retained independently of physical width/height. Optional LightShadowSettings is a request; budget and GPU implementation remain backend-owned. Ordered reusable buffers, separate emissive records and completed-frame ownership are verified by TypedLightFrameTests, including exact WARP pixels after two device recoveries.

- Status: Partially implemented
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0052](../Decisions/ADR-0052-Render-Graph-And-Preview-Boundary.md), [ADR-0058](../Decisions/ADR-0058-Visual-Shader-Graph-And-Artifact-Runtime.md), [ADR-0062](../Decisions/ADR-0062-Visible-Engine-Showcase-And-Deterministic-Input.md), [ADR-0069](../Decisions/ADR-0069-Physical-Sky-Exposure-Shadows-And-Dynamic-GI.md)
- Target layers: L0, L4, L6, L7, L8

## Environment lighting
ADR-0069 adds an opt-in physically based environment path driven by `SkyAtmosphere`, `SkyLight`, `PostProcessVolume`, `ExponentialHeightFog` and `GlobalIlluminationVolume` Components. `EngineHost` converts them once per frame into `EnvironmentLightingSettings`. A missing Component keeps the compatibility behavior (clear colour, flat ambient, legacy fog, `2^-EV100`, linear ACES).

| Capability | Contract |
|---|---|
| Sky | Hillaire transmittance/multi-scattering/sky-view LUTs (`Atmosphere.hlsl`); far-plane composite; the brightest directional light is the sun and is attenuated by transmittance at the camera altitude. |
| Sky light | SH9 irradiance of the sky, scaled by `SkyLight.Intensity`, with a lower-hemisphere colour; diffuse plus an analytic-BRDF rough reflection. |
| Exposure/output | Physical camera `2^c / (1.2 * 2^EV100)` at a fixed EV100 or by GPU histogram auto exposure (10–90 % average, EV100 = log2 L + 3, clamped, SpeedUp/SpeedDown adaptation, state read at t15); Unreal filmic in AP1 plus sRGB OETF; bloom intensity. |
| Fog | Unreal exponential height fog, closed form, per-metre density and falloff. |
| Sun shadows | Four cascades (exponent-3 splits to 200 m), sphere-fit and texel-snapped, 512 px quadrants of atlas tile 0. |
| Dynamic GI | Camera-relative probe grid (≤ 4096); round-robin six-face captures with the lit Stage pipeline; octahedral irradiance (8×8) and distance moments (16×16) with hysteresis; Chebyshev-weighted eight-probe sampling at t12/t13. |
| Ambient occlusion | Half-resolution depth prepass, SAO plus depth-aware blur (`AmbientOcclusion.hlsl`) at t14; multiplies indirect light only. |

Invariants: open-sky GI probes reproduce the SH irradiance, AO leaves direct light bit-identical, histogram exposure with MinEv100 == MaxEv100 equals the fixed camera bit for bit, an invalid GI volume disables only GI, GI requires typed UAV loads of RGBA16F/RG16F, and no shader-visible descriptor that an in-flight frame may read is rewritten. Verified by Stage `rendering.environment-lighting` ([M0089](../Milestones/M0089-Physically-Based-Environment-Lighting.md)). Not implemented: volumetric clouds, reflection probes, texture mipmaps.

## Scope and current baseline

This specification defines the target rendering contract. The Shader Graph compiler, shader/material/texture artifact runtime, platform-independent Render Graph compiler, canonical RGBA8 D3D12 upload boundary, stable-ID snapshot visibility/LOD/instance batching, Render Graph quality profiles, D3D12 recovery state machine, and shared out-of-process Preview Host are implemented. The D3D12 graph executor remains subsequent work.

The observed baseline is a Windows D3D12 path: `StageRenderer`, `RenderServer`, the WARP triangle stage, directional shadows, HDR, ACES, bloom, and point, spot, area, and emissive lighting/shadow work. The opt-in environment path above (ADR-0069) adds a physical sky, sky light, cascaded sun shadows, dynamic GI and ambient occlusion. Those capabilities are retained as regression baselines. The work tracked here adds graph ownership, artifact provenance, snapshot extraction, cascade/IBL/exposure/AA policy, recovery, and a common preview boundary.

## Ownership and boundaries

| Layer | Owner | Contract |
|---|---|---|
| L0 | Win32, DXGI, D3D12 binding | Private native objects, queues, fences, barriers, resource allocation, and device diagnostics. D3D12 types do not cross L0. |
| L4 | Render Server and Render Graph | Engine-owned handles, graph declarations, queue/fence ownership, resource lifetime, and snapshot consumption. |
| L6 | Rendering artifacts | Versioned shader, reflection, PSO provenance, material, texture, mesh, and dependency artifacts. |
| L7 | Compiler/bake | DXC compiles HLSL to DXIL and emits reflection plus dependency hashes. Runtime shader compilation is prohibited. |
| L8 | Preview client | A versioned out-of-process client of the Render Server. The VSIX does not own a second renderer. |

Optional provider APIs remain behind L4 adapters. Native or provider types must not appear in ECS components, public runtime APIs, artifacts, commands, or events.

## Target public contracts

The Render Graph, material/texture, instance-visibility, and quality-profile subset of the following target contract is implemented in `CSharpGameEngine.Rendering`. The common Render Server owns the compatibility `RenderQualityPlan` for its configured dimensions and exposes it to protocol-v4 tooling strictly as a `DeclaredPlan`; this is not evidence that the later D3D12 graph executor or per-pass timing exists.

```csharp
public readonly record struct RenderPassHandle(uint Value);
public readonly record struct RenderResourceHandle(uint Value);
public sealed class RenderGraphBuilder;
public sealed record RenderResourceDescriptor;
public sealed record RenderWorldSnapshot;
public sealed record RenderQualityProfile;
```

`RenderGraphBuilder` accepts named pass/resource declarations. A pass declares each resource as read, write, or read-write; compilation topologically sorts the graph, derives resource transitions and queue ownership transfers, verifies lifetime ranges, and permits transient aliasing only for non-overlapping live ranges. L0 translates the compiled graph to D3D12 barriers and command lists.

`RenderWorldSnapshot` is the only input to the new visibility path; that path never traverses ECS. The implemented instance slice copies and orders camera-relative renderable records by stable ID, validates finite transforms/bounds and LOD ranges, and exposes immutable spans. Frustum selection and LOD state use caller-owned buffers. Batch construction performs an in-place deterministic heap sort by mesh, material, LOD, and stable ID, so warmed 100,000-instance selection and batching allocate nothing. Shipping Runtime and both VSIX preview callers now enter the engine-owned Render Server boundary; `StageRenderer` remains its private D3D12 backend and regression oracle rather than a VSIX-owned renderer.

## Data flow

```text
C# scene / runtime state
  -> stable-ID RenderWorldSnapshot
  -> Render Graph declaration and compile
  -> artifact-backed shader/material/resource resolution
  -> L0 D3D12 execution (queues, fences, barriers)
  -> WARP or optional hardware readback and diagnostics
  -> immutable result / preview snapshot
```

Shader Graph source flows through L7 only:

```text
canonical Shader Graph -> deterministic HLSL/source map -> DXC -> DXIL + reflection + dependency hash -> L6 artifact -> Render Server
```

An artifact mismatch, missing dependency, or invalid reflection layout fails before a GPU command list is submitted. A runtime request must never silently compile source HLSL.

## Device and preview state machines

The device state set is fixed: `Ready`, `Suspended`, `Recovering`, `Faulted`, and `Disposed`.

| From | Event | To | Required behavior |
|---|---|---|---|
| Ready | swap-chain resize or suspension | Suspended | Stop submission after the current fence boundary. |
| Suspended | resume | Recovering | Recreate device-dependent state from L6 artifacts only. |
| Recovering | all required resources rebuilt | Ready | Publish a fresh immutable diagnostics snapshot. |
| Ready or Recovering | unrecoverable native failure | Faulted | Reject new work with a typed diagnostic. |
| any non-Disposed | disposal | Disposed | Release queues, fences, and descriptors once. |

**Persistent versus per-frame inputs.** Frame constants are double-buffered per frame in flight, and every frame must render identically whichever slot it uses.
- **Persistent until changed:** ambient light, simplified fog, environment lighting, camera altitude, presentation mode and the `SetDirectionalLight` singleton. `RenderFrame` writes them into the slot it records.
- **Replay:** `RenderServer`'s recovery replay state carries these forward from earlier frames, so a value set once survives device recovery.
- **Per frame:** additive lights (`AddDirectionalLight`, point, spot, rect, emissive) are submitted every frame. Nothing is lit before the first submission; the renderer does not start from a default light.

**Scene view runtime view (ADR-0070).** For a built game the Scene view is drawn by the game's own `EngineHost` and `RenderServer` inside the preview host, so it uses the runtime's environment, lights, materials, skinning, exposure and GI. One session keeps its device and swap chain from Open to Close: an edit changes component values in the running world and never rebuilds device state. A gizmo drag sends the dragged roots' local transforms with each frame (`RenderPreviewFrame.TransformOverrides`), applied to their `Transform` the same way. The editor camera replaces the scene camera at the point where `LevelLoad` applies it, and the designer's selection and gizmos are drawn over the image.

Preview/Import/Bake calls use the versioned host protocol described in [Editor-VSIX](Editor-VSIX.md). Cancellation, timeout, host crash, and version mismatch are visible state transitions, not retries that can appear successful.

## Visual and quality policy

The accepted compatibility profile retains existing directional shadow, bloom, and ACES behavior. Immutable profiles select optional shadow cascades, image-based lighting, bloom, ACES, FXAA, and bounded camera/fixed EV100 exposure. Each selection creates a deterministic Render Graph topology; disabled features create neither pass nor resource. Compatibility, Balanced, and Cinematic are named presets, while the validated factory supports explicit per-feature policy without driver toggles. WARP is the required visual adapter; hardware GPU runs are additional evidence and cannot replace WARP.

## Immediate migration rule

Each feature implementation cuts over in its own change. It removes the replaced public API, preview renderer, artifact producer, and reader in that same change; it regenerates artifacts and leaves no compatibility reader. A source identity may be preserved through a one-shot converter only when its resulting canonical representation is written immediately.

## Diagnostics and failure handling

- Graph cycles, undeclared hazards, invalid handles, and alias lifetime overlap are L4 errors with pass/resource names.
- Shader compilation, reflection mismatch, and artifact checksum failures identify source/dependency provenance without falling back to runtime compilation.
- Device recovery records the prior state, native error, completed fence, artifact IDs rebuilt, and final state.
- Preview protocol failures identify request ID, protocol version, timeout/cancellation reason, and host process outcome.

## Performance and release evidence

Rendering performance stages use exactly five warm-up iterations and thirty measured samples in an isolated process whose Tiered JIT mode is explicitly disabled and recorded, preventing compilation transitions from contaminating the sample set. The required WARP pass records a hidden 1280x720 frame, triangle readback oracle, zero debug-layer errors, allocation, median, p95, MAD, build identity, raw-report SHA-256, and machine fingerprint; WARP elapsed time is correctness evidence, not a hardware performance claim. The same run measures camera-relative selection and deterministic batching for 100,000 active instances. Debug records timing but gates correctness and allocation only because its non-optimized code generation is not a shipping-performance oracle. Release gates that CPU workload at median 6 ms, nearest-rank p95 8.33 ms, and allocation p95 zero. An optional Release hardware pass gates 1280x720 presentation p95 at 16.67 ms. Release evidence includes dependency closure, shader/artifact provenance, required WARP output, and unresolved diagnostic count.

## Skeletal meshes and animation
ADR-0066 adds skinned meshes. A `SkeletalMesh` Component names a glTF with exactly one skin. The roster gives each placement its own copy of the meshes, and `StageMeshCreateInfo.Skin` carries eight 16-bit joint/weight pairs per vertex.

At the start of every frame, `Skinning.hlsl` rewrites each skinned mesh's default-heap vertex buffer from the latest palette. The palette is `InverseBind × JointModel` in row vectors and goes through a per-frame-slot upload ring. The shadow, GI-capture, AO and main passes then all draw the same pose. `RenderServer.SetSkinPalette` records the palette for device-recovery replay.

`Runtime.Animation` evaluates `LocomotionAnimator` (Unreal's ABP_Unarmed graph) on the CPU in the Presentation phase.

Invariants:
- The rest palette is identity.
- The quantized weights of a vertex sum to one.
- A mesh keeps its bind pose until a palette arrives.

Verified by Stage `rendering.skeletal-animation` ([M0091](../Milestones/M0091-Skeletal-Animation.md)).

## Stage oracle matrix

| Feature | Level | Stage | Mode | Timeout | Oracle |
|---|---:|---|---|---:|---|
| F-L7-022 | 1 | rendering.baseline | Visual / Rendering | 1800s | Executable WARP baseline and no debug-layer errors. |
| F-L4-004 | 2 | `rendering.render-graph` | Headless / Foundation | 900s | Topology, hazards, barriers, and aliases are valid. |
| F-L0-008 | 2 | `foundation.d3d12-compute` | Visual / Rendering | 1800s | Compute output equals CPU vector. |
| F-L6-004 | 3 | rendering.shader-artifacts | Headless / Foundation | 1800s | Executable DXIL/reflection/provenance/runtime boundary. |
| F-L4-005 | 3 | `rendering.materials` | Visual / Rendering | 1800s | Material and texture result matches reference. |
| F-L4-006 | 4 | `rendering.visibility` | Visual / Rendering | 1800s | Snapshot/visibility/LOD/instance oracle matches. |
| F-L4-007 | 4 | `rendering.quality-profiles` | Visual / Rendering | 1800s | Quality profile reference tolerance holds. |
| F-L4-015 | 5 | `rendering.skeletal-animation` | Visual / Rendering | 1800s | Skin/clip import in engine space, locomotion graph, and WARP compute skinning match analytic poses without debug-layer errors. |
| F-L0-009 | 5 | `rendering.device-recovery` | Visual / Rendering | 1800s | Legal state transitions and artifact-only rebuild. |
| F-L8-018 | 5 | `vsix.rendering-preview` | Visual / VSIX | 1800s | Shared host protocol and no duplicate renderer. |
| F-L7-023 | 6 | `rendering.performance` | Visual / Rendering | 7200s | Five warm-ups plus thirty WARP frame and 100,000-instance samples publish SHA-bound correctness/performance evidence. |
| F-L7-024 | 7 | `rendering.release` | Headless / Foundation | 7200s | Canonical dedicated-profile performance, shared-preview, Shader Artifact, source, and provenance closure. |

All Rendering Roadmap rows through F-L7-024 are Implemented with executable stages. Compute HLSL is compiled offline into the same fixed-ABI artifact envelope and runtime code does not load DXC. Material/texture input is checksum/dependency validated, decoded once above L0, and deduplicated by canonical content hash. Recovery owns one device generation, exact applied artifact closure, and reusable completed-frame snapshot; real WARP device removal and minimize/restore reproduce the pixel oracle without reading a corrupted source artifact or rewalking ECS. The VSIX packages the exact Preview Host closure and offline Stage artifact; the packaged apphost renders through WARP while source/package tests reject a duplicate renderer. Performance evidence preserves raw samples and canonical SHA-bound reports while separating WARP correctness from hardware timing claims. The Rendering release report reopens and binds the raw capture, dedicated-profile performance and preview results, both performance reports, one configuration-qualified Shader Artifact SHA-256, and the fixed source/toolchain closure without taking over product-wide packaging or immutable manifest ownership.


## M0060 artifact-only update

Stage, Model, and Triangle consume fixed-ABI CGESHDR1 artifacts only. Debug artifact changes exchange validated PSOs at a frame boundary and preserve last-known-good on failure; Release loads once and contains no watcher. Canonical Graph and editor contracts are defined in [Shader Graph](Shader-Graph.md).

## M0061 external Subgraph update

Stage resolves PBR, Shadow, PCSS, and ACES from explicit external `.cgesubgraph` inputs. Recursive dependency hashes participate in Artifact identity while the root semantic hash remains independently observable. Exact HLSL parity preserves the M0060 WARP baseline; Runtime continues to consume the same CGESHDR1 contract.

## M0064 visible Showcase update

The default `UserEcsProject` window contains the existing D3D12 gallery and a 360-DIP double-buffered GDI child dashboard. The child panel changes no GPU Shader ABI. It reports completed simulation state, ECS statistics, Shader Artifact identity/hot-reload diagnostics, Physics state, and separate Feature/Stage progress bars from the generated manifest.

Runtime adapter selection is externally controllable through `CGE_GRAPHICS_ADAPTER=warp|hardware|default`; `CGE_D3D12_DEBUG_LAYER=0|1` controls validation. Normal startup preserves hardware/default selection. The executable `showcase.visible-progress` Visual Stage requires WARP and records the combined 3D/dashboard image plus a machine-readable child-window title.
