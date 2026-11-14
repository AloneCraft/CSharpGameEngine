# ADR-0092 — Static snapshot value graphs

- Date: 2026-11-02
- Status: Accepted
- Features: F-L6-007, F-L1-011
- Design: [Static production flow](../refactoring-designs/20261031-static-production-flow.md)

## Context

Snapshot generation previously called the generic runtime codec to discover member shapes and serialize values. The approved static-first direction requires the closed Component set to determine those operations during compilation. Existing Scene authoring also contains native references and configuration objects that do not themselves promise persistence.

## Decision

Resolve shape, transient state and value access from Roslyn symbols. Incremental outputs retain strings and value facts, not Compilation or runtime Type objects. Collect System, relation, effective Scene, startup and Bootstrap components through the existing archetype inputs.

Generate direct writers/readers for supported scalars and closed value graphs. Canonical member ordering and direct constructor calls preserve current bytes and constructor validation. Preserve nullable nulls explicitly. Constructors for readonly fields use parameter order, independently of serialized field order. Graph generation is bounded to 256 distinct types.

Generated World paths have no automatic reflection fallback. Unsupported persistence requested by a direct World call in the same source compilation produces SYSGEN044. Roslyn checks the receiver type so unrelated methods with the same name do not request that capability. Scene-only code may compile without persistence support; invoking its unsupported generated persistence path fails explicitly. This separates an optional capability from valid authoring state without relabeling persistent data as transient. General reference graphs, custom JSON converters and conditional member omission need additional static design before support is claimed.

Keep the standalone public generic codec as its own API and use it as an independent legacy compatibility oracle in tests. Canonical-payload checks and atomic restore remain required. [ADR-0095](ADR-0095-Profile-Neutral-Snapshots.md) defines the implemented version-2 container/numeric migration and its statically provable version-1 compatibility boundary.

## Verification and consequences

Generated nested-value tests compare bytes against the independent codec, round-trip nullable/identifier/nested state, reject constructor-invalid data and compile under nullable warnings-as-errors. Generator Debug/Release, Entities Debug/Release and an actual game build pass; final evidence is in [M0128](../Milestones/M0128-Static-Production-Flow.md).

Compile-time generation increases generated source size in exchange for fixed member access and a visible unsupported-type boundary. Same-compilation diagnostics do not inspect calls made only by later assemblies; those calls receive the explicit unsupported runtime failure rather than reflective execution. Configurable numeric builds and profile-neutral persistence are implemented under ADR-0093 and ADR-0095. M0128 records the remaining production acceptance work.
