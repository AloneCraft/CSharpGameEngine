# M0087: Immutable formal release

## Outcome

F-L7-048 is implemented as an executable Release-only formal publication gate. It consumes
one passing `release.candidate` Stage and its exact RC report, validated Rendering, Content,
ECS, Platform, and VSIX release reports, and the exact product archive/signing inputs.

## Immutable identity

The release ID is SHA-256 over product/version/target/configuration/Git provenance, archive,
signing inputs, inner package-manifest and SPDX hashes, and the eight retained evidence-file
identities. Publication atomically moves one complete directory to:

`.artifacts/Releases/<release-id>/`

The directory contains the exact archive, signing inputs, copied evidence reports, and one
canonical `csharp-game-engine-formal-release-v1` manifest. Its `FormalRelease` retention
class is immutable, non-expiring, and uses the release ID as rollback identity.

## Failure and adversarial review

Before publication, every source report is revalidated in the exact RC Stage execution
window. The VSIX SHA inside the product archive must equal the package observed by F-L8-029.
After publication, the copied archive is reopened through the existing package/SPDX
validator. Missing, changed, escaped, reordered, noncanonical, or extra bytes fail.

Republishing the same identity returns the original manifest without changing any byte or
timestamp. An invalid existing release is never repaired or overwritten. A validation failure
before the atomic move removes temporary output; a failure immediately after the move removes
the not-yet-published owned destination. Nested release commands execute the current
FeatureRunner DLL rather than rebuilding and locking their running apphost.

## Verification

Five focused manifest tests and the combined Catalog/CLI contracts pass in Debug and Release.
They cover package/SPDX reopening, content-addressed identity, idempotence, immutable refusal,
source mutation, extra files, partial cleanup, and nested-runner isolation. Final formal
publication waits on the same F-L8-029 environment evidence as M0086.
