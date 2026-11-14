# ADR-0094 — WorldCell numeric values independent of storage

- Status: Accepted; the version-1 reader is superseded by [ADR-0098](ADR-0098-Product-Goal-Phases-And-Compatibility.md) §5 (2026-11-15: a WorldCell is rebuildable, so only version 2 is read)
- Date: 2026-11-02
- Features: F-L1-011, F-L1-012, F-L6-008

## Context

WorldCell version 1 stores six signed Q64.64 values in fixed 16-byte slots. Native Single/Double and configurable fixed types have different layouts and ranges. Reinterpreting these slots as the selected primitive would change their meaning. Restricting every format to Q64.64 would lose native subnormals and the wider fixed range.

## Decision

Write version 2 with a UInt16 byte length and a canonical CGN1 sign/integer/fraction value for each coordinate. Preserve the identity prefix and the entity/dependency footer contracts. Read versions 1 and 2. Interpret legacy slots explicitly as Q64.64, then perform one checked nearest-even conversion to the selected numeric storage.

Validate the source representation before rounding. Do not require the rounded target to reproduce source bytes. Validate target bounds, closure and dependency order before publishing any definition. Overflow and invalid rounded bounds reject the whole read. Keep the outer artifact checksum and canonical envelope unchanged.

Simulation caches and cooked physics results remain specific to their numeric computation profile. Their existing profile identifiers must reflect the selected type; a different profile requires regeneration. This does not promise bit-identical simulation across profiles.

## Verification and limits

WorldCell tests cover deterministic version-2 round-trip, an independently assembled legacy version-1 payload, nearest-even conversion, malformed common values and atomic overflow rejection. Numeric profile tests exercise the shared value boundary for native minima and different fixed widths. Actual alternate-profile game builds establish compilation and dependency compatibility; cross-profile gameplay, Snapshot migration and distribution acceptance remain separate work in M0128.
