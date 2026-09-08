# M0076 — Content Release Evidence

## Outcome

F-L7-029 now closes the Asset/Content track with one content-scoped, canonical, SHA-256-bound evidence report. The report is derived from existing truths: the runtime-loadable `ShippingContentPackage`, owned content schema sources, an explicit project/toolchain dependency input set, Git provenance, and the passing F-L7-028 performance reports for the same Debug or Release configuration.

## Source of truth and boundary

- `ShippingContentManifest` and the package files remain the content identity and byte-closure truth.
- Metadata, artifact-envelope, and shipping-manifest codecs remain the schema truth; the evidence report records their public versions and hashes the owning source files.
- F-L7-028 reports remain the performance truth; F-L7-029 stores their file and payload hashes and independently revalidates them.
- The explicit project and toolchain files are SBOM inputs only. F-L7-045 still owns SPDX JSON, signing inputs, and install/run/uninstall. F-L7-048 still owns the formal immutable product release manifest.

No alternate package, artifact reader, schema registry, dependency database, or long-lived migration layer was added.

## Fail-closed validation

`ContentReleaseEvidence` publishes a deterministic seven-kind fixture closure through the production CAS, manifest builder, and package publisher. Validation reopens that package and recomputes its manifest fingerprint, exact file set, lengths, and SHA-256 values. It also recomputes every schema/SBOM input hash and requires recent passing cold and one-change performance reports with the same configuration, Git provenance, and machine fingerprint.

The report must be canonical UTF-8 JSON with one trailing LF and a valid payload hash. Path escape, reparse points, relocation outside the evidence roots, missing or extra files, stale performance, and any source, package, performance, or report mutation fail validation.

## Adversarial review

The first missing-performance test exposed an error classification defect: a required report absence escaped as an exception and would have appeared as CLI misuse. The probe now returns an oracle failure, writes the reason, and removes its incomplete evidence directory. Review also retained one existing package format, rejected the misleading “installable package” claim, and kept global SPDX/sign/install/formal-manifest responsibilities out of this Feature.

## Verification

- `ContentReleaseEvidenceTests`: 6/6 Debug; included in the complete 82/82 Debug and 82/82 Release FeatureRunner suites.
- `CSharpGameEngine.Content.Tests`: 33/33 Debug and 33/33 Release.
- `content.release` Debug: `20260901T124450645Z-full-615623b48c4049bc95f84a40d1a8d69d`.
- `content.release` Release: `20260901T124535873Z-full-2d1a7953dc1846c3af8491f70ee07618`.
- Debug and Release reports each bind eight package files, ten repository inputs, both required performance IDs, one machine fingerprint, and the current Git provenance.
- Diff hygiene and the focused `BigInteger`/`double`/legacy numeric scan pass for the new implementation and tests.
