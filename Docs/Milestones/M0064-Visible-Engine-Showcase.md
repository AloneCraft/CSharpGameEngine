# M0064 — Visible Engine Showcase and deterministic Input vertical slice

## Outcome

`UserEcsProject` now starts a visible engine gallery with a right-side localized dashboard. The runtime exposes ECS fixed-tick motion, the implemented rigid-body stack/CCD path, physical lights and shadows, HDR/bloom/ACES, Shader Artifact provenance, entity statistics, and live Feature/Stage progress in one window.

The previous FirstPerson Stage remains an explicit non-Startup sample. Prefracture and cache playback remain outside the default gallery because their Features are not complete.

## Implemented contracts

- Core input: monotonic timestamps; stable window/device/context/action IDs; typed controls, values, raw events, action values/phases, and release reasons; deterministic `StableId.FromName`.
- Win32 adapter: QPC timestamps, keyboard, mouse buttons/wheel, raw relative mouse, device notifications, repeat suppression, and forced releases for focus/capture/device/window loss.
- Input Server: deterministic event sorting, context priority, chords, 1D/2D dead zones, reserved actions, collision-checked rebind, and context/device cancellation.
- Runtime: `AppHostRuntime.Input`, FrameInput event draining/action finalization, `World.GetStatistics()`, and `CGE_GRAPHICS_ADAPTER`/`CGE_D3D12_DEBUG_LAYER` resolution.
- Showcase: fixed Action GUIDs, four pages, Pause/Resume, one-tick Step, 600-running-tick auto tour, localized 360 DIP GDI child panel, machine-readable title, and same-process DIB evidence.
- Manifest: deterministic `export-showcase`, semantic summary plus complete Feature/Stage rows, catalog schema, and manifest SHA-256; incremental UserEcs build publication.
- Automation: Debug/Release WARP execution, owned PID, four-page/Pause/Step/paging/visibility Oracles, pixel-diverse dashboard capture, combined screenshot, evidence JSON, activity log, and normal shutdown.

## Feature result

| Feature | Result | Stage |
|---|---|---|
| F-L1-008 | Implemented / TestVerified / Matches | `platform.boundaries` |
| F-L0-010 | Implemented / TestVerified / Matches | `platform.window-input` |
| F-L4-010 | Implemented / TestVerified / Matches | `input.actions` |
| F-L9-009 | Implemented / TestVerified / Matches | `showcase.visible-progress` |

The generated dashboard manifest after this milestone reports 150 Features with 58 Implemented and 113 Stages with 25 Executable. These are separate ratios and change deterministically with the catalog.

## Verification

- Focused Core, Entities, Windows, Feature Runner, manifest, dashboard DIB, and automation argument/exit-code tests pass.
- `platform.boundaries`, `platform.window-input`, `input.actions`, and `showcase.visible-progress` pass in Debug and Release with WARP.
- Runtime evidence proves four pages, Pause stability, one-tick Step, progress layer/paging, panel hide/show, pixel-diverse Japanese and English dashboard output, D3D12 Debug Layer startup, owned-process normal shutdown, and combined screenshot publication.
- Full Solution runs pass 1,680 Debug tests and 1,679 Release tests sequentially. The dedicated solution-registration test passes 3/3.
- Two independent Release manifest exports are byte-identical at SHA-256 `1C1C80B1D2504B994AF325437D4CABE4FD63F260C67652D19E490FA21E7F29AD`.

Evidence and screenshots are generated under `.artifacts/Showcase` and are not source-controlled.
