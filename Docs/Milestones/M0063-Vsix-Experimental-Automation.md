# M0063 — VSIX Experimental evidence and Shader Graph UI automation

## Outcome

The repository now has a versioned Experimental ledger, a net472 `CSharpGameEngine.Vsix.Automation` CLI, and executable `vsix.experimental-smoke` and `vsix.experimental-automation` stages. The runner discovers only Visual Studio installations with `devenv.exe`, installs the current VSIX into `CgeAutomation`, proves package/build/installed DLL SHA-256 agreement, and operates only the launched PID.

The Shader Graph editor exposes stable AutomationIds and node TransformPattern movement. Subgraph save validates without DXC or artifact publication, while root build rejects Subgraph input with an actionable diagnostic. External-change handling coalesces watcher bursts and retries temporary sharing and incomplete-JSON reads.

## Experimental evidence

Debug and Release 1041 runs passed all nineteen assertions on Visual Studio 18.9.12120.119. Both captured ActivityLog and 3096x1254 screenshots, matched package/build/installed DLL hashes, and exited normally without forced termination. The pre-existing normal Visual Studio PID 41320 and `CgeFixed` PID 25828 were unchanged before and after both runs. 1033 was not installed, so existing package/localization tests remain its fallback.

Evidence is intentionally local:

- Debug: `.artifacts/VsixExperimental/20260829-143615-be64f019/1041/evidence.json` (`cb2e859a9d04bb6ed60a0522f55c05181c6cf79f62fce6f45d2efcdce6d41a2f`)
- Release: `.artifacts/VsixExperimental/20260829-143750-0d9849df/1041/evidence.json` (`b317cb6aa336eb35f4840b023fece706fb86d638d533be21c578903439c62b7c`)

## Verification

- VSIX Debug and Release suites: 992 passed per configuration.
- Automation contract tests cover arguments/root suffix, candidate selection, process ownership, ledger false-pass, and evidence hash.
- Shader Graph CLI proves Subgraph validate succeeds and root build rejects Subgraph input.
- Feature Runner maps configured exit code 2 to InfrastructureError.
- Debug and Release CgeAutomation UI scenarios: 19/19 assertions Passed, `forcedTermination=false`.
- `vsix.experimental-smoke`, `vsix.experimental-automation`, and `vsix.shader-graph` passed sequentially in Debug and Release.
- Full solution tests passed sequentially with one MSBuild node: 1,655 Debug and 1,654 Release.

## Conformance

F-L8-020 and F-L8-021 are Implemented and TestVerified. F-L8-031 Matches its declared Shader Graph editor scope. Legacy Scene Designer, DPI, Escape, Physics sidebar, startup, render, and audio manual checks remain accurately Pending in the ledger.
