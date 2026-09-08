# M0078 — Platform Compatibility Evidence

## Outcome

F-L7-034 is implemented as one versioned Platform compatibility contract. Windows x64 Debug and Release correctness is mandatory through the existing solution, Windows-TFM, and WARP CI cells. XAudio2 and D3D12 hardware observations remain explicitly supplemental and cannot replace the required synthetic Input, Silent Audio, or WARP verdicts.

## Source of truth and ownership

- `Docs/Verification/CI-Matrix.json` remains the only required CI-cell source of truth; `.github/workflows/ci.yml` remains its executable mirror.
- `Docs/Verification/Manual-Evidence.json` remains the only dynamic manual-evidence ledger. It owns the status, observation time, expiry, and evidence link for optional hardware.
- `Docs/Verification/Platform-Compatibility-Matrix.json` owns only Platform scope: the exact prerequisite Stage closure, required CI-cell references, target Windows version/architecture, and optional hardware-ledger references.
- `FeatureStageCatalog` owns Feature/Stage lifecycle. The matrix validator requires every referenced Input, Audio, and diagnostics Stage to be executable.
- Feature Stage run-result artifacts own each local Debug or Release execution result. The checked-in matrix does not copy a Passed/Pending state.

This division keeps required verdicts, manual observations, and track scope as three non-overlapping truths. F-L7-034 does not create a new device detector, hardware probe, CI scheduler, or release manifest.

## Required and optional cells

The six required Platform cells reference `solution-debug`, `solution-release`, `windows-tfm-debug`, `windows-tfm-release`, `warp-debug`, and `warp-release`. The first two modes prove the complete portable/subsystem closure, the Windows-TFM pair proves the Windows 10 2004+ x64 runtime target, and the WARP pair proves graphics correctness without requiring a physical GPU.

The two optional cells reference `hardware-audio` and `hardware-d3d12` in the manual ledger. Pending or failed optional evidence remains visible but does not fail F-L7-034. Moving either hardware row into required cells, removing any required row, changing the CI adapter/configuration, losing a prerequisite Stage, or breaking its ledger owner fails validation.

## Failure contract

`PlatformCompatibilityMatrixValidator` validates the Platform schema and target, delegates full CI/workflow and manual-ledger validation to their existing owners, then checks exact cell/reference and executable-Stage agreement. Missing, extra, duplicate, null, empty, mismatched, optional-promoted, non-executable, wrong-owner, or wrong-adapter data returns sorted validation errors rather than throwing or accepting partial evidence.

## Adversarial review

The first implementation correctly separated the three truths but used `SingleOrDefault` after reporting duplicate cells. A corrupt duplicate therefore could throw before returning the complete validation result. It also read optional ledger rows without invoking the ledger's own schema/state validator. Review replaced duplicate-sensitive lookups with fail-closed first-match diagnostics, added empty-reference guards, delegated ledger validation, and added a regression that proves malformed duplicates and empty references return errors.

Overengineering review retained one small static matrix and one validator. It rejected a new dynamic compatibility database, duplicated CI workflow fields, a hardware probe, a second evidence status, and early product-wide aggregation owned later by F-L7-046.

## Verification

- Focused matrix/catalog/CI tests: 23/23 Debug and 23/23 Release.
- `platform.compatibility` Debug: `20260901T134331089Z-full-eda58875a7414b48b8919b4233ae5ceb`.
- `platform.compatibility` Release: `20260901T134337227Z-full-22bf326925d14baea1832fce1751c8cf`.
- Each Stage run also executes Core Input contracts, all Audio tests, focused Windows Input/Audio/ownership/Preview Host tests, Contracts protocol validation, and net472 VSIX diagnostics presentation/architecture tests.
- Feature coverage, manual evidence, CI matrix, VSIX compatibility, golden policy, and failure/quarantine validation pass during the FeatureRunner build gate.

Hardware Audio and GPU evidence remains Pending by design and is not reported as tested hardware.
