# ADR-0095 — Profile-neutral generated snapshots

- Status: Accepted; implemented and test verified. The version-1 correspondence and readers are superseded by [ADR-0098](ADR-0098-Product-Goal-Phases-And-Compatibility.md) §5 (2026-11-15: saves carry no compatibility promise before the first release; version 1 is rejected)
- Date: 2026-11-02
- Features: F-L1-011, F-L6-007

## Context

Version 1 includes CLR numeric names in component shapes and archetype keys. It stores only the schema fingerprint, not a complete historical type catalog. A native alias therefore changes a schema even when the authored data has the same meaning. Comparing a destination reserialization to source bytes also rejects valid rounding between profiles.

## Decision

Use a new artifact version for generated World persistence. Give fixed scalars, Single and Double a common real-value identity in saved shapes. Store exact finite decimal text from the common numeric boundary. Validate canonical source text and JSON independently of destination storage, then perform checked nearest-even conversion. Preserve integer and other type distinctions.

Generate the legacy schema/key correspondence and direct value readers from the closed compilation. Preserve a reader for provable version-1 structures. Do not guess a historical schema from its hash when type erasure or unavailable declarations prevent a reliable correspondence. Keep the standalone public reflection codec outside the generated World path.

Check the original envelope, state hash and canonical values before conversion. Normalize accepted legacy/profile-dependent payloads into a validated current document before restore, pending-cell handling or merge. Reject overflow and unsupported mappings before any World mutation. Writers and merges produce only the new version. A target representation need not reproduce the original source bytes.

## Verification and boundary

Version 2 and generated normalization are implemented. Actual separately compiled 64/32, 128/64, 192/96, Single and Double Worlds produce identical artifacts for identical representable values. Cross-profile reads verify target rounding; an overflow in a later entity leaves the entire World unchanged. A fixed version-1 fixture captured before migration reads through generated correspondence in fixed and native Worlds. Canonical JSON/text, constructor-state validation, pending cells, merge, schema mismatch and checksum rejection are covered by the generator and Entities suites. M0128 records commands and evidence.

Version-1 files contain only a schema fingerprint, so unknown historical schemas cannot be reconstructed from the artifact. Generated readers accept the current compilation's original shape and a default-fixed shape only where authored aliases or known numeric declarations prove the correspondence. Metadata-only erased aliases and primitive native component identities may not supply that evidence; use the original supported configuration to read and resave such files as version 2. This is an explicit compatibility boundary, with no runtime type discovery or guessed hash mapping.
