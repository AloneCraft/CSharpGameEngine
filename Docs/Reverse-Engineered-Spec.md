# Reverse-engineered specification

## RE-M0088 — Typed lighting and shadow ownership
- Confidence: High for automated code, static editing and WARP behavior; no interactive Visual Studio session is claimed.
- Evidence: LightValues/LightPhotometry, Runtime.Lighting/LightComponents, RuntimeSystems, LightSubmissions, RenderFrameState, ImportedLight, LightValueContract and LightConversionPlanner.
- Observed: physical values are validated readonly floats; only applicable fields exist on four ECS structs; optional LightShadow owns shadow requests; invalid extraction records an Entity-local companion property.
- Observed authoring: exact Roslyn constructor matching reads named/target-typed literal values without executing code. Constructor and field initializers supply palette defaults. 12 conversions preserve local flux, require explicit directional quantities, retain Node/shadow ownership, reject expression/default-contract conflicts and pass canonical source verification.
- Observed rendering: typed reusable buffers retain mixed submission order (including directional replacement) and separate emissive records. Rectangle forward/up retains roll without multiplying dimensions by Scale. WARP pixels match after two device recoveries. Warm recording of 10000 batches allocates zero bytes.
- Import: glTF cd/lux, optional range and source cone boundaries are preserved; zero-inner conversion is explicitly rejected by the stricter authored value type.
- Verification commands/results: [M0088](Milestones/M0088-Typed-Lighting.md). Intended contract: [Typed Lighting](Specifications/Typed-Lighting.md).

## RE-M0087 - Immutable formal release

- Confidence: High for implementation and focused evidence; final environment publication is pending F-L8-029.
- Evidence: `ReleaseManifestEvidence`, five immutable/publication tests, executable `release.manifest`, and Debug/Release Catalog/CLI regression.
- Observed behavior: one passing candidate Stage/report, five validated track reports, the product package report, exact archive/signing inputs, inner package-manifest/SPDX hashes, and current Git provenance derive one SHA-256 release ID.
- Observed storage behavior: publication atomically creates one self-contained `.artifacts/Releases/<release-id>` directory. Same-identity publication is byte- and timestamp-preserving; an invalid existing identity is not repaired or overwritten.
- Observed failure behavior: wrong Stage window/provenance, missing report, package/VSIX disagreement, changed source, path escape/reparse, package/SPDX failure, noncanonical manifest, changed/extra released bytes, or partial publication fails closed.
- Result: Matches F-L7-048 implementation. RuntimeVerified awaits the environment-complete RC.

## RE-M0086 - Level-gated Release Candidate

- Confidence: High for implementation and focused evidence; final environment execution is pending F-L8-029.
- Evidence: `ReleaseCandidateEvidence`, five closure/failure tests, executable `release.candidate`, and Debug/Release Catalog/CLI regression.
- Observed behavior: Catalog track, Level, dependency, lifecycle, and Stage ownership derive one ordinal executable closure. Only dedicated-profile Release/WARP/current-Git results within twenty-four hours may satisfy it.
- Observed ownership behavior: Stage result files remain execution truth. The RC report stores only their exact selected identity, file/artifact hashes, provenance, and fixed contract inputs.
- Observed failure behavior: Specified/Deferred/control Features, missing/failed/stale/ordinary-profile/wrong-Git results, incomplete assertions, artifact/input mutation, path escape, duplicate/extra evidence, and noncanonical bytes cannot become a pass.
- Result: Matches F-L7-047 implementation. RuntimeVerified awaits the environment-complete prerequisite run.

## RE-M0085 - Product release compatibility scope

- Confidence: High.
- Evidence: `Release-Compatibility-Matrix.json`, `ReleaseCompatibilityMatrixValidator`, four fail-closed tests, and passing Debug/Release `release.compatibility` Stage results.
- Observed behavior: one Windows 10 2004+ x64, .NET 10, net472 VSIX, WARP-certified product scope references exactly eight required CI cells, six required Platform cells, eight required VSIX release checks, two optional hardware cells, and four executable compatibility/package contract Stages.
- Observed ownership behavior: the aggregate stores only IDs and canonical source paths. It contains no Passed/Pending state, evidence path, observation time, expiry, hardware probe, or copied Stage verdict; each referenced matrix, ledger, and run result remains authoritative for its own data.
- Observed failure behavior: schema, target, source path, missing, extra, duplicate, optional-to-required promotion, source drift, and non-executable Stage contradictions return sorted errors. The CI, Platform, and VSIX source validators run first, so an unchanged aggregate cannot hide source corruption.
- Result: Matches F-L7-046.

## RE-M0084 - Product package, SPDX, and isolated lifecycle evidence

- Confidence: High.
- Evidence: `ReleasePackageEvidence`, nine closure/failure/Stage-isolation tests, the publish-target corrections, canonical Debug/Release package reports, and final `release.packages` Stage results.
- Observed behavior: each configuration publishes one deterministic Windows x64 portable archive containing the framework-dependent .NET 10 viewer CLI, the sole packable CodeAnalysis analyzer NuGet, the exact prebuilt localized VSIX and Preview Host, the Assimp native payload, and generated Shader Graph artifacts. PDB files and development paths are excluded.
- Observed integrity behavior: the ordinal/fixed-time ZIP, exact file manifest, canonical SPDX 2.3 file/dependency document, restored NuGet package bytes, signing-input closure, repository inputs, and complete evidence-directory closure are independently SHA-256 revalidated.
- Observed lifecycle behavior: validation safely extracts to a unique isolated directory, runs the packaged CLI for one hidden WARP frame with readback and zero D3D12 validation errors, independently recomputes the installed byte closure, and removes the installation on success or failure with zero residue.
- Observed failure behavior: missing, extra, changed, escaped, duplicate, reordered, non-reproducible, development-path, partial, stale, or noncanonical evidence fails closed without retaining a partial evidence root.
- Ownership boundary: this Stage owns the portable product package, SPDX, external signing inputs, and install/run/uninstall evidence. It neither rebuilds nor repacks the VSIX and does not own compatibility aggregation, an applied signature, or the formal immutable release manifest.
- Result: Matches F-L7-045.

## RE-M0083 - ECS runtime release evidence

- Confidence: High.
- Evidence: `EcsReleaseEvidence`, four closure/failure tests including clean-after-generation validation, dedicated Debug/Release prerequisites, canonical reports, and final `ecs.release` Stage results.
- Observed behavior: each configuration accepts only recent matching-Git WARP replay/schedule/snapshot/performance results under four exact release profiles, then binds their Stage files/artifacts, four raw captures, four common performance reports, twenty-two sources, machine, target, and repository provenance.
- Observed lifecycle behavior: generated output is independently hashed during release capture but is not retained as a second truth. Deleting derived benchmark `obj` after report creation does not invalidate the report; retained Stage/raw/performance evidence and the canonical generator input closure remain sufficient.
- Observed failure behavior: prerequisite, profile, provenance, assertion, raw/performance boundary, source/state, timing/budget, machine, input, file hash, path, canonical encoding, and directory-closure contradictions fail without partial success evidence.
- Ownership boundary: this is an ECS-track release input. It creates no runtime owner, package, SBOM, signature, installer, compatibility matrix, or immutable product manifest.
- Result: Matches F-L7-033.

## RE-M0082 - ECS runtime performance evidence

- Confidence: High.
- Evidence: the parameterized generated ECS benchmark, independent raw validator, four contract tests, exact-64 and 65/128 generator E2E tests, four canonical Release performance reports, and final Debug/Release `ecs.performance` results.
- Observed behavior: separate 1/64/65/128 Component compilations each run the highest-index generated write query over 100,000 live entities for exactly five warm-ups and thirty samples with Tiered JIT disabled. One and 64 contain one mask word and no generated high-word runtime member; 65 and 128 contain two and execute the high Component.
- Observed integrity behavior: the producer publishes raw evidence atomically. The consumer recomputes the exact final state hash, independently rehashes and inspects each persisted generator output, validates every sample, and publishes common machine/Git/raw-bound reports whose statistics and verdicts are independently recomputed.
- Observed performance: Release 1/64/65/128 median is 0.451/0.504/0.507/0.563 ms and p95 is 0.599/0.686/0.679/0.899 ms for 100,000 entities; every sample allocates zero managed bytes. All pass the 6 ms median and 8.33 ms p95 limits, and 64 remains within the 20-percent cross-process regression alarm from one type.
- Observed failure behavior: boundary omission/duplication/addition, source/state/hash/shape disagreement, invalid runtime/query/workload/sample, any allocation, budget regression, generated-output change, or common-report corruption fails closed.
- Ownership boundary: this Stage measures the actual static generated World and owns no alternative ECS, runtime cache, dynamic mask matcher, release package, or product manifest.
- Result: Matches F-L7-032.

## RE-M0081 - Rendering release evidence

- Confidence: High.
- Evidence: `RenderingReleaseEvidence`, raw/performance report validators, four failure/closure tests, dedicated performance/preview results, canonical Debug/Release reports, and final `rendering.release` Stage results.
- Observed behavior: each configuration accepts only recent matching-Git WARP prerequisites under `rendering-release-performance` and `rendering-release-preview`, then binds their Stage artifacts, the actual raw file, both performance reports, one Shader Artifact, nineteen sources, machine, target, and repository provenance.
- Observed failure behavior: ordinary profiles, missing/stale results, Stage tampering, raw removal/change, metadata/hash/Shader disagreement, performance verdict change, source change, path escape/reparse, noncanonical report bytes, and extra files fail closed without partial success evidence.
- Observed configuration contract: Debug and Release Shader Artifact bytes differ intentionally because the compiler embeds debug information and disables optimization only in Debug. Each release report requires one internally consistent artifact; it does not impose false cross-configuration byte equality.
- Ownership boundary: this is a Rendering-track evidence snapshot. It owns no runtime resource, package, signature, SBOM, installer, compatibility database, or immutable product manifest.
- Result: Matches F-L7-024.

## RE-M0080 - Rendering performance evidence

- Confidence: High.
- Evidence: `RenderVisibility`, `RenderingPerformanceRawCapture`, `RenderingPerformanceProbe`, contract/fast-path tests, canonical performance reports, and Debug WARP, Release WARP, and Release hardware Stage results.
- Observed behavior: exactly five warm-ups precede thirty measured hidden 1280x720 frame and 100,000-instance selection/batching samples. Raw evidence records adapter, Tiered-JIT-disabled runtime, debug-layer count, pixel oracle, time, and allocation; common reports bind its SHA-256 plus build, Git, and machine provenance.
- Observed performance: after removal of unconditional sorting for canonical input, Release WARP visibility is 5.086 ms median/6.650 ms p95 and Release RTX 4060 Ti visibility is 4.710 ms median/5.694 ms p95, with zero allocation p95. Hardware frame p95 is 8.691 ms.
- Observed failure behavior: missing/extra/duplicate/bad-index samples, non-measurable time, allocation, configuration/adapter/runtime mismatch, D3D12 error, pixel failure, report tampering, and budget failure return nonzero and cannot publish a passing Stage.
- Ownership boundary: WARP proves correctness rather than hardware speed; Debug timing is diagnostic; only optimized Release supplies shipping timing gates. The performance Stage creates no runtime owner, cache, device state, or release manifest.
- Result: Matches F-L7-023.

## RE-M0079 - Platform release evidence

- Intended source: F-L7-035 in Audio/Input/Platform, after F-L7-034.
- Evidence: PlatformReleaseEvidence, five failure-path and closure tests, dedicated prerequisite results, canonical Debug/Release reports, and platform.release run results.
- Observed behavior: each configuration re-runs platform.compatibility under the release-only profile and binds its exact file/result hash, configuration, Git state, completion time, Warp adapter, Platform source/verification closure, required modes, machine fingerprint, and optional hardware ledger snapshot.
- Observed failure behavior: missing/stale/wrong-profile prerequisites, tampered sources or Stage files, noncanonical or extra report bytes, invalid hashes/timing/architecture, path escape/reparse, and changed hardware rows fail closed without a partial success directory.
- Ownership boundary: this report is a Platform track input to later release aggregation. It does not duplicate content packages, CI topology, manual status, device state, signing, SBOM, installation, or the immutable product manifest.
- Result: Matches F-L7-035.

## RE-M0079 - Platform release evidence

- Intended source: F-L7-035 in Audio/Input/Platform, after F-L7-034.
- Evidence: PlatformReleaseEvidence, five failure-path and closure tests, dedicated prerequisite results, canonical Debug/Release reports, and platform.release run results.
- Observed behavior: each configuration re-runs platform.compatibility under the release-only profile and binds its exact file/result hash, configuration, Git state, completion time, Warp adapter, Platform source/verification closure, required modes, machine fingerprint, and optional hardware ledger snapshot.
- Observed failure behavior: missing/stale/wrong-profile prerequisites, tampered sources or Stage files, noncanonical or extra report bytes, invalid hashes/timing/architecture, path escape/reparse, and changed hardware rows fail closed without a partial success directory.
- Ownership boundary: this report is a Platform track input to later release aggregation. It does not duplicate content packages, CI topology, manual status, device state, signing, SBOM, installation, or the immutable product manifest.
- Result: Matches F-L7-035.

## RE-M0078 - Platform compatibility evidence

- Intended source: F-L7-034 in Audio/Input/Platform, constrained by the existing CI matrix, workflow, Feature Stage catalog, and Manual Evidence ledger.
- Evidence: `Platform-Compatibility-Matrix.json`, `PlatformCompatibilityMatrixValidator`, malformed-matrix regression tests, and Debug/Release `platform.compatibility` results.
- Observed behavior: six required rows reference the exact solution, Windows-TFM, and WARP Debug/Release CI cells; seven prerequisite Input/Audio/diagnostics Stages must be executable. Two optional rows reference only the existing hardware Audio/GPU manual entries.
- Observed ownership: CI/workflow own required execution topology, Manual Evidence owns hardware observation state, the Platform matrix owns track scope, and run-result artifacts own configuration-specific verdicts. No Passed/Pending state is mirrored.
- Observed failure behavior: missing, extra, duplicate, empty, mismatched, optional-promoted, wrong-owner, wrong-adapter, or non-executable references fail closed with validation errors. Optional hardware cannot replace required synthetic, Silent, or WARP correctness.
- Result: Matches F-L7-034 without a hardware probe, second CI description, dynamic compatibility database, or premature product-wide release aggregation.

## RE-M0076 - Content release evidence

- Confidence: High.
- Evidence: `ContentReleaseEvidence`, `ShippingContentManifest`, `ShippingContentPackage`, the common performance report store, `ContentReleaseEvidenceTests`, and the Debug/Release `content.release` run results.
- Observed behavior: one canonical report binds the production package manifest fingerprint and exact file closure, public schema versions plus their owning source hashes, a fixed project/toolchain dependency input set, Git provenance, and recent passing configuration-matched cold and one-change measurements. Validation reopens the package and recomputes every file, source, report, and payload hash.
- Observed failure behavior: missing, extra, stale, relocated, reparse-point, non-canonical, or modified evidence fails without publishing a partial success directory. Required-performance absence is an oracle failure rather than an argument error.
- Ownership boundary: the content package remains the runtime-loadable truth. This report supplies bounded content evidence and SBOM inputs; it does not implement SPDX JSON, signing, OS installation, or the formal immutable release manifest.
- Result: Matches F-L7-029.

## RE-M0075 - Content performance evidence

- Intended source: F-L7-028 in Asset/Content and the common performance framework.
- Evidence: production `IncrementalContentCompiler`, canonical Content CAS/artifact codec, `ContentPerformanceProbe`, integrity-validated performance reports, focused compiler/framework tests, and executable Debug/Release `content.performance` results.
- Observed behavior: a cold 32-root workload creates fresh state for every sample. A 33-node incremental workload changes one leaf and refuses any result other than two rebuilt and 31 reused nodes. Both run five warm-ups plus thirty measured samples and gate elapsed and allocation p95.
- Observed evidence: report payload and machine fingerprint hashes, sample cardinality/indexes, deterministic statistics, budgets, verdict, git provenance, and configuration are independently validated after publication.
- Result: Matches F-L7-028; measurements exercise the production compiler rather than a synthetic algorithm or alternate cache.

## RE-M0074 - VSIX asset authoring

- Intended source: F-L8-019 in Asset/Content and Editor/VSIX; original assets and canonical `.meta` are authoring truth, while the versioned host result is the UI execution truth.
- Evidence: `AssetAuthoringProtocol`, Content-owned `AssetAuthoringService`, the isolated asset-authoring CLI worker, thin net472 `AssetAuthoringHostClient`, Asset Preview presentation/UI, focused boundary tests, and executable `vsix.asset-authoring` WARP Stage.
- Observed behavior: missing supported assets receive an unpersisted candidate identity; Create sends that identity and inspected metadata hash to the host. Existing IDs cannot change. Canonical source URI/importer/options are published atomically only after validation; malformed/noncanonical metadata and stale hashes leave both source and prior sidecar bytes unchanged.
- Observed lifecycle: the VSIX owns no metadata codec or Content state. Resident and one-shot paths run the same worker. Request/result correlation and bounded collections fail closed. Model switches use operation generations so late results cannot overwrite the active asset presentation.
- Result: Matches F-L8-019 without a second metadata source, compatibility reader, in-process provider SDK, or VSIX-owned renderer.

## RE-M0071 - Bake, asset, and diagnostic UI

- Intended source: F-L8-026 in the Editor/VSIX specification; the versioned authoring-host DTOs and terminal result are the execution truth, while C# source remains the authoring truth.
- Evidence: `AuthoringOperationSnapshot`, `ResidentBridgeClient.TryRunDetailed`, `AuthoringHostOperationException`, `PhysicsSceneBakeService`, the Scene Designer Bake surface, Asset Preview inline operation surface, focused VSIX tests, common host tests, and executable `vsix.bake-integration` Debug/Release results.
- Observed behavior: Import and Bake publish request-correlated monotonic progress and typed diagnostics through one immutable UI projection. Physics Bake can cancel, preserves completed output paths after later failure/cancel, and exposes selected artifacts without owning a second build implementation. Asset Preview retains typed external-host diagnostics and recovered Render Server lifecycle state inline.
- Observed validation: protocol/request mismatch, backward or non-finite progress, invalid terminal progress, nonzero exit, or an error diagnostic cannot become success. Late progress is ignored after cancellation begins.
- Result: Matches F-L8-026.

## RE-M0070 - C# source synchronization v2

- Intended source: F-L8-025 in the Editor/VSIX specification; C# `IScene` and Visual Studio native text history are the sole authoring and Undo truths.
- Evidence: `SceneSourceCommitPlanner`, `SceneSaveVerification.TryVerifyAndAnalyze`, the source-first `SceneEditorSessionService`, the atomic Workspace/TextBuffer writer, async Hierarchy/Inspector/Viewport callers, focused sequence/architecture tests, complete VSIX suites, and executable `vsix.source-sync` Debug/Release results.
- Observed behavior: one completed gesture resolves the latest Roslyn model, explicitly rebases supported stale deltas or rejects stale unknown-origin member writes, produces and verifies one minimal source edit, and opens at most one native Undo transaction. The landing snapshot is re-derived from the edited C# tree and uses the operation projection only to preserve newly allocated node identity. Consecutive gestures before or after Roslyn landing therefore keep `EditorNodeId` and do not self-rebase.
- Observed lifecycle: Save only persists the live C# document; Undo/Redo calls native text history and schedules reanalysis. Failure or cancellation clears the transient landing basis. No editable Scene document, mirrored Undo stack, buffer watermark, save-time replay, external landing state machine, or multi-step source writer remains.
- Result: Matches F-L8-025. Host-backed Bake/diagnostic UI remains F-L8-026 and does not acquire another source writer.

## RE-M0068 - Typed Inspector and Component Palette contracts

- Intended source: F-L8-024 in the Editor/VSIX specification and the canonical `LogicalScalar`/`float` numeric boundary.
- Evidence: `SceneInspectorModel`, `InspectorMemberViewModel`, the Inspector WPF choice binding, component catalog/initializer/source editor policies, their focused VSIX tests, and executable `vsix.inspector-contracts` Debug/Release results.
- Observed behavior: Inspector control type follows the declared component field/property type rather than the right-hand literal type. Enum and `[SceneAssets]` references share one bounded candidate model; only exact-type immutable members from the referenced asset container can emit source. Unknown and unchanged choices produce no edit request. `double` and `decimal` fields cannot masquerade as integer editors when initialized from an integer literal.
- Observed behavior: `ReadOnly` and `IsEditable` cannot disagree. Existing nested vector, text, boolean, component add/remove/reorder/drop, minimal source mutation, and reparse contracts run under the same Stage.
- Result: Matches F-L8-024. Physical asset import/existence authoring remains F-L8-019; the completed one-gesture source conflict convergence is owned by F-L8-025 rather than being hidden inside the Inspector.

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
- Observed behavior: Component indices 0–63 use the low `ulong` during generation; indices 64–127 use a high `ulong`. A 129th distinct type emits SYSGEN026. Both paths select direct Archetype loops at generation time, so generated Runtime performs no mask matching.
- Result: Matches the implemented 128-type contract while preserving the 1–64 single-mask path.
# RE-M0063 - VSIX Experimental evidence and Shader Graph UI automation

- Observed implementation: `CSharpGameEngine.Vsix.Automation` enforces the exact `CgeAutomation` suffix, discovers `devenv.exe` through vswhere, verifies package-contained/build/installed DLL hashes, records all three hashes in v1 evidence, drives PID-owned UIA windows, and maps exit 2 to InfrastructureError.
- Observed editor behavior: stable `Cge.ShaderGraph.*` IDs and TransformPattern move provide layout mutation; canonical save preserves semantic hash; clean external save reloads; dirty save exposes Reload and Keep; invalid Graph diagnostics appear and clear after repair.
- Observed execution: Debug and Release 1041 each passed 19 assertions, captured screenshots and ActivityLog identity, retained normal VS/CgeFixed PIDs, and exited without forced termination.
- Result: Matches ADR-0061/M0063. F-L8-020 and F-L8-021 are TestVerified; F-L8-031 is no longer Partial.

## RE-L8-018 — Shared isolated rendering preview

- Confidence: High for protocol, ownership, presentation mapping, package closure, HWND lifecycle, and WARP execution; Medium for Visual Studio UI observation because the manual Scene Designer ledger entry is still Pending.
- Evidence: `RenderPreviewHostContracts`, `RenderPreviewHostServer`, `RenderPreviewRenderSession`, `RenderServer`, `RenderPreviewHostClient`, `RenderPreviewSceneBuilder`, `RenderSurfaceHostWindow`, `AssetPreviewHwndHost`, `SceneHwndHost`, the VSIX package target, focused Debug/Release tests, and the executable `vsix.rendering-preview` Stage.
- Observed behavior: bounded Open/ReplaceScene/UpdateFrame/Resize/Render/Close requests carry stable IDs, one session owner, monotonic sequences, deterministic frame hashes, current device generation/completed fence, and opt-in capture. Frame state includes object transforms and selection weights, Lit/BaseColor/Normal/Wireframe presentation, alpha-preserving triangle/line overlays, and up to three excluded-object camera previews with diagnostics.
- Observed behavior: Asset Preview and Scene View are clients of the Windows x64 host; neither owns native graphics resources. WPF retains a stable container HWND while every failed host generation receives a distinct child surface. The holder destroys the failed generation, retains the last positive size, creates the replacement surface, and only then replays source-derived scene/frame state once.
- Observed packaging: Debug and Release publish the matching framework-dependent x64 apphost closure plus `Stage.cgeshaderartifact` under `PreviewHost/`. The VSIX contains Contracts and the net472 `Microsoft.Bcl.HashCode` requirement, but no Vortice, SharpGen, PolySharp runtime, Preview Host PDB, `SceneRenderer`, `VorticeRenderer`, or `NativeEvent`.
- Adversarial corrections: real WARP execution proved that a new process cannot safely attach a swap chain to the killed host's prior HWND. The canonical public empty scene is mapped to one host-private degenerate sentinel because the private Stage backend requires an object; the sentinel never becomes public identity or frame state. A packaged-apphost test also exposed that lifecycle snapshots held a stale zero fence after ordinary frames; Preview diagnostics now read the Render Server's current monotonic completed fence.
- Result: Matches the automated F-L8-018 contract. Human-observed Experimental Instance behavior remains explicitly unverified rather than being inferred from package/WARP tests.

## RE-M0067 / F-L1-009 - Canonical numeric adversarial closure

- Confidence: High.
- Evidence: `LogicalScalar`, `LogicalMath`, logical vector/affine types, `CanonicalNumericAnalyzer`, member-scoped `ExternalNumericAdapterAttribute`, WPF/UI Automation projection sites, independent `BigInteger`/`double` test oracles, and Debug/Release `world.logical-scalar` results.
- Observed behavior: product authority is signed Q64.64 and relative presentation is `float`. Multiply/divide/square-root use allocation-free fixed-width intermediates and ties-to-even. Canonical text parsing validates the whole-number magnitude before scaling, so oversized decimal inputs cannot wrap through a 128-bit left shift.
- Observed enforcement: `double`, `decimal`, and `BigInteger` are build errors in product compilations. A class marker or `DependencyObject` ancestry cannot exempt fields or arbitrary state; only an external signature/member adapter can contain the forced type, and WPF/UI Automation values are validated/projected to `float` in that boundary.
- Result: Matches F-L1-009. Both canonical stages, both solution builds, 2,038 Debug tests, and 2,037 Release tests passed; the solution builds emitted only transient VSIX test-asset copy retry warnings.
## RE-M0069 - Transform visual conformance

- Confidence: High for the automated contract; Experimental Instance observation remains explicit Pending evidence.
- Evidence: `TransformGizmo`, `EditTransactionController`, `SceneTransformMath`, `SceneEditConflictMerger`, `SceneLandingDecision`, `SceneSourceEditor`, `SceneTextBufferWriter`, `RenderPreviewSceneBuilder`, `RenderPreviewRenderSession`, `VsixReferenceScene`, their focused tests, and Debug/Release `vsix.transform-tools` results.
- Observed behavior: Move, Rotate, Scale, numeric snap, vertex snap, and surface snap derive replaceable `float` preview matrices without editing C#. Repeated preview updates collapse into one final `SceneEditCommitRequest`; history emission retains one source step per gesture and the only live writer wraps that step in one `ITextUndoTransaction`.
- Observed behavior: Canonical Transform edits rewrite only frame-local Logical position/rotation/scale expressions and leave `ReferenceFrameId` unchanged. Equivalent Roslyn landing keeps the predicted document; foreign value or topology changes select explicit rebase instead of silently accepting stale state.
- Observed behavior: The VSIX sends bounded transparent overlay vertices through the versioned Preview protocol. With identical camera, object, and presentation state, an actual D3D12 WARP capture changes pixels while the overlay exists and returns to the exact baseline hash after it is cleared; the VSIX does not own a renderer.
- Result: Matches F-L8-023 for automated visual conformance. The Pending `scene-designer` Experimental Instance entry is not upgraded to `RuntimeVerified`.
# RE-M0073 — VSIX Compatibility Matrix and Fail-Closed Evidence Boundary

- Intended source: F-L8-028 in the Editor/VSIX specification.
- Observed implementation: `Docs/Verification/VSIX-Compatibility-Matrix.json` is the sole static compatibility declaration. `VsixCompatibilityMatrixValidator` checks net472, Visual Studio 17.14+ amd64 installation metadata, .NET Framework 4.7.2, SDK/Roslyn pins, en-US/ja-JP packaging, CgeFixed/CgeAutomation separation, Debug/Release CI cells, and exact detailed/manual evidence identities.
- Observed state model: static compatibility and dynamic evidence are not duplicated. Detailed checks remain `Pending`, `Passed`, or `Failed` in the Experimental ledger; the manual summary must equal their aggregate. Normal validation accepts explicit Pending for pre-release work, while strict release validation rejects every required non-passing or expired check.
- Result: Matches F-L8-028 without fabricating Visual Studio, DPI, focus, accessibility, or long-run observations from headless package tests.

## RE-M0098 — World-space procedural grid material

- Confidence: High for importer, converter, constants and WARP pixels; Medium for the match to Unreal's own filtering (observed by eye against one reference capture).
- Evidence: `GltfImporter.ReadWorldGrid`/`ExtractTextureBytes`, `ImportedWorldGrid`, `WorldGridGlbConverter`, `unreal_export_common.prototype_grid_materials`, `StageSceneDataBuilder.BakeMaterial`, `StageMaterialCreateInfo.WorldGrid`, `StageRenderer.WriteObjectConstants`, `world_grid_2026_10_02.py`, `GltfWorldGridImportTests`, `WorldGridGlbConverterTests`, `WorldGridGpuIntegrationTests`, and `tools/UnrealExport/inspect_prototype_grid.py` output.
- Observed behavior: a glTF material whose `extras.CGE_world_grid` holds a positive `gridSizeMeters`, `subGridDivisions` of at least 1, a `wireHalfWidth` in (0, 0.5) and two non-negative RGB colours imports a `WorldGrid`; any other value is reported with a warning naming `CGE_world_grid` and the material imports without a grid. The material's base colour factor is the surface between the lines.
- Observed behavior: the Stage pixel shader replaces the base colour with `lerp(lerp(surface, subGridColor, subWire), gridColor, mainWire)` when `ObjectCB.WorldGridParams.x > 0`. Each wire is the union of the two line sets of a projection, anti-aliased over the pixel footprint, and the three projections (XY, ZY, XZ) are blended by `saturate(3|N| − 1)` of the vertex normal. Lines sit at integer multiples of the cell (the main cell is the grid size, the sub cell is the grid size over the divisions), so floors and walls share one world grid and mesh UVs are not read.
- Observed behavior: a texture bound to a UV set other than TEXCOORD_0 is reported and not imported.
- Observed behavior: `worldgrid` rewrites a grid material's GLB by copying POSITION/NORMAL/TANGENT/TEXCOORD_0 and the indices into a new model; running it again produces identical bytes, and variants without a grid material are not touched.
- Result: Matches ADR-0075. Not reproduced: Unreal's anisotropic filtering at grazing angles, and grid instances other than `MI_PrototypeGrid_Gray`'s configuration (the export fails for those).
