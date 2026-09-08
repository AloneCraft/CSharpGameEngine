# M0086: Level-gated Release Candidate

## Outcome

F-L7-047 is implemented as an executable Release-only gate. The Catalog remains the only
source for Feature track, dependency Level, lifecycle, dependency edges, and Stage ownership.
The gate derives the selected product-track closure, removes `Specified` and `Deferred`
work, and requires every resulting executable Stage exactly once.

## Evidence model

`run-release-candidate --configuration Release --level 7` executes the derived prerequisites
under the dedicated `release-candidate-prerequisite` profile and WARP. The canonical
`csharp-game-engine-release-candidate-v1` report then binds:

- the ordinal Feature and Stage closure;
- Release/WARP/current-Git execution options;
- one complete passing, non-stale run result for every required Stage;
- each run-result file SHA-256 and Stage artifact SHA-256;
- Catalog, Roadmap, release specification, and compatibility-matrix inputs.

The report contains no copied dynamic compatibility database. Existing Stage results remain
execution truth. F-L7-047 and F-L7-048 are control Features and cannot recursively satisfy
their own prerequisites.

## Failure and adversarial review

Missing, failed, stale, ordinary-profile, wrong-configuration, wrong-adapter, wrong-Git,
incomplete, tampered, escaped, duplicate, noncanonical, or extra evidence fails without
retaining a partial report. Review corrected two subtle cases: an implemented gate must not
include itself in its closure, and a successful Stage with the one permitted recorded
infrastructure retry must remain acceptable.

## Verification

Five focused RC tests pass in Debug and Release, including full product closure, lower-Level
single-track exclusion, missing/failed/stale/profile faults, and post-capture mutation. Final
RuntimeVerified evidence waits only for the F-L8-029 Experimental Instance prerequisites.
