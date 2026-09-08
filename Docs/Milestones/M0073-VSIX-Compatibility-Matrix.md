# M0073 — VSIX Compatibility Matrix

## Outcome

F-L8-028 is implemented as one versioned, bidirectionally validated compatibility contract. Static support declarations and dynamic observations no longer live as unrelated strings across the VSIX project, source manifest, automation command, CI matrix, and evidence ledgers.

## Contract

- VSIX and automation target `net472`.
- The install contract is Visual Studio 17.14 or later, amd64, with the Core Editor prerequisite and .NET Framework 4.7.2 dependency.
- The package has neutral `en-US` metadata and a `ja-JP` satellite/language pack; item templates retain 1033 and 1041 cultures.
- `CgeFixed` is the developer F5 hive. Evidence can only come from the separately owned `CgeAutomation` hive.
- Debug and Release CI both contain a required `net472` VSIX cell.
- Detailed Experimental checks and the global manual summary have one aggregate state. Pending remains Pending.
- A Passed automated row additionally validates Release configuration, host version, locale, root suffix, provenance hashes, assertions, observation time, and expiry. The Feature Stage's own Debug/Release build configuration does not rewrite this historical observation contract.
- Strict release validation rejects every required Pending, Failed, missing, malformed, or expired row.

## Deliberate boundary

This milestone implements the matrix and its evidence validator; it does not relabel existing manual Visual Studio, DPI, focus, accessibility, or long-run checks as Passed. Those observations remain explicit F-L8-029 release inputs.

## Verification

- `VsixCompatibilityMatrixValidatorTests`
- `VsixManifestTests`
- `VsixPackagingLocalizationTests`
- `VsixLocalizationTests`
- `VsixExperimentalAutomationContractTests`
- `ScenePhysicsPanelLayoutTests`
- `vsix.compatibility` in Debug and Release

Final evidence: FeatureRunner 76/76 and net472 VSIX 970/970 pass in both configurations. The Stage passed as `20260901T114505804Z-vsix-a650a39f789646f1b6369d30e7bd6c87` (Debug) and `20260901T114546489Z-vsix-609836b366cc4b7c8a3d67cae87be3fd` (Release).
