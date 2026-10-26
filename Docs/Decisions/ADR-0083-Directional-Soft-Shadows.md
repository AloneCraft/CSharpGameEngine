# ADR-0083 — Matched directional soft shadows

- Status: Accepted (user request, 2026-10-26)
- Feature: F-L4-020

## Decision
Add opt-in directional PCSS shared by cascaded and virtual sampling. The sun's authored angular diameter is in degrees; penumbra radius in metres is blocker-to-receiver light-space separation times tan(half the diameter). Both backends use the same deterministic disk samples, linear depth, receiver-plane correction, world-space bias and bilinear comparison. Default scenes retain their existing shadow path until SoftShadows is enabled. A zero diameter in the enabled path gives the same bilinear hard-shadow algorithm on both backends.

Use 32 blocker disk samples plus the centre, and 64 filter disk samples. MaximumSoftShadowRadiusMeters bounds both search and filter footprints (default 2 m); this deliberately limits large penumbras and is not a general area-light ray tracer. Missing virtual taps fall back for the whole receiver to the CSM soft path. CPU page requests include the full maximum radius and bilinear footprint, including coarse clipmap selection margins. Changing softness requests extra pages without invalidating correct cached depth.

The comparison authors one shared angular diameter and radius, exposes hard/soft and angle controls, and labels their values. Verify increasing angle and blocker separation widens the transition while contact remains sharp, on both backends, including page boundaries, Radial, recovery and zero-angle behavior. Preserve existing default golden frames. Capture the rebuilt comparison and launch it visibly.
