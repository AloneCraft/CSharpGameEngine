# ECS and Runtime specification

- Status: Specified, except F-L3-018 remains PartiallyImplemented
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0055](../Decisions/ADR-0055-Static-Ecs-VNext-And-Deterministic-Execution.md)
- Related lower specification: [Ecs-Soa-Systems](Ecs-Soa-Systems.md)
- Target layers: L3, L5, L6, L7

## Scope and observed baseline

The current generated closed-set archetype runtime, SoA storage, deterministic simulation modules, system phases, and `MoveArchetype`/delayed structural behavior are real baseline evidence. F-L3-018 is therefore `PartiallyImplemented`: phase dispatch exists, while the shipping loop has not adopted the target fixed-tick catch-up state machine. This specification defines the vNext contract; it does not claim the remaining command buffer, generation, enable/change tracking, job, snapshot, or portable Transform work exists today.

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

`EntityCommandBuffer` is the sole structural path for Create, Destroy, Add, Remove, and Move. At fixed-tick end it replays a deterministic total order:

```text
system stable ID -> partition ID -> worker-local sequence -> command sequence
```

Worker commands and events are merged under that order. Immediate world mutation during system execution is rejected with a diagnostic naming the system and operation.

## Generated storage and scheduling

Enableable components have entity and component enable bits. Each component write increments a generated change version. Changed queries compare caller-visible version tokens without scanning unrelated archetypes.

The source generator builds an access graph from component/resource read/write declarations. It reports read/write conflict, ambiguous ordering, and cycles before execution. The runtime only executes the generated schedule; it does not perform dynamic mask matching or dependency solving per frame.

For one through sixty-four component types, source generation uses one `ulong` membership mask while compiling code. Generated runtime loops contain direct archetype dispatch and pay no mask or branch penalty. F-L3-020 remains `Deferred`: only a future real need may add the 65-128 two-mask generator path. It must not regress the 1-64 path, and 129+ types produce an explicit diagnostic.

## Snapshot artifact

Snapshots carry a generated schema fingerprint, canonical component ordering, tick, deterministic state/event hashes, byte length, and checksum. Unknown fingerprint, unsupported version, overflow, or checksum failure is rejected before world mutation. Old snapshot readers are not retained; a feature that changes the schema updates all producers/consumers and regenerates fixtures in the same change.

## Failure diagnostics and migration

- Stale entity reference: index, expected/actual generation, and world identity.
- Structural misuse: system stable ID, phase, command type, and required buffer path.
- Scheduler conflict/cycle: generated symbol, access sets, and cycle path.
- Snapshot rejection: fingerprint/version/checksum field and source provenance.
- Fixed-tick overrun: accumulator, attempted tick count, cap, and emitted degradation policy.

Each feature migration removes the previous API/source/artifact reader in its own change. It does not preserve a legacy runtime path, type alias, or snapshot reader.

## Performance and release evidence

ECS performance captures five warm-up plus thirty measured samples and records median/p95/MAD, type count, query shape, worker count, generated-source hash, and machine fingerprint. It explicitly tests 1, 64, and the excluded 65-type boundary. Release evidence contains generated schema and schedule closure, deterministic replay data, and all required Debug/Release results.

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
| F-L7-032 | 6 | `ecs.performance` | Headless / Foundation | 7200s | Thirty-sample no-mask-penalty evidence. |
| F-L7-033 | 7 | `ecs.release` | Headless / Foundation | 7200s | Schema, schedule, replay, performance release closure. |
