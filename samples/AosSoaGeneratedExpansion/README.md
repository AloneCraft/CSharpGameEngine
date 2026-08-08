# AosSoaGeneratedExpansion (historical)

This sample predates the SoA-only, `[GameSystem]`-based ECS (ADR-0029). It demonstrates the
earlier `[EcsArchetype]` / `[GenerateEntity]` source generators (`CSharpGameEngine.Ecs`,
`CSharpGameEngine.CodeAnalysis`'s `EcsArchetypeGenerator`/`GenerateEntityGenerator`), which were
deleted as part of the ADR-0029 migration - the AoS-vs-SoA benchmark comparison it illustrated
(ADR-0024, M0006) can no longer be re-run.

`AosSoaGeneratedExpansion.SingleFile.cs` is self-contained (no reference to any engine project),
so it still compiles and runs standalone with `dotnet run --project samples/AosSoaGeneratedExpansion`.
It is kept only as a historical record of the earlier design; see `Docs/Specifications/Ecs-Soa-Systems.md`
and `samples/SoaSystemsDemo/` for the current ECS.
