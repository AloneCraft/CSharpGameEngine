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
- Observed behavior: Component indices 0–63 use the low `ulong` during generation; indices 64–127 use a high `ulong`, and each further 64 indices one more word (Word2…Word7, ADR-0079). A 513th distinct type emits SYSGEN026. Both paths select direct Archetype loops at generation time, so generated Runtime performs no mask matching.
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

## RE-M0099 — Radial (logarithmic) depth
- Confidence: High. Current native contract: M0119 / ADR-0084; selection/wrapper parts of ADR-0076 are superseded.
- Evidence: RadialDepth.cs, RenderViewDepth.cs, RenderCameraPreview.cs, Stage.cgeshadergraph and Depth.cgesubgraph, StagePresentationShaderSource, StageRenderer.SetCamera/SetViewProjection/WriteViewDepth, RenderPreviewHostContracts and RenderPreviewRenderSession.
- Observed behavior: every perspective camera writes native logarithmic physical distance through VSMain/PSMain and one pipeline family. A shared VertexOutput carries physical perspective interpolation, clip distances and the conservative bound; ObjectCB.ConservativeRasterDepth preserves early rejection. There is no camera Standard/Radial selector. Orthographic views are linear. Bare perspective matrices are rejected; physical FOV/near/far/curve metadata creates a validated logarithmic packet and finite auxiliary projection before state changes.
- Observed behavior: main colour/depth also writes R32F SceneDistance. Sky and half-resolution AO read physical distance; DDGI captures logarithmic depth and CaptureDistance; Lumen surface depth agrees with the nearest physical surface. Shadows retain light-space depth. No logarithmic inverse is used for these distance consumers.
- Observed behavior: scene cameras expose the curve without depth selection. Protocol 9 requires perspective metadata, rejects version 8 and preserves original huge clip ranges for thumbnails. RenderCameraPreview requires depth matching its projection with zero conservative raster weight. Observers use ObserverVSMain and store their centre in RenderViewDepth; up to three game insets retain the marker and compressed-space interpolation.
- Verification: independent double oracle, pinned seven native images, CPU packet validation, full GPU/recovery/hot-reload tests and hardware precision/performance evidence. Current values and limitations are in M0119.
- Result: Matches the native depth contract. Runtime scene orthographic projection remains a separate known limitation; four render paths and broader layer/physics work remain M0120–M0123.

## RE-M0098 — World-space procedural grid material

- Confidence: High for importer, converter, constants and WARP pixels; Medium for the match to Unreal's own filtering (observed by eye against one reference capture).
- Evidence: `GltfImporter.ReadWorldGrid`/`ExtractTextureBytes`, `ImportedWorldGrid`, `WorldGridGlbConverter`, `unreal_export_common.prototype_grid_materials`, `StageSceneDataBuilder.BakeMaterial`, `StageMaterialCreateInfo.WorldGrid`, `StageRenderer.WriteObjectConstants`/`SetWorldGridAnchor`, `EngineHost.ReduceToGridCell`, `world_grid_2026_10_02.py`, `render_origin_2026_10_03.py`, `GltfWorldGridImportTests`, `WorldGridGlbConverterTests`, `WorldGridGpuIntegrationTests`, `RenderOriginTests`, and `tools/UnrealExport/inspect_prototype_grid.py` output.
- Observed behavior: a glTF material whose `extras.CGE_world_grid` holds a positive `gridSizeMeters`, `subGridDivisions` of at least 1, a `wireHalfWidth` in (0, 0.5) and two non-negative RGB colours imports a `WorldGrid`; any other value is reported with a warning naming `CGE_world_grid` and the material imports without a grid. The material's base colour factor is the surface between the lines.
- Observed behavior: the Stage pixel shader replaces the base colour with `lerp(lerp(surface, subGridColor, subWire), gridColor, mainWire)` when `ObjectCB.WorldGridParams.x > 0`. Each wire is the union of the two line sets of a projection, anti-aliased over the pixel footprint, and the three projections (XY, ZY, XZ) are blended by `saturate(3|N| − 1)` of the vertex normal. Lines sit at integer multiples of the cell (the main cell is the grid size, the sub cell is the grid size over the divisions), so floors and walls share one world grid and mesh UVs are not read.
- Observed behavior: the lines are evaluated at `WorldPosition + ObjectCB.WorldGridAnchor`. In the camera-relative runtime `EngineHost` sets the anchor to the camera's frame-local position modulo the grid size (an exact Q64.64 remainder rounded once to float), so the lines stay on the world lattice as the camera moves; world-coordinate callers leave it at zero. The simplified height fog likewise thins with `WorldPosition.y + FogParams0.z`, where `FogParams0.z` is the render origin's altitude. The anchor is computed in the camera's reference frame.
- Observed behavior: a texture bound to a UV set other than TEXCOORD_0 is reported and not imported.
- Observed behavior: `worldgrid` rewrites a grid material's GLB by copying POSITION/NORMAL/TANGENT/TEXCOORD_0 and the indices into a new model; running it again produces identical bytes, and variants without a grid material are not touched.
- Result: Matches ADR-0075. Not reproduced: Unreal's anisotropic filtering at grazing angles, and grid instances other than `MI_PrototypeGrid_Gray`'s configuration (the export fails for those).

## RE-M0101 — System の引数の役割表

- Confidence: High（解析器の分岐とテストで直接確認）
- Evidence: `GameSystemAnalyzer.AnalyzeMethod`、`GameSystemLoopEmitter.BuildCallArguments`、`GameSystemGeneratorDiagnosticsTests`
- Observed behavior: 先頭の値渡し `Entity`、末尾の `ref State`（任意）を取り除いた残りを、`ref`（非 nullable struct）=書き込む Component、`in`（プリミティブ以外の非 nullable struct）=読み取る Component、`[ReadOnlyComponent] in`=同じ、参照型の値渡し=Context として分類する。`in` の読み取りは変更バージョンを刻まない。`State` を持たないメソッドの呼び出しは `ref State` を渡さない。`in` のクラスや `ref` の参照型は SYSGEN018。
- Result: Matches ADR-0077 §1。

## RE-M0101b — キャラクター・カメラ・アニメーションの Component 名

- Confidence: High（型定義、生成コード、テンプレートの JSON、WARP での実行で確認）
- Evidence: `src/CSharpGameEngine.Runtime.Character/CharacterComponents.cs`、`PlayerCameraComponents.cs`、`src/CSharpGameEngine.Runtime.Animation/AnimationComponents.cs`、`Templates/CSharpGameEngine.Templates.ThirdPerson/Scenes/ThirdPersonCharacter.json`、`ThirdPersonCharacterVerification`
- Observed behavior: ゲームが使う Component は `CharacterMotion`（設定）、`CharacterMotionState`（速度・モード・向きと `OnGround`/`Falling`）、`MoveIntent`（`Direction`、`JumpRequested`、`Jump()`）、`ViewAngles`（`YawDegrees`/`PitchDegrees` と上下の制限）、`OrbitCamera`（`Pivot`、`Distance`、`Radius`、`Collide`）、`Locomotion`（6 つのクリップと速度）。シーン JSON のキーは、`[Angle]` の項目で単位の語を除く（`MaxSlope`、`TurnRate`、`Yaw`）。動作は改名前と同じ。
- Result: Matches ADR-0077（M2 の改名の範囲）。

## RE-M0101c — `in Parent<T>`

- Confidence: High（解析器の分岐、生成コード、実行テストで確認）
- Evidence: `GameSystemAnalyzer.AnalyzeMethod`、`GameSystemIndexAssigner.Assign`、`GameSystemLoopEmitter.BuildRelationResolution`、`GameSystemParentRelationTests`
- Observed behavior: `Update`/`Shutdown` の `in Parent<T>` は、メソッドの Component に読み取りの `Parent` を暗黙に加え、`T` を登録する。生成されたループは、Entity ごとに `var ParentEntityN = …Parent.Value;` と `TryGetComponent(ParentEntityN, out T ParentValueN)` を実行し、失敗したら `continue` する（変更バージョンの刻印より前）。成功したら `new Parent<T>(ParentEntityN, ParentValueN)` を渡す。値はコピーで、書き戻せない。
- Result: Matches ADR-0077 §2（M3）。

## RE-M0101d — `[InputActions]`

- Confidence: High（生成コード、テンプレートのビルド、WARP での実行で確認）。マウスの視点は Medium（コードの形のみ）。
- Evidence: `Core/InputActionDeclarations.cs`、`InputActionsEmitter`、`GameSystemAnalyzer`、`GameSystemLoopEmitter`、`InputActionsGeneratorTests`、`verify-thirdperson-character`
- Observed behavior: `[InputActions] partial struct` の public な `Stick`/`Look`/`Button` フィールドと `[Keys]`/`[Gamepad]`/`[Mouse]` から、`InputContext` を生成する。Stick のキーは Up=Swizzle、Down=Swizzle+Negate、Left=Negate、Right=そのまま。マウスの移動量は Y を反転する。`Look` はマウスの移動量 × `MouseTurnsPerCount` と、スティック × `StickTurnsPerSecond` × フレーム時間（0.1 秒で丸め）の和。`Button` は `ReadValue` が 0 でなければ Held、`CurrentFrame` の Started/Canceled が Pressed/Released。`in` の構造体を取る Update/Shutdown は、呼び出しの前にその構造体を読み直す。入力を読む System の既定フェーズは FrameInput、順序は `Window` の後。
- Result: Matches ADR-0077 §2（M4）。

## RE-M0101e — `Audio` サービスとイベント

- Confidence: High（生成コード、コンパイルして実行するテスト、WARP と実機の XAudio2 での実行で確認）
- Evidence: `Runtime.Windows/Audio.cs`、`EngineHost.Sound`、`AudioDeviceServer.PlayOneShot`、`WavReader`、`GameSystemEventWiring`、`GameSystemLoopEmitter`、`GameSystemEventTests`、`verify-thirdperson-character`
- Observed behavior: `Audio.Play`/`PlayLocal` は要求を貯め、`Present` の直前の `Flush` でクリップ（`WavReader` が `PcmClip` にする。キャッシュあり）を読み、`Source` の距離の減衰と左右の定位を掛けて、`AudioDeviceServer.PlayOneShot` に渡す。XAudio2 では source voice ごとに再生し、終わったものを毎フレーム破棄する。イベントは `GeneratedSystemSet` のキューに積まれ、受け手は読み取り位置から 1 件ずつ呼ばれる。キューと位置はフレームの頭で空になる。`[SystemPhase]` のない受け手は、送り手の最も遅いフェーズに入り、同じフェーズの送り手の後に並ぶ。
- Result: Matches ADR-0077 §2（M5・M6）。

## RE-M0101f — 型付きアセット `Assets.*`

- Confidence: High（生成コード、テンプレートのビルド、パッケージ経由の実行で確認）
- Evidence: `AssetCatalogEmitter`、`Core/Authoring/AssetReferences.cs`、`Game.props`、`AssetCatalogTests`、`verify-template-package`
- Observed behavior: `ProjectDir` の下の `Assets/` の `.wav`/`.glb`/`.gltf`/`.fbx`/`.png`/`.jpg`/`.jpeg`（`Assets/Editor` を除く）の一覧から、`internal static class Assets` を生成する。フォルダごとに入れ子のクラス、ファイルごとに `SoundAsset`/`ModelAsset`/`TextureAsset` の静的な読み取り専用フィールドを持つ。ランタイムのマーカー（ISystem など）が無いプロジェクトでは出さない。シーン JSON の文字列値 `"Assets/…"` が一覧に無ければ SCENE208。
- Result: Matches ADR-0077 §2（M7）。

## RE-M0101g — `Pointer` と `Picking`（クリック移動）

- Confidence: High（生成コード、光線の単体テスト、WARP での実行で確認）。実機のマウスのカーソルは Medium（自動実行の `CGE_AUTOMATION_CURSOR` で代用）。
- Evidence: `Core/InputActionDeclarations.cs`、`InputActionsEmitter`、`EngineHost.Cursor`、`Runtime.Windows/Picking.cs`、`MoveIntent`、`CharacterMotionUpdate`、`PickingTests`、`CharacterMotorTests`、`ClickToMoveVerification`
- Observed behavior: `[Pointer] Pointer` のメンバーは、`Read` の第 3 引数 `EngineHost.Cursor`（クライアント領域の左上を 0、右下を 1 とした割合と `Inside`）で毎フレーム埋まる。`CGE_AUTOMATION_CURSOR=x,y` があればそれを使う。`Picking.TryPick(Pointer, out PickHit)` は、カメラの姿勢・縦の視野角・ウィンドウの縦横比から光線（最大 2000 m）を作り、物理の静的コリジョンに当てて最も近い点を返す。カメラが物理と別の参照フレームにある、ポインターが領域の外、何にも当たらないときは false。`MoveIntent.MoveTo(Point)` は目的地を持ち、`CharacterMotionUpdate` が毎 tick `Steer` で `Direction` を水平の単位ベクトルに書く。目的地までの水平距離が制動距離（歩行速度² ÷ 2 ÷ 制動）より近いと、長さを距離に比例して縮め（下限 0.25）、0.15 m 以内で `Stop()` して `HasDestination` を false に戻す。
- Result: Matches ADR-0077 §2（TopDown の入力）。

## RE-M0101h — 簡易の車両モデルと三角形メッシュへの光線

- 失効(2026-10-23、M0109): 簡易の車両モデル(下の (1)〜(4))は Chaos 相当の車に置き換えた。今の車の振る舞いは RE-M0109。三角形メッシュへの光線(最後の文)は今も有効。

- Confidence: High（単体テスト 22 ケース、WARP での実行、最終フレームの画像で確認）。実機のキー入力と実際の運転感覚は Low（自動実行の入力のみ）。
- Evidence: `VehicleComponents.cs`、`VehicleMotor.cs`、`VehicleSystems.cs`、`PhysicsNarrowPhase.TryRaycastMesh`、`VehicleMotorTests`、`PhysicsMeshRaycastTests`、`VehicleDriveVerification`
- Observed behavior: `VehicleMotor.Step` は 1 固定 tick に、(1) 4 つの車輪の位置（車体の中心から前後 `Wheelbase/2`、左右 `Track/2`、上に 0.5 m）から車体の下へ光線を飛ばし、急でない面に当たった車輪が 2 つ以上なら接地とみなす。(2) 接地中は、ワールドの速度を車の前と右（前×上）に分け、前の速度に、アクセル（`Acceleration×Drive×(1−速度/TopSpeed)`）、進む向きと逆に踏むブレーキ（`BrakeDeceleration`）または後退、自然な減速（`RollingDeceleration`、止まるところで終わる）、ハンドブレーキ（ブレーキの半分）を足し、横の速度を `Grip`（ハンドブレーキ中は `HandbrakeGrip`）の割合で毎秒消す。進む向きの前（後退なら後ろ）の 2 隅から水平な光線を飛ばし、急な面（法線の上向き成分が 0.6 未満）までの隙間で、このtickの前進を打ち切る。ハンドル（`SteeringRate` で近づく）から前輪の角度（`MaxSteeringDegrees` を `1+|速度|/SteeringFalloffSpeed` で割る）を作り、旋回の角速度 `速度×tan(角度)/Wheelbase` を `MaxLateralAcceleration/|速度|` で頭打ちにして、上向きの軸のまわりに回す（右へ切ると上向きの軸のまわりでは負）。位置を速度で進める。(3) 空中は重力で飛び、上向きを世界の上へゆっくり戻す。(4) 進めたあとにもう一度 4 点を調べ、接地なら、4 点の平均から法線の向きに `RideHeight` の所へ高さを寄せ（めり込みはすぐ、下がるのは毎秒 25 の割合）、面の法線へ向きを寄せ（毎秒 12 の割合）、着地したtickは地面へ入る速度を消す。`Raycast` の静的三角形メッシュは、置き場所でローカルに移した光線を、BVH の候補の三角形と Möller–Trumbore（両面）で判定し、最も近い点と、光線に向いた側の面の法線を返す。
- Result: ObsoleteSpecification(車両。M0109 で置き換え)。三角形メッシュへの光線は Matches ADR-0077 §2(段階 9)。

## RE-M0102 — T3D から疑似コードへ

- Confidence: High（単体テストと、4 テンプレートの実データで確認）
- Evidence: `T3dDocument`、`BlueprintLogicWriter`、`BlueprintLogicWriterTests`、`.artifacts/UnrealExport/logic/*/*.logic.txt`
- Observed behavior:
  - `T3dDocument` は、`Begin Object … End Object` を ExportPath ごとのオブジェクトにし、属性と `CustomProperties Pin` を読む。ピンの `LinkedTo` は、ノード名とピン ID の組になる。
  - `BlueprintLogicWriter` は、入口のノード（イベント、入力、関数）から実行ピンをたどって文を書く。データのピンは純粋なノードを式として展開し、knot は透過にする。分岐は出力ピンごとに字下げする。ubergraph の空の入れ物は飛ばす。
  - 状態機械は、状態、別名状態（「from any of」）、遷移と条件式にする。アニメーションのポーズは、出力から入力へさかのぼる木にする。
  - 値が 20000 文字を超えたら切る。同じパスのグラフは 1 回だけ書く。
- Result: Matches ADR-0078 §1。

## RE-M0103 — プラグインの取り込み、サービス、データアセット

- Confidence: High（単体テスト、生成コード、パッケージから作ったゲームの WARP での実行で確認）
- Evidence: `Game.targets`、`Vehicles.plugin.props`、`GameSystemAnalyzer.ServiceSourceOf`、`GameSystemLoopEmitter`（`DisposeServices`）、`AssetCatalogEmitter`、`SceneJsonSceneAnalyzer.AnalyzeDataAsset`、`PluginManifestAnalyzer`、`verify-plugin-package`
- Observed behavior:
  - `Game.targets` はプロジェクトファイルの後に読まれ、`_CgePluginLocation` を Project・Engine・Package と変えながら `Plugins/*/*.plugin.props` とエンジンの `plugins/*/*.plugin.props` を取り込む。各 `.plugin.props` は、Engine の場所なら `CgePlugins` に名前があるときだけ有効にし、有効なら `CgePluginRoots` に `|名前=フォルダ` を足し、`.plugin.items` を取り込む。
  - `.editorconfig` を書く直前に、`CgeProjectDirectory` と `CgePluginRootsEncoded` を `#`→`?h`、`;`→`?s` で作る。生成器はこれを先に読み、無ければ `ProjectDir` を使う。
  - `[EngineService]` の引数は `(Systems.ServiceN ??= new T())` または `new T(EngineHost)` で渡す。フィールドは型名の順。`Shutdown`・`ShutdownEditor` の最後に逆順で `IDisposable` を破棄し、null に戻す。
  - `.cgeasset` は `schemaVersion` 3 だけを受け、型は `[DataAsset]` の非ジェネリックの struct か class。値はシーンの Component と同じ式になり、カタログの `public static readonly T 名前 = 式;` になる。問題があればその項目を出さない。
  - パッケージから作ったゲーム（ThirdPerson + Weather + Lightning）の 888 tick: Weather の更新 888 回・雨量 2664.0、Lightning の落雷 888 回・エネルギー 17760.0。名前を挙げていない Vehicles は生成コードに現れない。
- Result: Matches ADR-0079。

## RE-M0104 — 画面 UI

- Confidence: High(単体テスト、WARP の画像と指紋、パッケージから作ったゲームの実行で確認)。実機の GPU は未確認
- Evidence: `ScreenUi.cs`、`RuntimeSystems.cs`(`ScreenTextCollect`・`ScreenImageCollect`・`ScreenUiSubmit`)、`StageRenderer.ScreenLayer.cs`、`StagePresentationShaderSource`(`ScreenVSMain`・`ScreenPSMain`)、`FontAtlas`、`ScreenTextLayout`、`FontBaker`
- Observed behavior:
  - 要素は、レイヤーの昇順、同じレイヤーでは集めた順に並ぶ。隣り合う同じテクスチャと同じシェーディングはバッチにまとまる。非表示の要素、空の文字、大きさが 0 以下の文字、非有限の値、幅か高さが負の画像は描かない。
  - 短辺 1080 を基準に、`min(幅, 高さ) / 1080`(0.1〜8 に制限)で大きさとオフセットを拡縮する。四角形の左上は整数の画素に丸める。
  - 文字は 1 グリフ 6 頂点、空白は頂点を出さない。字詰めは無い。無い文字は `?`。
  - 読めないフォントと画像は、1 回だけ診断(標準エラーにも `CGE-SCREEN-UI`)を出して、その要素を描かない。
  - レイヤーはそのフレームだけ有効。`Build` は集めた要素を忘れる。
  - 指紋は頂点のバイト列の FNV-1a(64 ビット)で、レポートには 16 桁の 16 進で入る。
  - Vehicle の HUD: 速度は |前進速度| × 3.6 を偶数丸め(MidpointRounding.ToEven)して 3 桁のゼロ詰め(1000 で巻き戻る)、ギアは後退 R・ニュートラル N・前進は段の数字。
- Result: Matches ADR-0078(M0104)。

## RE-M0105 — 実行時の生成と使い捨てエフェクト

- Confidence: High(単体テスト、WARP の画像と描画物の数の推移、SDK パッケージから作ったゲームの実行で確認)。実機の GPU は未確認
- Evidence: `StageRenderer.cs`(`AddObject`・`RemoveObject`・`AddMesh`・`AddMaterial`)、`RenderServer.cs`、`RenderRoster.cs`、`RuntimeSystems.cs`(`StaticMeshBind`・`PrimitiveMeshBind`・`TransformSync`)、`Spawner.cs`、`Lifetime.cs`、`ParticleBurst.cs`、`ISpawnableScene.cs`、`SceneInstantiateEmitter.cs`、`FixedTransform.cs`
- Observed behavior:
  - 描画物の余裕は 4096 個、マテリアルは 256 個。使い切ると追加は断られ(`AddObject` は -1)、その Entity は描かれない。診断は 1 回。外したオブジェクトの番号は次の追加で使われる。
  - 描かれる Entity は、生きていて `StaticMesh`・`PrimitiveMesh`・`SkeletalMesh` のどれかを持つもの。壊れるか、メッシュを失うと、次のフレームの `TransformSync` で外れる。位置が解決できない Entity は、置けるまで描かれない(カメラの位置に出ない)。
  - 読めない資産(存在しない、マテリアルの余裕が足りない。M0107 から、スキン付きのメッシュは `SkeletalMesh` なら読める)は、診断を 1 回出して描かない。同じ資産は再び読もうとしない。
  - `Spawner.Spawn<TScene>(位置)` は、根の位置を置き換える(回転と拡縮は Scene に書いた値、回転を渡すと回転も)。Scene は呼んだフレームの終わりに現れ、描かれるのは次のフレームから。同じ Scene を続けて呼ぶと、独立した複製になる。
  - `Lifetime` は固定 tick ごとに 1 tick 分(1 / `FixedTickRate` 秒)減り、0 以下で壊れる(残りが 0 なら最初の tick)。Scene の根なら Scene ごと壊れる。
  - `ParticleBurst` は最初の固定 tick に 1 回だけ、ノードのワールド位置から `Count`(0〜256 に収める)個の粒子を出す。方向と速さは `Seed` から決まる。粒子は毎 tick 動き、重力で落ち、生まれてからの割合で大きさが `StartScale` から `EndScale` へ直線で変わる(動きは寿命を数える前に読む)。
- Result: Matches ADR-0078(M0105)。

## RE-M0106 — ナビメッシュのベイクと経路追従

- Confidence: High(単体テスト、決定的な物理の世界での実際の `CharacterMotor`、WARP の実走、SDK パッケージから作ったゲームの実行で確認)。実機の GPU と VS のボタンは未確認
- Evidence: `plugins/CSharpGameEngine.Navigation/Runtime/NavMesh.cs`・`NavMesh.Query.cs`・`NavigationComponents.cs`・`Navigation.cs`・`NavigationSystems.cs`、`Cook/NavMeshBuilder.cs`・`SceneGeometry.cs`・`NavMeshCooker.cs`、`Navigation.plugin.items`、`CharacterComponents.cs`(`MoveIntent`)、`NavigationCookService.cs`
- Observed behavior:
  - `.cgenav`: 印 `CGENAV01`、設定 6 つと原点 3 つ(`LogicalScalar`)、格子の幅と奥行き、多角形(MinX、MinZ、MaxX、MaxZ、Y の `int32`。Max は含まない)、portal(多角形 A・B と、格子の線の上の軸に平行の線分)。同じ入力から同じバイト列。壊れたファイルは、あらゆる切り詰め・後ろの余り・異常な件数・格子の外の長方形・存在しない多角形・境の上に無い portal・不正な設定で、例外ではなく理由つきで断る。
  - 焼く: `NavMeshBounds` の領域(同じ `Id` は和)と、起動シーンの `StaticMesh` + `StaticMeshCollider` の三角形と `RigidBody` の無い `Collider` の箱を、`LogicalScalar` と整数だけで、ボクセル化 → 歩ける面の絞り込み(閉じた形の底面が重なる床、低い出っ張り、崖と険しい坂、頭のつかえ) → 接続 → 半径ぶんの縁からの距離(2/3 の距離変換) → 同じ高さの長方形 → portal にする。格子の縁の 1 セルは崖として外れ、そのうえで半径 `ceil(AgentRadius / CellSize)` セルぶん離れる。床の高さは、面を含むボクセルの上端(最大 1 段高い)。
  - 経路: 出発点は、点を含む(または `2 × AgentRadius + CellSize` 以内の)多角形のうち、床の高さが背丈の範囲で一番近いもの。無ければ経路は無い。A*(費用は整数、同じ費用は番号の小さい方)、境の上の点は、現在の位置から目的地への最短の通り方に一番近い点。目的地の多角形へ着けば完全な経路、着けなければ、行ける多角形のうち目的地に一番近い点までの部分経路(`IsPartial`)。funnel で角だけにする。
  - 歩く: `NavigationSteering`(PhysicsSimulation、`CharacterMotionUpdate` の前)が、`MoveIntent.Destination` への経路を探し、次の経由点を `MoveIntent.Waypoint` へ置く。通過点は最高速度のまま `NavAgent.WaypointRadius`(0.3 m)に入ったら次へ、最後の点は `Destination` と同じ減速で止まる。目的地が 0.25 m 以上動いた、または経路の線から 1.5 m 以上離れたら探し直す。ナビメッシュが無い・出発点が外なら、経由点を置かず、従来の直線。`HasDestination` が false になると経路を捨てる。
  - ビルド: `CgeCookNavigation` が、`NavMeshBounds` を含むシーンごとに Cook CLI を別のプロセスで動かし、`Assets/Navigation/<Id>.cgenav` を出力へ複製する。シーン・アセット・ツールが新しいときだけ。失敗はビルドのエラー。
- Result: Matches ADR-0078(M0106)。

## RE-M0107 — キャラクターとアニメーション

- Confidence: High(単体テスト、実際の Mannequin の資産、WARP の実走、SDK パッケージから作ったゲームの実行で確認)。実機の GPU と VS の実機は未確認。足の IK の見た目と UE の比較、FABRIK の `Weight` の解釈は推測を含む
- Evidence: `Runtime.Animation/`(`BlendSpace2D.cs`・`PoseBlending.cs`・`LocomotionStateMachine.cs`・`TwoBoneIk.cs`・`FootIk.cs`・`FirstPersonRig.cs`)、`Runtime.Character/`(`LocomotionObserver.cs`・`FootIkSystems.cs`・`Respawn.cs`・`PlayerCamera.cs`・`CharacterMotor.cs`)、`Runtime.Windows/`(`FirstPersonView.cs`・`RenderRoster.cs`・`RuntimeSystems.cs`)、`StageRenderer.SetObjectVisibility`
- Observed behavior:
  - 動きの入力(`LocomotionObserver.Observe`): `Speed` は水平速度の長さ。`Direction`(度、−180〜180)は水平速度と体の前向きの符号つきの角度で、前を向いたときの +R 側が正(右のクリップが進む側)。`FaceMovement` なら ±45° に制限する。`ShouldMove` は `Speed > 0.0001 m/s` かつ `MoveIntent.Direction` が 0 でないとき。`Falling` は移動の状態、`VerticalSpeed` は上向きの速度(m/s)。
  - 状態機械(`LocomotionStateMachine`): Locomotion(入れ子の Idle / Move)、Jump、Fall Loop、Land。落下を始めると、`VerticalSpeed > 1` なら Jump(0.15 s)、でなければ Fall Loop(0.4 s)。Jump はクリップの残りが 0.3 s で Fall Loop(0.3 s)。落下が終わると Land(0.1 s)。Land は残り 0.4 s で Locomotion(0.4 s)、`ShouldMove` なら Locomotion(0.5 s)。Idle → Move は 0.2 s、Move → Idle は 0.3 s。すべて Cubic(3t² − 2t³)で、遷移の途中の別の遷移は画面にあったポーズから始める。
  - ブレンドスペース(`BlendSpace2D`): X は 45° ごとの 9 列(−180〜180)、Y は 0 / 歩行速度 / 走行速度(Y=0 は Idle のクリップ)。格子の三角形分割は、X の左半分が「左下から右上」、右半分が「右下から左上」の対角線。重みは目標へ毎秒 5 の指数的な近づき方で、合計は 1。ブレンドスペースの 16 本のクリップは、重みつきの長さに合わせた 1 つの位相で進む。
  - 足の IK(`FootIkUpdate`): 足首の少し前(0.05 m)から、メッシュの原点 + 0.3 m の高さから − 0.5 m の高さまで光線を下ろし、当たった高さをその足の目標のずれにする(当たらなければ 0、落下中は 0)。ずれは毎秒 10 で補間。骨盤は低い方の足に合わせて下げ、足は 2 ボーン IK(膝は今の曲がりを保ち、足首の向きは動かさない)で届く範囲まで引く。
  - 一人称(`FirstPersonPoseUpdate`): 体のポーズ(足の IK のあと)を腕のスケルトンへ写し、`spine_01` は動かさず `spine_01`〜`spine_04` を FABRIK(最大 10 回、精度 1 cm、重み 0.5)で `BodyOffset` へ寄せ、`head` を基準姿勢の位置と向きへ、`upperarm_l/r` を基準姿勢の位置へ置く(前腕と手は剛体で追従)。腕の描画は、カメラ相対のワールド行列に、カメラ空間の `(縮尺 × 広がり, 縮尺 × 広がり, 縮尺)` を掛ける(縮尺 0.6、広がり = tan(90° / 2) / tan(70° / 2))。体は `VisibleToCamera=false`(カメラのパスに描かれず、影のパスには描かれる)、腕は `CastsShadow=false`。
  - カメラの遅れ(`CameraLag`): 軸の位置は `VInterpTo`(距離 × clamp(dt × 速さ))、回転は slerp。1 フレームが `LagMaxTimeStep`(1/60 s)より長いと、刻んで進める(UE の substepping と同じ式)。`LagMaxDistance` を超えて離れない。新しい Pawn のカメラは最初のフレームで遅れずに置かれる。`SocketOffset` は腕を縮めたあとのカメラの座標系のずれ。
  - 平面への拘束(`CharacterMotion.ConstrainToPlane`): 入力・速度・各移動の変位から、法線(使うとき正規化)の成分を引く。法線が 0 なら何もしない。`SnapToPlaneAtStart` は最初の tick に平面へ寄せる。
  - `KillZ`(PostPhysics): 位置の高さが `Height`(既定 −10485.75 m)より低い Pawn を壊す(根なら Scene ごと)。壊したら `PawnDestroyed` を送る。`PlayerStarts.TryPick(World, First | Random, 種)` が PlayerStart を選ぶ(無作為は SplitMix64 の決定的な選び方)。テンプレートの `RespawnPlayer`・`RespawnVehicle` が `Spawner.Spawn` で新しい Pawn を出し、カメラが引き継ぐ。
  - 後から作る `SkeletalMesh`: `RenderRoster.BindSkeletalMesh` が資産ごとのプールからスキンつきメッシュとマテリアルの組を使い、足りなければ `AddMesh` で足す(パレットのリングの作り直しで GPU の待ちが 1 回)。Entity が消えると組はプールへ戻る。
- Result: MatchesWithDocumentedExtension(ADR-0078 / M0107。足の IK は解析的な 2 ボーン IK、一人称のカメラは固定の目の位置。差は `Docs/Milestones/M0107-Characters-And-Animation.md`)。

## RE-M0108 — 入力の拡張

- Confidence: High(単体テスト、生成されたコードの文字列、WARP の実走、SDK パッケージから作ったゲームの実行で確認)。実機のゲームパッド・マウスは未確認(自動実行は実機の入力を読まない)。UE の `ElapsedSeconds` との 1 フレームの差は推測(UE のソースは読んでいない)
- Evidence: `Core/InputActionDeclarations.cs`、`Windows/Input/InputServer.cs`、`Windows/Platform/Win32InputEventAdapter.cs`・`Win32Window.cs`、`CodeAnalysis/InputActionsEmitter.cs`、`Runtime.Windows/RuntimeAutomation.cs`、`Templates/CSharpGameEngine.Templates.TopDown/Gameplay/ClickToMove.cs`、`Templates/CSharpGameEngine.Templates.Vehicle/Gameplay/`
- Observed behavior:
  - `Axis`(`Value` は -1〜1): `[Keys]` の各キーが +1、`[NegativeKeys]` の各キーが -1、トリガーは 0〜1、スティックの片方の軸(`Gamepad_LeftX/LeftY/RightX/RightY` の 1D の制御。Y は上が正)は -1〜1、ボタンは押している間 +1。割り当ての合計を `Axis.FromSum` が -1〜1 に丸め、NaN と無限大は 0。`[Gamepad]` の値は不感帯(既定: スティック 0.2、トリガー 0.1。以下は 0、それより上は残りの幅へ引き伸ばす)、倍率、反転の順に整える。ボタンには書けず、不感帯が 0 以上 1 未満でない・倍率が有限で 0 より大きくないものは INPUT005。
  - `Button.HeldSeconds`: `InputServer.TrackHold` が操作ごとの時計を持つ。押したフレームは 0、押している間は各フレームの経過時間(`Read` に渡す値は 0.1 秒で頭打ち)を足した値、離したフレームはそれまでの合計(そのフレームの経過時間を含む)、それ以外は 0。同じフレームで押して離すと 0、離したフレームに押し直すと離した値を返して新しい時計を 0 から始める。同じ `FrameNumber`(`ProcessEvents` の回数)の読み直しは進まない。負・NaN・無限大の経過時間は 0。`ReleasedBefore(秒)` は離したフレームで `HeldSeconds < 秒`(ちょうど同じ長さは偽)。
  - `Look` は `MouseAxis.DeltaX`(横だけ)・`DeltaY`(縦だけ。上が正)と、スティックの片方の軸(`StickAxis`)を受ける。`InputMaskModifier` が反対の軸を 0 にし、スティックの Y は `InputSwizzleModifier` で `Look` の Y へ移る。
  - 自動実行の入力 `コード=値@開始-終了`: `GamepadLeftX|LeftY|RightX|RightY|LeftTrigger|RightTrigger` は範囲の間その値で外は 0(コントローラーは最初の呼び出しで接続され、それまでの値を保つ)、`GamepadSouth|East|West|North|LeftShoulder|RightShoulder` は 0 以外で押す、`MouseDeltaX|MouseDeltaY` は範囲の各フレームに値のカウントだけ動かす。値は有限の数だけで、未知の `Gamepad*`・`MouseDelta*` と、値を取らないコードへの `=` は起動時に拒否する。
  - TopDown(`ClickToMove`): 押した瞬間に `MoveIntent.Stop()` して、カーソルの下の地面を `ClickDestination` に覚える。押している間は、地面が見つかれば覚え直し、Pawn の位置からその点への 3 次元の単位ベクトルを `MoveIntent.Direction` に直接書く(見つからないフレームは 0)。離した瞬間に、`ReleasedBefore(0.5)` かつ覚えた地面があれば `MoveTo`(ナビメッシュの経路)と目印の `Spawner.Spawn<CursorMarker>`、そうでなければ `Stop()`。
  - Vehicle: `PlayerActions` は `Axis Throttle`(W・Up・右トリガー・西ボタン)、`Axis Brake`(S・Down・左トリガー・南ボタン)、`Axis Steering`(D/Right と A/Left、左スティックの横)、`Button Handbrake`(Space・左ショルダー)、`Look LookAround`(マウスの横・右スティックの横。使われない)。`VehicleControls` は `Drive = Throttle - Brake`、`Steering = Steering`、`Handbrake = Handbrake.Held`。
- Result: MatchesWithDocumentedExtension(ADR-0078 / M0108。差は `Docs/Milestones/M0108-Input-Extensions.md`)。

## RE-M0109 — Chaos 相当の車両と物理の命令

- Confidence: High(単体テスト `ChaosVehicleTests` 25 件と `PhysicsForceCommandTests` 9 件、`LogicalMathAtan2Tests`、WARP の実走と画像で確認)。UE の Chaos との数値の一致は Medium(式は UE 5.7 のソースを移したが、UE を同じ入力で走らせて比べてはいない)。実機の入力と運転感覚は Low(自動実行の入力のみ)。
- Evidence: `plugins/CSharpGameEngine.Vehicles/Runtime/`(`VehicleSetup.cs`・`VehicleChaos.cs`・`VehicleComponents.cs`・`VehicleMotor.cs`・`VehicleSystems.cs`)、`Runtime.Physics/PhysicsContracts.cs`・`DeterministicPhysicsWorld.cs`・`PhysicsNarrowPhase.cs`・`PhysicsComponents.cs`・`StaticMeshCollision.cs`、`Runtime.Windows/FirstPersonView.cs`(`MeshEmissive`)・`RuntimeSystems.cs`・`RenderRoster.cs`、`Windows/Graphics/StageRenderer.cs`、`Numerics/LogicalMath.cs`、テンプレートの `Gameplay/` と `Scenes/SportsCar.json`
- Observed behavior:
  - 物理の命令はそのtickの最初に(tick、送り手、番号)の順で適用される。`ForceAtPoint` は線速度に F/m·Δt、角速度に I⁻¹(r × F)·Δt を足す(r は点 − 物体の位置)。`Torque` は角速度だけ、`SetVelocity` は両方を置き換えて起こし、`Sleep` は速度を 0 にして眠らせる(同じ tick の眠りの更新が `Sleep` イベントを出す)。`LinearDamping`・`AngularDamping` は重力の後に速度へ max(0, 1 − 減衰·Δt) を掛ける。
  - 箱と三角形メッシュの接触: 触れた三角形の面より下で面への射影が三角形の中にある箱の角を、面の向き(箱の中心の側)ごとに集め、一番深い角の面に近い面の角から最大 4 点。角が入らなければ GJK/EPA の 1 点。
  - 投機的な点(M0109 の追補): 面から 5 cm 以内の角も、入った角のある面の点に加える(負の貫入)。その tick に面へ届く(接近速さ × Δt > 隙間)点は触れている点と同じ値に止め、届かない点は制約しない。反発は solver の繰り返しの前の接近速さから点ごとに 1 度だけ決める。接近速さが 0.5 m/s を超えた tick の力積は、次の tick の warm start に使わない。
  - 車輪のメッシュの向き: 取り込んだ車輪メッシュの車軸(薄い辺)は F を向く。シーンの休みの向きは左 −0.25 回転・右 +0.25 回転で車軸が R になり、`TryPose`(舵角 × 回転 × 休み)の回転は R の軸、舵角は U の軸のまわりに働く。
  - `VehicleMotor.Step` は 1 tick に: (1) 剛体の状態を読み、3 秒(180 tick)ごとに上の向きの鉛直成分が −0.2 未満かを調べ、2 回続いたか `ResetRequested` ならリセット(Actor の原点を 0.5 m 上げ、Yaw だけを残した向きに、速度を 0)してその tick は力を加えない。(2) 入力: ブレーキで後退、アクセルで 1 速へすぐ入れ、前へ 1 m/s より速いときのブレーキは 1(アクセル 0)、後退のギアならブレーキの値をアクセルにする。アクセル・ブレーキ・ハンドブレーキは線形、ハンドルは二乗で、上がり・下がりの速さで均す。(3) 操作が 0.02 未満で、全輪が接地し、上の向きの鉛直成分が 0.866 より大きく、速さが 0.1 m/s 未満の状態が 15 回続くと物理の `Sleep` を送り、眠っている間は力を加えない。(4) 4 輪の光線(休みの位置の 0.1 m 上から、0.1 + 0.1 + 半径の長さ、車体を除く)、エンジンへアクセルの二乗、ブレーキのトルク(ブレーキ × 最大 + アクセルが 0 で動いている駆動輪のエンジンブレーキ = 回転数 × 0.2、ハンドブレーキは置き換え)、空気抵抗とダウンフォース(0.5·ρ·A·係数·v²、進む向きと逆)を重心に、エンジン(ギアが入っていれば車輪から決まる回転へ毎秒 4、なければ自由に回る。アイドルより下がらない)、自動変速(変速中でなく、接地して空転していなければ、6000 rpm 以上で上げ 2000 rpm 以下で下げ、0.2 秒ニュートラル)、駆動トルク(トルク × ギア比 × 効率を後輪で半分ずつ。車輪から決まる回転が最大を超えると 0)、ばね(縮み × 25000 − 縮む速さ × 減衰)を上向きに、荷重(0.5 × ばね + 0.5 × 静止の荷重)、ロールバー(左右の荷重の差 × 0.15)、舵角(入力 × 速さの効き(マイル毎時)から、アッカーマンの棒の幾何で内側が 40°)、タイヤ(縦はブレーキか駆動、横は滑りの角 × 1e5 N/rad を止めるのに要る力まで、両方を 荷重 × 路面の摩擦 × 倍率 の円で切り、切られてブレーキ中なら車輪を止める。車輪の角速度は地面の速さ/半径 + 余りの駆動)を地面の法線と車の右から作る向きで、車輪ごとに 1 つの `ForceAtPoint` で加える。
  - SportsCar の値で: 静止の重心の高さ 0.583 m、前輪の荷重 3375 N・後輪 3963 N、車輪は休みの位置より前 4.3 cm・後 6.8 cm 上。0〜100 km/h 3.42 秒、1 速から 2 速へ 5962 rpm 付近で上げて 12 tick ニュートラル、最高速 約 269 km/h(5 速、空気抵抗と釣り合う)、100 km/h からの全ブレーキで 18.0 m・1.28 秒、5 m/s 付近の全舵で半径 4.2 m(左 30.8°・右 37.4°)、坂(3 m)から 68 tick 浮いて着地、壁の手前 2.25 m で止まる。
  - System: FixedSimulation の `VehicleBodySetup`(最初の tick に剛体の向き、毎 tick 質量)、PhysicsSimulation の `VehicleMotionUpdate`(静的メッシュの同期の後、物理の tick の前)と `VehicleBodyWriteback`(物理の向きを Transform へ)、PostPhysics の `VehicleWheelPose`(子の車輪を休みの姿勢 + 上下・舵・回転)、Presentation の `VehicleChaseView`(腕の Yaw = 車の Yaw + 見回し、見回しは FInterpTo で 0 へ。`OrbitCameraUpdate` の前)。
  - スナップショット: `Vehicle`(データアセットの参照と車輪の配列を持つ設定)は `[EcsSnapshotTransient]` で入らず、復元では読み直したシーンの値のまま。`VehicleState`・`VehicleIntent`・`VehicleWheelVisual`・`VehicleCameraLook` は入る。
  - 描画: `MeshEmissive` を持つメッシュの物体は、毎フレーム発光の倍率が物体の定数に入る(既定 1、外すと 1 に戻る)。そのメッシュは発光で周りを照らさない。`LightVisibility.Visible` が偽のノードの光(4 種のどれでも)は出さない。表示中のカメラが無効になり有効な別のカメラがあれば、その次のフレームから描画のカメラが替わる。
  - テンプレート: Tab で前と後ろのカメラ、L で左のヘッドライト、BackSpace でリセット、マウスの横・右スティックで見回し(離すと戻る)、ブレーキ(0 からの踏み込みで点き 0 で消える)とハンドブレーキ(押している間)でテールランプが 200 倍(通常 4 倍)に光る。
- Intended behavior: `Docs/Specifications/UE-Template-Behavior.md` の Vehicle(UE のテンプレートの挙動)。
- Result: MatchesWithDocumentedExtension(差は M0109 の文書: 力のサスペンション(UE の既定は制約)、ブレーキランプを光らせる(UE では光らない)、慣性は箱、エンジン音なし)。

## RE-M0110 — Lumen 相当の土台(ミップ、雲、空の球、メッシュの距離場、グローバル距離場)

- Confidence: High(単体テストと WARP の GPU 試験、テンプレートの実走と画像で確認)。UE の雲との見た目の一致は Medium(参照画像を目で比べた。形はエンジンのノイズ)。実機の GPU は Low(未確認)。
- Evidence: `src/CSharpGameEngine.Rendering/RenderTextureMipChain.cs`・`MeshDistanceField.cs`、`src/CSharpGameEngine.Windows/Graphics/StageRenderer.cs`・`StageAtmosphere.cs`・`StageGlobalDistanceField.cs`・`src/CSharpGameEngine.Rendering/EnvironmentLighting.cs`、`src/CSharpGameEngine.Rendering/Server/RenderServer.cs`、`assets/Shaders/Atmosphere.hlsl`・`GlobalDistanceField.hlsl`、`src/CSharpGameEngine.Runtime.Lighting/EnvironmentComponents.cs`、`src/CSharpGameEngine.Runtime.Windows/EnvironmentCollection.cs`・`EngineHost.cs`・`StageSceneDataBuilder.cs`・`RuntimeAutomation.cs`、`tools/CSharpGameEngine.ShaderGraph.Cli/Program.cs`、`build/CSharpGameEngine.ShaderGraph.targets`・`Game.props`
- Observed behavior:
  - 材質テクスチャ: 4 つの欄のテクスチャは、(内容のハッシュ、色空間)ごとに 1 つ常駐する。基本色と発光は sRGB、法線と金属・粗さは線形。段の数は floor(log2(max(幅, 高さ))) + 1、各段は前の段の半分(切り捨て、最小 1)で、目標の画素は覆う元の画素の重なりの長さを重みにした整数の平均(半分は切り上げ)。sRGB の RGB は定数の表で 16 ビットの線形へ戻して平均し、最も近い 8 ビットのコードへ(同点は上)。段から段へは 16 ビット。s0 は異方性 8・wrap、全段を見る。画面レイヤーのテクスチャは 1 段。
  - 雲: `VolumetricCloud` があり大気があるフレームで、最初(または雲・大気・太陽が変わったとき)にパノラマ(WARP 1024 × 256)の全画素を、以降は 4 × 4 の区画の Bayer の順の 1 画素ずつを描く。各画素は、その方向の視線が雲の殻(半径 R + 5 km から R + 15 km)に入る点から最大 50 km を 24 サンプル(画素ごとに固定の乱数のずれ)で積分し、透過率が 0.005 を下回ったら止める。空の合成は、遠平面の画素(と空の球の外の画素)の空と太陽の円盤の輝度に、パノラマの「残る割合」を掛け、雲の輝度を足し、そのあと高さの霧と露出をかける。空の光の SH は、雲があるとき雲を含み、一巡ごとに作り直す。雲の時間はフレームの経過時間の和(自動実行では 8/60 秒ずつ)、風は 10 m/s で +F から 45°。
  - 空の球: `SkySphere` と大気があれば、深度が遠平面でない画素も、面の位置(標準の深度は逆行列、放射の深度は T(2^(dG) − 1) の距離)が球の中心から半径より遠ければ空にする。
  - メッシュの距離場: ビルドはゲームの `Assets\**\*.glb` ごとに `obj\...\DistanceFields\<path>.glb.cgesdf` を作り(glb か CLI が新しいときだけ)、出力の glb の隣へコピーする。項目はメッシュごと(取り込みの順)で、骨格メッシュの項目は空。ランタイムは内容のハッシュが合う項目だけを使う。基本形状は実行時に作る(プロセスで 1 回)。
  - グローバル距離場: `GlobalDistanceField` があるフレームで、描画の前に更新する。段 L のボクセルは 0.5 m × 2^L、クリップは原点のボクセルから 32 ボクセル手前を 8 の倍数に切り下げた角から 64 ボクセル。作り直すブリックは、前のフレームのクリップに無かったもの、動いた・現れた・消えた物体(行列の回転と拡大が 1e-5 を超えて変わるか、位置がカメラの動き分と 1 mm を超えて違う)の前後の範囲 + 4 ボクセルにかかるもの。ブリックの各ボクセルは、届く物体ごとにメッシュの距離場(局所の空間での三線形 + 箱の外の距離)× 最小の拡大の最小を取り、±4 ボクセルに切る。行列が有限でないか逆行列が無い物体と、距離場の無いメッシュの物体は入らない。
- Intended behavior: `Docs/Specifications/Rendering.md` の環境ライティングとメッシュの距離場、`Docs/Decisions/ADR-0078-UE-Template-Full-Parity.md` §5。
- Result: MatchesWithDocumentedExtension(差は M0110 の文書の「範囲外・残り」)。

## RE-M0111 — Lumen 相当の動的 GI と反射

- Confidence: High(WARP の GPU 試験 9 件と単体試験、テンプレートの実走で確認)。UE の Lumen との見た目の一致は Medium(参照画像を目で比べた)。実機の GPU と Epic 段は Low(未確認、WARP では不安定)。
- Evidence: `assets/Shaders/Lumen.hlsl`、`src/CSharpGameEngine.Windows/Graphics/StageLumen.cs`・`LumenSurfaceCacheLayout.cs`・`StageRenderer.Lumen.cs`・`src/CSharpGameEngine.Rendering/EnvironmentLighting.cs`、`src/CSharpGameEngine.Runtime.Windows/EnvironmentCollection.cs`・`EngineHost.cs`
- Observed behavior:
  - 有効になる条件: `RenderScalability.GlobalIllumination` が High 以上、`EnvironmentLightingSettings.Lumen` があり(`PostProcessVolume.GlobalIlluminationMethod` が Lumen で値が範囲内)、グローバル距離場がある。どれかが欠けたフレームは DDGI と空の光のフレーム(M0110 と同じ)で、Lumen の履歴は捨てる。反射は、さらに `Reflections` が High 以上で `ReflectionMethod` が Lumen のとき。
  - 段: 実機 High・WARP Medium が既定。`CGE_SCALABILITY_GI`・`CGE_SCALABILITY_REFLECTIONS` が low/medium/high/epic か 0〜3 で置き換え、反射を省くと GI に従う。不正な値は起動時に `InvalidOperationException`。
  - 物体: 距離場のあるメッシュで、行列が有限で逆行列があり、境界球がカメラから Scene View Distance(既定 200 m)以内の物体だけがカードを持つ。カードは箱(1 % + 1 mm 太らせる)の 6 面で、解像度は 1 m あたり 4 × Scene Detail × Surface Cache Resolution テクセルの 2 のべき乗(8〜最大)。大きさが 4 倍を超えて変わると作り直す。新しいカードは近い順に撮り、撮ったカード・発光の倍率が変わった物体・巡回のカードを予算のテクセルまで照らす。
  - 放射輝度キャッシュ: 間隔 2 m(0.5 m のボクセル 4 つ)から段ごとに 2 倍、世界の格子に固定。クリップに新しく入ったプローブを細かい段・近い順に、残りの予算を段 L に 2/3 ずつ巡回で追跡し、値は前の値と 0.75 で混ぜる。追跡したプローブはその次のフレームから使う。
  - 画素: 拡散の照度(Lumen の t16)があればプローブボリュームと空の光の拡散を置き換え、Skylight Leaking × 空の光の照度を下限にする。反射(t17)があれば反射の照度を置き換える。履歴は前のフレームのカメラと距離・法線が合う画素だけ。
- Intended behavior: `Docs/Specifications/Rendering.md` の環境ライティング(Lumen の行)、`Docs/Decisions/ADR-0078-UE-Template-Full-Parity.md` §5。
- Result: MatchesWithDocumentedExtension(差は M0111 の文書の「UE との差」と「範囲外・残り」)。

## RE-M0119 — Native depth verification and diagnostic contract
- Confidence: High for code/API, GPU pixels and active template execution. Wider layer/render-path/physics migrations remain separate.
- Evidence: M0119 milestone and its solution, template, precision/performance and artifact-identity manifests; RuntimeAutomation.WriteReport; ThirdPersonCharacterVerification/ClickToMoveVerification/VehicleDriveVerification and their tests.
- Observed behavior: obsolete standalone renderers, artifact targets and root templates are absent. Retired artifact values 2/3 and graph values 1/2 are rejected and not reused. The Stage ABI has fourteen entries and unchanged pinned native images. A new root template uses Stage and five external subgraphs.
- Observed behavior: template verification requests the debug layer and requires the runtime's actual d3d12DebugLayerEnabled=true. False/missing is rejected, including native continuation without Graphics Tools. Ten diagnostic tests fail with the isolated previous evaluator; all 233 FeatureRunner tests pass after correction. Normal game diagnostics defaults remain unchanged.
- Observed behavior: all eight template cases report active diagnostics and zero errors, match the nine M0111 baseline metrics, and have identical complete camera trajectories and final images to the initial movement runs. Initial runs without proven active diagnostics are retained as movement evidence only.
- Observed behavior: the current Stage artifact exactly matches the RTX-measured artifact SHA256 E485F055...B6A5; original measurement provenance is retained. Release visibility/batching at 100000 instances has median 4.10015ms, p95 4.34760ms and zero measured allocation. WARP frame timing is correctness evidence rather than a hardware frame-budget claim.
- Limit: Stage-based VS GUI automation was compiled but not launched in this run. Package/editor unit tests and packaged preview/runtime-view execution were run. Full-solution closure and counts are recorded in M0119 after the last source change.

## RE-M0120 — Portable values and CPU rendering calculations (partial extraction)
- Confidence: High for the moved unit; the full layer migration is incomplete.
- Evidence: `src/CSharpGameEngine.Rendering/EnvironmentLighting.cs`, `StageRenderInputs.cs`, `EmissivePatch.cs`, `Pipeline/{PhysicalLighting,DirectionalShadowCascades,EmissivePatchBuilder}.cs`; native `StagePhysicalLightApi.cs` and `EmissivePatchSubmission.cs`; Rendering.Tests and EngineLayerSpec.
- Observed: environment/mesh/material inputs no longer require Windows.dll. Photometric units, deterministic light/shadow budgets, cascades and emissive rectangles compile and pass their unchanged CPU expectations in net10.0 tests. Native submission adapters remain separate. Five normalized source comparisons match the pre-move calculations; seven WARP image hashes also match.
- Result: Matches for the extraction unit. Native frame planning/state and four render paths remain subsequent work. EveryStageInputPathExists additionally found three old Spatial directory inputs; the catalog now names their actual source/test files and change-selection tests cover the paths.

## RE-M0120 — Portable L5 camera mathematics
- Confidence: High for the math relocation; frame/view policy extraction remains incomplete.
- Evidence: `src/CSharpGameEngine.Rendering/View/RenderCamera.cs`, Core project reference, `tests/CSharpGameEngine.Rendering.Tests/CameraTests.cs`, EngineLayerSpec, `.artifacts/M0120/camera-preservation.json` and `portable-camera-*.log`.
- Observed: the orbit/first-person/bounds-fit math and defaults pass the same 21 CPU tests in net10.0. The full struct body matches the pre-move implementation after the type-name change. Rendering has no native dependency and L4 source does not reference the L5 View folder/types. Native Stage still calls the camera during initialization/submission; those calls are not yet an executor-only boundary.
- Result: Matches for relocation (Rendering157, native camera/recovery43, layer6, catalog/selection31 passed). Seven pinned images and the Stage binary are unchanged. Scene component type identity is preserved; the legacy public graphics-camera type has moved to View.RenderCamera.

## RE-M0120 — Frame/Object ABI ownership
- Confidence: High for the constant-buffer extraction; frame planning/state extraction remains incomplete.
- Evidence: `Pipeline/StageGpuLayouts.cs`, native StageRenderer and its Lumen partial, L7 `HlslCompiler`/`HlslShaderReflection`, `StageConstantLayoutReflectionTests`, EngineLayerSpec and `.artifacts/M0120/gpu-layout-*.log`.
- Observed: L4 sequential structures with inline four-element arrays produce the actual 960/192-byte shader layouts. All 35 native offsets are derived from them, including the Lumen capture writes. Six independent DXC reflection cases verify field sizes/offsets and row-major matrices, with Lumen's inverse-matrix alias handled explicitly. A card PS has ObjectCB but no unused FrameCB. Runtime has no compiler/reflection dependency; the existing artifact JSON metadata remains separate from raw compiler reflection.
- Result: Matches for this extraction. Native GPU31, CLI15, portable rendering157, layer7 and FeatureRunner240 pass; Stage artifact SHA and seven images remain unchanged. This targeted validation follows the preceding full solution3539 at ff43243 and does not relabel that older full run as covering the new CB extraction. Remaining M0120 scope is recorded in the milestone.

## RE-M0119 — Default Stage startup without a camera submission
- Confidence: High. `SceneDistanceGpuIntegrationTests.DefaultRendererUsesLogarithmicDepthBeforeAnyCameraSubmission` failed before the change and passed after it.
- Observed before: the initialization wrote a perspective matrix but retained the default linear depth packet; at 4.145584m the first frame wrote 0.9759626 instead of logarithmic 0.22822031. Camera-factory defaults and explicit SetCamera tests did not exercise this path.
- Observed after: initialization applies the same validated camera setup before filling every slot. Three frames without SetCamera match the physical-distance oracle and use an active debug layer without errors. Native regression38 passes, including the unchanged seven pinned images. SDK0.1.29, evidence in M0119/M0120.

- Earlier cross-project verification: 5f4d647/SDK0.1.29 includes the relocated types/math, typed CB/reflection and default startup fix. All22 projects pass3548 tests, fail0, skip2 opt-in hardware measurements, exit0, maximum physical RAM66%. The seven golden frames and Stage artifact remain unchanged. Evidence: `.artifacts/M0120/native-startup-solution.summary.json`. M0120 remains Partial for the outstanding view/frame/server/path extraction.

## RE-M0120 — Validated main-view submission (SDK0.1.30)
- Confidence: High for value creation/submission/recovery; native adapter and server extraction remain incomplete.
- Evidence: Rendering/RenderView.cs, View/RenderViewPolicy.cs, Native SetView, RenderFrameState.RecordView/Replay, RenderViewTests, RenderViewGpuIntegrationTests.
- Observed: L4 owns immutable validated projection/depth/reconstruction/pose/exposure/basis data; L5 chooses perspective-logarithmic or orthographic-linear construction. An invertible auxiliary projection preserves physical XY/W rays even for extreme far. Invalid input is rejected before native state writes. Three GPU cases match legacy submission and retain the image through no-command frames and actual device-generation replacement. Legacy metadata is still retained for resize reconstruction.
- Visibility query rename: the former frustum/hysteresis-only RenderView is RenderVisibilityView; its structure and selection/batching algorithm are unchanged. Only three existing consumer files needed migration.
- Verified: portable169, GPU31 and final6, layer7, FeatureRunner242; seven hashes and Stage binary remain unchanged. Evidence paths and memory maxima are in M0120. The old full-suite3548 measurement predates this value extraction.

## RE-M0120 — Portable CPU frame readback (SDK0.1.31)
- Evidence: Rendering/PixelColor.cs, PixelReadbackResult.cs, RenderPixelFormat.cs; native readback initializer, Model/Triangle verifiers, RuntimeAutomation and GeneratedEditorPreviewBridgeEmitter.
- Observed: the CPU payload has no graphics-provider type. Native RGBA8 allocation/copy remains unchanged and maps to the engine format at return. Padded rows, RGBA/BGRA verifier decoding and the generated runtime capture signature remain connected.
- Verified: Windows43, generator338, layer7, editor15 pass; seven images and Stage binary are unchanged, peak physical RAM45%. Evidence is recorded in M0120. Overall server/frame/path extraction remains Partial.

## RE-M0120 — L5 scene vocabulary and primitive assembly (SDK0.1.32)
- Evidence: Runtime.Windows shared Camera/PrimitiveMesh/PrimitiveType/PrimitiveRoster and explicit projitems, portable StageMeshGeometry, SceneCameraTests, migrated roster/GPU tests, primitive-preservation.json.
- Observed: scene type names/defaults/enum values/attributes stay the same while their assembly is now the game runtime. The primitive builder and shape distance-field cache no longer belong to RenderServer. Mesh/material/object ordering and tangent arithmetic are preserved; two moved icons match byte for byte.
- Verified: targeted41 and the full solution22 projects pass3570, fail0, skip2 opt-in hardware measurements, exit0. Core registration/layer checks, Rendering169, FeatureRunner246, Windows779, generator338 and VSIX1197 are included. Maximum physical RAM45%. Seven golden images and the Stage artifact remain unchanged. This full run includes the preceding view/readback migrations. Overall pipeline/server/four-path extraction remains Partial. Evidence: `.artifacts/M0120/primitive-solution.summary.json`.

## RE-M0120 — Portable diagnostics and backend failure classification (SDK0.1.33)
- Evidence: Rendering/Backend/RenderDeviceMessage.cs and RenderBackendFailureException.cs; native D3D12Exception/GraphicsDevice mapping; Stage/RenderServer diagnostic APIs and RenderBackendDiagnosticsTests.
- Observed: the server receives an engine severity and opaque diagnostic identity fields, plus a provider-classified optional recovery reason. It no longer interprets provider HRESULT constants or catches a provider exception type. The native exception retains its public name, API/HResult/message and inherits the portable failure. Unknown failures remain non-recoverable.
- Verified: native18 (including real queue mapping, device recovery, view replay, seven unchanged frames and hot reload), layer7, FeatureRunner247 pass; peak physical RAM41%, no guard stop. All five severities use explicit empty filters on the test-owned queue, with product filters unchanged. The preceding full22/3570 run was SDK0.1.32 and does not cover this later change. Overall server/plan/path extraction remains Partial.

## RE-M0120 — Value-owned view reconstruction (SDK0.1.34)
- Evidence: RenderViewSource, View.RenderViewPolicy, Server.SetViewSource, FrameState source snapshot/replay, RenderViewSourceTests and four additional RenderViewGpuIntegrationTests.
- Observed: the snapshot retains L4 input data instead of an L5 controller. Resolve validates and creates the RenderView before backend submission. Camera sources reproject using the replacement surface aspect unless overridden; explicit VP sources preserve their matrix while resolving physical depth metadata for that surface; typed fixed views remain exact. Later camera mutations cannot alter the saved value.
- Verified: portable178, GPU30, layer7, FeatureRunner248 pass. Raw camera mathematics matches at three sizes and two far ranges. Warm resolution across all three kinds allocates zero managed bytes. Four GPU routes preserve resize semantics, three replay frames and actual generation2 recovery; seven pinned images and Stage artifact are unchanged. Maximum physical RAM43%. Evidence paths are in M0120. Full22/3570 remains the earlier SDK0.1.32 measurement; server/plan/path extraction remains Partial.

## RE-M0120 — Live backend submission and artifact ports (SDK0.1.35)
- Evidence: IStageRenderBackend/IStageShaderArtifact, StageRenderer.Backend, Server backend field/calls, FrameState replay, StageRenderDefaults, native canonical payload cache and RenderBackendDiagnosticsTests.
- Observed: normal server commands and frame replay use the L4 interface. The snapshot names neither the native renderer nor its light kind. Shader closure synchronization consumes owned canonical bytes while provider parsing stays native. Clear values/headroom/build capability flags have one portable owner and unchanged values.
- Verified: Debug native41, layer7, FeatureRunner249 and Release runtime-boundary/payload4 pass. Canonical shader bytes equal the artifact file, seven images and the Stage binary are unchanged, peak physical RAM42%. The native renderer's private directional record required explicit disambiguation from the public submission type; internal getters use explicit interface implementation.
- Limit: server/factory placement and actual frame-plan execution remain subsequent work. This is a consumed submission port, not completion of the executor-only native design. The earlier full22/3570 result remains SDK0.1.32 provenance.

## RE-M0120 — Portable server ownership and native composition (SDK0.1.36)
- Evidence: Rendering/Server/RenderServer.cs and RenderFrameState.cs; IStageRenderBackendFactory; native D3D12RenderServer; View.RenderServerViewExtensions; actual assembly/API boundary tests and EngineLayerSpec.
- Observed: the portable server owns roster closure, pending/completed value snapshots, device lifecycle and recovery. Its assembly references no native binding and its public creation API carries no window or native options. The native factory owns their snapshots, validates its artifact implementation and creates/recreates the provider. Camera/matrix convenience remains L5; actual scene type names are unchanged. Native test extensions locate the current generation through a weak composition table.
- Verified: targeted52 plus full22 projects pass3596/fail0/skip2 opt-in hardware tests, exit0; Windows791, Rendering178, Core159, FeatureRunner250, generator338, VSIX1197 and Import73 are included. Release boundary/canonical-payload5 also pass. Seven pinned images and Stage artifact remain unchanged. Maximum physical RAM66% full/62% Release, no guard stop. Evidence: `.artifacts/M0120/server-move-solution.summary.json` and the milestone.
- Limit: native planners, compatibility initialization and frame ordering/state remain; current submission port is not yet the final frame-plan executor. Four paths and physics milestones remain incomplete. The older full22/3570 SDK0.1.32 measurement is historical provenance.
