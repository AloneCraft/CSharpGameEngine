# ADR-0055 — Static ECS vNext and Deterministic Execution

- Status: Accepted
- Date: 2026-08-25

## Context

The generated static ECS architecture has working archetype/phase foundations, but entity reuse, structural mutation, fixed-tick catch-up, enable/change tracking, job ordering, shared Transform placement, and snapshots require a coherent next contract.

## Decision

Use index-plus-generation entities with slot retirement on generation wrap; portable L5 shared Transform source; a fixed 60 Hz scheduler with eight catch-up ticks; tick-end unified structural command replay; generated access graph diagnostics; deterministic worker merge; and schema-fingerprinted snapshots. Keep the one-to-sixty-four-type path as generator-time single `ulong` matching with no runtime mask penalty.

## Consequences

- F-L3-020 stays Deferred; a two-mask 65-128 path is future-only and cannot tax the 1-64 path.
- Old immediate/legacy structural APIs, namespace placement, and snapshot readers are removed with each feature migration.
- Unknown/corrupt snapshots are rejected rather than interpreted compatibly.
- Job conflicts/cycles become generation-time diagnostics, not run-time ordering guesses.

## Migration and verification

M0059 integrates the fixed-tick decision into the generated shipping loop. F-L7-030 and F-L3-018 are now Implemented with executable `ecs.baseline` and `ecs.fixed-tick` Stage oracles. The scheduler retains overrun backlog, while `State.FixedTick` remains the sole tick source of truth. `ecs.*` and `foundation.deterministic-jobs` Stages in [Ecs-Runtime](../Specifications/Ecs-Runtime.md) continue to define the remaining target proof.

## Related features

F-L7-030, F-L3-018 through F-L3-022, F-L5-035, F-L6-007, F-L7-031 through F-L7-033, F-L3-020.
