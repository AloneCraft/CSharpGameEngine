# ADR-0005: glTF Base-Color Texture Display

## Status
Accepted

## Context
Milestone 3 ([[ADR-0004-Gltf-Model-Viewer-Vertical-Slice]]) got a real glTF mesh importing and
rendering with fixed-camera Lambertian shading, but explicitly deferred materials/textures/PBR -
`Box.glb` renders as a flat gray box regardless of its actual material. Given a choice among the
deferred items (texture display, camera controls, or renderer consolidation), the user chose
**texture display** as the single next goal: a flat-gray box reveals nothing about what a real Fab
asset actually looks like. This ADR documents the decisions made adding base-color texture support,
in the same "verify every API against real source before writing code, build the smallest slice
that gets something real on screen, fix what's empirically wrong, document afterward" spirit as
ADR-0003/ADR-0004.

Scope is deliberately narrow: base-color texture only, applied via `sampled.rgb * baseColorFactor`.
No metallic/roughness/normal/emissive/occlusion maps, no full PBR, no camera controls, no
`TriangleRenderer`/`ModelRenderer` consolidation - all remain deferred.

## Decision

### 1. Image decode: WIC (Windows Imaging Component) via raw TerraFX COM interop
Chosen over `System.Drawing.Common` (Microsoft explicitly discourages it for new code and it would
be a new `PackageReference`) and `SixLabors.ImageSharp` (v4.0+ requires a license-key registration
step even on the free tier). WIC needed zero new packages - the bindings already exist inside
`TerraFX.Interop.Windows`, already referenced by `Graphics.D3D12`. The decoder
(`WicImageDecoder.Decode(byte[]) -> WicDecodeResult`) lives inside `CSharpGameEngine.Graphics.D3D12`
itself, not a new project - it is tightly coupled to "getting a texture onto the GPU," not a
general Windows/windowing concern that belongs in `Platform.Windows`. It follows the same
error-contract idiom as `HlslCompiler`/`GltfImporter`: bad/corrupt/unrecognized input data always
produces a `Failed(...)` result, never an exception; only a genuine environment failure (the WIC
factory itself unavailable) would throw, mirroring `HlslCompiler`'s `CreateDxcInstance`.

The call chain (`CoInitializeEx` → `CoCreateInstance(CLSID_WICImagingFactory)` →
`SHCreateMemStream` → `CreateDecoderFromStream` → `GetFrame(0)` → `CreateFormatConverter` →
`Initialize(..., GUID_WICPixelFormat32bppRGBA, ...)` → `GetSize` → `CopyPixels`) worked exactly as
verified from TerraFX source before writing any code - no symbol names, vtable indices, or enum
values needed correction. `IWICBitmapFrameDecode`/`IWICFormatConverter` both exposed
`GetSize`/`CopyPixels` directly (no cast to `IWICBitmapSource*` needed), confirming the same
`[NativeInheritance]` pattern already relied on elsewhere in this codebase (`IDxcResult` :
`IDxcOperationResult`). `CoInitializeEx` returning `RPC_E_CHANGEDMODE` (already-initialized on this
thread with a different concurrency model) is treated as non-fatal - skip `CoUninitialize` in that
case - and this path was never actually observed in testing.

### 2. `Import.Gltf` exposes only raw encoded bytes, no decoding
`ImportedMesh` gained `IReadOnlyList<Vector2> Uvs`, `byte[]? BaseColorTextureBytes` (the untouched
PNG/JPEG bytes straight from the glTF buffer view), and `Vector4 BaseColorFactor`. `SharpGLTF.Core`
has zero image-codec dependency (confirmed via its `.nuspec` - the `net10.0` dependency group is
empty), so `Import.Gltf` stays plain `net10.0` with no new package and no Windows coupling. All
decoding happens in `Graphics.D3D12`; `ModelRenderer.Create` grows with plain-CLR parameters
(`IReadOnlyList<Vector2> uvs, byte[]? baseColorTextureBytes, Vector4 baseColorFactor`) rather than
gaining a `ProjectReference` to `Import.Gltf` - the same rule ADR-0004 already established between
`Graphics.D3D12` and `Platform.Windows`.

`MaterialChannel.Parameter` is obsolete in the installed SharpGLTF version; `.Color` is used
instead (`baseColorChannel?.Color ?? new Vector4(1f, 1f, 1f, 1f)`), which resolves to the same
default-white-when-absent value without a `TreatWarningsAsErrors`-breaking obsolete-API warning.

### 3. Missing NORMAL and missing TEXCOORD_0 share one expansion pass
UV extraction rides along the existing NORMAL-usability branch from ADR-0004 §2, rather than
introducing a second, independently-triggered expansion pass. When `NORMAL` is usable, UVs are
used as-is, or zero-filled with their own `Warning` diagnostic if `TEXCOORD_0` is absent/
inconsistent (independent of the NORMAL warning - both can fire together, as they do for
`Triangle.gltf`, which has neither). When `NORMAL` is missing and the mesh is expanded into 3
unshared vertices per triangle, the same per-triangle loop that already writes expanded
positions/normals also writes `uvs[baseIndex+k] = uvsUsable ? rawUvs[originalIndex] : Vector2.Zero`,
indexed by the pre-expansion triangle vertex index. This keeps array lengths consistent by
construction with no separate pass.

### 4. Always-textured single render path, no branching PSO/shader
Every mesh gets a bound SRV, including `Box.glb` (which has no texture at all, only a real red
`baseColorFactor` of `(0.8, 0, 0, 1)`, confirmed by parsing its JSON directly) - `ModelRenderer`
synthesizes a procedural 1x1 white RGBA texture as the fallback whenever the imported bytes are
null, empty, or fail to decode (`TextureLoadError` records why, non-fatal). This keeps exactly one
PSO, one root signature, one shader path, with zero runtime conditionals for "does this mesh have a
texture." `TextureLoaded`/`TextureLoadError` are exposed as public properties and printed by the
`Extension` CLI's diagnostic output, matching the existing shader-compilation diagnostic
convention.

### 5. `TransformCB` grows in place; CBV visibility becomes `ALL`, not `VERTEX`
Rather than add a second constant buffer, the existing root-CBV buffer (`register(b0)`) grew from
just `row_major float4x4 MVP` (64 bytes) to also carry `float4 BaseColorFactor` (80 bytes total,
still well under the 256-byte buffer already allocated). Because `PSMain` now reads
`BaseColorFactor` out of `TransformCB`, the root parameter's `ShaderVisibility` had to change from
`D3D12_SHADER_VISIBILITY_VERTEX` to `D3D12_SHADER_VISIBILITY_ALL` - otherwise
`CreateGraphicsPipelineState` fails root-signature validation because the pixel shader references a
root parameter the root signature doesn't expose to that stage. This was flagged as a real,
derived requirement during planning and held up exactly as expected on the first real run - no
empirical correction was needed here.

### 6. Texture upload: default-heap resource, ephemeral upload-heap staging, row-by-row copy
`CreateTexture` mirrors the existing `CreateDepthBuffer`/readback patterns already in
`ModelRenderer`: a `D3D12_HEAP_TYPE_DEFAULT` `DXGI_FORMAT_R8G8B8A8_UNORM` 2D resource (initial state
`COPY_DEST`, no clear value), an ephemeral `D3D12_HEAP_TYPE_UPLOAD` staging buffer sized via
`GetCopyableFootprints`, a `CopyTextureRegion`, then a barrier to `PIXEL_SHADER_RESOURCE`. The copy
into the staging buffer is done **row by row**, not as one bulk block copy: `GetCopyableFootprints`
row pitch is 256-byte-aligned, while WIC's decoded buffer is tightly packed
(`stride = width * 4`) - these differ whenever `width * 4` isn't already a multiple of 256, which
is true for the Cesium logo texture used in testing. This one-off upload reuses the same
`_commandList`/`_commandAllocators[0]`/`WaitForGpuIdle()` already used by `Initialize` and
`Resize()` outside the per-frame loop - no new command-list machinery.

The SRV lives in a new `D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV` heap with exactly one descriptor
and `D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE` set (every prior heap in this codebase - RTV, DSV -
was `FLAG_NONE`, since none needed to be bound directly by shaders). The sampler is a **static
sampler** embedded directly in the root signature (`MIN_MAG_MIP_LINEAR`, `WRAP`/`WRAP`/`WRAP`,
pixel-visible, register `s0`) rather than a separate sampler heap - simplest option for a single,
fixed sampling mode.

### 7. Texture created once at `Initialize`, untouched by `Resize()`
Unlike the depth buffer (which must be recreated at the new size), the texture does not depend on
window dimensions, so `Resize()` is otherwise unchanged from ADR-0004.

### 8. sRGB/gamma correction deliberately skipped
The texture is created and sampled as plain `DXGI_FORMAT_R8G8B8A8_UNORM` with no gamma correction
anywhere in the pipeline, consistent with the renderer having no tonemapping at all today. Colors
render slightly washed out relative to a spec-correct (sRGB-aware) renderer - visible but minor in
the real screenshot. Documented here as a known, deliberate gap, not a bug, and left for a future
milestone alongside the rest of the deferred PBR work.

### 9. New test fixture: `BoxTextured.glb`
Downloaded from the Khronos `glTF-Sample-Assets` repository
(`Models/BoxTextured/glTF-Binary/BoxTextured.glb`) alongside the already-bundled `Box.glb` - same
license family (CC-BY 4.0 International with Trademark Limitations, plus a separate Cesium
trademark/logo restriction on the embedded image), recorded in
`assets/TestFixtures/Gltf/NOTICE.md`. Unlike `Box.glb`, it has `NORMAL`, `POSITION`, and
`TEXCOORD_0` all present (no vertex expansion needed) and an embedded PNG `baseColorTexture` (no
explicit `baseColorFactor`, so it defaults to the glTF spec default of `(1,1,1,1)`). Both fixtures
together exercise the two ends of the texture path: a mesh with no texture and a non-default
factor (`Box.glb`, regression case), and a mesh with a real texture and the default factor
(`BoxTextured.glb`, the new case). `Extension` bundles both as `Samples/Box.glb` and
`Samples/BoxTextured.glb` - `Box.glb` remains the plain-material regression sample.

## Consequences
- `Import.Gltf` remains platform-neutral and gains no new package dependency.
- `Graphics.D3D12` still has no `ProjectReference` to `Import.Gltf` - only `Extension` and
  `Graphics.D3D12.Tests` reference both, unchanged from ADR-0004.
- `Graphics.D3D12` now performs COM interop (WIC) in addition to the D3D12/DXGI/DXC interop it
  already did - a new category of native surface for this project, but through the same raw
  TerraFX-binding style already used everywhere else in the codebase.
- The vertex layout, root signature, and PSO changed shape (3rd input element, 2 root parameters
  instead of 1, a static sampler, a shader-visible SRV heap) - `TriangleRenderer` is untouched;
  this widens the gap between `TriangleRenderer` and `ModelRenderer` further, compounding the
  accepted "duplicate now, consolidate later" debt from ADR-0004 §3.

## Empirically discovered
Everything specified during planning (WIC symbol names/vtable indices, the `CoInitializeEx`
non-fatal-`RPC_E_CHANGEDMODE` handling, the row-pitch-alignment row-by-row copy requirement, the
CBV-visibility-must-become-`ALL` requirement, `Box.glb`'s real `baseColorFactor`) held up exactly
as verified against real source/data before implementation - no further empirical correction was
needed on the first real GPU run. Both the WARP `ModelGpuIntegrationTests` cases (the existing
`Box.glb` regression and the new `BoxTextured.glb` case) passed on the first attempt, with 0 D3D12
validation errors. The windowed run confirmed visually: `BoxTextured.glb` shows the real Cesium
yin-yang logo texture wrapped correctly across all six faces with per-face Lambertian shading, and
`Box.glb` still renders as a plain red box (the white 1x1 fallback texture multiplied by its real
`(0.8, 0, 0, 1)` factor), not white - confirming the always-textured fallback path is correct.

## Unresolved / explicitly deferred
- No metallic/roughness/normal/emissive/occlusion maps - base color only, no full PBR.
- No sRGB-correct color pipeline (see §8).
- No mipmapping (`D3D12_TEX2D_SRV.MipLevels = 1` throughout) - fine for a single static close-up
  view, would show aliasing/shimmer under camera movement or distance, neither of which exist yet.
- No texture caching or reuse across meshes - each `ModelRenderer` instance decodes and uploads its
  own texture independently; irrelevant while the viewer only ever loads one mesh at a time.
- No user camera controls (still fixed, auto-framed only, per ADR-0004 §6).
- `TriangleRenderer`/`ModelRenderer` consolidation into a shared render abstraction is still not
  attempted (see Consequences above).
- No real VSIX/VS-SDK project yet (see ADR-0004 §7).
