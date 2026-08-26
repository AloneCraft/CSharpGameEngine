# Audio, Input, and Platform specification

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0056](../Decisions/ADR-0056-Platform-Input-And-Audio-Server-Boundaries.md)
- Target layers: L0, L1, L4, L8

## Scope and observed baseline

Windows/Win32 input, XAudio2 abstractions, and the current Windows runtime are baseline evidence. This target specification defines portable value contracts, deterministic action processing, server ownership, silent/offline audio, stream artifacts, and device recovery. It does not claim that all target contracts are implemented.

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

## Audio contract and recovery

The Audio Server owns mixer graph, buses, voice limits, gain/pan/spatial parameters, loop points, stream buffering, and source lifetime. The silent/offline backend is required and renders deterministic PCM buffers for test oracles. Hardware/XAudio2 is optional additional evidence and cannot be a required headless test result.

Runtime codec dependency is avoided by converting source into stream artifacts in L7. An optional codec or provider is behind an L4 adapter and cannot leak types into artifacts/public APIs. An underrun increments a typed counter and follows an explicit silence/stop policy.

Audio device state is `Unavailable`, `Creating`, `Ready`, `Recovering`, `Faulted`, `Disposed`. On device loss the server stops submission, records voice restoration eligibility, recreates the backend, then either restores artifact-backed loop/stream state at a defined logical frame or emits a stop event. Restoration never relies on a live native pointer.

## Immediate migration and failure policy

When a target feature lands, it replaces the affected native facade, input abstraction, codec reader, or audio path in the same change. It removes the old public API and old artifact reader and regenerates test artifacts. Failure reports identify server state, device/window/action/voice IDs, timestamp, native HRESULT/provider identity where applicable, and recovery decision.

## Performance and compatibility

Headless contract tests use the silent backend and WARP where graphics diagnostics are involved. Windows x64 Debug/Release are mandatory; actual audio hardware, GPU hardware, and Experimental Instance are supplemental evidence. Performance stores five warm-ups plus thirty samples, median/p95/MAD, backend mode, buffer configuration, and machine fingerprint.

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

All rows are `Specified` and registered as `NotImplemented` until their concrete server implementations exist.
