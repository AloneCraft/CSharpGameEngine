# ADR-0066 — Skeletal meshes, GPU skinning and locomotion animation
- Status: Accepted
- Date: 2026-09-30

## Context
The ThirdPerson template must show Unreal's mannequin (SKM_Quinn_Simple) animated like `ABP_Unarmed`: Idle, Walk and Jog blended by speed (BS_Idle_Walk_Run), and Jump → Fall loop → Land.

Observed facts:
- The Unreal export gives one skinned mesh with two primitives and two materials. It has one skin of 89 joints, with eight influences per vertex (JOINTS_0/1 and WEIGHTS_0/1, UNORM8).
- It gives six clips. Each clip has 89 TRS tracks addressed by joint name and no root motion.
- Imported to engine space, Quinn stands 1.80 m tall with its origin at the feet and faces −F.
- The Stage renderer draws interleaved 48-byte vertices from upload-heap buffers and has no per-object culling.
- The engine already runs compute passes with host-owned root signatures (auto exposure, SSAO, GI).

## Decision
1. **Import.** `GltfImporter.ImportScene` returns `ImportedScene.Skins` (`ImportedSkin`). Each skin has its joint names, parent indices, rest TRS, inverse bind matrices and the transform of the roots' parent.
   - Skinned primitives carry `ImportedSkinInfluences`: eight joints and eight weights per vertex, normalized.
   - `GltfImporter.ImportAnimations` returns joint tracks (translation, rotation and scale keys; LINEAR, STEP, and CUBICSPLINE as values).
   - Everything is converted with the existing mirror `C = diag(1,1,-1)`:
     - points: `C·p`;
     - rotations: `(−x, −y, z, w)`;
     - matrices: `C·M·C`.

   Skinned vertices therefore stay consistent with the mesh geometry, and the rest palette is identity (verified on Quinn to 7e-7).
2. **GPU skinning.** `StageMeshCreateInfo.Skin` marks a skinned mesh.
   - `StageSkinning` gives each skinned mesh an influence buffer and a default-heap skinned vertex buffer. Joints and weights are packed as 16-bit values; the rounding remainder goes to the heaviest weight, so the weights still sum to one.
   - The mesh's vertex buffer view is redirected to the skinned vertex buffer.
   - At the start of every frame, `Skinning.hlsl` rewrites that buffer by linear blend skinning with the latest palette. This happens before the shadow, GI-capture, AO and main passes, so every pass draws the same pose.
   - Palettes go through a per-frame-slot upload ring, so no in-flight data is rewritten (ADR-0069's descriptor lesson).
   - `RenderServer.SetSkinPalette` records the palette in the frame state, so device recovery replays the pose.
3. **Runtime.** The new portable shared project `Runtime.Animation` contains:
   - `Skeleton`, which computes the palette `InverseBind[j] × Model[j]` in row vectors.
   - `AnimationClip`, which binds a clip by joint name; joints the clip does not animate keep their rest pose.
   - `JointPose` blending: lerp for translation and scale, nlerp along the shorter arc for rotation.
   - `SkeletalAssetCache`, a single import per path that the renderer roster and the animators share.
   - Components:
     - `SkeletalMesh` (asset path);
     - `LocomotionAnimator` (clip paths, walk/jog speeds, blend time, and per-frame inputs Speed, Falling and VerticalSpeed);
     - their runtime companions.
   - Presentation systems: setup (order 11), locomotion (12) and the renderer sync (13). Evaluation uses wall-clock time capped at 0.1 s per frame. Animation is presentation data and never feeds the simulation.
4. **Locomotion graph.** `LocomotionStateMachine` reproduces ABP_Unarmed.
   - Locomotion blends Idle → Walk → Jog along the blend space's forward column (3 and 6 m/s). The template turns the character toward its movement, so the direction axis stays at 0.
   - Walk and Jog share one normalized phase, so their steps stay in sync.
   - Leaving the ground enters Jump (while rising) or Fall.
   - Jump hands over to the looping Fall one blend time before it ends.
   - Touching down plays Land, which yields to Locomotion when it finishes or when moving faster than 1 m/s.
   - Each state change cross-fades for 0.2 s from a snapshot of the last output pose.
5. **Template.** The importer emits a `Mesh` child of the character.
   - Its Transform is Unreal's relative location converted to the character frame, (0, −0.89, 0).
   - Its rotation is Unreal's relative yaw minus 90°, which is 180° for Quinn. This matches the observed −F facing.
   - It carries `SkeletalMesh` (Quinn) and `LocomotionAnimator` with the six clips and the blend-space speeds.
   - A template system (`ThirdPersonAnimation`, the ABP event-graph equivalent) feeds the parent's `CharacterMovementState` into the animator.
   - The capsule stand-in of ADR-0067 is removed.

## Alternatives considered
- CPU skinning into per-frame upload buffers: about 46k vertices × 8 influences per frame on the CPU. It duplicates what the existing compute infrastructure does cheaply, and it scales badly with more characters.
- Skinning in the vertex shader: every pass (shadow cascades, GI probe capture, AO prepass, main) would repeat the skinning, and every pipeline would need a skinned variant.
- The full 2D blend space: with orient-to-movement the direction is always 0. The directional samples were therefore not exported.

## Consequences
- Each placement of a SkeletalMesh gets its own copy of the meshes, so poses are independent, at the cost of memory per character.
- There is no culling to invalidate. When culling arrives, skinned bounds will need a conservative joint-based bound.
- Clips are sampled on the CPU each frame (89 joints × up to three clips). This is negligible for one character.
- Out of scope: animation retargeting, root motion, additive layers, IK (Unreal's foot IK), montages, and GPU animation compression.

## Verification
- `GltfSkinAnimationImportTests` builds a synthetic skin with SharpGLTF.Toolkit and checks:
  - axis conversion and parents;
  - an identity rest palette;
  - normalized and remapped influences (including the flat-normal path);
  - key conversion.
- `AnimationRuntimeTests` checks:
  - the palette at rest and under a parent move;
  - linear, slerp, loop, clamp and step sampling;
  - the speed blend at each boundary;
  - Jump → Fall → Land with cross-fade values;
  - Land cancelled by moving, and walking off an edge into Fall.
- `SkinningGpuIntegrationTests` on WARP checks:
  - the bind pose before any palette;
  - a bent joint and a 50/50 vertex;
  - an unchanged normal;
  - the skin kept through `RenderServer` (fail-first: `CloneMeshes` dropped it);
  - weight quantization;
  - no debug-layer errors.
- The template was run on hardware. Quinn idles facing away from the camera and walks toward the pressed direction; see [M0091](../Milestones/M0091-Skeletal-Animation.md).

## Related features
F-L4-015 (this ADR), F-L0-005 (glTF import), F-L0-008 (compute), F-L5-006 (character, ADR-0067).
