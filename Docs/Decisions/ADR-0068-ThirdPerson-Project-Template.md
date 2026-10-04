# ADR-0068 — ThirdPerson Visual Studio project template
- Status: Accepted
- Date: 2026-09-30

## Context
The ThirdPerson port has everything a user needs:
- the level and its lighting (ADR-0069);
- the character, camera and input (ADR-0067);
- Quinn's animation (ADR-0066).

It lives in the repository as an ordinary compilable project, `templates/CSharpGameEngine.Templates.ThirdPerson`, whose namespace is `CgeThirdPerson`.

The user decided (2026-09-30) on three points:
- a Visual Studio New Project entry;
- NuGet-based engine references (ADR-0065);
- bundling Unreal's assets as they are, with the licence being the user's responsibility.

## Decision
1. **The template source stays the repository project.** It is built and tested there in SourceMode. Nothing is duplicated into a token-filled copy.
2. **`GameProjectGenerator`** (`tools/CSharpGameEngine.ProjectTemplates`, linked into the VSIX and the Feature Runner) creates a game from the template source:
   - It copies the source except `bin/`, `obj/` and the template csproj.
   - It replaces `CgeThirdPerson` with the project's safe identifier (Visual Studio's `$safeprojectname$` rule; a C# reserved keyword such as `event` also gets a leading underscore, added 2026-10-04 because `namespace event.Gameplay;` does not parse).
   - It writes the PackageMode project file: TargetFramework, RuntimeIdentifier, RootNamespace and `PackageReference CSharpGameEngine.Sdk`.
   - It writes a `nuget.config` that adds the local engine feed to the user's sources.
3. **The Visual Studio template** is `ProjectTemplates/CSharp/GameEngine/ThirdPerson` in the VSIX. It carries:
   - the names "Third Person" and "三人称" and a description through VSPackage resources 136/137;
   - the tags csharp, windows and games;
   - `CreateInPlace`;
   - a `ThirdPerson.csproj.template` whose file name avoids the repository's "every .csproj is in the solution" rule, and whose text equals the generator's project file with `$safeprojectname$` and `$cgeengineversion$` tokens (tested);
   - a `ReadMe.md` with the controls.

   The VSSDK archives a project template's `<Project File>` only when the `<Project>` element has items, so the ReadMe is also what makes the archive complete.
4. **`GameProjectTemplateWizard`:**
   - Copies the bundled `Packages/CSharpGameEngine.Sdk.<version>.nupkg` into `%LOCALAPPDATA%\CSharpGameEngine\packages\<version>`, unless it is already there.
   - Supplies `$cgeengineversion$`.
   - Writes the sources from `ProjectTemplateSources/ThirdPerson` and the `nuget.config`.
   - After creation, opens `Scenes/Lvl_ThirdPerson.json` (a C# scene class before ADR-0070).
5. **Unreal assets** are bundled unchanged under the Unreal Engine EULA, as the user decided. `THIRD_PARTY_NOTICES.md` records their origin and terms.

## Alternatives considered
- **ReplaceParameters over the whole template:** the template source would need tokens and would stop compiling in the repository.
- **Extracting the package on package load:** it would install files for users who never create a game. The wizard does it on demand instead.

## Consequences
- The wizard and the end-to-end Stage share one generator, so the Stage's output is exactly what users get.
- Creating a game needs restore access to the engine's dependencies (AssimpNet, SharpGLTF.Core, TerraFX) from the user's NuGet sources.
- Combat, Platforming and SideScroller variants and touch controls are out of scope.

## Verification
- `GameProjectGeneratorTests` checks:
  - the safe identifier;
  - copy exclusions and the namespace rename;
  - the template project file against the generator;
  - a full generation from the real template;
  - package version parsing and feed publishing.
- `VsixManifestTests` checks the template, the sources and the package in the VSIX.
- Stage `vsix.project-template` generates "My ThirdPerson" in %TEMP%, builds it from the package alone, and plays it on WARP.
- Interactive creation from the Visual Studio dialog is a manual check ([M0092](../Milestones/M0092-Sdk-Package-And-Project-Template.md)).

## Related features
F-L8-032 (this ADR), F-L6-009 (ADR-0065), F-L5-006, F-L4-015, F-L4-014.
