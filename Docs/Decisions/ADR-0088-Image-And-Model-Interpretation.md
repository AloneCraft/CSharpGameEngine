# ADR-0088 — Image and model interpretation

- Status: Accepted
- Date: 2026-10-26
- Feature: F-L7-058, related F-L8-035

## Context

An image extension does not identify color space, PBR channel meaning or whether the caller needs encoded bytes. One FBX may contain several meshes, a shared rig and multiple Takes. Guessing again on reimport would overwrite author intent and break references.

## Decision

Keep source bytes and canonical full-filename `.meta` schema 1. Persist explicit output settings in the existing string dictionary. Derive output identity from source AssetId, kind and immutable key. Filename/target inference is initial-only; pixel Normal detection is advisory. Use StbImageSharp 2.30.16 in L7 for bounded PNG/JPEG decode and the existing Assimp adapter for composite FBX. L2 owns settings/identity, L6 owns payloads/CAS/metadata transactions, L7 owns interpretation and cooking, L8 owns review and native text Undo. ImageData and TextureOutputData are distinct payloads/codecs. No provider types enter runtime contracts.

## Alternatives considered

- Renaming the source extension or writing many adjacent derivatives: loses the original identity or clutters the source tree.
- One destructive interpretation per source: prevents independent Color/Normal/Image consumers.
- Pixel heuristics as authoritative settings: cannot reliably determine green orientation or PBR channel meaning.
- Moving importers into VSIX: breaks headless parity and layer ownership.

## Consequences

Registration is explicit. Existing references require semantic migration when a field becomes an output group. Output removal needs reference replacement/review. CAS publication keeps the previous successful manifest on failure. Unsupported advanced model processors remain explicit diagnostics or future operations, not invented data. This work does not implicitly complete the universal runtime import cutover.

## Verification

See M0126 and the image/model interpretation specification for commands, results and remaining runtime gates.
