# ADR-0004: glTF Model Viewer Vertical Slice

## Status
Accepted

## Context
Milestone 2 ([[ADR-0003-Windows-D3D12-Vertical-Slice]]) proved the raw path Win32 window → D3D12
device → DXC-compiled HLSL → one hardcoded flat-colored triangle. The next goal was to get a
**real 3D asset purchased on Fab** (Epic's asset marketplace) imported and visually confirmed on
screen, on the way to a future Visual Studio extension. The user explicitly prioritized a visible
result over foundation work for this round: proper scene graphs, materials, PBR textures, camera
controls, and a real VSIX are all deliberately deferred. This ADR documents the decisions made
building that slice, in the same "build small, verify for real, document afterward" spirit as
ADR-0003.

## Decision

### 1. Model format: glTF/GLB via SharpGLTF.Core
`SharpGLTF.Core` (v1.0.6, MIT license) was chosen over the alternatives considered:
- **AssimpNet** (wraps the native Assimp library) - rejected: pulls in a native dependency,
  which conflicts with this milestone's "zero native deps for the importer" goal (the importer
  itself, `CSharpGameEngine.Import.Gltf`, targets plain `net10.0` with no Windows/D3D coupling at
  all, mirroring how `CSharpGameEngine.Core` stays platform-neutral).
- **Hand-rolled glTF parser** - rejected: reinvents what SharpGLTF already provides; SharpGLTF is
  pure managed, explicitly multi-targets `net10.0` directly, and its low-level `Schema2` API
  (`ModelRoot.Load`, `GetVertexAccessor("POSITION"/"NORMAL")`, `GetTriangleIndices()`) gives raw
  per-primitive vertex/index data directly with no additional ceremony - the higher-level
  `SharpGLTF.Toolkit` (`IMeshBuilder`) package was not needed and is not referenced.

### 2. Missing NORMAL attribute: flat per-face normal synthesis, not a per-vertex hack
A vertex shared by multiple triangles can only hold one `NORMAL` value, so when a primitive has no
`NORMAL` attribute (or a malformed one), `GltfImporter` cannot patch normals in place. Instead it
expands the mesh: every triangle becomes 3 unshared vertices carrying one computed flat face
normal (`Normalize(Cross(pb-pa, pc-pa))`, with a `Vector3.UnitY` fallback for degenerate/zero-area
triangles). A `Warning` diagnostic is emitted; the import still succeeds. This keeps
`ModelRenderer` uniform - it always receives an indexed triangle list with matching
positions/normals, regardless of which import path produced it.

### 3. `ModelRenderer` is a new class, not an edit to `TriangleRenderer`
ADR-0003 §5 already anticipated this exact fork: *"the next milestone that actually needs
multiple draw calls or resources is the right place to discover [a generalized render
abstraction's] shape."* Rather than design that abstraction now, `ModelRenderer` duplicates
`TriangleRenderer`'s frame-context/fence-sync/swap-chain scaffolding (including the
`ResizeBuffers`-desyncs-fence-values fix from ADR-0003 §8) and adds what a real mesh needs on top.
`TriangleRenderer`, `TriangleGpuIntegrationTests`, and the `triangle` CLI command are untouched.
This is the same "duplicate now, consolidate later" tradeoff ADR-0003 itself already made for the
vertex buffer; a shared render abstraction remains a deliberately open question for a later
milestone once there is more than one real consumer shape to generalize from.

### 4. Depth buffer added (was explicitly out of scope for the triangle)
ADR-0003 §5 called out "no depth buffer" as correct for a single unindexed triangle, which can
never overlap itself. A real closed mesh (e.g. a cube) has front and back faces at different
depths from any viewpoint; without depth testing, triangle submission order alone would produce
visibly wrong overlap. `ModelRenderer` adds a `DXGI_FORMAT_D32_FLOAT` depth buffer in a
`D3D12_HEAP_TYPE_DEFAULT` resource with its own 1-entry DSV heap, uses
`D3D12_DEPTH_STENCIL_DESC.DEFAULT` unmodified (depth test + write enabled, unlike
`TriangleRenderer`'s explicit `DepthEnable = FALSE`), and sets `DSVFormat` on the PSO. `Resize()`
now also releases and recreates the depth buffer at the new size - this path has no
`TriangleRenderer` precedent and is covered by `ModelGpuIntegrationTests`' resize case.

### 5. Transform via a root CBV (root descriptor), not a descriptor table/heap
The model-view-projection matrix is uploaded through a single root parameter
(`D3D12_ROOT_PARAMETER_TYPE_CBV`, vertex-shader visibility, register `b0`), bound per frame via
`SetGraphicsRootConstantBufferView`. This is the simplest option available in D3D12 for a single
constant buffer and avoids needing a CBV/SRV/UAV descriptor heap for this milestone. The backing
resource is a single (not per-frame-in-flight double-buffered) upload-heap allocation, written
once at initialization and again inside `Resize()` (both call `WaitForGpuIdle()` first, so there
is no read/write race) - safe because the camera is static and the transform never changes
mid-flight between the two swap-chain frame contexts.

### 6. Camera: fixed, bounding-sphere auto-framed, no user controls
The camera has no user input this milestone. Given the mesh's bounding sphere (center + radius,
computed by the importer), the renderer places a fixed eye position along a 3/4 angle
(`Normalize(1, 0.75, 1)`) at a distance chosen so the sphere fits the vertical field of view with
margin, and builds `View`/`Projection` via `System.Numerics.Matrix4x4.CreateLookAt` /
`CreatePerspectiveFieldOfView`. The HLSL side uses `row_major float4x4` and `mul(vector, matrix)`.
**This worked correctly on the first real GPU run** (WARP and hardware both passed pixel
verification immediately, and the rendered Box was confirmed visually via a real screenshot) -
the row/column-major and winding-handedness risk flagged during planning did not materialize, so
no empirical correction was needed. Backface culling stays disabled
(`D3D12_CULL_MODE_NONE`, matching ADR-0003 §7) - with a depth buffer, a closed mesh no longer
*requires* culling for correctness, only for performance, which is out of scope here.

One real DXC quirk was found and fixed: a `static const float3` HLSL global initialized via a
function call (`normalize(float3(...))`) made DXC's `-Zi` debug-info generation emit a spurious
`!dbg attachment points at wrong subprogram for function` diagnostic to stderr (harmless -
`Shader compilation: OK`, 0 D3D12 validation errors, correct render either way, but noisy). Fixed
by keeping the global as a plain literal (`LightDirectionRaw`) and calling `normalize()` inline
inside `PSMain` instead.

### 7. Extension project is a placeholder exe, not a real VSIX
`tools/CSharpGameEngine.Extension` targets `net10.0-windows10.0.19041.0`, the same shape as
`CSharpGameEngine.TestHost` - not a Visual Studio SDK/VSIX project. Per ADR-0001, L8 ("Visual
Studio Authoring Environment") is explicitly deferred, and a real VSIX would need `net472`-ish
constraints incompatible with this repo's `net10.0` path. `Extension` is positioned as "the future
extension's core logic host": it composes `Platform.Windows` + `Graphics.D3D12` + `Import.Gltf`
exactly the way `TestHost` composes `Platform.Windows` + `Graphics.D3D12`, and is the project a
real VSIX shell would eventually wrap. A minimal VSIX shell that does exactly this (launches
`Extension` from a VS command) was built in [[ADR-0006-VSIX-Extension-Skeleton]].

### 8. `UserGameProject` is a folder, not a `.csproj`
Nothing in this milestone consumes `UserGameProject` programmatically - the viewer takes
`--model <path>` as a plain string argument. A permanently-empty build unit that must still
compile clean under `TreatWarningsAsErrors=true` for zero functional payoff was not worth adding
yet; `tools/CSharpGameEngine.Extension` already *is* the "engine is consumable from an external
project" signal this milestone needs. `UserGameProject/Assets/Fab/` is where a user places their
own purchased Fab content manually - Claude cannot access Fab.com (paid marketplace, no
credentials, and downloading third-party commercial content is a user action) - and that content
must not be committed/redistributed. Revisit a real `.csproj` once there is actual gameplay code
or a real VS extension needs a concrete project to load.

### 9. glTF asset consumed for real verification
`tools/CSharpGameEngine.Extension` bundles the Khronos-provided `Box.glb` sample
(`assets/TestFixtures/Gltf/Box.glb`, CC BY 4.0, © 2017 Cesium - attribution recorded in
`assets/TestFixtures/Gltf/NOTICE.md`) as `Samples/Box.glb`, giving an out-of-the-box smoke test
that needs no manually-placed Fab content. The same fixture is reused by
`CSharpGameEngine.Import.Gltf.Tests` and `ModelGpuIntegrationTests`.

## Consequences
- `Import.Gltf` stays platform-neutral (plain `net10.0`, no `ProjectReference` to `Core` or any
  Windows-touching project), matching the existing pattern where `Compiler.Hlsl` also has no
  dependency on `Core`.
- `Graphics.D3D12` gains no `ProjectReference` to `Import.Gltf` - `ModelRenderer.Create` takes raw
  `Vector3`/`uint` arrays, the same rule ADR-0003 already applied between `Graphics.D3D12` and
  `Platform.Windows` (`nint` hwnd, not `Win32Window`). Only `Extension` (and, test-only,
  `Graphics.D3D12.Tests`) references both `Graphics.D3D12` and `Import.Gltf`.
- `TriangleRenderer` and `ModelRenderer` now duplicate a substantial amount of D3D12 boilerplate.
  This is accepted debt, not an oversight - see §3.

## Unresolved / explicitly deferred
- No scene graph, no multiple objects, no materials/textures/PBR (Fab assets have textures; this
  milestone deliberately does not load them). Base-color texture display is addressed in
  [[ADR-0005-Gltf-BaseColor-Texture-Display]]; full PBR (metallic/roughness/normal/emissive/
  occlusion) remains deferred.
- No user camera controls (fixed, auto-framed only).
- No real backface culling (still `D3D12_CULL_MODE_NONE`).
- Geometry still uploaded through an upload heap (no default-heap + copy-queue path) - the same
  accepted tradeoff ADR-0003 §6 made for the triangle's vertex buffer, now also applied to the
  index buffer.
- The transform constant buffer is a single non-per-frame-in-flight resource; fine while the
  camera is static, but would need double-buffering if per-frame updates (e.g. an animated camera)
  are added later.
- `TriangleRenderer`/`ModelRenderer` consolidation into a shared render abstraction is not
  attempted.
- No real VSIX/VS-SDK project yet (see §7).
