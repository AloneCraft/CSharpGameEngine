# C# Game Engine — Architecture & Implementation Ledger Skill

## Purpose

This skill keeps the C# Game Engine documentation synchronized with the repository as implementation proceeds.

Its job is to maintain four kinds of truth:

1. **Planned architecture** — what should exist across L0–L9.
2. **Implemented architecture** — what currently exists in source code and artifacts.
3. **Reverse-engineered behavior** — what the current implementation actually does, inferred from code, tests, generated files, runtime behavior, and diagnostics.
4. **Conformance status** — whether the implementation and the reverse-engineered specification agree with the intended specification.

The documentation must never claim that a feature exists merely because it appears in a plan or prompt. Planned, implemented, inferred, verified, partial, and missing states must remain distinct.

---

## Product naming

- User-facing product name: **C# Game Engine**
- Internal identifier: **CSharpGameEngine**
- Do not use `#` in file names, project names, assembly names, namespaces, NuGet IDs, VSIX IDs, cache keys, or command names.

---

## Architecture layers

Use these layers consistently:

- **L0 — Platform / Native Backends**
- **L1 — Core Kernel**
- **L2 — Type Schema / Identity**
- **L3 — ECS / Job Runtime**
- **L4 — Domain Servers**
- **L5 — Runtime Feature Modules**
- **L6 — Runtime Artifacts**
- **L7 — Static Compiler / Bake**
- **L8 — Visual Studio Authoring Environment**
- **L9 — Game / Application**

Layer numbers are architecture metadata. Do not include `L0`, `L1`, and similar prefixes in public namespaces unless an existing project already does so and changing it would be disruptive.

Dependency rule:

> Higher layers may depend on lower layers. Lower layers must not depend on higher layers.

Additional mandatory boundaries:

- Runtime L0–L6 must not depend on Roslyn or Visual Studio SDK.
- L7 compiler/bake operations must run without Visual Studio.
- L8 is a frontend over standalone engine/compiler/runtime functionality.
- `.cs` files and source assets are authoritative source.
- Artifacts and caches are generated and reproducible.
- ECS stores runtime data in SoA/archetype/chunk form.
- Visual hierarchies are projections, not runtime object ownership graphs.

---

## Documentation files to maintain

Create missing files when the repository reaches the point where they are useful. Do not create dozens of empty placeholders.

Recommended structure:

```text
Docs/
├ Architecture.md
├ Roadmap.md
├ Implementation-Status.md
├ Reverse-Engineered-Spec.md
├ Conformance.md
├ Layers/
│  ├ L0-Platform.md
│  ├ L1-Core.md
│  ├ L2-Schema.md
│  ├ L3-ECS.md
│  ├ L4-Servers.md
│  ├ L5-Runtime-Features.md
│  ├ L6-Artifacts.md
│  ├ L7-Compiler-Bake.md
│  ├ L8-VisualStudio.md
│  └ L9-Game.md
├ Specifications/
│  ├ Numerics.md
│  ├ ECS.md
│  ├ Scene.md
│  ├ Rendering.md
│  ├ Omni.md
│  ├ Assets.md
│  ├ Behavior.md
│  └ Tooling.md
├ Decisions/
│  └ ADR-NNNN-*.md
└ Milestones/
   └ MNNN-*.md

CHANGELOG.md
README.md
```

Do not create a specification file for a subsystem that has neither a real plan nor implementation evidence.

---

## Status vocabulary

Every tracked feature must use exactly one primary status:

- `Planned`
- `Investigating`
- `Specified`
- `Implementing`
- `Implemented`
- `PartiallyImplemented`
- `Verified`
- `Blocked`
- `Deferred`
- `Removed`

Optional verification state:

- `Unverified`
- `CodeVerified`
- `TestVerified`
- `RuntimeVerified`
- `ReverseEngineered`
- `ConformanceVerified`

Never use vague terms such as “done”, “mostly done”, or “should work” without mapping them to one of these states.

---

## Feature ledger format

Maintain a single feature ledger in `Docs/Implementation-Status.md`.

Each feature entry should contain:

```markdown
## F-L3-004 — Archetype storage

- Layer: L3 — ECS / Job Runtime
- Parent capability: ECS Runtime
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/ECS.md#archetype-storage
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/Archetype.cs
  - src/CSharpGameEngine.Ecs/Chunk.cs
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/ArchetypeTests.cs
- Reverse-engineered behavior:
  - Entity locations are resolved through World-owned location tables.
  - Components are stored in chunk columns by component type.
- Conformance: Matches
- Known deviations: None
- Next action: Implement structural command buffering.
```

Feature IDs should be stable and never reused.

Recommended ID pattern:

```text
F-L<layer>-<number>
```

Examples:

- `F-L0-001` Win32 window host
- `F-L1-003` StableId
- `F-L3-012` Structural command buffer
- `F-L5-008` Omni camera-relative mapping
- `F-L8-004` Scene hierarchy tool window

---

## Reverse-engineering rules

Whenever code already exists, derive the current behavior from evidence before updating specifications.

Inspect, in this order when relevant:

1. Public API and type declarations.
2. Internal implementation.
3. Unit and integration tests.
4. Generated source.
5. Build scripts and project references.
6. Runtime logs and diagnostics.
7. Serialized/artifact formats.
8. Actual execution results.

For every reverse-engineered claim, record evidence paths and confidence:

- `High` — directly enforced by code and tests.
- `Medium` — strongly implied by code, but incompletely tested.
- `Low` — inferred from naming, comments, or incomplete paths.

Do not rewrite intended specifications to match accidental bugs. Instead record a conformance deviation.

Example:

```markdown
### RE-L5-002 — Omni mapping continuity

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Omni/OmniMapping.cs
  - tests/CSharpGameEngine.Omni.Tests/ContinuityTests.cs
- Observed behavior:
  - The linear and logarithmic regions meet continuously at the configured threshold.
- Intended behavior:
  - Same as observed.
- Result: Match
```

---

## Conformance model

Maintain `Docs/Conformance.md` as a comparison between:

```text
Intended specification
↕
Implementation
↕
Reverse-engineered observed behavior
↕
Tests and runtime verification
```

Use one of these conformance states:

- `Matches`
- `MatchesWithDocumentedExtension`
- `Partial`
- `Mismatch`
- `Unknown`
- `ObsoleteSpecification`

A feature may be marked `Verified` only when:

1. Its intended behavior is documented.
2. Its implementation evidence is identified.
3. Tests or runtime verification exist.
4. Reverse-engineered behavior has been compared against the intended behavior.
5. No unresolved mismatch remains.

If implementation and specification differ, never silently change both to make them appear consistent. Record the mismatch, determine whether the code or specification is authoritative, then create an ADR or issue-sized next action.

---

## Required behavior on every implementation task

Before editing code:

1. Read this skill file.
2. Read `Docs/Architecture.md`, `Docs/Implementation-Status.md`, and relevant layer/specification files.
3. Inspect the repository to establish the current implementation state.
4. Identify the feature IDs affected by the task.
5. State the intended outcome and the existing implementation evidence.

During implementation:

1. Keep layer boundaries intact.
2. Do not implement unrelated future features.
3. Add or update tests that prove the observable contract.
4. Record architectural decisions that are not obvious from code.
5. Do not mark features as implemented before code exists.

After implementation:

1. Run relevant restore/build/test/runtime checks.
2. Reverse-engineer the resulting implementation from the final code, not from the prompt.
3. Update feature status and evidence paths.
4. Update the intended specification only when the task intentionally changes the contract.
5. Update reverse-engineered behavior.
6. Compare intended and observed behavior.
7. Record mismatches, unresolved questions, and next actions.
8. Update the milestone file.
9. Update CHANGELOG only for user-visible or public API changes.
10. Update README only when setup or usage instructions changed.

Documentation updates are part of the definition of done.

---

## Automatic documentation update procedure

For each task, perform this procedure:

### Phase A — Discover

- Enumerate changed and relevant projects.
- Map each project to L0–L9.
- Locate current docs and feature entries.
- Search for implemented APIs, tests, generated files, and runtime entry points.
- Identify undocumented implemented behavior.
- Identify documented but unimplemented behavior.

### Phase B — Plan

Produce an internal task map containing:

```text
Feature ID
Layer
Current status
Target status
Files to inspect
Files to change
Verification required
Documentation files to update
```

### Phase C — Implement

Implement only the requested scope.

### Phase D — Verify

Run the strongest applicable verification:

- Unit tests
- Integration tests
- Golden/snapshot tests
- GPU validation
- Runtime scenario
- CLI smoke test
- Architecture dependency test

### Phase E — Reverse engineer

Re-read the final implementation and summarize actual behavior independently of the original prompt.

### Phase F — Reconcile

Compare intended and observed behavior:

```text
Intended = Observed     → Matches
Observed is a compatible superset → MatchesWithDocumentedExtension
Only part exists         → Partial
Behavior differs         → Mismatch
Insufficient evidence    → Unknown
```

### Phase G — Update docs

Update only relevant documents. Avoid broad rewrites that create unrelated diffs.

### Phase H — Report

The final completion report must include:

1. Feature IDs changed.
2. Previous and new statuses.
3. Implementation evidence.
4. Verification commands and results.
5. Reverse-engineered observed behavior.
6. Conformance result.
7. Documentation updated.
8. Known deviations and next actions.

---

## Planned capability tree

Maintain the detailed version in `Docs/Roadmap.md`. The following is the minimum architecture inventory that should eventually be represented.

### L0 — Platform / Native Backends

- Win32 window host
- Time and event loop
- Input backend
- Native library loading
- D3D12 raw/binding boundary
- DXGI adapter and swap chain
- D3D12 device, queues, command lists, fences
- GPU resource allocation
- Descriptor management
- Upload/readback
- Native diagnostics and debug layer

### L1 — Core Kernel

- Stable IDs and hashes
- Diagnostics
- Result/error model
- Memory ownership and allocators
- Collections needed by lower layers
- High-precision numerics
- Math primitives
- Deterministic low-level utilities

### L2 — Type Schema / Identity

- Type IDs and schemas
- Component schemas
- Asset identity
- Scene identity
- Logical coordinate types
- Serialization metadata
- Versioning and migration descriptors

### L3 — ECS / Job Runtime

- Entity ID and generation
- World-owned entity location tables
- Archetypes
- Chunks and SoA columns
- Queries
- Systems
- Structural command buffer
- Enable masks and change versions
- Job scheduling
- Debug inspection projection

### L4 — Domain Servers

- Render server
- Render graph execution
- Physics server
- Audio server
- Resource/device services
- Async handle-based APIs

### L5 — Runtime Feature Modules

- Simulation-to-render extraction
- Camera and visibility
- Omni/logarithmic mapping
- High-precision transform hierarchy
- Culling and LOD
- Behavior runtime
- Animation runtime
- Navigation and characters
- Gameplay/UI runtime modules

### L6 — Runtime Artifacts

- Shader artifacts
- Mesh/texture/material artifacts
- Scene artifacts and spawn plans
- Behavior artifacts
- Versioned binary formats
- Dependency and content-hash metadata

### L7 — Static Compiler / Bake

- HLSL compilation
- Asset importing
- Fab import bridge
- Dependency graph
- Incremental artifact compiler
- Scene DSL bake
- Behavior DSL bake
- Source generators and analyzers
- World partition bake
- Omni bounds and LOD bake

### L8 — Visual Studio Authoring Environment

- VSIX shell
- Commands
- Output and Error List integration
- Asset browser
- Inspector
- Scene hierarchy
- Scene view
- Render graph viewer/editor
- Behavior graph viewer/editor
- Debugger visualizers
- Roslyn code rewriting

### L9 — Game / Application

- Game projects
- Static C# scene definitions
- Components and systems
- Materials, behavior, and build definitions
- Game-specific features
- Shipping configuration

---

## Documentation truth hierarchy

When sources disagree, use this order to decide what is currently true:

1. Reproducible runtime behavior and passing tests.
2. Final source code and generated code.
3. Artifact schema actually emitted and consumed.
4. Current specification documents.
5. ADRs describing intended decisions.
6. Roadmap and milestone plans.
7. Prompts, comments, and informal notes.

This hierarchy determines **current observed truth**, not necessarily desired behavior. A mismatch against a higher-level intended contract must still be recorded.

---

## ADR creation rules

Create an ADR when a decision:

- changes dependency direction;
- changes ownership or lifetime;
- changes authoritative source format;
- affects public API or persisted format;
- introduces a new native binding or external dependency;
- changes ECS layout or identity semantics;
- changes Scene, Asset, Behavior, Render Graph, or Omni semantics;
- would be expensive to reverse later.

Do not create ADRs for routine local refactors.

ADR template:

```markdown
# ADR-NNNN — Title

- Status: Proposed | Accepted | Superseded | Rejected
- Date: YYYY-MM-DD

## Context

## Decision

## Alternatives considered

## Consequences

## Verification

## Related features
```

---

## Milestone update rules

Each milestone file must answer:

- What was the goal?
- What was implemented?
- What was deliberately excluded?
- Which feature IDs changed?
- Which commands were run?
- What passed and failed?
- What behavior was reverse-engineered?
- Does observed behavior match intended behavior?
- What remains before the next milestone?

A milestone is not complete if the implementation exists but the final observed behavior has not been documented and reconciled.

---

## Prompt for future implementation tasks

Use this block at the end of every implementation prompt:

```text
Documentation and conformance requirements:

Before implementing, read:
- CSharpGameEngine_Documentation.skill.md or the repository-local equivalent
- Docs/Architecture.md
- Docs/Roadmap.md
- Docs/Implementation-Status.md
- relevant Docs/Layers and Docs/Specifications files
- relevant ADRs and milestone documents

Identify the affected stable feature IDs before changing code.

After implementing and verifying the task:
1. Re-read the final code and reverse-engineer what it actually does.
2. Update the intended specification only when this task intentionally changes the contract.
3. Update Docs/Implementation-Status.md with status, evidence, tests, and next action.
4. Update Docs/Reverse-Engineered-Spec.md with observed behavior and confidence.
5. Update Docs/Conformance.md by comparing intended and observed behavior.
6. Update relevant L0–L9 layer files and subsystem specifications.
7. Create or update an ADR for consequential architecture decisions.
8. Update the current milestone document.
9. Update CHANGELOG.md only for user-visible or public API changes.
10. Update README.md only when build, setup, or usage changed.

Do not mark a feature Verified unless implementation evidence, tests/runtime verification, reverse-engineered behavior, and conformance comparison all exist.
Do not hide mismatches by editing both code and specification to agree without recording the decision.
The completion report must list feature IDs, status transitions, verification commands, observed behavior, conformance results, documentation changes, and unresolved deviations.
```

---

## Bootstrap task for a repository without these documents

When this skill is first introduced into an existing repository:

1. Inspect all projects, project references, source folders, tests, and entry points.
2. Map projects and subsystems to L0–L9.
3. Create `Docs/Architecture.md` describing only confirmed architecture plus explicitly marked planned architecture.
4. Create `Docs/Roadmap.md` with the capability tree and current priorities.
5. Create `Docs/Implementation-Status.md` populated from actual repository evidence.
6. Create `Docs/Reverse-Engineered-Spec.md` from current code and tests.
7. Create `Docs/Conformance.md` comparing intended and observed behavior.
8. Create the current milestone record.
9. Add ADRs only for decisions already evident or explicitly established.
10. Do not claim unimplemented planned features exist.

The bootstrap result must make it possible for a new developer or coding agent to answer:

- What is planned?
- What exists?
- What does it actually do?
- How was that behavior verified?
- Where does it differ from the intended design?
- What should be implemented next?

---

## Definition of done

A task is complete only when all applicable conditions are met:

- Code is implemented.
- Relevant tests or runtime scenarios pass.
- Build warnings and diagnostics are reported honestly.
- Feature ledger is updated.
- Actual behavior is reverse-engineered from final code.
- Intended and observed behavior are compared.
- Relevant specifications and layer docs are updated.
- Architectural decisions are recorded where necessary.
- The milestone record is updated.
- The final report states unresolved mismatches and next actions.

Documentation is not a retrospective optional step. It is part of the implementation transaction.
