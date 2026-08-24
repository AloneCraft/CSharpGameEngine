# ADR-0048: Shared Windows ECS Runtime

- Status: Accepted
- Date: 2026-08-24

## Decision

Windows runtime components and systems are maintained as shared source in `src/CSharpGameEngine.Runtime.Windows` and imported through `CSharpGameEngine.Runtime.Windows.projitems`. The source is compiled together with each consuming ECS application so the selected Logical128/192/256 precision and generated World/State partials remain one compilation.

The module owns AppHost, Level, Camera-facing renderer systems, lights, audio facade (XAudio2), stage roster construction, and fixed-precision hierarchy resolution. Runtime companion components are implementation types and are not authored by game scenes. `PlayerStart` remains an optional game marker with no camera fallback.

A World must contain exactly one active Camera and at most one AppHost/Level runtime pair. Ambient light, fog, lights, and audio are optional. Runtime StaticMesh assets remain GLB/glTF; FBX conversion is an import-time responsibility.

## Consequences

- UserEcsProject owns scenes, gameplay components, assets, and Logical precision selection only.
- Runtime and editor preview consumers share the same source and generated system surface; no compatibility wrappers for the retired UserEcsProject runtime types are provided.
- Source-only shared compilation is intentional. Consumers that need the concrete authored component contract must compile the shared source with their selected precision.

## Verification

- UserEcsProject Debug build succeeds after importing the shared projitems.
- Physical light contract tests use `CSharpGameEngine.Runtime.Windows`.
- `CSharpGameEngine.slnx` registers both the `.shproj` and `.projitems`.