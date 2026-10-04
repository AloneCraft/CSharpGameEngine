# ADR-0071 — Scene Editor: one document with Design and JSON views, no context menus
- Status: Accepted
- Date: 2026-10-01
- Supersedes: the context-menu entry point of ADR-0040 Phase 9 and M0020 (code-editor and Solution Explorer "Open in Scene Designer"), and the external virtual document (`.cgescene`) that hosted the Scene Designer.
- Refines: [ADR-0070](ADR-0070-Scene-Json-Documents-And-Runtime-Editor-World.md) decision 5 (the JSON text buffer is the only in-memory copy).

## Context
The user reported on 2026-10-01, with Visual Studio screenshots:
- Right-clicking a C# file showed "シーン デザイナーで開く" followed by some twenty rows reading "Scene". The user asked that C# files never show them, and that the scene context menus not be shown at all.
- Double-clicking a `*.scene.json` should open the Scene view, with a Design view and a JSON view in the editor tab, like the XAML editor.

Facts found:
- The context-menu group held 34 buttons (the direct command, 32 scene slots and an overflow line), none marked `DefaultInvisible`. The package loads late (it has no auto-load), and its command handlers are registered only after the Roslyn workspace is ready. Until then Visual Studio shows `.vsct` defaults: every button visible, with placeholder text.
- The Scene Designer opened in an external virtual document keyed to a temporary `.cgescene` moniker. It was not the `*.scene.json` document, and it had no JSON view.
- `ProvideEditorExtension(".scene.json")` was tried before and never matched: Visual Studio treats the extension as `.json` (ADR-0020/ADR-0021).

## Decision
1. **No context menus.** The code-editor and Solution Explorer context-menu group and its 34 buttons are removed. Their command IDs (0x0109, 0x0110-0x0130, group 0x1024) are retired and not reused. The code behind them is deleted too: the menu policy, the scene slots, the Solution Explorer selection analysis, caret capture and the menu snapshots. The Extensions menu and toolbar keep "Open in Scene Designer", which opens the active scene JSON in the Scene Editor.
2. **The Scene Editor** (`SceneJsonEditorFactory`, `SceneJsonEditorPane`) is the default editor of scene JSON (`Scenes\**\*.json`, ADR-0070 decision 1; `*.scene.json` until 2026-10-01).
   - It is registered for `.json` at priority 0x40, above the Visual Studio JSON editor (0x32). `CreateEditorInstance` returns `VS_E_UNSUPPORTEDFORMAT` for any JSON that is not a scene (not under its project's `Scenes` folder, found through the nearest `*.csproj`), and Visual Studio opens it with the next editor.
   - The tab has two views, switched by tabs at the bottom as in the XAML editor: **Design** (the Scene Designer, the default) and **JSON**. Both are one window. `View Code`/`View Designer` (the TextView/Code and Designer logical views) switch the tab through `IVsMultiViewDocumentView` instead of opening a second window.
   - The JSON view is a Visual Studio text view over the document's buffer with the JSON language service, so the scene schema's warnings and completion are the JSON editor's. It is created for WPF hosting without an HWND (`VIF_NO_HWND_SUPPORT`). A code window created with `CreatePaneWindow` parents its WPF host in a window of its own; placing that host in the pane crashed Visual Studio in WPF layout (observed, `AddVisualChild`).
3. **One copy of the document.** The editor's document data is the JSON text buffer, shared with the JSON editor when that already has the file open. The editor has no model, save or Undo of its own (ADR-0070 decision 5). The designer's gestures are written into this buffer as before (`SceneTextBufferWriter`), so Save, the `*` mark and Undo belong to Visual Studio in both views. The pane registers no standard commands (M0045); in the JSON view it forwards command routing to its text view.
4. **Each tab shows its own scene.** The session still pins one scene. A tab pins its scene when it comes to the front (`IVsWindowFrameNotify3.OnShow`), and its designer ignores other scenes' pins (`SceneDesignerTargetFilter`). Hierarchy and Inspector follow the pin as before. The scene switcher is hidden in the editor, since a scene JSON declares one scene.
5. **The tool window** stays registered only as a recovery path when the Scene Editor cannot be created.

## Consequences
- Right-click menus carry nothing from this extension, whether the package has loaded or not.
- Opening a scene is a double-click, as for XAML, and the JSON is one tab away with the JSON editor's own features.
- "Open With" lists the Scene Editor for every `.json`; choosing it for a non-scene file reports that the format is not supported.
- Keyboard focus follows ADR-0041: switching tabs happens only on a click (or UI Automation's Toggle) and never steals focus.

## Verification
Tracked in [M0094](../Milestones/M0094-Scene-Editor-Document.md).
