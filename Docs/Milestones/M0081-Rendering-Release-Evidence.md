# M0081 - Rendering Release Evidence

## Outcome

F-L7-024 completes the Rendering Roadmap with one canonical self-validating report per configuration. The `rendering.release` Stage re-executes `rendering.performance` and `vsix.rendering-preview` under dedicated profiles, then binds their complete WARP verdicts, the exact raw capture, both common performance reports, one configuration-qualified Shader Artifact SHA-256, nineteen source/toolchain inputs, Windows x64, machine identity, and Git provenance.

The report is a bounded Rendering-track input for later product-wide release aggregation. It is not a package, installer, signature format, SBOM, compatibility database, or competing immutable product manifest.

## Source of truth and ownership

- Feature Stage run results remain execution truth. Only recent `rendering-release-performance` and `rendering-release-preview` results with the requested configuration, matching Git state, WARP, complete assertions, and valid artifact hash are accepted.
- The raw schema-v2 report remains the adapter/debug/pixel/sample/Shader Artifact observation. The release report reopens the actual raw path and verifies its file SHA, not merely a copied hash string.
- Common performance reports remain statistics and budget truth. The release validator reopens both, invokes their canonical integrity validator, and requires their raw and Shader hashes to equal the bound raw report.
- Rendering, Preview Host, Shader Graph, toolchain, performance, and Stage sources remain their own truths. The release report stores ordered role/path/length/SHA entries and revalidates them against the repository.
- Debug and Release Shader Artifact hashes intentionally differ: Debug embeds debug information with optimization disabled, while Release uses the optimized compiler contract. Each report binds exactly one artifact within its configuration.

## Canonical report

`RenderingReleaseEvidence` writes `csharp-game-engine-rendering-release-v1` under `.artifacts/RenderingRelease/<unique>/rendering-release.json`. The SHA-256 covers every payload field except the report hash. Bytes are canonical, UTF-8, and newline terminated; the evidence directory must contain exactly one file.

The fixed closure contains nineteen inputs. Path escape, rooted/backslash/noncanonical paths, reparse points, and changed file length/hash are rejected. The captured machine fingerprint must independently recompute and both OS and process architecture must be x64.

## Failure contract

Generation leaves no partial directory when either prerequisite is absent, stale, ordinary-profile, wrong configuration/Git/adapter, incomplete, or internally inconsistent. Revalidation rejects changed Stage files/results, raw deletion or modification, malformed raw schema/adapter/runtime/shader/pixel/debug/sample state, missing or modified performance reports, raw/performance Shader disagreement, statistics/budget/fingerprint/hash changes, noncanonical report bytes, changed inputs, and extra evidence files.

## Adversarial review

The first report bound only raw-hash strings copied into performance metadata. Review demonstrated that deleting or replacing the raw artifact could otherwise leave the release closure apparently valid. The final architecture adds one `RenderingReleaseRawEvidence` record and reopens the raw file through the existing v2 validator. No new cache, general release framework, package copy, signature, hardware database, or second manifest was introduced.

The apparent Debug/Release Shader hash mismatch was also investigated. It is the explicit compiler contract (`Debug`: embedded symbols and disabled optimization; `Release`: optimized artifact), not nondeterministic output. Cross-configuration equality is therefore not imposed.

## Verification

- Focused performance/release/application contracts: 16/16 Debug.
- Release Stage also executes 5/5 performance-probe tests, 4/4 release-evidence tests, and the shared Preview Host closure.
- Debug `rendering.release`: `20260901T150106776Z-rendering-release-closure-c57dad475709486596ada0090e47f311`.
- Release `rendering.release`: `20260901T150243962Z-rendering-release-closure-3a2f2c97ad934616921bd25c2133b60a`.
- Debug report SHA-256: `da430644f48dba587388140f3ff3e0d03ff1a6b33341a058f31299bfde84d96f`.
- Release report SHA-256: `cf06add71007dd4a10b32104b3716c34fea214fa4e393c1b09309485593f0aa2`.
- Debug raw SHA / Shader SHA: `032626d8f35a226d89e6813589208908259e28e15d85e44711641411d25a2631` / `6ef8c2afb7808bfecba0246f850c32aae0820172464220defcf8fa5b830da32e`.
- Release raw SHA / Shader SHA: `022f844c0d8a259fd6fc15ef54bb0bf1df16f82d1dc1d32fc82981b41a9575fc` / `e53babd26c14212d0322117ddaa1090f558cc55d4bc2011a75c2df7b7c707d0b`.
- Release WARP visibility: median 4,118,550 ns, p95 4,490,200 ns, allocation p95 0.

All Rendering Roadmap Features are now Implemented. Product-wide package, compatibility, RC, and immutable manifest ownership remains F-L7-045 through F-L7-048.
