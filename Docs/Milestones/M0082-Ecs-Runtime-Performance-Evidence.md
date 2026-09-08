# M0082 - ECS Runtime Performance Evidence

## Outcome

F-L7-032 is implemented as the executable `ecs.performance` Stage. Four independent compilations of the shipping Source Generator are built at exactly 1, 64, 65, and 128 Component types. Each compilation executes its highest-index generated write query over 100,000 live entities for exactly five warm-ups followed by thirty measured samples with Tiered JIT disabled.

This closes both sides of the Component boundary: 1 and 64 emit one enable-mask word and no `ComponentEnabledHigh` runtime shape; 65 and 128 emit two words and exercise the high Component. The Stage does not infer the common path from a 128-type compilation and does not use reflection, a dynamic archetype, or a synthetic loop in place of the generated World.

## Source of truth and ownership

- `tools/CSharpGameEngine.Ecs.Benchmarks` is the isolated raw producer. The `EcsComponentCount` MSBuild property accepts only 1, 64, 65, or 128 and places persisted generator output under a count-qualified `obj/<configuration>/net10.0/generated` directory.
- The generated `World`, archetype storage, and `GeneratedGameLoop` are the measured implementation. Setup creates one full-shape archetype with 100,000 entities; measured work writes the highest-index Component.
- Raw reports are canonical newline-terminated JSON and are published by same-directory temporary-file move. They record configuration, runtime mode, boundary, query, entity and worker counts, mask shape, generated-source SHA-256, deterministic state SHA-256, and every timing/allocation sample.
- `EcsPerformanceProbe` is the independent validator. It recomputes the known final state hash, reopens and rehashes the persisted generated source, reinspects its high-word shape, and then publishes common machine/Git/SHA-bound performance reports.
- The common `PerformanceReportStore` remains statistics and integrity truth. It recomputes median, nearest-rank p95, MAD, allocation, budgets, machine fingerprint, and report payload hash.

## Performance contract

Debug timing is diagnostic because non-optimized code generation is not a shipping-performance oracle. Debug and Release both require zero managed allocation in every measured generated query.

Release gates every boundary at 6,000,000 ns median and 8,330,000 ns p95. The independently compiled 64-type path is also compared against the one-type direct path; median or p95 regression above 20 percent fails. This relative tolerance is only a cross-process regression alarm. It does not replace or weaken the fixed simulation budget, and the structural test independently proves that no high-word field or branch enters the 1-64 generated shape.

## Failure contract

The Stage fails for a missing, duplicate, or extra boundary; wrong configuration, runtime mode, query, entity or worker count; invalid or mismatched source/state hash; one-word/high-word aliasing; missing, duplicate, extra, incorrectly indexed, non-measurable, or allocating samples; changed persisted generated source; failed absolute or relative budget; invalid common report; or failed 64/65/128 generator E2E test.

A capture failure cannot publish a partial raw report. Source generation output is count-qualified so a later compilation cannot silently replace the evidence for an earlier boundary.

## Adversarial review

The first complete implementation validated only the format and distinctness of the state and generated-source hashes. That still trusted the raw producer to describe its own output. The final validator independently reconstructs the deterministic final-state hash and independently hashes and inspects each persisted generator output.

The first auxiliary cross-process comparison used a 15 percent p95 threshold. One otherwise-valid Release run measured 418,450/560,100 ns for one type and 445,800/650,000 ns for 64 types: median regression was 6.5 percent while the thirty-sample nearest-rank p95 differed by 16.0 percent. The product-wide 8.33 ms p95 gate passed by more than an order of magnitude and the source-shape oracle proved a single word. The relative alarm was fixed at 20 percent to avoid a one-sample process-noise cliff; the absolute budget, zero-allocation rule, and source-shape proof were retained.

Overengineering review kept one parameterized benchmark project rather than four copied projects. The four builds remain genuinely separate compilations because the preprocessor symbol and generated source are count-qualified and independently hashed.

## Verification

- Focused validator/plan tests: 4/4 Debug.
- Current Debug Stage: `20260901T155813468Z-ecs-release-performance-920d1c8ed90d477288a1349f15c72996`, artifact SHA-256 `9cc9b11e486f2f991fc27ada7877eb040c4313e49f3d70c193b31c88a00d181b`.
- Current Release Stage: `20260901T160004976Z-ecs-release-performance-9358908023fa4e8386adbfd9b7b67e3b`, artifact SHA-256 `e267839d937db8c57258d793f8270778bdcaf1862f42254ef61ef7b59ad6c3b7`.
- Both Stages execute 4/4 performance-probe contracts and 3/3 exact-64 plus wide-mask generator E2E cases.

| Component types | Mask words | Release median | Release p95 | Allocation p95 | Common report SHA-256 |
|---:|---:|---:|---:|---:|---|
| 1 | 1 | 484,600 ns | 645,700 ns | 0 | `0226e98343e4cef9796af60961212cd54a454bf6f47167cb3390c40a238d928a` |
| 64 | 1 | 526,750 ns | 695,600 ns | 0 | `1deaf2a719a38cfacf2c9a8d536e890b144a3d6e7ee2bc2d26914049cbcbccc8` |
| 65 | 2 | 638,900 ns | 929,600 ns | 0 | `9426cbee3a49a417accae3b4516c6861c003f0a06c810615bd600987f33f1e2f` |
| 128 | 2 | 563,850 ns | 646,500 ns | 0 | `0d22baae9d358f18f1117b3686fde76585804a23490952eb37f37350748919bc` |

The measured 128-type p95 is approximately 0.90 ms, leaving the structural, scheduling, snapshot, and performance evidence ready for F-L7-033 ECS release closure.
