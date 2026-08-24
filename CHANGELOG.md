# Changelog

## Unreleased

- Added the autonomous Feature Runner with stable Feature/Stage catalogs, changed/smoke/full/stress/VSIX suites, WARP-first visual validation, resumable versioned JSON results, and explicit `NotImplemented` outcomes for deferred features.
- Assigned machine-verifiable stages to the active 48 features and deterministic Oracle designs to 28 deferred Simulation/Physics features; retained the current 64-component Source Generator limit without adding runtime mask cost.
- Added 10 isolated C# `IScene` Stage Pack executables and made solution registration checks ignore transient WPF `*_wpftmp.csproj` build files.
- Added the repository rule that inferred local declarations use `var`.

- Localized the complete VSIX-owned UI for English and Japanese, with English neutral fallback, Visual Studio UI-culture selection, localized menus/installer/templates, and culture-independent debug protocol tokens.
- Bumped the VSIX deployment version to 0.1.7 and added locale/resource payload tests so stale or incomplete localized packages fail verification.

- Added shared deterministic Simulation and Physics source runtimes, system-major fixed phases, Unity-style Collider/RigidBody APIs, Q32.32 local physics, a deterministic physics broadphase, cache/cooking identities, and prefracture contracts.
- Added demand-driven Runtime/Editor Preview Physics debug snapshots and Scene View AABB/velocity/sleep/center-of-mass overlays with Simulate/Pause/Step/Reset.
- Recorded advanced rigid body, fluid, destruction, celestial, and quantum domains as Deferred; incomplete v1 solver/Bake paths remain explicitly PartiallyImplemented.

- Scene ViewにLit/Base Color/Normal/Wireframe表示モードを追加。
- Global Spatial Octreeの要求駆動Named Pipeスナップショットとワイヤーフレームオーバーレイを追加。

## 2026-08-24

- Added the shared `CSharpGameEngine.Runtime.Windows` ECS source module and moved Windows runtime components, stage data, fixed hierarchy resolution, XAudio2 facade, and runtime systems out of UserEcsProject.
- Migrated Scene View light bindings to the Runtime.Windows namespace; retained legacy light names only for analyzer diagnostics.
- Scene View Physics controls now live in a persisted right sidebar with a chevron tab and `N` shortcut.
