# ADR-0061 — VSIX Experimental evidence and isolated automation

## Status

Accepted on 2026-08-29.

## Decision

Visual Studio integration evidence uses a dedicated `CgeAutomation` root suffix. Automation rejects every other suffix, records pre-existing `devenv.exe` processes, and owns only the PID it launches. Normal Visual Studio and `CgeFixed` are never installation, UI Automation, close, or kill targets. Normal close is required; force termination is a failed oracle and is limited to the owned PID after a grace period.

`CSharpGameEngine.Vsix.Automation` is the net472 command-line boundary. Exit codes are `0` success, `1` oracle mismatch, and `2` infrastructure error. Feature Runner commands declare `2` as an InfrastructureError code. Interactive desktop, Visual Studio discovery, installation, package provenance, and UI Automation availability are infrastructure preconditions rather than feature assertions.

Experimental baseline records use `Docs/Verification/VSIX-Experimental-Baseline.json`. Pending is an explicit non-pass state. Passed records require an owner, observation and expiry times, and an existing evidence file. Run evidence, scratch Graphs, ActivityLog, and screenshots remain under `.artifacts/VsixExperimental` and are not source assets.

An elapsed expiry blocks release validation but not an ordinary build. The window answers "has the code drifted from what was observed", but nothing measures that drift: the record already carries the observed DLL SHA-256, and `CSharpGameEngine.VSExtension` deliberately stamps a fresh build timestamp into every assembly so an older VSIX cannot be reused silently, which makes that hash change on every rebuild and unusable as the signal. The elapsed-days window is therefore an approximation in both directions — evidence lapses after a quiet week with no source change, and survives a same-day rewrite. That is sound as a release gate and too weak to justify halting unrelated work, so freshness is required only under `--require-passing-evidence`. `Docs/Verification/Manual-Evidence.json` follows the same rule.

Shader Graph controls expose stable `Cge.ShaderGraph.*` AutomationIds. Node layout uses the standard UI Automation Transform pattern. The scenario proves Editor Factory load, layout dirty state, Undo/Redo, canonical save, semantic-hash stability, clean reload, dirty Reload/Keep options, diagnostics and repair, DLL hash/culture identity, screenshot capture, and normal shutdown.

## Consequences

- `vsix.experimental-smoke` validates the ledger without converting Pending into Passed.
- Expired Passed evidence is reported by `verify-vsix-compatibility --require-passing-evidence` and is silent in an ordinary build, so a lapsed observation is discovered at the release gate rather than by a broken build in unrelated work.
- `vsix.experimental-automation` runs only in the `vsix` profile.
- A stale package, missing interactive desktop, missing 1041 installation, or pre-existing `CgeAutomation` process returns InfrastructureError.
- External Graph watcher events are trailing-edge debounced and retry temporary sharing/JSON parse failures.
- 1033 runs only when the language pack is installed; package/localization tests remain the 1033 fallback evidence.

## References

ADR-0054; ADR-0058; M0063; F-L8-020; F-L8-021; F-L8-031.
