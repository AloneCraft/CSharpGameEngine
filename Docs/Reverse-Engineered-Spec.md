# Reverse-engineered specification

## RE-L7-021 addendum — Catalog v2 non-physics registration

- Confidence: High
- Evidence: `tools/CSharpGameEngine.FeatureRunner/FeatureStageContracts.cs`, `FeatureStageCatalog.cs`, `FeatureRunnerApplication.cs`, `tests/CSharpGameEngine.FeatureRunner.Tests/`.
- Observed behavior: The catalog now contains `Specified`, a track enum, dependency Levels, and dependency Feature IDs. It validates unknown/self/cyclic/Level-inverted dependencies and requires an executable Stage for implemented states and a `NotImplemented` Stage for specified/deferred states.
- Observed behavior: `list --json` emits catalog schema v2 metadata. Run-result artifacts remain schema v1. Explicit future `run`/`run-feature` execution returns `NotImplemented` and nonzero; normal suites continue to select executable stages only.
- Observed behavior: Sixty-three non-physics Roadmap Feature IDs are registered. Two existing IDs reuse their Stage IDs and sixty-one new target Stage IDs have no command. New Rendering/VSIX visual reservations alias existing C# reference scenes.
- Intended behavior: Matches ADR-0057 and [Testing-CI-Release](Specifications/Testing-CI-Release.md) for the registration infrastructure.
- Result: MatchesWithDocumentedExtension. The target runtime bodies described by the six new specifications are still unimplemented and must not be inferred from the registration.

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

## RE-L8-017 — Scene Designer common Escape handling

- Confidence: High for routing policy and selection service; Medium for native Visual Studio command suppression pending Experimental Instance verification.
- Evidence: `SceneEditorEscapeRouter.cs`, `SceneEditorEscapeRoutingPolicy.cs`, the four panel controls, `SceneEditorSessionContracts.cs`, `SceneEditorEscapeRoutingPolicyTests.cs`, `SceneEditorSessionContractsTests.cs`.
- Observed behavior: each Scene Designer panel installs one focus-scoped router. It consumes Esc through WPF preview events and its owning HwndSource hook, latches repeated key-down events, and resets the latch on key-up, focus loss, unload, or dispose. Viewport cancellation wins over selection clearing; text/rename/combo/search cancellation wins in the corresponding panels. Empty selection is published only when a semantic selection exists.
- Result: Matches automated contract; native VS tab-activation regression requires manual Experimental Instance verification.

## RE-L7-021 — Autonomous Feature Runner

- Confidence: High.
- Evidence: `tools/CSharpGameEngine.FeatureRunner/`, `tests/CSharpGameEngine.FeatureRunner.Tests/`, `stages/`, Debug/Release test and Pack runs, `list --json`, `stage-packs.contract`, and explicit `run-feature F-L5-004` execution.
- Observed behavior: startup validation rejects catalog inconsistencies. Existing stages launch child processes without a shell, capture stdout/stderr, enforce per-Stage timeout, append WARP arguments where declared, and hash captured evidence. `changed` uses Git porcelain paths; other suites select only executable stages by profile. A result document is always versioned and saved for an executed selection. `resume` selects prior non-passing Stage IDs. Deferred stages do not start a process and produce `NotImplemented`. Ten isolated Pack projects compile handwritten C# `IScene` definitions and generated ECS glue, then verify entity ownership and unload in separate processes.
- Result: Matches ADR-0051 for F-L7-021 and F-L9-008 infrastructure. Future solver behavior remains explicitly deferred.

## RE-L3-020 — Current static ECS mask limit

- Confidence: High.
- Evidence: `GameSystemIndexAssigner`, `GameSystemLoopEmitter`, `SceneArchetypeIntegrationTests`, and generated loop structure.
- Observed behavior: Component indices 0–63 are represented by one signed 64-bit value during generation. A 65th type emits SYSGEN026. The mask is used to decide which direct Archetype loops to emit and is absent from generated Runtime matching.
- Result: Matches the current 64-type contract; the two-mask path is deferred.
