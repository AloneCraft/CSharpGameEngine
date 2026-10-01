# ADR-0056 — Platform, Input, and Audio Server Boundaries

- Status: Accepted
- Date: 2026-08-25

## Context

Windows/Win32/XAudio2 is the first vertical slice, while input timing, action semantics, audio mixing, and recovery must remain portable and testable without hardware.

## Decision

Put Win32 and XAudio2 bindings in L0; define portable L1 timestamp/window/device/action/audio value contracts; place Platform, Input, and Audio servers in L4; use QPC-derived monotonic timestamps; and require a silent/offline PCM reference backend.

## Consequences

- Focus/capture/hotplug force action release to prevent stuck input state.
- Action maps are deterministic for contexts, chords, dead zones, and rebinds.
- Runtime codecs are avoided through L7 stream artifacts; optional providers are L4 adapters.
- Device recreation restores only artifact-backed eligible voice state and keeps hardware out of mandatory headless verdicts.

## Migration and verification

Target features remove superseded facades/readers in the same change. `platform.*`, `input.actions`, and `audio.*` Oracles in [Audio-Input-Platform](../Specifications/Audio-Input-Platform.md) provide proof.

## Related features

F-L1-008, F-L0-010, F-L4-009 through F-L4-012, F-L8-030 (retired in M0097), F-L7-034, F-L7-035.
