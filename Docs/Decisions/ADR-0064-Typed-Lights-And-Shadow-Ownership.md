# ADR-0064 — Typed lights, physical values and shadow ownership
- Status: Accepted
- Date: 2026-09-08
## Context
The engine already had four ECS light types, but import and replay records retained kind-dependent numeric fields. Scalar physical fields still allowed units to be interchanged, and each light owned a duplicated shadow switch/settings tuple. The user approved breaking public API changes and explicitly limited kind changes to source authoring.
## Decision
1. Put validated physical readonly values in Contracts/Lighting and four light structs plus optional LightShadow in a portable Runtime.Lighting shared-source project.
2. Keep Transform, ECS lifetime and static archetype generation unchanged. One Entity owns at most one analytic light; shadow requests have no independent target identity.
3. Use applicable-only typed render submissions through RenderServer and reusable typed completed-frame buffers with explicit cross-type ordering. Keep native layout tags behind the graphics boundary.
4. Model imported directional/local quantities with different variant types and explicit photometric conversion. Preserve missing range and glTF-specific angle semantics.
5. Use C# IScene as the only authoring truth. Parse constructors statically from declared Roslyn symbols. Convert types as one verified native source-Undo transaction, preserve local total flux, and demand explicit values across the directional dimensional boundary.
6. Remove old namespace/type/field contracts without compatibility wrappers. Historical migration diagnostics may mention removed names, but may not recreate those APIs.
## Alternatives considered
- A universal Light with a mutable type enum leaves invalid fields and ambiguous units representable.
- A large inheritance hierarchy does not fit the existing SoA/static-archetype data model.
- Separate emission/shape components permit unsupported combinations and need another pairing identity.
- Runtime kind replacement and hidden old-type fields broaden lifecycle and persistence semantics beyond the request.
## Consequences
This intentionally breaks authored code and import/render consumers. Defaults and existing scene brightness/shadow presence are preserved by migration. The renderer keeps its current photometry, LTC and Shader ABI. Float overflow/invalid runtime values cannot stop unrelated light extraction.
A glTF zero-inner-angle cone remains valid imported data but cannot become the stricter authored cone without explicit resolution; import must not silently clamp it.
## Verification
Physical quantity tests, compile-time invalid-assignment tests, generated ECS diagnostics, static editing/conversion tests, WARP recovery readback and warm-buffer allocation tests are recorded in [M0088](../Milestones/M0088-Typed-Lighting.md). Interactive VS deployment is a separate environment verification, not implied by unit tests.
## Related features
F-L5-001, F-L8-024, F-L8-025; rendering recovery and glTF/Unreal import. See [Typed Lighting](../Specifications/Typed-Lighting.md).
