# M0074 — VSIX Asset Authoring

## Outcome

F-L8-019 is implemented as one typed boundary: the net472 VSIX displays and requests asset metadata operations, while the net10 Content host remains the only canonical metadata implementation and writer.

## Invariants

- Original asset bytes are never mutated by metadata authoring.
- `<source>.meta` is either absent or canonical UTF-8 produced by `AssetMetadata`.
- Existing `AssetId` values are immutable.
- Save succeeds only against the metadata SHA-256 observed by the caller.
- Invalid, corrupt, unsupported, escaping, stale, or uncorrelated input publishes no bytes.
- Resident and one-shot execution use the same worker and typed DTOs.
- The VSIX owns neither the metadata codec, provider SDK, Content state, nor renderer.
- A late operation result cannot replace the presentation for a newer asset path.

## Architecture

`AssetAuthoringHostClient` serializes bounded request DTOs and invokes `CSharpGameEngine.Import.AssetAuthoring.Cli` through the existing versioned authoring-host lifecycle. The worker delegates to `AssetAuthoringService`, atomically publishes a typed result, and treats domain rejection as a valid transport result. Asset Preview projects the correlated result and uses the shared Render Preview Host for WARP rendering.

## Verification

The complete Contracts suites pass 17/17, Content 33/33, Bridge Hosting 24/24, VSIX 974/974, FeatureRunner 76/76, and solution registration 3/3 in both Debug and Release. The `vsix.asset-authoring` Visual Stage owns contract, Content service, bridge worker, VSIX presentation/architecture/localization tests and the VSIX reference Scene on WARP. It passed Debug as `20260901T121013501Z-full-d5a97f569e814842b8a74c067d0e824c` and Release as `20260901T121126050Z-full-0434ea4bc7ed409e9514a11e96c3d64e`.
