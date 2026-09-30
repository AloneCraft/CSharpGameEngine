# Audio, Input, and Platform specification

- Status: Implemented for portable input, Win32 event adaptation, deterministic Input Actions, audio device lifecycle, offline mixing, listener-relative spatial audio, artifact-backed voice recovery, and read-only Platform/Input/Audio diagnostics
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0056](../Decisions/ADR-0056-Platform-Input-And-Audio-Server-Boundaries.md)
- Target layers: L0, L1, L4, L8

## Scope and observed baseline

Windows/Win32 input, XAudio2 abstractions, and the current Windows runtime are baseline evidence. M0064 implements the portable value contracts, QPC-stamped Win32 event adaptation, and deterministic action processing. The EngineHost-owned audio device server implements native creation, operational silent fallback, explicit recreation, and disposal. Its portable mixer owns immutable PCM clips and decoded stream artifacts, buses, voice cursors, spatial gain/pan, looping, limits, typed underrun policy, and typed recovery decisions. Protocol v5/debug snapshot v2 copies bounded Platform/Input/Audio diagnostics into the existing Preview Host request without transferring service or native ownership.

Windows is the first backend: Win32 and XAudio2 live in L0. The corresponding L1/L4 contracts are platform-neutral so another backend can be added without pulling native types above L0.

## L1 value contracts

```csharp
public readonly record struct MonotonicTimestamp(long Ticks);
public readonly record struct WindowId(StableId Value);
public readonly record struct InputDeviceId(StableId Value);
public readonly record struct InputActionId(StableId Value);
public readonly record struct AudioBusId(StableId Value);
public readonly record struct AudioVoiceId(StableId Value);
public sealed record InputEvent;
public sealed record InputActionValue;
```

The contracts carry values and explicit ownership only; they expose neither `HWND`, Win32 message types, COM pointers, XAudio2 interfaces, nor codec provider types. `MonotonicTimestamp` is sourced from QPC on Windows and is converted into a process-monotonic timeline with a documented frequency/provenance.

## Server data flow

```text
Win32 message / device notification -> L0 event adapter -> timestamped L1 InputEvent
  -> L4 Input Server -> contexts/chords/dead zones/rebinds -> deterministic InputAction sequence

asset stream artifact -> L4 Audio Server -> buses/mixer/voice allocator -> L0 XAudio2 adapter
  -> hardware output or mandatory silent/offline PCM reference backend
```

Focus loss, pointer capture release, window destruction, and hotplug force release actions for every held key/button/axis state. The action server processes events in monotonic timestamp plus stable device/sequence order, so the same raw trace produces the same action sequence.

## Input contract

Action maps declare context priority, chord predicates, normalization, dead zones, and rebind policy. Rebind validates collision and reserved actions before it becomes active. A context transition emits releases for controls no longer valid; it cannot leave stale pressed state in gameplay. Diagnostics include timestamp, window/device/action IDs, context, raw value, transformed value, and release reason.
### Value reads and modifiers (ADR-0067)

Gameplay reads an action's current value with `InputServer.ReadValue`. The highest-priority active context that binds the action answers, and the value is the sum of all its bindings. This is how Unreal's Enhanced Input combines W/A/S/D into one 2D move.

A binding transforms its raw value in a fixed order. The dead zone and scale apply first. The ordered `InputModifier` list follows: `InputNegateModifier` per axis, and `InputSwizzleModifier` for Unreal's YXZ swizzle.

Relative controls are `RelativeAxis1D` and `RelativeAxis2D`: raw mouse movement and the wheel. They hold no state. The events of one `ProcessEvents` call are accumulated, and the sum is readable until the next call.

Keyboard, mouse, and gamepad controls use Unreal key names (for example `SpaceBar`, `Gamepad_Left2D`, and `Gamepad_FaceButton_Bottom`), so imported mappings bind without translation. Raw mouse movement is `Delta`, which corresponds to Unreal's `Mouse2D`.

XInput pad 0 is polled once per `DrainInputEvents()`. It emits button, stick, and trigger changes, plus Connected and Disconnected events; a disconnect releases every held control.

Mouse-look capture hides and clips the cursor only while the window owns focus. `ClipCursor` is system-wide, so a background or hidden window applies the capture when it receives focus.

Automation input (`CGE_AUTOMATION_INPUT`, e.g. `W@20-40;SpaceBar@50-51`) presses keys by presented frame through the same adapter path as the window procedure.

## Audio contract and recovery

The Audio Server owns mixer graph, buses, voice limits, gain/pan/spatial parameters, loop points, decoded stream artifacts, and source lifetime. The silent/offline backend is required and renders deterministic PCM buffers for test oracles. Hardware/XAudio2 is optional additional evidence and cannot be a required headless test result. Spatialization accepts only a listener-local `Vector3` produced after the Q64.64 reference-frame transform; it derives linear range attenuation and stereo pan and never creates a global float coordinate.

Runtime codec dependency is avoided by converting source into stream artifacts in L7. An optional codec or provider is behind an L4 adapter and cannot leak types into artifacts/public APIs. An underrun increments a typed counter and follows an explicit silence/stop policy.

Audio device state is `Unavailable`, `Creating`, `Ready`, `Recovering`, `Faulted`, `Disposed`; backend mode is independently `None`, `XAudio2`, or `Silent`. On device loss the server stops submission, releases the old native generation exactly once, and recreates the backend. A successful hardware or silent generation restores only `RestoreFromArtifact` voices at the mixer-owned logical frame; all other voices emit `StoppedNotEligible`. A failed recreation without fallback leaves logical voice state unchanged for an explicit retry. Restoration uses `AssetArtifactReference` and never a live native pointer.

## Immediate migration and failure policy

When a target feature lands, it replaces the affected native facade, input abstraction, codec reader, or audio path in the same change. It removes the old public API and old artifact reader and regenerates test artifacts. Failure reports identify server state, device/window/action/voice IDs, timestamp, native HRESULT/provider identity where applicable, and recovery decision.

## Performance and compatibility

Headless contract tests use the silent backend and WARP where graphics diagnostics are involved. Windows x64 Debug/Release are mandatory; actual audio hardware, GPU hardware, and Experimental Instance are supplemental evidence. Performance stores five warm-ups plus thirty samples, median/p95/MAD, backend mode, buffer configuration, and machine fingerprint.

One explicitly requested diagnostic capture records the process monotonic counter and frequency, OS/runtime/process architecture, process ID, Input server state and at most 64 current action values, plus Audio device/backend generation, mixer counts, recovery counts, failure, and typed availability. Each subsystem copies its own current truth. The isolated Render Preview Host does not own runtime Input or Audio and must report `NotOwned` with zero owned counters. The existing Scene Designer Runtime Diagnostics view may project this envelope but cannot poll, retain a service, expose a native handle, synthesize elapsed timing, or turn `Unavailable`/`Faulted` into success. Host and client both validate bounds; malformed or generation/fence-inconsistent data fails closed.

## Stage oracle matrix

| Feature | Level | Stage | Mode | Timeout | Oracle |
|---|---:|---|---|---:|---|
| F-L1-008 | 1 | `platform.boundaries` | Headless / Foundation | 900s | Portable values contain no native types. |
| F-L0-010 | 2 | `platform.window-input` | Headless / Foundation | 900s | QPC monotonicity and forced releases. |
| F-L4-009 | 2 | `audio.lifecycle` | Headless / Foundation | 900s | Device/server ownership transitions. |
| F-L4-010 | 3 | `input.actions` | Headless / Foundation | 900s | Deterministic actions for contexts/chords/rebind. |
| F-L4-011 | 3 | `audio.mixer` | Headless / Foundation | 900s | Offline PCM mixer/voice/underrun oracle. |
| F-L4-012 | 4 | `audio.spatial` | Headless / Foundation | 900s | Spatial/stream/device recovery policy. |
| F-L8-030 | 5 | `vsix.platform-debug` | Headless / VSIX | 1800s | Read-only diagnostics snapshots. |
| F-L7-034 | 6 | `platform.compatibility` | Headless / Foundation | 3600s | Required Windows cells separate optional hardware. |
| F-L7-035 | 7 | `platform.release` | Headless / Foundation | 7200s | Scoped platform provenance and closure. |

All rows are Implemented/TestVerified with executable stages. `Platform-Compatibility-Matrix.json` references the required Debug/Release solution, Windows-TFM, and WARP CI cells and the executable prerequisite Stage closure; hardware Audio/GPU are optional Manual Evidence references and never substitute for required correctness. `platform.release` re-executes that prerequisite and binds its recent configuration/Git-matched result, the exact Platform source/verification closure, required adapter modes, optional hardware snapshot, and machine/timestamp provenance into one canonical SHA report. Product packaging, signing, SBOM, and the immutable product release manifest remain owned by F-L7-045/F-L7-048.

## M0064 deterministic input vertical slice

`Win32Window.DrainInputEvents()` is the only public raw-input boundary. Keyboard, mouse buttons/wheel, raw relative mouse, focus/capture loss, device removal, and destruction become ordered `InputEvent` values; the removed polling APIs are not retained. `AppHostRuntime` owns one `InputServer`, and the FrameInput phase finalizes exactly one action frame before simulation.

Showcase actions use stable IDs and a dedicated context. Context priority, chords, one/two-dimensional dead zones, reserved actions, rebind collision checks, forced cancellation/release, and canonical action hashing are covered by Debug/Release contract stages.
