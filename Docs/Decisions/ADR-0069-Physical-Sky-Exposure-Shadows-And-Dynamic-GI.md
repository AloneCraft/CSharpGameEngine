# ADR-0069 — Physical sky, exposure, sun shadows, dynamic GI and ambient occlusion
- Status: Accepted
- Date: 2026-09-30
## Context
The ThirdPerson template port (UE5.8 `Lvl_ThirdPerson`) rendered with almost no shading contrast. Observed causes: the flat `AmbientLight` was added as an absolute radiance roughly equal to the sun's diffuse term, exposure was fixed at EV100 0, the sky was the clear colour, fog colour was constant, and the only sun shadow was one 40 m orthographic projection around the camera. Unreal lights the same level with SkyAtmosphere, a captured SkyLight, Lumen GI/reflections, virtual shadow maps, exponential height fog, SSAO and a PostProcessVolume. The user asked for the lighting system and the template to be implemented, choosing sky + SkyLight + exposure + cascaded shadows + SSAO + dynamic GI.
## Decision
1. **Environment Components** live in the portable `Runtime.Lighting` shared source: `SkyAtmosphere`, `SkyLight`, `PostProcessVolume` (exposure, tone mapper, bloom, ambient occlusion), `ExponentialHeightFog` and `GlobalIlluminationVolume`. They keep Unreal's authored units so imported values need no conversion and hold only plain mutable fields, so the snapshot codec round-trips them canonically (the AUD-001 contract). A Presentation System records them each frame; `EngineHost` converts them once into `EnvironmentLightingSettings`. An absent Component keeps the renderer's original path (clear colour, flat ambient, legacy fog, `2^-EV100`, linear ACES), so existing scenes and the M0064 Showcase are unchanged.
2. **Sky**: Hillaire 2020 transmittance, multiple-scattering and sky-view LUTs in the compute artifact `Atmosphere.hlsl`; far-plane pixels are composited after the opaque pass. The brightest directional light is the sun; its top-of-atmosphere illuminance is attenuated by a CPU transmittance evaluated at the camera altitude.
3. **Sky light**: nine SH coefficients of the sky radiance, stored already convolved with the clamped cosine (bands scaled by π, 2π/3, π/4). Stage uses them for diffuse irradiance and, with an analytic environment BRDF, for a rough reflection term. `SkyLight.Intensity` multiplies the captured radiance; it is not an absolute ambient.
4. **Exposure and output**: Unreal's physical camera, `2^compensation / (1.2 * 2^EV100)`, either at a fixed EV100 or metered by histogram auto exposure (`AutoHistogram`, Unreal's AEM_HISTOGRAM). The output is Unreal's filmic curve in AP1 followed by the sRGB transfer function. The histogram mode stays on the GPU (`LuminanceHistogram.hlsl`, `StageAutoExposure`):
   - Each frame bins the log2 scene luminance of the HDR target. The applied scale is divided back out, and pixels clipped at the RGBA16F ceiling go to a separate last bin.
   - It averages the 10–90 % pixels and meters EV100 = log2 L + 3 (the K = 12.5 reflected-light meter), clamped to [Min, Max].
   - It adapts exponentially with Unreal's SpeedUp/SpeedDown into a one-float4 state buffer. The Stage program (t15) and the sky composite read that buffer in the next frame.
   - Until a histogram whose clipped pixels stay inside the discarded brightest 10 % arrives, the metered value jumps directly to each target.
   - The CPU never maps the result per frame.
5. **Fog**: Unreal's exponential height fog in closed form; authored density and falloff are converted to per-metre values (×0.1).
6. **Sun shadows**: four cascades split by Unreal's exponent-3 distribution up to 200 m, each a bounding sphere of its frustum slice with texel-snapped centres, rendered into 512 px quadrants of shadow-atlas tile 0 and sampled with a normal offset of 1.5 texels. This replaces the single 40 m projection.
7. **Dynamic diffuse GI** is a rasterized DDGI-style probe volume, not ray tracing. A `GlobalIlluminationVolume` places a regular grid (at least two probes per axis, at most 4096) in the renderer's camera-relative space, so absolute positions never become floats. Every frame a round-robin batch of probes is captured as six 32×32 faces with the lit Stage pipeline itself (exposure 1, fog, Forward+ local lights and AO off). `GlobalIllumination.hlsl` then convolves each capture into an 8×8 octahedral irradiance tile and a 16×16 distance-moment tile with hysteresis. Captures sample the previous result, so each refresh adds a bounce. Stage blends the eight surrounding probes with trilinear, wrap-shading and Chebyshev visibility weights. The result replaces the sky light's SH diffuse inside the volume and scales its reflection as a specular occlusion term.
8. **Ambient occlusion**: Scalable Ambient Obscurance on a half-resolution camera depth prepass (the depth-only shadow pipeline), followed by a separable depth-aware blur in `AmbientOcclusion.hlsl`. Stage multiplies only indirect light (sky light, GI and the flat ambient) by it.
9. **ABI**: Stage moves to `stage-v7` (t11 sky SH, t12/t13 GI atlases, t14 AO, t15 exposure state, post-process b0 constants). Compute artifacts keep the single `compute-v2` contract; each host documents its root signature next to its HLSL and C# helper.
10. **Import prerequisites for Unreal parity**: glTF base-colour and emissive textures are read through sRGB views. `KHR_texture_transform` is baked into `TEXCOORD_0` because meshes carry one UV set; the base colour transform wins and a differing channel is reported.
## Invariants
- A GI probe that sees only sky reproduces the Stage SH irradiance (the sky texels use the band-limited radiance without a per-sample clamp), so enabling GI does not change open areas and there is no seam at the volume boundary.
- Ambient occlusion never changes direct light: a frame without indirect light is bit-identical with AO on.
- An invalid or oversized GI volume disables only GI; the sky, exposure and fog stay.
- Histogram auto exposure with `MinEv100 == MaxEv100` renders bit-identically to the fixed physical camera at that EV100, so Unreal's degenerate histogram ranges import as histogram mode.
- A shader-visible descriptor that an in-flight frame may read is never rewritten; per-frame views live in per-frame slots or are written only when their resource changes.
- GI requires typed UAV loads of RGBA16F/RG16F (`TypedUAVLoadAdditionalFormats`); without it GI is off, never approximated.
## Alternatives considered
- DXR ray-traced probes: not available on WARP, the required visual adapter, and a hardware dependency for every user.
- Offline baked lightmaps or probes: not dynamic; the user chose dynamic GI.
- Screen-space GI: misses off-screen and occluded emitters and flickers with camera motion.
- Keeping the flat ambient and tuning its value: cannot produce directional sky light or occlusion, and was the observed cause of the flat image.
- Mipmapped sky cubemap for reflections: deferred; the SH reflection term is rough-only.
## Consequences
- Per-frame cost: probe captures draw every object six times per scheduled probe (32 by default), plus one convolution dispatch per probe and a half-resolution depth prepass. WARP renders the 578-probe ThirdPerson level at about 3 frames per second.
- Known gaps: no volumetric clouds (not in the chosen scope), no texture mipmaps (fine texture detail aliases), no reflection probes, local lights do not contribute to GI, probes inside geometry are not relocated (thin walls can leak), and camera previews render without ambient occlusion.
- The ThirdPerson importer derives the GI volume (Unreal has no authored equivalent under Lumen) from the level meshes: floor + 0.3 m to the level top, 2.5 m spacing, growing until the grid has at most 2048 probes.
## Verification
WARP oracles and unit tests are recorded in [M0089](../Milestones/M0089-Physically-Based-Environment-Lighting.md). They cover the atmosphere LUT against the CPU reference, cascade fitting, sun shadows, GI (sky reproduction, wall distance and colour, colour bleeding), AO (crease, open surfaces, sky, direct-light invariance), the Component snapshot round trip and `KHR_texture_transform`. Hardware GPU and interactive VSIX verification are not implied.
## Related features
F-L4-014 (new), F-L5-001 (Runtime.Lighting Components), F-L0-005 (`GltfImporter.ImportScene`). See [Rendering](../Specifications/Rendering.md#environment-lighting).
## 追記(2026-10-23、ADR-0078 / M0110)
- ユーザーの決定(ADR-0078)で、GI と反射は Lumen 相当(M0111: サーフェスキャッシュ、グローバル距離場への最終収集、反射)にする。この ADR の DDGI プローブ(決定 7)は捨てずに、低品質と WARP の「代替の段」として残し、設定で切り替える。M0110 は M0111 の土台(メッシュの距離場とグローバル距離場)を作った。
- 「Known gaps」のうち、ボリュメトリック雲とテクスチャのミップマップは M0110 で実装した(`VolumetricCloud`、材質テクスチャの全ミップと異方性 8)。空の光の SH は雲を含む(雲のパノラマを一巡するたびに作り直す)。反射プローブ、局所光の GI、プローブの移動は M0111 の範囲。詳細は [M0110](../Milestones/M0110-Lumen-Foundation-And-Visual-Gaps.md)。
