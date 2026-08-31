# Reverse-engineered specification

## RE-M0064 - Visible Engine Showcase and deterministic input

- Intended source: ADR-0062, M0064, Audio/Input/Platform, ECS Runtime, Rendering, and Feature Stage specifications.
- Evidence: portable Core input contracts, Win32InputEventAdapter/Win32Window, InputServer, generated Showcase manifest, EngineShowcaseScene/ShowcaseSystems, Win32DashboardPanel, Showcase Automation, and the four executable M0064 stages.
- Observed behavior: one QPC-ordered event stream is finalized in FrameInput and drives stable-ID Showcase actions. ECS motion advances only on completed FixedTicks. The same window displays the D3D12 gallery and a localized 360-DIP GDI child dashboard whose title exposes page/tick/playback/manifest hash. Progress reports Feature and Stage ratios separately.
- Result: Matches ADR-0062/M0064 for F-L1-008, F-L0-010, F-L4-010, and F-L9-009. The gallery labels incomplete prefracture/cache behavior through Catalog status rather than presenting it as completed Physics.

## RE-M0062 - Structured Shader Graph regions

- Confidence: High.
- Evidence: ShaderGraphRegion schema/canonicalization, BuiltInNodeCatalog, ShaderGraphValidator, ShaderGraphEmitter, VSIX editor document/control, template/dependency clone paths, and focused tests.
- Observed behavior: Existing regions default to non-semantic comments. If and ForRange require owned control nodes and explicit branch membership; parent-region chains permit structured nesting but reject cycles and invalid parent branches.
- Observed behavior: Outer values may feed branches, while branch-local values cannot escape or cross siblings. ForRange is statically bounded, LoopIndex resolves the nearest active loop, and Return assigns a compatible Subgraph output before exiting.
- Result: Matches ADR-0060/M0062 for F-L2-005 and F-L7-049. F-L8-031 remains Partial only for Experimental Instance automation evidence.

## RE-M0061 - External Shader Subgraph composition

- Confidence: High.
- Evidence: Stage root/dependency Graph JSON, ShaderGraphDependencyComposition, CLI cache validation, CgeShaderSubgraph MSBuild inputs, VSIX dependency UI/template bundle logic, and focused tests.
- Observed behavior: Recursive composition preserves original translation-unit declaration slots and exact HLSL bytes. Missing files, identity mismatch, recursion, and slot/node collisions fail before DXC.
- Observed behavior: Root semantic identity remains stable when only dependency content changes; recursive DependencyHash invalidates the artifact. Matching validated provenance and ABI entries produce an `up-to-date` build without DXC.
- Result: Matches ADR-0059/M0061. Structured region authoring and Experimental Instance automation remain outside this closure.

## RE-L2-005 / RE-L7-049 / RE-L6-004 / RE-L8-031 — Visual Shader Graph pipeline

- Confidence: High.
- Evidence: CSharpGameEngine.ShaderGraph, CSharpGameEngine.ShaderArtifacts, CSharpGameEngine.ShaderGraph.Cli, ShaderArtifactRuntime, the three D3D12 renderers, MSBuild target, VSIX ShaderGraphEditor files, production Graph JSON, and their test projects.
- Observed behavior: Production graph files contain stable GUID syntax nodes and typed child edges rather than raw HLSL payloads. Canonical layout exclusion gives stable semantic hashes. The compiler regenerates byte-identical HLSL and fixed-entry artifacts for identical input.
- Observed behavior: Runtime validates target, ABI, entries, profiles, reflection, and checksum before PSO creation. Debug watches artifacts and preserves the old PSO on load or creation failure; Release excludes watch behavior.
- Observed behavior: VSIX registers both file extensions and packages localized templates. Source save is independent from successful artifact publication; diagnostics route to the Error List.
- Result: Matches for artifact/runtime and Rendering baseline. Partial for first-class high-level region authoring and Experimental Instance automation; external production Subgraph composition and dedicated navigation now Match ADR-0059.

## RE-L7-021 addendum — Catalog v2 non-physics registration

- Confidence: High
- Evidence: `tools/CSharpGameEngine.FeatureRunner/FeatureStageContracts.cs`, `FeatureStageCatalog.cs`, `FeatureRunnerApplication.cs`, `tests/CSharpGameEngine.FeatureRunner.Tests/`.
- Observed behavior: The catalog now contains `Specified`, a track enum, dependency Levels, and dependency Feature IDs. It validates unknown/self/cyclic/Level-inverted dependencies and requires an executable Stage for implemented states and a `NotImplemented` Stage for specified/deferred states. `ecs.baseline` and `ecs.fixed-tick` are executable.
- Observed behavior: `list --json` emits catalog schema v2 metadata. Run-result artifacts remain schema v1. Explicit future `run`/`run-feature` execution returns `NotImplemented` and nonzero; normal suites continue to select executable stages only.
- Observed behavior: Sixty-seven non-physics Roadmap Feature IDs are registered. Implemented target stages include ECS/fixed-tick, Shader Graph/artifacts, VSIX Experimental evidence, deterministic input, and the visible Showcase; remaining future target stages retain explicit NotImplemented or deferred handling.
- Intended behavior: Matches ADR-0057 and [Testing-CI-Release](Specifications/Testing-CI-Release.md) for the registration infrastructure.
- Result: MatchesWithDocumentedExtension. The target runtime bodies described by the six new specifications are still unimplemented and must not be inferred from the registration.

## RE-L7-030 / RE-L3-018 — ECS baseline and fixed-tick shipping loop

- Confidence: High.
- Evidence: `Runtime.cs`, `SimulationSystems.cs`, `FixedTickScheduler.cs`, `GameSystemLoopEmitter.cs`, `GameSystemGeneratorEndToEndTests.cs`, `FixedTickSchedulerTests.cs`, and the executable `ecs.baseline`/`ecs.fixed-tick` stages.
- Observed behavior: `State.FixedTick` advances only after a complete FixedSimulation/PhysicsSimulation/PostPhysics tick. Each host frame executes FrameInput, global spatial refresh, and Presentation once; simulation runs 0..8 times from a 60 Hz accumulator. Pause ignores elapsed counter units, Step queues work while paused and consumes one tick per host frame, and interpolation reports the retained accumulator boundary. Clamp entry emits one `CGE-FIXED-TICK-OVERRUN` diagnostic with the attempted count, cap, backlog, and `RetainBacklog` policy.
- Observed behavior: Generated `Run` owns Stopwatch sampling. `RunFrame` accepts the scheduler and explicit elapsed counter/frequency; Editor Preview calls the shared simulation-tick helper explicitly and does not perform wall-clock catch-up.
- Result: Matches for F-L7-030 and F-L3-018 fixed-tick scope. Physics solver/cooking/bake/UI behavior remains outside this result.

## RE-L3-015/016 - Spatial partitioning

- Confidence: High
- Evidence: `SpatialPartitioning.cs`, `SpatialWorld.cs`, `SpatialWorldEmitter.cs`, and their tests.
- Observed behavior: the octree uses deepest-containing loose-node placement, expands its root without automatic shrink, and queries caller-owned result lists. Generated systems refresh a `Transform + SpatialBounds` World snapshot after structural playback.
- Result: Match.

## RE-L3-018 / RE-L5-002-003 — Simulation and Physics foundation

- Confidence: High for phase/Q32.32/API/cache; Medium for basic contact behavior.
- Evidence: generated loop source, shared Physics source, and selected automated tests.
- Observed behavior: generated loops retain system structs and execute phase barriers. Dynamic Physics bodies preserve solver pose across ECS synchronization, integrate gravity at 60 Hz, use stable-ID sorted storage and a separate AABB tree. Debug data is only enumerated after a pipe request. Pause/step/reset control the same world used by Runtime and Editor Preview.
- Result: Matches for the fixed-tick/ECS portion; Partial against the full v1 physics contract because advanced narrowphase, CCD, destruction playback and real Scene bake are absent.

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
# RE-M0063 - VSIX Experimental evidence and Shader Graph UI automation

- Observed implementation: `CSharpGameEngine.Vsix.Automation` enforces the exact `CgeAutomation` suffix, discovers `devenv.exe` through vswhere, verifies package-contained/build/installed DLL hashes, records all three hashes in v1 evidence, drives PID-owned UIA windows, and maps exit 2 to InfrastructureError.
- Observed editor behavior: stable `Cge.ShaderGraph.*` IDs and TransformPattern move provide layout mutation; canonical save preserves semantic hash; clean external save reloads; dirty save exposes Reload and Keep; invalid Graph diagnostics appear and clear after repair.
- Observed execution: Debug and Release 1041 each passed 19 assertions, captured screenshots and ActivityLog identity, retained normal VS/CgeFixed PIDs, and exited without forced termination.
- Result: Matches ADR-0061/M0063. F-L8-020 and F-L8-021 are TestVerified; F-L8-031 is no longer Partial.
