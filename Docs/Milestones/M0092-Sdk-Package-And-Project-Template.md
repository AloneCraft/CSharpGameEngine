# M0092 — Engine SDK package, repository-free editor and the Third Person project template
- Date: 2026-09-30
- Implementation: Complete for the SDK package, the editor's game-project context with bundled tools, the Third Person Visual Studio template and its wizard. Physics Scene Bake and the legacy viewer remain SourceMode-only.
- Verification: TestVerified / WARP RuntimeVerified; Stage `vsix.project-template`. Interactive creation in the Visual Studio dialog is a pending manual check.
- Decisions: [ADR-0065](../Decisions/ADR-0065-Engine-Sdk-Package-And-Project-Context.md), [ADR-0068](../Decisions/ADR-0068-ThirdPerson-Project-Template.md)
## Goal and scope
Make the engine usable outside its repository. A user installs the VSIX, chooses "Third Person / 三人称" in Visual Studio's New Project dialog, and gets the ported Unreal template: level, lighting, character, camera, input and Quinn's animation. The new project builds from the `CSharpGameEngine.Sdk` NuGet package.

Changed features: F-L6-009 (new), F-L8-032 (new), F-L8-018 (bundling now also covers the bridges).
## Observed behavior and fixes found along the way
- **TargetFramework.** A package-only game first failed with NETSDK1013 (TargetFramework ''): NuGet imports package props only after restore. The game project therefore states TargetFramework and RuntimeIdentifier itself, and the generator writes them.
- **Runtime.Lighting.** The first package restore failed because `Runtime.Windows.projitems` imports `Runtime.Lighting.projitems`. The package now carries Runtime.Lighting.
- **System.Text.Json.** NU1510 rejected an explicit System.Text.Json dependency (it is part of the framework); the dependency was removed.
- **Shell escaping.** Editing the packaging project through shell heredocs turned `\b` into a backspace character, and MSBuild then rejected the file (0x08). Those edits now go through script files.
- **Project template archive.**
  - The Visual Studio project template archive first missed its project file. The SDK's default item excludes (`**/*.*proj`) hid `.cgetemplateproj`, so the file became `ThirdPerson.csproj.template`.
  - After that, the VSSDK's `GetVSTemplateItems` still collected only `<ProjectItem>` children, not the `<Project File>` of a childless `<Project>`.
  - Stale archives were also not rebuilt incrementally, which masked the change.
  - With a `ReadMe.md` item the archive and its manifests are complete.
- **Wizard assembly.** The first manual run in the Experimental instance failed on "Create" with "this template tried to load component assembly 'CSharpGameEngine.VSExtension'". The vstemplates name their wizard by simple assembly name, and the package declared no `BindingPaths`, so Visual Studio could not resolve the assembly. The package now carries `[ProvideBindingPath]`, and `VsixManifestTests` checks the packaged pkgdef for the entry.
- **Repository paths.** The first package game ran with the repository removed from its build inputs. The Stage checks this on every run: `obj/*.nuget.g.*`, `project.assets.json` and `*.deps.json` must not mention the repository root.
## Commands and results
Commands executed from the repository root:
```text
dotnet pack packaging/CSharpGameEngine.Sdk -c Release
dotnet build tools/CSharpGameEngine.VSExtension -c Debug
dotnet test tests/CSharpGameEngine.VSExtension.Tests -c Debug
dotnet tools/CSharpGameEngine.FeatureRunner/bin/Debug/net10.0/CSharpGameEngine.FeatureRunner.dll verify-template-package --adapter hardware
dotnet run --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj -c Debug -- run vsix.project-template --configuration Debug --adapter warp
dotnet test CSharpGameEngine.slnx -c Release
```
| Check | Result |
|---|---|
| Complete solution tests, Release | 2282 passed, 0 failed, 0 skipped. This includes VSExtension 1065, Windows 319, FeatureRunner 154 and Core 128 (the three solution-registration tests are among them). |
| `CSharpGameEngine.Sdk.0.1.0.nupkg` | 31 MB: 10 libraries, the analyzer, the build entry points, six shared-source folders, 13 shader sources, and the shader compiler |
| Package game outside the repository, hardware GPU | Built with only a `PackageReference`; same frame as the repository build (Quinn, lighting, shadows), 0 D3D12 errors |
| `verify-template-package`, hardware | Generated "My ThirdPerson" in %TEMP%; walk 15.700 m, rise 1.228 m, landed, 0 D3D12 errors, no repository path in the build inputs |
| Stage `vsix.project-template`, Debug, WARP | Passed |
| VSIX contents | Five `Bridges/*` executables with runtimeconfig, `Packages/CSharpGameEngine.Sdk.0.1.0.nupkg`, the ThirdPerson template for 1033 and 1041 with manifests, and `ProjectTemplateSources/ThirdPerson` without bin, obj or csproj |
## Manual check (pending)
In the Experimental instance, press F5:
1. Choose New Project, then "Third Person" (English) or "三人称" (Japanese).
2. Create the project in any folder outside the repository.
3. Check that the package restores and `Scenes/Lvl_ThirdPerson.cs` opens.
4. Press F5 in the new solution, then walk, look around and jump.
## Remaining uncertainty
- The wizard's file work (publishing the bundled package to the feed, writing the renamed sources and nuget.config) is unit-tested through `GameProjectTemplateWizard.CreateProject`. The dialog itself (template listing, the replacement dictionary, restore, opening the level) has not run in Visual Studio.
- The engine's dependencies restore from the user's NuGet sources. A machine without nuget.org access needs its own mirror.
