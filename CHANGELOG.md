# Changelog

## Unreleased

- Scene ViewにLit/Base Color/Normal/Wireframe表示モードを追加。
- Global Spatial Octreeの要求駆動Named Pipeスナップショットとワイヤーフレームオーバーレイを追加。

## 2026-08-24

- Added the shared `CSharpGameEngine.Runtime.Windows` ECS source module and moved Windows runtime components, stage data, fixed hierarchy resolution, XAudio2 facade, and runtime systems out of UserEcsProject.
- Migrated Scene View light bindings to the Runtime.Windows namespace; retained legacy light names only for analyzer diagnostics.