# ADR-0065 — Engine SDK package and the editor's game project context
- Status: Accepted
- Date: 2026-09-30

## Context
Games could only be built inside the engine repository. Two couplings caused this.

**Game projects referenced the engine by relative path.** `UserEcsProject` and the ThirdPerson template import `build/CSharpGameEngine.Game.props`. That file brings in:
- the shared runtime sources (Runtime.* projitems and Entities/*.cs), which the ECS generator completes inside each game (ADR-0033);
- the engine libraries, as ProjectReferences;
- the default Stage shaders.

The repository's `Directory.Build.targets` also adds the analyzer and the shader-compiler targets. A project elsewhere gets none of these.

**The VSIX located everything through the repository.**
- `RepoLocator` reads the repository path baked in at build time and requires `CSharpGameEngine.slnx` there.
- About a dozen call sites join it with `UserEcsProject/...`, for Assets, build output and primitives.
- Authoring bridges (glTF, FBX, asset authoring, primitives, shader compiler) are started from repository build outputs.
- Only the Preview Host was already bundled.

## Decision
1. **One package, `CSharpGameEngine.Sdk`, laid out like the repository** (`packaging/CSharpGameEngine.Sdk`):
   - `build/`: the NuGet entry points `CSharpGameEngine.Sdk.props/.targets`, plus `CSharpGameEngine.Game.props` and `CSharpGameEngine.ShaderGraph.targets` unchanged;
   - `lib/net10.0-windows10.0.19041.0/`: the ten engine libraries;
   - `analyzers/dotnet/cs/`: the generator and analyzers;
   - `src/`: the shared sources, including Runtime.Lighting, which Runtime.Windows imports;
   - `assets/Shaders/`: graphs, subgraphs and compute HLSL;
   - `tools/ShaderGraph/`: the offline compiler and its DXC binaries;
   - AssimpNet, SharpGLTF.Core and TerraFX are package dependencies.

   `Game.props` finds the engine root relative to its own location, so SourceMode (repository) and PackageMode use the same file. A single package keeps the libraries, the generator and the shared sources at one version: they are only valid together.
2. **Modes.**
   - `CSharpGameEngine.Sdk.props` sets `CgeEngineMode=Package`.
   - `ProjectReference`s exist only in Source mode.
   - The shader targets use a predefined `CgeShaderGraphCliExe` and skip building the compiler.
   - `Game.props` now defaults LangVersion 14, Nullable and ImplicitUsings, which the repository's `Directory.Build.props` used to supply.
   - `CgeSkipEngineAnalyzerReference` lets the packaging project opt out of the repository analyzer reference.
   - A game project states TargetFramework and RuntimeIdentifier itself, because restore must know them before package props exist.
3. **Game project context in the VSIX** (`CgeProjectContext`). A game project is a csproj that either imports `CSharpGameEngine.Game.props` or references `CSharpGameEngine.Sdk`. It is resolved in this order:
   - from a hint path (the Scene, model or asset being edited) by walking up;
   - otherwise from the open solution, which the package's solution events record. Within the solution, a project named UserEcsProject wins, then the shallowest project.
   - otherwise from the repository the VSIX was built from, for development.

   The context provides:
   - `ProjectDirectory`, which anchors Scene asset paths, Assets, `Primitives`, `Fab` and the game assembly `bin/<cfg>/net10.0-windows10.0.19041.0/win-x64/<Assembly>.dll`;
   - `EngineRoot`, the repository in SourceMode;
   - `WorkspaceRoot`, where bridges run: the engine root, else the project directory.

   It replaces `RepoLocator` for all project paths. The asset meta watcher restarts whenever a solution opens or closes.
4. **Bundled tools.** The VSIX publishes the glTF, FBX, asset-authoring and primitive bridges and the shader compiler into `Bridges/<name>/`.
   - `BridgeExecutableLocator` prefers them. Repository builds remain a development fallback.
   - The VSIX also carries the SDK package (`Packages/`) and the ThirdPerson template (ADR-0068).
5. **SourceMode-only features.**
   - Physics Scene Bake builds its exact host from the repository's engine sources. Outside the repository it reports that it needs the engine sources.
   - M0096 G: the host no longer references a game. The Bake builds the scene's own game project separately and the host loads that build (`bake --assembly`), so any game project in the repository can bake, not only UserEcsProject.
   - The legacy model viewer command stays repository-only.

## Alternatives considered
- **Two packages (runtime libraries and SDK tooling):** they would have to be versioned in lockstep anyway, because the generator emits code against the shared sources and libraries of the same revision.
- **Shipping the shared runtime as a compiled library:** the ECS generator completes `World`, `State` and the systems inside each game from the Entities sources, so they must stay sources (ADR-0033).
- **MSBuild evaluation of TargetPath inside devenv:** it would need the project system's evaluation and a live shell to test. The output path is fixed by Game.props, so the convention is exact and unit-testable.

## Consequences
- The package is about 31 MB, and the VSIX grows by the bridges and the package.
- A package rebuilt without a version bump is cached by NuGet. The end-to-end Stage therefore drops the cached copy before restoring.
- Physics Bake for projects outside the repository needs a Bake Host that loads the game assembly dynamically. This is future work.

## Verification
- `CgeProjectContextTests` covers PackageMode and SourceMode detection, ordinary projects, the walk up from a Scene file, and the search order.
- `BridgeExecutableLocatorTests` checks that a bundled bridge wins over a repository build.
- `VsixManifestTests` checks the bundled bridges, package, template and template sources.
- Stage `vsix.project-template` packs the SDK, generates the template outside the repository, builds it from the package alone, and checks that no restore or build input refers to the repository. It then plays it on WARP ([M0092](../Milestones/M0092-Sdk-Package-And-Project-Template.md)).

## Related features
F-L6-009 (this ADR), F-L8-032 (ADR-0068), F-L8-018 (Preview Host bundling).
