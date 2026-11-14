# ECS and Runtime specification

- Status: Implemented through schema-fingerprinted snapshots; performance and release closure remain specified
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0055](../Decisions/ADR-0055-Static-Ecs-VNext-And-Deterministic-Execution.md), [ADR-0092](../Decisions/ADR-0092-Static-Snapshot-Value-Graphs.md), [ADR-0095](../Decisions/ADR-0095-Profile-Neutral-Snapshots.md)
- Related lower specification: [Ecs-Soa-Systems](Ecs-Soa-Systems.md)
- Target layers: L3, L5, L6, L7

## Scope and observed baseline

The generated closed-set archetype runtime now includes generational entities, one structural command barrier, portable Transform, enable/change tracking, deterministic fixed-partition jobs, compile-time schedule diagnostics, and schema-fingerprinted cell-delta snapshots. `FixedTickScheduler` supplies 60 Hz fixed updates, at most eight simulation ticks per host frame, retained backlog, pause/step control, and an interpolation boundary. M0064 exposes a read-only `World.GetStatistics()` snapshot and demonstrates FixedTick-only motion in the default Showcase. Final performance and release evidence remain later work.

## Runtime boundaries and shared Transform

`Transform` becomes a portable L5 source-shared module with no Windows namespace or platform API dependency. Rendering, physics, and audio consume that same type through lower-layer contracts. The migration removes the old Windows-namespaced source in the same change and updates all consumers; no type-forwarding compatibility layer remains.

Entity identity is `Entity(Index, Generation)`. Destruction increments a slot generation before reuse. If a generation would wrap, the slot is permanently retired. Entity references validate index, generation, and world identity before they resolve a location.

## Tick state and structural changes

The default simulation cadence is 60 Hz with at most eight catch-up ticks per presented frame. The fixed state machine is:

```text
Running -> (accumulator >= tick) -> FixedTick[0..8] -> Extraction -> Present
Running -> Pause -> Paused -> Step -> one FixedTick -> Paused
Running or Paused -> Dispose -> Disposed
```

Interpolation observes only completed tick snapshots. It cannot expose partially replayed structural changes.

`State.FixedTick` is the only simulation tick source of truth. `FixedTickScheduler` reports the number of available steps, the pre-cap attempted count, clamp transition, retained backlog, and `InterpolationAlpha`; it does not own a tick number. The generated shipping `Run` is the only loop entry that samples `Stopwatch` and passes elapsed counter units into `RunFrame`. With an `EngineHost` it passes them through `EngineHost.FrameTime`, which during an automation run (`CGE_AUTOMATION_FRAMES`) replaces them with exactly the per-frame cap (8 ticks at 60 Hz, `RuntimeAutomation.FixedFrameTime`), so an automated run's frame-numbered input and observations are the same on every adapter. Systems and physics receive only `State` and deterministic simulation data.

While `State.PlaybackState` is `Paused`, elapsed host time is ignored. `StepSimulation()` changes the state to paused and queues one or more manual steps; each host frame consumes at most one queued step. `PauseSimulation()` and `ResumeSimulation()` clear the pending manual-step queue. FrameInput, global spatial refresh, and Presentation still run once per host frame while paused. Editor Preview uses the same complete simulation-tick helper through explicit `RunEditorFrame` calls and never performs wall-clock catch-up.

When the attempted step count exceeds the cap, the scheduler retains the unconsumed backlog. The generated loop emits one `CGE-FIXED-TICK-OVERRUN` diagnostic on entry to the clamped state containing attempted steps, maximum steps, backlog, and `RetainBacklog`.

`EntityCommandBuffer` is the sole structural path for Create, Destroy, Add, Remove, and Move. At fixed-tick end it replays a deterministic total order:

```text
system stable ID -> partition ID -> worker-local sequence -> command sequence
```

Worker commands and events are merged under that order. Immediate world mutation during system execution is rejected with a diagnostic naming the system and operation.

## Generated storage and scheduling

Every generated archetype owns entity and component enable bits. Disabled rows remain structurally present but are omitted from generated system dispatch and changed queries. Create, move, component-enable transition, and conservative system `ref` access stamp per-row versions from one World-owned clock. `ChangeVersion.IsNewerThan` defines the wrap-safe half-range ordering; caller-span changed queries compare captured tokens without allocation or scanning unrelated archetypes. Archetype moves preserve entity/common-component enable state and enable only newly added components.

The 1–64 component compilation emits one low enable word and direct generated calls. The high word and its arguments exist only for compilations of more than 64 component types, and each further 64 types add one word (Word2, Word3, …; ADR-0079), so the extended path does not enter the common generated shape.

`ref T` declares component write access. `in T` on a non-nullable struct declares component read access (ADR-0077; the former `[ReadOnlyComponent]` attribute is retired and is a CS0619 build error naming the replacement); a context is a reference type passed by value. The trailing `ref State` is optional. `[RunsAfter(typeof(TSystem))]` and `[RunsBefore(typeof(TSystem))]` order systems of one phase for every system; systems with no path between them run by full name (ADR-0073). `[ScheduledSystem]` only opts a system into the static access schedule: the source generator emits low/high read and write masks, predecessor indices, and dependency waves, where an unscheduled system in between still orders two scheduled ones. A target that is not a system (SYSGEN033), a cycle (SYSGEN034), an order against the phase order (SYSGEN035), and an unordered read/write or write/write conflict between scheduled systems (SYSGEN032) are build errors. No reflection, dictionary dispatch, or per-frame dependency solving is introduced.

`DeterministicJobRuntime` operates on fixed index partitions with at most the configured number of worker tasks. Every worker receives value-only unmanaged job and state data, writes only its assigned output range, and records events under `(system, partition, worker-local, command)` keys. Results become observable only after all partitions succeed and a stable partition-order merge completes. Failure selects the lowest failed partition after all workers settle; failure and cancellation discard the output and cannot mutate the caller input.

For one through sixty-four component types, source generation evaluates only one low `ulong` membership word. For 65 through 128 types it evaluates low and high words, and one more word per further 64 types up to 512 (ADR-0079: plugins share the game's component budget); a higher index never aliases a lower one. Generated runtime loops contain direct archetype dispatch and pay no mask or branch penalty. SYSGEN026 rejects 513 or more types.

## Snapshot artifact

World snapshot structure and value access are generated from the closed Component set by Roslyn. Compilation includes System inputs, relation targets, effective Scene components, startup components and the injected Bootstrap component. Generated code holds schema strings and calls fields, properties, scalar parsers and constructors directly. It does not discover members at runtime or fall back to `EcsSnapshotComponentCodec<T>`. Supported graphs include primitive values, strings, enums, Nullable values, identifiers, LogicalScalar and nested value types; readonly fields require one public constructor matching the fields, and constructor argument order is preserved independently of canonical member-name order. Custom JSON converters, general reference objects and conditional member omission are outside the current generated contract. A graph is bounded to 256 distinct types.

Requesting a snapshot operation in the source compilation with an unsupported persistent Component produces SYSGEN044. A Scene that does not request persistence can contain unsupported authoring state; the generated unsupported persistence path fails explicitly if invoked later. Native resources and derived state must be explicitly transient rather than silently omitted to make persistence succeed. The standalone public generic codec is a separate API and remains outside this generated World path.

Writers produce container version 2. Scalar leaves declared as LogicalScalar, Single or Double share a real-value shape identity and exact finite decimal strings from NumericValueCodec. Named component identities, integer types and other structural distinctions remain significant. Equal representable values have identical artifacts across supported profiles. The reader validates source canonical JSON, exact canonical number strings and envelope hashes independently of destination precision, then converts with checked nearest-even rounding. Overflow is rejected. Constructor validation compares the destination-normalized state to the constructed value, so legitimate rounding succeeds while constructors that replace persisted values are rejected. This does not promise identical simulation results between profiles.

`World.CaptureSnapshot` serializes only a completed structural barrier. The generated archetype descriptors fingerprint every Component shape and whether it is persistent or transient. Payloads contain canonical `WorldCellId` and `PersistentEntityId` ordering, entity/component enable state, the completed tick, deterministic state/event hashes, byte length, and SHA-256 checksum. Runtime `Entity` handles, `IRuntimeComponent`, `Parent`, `SceneNodeTag`, native resources, and other derived links are never authoritative snapshot state. A validated value type (a struct with no public fields and only properties without setters, whose one public constructor names the properties holding its state, such as the typed light quantities) is written as those properties and read back through that constructor, so its range check also guards a restore; its descriptor lists those properties only, and computed ones such as `IsValid` are not state.

`World.UpdateSnapshot` replaces deltas for currently owned cells and retains prior deltas for unloaded cells. Generated artifact reading validates and normalizes every persistent component into a current document before restore, pending-cell handling or merge. Restore validates the entire loaded-cell subset before applying any Component value or tombstone. Missing persistent entities, cell ownership mismatch, archetype mismatch, noncanonical Component payload, unknown fingerprint, unsupported version, invalid length, overflow, or checksum failure therefore leaves the World unchanged. Deltas for unloaded cells remain an explicit `EcsSnapshotPending` and are applied only after their base cell is loaded.

Only version 2 is read. Until the first public release saves carry no compatibility promise (ADR-0098 §5 D), so a version-1 artifact is rejected as `UnsupportedVersion` with a message to save the world again with the current engine; the World is left unchanged. The generator emits one portable schema (real values as `CSharpGameEngine.Numerics.NumericValue`) and no historical schema/archetype correspondence. No reflection reader is introduced.

## Failure diagnostics and migration

- Stale entity reference: index, expected/actual generation, and world identity.
- Structural misuse: system stable ID, phase, command type, and required buffer path.
- Scheduler conflict/cycle: generated symbol, access sets, and cycle path.
- Snapshot rejection: fingerprint/version/checksum field and source provenance.
- Fixed-tick overrun: accumulator, attempted tick count, cap, and emitted degradation policy.

Feature migrations remove superseded runtime implementations. There is no data-compatibility exception: the former Snapshot version-1 reader of ADR-0095 was removed under ADR-0098 §5 rule 2 (2026-11-15).

## Performance and release evidence

ECS performance builds independent 1, 64, 65, and 128 Component compilations. Each executes its highest-index generated write query over 100,000 live entities for five warm-ups plus thirty measured samples with Tiered JIT disabled and records median/p95/MAD, exact type and mask-word count, query shape, worker count, generated-source hash, deterministic state hash, allocation, Git provenance, and machine fingerprint. The validator independently recomputes the state hash and rehashes/reinspects persisted generated source. One through 64 must contain no high word; 65 and 128 must contain and execute it. Debug elapsed time is entirely diagnostic but exact workloads, source/state integrity, and zero allocation remain mandatory. Release requires 6 ms median, 8.33 ms p95, zero allocation, and no more than 20 percent auxiliary cross-process median regression from the one-type to 64-type path (thirty samples' nearest-rank p95 is a single sample, so p95 is gated only by the absolute budget).

ECS release evidence reruns deterministic jobs, schedule diagnostics, snapshots, and performance under dedicated profiles. One canonical report binds all four complete Stage results, four raw reports, four common performance reports, twenty-two fixed ECS/generator/toolchain inputs, machine identity, and Git provenance. Generated output is independently rehashed while producing the report, but later validation must succeed after derived `obj` deletion by reopening the retained Stage/raw/performance evidence and generator input closure. The report is an ECS-track input, not a package, SBOM, installer, or product manifest.

## Stage oracle matrix

| Feature | Level | Stage | Mode | Timeout | Oracle |
|---|---:|---|---|---:|---|
| F-L7-030 | 1 | `ecs.baseline` | Headless / Foundation | 900s | Generated archetype and scene baseline. |
| F-L3-018 | 2 | `ecs.fixed-tick` | Headless / Foundation | 900s | 60 Hz, pause, step, interpolation, eight catch-up cap. |
| F-L5-035 | 2 | `ecs.runtime-boundary` | Headless / Foundation | 900s | Portable Transform has no Windows dependency. |
| F-L3-021 | 3 | `ecs.structural-changes` | Headless / Foundation | 900s | Stable merged command replay. |
| F-L3-022 | 4 | `ecs.change-tracking` | Headless / Foundation | 900s | Enable/change generated queries, no 1-64 penalty. |
| F-L3-019 | 4 | `foundation.deterministic-jobs` | Headless / Foundation | 900s | Scheduled and single-thread hashes match. |
| F-L7-031 | 5 | `ecs.schedule-diagnostics` | Headless / Foundation | 900s | Access conflicts and cycles diagnosed pre-run. |
| F-L6-007 | 5 | `ecs.snapshot` | Headless / Foundation | 900s | Fingerprint and corruption rejection. |
| F-L7-032 | 6 | `ecs.performance` | Headless / Foundation | 7200s | Four 5+30-sample generated boundary reports prove one-word/two-word shape, budget, state, and zero allocation. |
| F-L7-033 | 7 | `ecs.release` | Headless / Foundation | 7200s | Schema, schedule, replay, performance release closure. |

## M0064 observable runtime state

`World.GetStatistics()` returns alive entity count, allocated entity slots, pending destroys, and pending scene unloads without exposing mutable storage. The Showcase dashboard reads that snapshot plus completed `State.FixedTick`, playback state, and interpolation alpha. Its triangle-wave entities advance only in FixedSimulation; Pause stops them, and each Step action completes exactly one tick while FrameInput and Presentation continue.
