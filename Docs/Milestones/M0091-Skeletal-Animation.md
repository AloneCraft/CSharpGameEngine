# M0091 — Skeletal meshes and locomotion animation for the ThirdPerson template
- Date: 2026-09-30
- Implementation: Complete for glTF skins and clips, GPU compute skinning, Unreal's ABP_Unarmed locomotion graph, and the template's animated Quinn
- Verification: TestVerified / WARP RuntimeVerified; Stage `rendering.skeletal-animation`
- Decision: [ADR-0066](../Decisions/ADR-0066-Skeletal-Meshes-And-Animation.md)
- Intended contract: [Rendering — Skeletal meshes and animation](../Specifications/Rendering.md#skeletal-meshes-and-animation)
## Goal and scope
Replace the ThirdPerson template's capsule with Unreal's SKM_Quinn_Simple, animated like ABP_Unarmed: Idle/Walk/Jog by speed, and Jump → Fall → Land.

Implemented:
- Skin and animation import.
- `Runtime.Animation` (skeleton, clip binding, blending, locomotion state machine, Components and Systems).
- `StageSkinning` with `Skinning.hlsl`.
- The palette path through `RenderServer`, including device-recovery replay.
- The importer's emission of the character's `Mesh` node from the Blueprint and blend-space values.

Excluded:
- Retargeting, root motion, IK, montages and skinned-bound culling.
- Directional (strafe) blend-space samples.
- Hardware-GPU certification and interactive VSIX verification.

Changed features: F-L4-015 (new), F-L5-006 (template character), F-L0-010 (automation input).
## Observed behavior and fixes found by tests
- **Unreal export contents.** The export has one skin of 89 joints with eight influences per vertex, and six clips of 89 tracks each without root motion. In engine space Quinn faces −F with its origin at the feet. Unreal's relative mesh yaw of 270° is therefore an engine yaw of 180° relative to a character that faces +F. The derivation is recorded in ADR-0066 and matches the observed facing.
- **Rest palette on the real asset.** Importing Quinn with the mirror conversion gives an identity rest palette to 7e-7.
- **Skin lost in RenderServer.** The first template run threw "Mesh 8 is not skinned": `RenderServer.CloneMeshes` copied the geometry but dropped the skin. A fail-first WARP test through `RenderServer` reproduced it. The clone and the mesh artifact identity now include the skin.
- **Physical input in automation runs.** An automation capture showed a top-down camera. Two identical scripted runs disagreed: one had already turned 90° and pitched down before its first scripted key. The visible window had received the user's raw mouse movement. Automation windows now ignore device input (`Win32WindowOptions.IgnoreDeviceInput`), with a fail-first test.
- **Spawn facing (fixed with the character, M0090).** The first frame now matches Unreal's spawn reference. Quinn idles with its back to the camera, and pressing D turns it to walk right.
## Commands and results
Commands executed from the repository root:
```text
dotnet test tests/CSharpGameEngine.Import.Tests -c Debug --filter "FullyQualifiedName~GltfSkinAnimationImportTests"
dotnet test tests/CSharpGameEngine.Windows.Tests -c Debug --filter "FullyQualifiedName~AnimationRuntimeTests|FullyQualifiedName~SkinningGpuIntegrationTests"
dotnet run --project tools/CSharpGameEngine.Unreal.SceneImporter --no-build -- thirdperson .artifacts/UnrealExport/Lvl_ThirdPerson/manifest.json templates/CSharpGameEngine.Templates.ThirdPerson
CGE_AUTOMATION_FRAMES=150 CGE_AUTOMATION_CAPTURE=<png> CgeThirdPerson.exe
CGE_AUTOMATION_FRAMES=110 CGE_AUTOMATION_INPUT=D@80-110 CGE_AUTOMATION_CAPTURE=<png> CgeThirdPerson.exe
dotnet test CSharpGameEngine.slnx -c Release
```
| Check | Result |
|---|---|
| Complete solution tests, Release | 2267 passed, 0 failed, 0 skipped. This includes Windows 319, Import 50, VSExtension 1050 and FeatureRunner 154. |
| Stage `rendering.skeletal-animation`, Debug, WARP | Passed |
| Template on the hardware GPU, 150 frames idle | Quinn idles on the central platform, back to the camera, matching Unreal's spawn view. 0 D3D12 errors. |
| Template, D held from frame 80 | Quinn turns right and walks. 0 D3D12 errors. |
## Remaining uncertainty
- Blend timing (0.2 s cross-fades, cancelling Land above 1 m/s) is chosen to resemble ABP_Unarmed. It was not measured against Unreal.
- Only this machine's hardware GPU and WARP were used.
