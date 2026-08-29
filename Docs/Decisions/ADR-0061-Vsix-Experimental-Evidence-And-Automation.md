# ADR-0061 — VSIX Experimental evidence and isolated automation

## Status

Accepted on 2026-08-29.

## Decision

Visual Studio integration evidence uses a dedicated `CgeAutomation` root suffix. Automation rejects every other suffix, records pre-existing `devenv.exe` processes, and owns only the PID it launches. Normal Visual Studio and `CgeFixed` are never installation, UI Automation, close, or kill targets. Normal close is required; force termination is a failed oracle and is limited to the owned PID after a grace period.

`CSharpGameEngine.Vsix.Automation` is the net472 command-line boundary. Exit codes are `0` success, `1` oracle mismatch, and `2` infrastructure error. Feature Runner commands declare `2` as an InfrastructureError code. Interactive desktop, Visual Studio discovery, installation, package provenance, and UI Automation availability are infrastructure preconditions rather than feature assertions.

Experimental baseline records use `Docs/Verification/VSIX-Experimental-Baseline.json`. Pending is an explicit non-pass state. Passed records require an owner, observation and expiry times, and an existing evidence file. Run evidence, scratch Graphs, ActivityLog, and screenshots remain under `.artifacts/VsixExperimental` and are not source assets.

Shader Graph controls expose stable `Cge.ShaderGraph.*` AutomationIds. Node layout uses the standard UI Automation Transform pattern. The scenario proves Editor Factory load, layout dirty state, Undo/Redo, canonical save, semantic-hash stability, clean reload, dirty Reload/Keep options, diagnostics and repair, DLL hash/culture identity, screenshot capture, and normal shutdown.

## Consequences

- `vsix.experimental-smoke` validates the ledger without converting Pending into Passed.
- `vsix.experimental-automation` runs only in the `vsix` profile.
- A stale package, missing interactive desktop, missing 1041 installation, or pre-existing `CgeAutomation` process returns InfrastructureError.
- External Graph watcher events are trailing-edge debounced and retry temporary sharing/JSON parse failures.
- 1033 runs only when the language pack is installed; package/localization tests remain the 1033 fallback evidence.

## References

ADR-0054; ADR-0058; M0063; F-L8-020; F-L8-021; F-L8-031.
