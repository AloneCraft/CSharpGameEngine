# M0062 - Structured Shader Graph regions

## Outcome

Shader Graph now has first-class canonical `If`, statically bounded `ForRange`, `Return`, and `LoopIndex` constructs. The validator fixes ownership, nesting, branch membership, loop bounds, Return output typing, LoopIndex scope, and non-escaping branch values. The deterministic backend emits structured HLSL and source-map entries for control and exit nodes.

## Visual Studio behavior

The node palette can instantiate built-in nodes even when the current asset has no prototype. Toolbar commands create If and ForRange regions and typed Return nodes. Selecting a Bool condition and two typed values creates Then/Else branches with matching Returns; ForRange creates its bounded control and LoopIndex together. Region kind and name are rendered on the canvas, built-in ports use exact type checks, and control arguments are editable in the Property Inspector.

## Compatibility

Schema version remains v1. Existing regions deserialize as `Comment`; the added semantic fields use default empty identities and `Body`. Dependency extraction, canonical cloning, and item-template GUID remapping preserve control, parent, branch, and Else membership.

## Automated evidence

- Canonical structured-region round-trip and semantic hashing.
- If/Else/Return and ForRange/LoopIndex HLSL lowering with source maps.
- Invalid bounds, branch value escape, invalid LoopIndex scope, output-in-region, and ownership diagnostics.
- VSIX palette creation, typed connection rejection, If/Else Returns, bounded ForRange creation, Undo, localization, and package build.
- shadergraph.schema, shadergraph.compiler, and vsix.shader-graph passed in Debug and Release; full solution runs passed 1,641 Debug tests and 1,640 Release tests.

## Conformance

F-L2-005 and F-L7-049 now Match their structured-region scope. F-L8-031 remains Partial only because automated Visual Studio Experimental Instance open/edit/save/reload evidence is still absent; its headless editor/package contract is TestVerified.

## Next milestone

Automate the Visual Studio Experimental Instance interaction path and then close F-L8-031 conformance if the observed package behavior matches the editor contract.
