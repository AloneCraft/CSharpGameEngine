# M0066 — Shared Preview Host foundation

- Status: Completed
- Date: 2026-09-01

## Outcome

F-L8-018 is Implemented/TestVerified. `RenderPreviewHostContracts` fixes the versioned request, scene, frame, diagnostic, capture, session, and monotonic-sequence contracts. The Windows x64 Editor Preview Host owns common `RenderServer` sessions behind named pipes, and both Asset Preview and Scene View are clients rather than native renderer owners.

The common server preserves Scene View selection tint, Lit/BaseColor/Normal/Wireframe modes, bounded alpha overlays, up to three camera previews, duplicate preview rosters, and device/fence/camera diagnostics. Host-process recovery has an explicit two-generation invariant: a new process renders to a newly created child surface HWND. `RenderSurfaceHostWindow` keeps the HWND returned to WPF stable, destroys the failed generation's DXGI child, creates a replacement at the last positive size, and only then replays source-derived state. The public protocol admits a canonical zero-object scene; a host-private degenerate sentinel satisfies the private Stage backend without becoming public state.

The VSIX packages the exact same-configuration x64 framework-dependent Preview Host closure and offline Stage shader artifact. `SceneRenderer`, `VorticeRenderer`, `NativeEvent`, and the VSIX Vortice/SharpGen dependency chain are removed. `vsix.rendering-preview` is executable and runs the C# VSIX reference Scene, protocol/package/client/lifecycle contracts, and an extracted-package WARP apphost oracle. The manual Experimental Instance Scene Designer entry remains Pending, so this milestone does not claim `RuntimeVerified`.

## Verification

- Debug and Release `RenderPreviewHostProtocolTests`
- Debug and Release `RenderPreviewHostClientTests`
- Debug and Release `RenderPreviewSceneBuilderTests`
- Debug and Release `RenderSurfaceHostWindowTests`
- Debug and Release real WARP `RenderPreviewRenderSessionTests`
- Separate-process host crash/new-surface replay tests
- Debug and Release VSIX full suites: 1,017 passed, 0 failed
- Debug and Release VSIX package builds: 0 warnings, 0 errors
- Debug and Release focused common Render Server/Preview Host suites: 13 passed, 0 failed
- Package-extracted `PreviewHost.exe` WARP capture with nonzero device generation and completed fence
- Executable `vsix.rendering-preview` Feature Stage

## Remaining work outside F-L8-018

- F-L8-023 collects Experimental Instance transform/WARP/rebase/native-Undo visual evidence.
- F-L7-023 adds five-warm-up/thirty-sample rendering performance evidence.
- F-L7-024 consumes both dependencies in the rendering Release gate.
