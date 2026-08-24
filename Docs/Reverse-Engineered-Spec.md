# Reverse-engineered specification

## RE-L3-015/016 - Spatial partitioning

- Confidence: High
- Evidence: `SpatialPartitioning.cs`, `SpatialWorld.cs`, `SpatialWorldEmitter.cs`, and their tests.
- Observed behavior: the octree uses deepest-containing loose-node placement, expands its root without automatic shrink, and queries caller-owned result lists. Generated systems refresh a `Transform + SpatialBounds` World snapshot after structural playback.
- Result: Match.

## RE-L3-018 / RE-L5-002-003 — Simulation and Physics foundation

- Confidence: High for phase/Q32.32/API/cache; Medium for basic contact behavior.
- Evidence: generated loop source, shared Physics source, and selected automated tests.
- Observed behavior: generated loops retain system structs and execute phase barriers. Dynamic Physics bodies preserve solver pose across ECS synchronization, integrate gravity at 60 Hz, use stable-ID sorted storage and a separate AABB tree. Debug data is only enumerated after a pipe request. Pause/step/reset control the same world used by Runtime and Editor Preview.
- Result: Partial against the full v1 contract because advanced narrowphase, CCD, destruction playback and real Scene bake are absent.

## RE-L8-015 — VSIX English/Japanese localization

- Confidence: High for source/resource/package structure; visual layout requires LCID smoke.
- Evidence: `VsixLocalization`, `VsixStrings`英日resource、XAML `LocExtension`、culture別VSCT/VSPackage、VSIX language pack、1033/1041 Scene template、成果物検査テスト。
- Observed behavior: Package初期化時のVisual Studio UI cultureをVSIX内部だけへ固定し、未対応cultureは英語neutralへフォールバックする。Named Pipe statusは理由enumとwire tokenを分離し、Scene Viewで全文を組み立てる。Build identityはinvariant。
- Result: Matches automated contract. `/LCID 1033`と`/LCID 1041`での画面目視はpending。
