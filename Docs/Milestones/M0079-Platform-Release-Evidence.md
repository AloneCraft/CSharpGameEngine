# M0079 — Platform Release Evidence

## Outcome

F-L7-035 completes the Audio/Input/Platform track with one canonical, self-validating report per configuration. The report binds a freshly re-executed F-L7-034 verdict, the exact Platform source and verification inputs, Windows x64 and required adapter modes, machine/timestamp provenance, Git provenance, and the current optional hardware observations.

This is a bounded track report for later product-wide release aggregation. It is not a package, signing format, SPDX document, installer, hardware certification database, or competing immutable product release manifest.

## Source of truth and ownership

- platform.compatibility remains the correctness execution and matrix-agreement truth.
- Feature Stage run-result files remain the result truth. platform.release invokes F-L7-034 with the dedicated platform-release-prerequisite profile and accepts only its recent passing Warp result with the same configuration, commit, and dirty state.
- Platform runtime source, the Platform matrix, CI matrix/workflow, Manual Evidence ledger, global.json, and Feature Stage catalog remain their own truths. The report stores their ordered paths, roles, lengths, and SHA-256 values.
- The Manual Evidence ledger still owns optional hardware state. The report records the two hardware rows as an immutable release-time snapshot; it does not change Pending into Passed.
- PerformanceMeasurementFramework supplies the privacy-preserving machine fingerprint and Stopwatch frequency. No device handle, provider object, authoritative simulation value, or second service owner enters the report.

## Canonical report

PlatformReleaseEvidence writes csharp-game-engine-platform-release-v1 beneath .artifacts/PlatformRelease/<unique>/platform-release.json. A SHA-256 covers every payload field except the hash itself, serialization is canonical and newline-terminated, and the directory must contain exactly that one report.

The required modes are fixed to QPC/Stopwatch counter provenance, synthetic Win32 Input events, Silent Audio, and WARP graphics. Target and captured process/OS architecture must both be x64. Eighteen repository inputs form the scoped dependency closure.

## Failure contract

Generation fails without leaving a partial evidence directory when matrix/ledger validation fails or no fresh configuration/Git/profile/adapter-matched prerequisite exists. Revalidation rejects non-canonical bytes, payload-hash mismatch, changed inputs, moved/escaped/reparse paths, altered Stage files, invalid Stage artifact hashes/assertions/timing, stale or mismatched provenance, changed optional hardware state, invalid machine fingerprint, non-x64 capture, and missing or extra evidence files.

## Adversarial review

The first implementation accepted any recent passing F-L7-034 profile and declared x64 only in a target field. Review required the dedicated platform-release-prerequisite profile and independently checked both OS and process architecture in the hashed machine fingerprint. A regression proves an ordinary full result cannot substitute for the release prerequisite.

Overengineering review retained one report and one producer/validator. It rejected copied packages, a new CI description, a hardware probe, duplicated dynamic status, signing/SBOM placeholders, and a general release framework already owned by F-L7-045 through F-L7-048.

## Verification

- Focused release/compatibility/catalog tests: 25/25 Debug and 25/25 Release.
- platform.release Debug: 20260901T135955088Z-full-dbd3216597ae47fc9accafb6fa8d5bf9.
- platform.release Release: 20260901T135955011Z-full-a0dae41606384a91a49b3fc0d67a9752.
- Debug report SHA-256: 822f1feb7633f7ce2fcd9ef74ee0a45f27f0f28106788e70b6c23f9bbb1f9901.
- Release report SHA-256: 2b26dc75d215bf15f36dc89c790c0bb9b897767a66337b8a7160592e63bfe4d1.
- Both reports record machine fingerprint 3085d0ff0b36619f65edccbfe5a39f0dfe5b6a14ccc264a0faa129fd06502e57.

Hardware Audio and GPU rows remain Pending supplemental observations and are not represented as hardware passes.
