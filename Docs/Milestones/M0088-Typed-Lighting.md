# M0088 — Typed lighting, quantities and shadow ownership
- Date: 2026-09-08
- Implementation: Complete
- Verification: TestVerified / WARP RuntimeVerified; all ten related Feature Stage runs passed.
- Decision: [ADR-0064](../Decisions/ADR-0064-Typed-Lights-And-Shadow-Ownership.md)
- Intended contract: [Typed Lighting](../Specifications/Typed-Lighting.md)
## Goal and scope
Remove kind-dependent public light properties and unit ambiguity throughout ECS, import, RenderServer input, completed-frame replay and source authoring. Breaking API migration is intentional.
Implemented: validated Contracts values; portable shared Runtime.Lighting with four applicable-only structs and LightShadow; typed render/import variants; ordered reusable replay and separate emissive records; static symbol-checked Inspector editing; cone/range/rectangle gizmos; source-only conversion; palette transactions; scene/template/Unreal emission migration; icons owned by the shared module.
Excluded: new light shapes, GI/baking, gameplay light directors, runtime archetype/kind replacement, compatibility wrappers, interactive VS deployment and hardware-GPU certification.
Changed features: F-L5-001, F-L8-024, F-L8-025, F-L4-002, F-L0-005, and F-L0-009 recovery evidence.
## Observed behavior and fixes found by tests
- Unit/property misuse is rejected at compile time; constructors reject nonfinite/negative quantities and invalid shapes. Scene and runtime checks catch invalid defaults.
- Palette defaults now read literal field initializers as well as simple constructor assignments. The previous policy silently replaced primary-constructor field defaults with default values; the new conversion tests exposed and fixed that.
- Named and target-typed constructor arguments round-trip through the exact declared constructor contract. Compound values are atomic and constant/static expressions stay read-only.
- All 12 conversions preserve Node identity, Transform and shadow ownership. Local conversions preserve flux; Directional requires explicit target units. Source/default-contract conflicts reject without a write.
- Conversion runs through the existing verified one-write/native-Undo boundary. Tests reanalyze the before and after text for Undo/Redo semantics; they do not simulate an installed Visual Studio session.
- RectAreaLight submissions carry forward and up, preserving roll while normalizing scale. Physical dimensions remain independent of Transform Scale.
- Typed records retain cross-type ordering, including SetDirectional replacement commands. A mixed four-light WARP scene has byte-identical pixels after two device removals/recoveries, with no D3D12 error messages.
- Warm typed frame recording: 10000 batches, zero additional managed bytes. Warm RenderServer submission: 32 rounds of four typed lights (128 submissions), zero additional managed bytes.
- Imported glTF point/spot retain cd, optional range and half-radian cones. Zero inner angle stays legal imported data but is explicitly rejected when lowered to the stricter authored cone.
## Commands and results
Commands executed from the repository root:
```text
dotnet build CSharpGameEngine.slnx -c Debug --no-restore --nologo -v quiet
dotnet build CSharpGameEngine.slnx -c Release --no-restore --nologo -v quiet
dotnet test CSharpGameEngine.slnx -c Debug --no-build --nologo -v quiet
dotnet test CSharpGameEngine.slnx -c Release --no-restore --nologo -v quiet
dotnet test tests/CSharpGameEngine.Core.Tests/CSharpGameEngine.Core.Tests.csproj -c Debug --no-build --filter FullyQualifiedName~SolutionProjectRegistrationSpec
dotnet run --no-build --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj -c <Configuration> -- run <Stage> --configuration <Configuration> --profile typed-light-m0088 --adapter warp
git -c core.safecrlf=false diff --check
```
| Check | Debug | Release |
|---|---:|---:|
| Solution build | Passed | Passed |
| Complete solution tests | 2162 passed, 0 failed, 0 skipped | 2161 passed, 0 failed, 0 skipped |
| Windows/WARP suite (included above) | 243 passed | 242 passed |
| VSIX suite (included above) | 1039 passed | 1039 passed |
| Explicit solution/shared registration check | 3 passed | Included in the 128-test Core suite |
Both builds have zero errors. Some runs emitted one MSB3026 retry warning when the VSIX test asset was temporarily locked; the retry succeeded. No user process was terminated.
Early expected failures included missing new Contracts types, old API consumers/test fixtures, field-initializer defaults, canonical expression whitespace, localization resources and the rectangle pose connection. All were fixed before final suite verification.
## Feature Stage evidence
Stages: geometry.import, vsix.inspector-contracts, vsix.source-sync, rendering.device-recovery, vsix.rendering-preview, each in Debug and Release/WARP with profile typed-light-m0088. New typed editing/frame tests are included in the relevant Stage filters.
All ten runs passed. The JSON files retain command output, assertions, Git commit/dirty state, artifact SHA-256 and timings.

| Configuration | Stage | Result artifact | File SHA-256 |
|---|---|---|---|
| Debug | geometry.import | [Passed](../../.artifacts/FeatureStages/20260908T024509591Z-typed-light-m0088-a368bb873c9149c2949216bca62eb859.run-result.json) | `DB91F75F194AFAB51FBD4B07D7A97EF3E287139BD926263BF85FB23FE3C8ACA2` |
| Debug | vsix.inspector-contracts | [Passed](../../.artifacts/FeatureStages/20260908T024532943Z-typed-light-m0088-469c3d9bba7246009b0bd6cd6a0ce386.run-result.json) | `694C6905B7938F8E9D89FE494A0F07F71705967A95F86FD0BDADBC2D52DD6231` |
| Debug | vsix.source-sync | [Passed](../../.artifacts/FeatureStages/20260908T024552812Z-typed-light-m0088-c6e7b8735bd245f79be34f3c406ef9c5.run-result.json) | `9F2A1084FC9589303C52F6558A14B17D9B4DB2F894D615377B30BC8999F89799` |
| Debug | rendering.device-recovery | [Passed](../../.artifacts/FeatureStages/20260908T024614541Z-typed-light-m0088-bc4e4f65faa9459d8c0f122897e3f2b0.run-result.json) | `BE5B53985210582D87EE377FB1053ADE9991C26BEEDC02D89A89D07A79C91865` |
| Debug | vsix.rendering-preview | [Passed](../../.artifacts/FeatureStages/20260908T024655301Z-typed-light-m0088-e12321e482634dc88a62b2a6bc3662ad.run-result.json) | `8B9BD3BDEF77C887CDF7F35940A2020386C628ED90B9D34BC8B1FFAEE3BC344D` |
| Release | geometry.import | [Passed](../../.artifacts/FeatureStages/20260908T024700287Z-typed-light-m0088-e83702d56e1e4453be2888db4e1d02f6.run-result.json) | `1B090386FA764AB909DF95884F4457A88020B8916F1380D8ACD3165EA8E57A93` |
| Release | vsix.inspector-contracts | [Passed](../../.artifacts/FeatureStages/20260908T024726211Z-typed-light-m0088-073a3253eabc46848cb5c68ff4547140.run-result.json) | `CFA6D953875A9F7693704BF6E89E7BAC0D873CB93E947B2976E19CD1F3898566` |
| Release | vsix.source-sync | [Passed](../../.artifacts/FeatureStages/20260908T024745760Z-typed-light-m0088-7e8a1cae98aa4e2a80468f4b13f2447d.run-result.json) | `50173BAA0E06F0E8DE247EB23816BEE41394CDCBCCDD09DB941270077596505F` |
| Release | rendering.device-recovery | [Passed](../../.artifacts/FeatureStages/20260908T024808624Z-typed-light-m0088-65f86138164c4704a2aaef4d124ac49a.run-result.json) | `A93B92552B6C431A210D11FE17138A7C92CD5EBDD1440C816AB33CE878324A62` |
| Release | vsix.rendering-preview | [Passed](../../.artifacts/FeatureStages/20260908T024850533Z-typed-light-m0088-9e6020f1369341d680d5b729b89f473f.run-result.json) | `F172A484C2B0D5E9D961174B59D6CECA470666F7213C0A63ABFF239DCAE544D9` |
## Conformance and handoff
Automated observed behavior matches ADR-0064 and the approved typed-light scope. Existing shader ABI, physical attenuation, LTC and shadow budgets remain intact. Full native interactive UI operation and hardware-GPU testing remain explicitly unclaimed.
Source files and documentation are uncommitted for review. No compatibility wrappers or runtime light-kind switch were added. The four old Windows icon files were moved into Runtime.Lighting (Area renamed RectArea); LightShadow has its own icon. Git retains the old locations.
