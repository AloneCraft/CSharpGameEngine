# ADR-0060 — Structured Shader Graph regions

## Status

Accepted on 2026-08-29.

## Decision

Shader Graph v1 represents control flow with semantic `If` and `ForRange` regions owned by explicit control nodes. Direct members are assigned to `Then`, `Else`, or `Body`; nested regions identify their parent region and branch. `Return` is a typed node that writes a named Subgraph output and exits the generated function. `LoopIndex` is valid only within a bounded `ForRange` ancestry.

ForRange bounds are canonical integer arguments. Validation requires a non-negative static range, a maximum in 1..4096, and a maximum that covers the requested range. Values may flow from an outer scope into a structured branch, but may not escape a branch or cross between sibling branches.

The VSIX owns visual creation of these constructs. It creates control nodes, branch membership, typed edges, layout, and Undo/Redo mutations atomically. Control arguments remain editable in the Property Inspector.

## Consequences

- Region membership is semantic and contributes to the graph hash; visual rectangle layout does not.
- Generated HLSL preserves structured `if`/`else`, bounded `for`, and early `return` statements with node source mapping.
- Legacy comment regions remain valid because `Comment` is the default region kind.
- Output nodes are forbidden inside structured regions; branch exits use `Return`.

## References

ADR-0058; ADR-0059; M0062; F-L2-005; F-L7-049; F-L8-031.
