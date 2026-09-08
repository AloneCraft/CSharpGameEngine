# M0067 - Canonical numeric adversarial closure

- Status: Completed
- Date: 2026-09-01

## Outcome

F-L1-009 now enforces the product-wide two-type real-number model without class-wide escape hatches. Authoritative state remains signed Q64.64 `LogicalScalar`; camera/domain-relative presentation remains `float`. `double`, `decimal`, and `BigInteger` remain available to independent tests and forced framework/SDK signatures only.

Adversarial review found and removed two structural holes. Decimal parsing previously admitted integer magnitudes wider than Q64.64 before shifting by 64, allowing inputs such as `18446744073709551616` to wrap to a different raw value. The analyzer also allowed a type-level adapter marker or `DependencyObject` inheritance to suppress the rule for arbitrary state. Parsing now checks the signed Q64.64 whole-number limit before scaling, adapter permission is method/property scoped, and external values may not remain as inferred engine state.

WPF and UI Automation boundaries now project to `float` inside the boundary: icon size validates finite/range before conversion, Shader Graph coordinates and zoom are converted once, render-card rounding uses the float adapter, and automation rectangles reject non-finite projected values.

## Verification

- `CSharpGameEngine.Numerics.Tests`: 10/10 Debug and Release through `world.logical-scalar`.
- `CSharpGameEngine.CodeAnalysis.Tests`: 146/146 Debug and Release through `world.logical-scalar`.
- `world.logical-scalar`: Passed in Debug and Release.
- `CSharpGameEngine.slnx`: Debug and Release build succeeded; transient VSIX test-asset copy retry warnings were non-fatal.
- Full solution tests: 2,038/2,038 Debug and 2,037/2,037 Release.

## Adversarial result

The root causes are removed rather than hidden behind another allowlist. There is one authoritative scalar, one presentation scalar, no product `BigInteger`, no type-wide numeric mode, and no legacy Logical profile or `PhysicsScalar` compatibility layer.
