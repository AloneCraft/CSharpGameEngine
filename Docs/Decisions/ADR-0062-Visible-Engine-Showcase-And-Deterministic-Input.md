# ADR-0062: Visible Engine Showcase and deterministic Input Actions

- Status: Accepted
- Date: 2026-08-30
- Milestone: [M0064](../Milestones/M0064-Visible-Engine-Showcase.md)

## Context

Implemented engine behavior was mainly observable through tests, feature ledgers, and separate samples. The default `UserEcsProject` still opened the imported FirstPerson scene and used native polling APIs, so fixed-tick ECS, physics, Shader Artifacts, and catalog progress were not visible through one stable runtime surface.

## Decision

The default sample is a Windows x64 Engine Showcase. One fixed camera observes an ECS motion lane, a deterministic rigid-body stack and CCD projectile, and a physically lit primitive gallery. A non-interactive 360 DIP Win32 child dashboard occupies the right side of the same window. The old FirstPerson scene and incomplete prefracture/cache samples remain source samples but are not Startup Scenes.

Portable input values live in `CSharpGameEngine.Core`. The Windows assembly implements `CSharpGameEngine.Input.InputServer` and the Win32 event adapter. `Win32Window` exposes timestamped `DrainInputEvents()` and no longer exposes raw key polling or mouse-delta consumption. `AppHostRuntime` owns the Input Server and finalizes one action frame during FrameInput.

The Showcase Action context owns fixed action GUIDs for pages 1-4, Pause, Step, panel visibility, layer selection, and progress paging. Manual activity defers automatic page rotation for 600 running fixed ticks. Pause does not advance the timer.

Feature progress is generated from `FeatureStageCatalog` at build time as canonical `csharp-game-engine-showcase-manifest-v1`. The dashboard reports Implemented Feature and Executable Stage ratios separately. No synthetic aggregate completion percentage is permitted.

Visual evidence is produced only by `CSharpGameEngine.Showcase.Automation`. It owns the launched PID, selects WARP through environment contracts, drives the fixed Action path, validates the machine-readable child title, requests a same-process dashboard DIB, composes a screenshot, and requests normal shutdown. Exit code 2 means infrastructure failure and is never a feature pass.

## Consequences

- Engine progress is visible without reading test output.
- Raw Win32 polling is removed from the public Window API.
- The Feature Catalog becomes a build input of the default sample.
- Debug and Release can select WARP without changing normal hardware/default startup behavior.
- Dashboard rendering remains outside the GPU Shader ABI and does not add wall-clock values to Systems.
- Incomplete physics demonstrations stay accurately represented as Partial/Deferred progress rather than visible promises.
