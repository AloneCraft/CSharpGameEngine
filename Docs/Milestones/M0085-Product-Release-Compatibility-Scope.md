# M0085 - Product Release Compatibility Scope

## Outcome

F-L7-046 completes one static product compatibility scope for Windows 10 2004+ x64, .NET 10, a net472 VSIX, and mandatory WARP visual certification. `release.compatibility` validates the existing CI, Platform, VSIX, manual-evidence, and package-contract truths as one exact set without copying any dynamic observation or verdict.

## Source of truth and ownership

- `Docs/Verification/CI-Matrix.json` remains the only owner of the eight required Windows Debug/Release CI cells.
- `Docs/Verification/Platform-Compatibility-Matrix.json` remains the owner of six required Platform cells and two supplemental hardware references.
- `Docs/Verification/VSIX-Compatibility-Matrix.json` remains the owner of the eight release-required VSIX observations; the Experimental and manual ledgers own their states and evidence.
- `FeatureStageCatalog` remains the owner of Feature lifecycle and executable Stage availability.
- Feature Stage run results remain the owners of configuration-qualified execution verdicts.
- `Docs/Verification/Release-Compatibility-Matrix.json` owns only product-scope membership and canonical source references.

The aggregate contains no `status`, `Passed`, evidence path, observation time, expiry, copied hash, device probe, or Stage result. F-L7-047 consumes the scope and evaluates actual evidence later.

## Exact scope

The required automated CI set is `solution-debug`, `solution-release`, `windows-tfm-debug`, `windows-tfm-release`, `vsix-debug`, `vsix-release`, `warp-debug`, and `warp-release`.

The required Platform set is `platform-solution-debug`, `platform-solution-release`, `platform-windows-debug`, `platform-windows-release`, `platform-warp-debug`, and `platform-warp-release`. `platform-hardware-audio` and `platform-hardware-d3d12` remain supplemental.

The required VSIX observation set is `shader-graph-1041`, `shader-graph-1033`, `scene-designer`, `dpi-scaling`, `escape-routing`, `physics-sidebar`, `startup-layout`, and `preview-render-audio`.

The executable contract Stage set is `verification.ci-matrix`, `platform.compatibility`, `vsix.compatibility`, and `release.packages`.

## Failure contract

Strict JSON rejects unknown aggregate fields. Validation first delegates CI, Platform, and VSIX sources to their existing validators, then checks exact aggregate membership, canonical target and source paths, duplicate/empty references, optional-to-required hardware promotion, source drift, and executable Stage availability. Errors are distinct and ordinal-sorted; malformed duplicates return diagnostics rather than throwing.

## Adversarial review

The rejected alternative flattened every source row and its current status into a product matrix. That would create a second dynamic truth and require synchronization with CI, manual evidence, Experimental observations, and Stage results. The final design stores only identity references and therefore cannot hide a changed source behind a stale copied pass.

Overengineering review rejected a compatibility database, provider interface hierarchy, runtime hardware detector, and generic evidence graph. One checked-in document and one static validator are sufficient for the fixed Windows product scope.

## Verification

- Focused compatibility plus Catalog tests: 20/20 Debug and 20/20 Release.
- Debug `release.compatibility`: `20260901T173856314Z-full-1602a39885a44e6db299e37d3b812804`; artifact SHA-256 `10bcefd6f08e66c8c5f991cc98cdbc1fdcfd7ec9ceadfb181cce4f82eccff80c`.
- Release `release.compatibility`: `20260901T173928467Z-full-cfaf190a66164a62a8d9a17f4b0f80a1`; artifact SHA-256 `b0ed419887e0f0b6b8274bed89de1fb213756a4a3b0d7b072f16f7f33ee49756`.

## Feature transition

- F-L7-046: `Specified` to `Implemented`, verification `RuntimeVerified`, conformance `Matches`.
- F-L7-047 remains responsible for Level/track-scoped actual evidence and RC verdicts.
