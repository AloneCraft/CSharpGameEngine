# M0075 — Content Performance Evidence

## Outcome

F-L7-028 measures the production incremental compiler rather than a synthetic proxy. Cold and one-change workloads publish integrity-validated reports under the common performance schema.

## Workloads

- Cold: 32 independent reachable roots, a new CAS/state directory per iteration, concurrency four.
- Incremental: 32 leaves plus one dependent root; exactly one leaf changes per iteration, so exactly two nodes rebuild and 31 reuse.
- Each workload uses exactly five warm-ups and thirty measured samples.

## Evidence contract

Reports contain elapsed and allocation samples, median, nearest-rank p95, MAD, maximum, explicit absolute budgets, machine fingerprint, git commit/dirty state, and a payload SHA-256. Validation recomputes the report hash, machine fingerprint, statistics, and verdict instead of trusting serialized conclusions.

## Complexity

No benchmark-only compiler, cache, artifact format, baseline database, or timing API was added. The probe calls `IncrementalContentCompiler` and canonical `ContentArtifactCodec` directly, then reuses the existing common measurement/report framework.

## Verification

The direct Debug measurement recorded cold median/p95 95.5/103.5 ms and one-change 14.8/18.4 ms. Release recorded cold 94.8/100.0 ms and one-change 14.8/17.9 ms. Allocation p95 stayed below 3.12 MB cold and 1.25 MB incremental. `content.performance` passed Debug as `20260901T122500384Z-full-26dc4fc680c24408960d5a5f8fc04bc6` and Release as `20260901T122502393Z-full-c6cadb1813fa48c48e68fca03ff960cb`.
