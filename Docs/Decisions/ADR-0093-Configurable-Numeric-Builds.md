# ADR-0093 — Configurable numeric builds

- Status: Accepted
- Date: 2026-11-02
- Features: F-L1-012, F-L1-011

## Context

The approved production-flow design selects fixed 64/128/192-bit storage and a fractional position, or actual Single/Double computation. Q64.64 assumptions also exist in mathematics, world artifacts, snapshots and dependency builds; changing only a project property would leave an inconsistent runtime.

## Decision

Select storage and fractional constants during compilation. Keep the optimized default Q64.64 path. Use signed long, Int128 or a three-limb Int192 for other fixed formats, backed by bounded stack arithmetic for multiply/divide/sqrt and checked rescaling. Commit output only after rounding and range validation. Keep independent integer and IEEE oracles in tests.

The fixed fractional range is zero through width minus two, preserving the sign and the value one, matching the existing persistence contract. Arithmetic kernels may test an additional fractional boundary internally without extending the public persistence contract.

Use direct fraction construction for mathematical coefficients so their integer denominator need not fit as an enlarged fixed-point value. A closed static generic kernel shares fixed mathematics. For coarse or narrow-range formats, compile its 256-bit temporary specialization and round results back to the selected storage. The scratch precision is at least 32 fractional bits. Representability of a requested constant is checked by its property getter; an unrepresentable constant must not disable all mathematics at type initialization. Try operations reject unrepresentable results atomically. Higher fractional precision uses an appropriate series length; default Q64.64 keeps its polynomial path.

Float/Double use actual CLR primitives, compile-time aliases and C# 14 extension members ([official language specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions)). Actual compiler builds and execution tests verify their metadata/layout, subnormal and negative-zero retention, arithmetic, math and persistence. MathF/Math supply their transcendental functions directly. NUM001 allows double only when that mode is selected; decimal/BigInteger restrictions remain. No runtime reflection or mode dispatch selects a numeric type.

Give each nondefault numeric build a distinct output/cache identity. Extend that identity through the complete dependency closure and SDK before exposing game settings. Do not silently substitute the default DLL for a different requested format. Rendering projection remains independent, and persisted value encoding remains independent of storage layout.

Apply project-body settings before Microsoft.Common freezes output, target and reference-assembly paths. Late property changes can separate intermediate files while still overwriting the final DLL; an MSBuild evaluation regression checks all of these paths. Alternate builds must leave previously built profiles unchanged.

The SDK keeps prebuilt default libraries and ships immutable source plus a closed auxiliary-project template for other settings. Build selected Numerics/World and active built-in Cook modules under the consumer's obj directory, preserving assembly names. Disable parent Directory.Build imports in these projects. Replace both compile references and runtime copies, and reject missing outputs. The package is never modified. Actual consumers are generated outside the repository; their inputs must not reference the repository.

## Alternatives considered

Runtime mode switches would retain representation branches and permit dependency disagreement. A fixed-point wrapper around Float would violate the selected primitive-type contract. BigInteger as simulation storage would violate bounded native storage and allocation requirements. These alternatives are not adopted.

## Consequences and verification

The current implementation is partial for the full runtime: numeric-library modes, Source-game dependency/cooking-tool propagation and SDK consumer profile builds are connected. DLL metadata and NUM002 reject mixed profiles. Grid indices use exact floor over bounded integer digits or IEEE significands; caches and cooked physics data use the selected identity. WorldCell common-value migration is recorded in ADR-0094. Source compilations execute twelve fixed settings and both CLR floating types. Actual builds cover alternate-profile games, a game-local setting, external ThirdPerson SDK consumers and TopDown cooking. Native profile metadata/copies agree and no repository reference remains. Selected physics bake/replay runs are verified separately from actual UI acceptance. Independent oracles cover arithmetic/IEEE conversion and selected high-precision transcendental values. Snapshot v2 migration is implemented under ADR-0095. Authoring settings/input validation and actual gameplay/distribution acceptance remain.

The observed boundary and final evidence are in [Numeric Configuration](../Specifications/Numeric-Configuration.md) and [M0128](../Milestones/M0128-Static-Production-Flow.md).
