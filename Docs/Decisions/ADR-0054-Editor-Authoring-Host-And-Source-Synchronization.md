# ADR-0054 — Editor Authoring Host and Source Synchronization

- Status: Accepted
- Date: 2026-08-25

## Context

The Scene Designer already has mature C# authoring and edit behavior. Future import, bake, preview, and debugging must not create a second authoring truth or embed backend lifetime inside Visual Studio.

## Decision

Keep C# `IScene` as the only source of truth; use a predicted model only during interaction; map one committed edit to one native Undo transaction; always converge to a Roslyn reparse; and route Preview/Import/Bake through a versioned out-of-process host protocol.

## Consequences

- Scene Designer is a multi-view document over one snapshot/session identity.
- JSON compatibility readers and VSIX-owned rendering are not supported after their replacement features land.
- Invalid typed edits leave source unchanged.
- Protocol cancellation, timeout, crash, and version mismatch are terminal/visible states.

## Migration and verification

Existing transform behavior is baseline evidence; `vsix.transform-tools` adds visual acceptance. Host/source/UI behavior is gated by the `vsix.*` Oracles in [Editor-VSIX](../Specifications/Editor-VSIX.md).

## Related features

F-L8-020 through F-L8-029 and F-L8-018/F-L8-019.
