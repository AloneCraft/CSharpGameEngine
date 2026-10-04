# M0094 — Scene Editor document (Design / JSON) and removal of the scene context menus
- Date: 2026-10-01
- Decision: [ADR-0071](../Decisions/ADR-0071-Scene-Editor-Document-With-Design-And-Json-Views.md)
- Implementation: Done (automated and experimental-instance checks); manual keyboard checks pending

## Goal
Scenes open by double-click in one editor tab with a Design view and a JSON view, and the extension adds nothing to the code-editor or Solution Explorer context menus (user request, 2026-10-01).

## Steps
| Step | Content | Status |
|---|---|---|
| 1 | Remove the context-menu group, its 34 buttons and the code behind them. A test keeps every `.vsct` off `IDM_VS_CTXT_*` | Done (d0729ee) |
| 2 | `SceneJsonEditorFactory` for `.json` (declines non-scene files), `SceneJsonEditorPane` with Design/JSON tabs over the shared text buffer, per-tab scene filter, toolbar command opening the editor; the external virtual document is removed | Done (62b46fd) |
| 3 | Scene files become `Scenes\**\*.json` (user request, 2026-10-01; ADR-0070 decision 1 revised): one rule `SceneFiles.IsScene` for the build (`ProjectDir`), the generator, the analyzers and the extension; the 29 repository scenes renamed (the 10 FeatureStages scenes moved into `Scenes/`); the converter, item template, project generator, export scripts and release closure follow. MyGame3's `Lvl_ThirdPerson.cs` converted to `Scenes/Lvl_ThirdPerson.json` | Done (this commit) |

## Experimental instance checks (CgeAutomation hive, 2026-10-01)
Driven through DTE and UI Automation (Toggle on the tabs, no mouse or key input):
- `global.json` opens in the JSON editor; `PrimitiveShowcaseBoxScene.scene.json` (now `.json`) opens in the Scene Editor's Design view.
- Switching Design → JSON → Design → JSON keeps Visual Studio running. The first build, which embedded a code window, crashed on the JSON tab (`AddVisualChild`); the text view created with `VIF_NO_HWND_SUPPORT` does not.
- The JSON view shows the scene schema bar; an unknown top-level key gets a warning squiggle and the status bar counts one warning.
- An edit marks the document dirty, and Undo restores the text and clears the dirty mark.
- View Code switches the tab of the same window; opening, switching and closing leave the file unchanged.

## Pending manual checks
- Ctrl+Z / Ctrl+Y typed in the JSON view, and a gizmo edit in the Design view appearing in the JSON view.
- Two scene tabs side by side each show their own scene, and Hierarchy follows the tab brought to the front.

## Tests
`VsixMenuPlacementTests.NothingIsPlacedOnEditorOrSolutionExplorerContextMenus` (fails before step 1), `SceneJsonEditorTests` (file acceptance, logical-view mapping, `.json` registration above the JSON editor, per-tab scene filter), `InitialLayoutToolWindowPlanTests` (retired IDs are not reused).
