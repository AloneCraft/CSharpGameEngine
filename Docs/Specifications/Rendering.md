# Rendering specification

- Status: Partially implemented
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0052](../Decisions/ADR-0052-Render-Graph-And-Preview-Boundary.md), [ADR-0058](../Decisions/ADR-0058-Visual-Shader-Graph-And-Artifact-Runtime.md), [ADR-0062](../Decisions/ADR-0062-Visible-Engine-Showcase-And-Deterministic-Input.md)
- Target layers: L0, L4, L6, L7, L8

## Scope and current baseline

This specification defines the target rendering contract. The Shader Graph compiler and artifact runtime are implemented; the target Render Graph, recovery state machine, and shared Preview Host remain specified work.

The observed baseline is a Windows D3D12 path: `StageRenderer`, `RenderServer`, the WARP triangle stage, directional shadows, HDR, ACES, bloom, and point, spot, area, and emissive lighting/shadow work. Those capabilities are retained as regression baselines. The work tracked here adds graph ownership, artifact provenance, snapshot extraction, cascade/IBL/exposure/AA policy, recovery, and a common preview boundary.

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

The following names are target contracts, not present implementation claims.

```csharp
public readonly record struct RenderPassHandle(uint Value);
public readonly record struct RenderResourceHandle(uint Value);
public sealed class RenderGraphBuilder;
public sealed record RenderResourceDescriptor;
public sealed record RenderWorldSnapshot;
public sealed record RenderQualityProfile;
```

`RenderGraphBuilder` accepts named pass/resource declarations. A pass declares each resource as read, write, or read-write; compilation topologically sorts the graph, derives resource transitions and queue ownership transfers, verifies lifetime ranges, and permits transient aliasing only for non-overlapping live ranges. L0 translates the compiled graph to D3D12 barriers and command lists.

`RenderWorldSnapshot` is the only rendering extraction input. ECS itself is never traversed by the renderer. Extraction orders renderable records by stable ID, then creates immutable camera, light, material, mesh, and instance data. The visibility path compares frustum, LOD hysteresis, and instance batching with a CPU reference oracle.

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

Preview/Import/Bake calls use the versioned host protocol described in [Editor-VSIX](Editor-VSIX.md). Cancellation, timeout, host crash, and version mismatch are visible state transitions, not retries that can appear successful.

## Visual and quality policy

The accepted baseline retains existing directional/local/area/emissive shadow behavior. New quality profiles add cascaded directional shadows, image-based lighting, exposure, anti-aliasing, and documented profile settings. A profile is data, not an untracked driver toggle. WARP is the required visual adapter; hardware GPU runs are additional evidence and cannot replace WARP.

## Immediate migration rule

Each feature implementation cuts over in its own change. It removes the replaced public API, preview renderer, artifact producer, and reader in that same change; it regenerates artifacts and leaves no compatibility reader. A source identity may be preserved through a one-shot converter only when its resulting canonical representation is written immediately.

## Diagnostics and failure handling

- Graph cycles, undeclared hazards, invalid handles, and alias lifetime overlap are L4 errors with pass/resource names.
- Shader compilation, reflection mismatch, and artifact checksum failures identify source/dependency provenance without falling back to runtime compilation.
- Device recovery records the prior state, native error, completed fence, artifact IDs rebuilt, and final state.
- Preview protocol failures identify request ID, protocol version, timeout/cancellation reason, and host process outcome.

## Performance and release evidence

Rendering performance stages use five warm-up iterations and thirty measured samples. They store median, p95, MAD, adapter policy, WARP/hardware choice, build identity, and machine fingerprint. Release evidence includes dependency closure, shader/artifact provenance, required WARP output, and unresolved diagnostic count.

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
| F-L0-009 | 5 | `rendering.device-recovery` | Visual / Rendering | 1800s | Legal state transitions and artifact-only rebuild. |
| F-L8-018 | 5 | `vsix.rendering-preview` | Visual / VSIX | 1800s | Shared host protocol and no duplicate renderer. |
| F-L7-023 | 6 | `rendering.performance` | Visual / Rendering | 7200s | Thirty-sample performance evidence. |
| F-L7-024 | 7 | `rendering.release` | Headless / Foundation | 7200s | Scoped release closure and required evidence. |

F-L7-022 and F-L6-004 are Implemented with executable stages. F-L0-008 remains Specified and is not a dependency of Shader Artifacts. Remaining rows retain their declared target status.


## M0060 artifact-only update

Stage, Model, and Triangle consume fixed-ABI CGESHDR1 artifacts only. Debug artifact changes exchange validated PSOs at a frame boundary and preserve last-known-good on failure; Release loads once and contains no watcher. Canonical Graph and editor contracts are defined in [Shader Graph](Shader-Graph.md).

## M0061 external Subgraph update

Stage resolves PBR, Shadow, PCSS, and ACES from explicit external `.cgesubgraph` inputs. Recursive dependency hashes participate in Artifact identity while the root semantic hash remains independently observable. Exact HLSL parity preserves the M0060 WARP baseline; Runtime continues to consume the same CGESHDR1 contract.

## M0064 visible Showcase update

The default `UserEcsProject` window contains the existing D3D12 gallery and a 360-DIP double-buffered GDI child dashboard. The child panel changes no GPU Shader ABI. It reports completed simulation state, ECS statistics, Shader Artifact identity/hot-reload diagnostics, Physics state, and separate Feature/Stage progress bars from the generated manifest.

Runtime adapter selection is externally controllable through `CGE_GRAPHICS_ADAPTER=warp|hardware|default`; `CGE_D3D12_DEBUG_LAYER=0|1` controls validation. Normal startup preserves hardware/default selection. The executable `showcase.visible-progress` Visual Stage requires WARP and records the combined 3D/dashboard image plus a machine-readable child-window title.
