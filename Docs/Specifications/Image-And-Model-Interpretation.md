# Image and model interpretation

Status: PartiallyImplemented; standalone import and frontend contracts TestVerified. Approved 2026-10-26. See M0126 for implementation evidence and runtime gates.

Sources remain authoritative and use <complete-filename>.meta canonical JSON schema 1. One AssetId owns explicitly registered outputs; output identity uses kind and immutable key, never pixels/settings/order/display name. cge.image handles PNG/JPG/JPEG. Existing metadata wins; a typed drop target then a delimited filename suffix supplies only initial defaults, otherwise Color. Pixel normal detection is advisory only.

Color/emission are sRGB (alpha linear); normal/mask/scalars/packed data are linear. Normal uses tangent-space +Y; DX flips green. ORM/ARM = AO,Roughness,Metallic; MRA = Metallic,Roughness,AO. Explicit smoothness conversion inverts roughness. Defaults AO=1,roughness=1,metallic=0. Normal mips renormalize vectors. Image outputs retain encoded original bytes without GPU creation/mips. Outputs are registered lazily and coexist.

Image Inspector supports default/output addition/settings/preview/apply/revert. Destructive output edits require reference review. Viewing/building never creates source sidecars. Explicit registration/import writes metadata through the authoring service; move/rename preserves IDs and copy allocates a new one. Publish only complete outputs and preserve the previous manifest on failure. Standalone CLI and VS use the same service.

FBX is a composite model: nodes and skins form Model, mesh/material/skeleton/clip are subassets; bones belong to skeletons. Import only data actually present, take-based clips, Generic rig by default. Humanoid, root motion, scene lights/cameras, colliders and extraction require explicit settings. Embedded images use material roles.

Layers: Contracts L2 owns DTO/settings/identity, Content L6 owns payloads and metadata, Import L7 decodes/transforms/publishes, VSIX L8 only presents and transports. No provider SDK objects cross these boundaries. Generated image members are typed output groups with Default. Existing references require syntax/symbol migration and generated files are never edited.

Acceptance: filename/target/default precedence and advisory detection; one PNG multiple output kinds; correct linear channels and normal mips; stable IDs after reordering/settings/pixel changes; static/rigged/multi-take/animation-only FBX; identity-preserving organization/native Undo/concurrent-edit guards; explicit old-image registration. Implementation and remaining gaps must be recorded separately in Conformance.

## Persisted options and identity

The schema remains `schemaVersion / assetId / sourceUri / importer / options`. The filename is `Wall.png.meta`, never `Wall.meta`. `options` is a string dictionary. Image keys are `image.defaultOutput` and `image.outputs.<i>.key/name/kind/usage/flipGreen/mipmaps/channel/occlusion/roughness/metallic/invertRoughness`. Kinds are `texture` and `image`; usages are `color/emission/normal/mask/metallic/roughness/ao/packed`. Only contiguous output indices are accepted. There are 1–32 registered outputs. Keys are immutable lower-case identifiers; display names and ordering do not affect identity. Keys that generate colliding C# members are rejected. Color space is derived from the declared usage and cannot accidentally enable gamma on data channels.

Output references combine the source AssetId with `SubAssetIdentity.Derive(Texture|Image,key)`. Generated members use the immutable key, not the editable display label. `<source>:<key>` selectors remain typed as TextureAsset or ImageAsset. The default member has the actual default output's type. A registered ImageAsset has no implicit conversion to TextureAsset. Legacy unregistered image fields remain TextureAsset until explicit registration; migration uses Roslyn symbols and syntax, never generated-code edits.

L6 payloads use bounded RGBA8 texture mip levels and separately bounded encoded PNG/JPEG ImageData. Maximum edge is 8192 and encoded source size is 128 MiB. Runtime ImageData loading retains encoded bytes; GPU upload is an explicit texture operation. Imported texture mips and color space are retained by RenderMaterialRegistry and D3D12 upload, avoiding a second mip-generation step.

## Standalone commands

Use `CSharpGameEngine.Import.AssetAuthoring.Cli` with the same service used by VSIX:

```text
image inspect <project-root> <source>
image register <project-root> <source>
image import <project-root> <source>
image apply <project-root> <source> <settings.json>
image register-tree <project-root> <folder>
model inspect <project-root> <fbx>
model register <project-root> <fbx>
model import <project-root> <fbx>
model apply <project-root> <fbx> <settings.json>
```

Inspect is read-only. Explicit `register-tree` creates legacy-equivalent Color outputs. Ordinary build/catalog generation reads existing metadata and does not register assets or create sidecars. Import without metadata is rejected; registration and settings application are explicit operations. Settings JSON uses the public ImageImportSettings/ModelImportSettings DTOs. Metadata publication still goes through AssetAuthoringService or the reviewed, conflict-checked file transaction service.

Artifacts live under `<project-root>/obj/cge-content/images` and `models`. CAS objects may be staged before completion; only the final per-AssetId manifest selects published outputs. A failed import retains the previous manifest. Image recooking reuses validated artifacts for unchanged source/profile pairs; changing one profile does not invalidate the identity or CAS output of another profile. Display-name/order changes do not recook image outputs. Source changes may invalidate all outputs.

## FBX adapter

Assimp is confined to L7. The engine-owned result contains all nodes, meshes, materials, shared skeleton/skins and take clips. Material roles create separate texture outputs from an embedded/shared image. Raw embedded pixels receive a deterministic PNG representation in cache; no files are extracted beside the FBX. Model options are `model.meshes/materials/skeleton/animations/rig` plus `model.clips.<i>.name/take/startFrame/endFrame`. Generic is the supported rig. Additional named frame ranges preserve the source Take and produce clips with interpolated boundary keys rebased to zero.

Humanoid/retarget/root-motion processors, scene camera/light generation, collision generation and external extraction are not implemented by this adapter; they must not be silently enabled. This addition does not finish the separate M0114 artifact-only runtime migration. The existing glTF/legacy source-reader path remains for its current consumers. Model subasset aliases use the existing `subasset.alias.<kind>:<name>` identity contract.

## Verification boundaries

Unit/integration tests exercise real PNG decode, channel conversion, normal mips, raw encoded-byte preservation, source/metadata conflict protection, ID-preserving transactions, semantic catalog migration and real static FBX publication. Synthetic Assimp scenes exercise shared skins, multiple Takes, frame splitting and animation-only content. Real skinned/multi-Take FBX fixtures and the complete Visual Studio shell drag/drop/native linked Undo/DPI matrix require separate runtime evidence; successful compilation is not that evidence.


## Current frontend and operation limits

Image/FBX reimport is enabled in the Inspector. A cache-only last-attempt diagnostic produces ImportFailed and retains the last successful manifest; successful reimport clears the diagnostic. New filename-based registration uses suffix inference, while the explicit legacy `register-tree` operation requests Color. Existing metadata always wins. CLI metadata registration does not edit C# source: use the Inspector's semantic migration review for already referenced legacy image fields. Generated source is never hand-edited.

Saved image usage/key/kind cannot be silently changed in the Inspector. Add a new output, then review replacement/removal. Unreferenced outputs can be removed without a replacement; references block that operation. Default references are made explicit when needed to preserve their prior interpretation. Output refactoring fails before mutation for a read-only source or another loaded project owner. Arbitrary dynamically constructed references cannot be completely analyzed.

The current FBX view shows subasset groups in the Inspector; model thumbnails and full catalog/runtime integration remain under M0114. Advanced optional processors are not implemented. Actual native file/text linked Undo and shell drag/drop are code-integrated but require real-shell acceptance evidence.
