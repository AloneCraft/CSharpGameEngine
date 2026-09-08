# M0084 - Product Package And SBOM Evidence

## Outcome

F-L7-045 completes the product-package boundary with one deterministic Windows x64 portable archive per configuration. The `release.packages` Stage publishes the viewer CLI, analyzer NuGet, exact prebuilt localized VSIX and Preview Host, native Assimp payload, and generated Shader Graph artifacts, then binds their exact bytes to a canonical manifest, SPDX 2.3 file/dependency SBOM, restored NuGet dependency closure, external signing inputs, and isolated install/run/uninstall evidence.

This feature does not claim that an external signature has been applied. It does not rebuild or repack the VSIX, decide the supported compatibility matrix, gate an RC, or create the formal immutable product manifest. Those remain with F-L8-029 and F-L7-046 through F-L7-048.

## Canonical package

`ReleasePackageEvidence` writes `CSharpGameEngine.<version>.<configuration>.win-x64.zip` beneath a unique `.artifacts/ReleasePackages/<run>/` evidence root. The archive has ordinal entry order, fixed ZIP timestamps, no explicit directory entries, no duplicate or escaping paths, an exact SHA-256 file manifest, and canonical UTF-8 JSON documents.

The archive contains:

- the framework-dependent .NET 10 viewer CLI without PDB files;
- the sole packable `CSharpGameEngine.CodeAnalysis` analyzer NuGet;
- the exact already-built VSIX with en-US and ja-JP Scene templates and Preview Host;
- `assimp.dll` and its owned runtime closure;
- generated Shader Graph artifacts under `Shaders/`.

The SPDX 2.3 document records product files and the exact restored NuGet dependency packages. Dependency hashes come from restored `.nupkg` bytes rather than names or versions alone. Signing inputs bind archive, manifest, and SBOM hashes and lengths without manufacturing a signature verdict.

## Lifecycle contract

Validation safely extracts into one unique isolated directory, rejects unsafe or unowned entries, independently recomputes installed bytes from the archive, runs the packaged viewer for one hidden WARP frame with readback verification and zero D3D12 validation errors, and deletes the installation on every success or failure path. A successful report requires zero residue.

Generation and validation fail for missing, extra, changed, escaped, duplicate, reordered, non-reproducible, development-path, partial, stale, noncanonical, or closure-inconsistent evidence. Failed generation removes its partial evidence directory.

## Build corrections discovered by execution

The first real publish exposed two product-build defects that unit-only package construction would not have found:

1. the parent publish `RuntimeIdentifier` leaked into the nested host Shader Graph compiler and produced `NETSDK1047`;
2. generated Shader Graph artifacts existed after build but were absent from `ResolvedFileToPublish`.

`build/CSharpGameEngine.ShaderGraph.targets` now removes `RuntimeIdentifier`, `SelfContained`, and `PublishDir` from the nested tool build and explicitly registers generated Shader Graph and Compute artifacts for publish. A focused regression test owns both contracts.

## Adversarial review

Correctness review added independent recomputation of the installed closure instead of trusting copied manifest values and corrected SPDX `filesAnalyzed` to the required Boolean representation. Archive and nested-container validation remain distinct: only the outer product archive owns deterministic ordering and fixed timestamps, while nested VSIX and NuGet containers retain safety, duplication, and bounded-size validation without false repack requirements.

Overengineering review retained one package-specific transaction rather than introducing a generic release framework, package abstraction hierarchy, or second product manifest.

## Verification

- Focused `ReleasePackageEvidenceTests`: 9/9 Debug and Release.
- Frozen-input Debug `release.packages`: `20260901T172840773Z-full-87f7e27773a1446190e6dd29b01ecffd`; Stage artifact SHA-256 `164ceef76e35eb17c47bd9124762dbfd34d5cb9c03957434833b1e407338b1a6`; archive SHA-256 `752fe605732ecb4947b3e1ef89ee980555cdda3c5ba8fa1a4c19900e46200d8d`; report SHA-256 `89839844e896c827c8975f8178e4149acfbd4356df1d0195d9d3f859282f314a`.
- Frozen-input Release `release.packages`: `20260901T172932938Z-full-f538f59fd0674eecb58162a02999158d`; Stage artifact SHA-256 `2663c14a1fd3c31fb74512eb64d04179f5011b38771cffc90fd693b2062598b6`; archive SHA-256 `85d9b1231a3c4b91fbd256e54f19b5a9b16b7b34333342255a3e29eec62b8cf0`; report SHA-256 `2e3b4b164cd537d073799e30c52b57866736cf115052845b328109f2b8e6f27f`.
- Both package transactions close 17 restored NuGet dependencies, run the extracted CLI on WARP, and uninstall with zero residue.

## Feature transition

- F-L7-045: `Specified` to `Implemented`, verification `RuntimeVerified`, conformance `Matches`.
- F-L7-046 compatibility aggregation, F-L7-047 RC gating, and F-L7-048 immutable manifest remain the next release-closure owners.
