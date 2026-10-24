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
| ボリュメトリック雲(M0110) | `VolumetricCloud`(UE の VolumetricCloud と既定の雲の材質の値)。上半球のパノラマ(u = 方位、v = √(仰角 / 90°)。WARP は 1024 × 256・視線 24 サンプル、GPU は 2048 × 512・48 サンプル)に、雲の殻を Hillaire 系でレイマーチする(多重散乱の 3 段、2 葉の Henyey-Greenstein、太陽は透過率 LUT 越し、環境光は晴れた空の平均の放射輝度)。毎フレーム 4 × 4 ごとに 1 画素を描き直し、16 フレームで一巡する(雲・大気・太陽が変わったら全画素)。空の合成は、空にパノラマの「残る割合」を掛けて雲の輝度を足す(3 次 B スプラインで読む)。空の光の SH は雲を含み、一巡ごとに作り直す。雲の形はエンジンのタイルする Perlin-Worley ノイズ(32³、GPU で一度だけ作る)。風とカメラの位置で動く。時間はフレームの経過時間(自動実行では固定)。 |
| 空の球(M0110) | `SkySphere`(UE の SM_SkySphere と Is Sky の材質 M_SimpleSkyDome。色は大気のビューの輝度 + 太陽の円盤)。空の合成は、遠平面の画素に加えて、球の中心から半径より遠い面の画素も空で覆う(標準の深度はビュー射影の逆行列で、放射深度は T(2^(dG) − 1) で距離へ戻す)。無ければ従来どおり遠平面の画素だけが空。 |
| 材質テクスチャ(M0110) | 材質テクスチャは全ミップでアップロードする。ミップはアップロード時に CPU で作る(`RenderTextureMipChain`: 整数の重みの面積加重の箱フィルターで、奇数の大きさも全画素を使う。sRGB の欄は固定の 16 ビットの表で線形空間で平均する。決定的)。サンプラーは異方性 8(UE の r.MaxAnisotropy)。同じテクスチャの sRGB と線形の使い方は別々に常駐する。画面レイヤーのテクスチャは 1 段のままで、別のサンプラー。 |
| グローバル距離場(M0110) | `GlobalDistanceField`: 符号つき距離のクリップマップ(既定 4 段 × 64³、最も細かい段のボクセル 0.5 m、4 ボクセルで打ち切り)。R16F の Texture3D を世界のボクセル格子(カメラの位置で固定小数から決める)のトーラス状の番地で持ち、物体のメッシュの距離場から 8³ のブリック単位で作り直す(`GlobalDistanceField.hlsl`)。作り直すのは、クリップが新しく覆ったブリックと、動いた・現れた・消えた物体の届く範囲のブリック。描画には使わない。M0111 がトレースする(`SampleGlobalDistance`、`TraceGlobalDistanceField`)。 |
| Lumen 相当の GI と反射(M0111) | `RenderScalability`(UE の sg.GlobalIlluminationQuality・sg.ReflectionQuality)が High 以上で、`PostProcessVolume` の `GlobalIlluminationMethod` が Lumen(既定)で、グローバル距離場があるとき有効。既定は実機 High・WARP Medium(Medium 以下は上の Dynamic GI と空の光の反射のまま)。`CGE_SCALABILITY_GI`・`CGE_SCALABILITY_REFLECTIONS` で選び、`CGE_LUMEN_VISUALIZE`(diffuse・reflections・surfacecache)で中身を見る。サーフェスキャッシュ: 物体ごとに箱の 6 面のカード(メッシュの空間の正射影で基本色 ×(1 − 金属)・法線・発光・深度を撮る)を 2 のべき乗の矩形でアトラス(High 1024²・Epic 2048²)に詰め、予算内で太陽(カスケード、外は距離場の影)・局所光・前のフレームの放射輝度キャッシュ・空・発光 × `MeshEmissive` で照らす。最終収集: 画面のプローブ(High 32・Epic 16 画素ごと、8 × 8 の八面体)が画面の深度 → 距離場(グローバル距離場の候補を物体のメッシュの距離場で確かめる)→ 世界に固定したクリップマップの放射輝度キャッシュをたどり、3 × 3 で均して SH にし、画素は 4 つのプローブを補間して前のフレームと混ぜる。反射: GGX の可視法線の 1 光線を画面 → 距離場(カード)→ 空でたどり、粗さ 0.4 以上はプローブの SH。Stage の t16/t17 と FrameCB の `LumenParams`(`stage-v13`)で、拡散はプローブボリュームと空の光の拡散を、反射は反射の照度を置き換える(`Lumen.hlsl`、`StageLumen`)。 |

不変条件: 空だけを見る GI プローブは SH の放射照度を再現する、AO は直接光をビット単位で変えない、MinEv100 == MaxEv100 のヒストグラム露出は固定のカメラとビット単位で同じ、不正な GI ボリュームは GI だけを止める、GI は RGBA16F/RG16F の型付き UAV 読みを要する、実行中のフレームが読むかもしれないシェーダー可視の記述子は書き換えない(雲のパノラマとノイズは空と同時に作る。距離場のビューは GPU が空のときだけ書く)。M0110: 密度 0 の雲は雲の無いフレームとビット単位で同じ、雲・空の球・距離場の不正な値はそれだけを止める、距離場は画素を 1 つも変えない、数フレームかけて差分で更新した距離場は一度に作ったものと等しい(動く物体ではビット単位、カメラの移動をまたぐと浮動小数の丸めの範囲)。Stage `rendering.environment-lighting` で検証する([M0089](../Milestones/M0089-Physically-Based-Environment-Lighting.md)、[M0110](../Milestones/M0110-Lumen-Foundation-And-Visual-Gaps.md))。未実装: 反射プローブ、地面への雲の影、遠くのジオメトリの手前の雲。M0111: High より下の段では Lumen の設定は画素を変えない(Medium の Lumen ありとなしのフレームはビット単位で同じ)、グローバル距離場が無ければ Lumen は動かずプローブボリュームのフレームになる、同じ入力の 2 回の描画は同じ値、NaN や巨大な物体はカードに入らず結果は有限([M0111](../Milestones/M0111-Lumen-Dynamic-GI-And-Reflections.md))。未実装(M0111): ハードウェアのレイトレーシング、半透明の Lumen、多重の反射。

### メッシュの距離場(M0110)
メッシュの符号つき距離場は、ゲームの前に作るジオメトリの前処理で、ライティングは焼かない。立方体のボクセルで、最長辺を 44 ボクセルに収め、まわりに 2 ボクセルの余白を取る(1 軸最大 48)。各ボクセルの中心から最も近い三角形までの正確な距離(BVH)を持ち、X・Y・Z の 3 軸の光線の奇偶のうち 2 つ以上が奇数なら内側(負)とする(閉じたメッシュは正確、開いた板は両側とも正のまま)。値はボクセル単位の半精度。ビルドは、ゲームの `Assets\**\*.glb` ごとに `<asset>.glb.cgesdf`(`CGEMSDF1`。取り込みの順のメッシュごとに、メッシュの内容のハッシュを持つ)を `CSharpGameEngine.ShaderGraph.Cli build-distance-fields` で作り(`CgeGenerateMeshDistanceFields`、既定で有効。シェーダー成果物と同じ targets)、glTF の隣へコピーする。ランタイムは読み込んだメッシュに、ハッシュが一致する項目を渡す。骨格メッシュには無い。手続きの基本形状は実行時に作る。

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

**Scene view runtime view (ADR-0070).** For a built game the Scene view is drawn by the game's own `EngineHost` and `RenderServer` inside the preview host, so it uses the runtime's environment, lights, materials, skinning, exposure and GI. One session keeps its device and swap chain from Open to Close: an edit changes component values in the running world and never rebuilds device state. A gizmo drag sends the dragged roots' local transforms with each frame (`RenderPreviewFrame.TransformOverrides`), applied to their `Transform` the same way. The editor camera replaces the scene camera at the point where `LevelLoad` applies it, and the designer's selection and gizmos are drawn over the image. The view keeps the scene camera's exposure and depth mapping, read on every frame like the other components, so editing them changes the view; while an edited value is unusable (a zero near plane while typing) the last usable projection stays and `LevelRuntime.InvalidCameraProperty` names the member.

**Isolated preview depth (M0100).** The isolated preview (Scene view of an unbuilt game, Asset Preview) draws a perspective camera with radial depth from the editor's own view-projection (`RenderServer.SetViewProjection` with `RadialViewParameters`), so coverage, gizmos and picking stay those of the editor's matrix; an orthographic view stays standard. A camera thumbnail follows its scene camera's depth mapping.

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

## Radial (logarithmic) depth
ADR-0076 / M0099 ([Omni](Omni.md)): a camera may select `RenderDepthMapping.Radial`. The image is the standard one; the depth becomes `min(log2(1 + r/T) / log2(1 + rMax/T), 0.99999994)` of the physical distance `r`, so one D32 depth buffer orders surfaces from millimetres to the Q64.64 range.

| Pass | Radial camera |
|---|---|
| Main (Lit, Presentation, Selection, Wireframe) | Radial pipeline family: `RadialVSMain` (the standard `VSMain` with the conservative raster depth `a − b/w` and the standard near/far as `SV_ClipDistance`, depth clipping off) plus `RadialPSMain`/`PreviewRadialPSMain`, which call the unchanged pixel entries and add `SV_DepthGreaterEqual`; fragments beyond `FarZ·(1 + 1/1024)` of physical view depth are discarded |
| Camera previews | Per preview: standard, radial (`PreviewCleanRadialPSMain`) or radial observer (`RadialObserverVSMain` + `PreviewCleanPSMain`) |
| Shadow atlas, GI capture, overlay | Unchanged (standard) |
| Sky direction, sun cascades, AO prepass | The camera rotation with the auxiliary planes near `max(NearZ, min(0.01 m, FarZ/2))`, far `min(FarZ, near·65536)`; for an ordinary range this is exactly the camera projection |
| Sky composite, Forward+ | Unchanged (radial depth stays below the 1.0 clear; Forward+ uses clip W and XY only) |

Invariants: a standard camera's frames are unchanged (`StandardModeGoldenFrameTests`); a radial camera renders the standard pixels wherever the same surface wins (bit for bit on unclipped triangles, within one level on clipped ones), including the full environment path; depth order is correct at relative gaps of a few 1e-6 up to 5e18 m; the depth equals the CPU encoding; far planes of 1e7 m and 1e18 m keep the sky, cascades and AO finite; frame slots, hot reload and device recovery keep the radial state. Verified by `RadialDepthGpuIntegrationTests`, `ShaderArtifactHotReloadTests.RenderServer_DebugHotReload_RebuildsTheRadialPipelines` and `RadialDepthTests`. The radial main pass keeps early depth rejection through its conservative depth, tangent per object: on an RTX 4060 Ti, 24 full-screen layers drawn front to back take 0.171 ms against 0.133 ms standard between 2 and 48 m, and 0.123 ms against 0.091 ms between 100 and 1020 m (with plain `SV_Depth` 2.97 ms; with one tangent at 8 m 1.74 ms for the far layers), measured by `RadialDepthPerformanceEvidence` (`CGE_PERFORMANCE_EVIDENCE=1`).

## Screen layer
ADR-0078 / M0104. After the frame is tone mapped and the overlay is drawn, the screen layer draws text and images straight into the `R8G8B8A8_UNORM` back buffer; values are sRGB-encoded and written as they are. The game does not talk to the renderer: `ScreenText` and `ScreenImage` components are collected by the `ScreenUi` service during Presentation and `ScreenUiSubmit` hands one layer to `RenderServer.SetScreenLayer` just before `Present`.

- A layer is a list of pixel-space vertices (`RenderScreenVertex`: position, UV, straight color) and batches (`RenderScreenBatch`: texture, shading, vertex range). Limits: 24,576 vertices, 32 batches, 64 resident textures. The CPU converts pixels to NDC when the layer is set. The layer lasts one frame; `RenderFrameState` replays it after a device loss.
- Shading is `Image` (`texture × color`) or `DistanceField` (the texture's alpha is a signed distance, 128 = the outline; coverage is `smoothstep(0.5 ± fwidth)`). Blending is straight alpha over the frame.
- Textures are `RenderTextureResource` (RGBA8), resident by content hash and uploaded after `WaitForGpuIdle`. Fonts are `.cgefont` (`CGEFONT1`: em-unit metrics, plane and UV quads, an 8-bit distance atlas), baked by `tools/CSharpGameEngine.FontBaker`.
- Placement is UMG's: an element's `Pivot` sits on the screen's `Anchor` plus an offset; sizes and offsets are pixels of a screen whose shorter side is 1080 and are scaled to the surface. The Stage contract entries are `ScreenVSMain`/`ScreenPSMain` (`stage-v11`).
- Evidence: `FontAtlasTests`, `FontBakerTests`, `ScreenUiTests`, `ScreenLayerTests` (WARP: placement, blend, one-frame lifetime, device recovery, distance-field threshold), and the Vehicle template's HUD in `VehicleDriveVerification` (`screenLayerFingerprints`).

## Dynamic objects
ADR-0078 / M0105. The renderer is still created from a roster (meshes, materials, objects), which `LevelLoad` collects from the Entities that exist when the level loads. After that the roster follows the world instead of staying fixed.

- `StageRenderer` and `RenderServer` can add and remove drawn objects while running: `AddObject(mesh, material)` returns an index (the slot of a removed object is handed out again, `-1` when the headroom of `DynamicObjectHeadroom` = 4096 is used up), `RemoveObject(index)` stops drawing it. `AddMesh` adds a mesh (a separate buffer pair; since M0107 it may carry a skin, in which case the call waits for the GPU to be idle and rebuilds the palette upload ring) and `AddMaterial` a material (`DynamicMaterialHeadroom` = 256; its textures are uploaded after one `WaitForGpuIdle`, so the first use of an asset costs a frame). The object constant buffer and the material descriptor heap are sized for the headroom at creation; the environment descriptors sit after the material slots.
- An added object's constants are written to the current frame's slot only; the per-frame `SetObjectTransform` fills the others. Every pass (main, shadow, probe capture, camera previews, radial raster depth) skips removed objects.
- `RenderServer` keeps the roster it rebuilds from after a device loss: it grows with each add, and a removed object keeps its index marked `Removed`. The required-artifact closure (mesh, material and object-roster identities) is updated, the object roster once per frame. `RenderFrameState` grows its per-object arrays to the indices used.
- In the runtime, `RenderRoster` (`EngineHost.Roster`) binds Entities: Presentation systems `StaticMeshBind` and `PrimitiveMeshBind` give a not-yet-drawn Entity its objects (loading and adding an asset the renderer has not seen), and a `Renderable` wrapper whose source Entity is gone or lost its mesh component removes its object and itself. A `SkeletalMesh` that appears after the load is not drawn (skinning is set up at creation) and is reported once.
- The Stage shader contract does not change (`stage-v11`).
- Evidence: `RenderServerDynamicObjectTests` (WARP: add, remove, slot reuse, mesh and material, device loss, headroom), `RenderRosterTests` (WARP, through the generated Presentation phase), `SpawnerLifetimeParticleTests`, and the TopDown click marker in `ClickToMoveVerification` (`renderObjectCounts`).

## Skeletal meshes and animation
ADR-0066 adds skinned meshes. A `SkeletalMesh` Component names a glTF with exactly one skin. The roster gives each placement its own copy of the meshes, and `StageMeshCreateInfo.Skin` carries eight 16-bit joint/weight pairs per vertex.

At the start of every frame, `Skinning.hlsl` rewrites each skinned mesh's default-heap vertex buffer from the latest palette. The palette is `InverseBind × JointModel` in row vectors and goes through a per-frame-slot upload ring. The shadow, GI-capture, AO and main passes then all draw the same pose. `RenderServer.SetSkinPalette` records the palette for device-recovery replay.

`Runtime.Animation` evaluates `Locomotion` (Unreal's ABP_Unarmed graph) on the CPU in the Presentation phase.

`SetObjectVisibility(object, visibleToCamera, castsShadow)` (M0107, recorded in `RenderFrameState` so a device loss replays it) keeps an object out of the camera passes (main, GI capture, camera preview) while it still casts its shadow, or draws it without casting one. A new object starts visible and casting, also in a slot that a hidden one gave up. The first-person arms are drawn closer to the eye and with a narrower field of view by changing the object's matrix in the camera's frame (`FirstPersonViewWarp`), so no `stage-v*` contract and no projection changed.

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
| F-L5-038, F-L5-039 | 6, 7 | `rendering.radial-depth` | Visual / Rendering | 1800s | Radial depth curve and budget, Stage radial entries, pinned standard frames, bit-identical radial colour, coplanar separation up to 5e18 m, CPU-equal depth, finite sky/cascades/AO with extreme far planes, scene camera and editor follow, observer inset, all on WARP without debug-layer errors. |
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
