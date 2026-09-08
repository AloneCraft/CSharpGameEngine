# M0077 — Platform Diagnostics Viewer

## Outcome

F-L8-030 is implemented by extending the existing Render Preview Host snapshot boundary to protocol v5/debug snapshot v2. One explicit Scene Designer refresh now presents a bounded read-only copy of process identity, current Input actions, and Audio lifecycle/mixer/recovery state together with the previously implemented Render diagnostics.

## Source of truth and ownership

- `InputServer` remains the sole owner of active contexts, held controls, active actions, and the current action frame. `CaptureDebugSnapshot` copies at most the latest 64 current-frame actions and never returns the mutable source collections.
- `AudioDeviceServer` and its `AudioMixer` remain the sole device/backend generation, voice, bus, and recovery truth. `CaptureDebugSnapshot` copies their typed lifecycle and counters without a native handle.
- `EngineHost` composes snapshots from services it owns. An uninitialized lazy Audio device is reported as `Unavailable`; it is not initialized as a side effect of inspection.
- The isolated Render Preview Host owns only its Render Server. Its Input and Audio entries are typed `NotOwned` with zero owned counters instead of synthetic activity.
- The net472 VSIX owns only a validated presentation of one response. It does not poll, cache runtime truth, acquire a service, or issue a control command.

The contract contains only integers, bounded strings, enums, and presentation-relative `float` action values. It contains no native pointer, provider object, `double`, `BigInteger`, or new authoritative simulation value.

## Failure and lifecycle contract

The Contracts validator rejects invalid monotonic metadata, unbounded strings/actions, unknown enum values, non-finite Input values, impossible owned/not-owned combinations, and invalid Audio generation/mode state. The Preview Host validates the produced snapshot and its device generation/fence against the live session before writing it to the pipe. The net472 client validates it again against the correlated response envelope.

Snapshot capture does not render or change frame, fence, device generation, action state, voice state, or ownership. A capture exception or invalid producer result consumes the request sequence and returns `PREVIEW_SNAPSHOT_FAILED` without disposing the healthy session. Malformed success received by the VSIX remains a terminal protocol fault requiring the existing fresh-session recovery path.

## Adversarial review

The first implementation used enum ordinal conversion between Input/Audio and wire states. Review replaced that hidden coupling with explicit exhaustive mappings and made `NotOwned` reject truncation/failure state. It also found that only the concrete Render session and the client validated snapshots; the common Host session table could serialize an invalid alternate `IRenderPreviewSession` result. Host-side schema and generation/fence validation plus a non-terminal malformed-producer regression test now close that boundary.

Overengineering review retained the existing `Snapshot` command, Preview Host, Scene Designer button, Runtime Diagnostics list, and localization resources. No Platform Manager, debug service, second ToolWindow, timer, event bus, runtime attachment, compatibility reader, or duplicate cache was added.

## Verification

- Contracts: 18/18 Debug and 18/18 Release.
- Windows/WARP: 239/239 Debug and 238/238 Release.
- VSIX/net472: 974/974 Debug and 974/974 Release.
- FeatureRunner: 82/82 Debug and 82/82 Release.
- `vsix.platform-debug` Debug: `20260901T133114497Z-single-684cc2d1777640178c167212ef1cc073`.
- `vsix.platform-debug` Release: `20260901T133210571Z-single-58ede3177a02485e8ae30ad2db5f24fe`.
- Feature coverage, manual evidence, golden policy, CI matrix, VSIX compatibility, and failure/quarantine validation pass in both configurations.

The known VSIX test-asset copy lock retried once and then completed; it did not change the test verdict.
